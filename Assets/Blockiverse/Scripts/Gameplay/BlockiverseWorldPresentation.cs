using System;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // The presentation half of the world root: chunk rendering, scene lighting, emitter lights, the
    // void safety floor, and the interaction/placement rig. CreativeWorldManager owns the simulation
    // and drives this through IWorldPresentation, so the simulation never names a type that depends
    // on XRI, TextMeshPro, or uGUI.
    //
    // Lives on the same GameObject as CreativeWorldManager. Absent by construction on a dedicated
    // server, where this whole assembly is excluded from the build (ADR 0007).
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-8900)]
    public sealed class BlockiverseWorldPresentation : MonoBehaviour, IWorldPresentation
    {
        [SerializeField] Material chunkMaterial;
        [SerializeField] string[] blockTextureSetIds;
        [SerializeField] Texture2D[] blockTextureSetAtlases;
        [SerializeField] int interactionLayer = -1;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] CreativeHotbar hotbar;
        [SerializeField] PlacementPreview placementPreview;
        [SerializeField] BlockiverseVoidSafetyFloor voidSafetyFloor;

        VoxelWorldRenderer worldRenderer;
        GlowwickLightManager glowwickLightManager;
        VoxelWorld world;
        BlockRegistry registry;
        WorldGenerationSettings settings;
        MultiplayerChunkAuthoritySync authoritySync;

        public VoxelWorldRenderer Renderer => worldRenderer;
        public bool SpawnRegionReady => worldRenderer != null && worldRenderer.SpawnRegionReady;

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

        // Attaches (or reuses) the presentation on a world root and configures it in one step.
        // The presentation must live on the same GameObject as the manager, which finds it by
        // interface; this keeps that requirement in one place for the bootstrapper and for tests
        // that build a world root by hand. Idempotent, so repeated setup calls are safe.
        public static BlockiverseWorldPresentation Attach(
            CreativeWorldManager manager,
            Material material,
            int layer,
            CreativeInteractionController controller = null,
            CreativeHotbar creativeHotbar = null,
            PlacementPreview preview = null)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            BlockiverseWorldPresentation presentation = manager.GetComponent<BlockiverseWorldPresentation>();
            if (presentation == null)
                presentation = manager.gameObject.AddComponent<BlockiverseWorldPresentation>();

            presentation.Configure(material, layer, controller, creativeHotbar, preview);
            return presentation;
        }

        public void ConfigureBlockTextureAtlases(string[] textureSetIds, Texture2D[] atlasTextures)
        {
            blockTextureSetIds = textureSetIds ?? Array.Empty<string>();
            blockTextureSetAtlases = atlasTextures ?? Array.Empty<Texture2D>();
        }

        public void ConfigureForWorld(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            VoxelSkyLightMap skyLight,
            WorldGenerationSettings generationSettings,
            string textureSetId,
            MultiplayerChunkAuthoritySync sync,
            bool deferInitialRebuild)
        {
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            settings = generationSettings;
            authoritySync = sync;

            BlockiverseLightingRuntime.EnsureSceneLighting();

            if (worldRenderer == null)
                worldRenderer = GetComponent<VoxelWorldRenderer>();
            if (worldRenderer == null)
                worldRenderer = gameObject.AddComponent<VoxelWorldRenderer>();

            worldRenderer.Configure(
                world,
                registry,
                chunkMaterial,
                interactionLayer,
                ResolveSelectedBlockAtlas(textureSetId),
                textureSetId,
                deferInitialRebuild,
                skyLight);

            ConfigureGlowwickLights();
            ConfigureVoidSafetyFloor();
            ConfigureInteractionController();
        }

        public void ConfigureAuthority(MultiplayerChunkAuthoritySync sync)
        {
            authoritySync = sync;
            if (world != null && registry != null)
                ConfigureInteractionController();
        }

        public void PositionRigAtSpawn(BlockPosition spawnPosition) =>
            BlockiverseRigPlacement.PositionAtSpawn(spawnPosition);

        public void RebuildDirty()
        {
            if (worldRenderer != null)
                worldRenderer.RebuildDirty();
        }

        public void RebuildAll()
        {
            if (worldRenderer != null)
                worldRenderer.RebuildAll();
        }

        public void RebuildSpawnRegion(BlockPosition spawn, int radiusChunks = 1)
        {
            if (worldRenderer != null)
                worldRenderer.RebuildSpawnRegion(spawn, radiusChunks);
        }

        Texture2D ResolveSelectedBlockAtlas(string textureSetId)
        {
            string selected = BlockTextureSetIds.Normalize(textureSetId);
            int count = Math.Min(blockTextureSetIds?.Length ?? 0, blockTextureSetAtlases?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(BlockTextureSetIds.Normalize(blockTextureSetIds[i]), selected, StringComparison.OrdinalIgnoreCase))
                    return blockTextureSetAtlases[i];
            }

            return null;
        }

        void ConfigureGlowwickLights()
        {
            if (glowwickLightManager == null)
                glowwickLightManager = GetComponent<GlowwickLightManager>();

            if (glowwickLightManager == null)
                glowwickLightManager = gameObject.AddComponent<GlowwickLightManager>();

            glowwickLightManager.Configure(world, registry);
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
                world.Bounds,
                BlockiverseVoidSafetyFloor.DefaultFallAllowanceMeters,
                BlockiverseVoidSafetyFloor.DefaultThicknessMeters,
                BlockiverseVoidSafetyFloor.DefaultHorizontalMarginMeters,
                interactionLayer,
                ResolveVoidRecoverySpawnPosition());
        }

        BlockPosition ResolveVoidRecoverySpawnPosition()
        {
            if (settings != null)
                return settings.SpawnPosition;

            int x = world.Bounds.Width / 2;
            int z = world.Bounds.Depth / 2;
            int surfaceY = StructureService.FindSurfaceY(world, x, z);
            return new BlockPosition(x, surfaceY >= 0 ? surfaceY + 1 : 1, z);
        }

        void ConfigureInteractionController()
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
                world,
                registry,
                hotbar,
                placementPreview,
                settings != null
                    ? new Bounds(new Vector3(settings.SpawnPosition.X + 0.5f, settings.SpawnPosition.Y + 0.5f, settings.SpawnPosition.Z + 0.5f), Vector3.one)
                    : null,
                worldRenderer,
                authoritySync: authoritySync);
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

            MeshRenderer meshRenderer = previewObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = CreatePreviewMaterial();
            // CreatePrimitive defaults to casting shadows. URP/Unlit happens to ship no ShadowCaster
            // pass today, so this is belt-and-braces rather than load-bearing — but the aim ghost
            // must never throw a solid block shadow if this material is ever swapped for a lit one.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            PlacementPreview preview = previewObject.AddComponent<PlacementPreview>();
            preview.Configure(meshRenderer);
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
