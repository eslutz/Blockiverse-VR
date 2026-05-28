using System;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public readonly struct GeneratedCreativeWorld
    {
        public GeneratedCreativeWorld(BlockRegistry registry, WorldGenerationSettings settings, VoxelWorld world)
        {
            Registry = registry;
            Settings = settings;
            World = world;
        }

        public BlockRegistry Registry { get; }
        public WorldGenerationSettings Settings { get; }
        public VoxelWorld World { get; }
    }

    public sealed class CreativeWorldManager : MonoBehaviour
    {
        [SerializeField] Material chunkMaterial;
        [SerializeField] int interactionLayer = -1;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] PlacementPreview placementPreview;

        public BlockRegistry Registry { get; private set; }
        public VoxelWorld World { get; private set; }
        public VoxelWorldRenderer Renderer { get; private set; }

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

        public void InitializeGeneratedWorld(GeneratedCreativeWorld generatedWorld)
        {
            if (generatedWorld.Registry == null)
                throw new ArgumentException("Generated world requires a block registry.", nameof(generatedWorld));
            if (generatedWorld.Settings == null)
                throw new ArgumentException("Generated world requires generation settings.", nameof(generatedWorld));
            if (generatedWorld.World == null)
                throw new ArgumentException("Generated world requires voxel data.", nameof(generatedWorld));

            Registry = generatedWorld.Registry;
            WorldGenerationSettings settings = generatedWorld.Settings;
            World = generatedWorld.World;

            Renderer = GetComponent<VoxelWorldRenderer>();

            if (Renderer == null)
                Renderer = gameObject.AddComponent<VoxelWorldRenderer>();

            Renderer.Configure(
                World,
                Registry,
                chunkMaterial,
                interactionLayer);

            if (interactionController != null)
            {
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
                    new Bounds(new Vector3(settings.SpawnPosition.X + 0.5f, settings.SpawnPosition.Y + 0.5f, settings.SpawnPosition.Z + 0.5f), Vector3.one),
                    Renderer);
            }

            PositionRigAtSpawn(settings.SpawnPosition);
        }

        public static GeneratedCreativeWorld CreateDefaultGeneratedWorld(int seed = 6401)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);
            VoxelWorld world = new SurvivalLiteWorldPreset(registry, settings).Generate();
            return new GeneratedCreativeWorld(registry, settings, world);
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
