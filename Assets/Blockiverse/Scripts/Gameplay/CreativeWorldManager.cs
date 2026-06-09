using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum CreativeWorldGenerationPreset
    {
        SurvivalLite,
        FlatCreative
    }

    public readonly struct GeneratedCreativeWorld
    {
        public GeneratedCreativeWorld(BlockRegistry registry, WorldGenerationSettings settings, VoxelWorld world)
            : this(registry, settings, world, InferGenerationPreset(settings))
        {
        }

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

        static CreativeWorldGenerationPreset InferGenerationPreset(WorldGenerationSettings settings)
        {
            return settings != null && settings.Bounds.Height >= 32
                ? CreativeWorldGenerationPreset.SurvivalLite
                : CreativeWorldGenerationPreset.FlatCreative;
        }
    }

    public sealed class CreativeWorldManager : MonoBehaviour
    {
        [SerializeField] Material chunkMaterial;
        [SerializeField] int interactionLayer = -1;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] PlacementPreview placementPreview;
        [SerializeField] BlockiverseVoidSafetyFloor voidSafetyFloor;
        MultiplayerChunkAuthoritySync authoritySync;
        TorchbudLightManager torchbudLightManager;
        WeatherService weatherService;
        VegetationService vegetationService;
        FarmingService farmingService;
        WorldTimeClock worldTimeClock;
        string pendingWeatherState;
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

        public string CurrentWeatherState => weatherService?.CurrentState.ToString();
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
        // If the service does not exist yet (snapshot arrived before world init), the full state is
        // buffered and applied at the end of ConfigureEnvironmentServices — no ticks/RNG are lost.
        public void RestoreWeatherSyncState(WeatherSyncState sync)
        {
            if (weatherService == null)
            {
                pendingWeatherSync = sync;
                return;
            }
            weatherService.RestoreState(sync.State, sync.Ticks, sync.RngState);
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
        }

        public void RestoreWeatherState(string weatherStateString)
        {
            if (string.IsNullOrEmpty(weatherStateString))
                return;

            if (weatherService == null)
            {
                pendingWeatherState = weatherStateString;
                return;
            }

            if (Enum.TryParse(weatherStateString, ignoreCase: true, out WeatherState parsed))
            {
                uint seed = Settings != null ? (uint)Settings.Seed : 1u;
                weatherService = new WeatherService(seed, parsed);
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

        public void InitializeDefaultWorld()
        {
            InitializeGeneratedWorld(CreateDefaultGeneratedWorld());
        }

        public void InitializeGeneratedWorld(
            GeneratedCreativeWorld generatedWorld,
            MultiplayerChunkAuthoritySync authoritySyncOverride = null)
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
            ConfigureWorldRuntime(settings, authoritySyncOverride);
            PositionRigAtSpawn(settings.SpawnPosition);
        }

        public void InitializeAuthoritativeWorldSnapshot(
            BlockRegistry registry,
            VoxelWorld world,
            MultiplayerChunkAuthoritySync authoritySyncOverride = null)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Settings = null;
            GenerationPreset = CreativeWorldGenerationPreset.SurvivalLite;
            World = world ?? throw new ArgumentNullException(nameof(world));
            ConfigureWorldRuntime(null, authoritySyncOverride);
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
            MultiplayerChunkAuthoritySync authoritySyncOverride = null)
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
                interactionLayer);

            ConfigureTorchbudLights();
            ConfigureEnvironmentServices(settings);
            ConfigureVoidSafetyFloor();

            if (authoritySyncOverride != null)
                authoritySync = authoritySyncOverride;

            ConfigureInteractionController(settings);
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

            if (vegetationService != null && World != null)
                World.BlockChanged -= OnBlockChanged;

            // Build container contents (structure loot crates) from generation loot. Done before the
            // WorldTimeClock check so containers exist even in scenes/tests without a clock.
            BuildContainerStore();

            worldTimeClock = FindFirstObjectByType<WorldTimeClock>();
            if (worldTimeClock == null)
                return;

            uint seed = settings != null ? (uint)settings.Seed : 1u;
            weatherService    = new WeatherService(seed);
            vegetationService = new VegetationService();
            farmingService    = new FarmingService();

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
            World.BlockChanged += OnBlockChanged;
            worldTimeClock.Ticked += OnWorldTick;

            // Apply any environment state received before the services existed (message ordering).
            if (pendingWeatherSync.HasValue)
            {
                weatherService.RestoreState(pendingWeatherSync.Value.State, pendingWeatherSync.Value.Ticks, pendingWeatherSync.Value.RngState);
                pendingWeatherSync = null;
            }
            else if (!string.IsNullOrEmpty(pendingWeatherState))
            {
                RestoreWeatherState(pendingWeatherState);
                pendingWeatherState = null;
            }

            if (pendingWorldTimeTicks.HasValue)
            {
                worldTimeClock.RestoreElapsedTicks(pendingWorldTimeTicks.Value);
                pendingWorldTimeTicks = null;
            }
        }

        void OnBlockChanged(BlockChange change)
        {
            BlockId b = change.NewBlock;
            if (b == BlockRegistry.Sapling || b == BlockRegistry.Sapling_S1 || b == BlockRegistry.Sapling_S2)
                vegetationService?.TrackSapling(change.Position);
            if (FarmingService.IsCropBlock(b))
                farmingService?.TrackCrop(change.Position);

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
            // the store stays consistent with the world. Auto-loot is skipped while applying a save.
            if (IsContainerBlock(change.PreviousBlock) && !IsContainerBlock(b) && containerStore != null)
            {
                if (!suppressContainerAutoLoot && activePlayerInventory != null)
                    TryLootContainerInto(change.Position, activePlayerInventory);
                containerStore.Remove(change.Position);
            }
        }

        // Wild (non-cultivated) plants that the vegetation service restores after a regrow delay.
        // Berrybush is intentionally excluded — FarmingService owns its regrowth.
        static bool IsWildRegrowthPlant(BlockId block) =>
            block == BlockRegistry.GrainStalk || block == BlockRegistry.Reedgrass || block == BlockRegistry.Thornbrush;

        // Blocks that carry per-position container contents.
        static bool IsContainerBlock(BlockId block) => block == BlockRegistry.StorageCrate;

        long CurrentWorldTick => worldTimeClock != null ? worldTimeClock.TotalElapsedTicks : 0L;

        // ── Container contents (structure loot crates) ───────────────────────

        ItemRegistry ContainerItemRegistry => containerItemRegistry ??= ItemRegistry.CreateDefault();

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
        public bool TryLootContainerInto(BlockPosition position, Inventory target)
        {
            if (containerStore == null || target == null)
                return false;
            if (!containerStore.Contains(position))
                return false;
            return containerStore.TransferAllInto(position, target);
        }

        // Replaces the live container store with saved contents on load (saved state is authoritative
        // over regenerated loot, so emptied crates stay empty across reloads).
        public void RestoreContainerStore(IEnumerable<(BlockPosition position, IEnumerable<(string itemId, int count)> items)> savedContainers)
        {
            containerStore ??= new ContainerInventoryStore(ContainerItemRegistry);
            if (savedContainers == null)
                return;

            foreach ((BlockPosition position, IEnumerable<(string itemId, int count)> items) in savedContainers)
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
                farmingService?.TickGrowth(World, ticks);
                farmingService?.TickRegrowth(World, ticks);
            }
        }

        void OnDestroy()
        {
            if (worldTimeClock != null)
                worldTimeClock.Ticked -= OnWorldTick;
            if (vegetationService != null && World != null)
                World.BlockChanged -= OnBlockChanged;
        }

        void ConfigureInteractionController(WorldGenerationSettings settings)
        {
            if (interactionController == null)
                return;

            if (hotbar == null)
                hotbar = FindFirstObjectByType<CreativeHotbar>();

            if (placementPreview == null)
                placementPreview = FindFirstObjectByType<PlacementPreview>();

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
                interactionLayer);
        }

        public static GeneratedCreativeWorld CreateDefaultGeneratedWorld(int seed = 6401)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);
            var preset = new SurvivalTerrainPreset(registry, settings);
            VoxelWorld world = preset.Generate();
            return new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.SurvivalLite, preset.ContainerLoot);
        }

        void Awake()
        {
            if (World == null)
                InitializeDefaultWorld();
        }

        static void PositionRigAtSpawn(BlockPosition spawnPosition)
        {
            GameObject rigObject = GameObject.Find(BlockiverseProject.XrRigRootName);
            if (rigObject == null)
                return;

            rigObject.transform.position = new Vector3(spawnPosition.X + 0.5f, spawnPosition.Y, spawnPosition.Z + 0.5f);
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
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Sprites/Default") ??
                            Shader.Find("Standard");
            var material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.34f, 0.84f, 0.52f, 0.42f));
            else
                material.color = new Color(0.34f, 0.84f, 0.52f, 0.42f);

            return material;
        }
    }
}
