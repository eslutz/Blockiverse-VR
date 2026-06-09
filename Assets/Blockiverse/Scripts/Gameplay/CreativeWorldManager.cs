using System;
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
            CreativeWorldGenerationPreset generationPreset)
        {
            Registry = registry;
            Settings = settings;
            World = world;
            GenerationPreset = generationPreset;
        }

        public BlockRegistry Registry { get; }
        public WorldGenerationSettings Settings { get; }
        public VoxelWorld World { get; }
        public CreativeWorldGenerationPreset GenerationPreset { get; }

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

        public BlockRegistry Registry { get; private set; }
        public WorldGenerationSettings Settings { get; private set; }
        public CreativeWorldGenerationPreset GenerationPreset { get; private set; }
        public VoxelWorld World { get; private set; }
        public VoxelWorldRenderer Renderer { get; private set; }

        public string CurrentWeatherState => weatherService?.CurrentState.ToString();
        public WorldTimeClock WorldTimeClock => worldTimeClock;

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

            worldTimeClock = FindFirstObjectByType<WorldTimeClock>();
            if (worldTimeClock == null)
                return;

            uint seed = settings != null ? (uint)settings.Seed : 1u;
            weatherService    = new WeatherService(seed);
            vegetationService = new VegetationService();
            farmingService    = new FarmingService();
            vegetationService.ScanAndTrackSaplings(World);
            farmingService.ScanAndTrackCrops(World);
            World.BlockChanged += OnBlockChanged;
            worldTimeClock.Ticked += OnWorldTick;

            if (!string.IsNullOrEmpty(pendingWeatherState))
            {
                RestoreWeatherState(pendingWeatherState);
                pendingWeatherState = null;
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
            if (b == BlockRegistry.Air && FarmingService.IsBerrybushStage(change.PreviousBlock))
                farmingService?.OnBlockHarvested(change.PreviousBlock, change.Position);
        }

        void OnWorldTick(int ticks)
        {
            weatherService?.Tick(ticks);
            if (World != null)
            {
                vegetationService?.TickLeafDecay(World, ticks);
                vegetationService?.TickSapling(World, ticks);
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
            VoxelWorld world = new SurvivalTerrainPreset(registry, settings).Generate();
            return new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.SurvivalLite);
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
