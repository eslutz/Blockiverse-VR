using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DefaultExecutionOrder(-9000)]
    public sealed class CreativeWorldManager : MonoBehaviour, IMultiplayerWorldContext
    {
        [SerializeField] string textureSet = BlockTextureSetIds.Default;
        [SerializeField] bool initializeDefaultWorldOnAwake;
        // The presentation half of the world root, when this process has one. Resolved from this
        // GameObject's components rather than serialized, so the generated scene carries no extra
        // field and there is no stale reference to keep in sync.
        //
        // Held as a MonoBehaviour as well as an interface because an interface-typed reference does
        // NOT get Unity's lifetime-aware `==` overload: a destroyed presentation compares non-null
        // through IWorldPresentation, so every guard would call into a dead object. Null-check the
        // MonoBehaviour, never the interface.
        MonoBehaviour presentationBehaviour;
        IWorldPresentation presentation;
        bool presentationResolved;
        MultiplayerChunkAuthoritySync authoritySync;
        WeatherService weatherService;
        VegetationService vegetationService;
        FarmingService farmingService;
        FluidFlowService fluidFlowService;
        // Biome lookups for this world, built from the same seed and world height the terrain was
        // generated with (so it reproduces the generated biomes exactly). Null for presets that
        // have no biomes (flat creative, void builder) and before the world is configured; every
        // read goes through BiomeIndexAt, which degrades to AnyBiomeIndex.
        SurvivalBiomeResolver biomeResolver;
        WorldTimeClock worldTimeClock;
        // The world instance whose BlockChanged event we are currently subscribed to. Tracked
        // separately from `World` so re-configuration unsubscribes from the right instance.
        VoxelWorld subscribedWorld;
        // Environment sync values received before the services existed (message-ordering safety net).
        WeatherSyncState? pendingWeatherSync;
        long? pendingWorldTimeTicks;
        // Per-block container contents (structure loot crates). Built from generation loot, then
        // overridden by any saved contents on load.
        ContainerInventoryStore containerStore;
        ItemRegistry containerItemRegistry;
        IReadOnlyList<StructureContainerLoot> pendingContainerLoot;
        // The inventory that receives container loot when a crate is broken (the active player's
        // survival inventory). When a save is being applied, auto-loot is suppressed so loaded block
        // deltas that remove crates don't dump loot into the player.
        Inventory activePlayerInventory;
        bool suppressContainerAutoLoot;

        public BlockRegistry Registry { get; private set; }
        public WorldGenerationSettings Settings { get; private set; }
        public CreativeWorldGenerationPreset GenerationPreset { get; private set; }
        public VoxelWorld World { get; private set; }
        // Sky occlusion for this world. Owned by the simulation, not the renderer, because crop
        // growth and cave detection read it and must work with no renderer in the process.
        public VoxelSkyLightMap SkyLight { get; private set; }

        // Null on a dedicated server, and null once the presentation object has been destroyed.
        // The Unity lifetime check lives here so callers can safely use `?.` on the result.
        public IWorldPresentation Presentation
        {
            get
            {
                if (!presentationResolved)
                    ResolvePresentation();

                return presentationBehaviour != null ? presentation : null;
            }
        }
        IVoxelWorldRenderer IMultiplayerWorldContext.Renderer => Presentation;
        public string TextureSet => BlockiverseTextureSelection.NormalizeToken(textureSet);
        public BlockPosition SpawnPosition => Settings != null ? Settings.SpawnPosition : new BlockPosition(0, 64, 0);

        // The world's rules mode. Explicitly initialized sandbox worlds default to Creative; saves
        // and the new-world flow set it from their manifest/config (see SetGameMode/ParseGameMode).
        public WorldGameMode GameMode { get; private set; } = WorldGameMode.Creative;
        public bool InitializeDefaultWorldOnAwake
        {
            get => initializeDefaultWorldOnAwake;
            set => initializeDefaultWorldOnAwake = value;
        }

        public void SetGameMode(WorldGameMode mode) => GameMode = mode;
        // Takes a texture TOKEN: a built-in set id or `pack:<id>`. Local to this peer and never
        // transmitted -- see IMultiplayerWorldContext.SetTextureSet.
        public void SetTextureSet(string textureSetId) => textureSet = BlockiverseTextureSelection.NormalizeToken(textureSetId);

        public static WorldGameMode ParseGameMode(string gameMode) =>
            string.Equals(gameMode, "creative", StringComparison.OrdinalIgnoreCase)
                ? WorldGameMode.Creative
                : WorldGameMode.Survival;

        // The canonical manifest string for the current mode.
        public string GameModeString => GameMode == WorldGameMode.Creative ? "creative" : "survival";

        public string CurrentWeatherState => weatherService?.CurrentState.ToString();
        public int CurrentWeatherTicksInState => weatherService?.TicksInCurrentState ?? 0;
        public WorldTimeClock WorldTimeClock => worldTimeClock;

        // Evaluates the current environment (temperature, fog, precipitation kind and intensity,
        // storm, cloud coverage) at the given altitude but WITHOUT a horizontal position, so the
        // biome is unknown and temperature falls back to the temperate (Meadow) base. This is the
        // global sky query the lighting controller uses; anything tied to where the player actually
        // stands should use a position-aware overload instead. Returns false until the weather
        // service exists.
        public bool TryEvaluateEnvironment(int altitudeY, out EnvironmentState environment) =>
            TryEvaluateEnvironmentAt(altitudeY, SurvivalBiomeResolver.AnyBiomeIndex, out environment);

        // Position-aware evaluation: resolves the column's biome so the temperature — and therefore
        // the rain/snow decision derived from it — is correct where the player actually is.
        public bool TryEvaluateEnvironment(BlockPosition position, out EnvironmentState environment) =>
            TryEvaluateEnvironmentAt(position.Y, BiomeIndexAt(position.X, position.Z), out environment);

        /// <summary>Whether the active weather is reaching this world position as SNOW.
        ///
        /// Returns a bool rather than an EnvironmentState on purpose: the caller is the creative
        /// tools UI, and Blockiverse.UI does not reference Blockiverse.WorldGen. Handing back the
        /// state would drag PrecipitationKind and EnvironmentState across an assembly boundary the
        /// layering deliberately keeps closed.</summary>
        public bool IsPrecipitationSnowAt(Vector3 worldPosition)
        {
            var cell = new BlockPosition(
                Mathf.FloorToInt(worldPosition.x),
                Mathf.FloorToInt(worldPosition.y),
                Mathf.FloorToInt(worldPosition.z));

            if (!TryEvaluateEnvironment(cell, out EnvironmentState environment))
                return false;

            return environment.Precipitation == PrecipitationKind.Snow;
        }

        bool TryEvaluateEnvironmentAt(int altitudeY, int biomeIndex, out EnvironmentState environment)
        {
            if (weatherService == null)
            {
                environment = default;
                return false;
            }

            float normalizedTime = worldTimeClock != null ? worldTimeClock.NormalizedTime : 0.25f;
            environment = weatherService.Evaluate(normalizedTime, altitudeY, biomeIndex);
            return true;
        }

        // Biome index for a world column, or SurvivalBiomeResolver.AnyBiomeIndex when this world
        // has no biomes (flat creative / void builder presets) or is not configured yet. Pure seed
        // math, so host and clients resolve identical biomes without any extra sync traffic.
        // Public since 2026-08-25 for the gameplay debug readout, which reports the biome the
        // player is standing in. Safe to expose: it is pure seed math with no state, which is the
        // same property that lets host and clients resolve identical biomes with no sync traffic.
        public int BiomeIndexAt(int worldX, int worldZ) =>
            biomeResolver != null
                ? biomeResolver.BiomeIndexAt(worldX, worldZ)
                : SurvivalBiomeResolver.AnyBiomeIndex;

        // Whether a head/world position sits underground (no sky access above its cell), the O(1)
        // sky-map answer the ambience and music presentation layers share so they agree on when
        // the player is in a cave. False when there is no sky map or the cell is out of bounds.
        public bool IsHeadUnderground(Vector3 headWorldPosition)
        {
            if (SkyLight == null || World == null)
                return false;

            BlockPosition cell = VoxelWorldCoordinates.ToBlockPosition(headWorldPosition);
            return World.Bounds.Contains(cell) && !SkyLight.HasSkyAccess(cell);
        }

        // Whether a world position sits inside a fluid cell, and which family. The shared answer
        // for every "is this point in water" consumer -- the underwater view, vitals, and anything
        // that follows -- so they cannot drift apart. False when there is no world yet or the cell
        // is out of bounds, both of which are real states (before the first world is generated,
        // and above WorldMaxY).
        public bool TryGetFluidFamilyAt(Vector3 worldPosition, out FluidFamily family)
        {
            family = default;

            // Read live, never cached: New World and Load replace the VoxelWorld instance whole.
            VoxelWorld world = World;
            if (world == null)
                return false;

            BlockPosition cell = VoxelWorldCoordinates.ToBlockPosition(worldPosition);
            if (!world.Bounds.Contains(cell))
                return false;

            return FluidBlocks.TryGetFamily(world.GetBlock(cell), out family);
        }

        // Returns the weather state, accumulated ticks, and RNG position for a network snapshot.
        // Returns a Clear default when the weather service is not yet initialized.
        public WeatherSyncState GetWeatherSyncState() =>
            weatherService != null
                ? new WeatherSyncState(weatherService.CurrentState, weatherService.TicksInCurrentState, weatherService.RngState)
                : new WeatherSyncState(WeatherState.Clear, 0, 1u);

        // Restores weather state (incl. RNG) received from a host snapshot, preserving lockstep.
        // If the service does not exist yet, or if a network caller knows the host world is about
        // to replace this world, the full state is buffered and applied at the end of
        // ConfigureEnvironmentServices — no ticks/RNG are lost across message ordering.
        public void RestoreWeatherSyncState(WeatherSyncState sync, bool preserveForNextWorldInitialization = false)
        {
            if (preserveForNextWorldInitialization)
                pendingWeatherSync = sync;

            if (weatherService == null)
            {
                pendingWeatherSync = sync;
                return;
            }

            weatherService.RestoreState(sync.State, sync.Ticks, sync.RngState);
        }

        // Creative env control: forces a weather state immediately (offline/host worlds — clients
        // mirror the host's weather via the environment sync, never set it locally). The RNG
        // position is preserved so the machine's future transitions stay on its timeline.
        public void SetWeather(WeatherState state)
        {
            if (weatherService == null)
                return;

            weatherService.RestoreState(state, ticks: 0, GetWeatherSyncState().RngState);
        }

        // ── Creative spawn helpers (A2 UI decoupling) ─────────────────────────
        // These wrap Blockiverse.WorldGen services so Blockiverse.UI can invoke them through the
        // Gameplay manager it already references, instead of taking a direct WorldGen asmdef ref.

        // Places a procedurally-built standard tree at the given base position (tracked as an edit).
        public void SpawnStandardTree(VoxelWorld world, BlockPosition basePos)
        {
            if (world == null)
                return;

            new VegetationService().PlaceCrownbranchTree(world, basePos, trackChange: true);
        }

        // Places a seeded structure at the given base position (tracked as an edit).
        public void SpawnStructure(VoxelWorld world, BlockPosition basePos)
        {
            if (world == null)
                return;

            StructureService.PlaceStructureAt(world, basePos.X, basePos.Y, basePos.Z, world.Seed, trackChange: true);
        }

        // Returns the highest solid surface Y at the given column, or a negative value if none.
        public int FindSurfaceY(VoxelWorld world, int x, int z)
        {
            return StructureService.FindSurfaceY(world, x, z);
        }

        // Restores the world-time clock from a host snapshot, buffering if the clock is not ready.
        public void RestoreWorldTimeTicks(long totalElapsedTicks)
        {
            if (worldTimeClock == null)
            {
                pendingWorldTimeTicks = totalElapsedTicks;
                return;
            }
            worldTimeClock.RestoreElapsedTicks(totalElapsedTicks);

            // When the clock is restored AFTER the fluid sim was configured (save load applies
            // the world first, then restores time), resume the sim from the restored tick — the
            // loaded world is already the post-tick state, so the next Tick must not replay every
            // elapsed tick. (When the restore arrives first it is buffered into
            // pendingWorldTimeTicks and the sim is Configured at the restored tick directly.)
            fluidFlowService?.SyncToWorldTick(totalElapsedTicks);
        }

        // ── World-simulation persistence ─────────────────────────────────────

        // Fills the world-owned portion of the save extras: the full weather-machine position
        // plus the vegetation/farming simulation queues. Player state and stations belong to
        // other components and are appended by the caller.
        public void FillSaveExtras(WorldSaveExtras extras)
        {
            if (extras == null)
                throw new ArgumentNullException(nameof(extras));

            WeatherSyncState weather = GetWeatherSyncState();
            extras.WeatherTicksInState = weather.Ticks;
            extras.WeatherRngState = weather.RngState;

            if (vegetationService != null)
            {
                IReadOnlyList<(BlockPosition position, int accumulatedTicks)> saplings =
                    vegetationService.ExportSaplingProgress();
                var saplingsOut = new VxlwSaplingProgress[saplings.Count];
                for (int i = 0; i < saplings.Count; i++)
                {
                    (BlockPosition position, int accumulatedTicks) = saplings[i];
                    saplingsOut[i] = new VxlwSaplingProgress
                    {
                        X = position.X, Y = position.Y, Z = position.Z,
                        AccumulatedTicks = accumulatedTicks
                    };
                }
                extras.Saplings = saplingsOut;

                IReadOnlyList<VegetationService.WildRegrowthMarker> wild = vegetationService.ExportWildRegrowth();
                var wildOut = new List<VxlwWildRegrowthMarker>(wild.Count);
                foreach (VegetationService.WildRegrowthMarker marker in wild)
                {
                    if (Registry == null || !Registry.TryGet(marker.BlockId, out BlockDefinition def))
                        continue;

                    wildOut.Add(new VxlwWildRegrowthMarker
                    {
                        CanonicalId = def.CanonicalId,
                        X = marker.Position.X, Y = marker.Position.Y, Z = marker.Position.Z,
                        RegrowAfterTick = marker.RegrowAfterTick,
                        AttemptsLeft = marker.AttemptsLeft
                    });
                }
                extras.WildRegrowth = wildOut.ToArray();
            }

            if (farmingService != null)
            {
                IReadOnlyList<(BlockPosition position, int accumulatedTicks)> regrowth =
                    farmingService.ExportBerrybushRegrowth();
                var regrowthOut = new VxlwBerrybushRegrowth[regrowth.Count];
                for (int i = 0; i < regrowth.Count; i++)
                {
                    (BlockPosition position, int accumulatedTicks) = regrowth[i];
                    regrowthOut[i] = new VxlwBerrybushRegrowth
                    {
                        X = position.X, Y = position.Y, Z = position.Z,
                        AccumulatedTicks = accumulatedTicks
                    };
                }
                extras.BerrybushRegrowth = regrowthOut;
            }
        }

        // Restores the world-owned simulation state from a loaded save: weather machine position
        // (state + ticks + RNG) and the vegetation/farming queues. Call after the world has been
        // initialized and saved block deltas applied — the queues validate against world blocks.
        public void RestoreSimulationState(WorldSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            WeatherState weatherState = WeatherState.Clear;
            if (!string.IsNullOrEmpty(data.WeatherState))
                Enum.TryParse(data.WeatherState, ignoreCase: true, out weatherState);
            RestoreWeatherSyncState(new WeatherSyncState(weatherState, data.WeatherTicksInState, data.WeatherRngState));

            if (vegetationService != null)
            {
                if (data.Saplings != null)
                {
                    var saplings = new List<(BlockPosition, int)>(data.Saplings.Length);
                    foreach (VxlwSaplingProgress entry in data.Saplings)
                        saplings.Add((new BlockPosition(entry.X, entry.Y, entry.Z), entry.AccumulatedTicks));
                    vegetationService.RestoreSaplingProgress(saplings);
                }

                if (data.WildRegrowth != null)
                {
                    var markers = new List<VegetationService.WildRegrowthMarker>(data.WildRegrowth.Length);
                    foreach (VxlwWildRegrowthMarker entry in data.WildRegrowth)
                    {
                        // Markers whose canonical block no longer resolves are dropped (unreleased:
                        // no legacy fallbacks).
                        if (Registry == null || !Registry.TryGetByCanonicalId(entry.CanonicalId, out BlockDefinition def))
                            continue;

                        markers.Add(new VegetationService.WildRegrowthMarker(
                            def.Id,
                            new BlockPosition(entry.X, entry.Y, entry.Z),
                            entry.RegrowAfterTick,
                            entry.AttemptsLeft));
                    }
                    vegetationService.RestoreWildRegrowth(markers);
                }
            }

            if (farmingService != null && data.BerrybushRegrowth != null)
            {
                var regrowth = new List<(BlockPosition, int)>(data.BerrybushRegrowth.Length);
                foreach (VxlwBerrybushRegrowth entry in data.BerrybushRegrowth)
                    regrowth.Add((new BlockPosition(entry.X, entry.Y, entry.Z), entry.AccumulatedTicks));
                farmingService.RestoreBerrybushRegrowth(regrowth);
            }
        }

        public void InitializeDefaultWorld()
        {
            InitializeGeneratedWorld(CreateDefaultGeneratedWorld());
        }

        public void InitializeGeneratedWorld(
            BlockRegistry registry,
            WorldGenerationSettings settings,
            VoxelWorld world,
            CreativeWorldGenerationPreset generationPreset,
            IReadOnlyList<StructureContainerLoot> containerLoot = null)
        {
            InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, generationPreset, containerLoot));
        }

        public void InitializeGeneratedWorld(
            GeneratedCreativeWorld generatedWorld,
            MultiplayerChunkAuthoritySync authoritySyncOverride = null,
            bool deferInitialRendererRebuild = false)
        {
            if (generatedWorld.Registry == null)
                throw new ArgumentException("Generated world requires a block registry.", nameof(generatedWorld));
            if (generatedWorld.Settings == null)
                throw new ArgumentException("Generated world requires generation settings.", nameof(generatedWorld));
            if (generatedWorld.World == null)
                throw new ArgumentException("Generated world requires voxel data.", nameof(generatedWorld));

            Registry = generatedWorld.Registry;
            WorldGenerationSettings settings = generatedWorld.Settings;
            Settings = settings;
            GenerationPreset = generatedWorld.GenerationPreset;
            World = generatedWorld.World;
            pendingContainerLoot = generatedWorld.ContainerLoot;
            pendingWorldTimeTicks = 0;
            ConfigureWorldRuntime(settings, authoritySyncOverride, deferInitialRendererRebuild);

            // ConfigureWorldRuntime queues the full world rebuild; eagerly bake the spawn
            // neighbourhood so the rig lands on visible, collidable ground before it is positioned.
            if (settings != null)
                Presentation?.RebuildSpawnRegion(settings.SpawnPosition);

            Presentation?.PositionRigAtSpawn(settings.SpawnPosition);
        }

        public void ConfigureAuthoritySync(MultiplayerChunkAuthoritySync sync)
        {
            if (authoritySync == sync)
                return;

            authoritySync = sync;

            if (World != null && Registry != null)
                Presentation?.ConfigureAuthority(sync);
        }

        void ConfigureWorldRuntime(
            WorldGenerationSettings settings,
            MultiplayerChunkAuthoritySync authoritySyncOverride = null,
            bool deferInitialRendererRebuild = false)
        {
            if (World == null)
                throw new InvalidOperationException("Creative world runtime requires voxel data.");

            // Built before the presentation is configured so the renderer shares this instance
            // rather than constructing a second map that could drift from the simulation's.
            SkyLight = new VoxelSkyLightMap(World, Registry);

            ResolvePresentation();

            if (authoritySyncOverride != null)
                authoritySync = authoritySyncOverride;

            // Presentation first: it ensures the scene sun, which is what currently owns the
            // WorldTimeClock that ConfigureEnvironmentServices looks for.
            Presentation?.ConfigureForWorld(
                World,
                Registry,
                SkyLight,
                settings,
                TextureSet,
                authoritySync,
                deferInitialRendererRebuild);

            ConfigureEnvironmentServices(settings);
        }

        // Finds the presentation component on this GameObject, if the build has one at all.
        // Always rescans: a world root built in code may gain its presentation after the manager.
        void ResolvePresentation()
        {
            presentationResolved = true;

            foreach (Component component in GetComponents<Component>())
            {
                if (component is not IWorldPresentation candidate)
                    continue;

                presentation = candidate;
                presentationBehaviour = component as MonoBehaviour;
                return;
            }

            presentation = null;
            presentationBehaviour = null;
        }

        void ConfigureEnvironmentServices(WorldGenerationSettings settings)
        {
            if (worldTimeClock != null)
                worldTimeClock.Ticked -= OnWorldTick;

            // The flow sim is bound to the world it was configured on; drop it now so a stale
            // instance never reacts to the replacement world's edits (it is recreated at the end
            // of this method once the clock is known).
            fluidFlowService = null;

            // Same reasoning, and cleared here rather than beside its rebuild below because the
            // rebuild sits under the `worldTimeClock == null` early return: reconfiguring into a
            // different world in a clock-less scene would otherwise keep answering biome queries
            // from the previous world's seed and height. SurvivalBiomeResolver.SurfaceHeight does
            // no bounds checking, so that failure is silently plausible rather than loud.
            biomeResolver = null;

            // Unsubscribe from the world we actually subscribed to — `World` may already point at a
            // replacement (e.g. a multiplayer regeneration), and unsubscribing from the new instance
            // would leak the old world's handler.
            if (subscribedWorld != null)
            {
                subscribedWorld.BlockChanged -= OnBlockChanged;
                subscribedWorld = null;
            }

            // Build container contents (structure loot crates) from generation loot. Done before the
            // WorldTimeClock check so containers exist even in scenes/tests without a clock.
            BuildContainerStore();

            // Subscribe block-change tracking before the clock gate: container auto-loot and
            // sapling/crop tracking must work even in scenes without a WorldTimeClock.
            if (World != null)
            {
                World.BlockChanged += OnBlockChanged;
                subscribedWorld = World;
            }

            worldTimeClock = FindFirstObjectByType<WorldTimeClock>();
            if (worldTimeClock == null)
                return;

            uint seed = settings != null ? (uint)settings.Seed : 1u;
            weatherService    = new WeatherService(seed);
            vegetationService = new VegetationService();
            farmingService    = new FarmingService();

            // Crop growth rolls must be a pure function of synced state (world seed + world clock):
            // environmental mutations are never broadcast, so host and clients simulate in lockstep.
            farmingService.ConfigureDeterministicGrowth(settings != null ? settings.Seed : World.Seed);

            // Wire biome-aware sapling growth for survival terrain worlds, and keep the resolver for
            // biome-aware environment queries (§6.1 base temperatures). The resolver is a pure
            // function of (seed, worldHeight), so host and late-joining clients (which receive the
            // seed in the generation snapshot) resolve identical biomes and stay in growth lockstep.
            // Only the survival terrain preset has biomes; flat/void worlds keep the null resolver
            // cleared at the top of this method and fall back to the temperate default.
            if (settings != null && World != null && GenerationPreset == CreativeWorldGenerationPreset.SurvivalLite)
            {
                biomeResolver = new SurvivalBiomeResolver(settings.Seed, World.Bounds.Height);
                // The seed rides along because Windbranch bends downwind and the wind is
                // seed-derived: a sapling maturing on a highland ridge has to bend the same way as
                // the wild trees generated around it.
                vegetationService.Configure(biomeResolver.BiomeIndexAt, settings.Seed);
            }

            vegetationService.ScanAndTrackSaplings(World);
            farmingService.ScanAndTrackCrops(World);
            worldTimeClock.Ticked += OnWorldTick;

            // Apply any environment state received before the services existed (message ordering).
            if (pendingWeatherSync.HasValue)
            {
                weatherService.RestoreState(pendingWeatherSync.Value.State, pendingWeatherSync.Value.Ticks, pendingWeatherSync.Value.RngState);
                pendingWeatherSync = null;
            }

            if (pendingWorldTimeTicks.HasValue)
            {
                worldTimeClock.RestoreElapsedTicks(pendingWorldTimeTicks.Value);
                pendingWorldTimeTicks = null;
            }

            // Configured after any pending clock restore so the flow phase aligns with the
            // synced absolute tick — late joiners then step fluids at the same world ticks
            // as the host.
            fluidFlowService = new FluidFlowService();
            fluidFlowService.Configure(World, settings != null ? settings.Seed : World.Seed, CurrentWorldTick);
        }

        void OnBlockChanged(BlockChange change)
        {
            // The renderer's rebuild queue applies edits to the sky map whenever a presentation
            // exists. With none — a dedicated server — nothing else would, and crop growth reads
            // it. Applied ONLY in that case: a double apply would hand the rebuild queue a wrong
            // previousTop/newTop verdict and silently under-invalidate lighting.
            if (SkyLight != null && Presentation == null)
                SkyLight.ApplyChange(change, out _, out _);

            BlockId b = change.NewBlock;

            // Fluid simulation reacts to every edit: placed/removed fluids and new openings
            // activate the affected cells (a no-op for changes far from any fluid).
            fluidFlowService?.OnBlockChanged(World, change);

            if (b == BlockRegistry.Sapling || b == BlockRegistry.Sapling_S1 || b == BlockRegistry.Sapling_S2)
                vegetationService?.TrackSapling(change.Position);
            // Only a crop NEWLY appearing at a position re-anchors growth (planting/replanting).
            // A crop→crop change is FarmingService's own stage advance; re-anchoring there would
            // reset the interval anchor and silently skip the next growth roll after each advance.
            if (FarmingService.IsCropBlock(b) && !FarmingService.IsCropBlock(change.PreviousBlock))
                farmingService?.TrackCrop(change.Position);

            // Keep the leaf-decay candidate set current: newly placed Leafmoss must be checked,
            // and removing a log may orphan the leaves around it.
            if (b == BlockRegistry.Leafmoss)
                vegetationService?.MarkLeafDecayCandidate(change.Position);
            if (VegetationService.IsLeafSupportBlock(change.PreviousBlock) && !VegetationService.IsLeafSupportBlock(b))
                vegetationService?.MarkLeafDecayCandidates(World, change.Position);

            // Harvesting a berrybush (cleared to air) queues it to regrow after two game days (§3).
            // Berrybush is owned by FarmingService (it replants a fresh stage-0 bush and tracks its
            // growth); the wild-regrowth queue below handles the other wild plants so the two paths
            // never both fire for the same block.
            if (b == BlockRegistry.Air && FarmingService.IsBerrybushStage(change.PreviousBlock))
                farmingService?.OnBlockHarvested(change.PreviousBlock, change.Position);
            else if (b == BlockRegistry.Air && IsWildRegrowthPlant(change.PreviousBlock))
                vegetationService?.MarkWildHarvest(change.PreviousBlock, change.Position, CurrentWorldTick);

            // A container block that is removed (broken, or replaced by a loaded save delta): deposit
            // its contents into the active player inventory (best effort) then drop the store entry so
            // the store stays consistent with the world. Auto-loot is skipped while applying a save,
            // and only the world-owning peer (offline/host) may grant loot — on clients this handler
            // also fires for replicated deltas, where granting locally would duplicate items that the
            // host has already attributed to the breaking player (see ProcessHostHarvest).
            if (IsContainerBlock(change.PreviousBlock) && !IsContainerBlock(b) && containerStore != null)
            {
                bool ownsWorld = authoritySync == null || authoritySync.CurrentBoundary.CanCommitMutations;
                Inventory lootDestination = ActivePlayerInventory;
                if (ownsWorld && !suppressContainerAutoLoot && lootDestination != null)
                    TryLootContainerInto(change.Position, lootDestination);
                containerStore.Remove(change.Position);
            }
        }

        // Wild (non-cultivated) plants that the vegetation service restores after a regrow delay.
        // Berrybush is intentionally excluded — FarmingService owns its regrowth.
        static bool IsWildRegrowthPlant(BlockId block) =>
            block == BlockRegistry.GrainStalk || block == BlockRegistry.Reedgrass || block == BlockRegistry.Thornbrush;

        // Blocks that carry per-position container contents.
        static bool IsContainerBlock(BlockId block) =>
            block == BlockRegistry.StorageCrate ||
            block == BlockRegistry.ReedBasket ||
            block == BlockRegistry.ToolRack ||
            block == BlockRegistry.PantryJar ||
            block == BlockRegistry.DeepLocker;

        long CurrentWorldTick => worldTimeClock != null ? worldTimeClock.TotalElapsedTicks : 0L;

        // ── Container contents (structure loot crates) ───────────────────────

        ItemRegistry ContainerItemRegistry => containerItemRegistry ??= ItemRegistry.Default;

        void BuildContainerStore()
        {
            containerStore = new ContainerInventoryStore(ContainerItemRegistry);

            if (pendingContainerLoot != null)
            {
                foreach (StructureContainerLoot loot in pendingContainerLoot)
                {
                    if (loot?.Items == null)
                        continue;
                    var stacks = new List<(string, int)>(loot.Items.Count);
                    foreach (ContainerLootItem item in loot.Items)
                        stacks.Add((item.ItemId, item.Count));
                    containerStore.Populate(loot.Position, stacks);
                }
            }

            pendingContainerLoot = null;
        }

        // The container contents store (structure loot crates). May be null before a world is loaded.
        public ContainerInventoryStore ContainerStore => containerStore;
        public ContainerInventoryStore GetOrCreateContainerStore()
        {
            containerStore ??= new ContainerInventoryStore(ContainerItemRegistry);
            return containerStore;
        }

        // The inventory that receives loot when a player breaks a container. Set by the survival
        // runtime (the active player's inventory). Null disables auto-loot.
        // An explicitly set inventory wins; otherwise the local player's survival inventory is
        // resolved on demand.
        //
        // The explicit registration used to be made by SurvivalHudController.Bind, because the
        // survival HUD happened to hold the inventory reference. That put a gameplay rule —
        // breaking a crate fills the breaker's inventory — inside a menu component, where it was
        // the sole caller of SetActivePlayerInventory in the repository and would have vanished
        // silently with the uGUI menus, leaving containers to delete their contents. Resolving it
        // here makes the rule independent of which UI stack is present.
        //
        // Deliberately resolved per use rather than cached at bind time: LocalInventory is itself
        // computed from the local client id, and the old code had to re-push it from a
        // LocalInventoryChanged handler every time the sync swapped instances. Looking it up when
        // a container actually breaks has no such staleness to repair. The search is skipped
        // entirely once something sets the inventory explicitly, and only ever runs on a container
        // break, so the cost does not sit on any hot path.
        public Inventory ActivePlayerInventory =>
            activePlayerInventory ?? ResolveLocalSurvivalInventory();

        public void SetActivePlayerInventory(Inventory inventory) => activePlayerInventory = inventory;

        MultiplayerSurvivalSync survivalSyncForLoot;

        Inventory ResolveLocalSurvivalInventory()
        {
            // Cached once found; retried while absent, because the sync is spawned by Netcode and
            // may not exist yet the first time a container breaks in a session.
            if (survivalSyncForLoot == null)
                survivalSyncForLoot = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            return survivalSyncForLoot != null ? survivalSyncForLoot.LocalInventory : null;
        }

        // Persistence sets this while applying a save so loaded crate-removal deltas don't dump loot
        // into the player; cleared once the saved container store has been restored.
        public bool SuppressContainerAutoLoot
        {
            get => suppressContainerAutoLoot;
            set => suppressContainerAutoLoot = value;
        }

        // Moves all contents from the container at a position into the target inventory. Returns true
        // when the container was fully emptied. Safe to call on a position with no container. Used by
        // the break-to-loot path; a future container-open UI can reuse it or read ContainerStore.
        // Fires when a broken container's contents were granted to the active player (feedback
        // layers play the open/pickup cues from it).
        public event Action<BlockPosition> ContainerLooted;

        public bool TryLootContainerInto(BlockPosition position, Inventory target)
        {
            if (containerStore == null || target == null)
                return false;
            if (!containerStore.Contains(position))
                return false;

            bool looted = containerStore.TransferAllInto(position, target);
            if (looted)
                ContainerLooted?.Invoke(position);
            return looted;
        }

        public void NotifyContainerLooted(BlockPosition position)
        {
            ContainerLooted?.Invoke(position);
        }

        // Replaces the live container store with saved contents on load (saved state is authoritative
        // over regenerated loot, so emptied crates stay empty across reloads).
        public void RestoreContainerStore(IEnumerable<(BlockPosition position, IEnumerable<(string itemId, int count, int durability)> items)> savedContainers)
        {
            containerStore = new ContainerInventoryStore(ContainerItemRegistry);
            if (savedContainers == null)
                return;

            foreach ((BlockPosition position, IEnumerable<(string itemId, int count, int durability)> items) in savedContainers)
                containerStore.Populate(position, items);
        }

        void OnWorldTick(int ticks)
        {
            weatherService?.Tick(ticks);
            if (World != null)
            {
                vegetationService?.TickLeafDecay(World, ticks);
                vegetationService?.TickSapling(World, ticks);
                vegetationService?.TickWildRegrowth(World, CurrentWorldTick);
                farmingService?.TickGrowth(World, CurrentWorldTick, ResolveCropGrowthConditions);
                farmingService?.TickRegrowth(World, ticks);
                fluidFlowService?.Tick(World, CurrentWorldTick);

                // World-sim mutations only mark chunks dirty; repaint them here so growth and
                // flow are visible without waiting for a player edit to trigger a rebuild.
                Presentation?.RebuildDirty();
            }
        }

        CropGrowthConditions ResolveCropGrowthConditions(BlockPosition cropPosition)
        {
            if (World == null)
                return CropGrowthConditions.Favorable;

            BlockRegistry registry = Registry ?? BlockRegistry.Default;
            float sampledLight = VoxelLightSampler.SampleAirLight(World, registry, cropPosition, skyLight: SkyLight);
            int lightLevel = Mathf.RoundToInt(Mathf.Clamp01(sampledLight) * 15.0f);

            var soilPosition = new BlockPosition(cropPosition.X, cropPosition.Y - 1, cropPosition.Z);
            bool soilMoist = World.Bounds.Contains(soilPosition) &&
                             FarmingService.HasFreshwaterNearby(World, soilPosition);
            return new CropGrowthConditions(lightLevel, soilMoist);
        }

        void OnDestroy()
        {
            if (worldTimeClock != null)
                worldTimeClock.Ticked -= OnWorldTick;
            if (subscribedWorld != null)
            {
                subscribedWorld.BlockChanged -= OnBlockChanged;
                subscribedWorld = null;
            }
        }

        public static GeneratedCreativeWorld CreateDefaultGeneratedWorld(int seed = 6401)
        {
            return WorldSaveGeneration.GenerateDefaultWorld(seed);
        }

        void Awake()
        {
            if (initializeDefaultWorldOnAwake && World == null)
                InitializeDefaultWorld();
        }
    }
}
