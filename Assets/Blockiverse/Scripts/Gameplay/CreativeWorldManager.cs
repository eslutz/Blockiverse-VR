using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum CreativeWorldGenerationPreset
    {
        MenuWorld,
        SurvivalLite,
        FlatCreative,
        VoidBuilder
    }

    // World-level rules mode (the manifest's "gameMode"): survival worlds accept edits only
    // through the validated survival command channel; creative worlds allow direct mutations.
    public enum WorldGameMode
    {
        Creative,
        Survival,
    }

    public readonly struct GeneratedCreativeWorld
    {
        public GeneratedCreativeWorld(
            BlockRegistry registry,
            WorldGenerationSettings settings,
            VoxelWorld world,
            CreativeWorldGenerationPreset generationPreset,
            IReadOnlyList<StructureContainerLoot> containerLoot = null)
        {
            Registry = registry;
            Settings = settings;
            World = world;
            GenerationPreset = generationPreset;
            ContainerLoot = containerLoot;
        }

        public BlockRegistry Registry { get; }
        public WorldGenerationSettings Settings { get; }
        public VoxelWorld World { get; }
        public CreativeWorldGenerationPreset GenerationPreset { get; }
        // Container loot rolled during generation (null when the preset places none).
        public IReadOnlyList<StructureContainerLoot> ContainerLoot { get; }
    }

    public sealed class CreativeWorldManager : MonoBehaviour
    {
        [SerializeField] Material chunkMaterial;
        [SerializeField] string textureSet = BlockTextureSetIds.Default;
        [SerializeField] string[] blockTextureSetIds;
        [SerializeField] Texture2D[] blockTextureSetAtlases;
        [SerializeField] int interactionLayer = -1;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] PlacementPreview placementPreview;
        [SerializeField] BlockiverseVoidSafetyFloor voidSafetyFloor;
        [SerializeField] bool initializeDefaultWorldOnAwake;
        MultiplayerChunkAuthoritySync authoritySync;
        TorchbudLightManager torchbudLightManager;
        WeatherService weatherService;
        VegetationService vegetationService;
        FarmingService farmingService;
        FluidFlowService fluidFlowService;
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
        public VoxelWorldRenderer Renderer { get; private set; }
        public string TextureSet => BlockTextureSetIds.Normalize(textureSet);
        public bool IsMenuWorldActive { get; private set; }

        // The world's rules mode. Explicitly initialized sandbox worlds default to Creative; saves
        // and the new-world flow set it from their manifest/config (see SetGameMode/ParseGameMode).
        public WorldGameMode GameMode { get; private set; } = WorldGameMode.Creative;
        public bool InitializeDefaultWorldOnAwake
        {
            get => initializeDefaultWorldOnAwake;
            set => initializeDefaultWorldOnAwake = value;
        }

        public void SetGameMode(WorldGameMode mode) => GameMode = mode;
        public void SetTextureSet(string textureSetId) => textureSet = BlockTextureSetIds.Normalize(textureSetId);

        public void ConfigureBlockTextureAtlases(string[] textureSetIds, Texture2D[] atlasTextures)
        {
            blockTextureSetIds = textureSetIds ?? Array.Empty<string>();
            blockTextureSetAtlases = atlasTextures ?? Array.Empty<Texture2D>();
        }

        public static WorldGameMode ParseGameMode(string gameMode) =>
            string.Equals(gameMode, "creative", StringComparison.OrdinalIgnoreCase)
                ? WorldGameMode.Creative
                : WorldGameMode.Survival;

        // The canonical manifest string for the current mode.
        public string GameModeString => GameMode == WorldGameMode.Creative ? "creative" : "survival";

        public string CurrentWeatherState => weatherService?.CurrentState.ToString();
        public int CurrentWeatherTicksInState => weatherService?.TicksInCurrentState ?? 0;
        public WorldTimeClock WorldTimeClock => worldTimeClock;

        // Evaluates the current environment (weather-derived temperature, fog, precipitation, storm,
        // cloud coverage) at the given altitude. Returns false until the weather service exists.
        // Lets runtime systems (lighting, fog, future VFX/audio) react to live weather.
        public bool TryEvaluateEnvironment(int altitudeY, out EnvironmentState environment)
        {
            if (weatherService == null)
            {
                environment = default;
                return false;
            }

            float normalizedTime = worldTimeClock != null ? worldTimeClock.NormalizedTime : 0.25f;
            environment = weatherService.Evaluate(normalizedTime, altitudeY);
            return true;
        }

        // Whether a head/world position sits underground (no sky access above its cell), the O(1)
        // sky-map answer the ambience and music presentation layers share so they agree on when
        // the player is in a cave. False when there is no sky map or the cell is out of bounds.
        public bool IsHeadUnderground(Vector3 headWorldPosition)
        {
            VoxelSkyLightMap skyLight = Renderer != null ? Renderer.SkyLight : null;
            if (skyLight == null || World == null)
                return false;

            BlockPosition cell = CreativeInteractionController.ToBlockPosition(headWorldPosition);
            return World.Bounds.Contains(cell) && !skyLight.HasSkyAccess(cell);
        }

        // Full weather snapshot: state + tick accumulator + RNG position. The RNG position is
        // what keeps a late-joining client in deterministic lockstep with the host's weather.
        public readonly struct WeatherSyncState
        {
            public readonly WeatherState State;
            public readonly int Ticks;
            public readonly uint RngState;

            public WeatherSyncState(WeatherState state, int ticks, uint rngState)
            {
                State = state;
                Ticks = ticks;
                RngState = rngState;
            }
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

        public void Configure(
            Material material,
            int layer,
            CreativeInteractionController controller = null,
            CreativeHotbar creativeHotbar = null,
            PlacementPreview preview = null)
        {
            chunkMaterial = material;
            interactionLayer = layer;
            interactionController = controller;
            hotbar = creativeHotbar;
            placementPreview = preview;
        }

        Texture2D ResolveSelectedBlockAtlas()
        {
            string selected = TextureSet;
            int count = Math.Min(blockTextureSetIds?.Length ?? 0, blockTextureSetAtlases?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(BlockTextureSetIds.Normalize(blockTextureSetIds[i]), selected, StringComparison.OrdinalIgnoreCase))
                    return blockTextureSetAtlases[i];
            }

            return null;
        }

        public void InitializeDefaultWorld()
        {
            InitializeGeneratedWorld(CreateDefaultGeneratedWorld());
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
            IsMenuWorldActive = generatedWorld.GenerationPreset == CreativeWorldGenerationPreset.MenuWorld;
            World = generatedWorld.World;
            pendingContainerLoot = generatedWorld.ContainerLoot;
            pendingWorldTimeTicks = 0;
            ConfigureWorldRuntime(settings, authoritySyncOverride, deferInitialRendererRebuild);
            interactionController?.SetBlockEditingEnabled(!IsMenuWorldActive);
            PositionRigAtSpawn(settings.SpawnPosition);
        }

        public void ConfigureAuthoritySync(MultiplayerChunkAuthoritySync sync)
        {
            if (authoritySync == sync)
                return;

            authoritySync = sync;

            if (World != null && Registry != null)
                ConfigureInteractionController(Settings);
        }

        void ConfigureWorldRuntime(
            WorldGenerationSettings settings,
            MultiplayerChunkAuthoritySync authoritySyncOverride = null,
            bool deferInitialRendererRebuild = false)
        {
            if (World == null)
                throw new InvalidOperationException("Creative world runtime requires voxel data.");

            BlockiverseLightingRuntime.EnsureSceneLighting();
            Renderer = GetComponent<VoxelWorldRenderer>();

            if (Renderer == null)
                Renderer = gameObject.AddComponent<VoxelWorldRenderer>();

            Renderer.Configure(
                World,
                Registry,
                chunkMaterial,
                interactionLayer,
                ResolveSelectedBlockAtlas(),
                TextureSet,
                deferInitialRendererRebuild);

            ConfigureTorchbudLights();
            ConfigureEnvironmentServices(settings);
            ConfigureVoidSafetyFloor();

            if (authoritySyncOverride != null)
                authoritySync = authoritySyncOverride;

            ConfigureInteractionController(settings);
        }

        public void InitializeMenuWorld()
        {
            InitializeGeneratedWorld(WorldSaveGeneration.GenerateMenuWorld());
            SetGameMode(WorldGameMode.Creative);
        }

        void ConfigureTorchbudLights()
        {
            if (torchbudLightManager == null)
                torchbudLightManager = GetComponent<TorchbudLightManager>();

            if (torchbudLightManager == null)
                torchbudLightManager = gameObject.AddComponent<TorchbudLightManager>();

            torchbudLightManager.Configure(World, Registry);
        }

        void ConfigureEnvironmentServices(WorldGenerationSettings settings)
        {
            if (worldTimeClock != null)
                worldTimeClock.Ticked -= OnWorldTick;

            // The flow sim is bound to the world it was configured on; drop it now so a stale
            // instance never reacts to the replacement world's edits (it is recreated at the end
            // of this method once the clock is known).
            fluidFlowService = null;

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

            worldTimeClock = GetComponent<WorldTimeClock>();
            if (worldTimeClock == null)
                worldTimeClock = FindAnyObjectByType<WorldTimeClock>();
            if (worldTimeClock == null)
                return;

            uint seed = settings != null ? (uint)settings.Seed : 1u;
            weatherService    = new WeatherService(seed);
            vegetationService = new VegetationService();
            farmingService    = new FarmingService();

            // Crop growth rolls must be a pure function of synced state (world seed + world clock):
            // environmental mutations are never broadcast, so host and clients simulate in lockstep.
            farmingService.ConfigureDeterministicGrowth(settings != null ? settings.Seed : World.Seed);

            // Wire biome-aware sapling growth for survival terrain worlds. The resolver is a pure
            // function of (seed, worldHeight), so host and late-joining clients (which receive the
            // seed in the generation snapshot) resolve identical biomes and stay in growth lockstep.
            if (settings != null && GenerationPreset == CreativeWorldGenerationPreset.SurvivalLite)
            {
                var biomeResolver = new SurvivalBiomeResolver(settings.Seed, World.Bounds.Height);
                vegetationService.Configure(biomeResolver.BiomeIndexAt);
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
                if (ownsWorld && !suppressContainerAutoLoot && activePlayerInventory != null)
                    TryLootContainerInto(change.Position, activePlayerInventory);
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
        public Inventory ActivePlayerInventory => activePlayerInventory;
        public void SetActivePlayerInventory(Inventory inventory) => activePlayerInventory = inventory;

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
                if (Renderer != null)
                    Renderer.RebuildDirty();
            }
        }

        CropGrowthConditions ResolveCropGrowthConditions(BlockPosition cropPosition)
        {
            if (World == null)
                return CropGrowthConditions.Favorable;

            BlockRegistry registry = Registry ?? BlockRegistry.Default;
            VoxelSkyLightMap skyLight = Renderer != null ? Renderer.SkyLight : null;
            float sampledLight = VoxelLightSampler.SampleAirLight(World, registry, cropPosition, skyLight: skyLight);
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

        void ConfigureInteractionController(WorldGenerationSettings settings)
        {
            if (interactionController == null)
                return;

            if (hotbar == null)
                hotbar = FindAnyObjectByType<CreativeHotbar>();

            if (placementPreview == null)
                placementPreview = FindAnyObjectByType<PlacementPreview>();

            if (placementPreview == null)
                placementPreview = CreatePlacementPreview();

            interactionController.Configure(
                World,
                Registry,
                hotbar,
                placementPreview,
                settings != null
                    ? new Bounds(new Vector3(settings.SpawnPosition.X + 0.5f, settings.SpawnPosition.Y + 0.5f, settings.SpawnPosition.Z + 0.5f), Vector3.one)
                    : null,
                Renderer,
                authoritySync: authoritySync);
        }

        void ConfigureVoidSafetyFloor()
        {
            if (voidSafetyFloor == null)
                voidSafetyFloor = GetComponentInChildren<BlockiverseVoidSafetyFloor>(true);

            if (voidSafetyFloor == null)
            {
                var floorObject = new GameObject("Void Safety Floor");
                floorObject.transform.SetParent(transform, false);
                voidSafetyFloor = floorObject.AddComponent<BlockiverseVoidSafetyFloor>();
            }

            voidSafetyFloor.Configure(
                World.Bounds,
                BlockiverseVoidSafetyFloor.DefaultFallAllowanceMeters,
                BlockiverseVoidSafetyFloor.DefaultThicknessMeters,
                BlockiverseVoidSafetyFloor.DefaultHorizontalMarginMeters,
                interactionLayer,
                ResolveVoidRecoverySpawnPosition());
        }

        BlockPosition ResolveVoidRecoverySpawnPosition()
        {
            if (Settings != null)
                return Settings.SpawnPosition;

            int x = World.Bounds.Width / 2;
            int z = World.Bounds.Depth / 2;
            int surfaceY = StructureService.FindSurfaceY(World, x, z);
            return new BlockPosition(x, surfaceY >= 0 ? surfaceY + 1 : 1, z);
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

        // Shared by world load (BlockiverseWorldSessionController, separate assembly) and survival
        // respawn (SurvivalVitalsRuntime) — public because no InternalsVisibleTo covers the UI assembly.
        public static void PositionRigAtSpawn(BlockPosition spawnPosition)
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return;

            Vector3 position = new(spawnPosition.X + 0.5f, spawnPosition.Y, spawnPosition.Z + 0.5f);
            float yawDegrees = rig.eulerAngles.y;
            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, yawDegrees))
                rig.position = position;
        }

        // Places the rig at a saved player position/heading (world load with saved player state).
        public static void PositionRig(Vector3 position, float yawDegrees)
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return;

            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, yawDegrees))
                rig.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        PlacementPreview CreatePlacementPreview()
        {
            GameObject previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewObject.name = "Placement Preview";
            previewObject.transform.SetParent(transform, false);

            Collider collider = previewObject.GetComponent<Collider>();

            if (collider != null)
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }

            MeshRenderer renderer = previewObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreatePreviewMaterial();

            PlacementPreview preview = previewObject.AddComponent<PlacementPreview>();
            preview.Configure(renderer);
            return preview;
        }

        static Material CreatePreviewMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Standard");
            var material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.34f, 0.84f, 0.52f, 0.42f));
            else
                material.color = new Color(0.34f, 0.84f, 0.52f, 0.42f);

            return material;
        }
    }
}
