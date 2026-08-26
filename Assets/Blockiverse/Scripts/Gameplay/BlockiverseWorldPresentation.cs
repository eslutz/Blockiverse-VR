using System;
using Blockiverse.Core;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // The presentation half of the world root: chunk rendering, scene lighting, emitter lights, the
    // mirror-pane avatar surface, the void safety floor, and the interaction/placement rig.
    // CreativeWorldManager owns the simulation and drives this through IWorldPresentation, so the
    // simulation never names a type that depends on XRI, TextMeshPro, uGUI, or Meta Avatars.
    //
    // Lives on the same GameObject as CreativeWorldManager. Absent by construction on a dedicated
    // server, where this whole assembly is excluded from the build (ADR 0007) — the mirror (issue
    // #340) is presentation-only for the same reason the void floor and glowwick lights are: none
    // of them mean anything with no renderer or local player.
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
        BlockiverseMirrorSurfaceManager mirrorSurfaceManager;
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
            ConfigureMirrorSurfaces();
            ConfigureVoidSafetyFloor();
            ConfigureInteractionController();
            ConfigureCloudDeck(world, chunkMaterial, textureSetId);
        }

        // The blocky cloud layer. Render-only: it is never a voxel, never saved, never collided
        // with, and is a pure function of (seed, clock) so every peer draws the same sky with
        // nothing on the wire.
        void ConfigureCloudDeck(VoxelWorld world, Material chunkMaterial, string textureSetId)
        {
            if (world == null)
                return;

            BlockiverseLightingCycleController lighting = BlockiverseLightingRuntime.EnsureSceneLighting();

            if (lighting == null)
                return;

            if (cloudDeck == null)
            {
                Transform existing = transform.Find(CloudDeckObjectName);
                GameObject host;

                if (existing != null)
                {
                    host = existing.gameObject;
                }
                else
                {
                    host = new GameObject(CloudDeckObjectName);
                    host.transform.SetParent(transform, worldPositionStays: false);
                }

                cloudDeck = host.GetComponent<BlockiverseCloudDeck>();
                if (cloudDeck == null)
                    cloudDeck = host.AddComponent<BlockiverseCloudDeck>();
            }

            // Borrows the chunk material rather than minting a shader. GraphicsSettings'
            // always-included list carries the voxel shader alone, so anything reached through
            // Shader.Find is stripped from the Android player and renders magenta on device while
            // looking correct in the editor. The sky variant of that one shader is UNLIT and reads
            // vertex colour as a colour, which is what lets the deck and the skirt fade their far
            // edges into the sky; it is also still fogged like everything else.
            // Destroyed and re-minted, never accumulated. ConfigureForWorld runs on world load,
            // texture-set switch, multiplayer join and every return to the title mini-world, so a
            // clone that is only ever created leaks one material per world entered.
            // VoxelWorldRenderer already does this correctly for its four clones; this did not.
            DestroyGenerated(skyMaterial);
            skyMaterial = chunkMaterial != null
                ? BlockVisualAtlas.CreateSkyMaterial(chunkMaterial, ResolveSelectedBlockAtlas(textureSetId), textureSetId)
                : null;
            Material deckMaterial = skyMaterial;

            // Valid UVs for the sky meshes, and nothing more: the sky shader variant samples no
            // texture at all. It used to tint itself by this one "white" texel, which is white in
            // three of the four texture sets and (194, 204, 209) in `original` — see the comment
            // on WhiteTexelUv for why that had to go.
            Rect cloudTile = BlockVisualAtlas.WhiteTexelUv;

            // Follows the head so the deck stays overhead wherever the player walks, the same
            // window-that-travels-with-you pattern the precipitation volume uses. Falls back to
            // this transform outside play mode, where there is no main camera.
            Camera headCamera = Camera.main;
            Transform followTransform = headCamera != null ? headCamera.transform : transform;

            cloudDeck.Configure(followTransform, deckMaterial, cloudTile, world.Seed);
            lighting.ConfigureCloudDeck(cloudDeck);

            ConfigureHorizonSkirt(world, deckMaterial, cloudTile, lighting);
        }

        // The sea-level plane that hides the world's own edge. Same material, same white texel and
        // the same render-only contract as the deck — it is the deck's mirror image, one lid over
        // the void above and one below.
        void ConfigureHorizonSkirt(
            VoxelWorld world,
            Material skirtMaterial,
            Rect tile,
            BlockiverseLightingCycleController lighting)
        {
            // Only worlds that HAVE a sea level get a sea. Builder canvases are 64 blocks tall
            // (WorldSaveGeneration.BuilderWorldHeight) against a sea level of 64, so a plane at sea
            // level would float above the entire world rather than meeting anything — and a void
            // builder is deliberately a void, which a floor to the horizon would quietly undo.
            if (!BlockiverseHorizonSkirt.SuitsWorld(world.Bounds))
            {
                if (horizonSkirt != null)
                    horizonSkirt.gameObject.SetActive(false);

                lighting.ConfigureHorizonSkirt(null);
                return;
            }

            if (horizonSkirt == null)
            {
                Transform existing = transform.Find(HorizonSkirtObjectName);
                GameObject host;

                if (existing != null)
                {
                    host = existing.gameObject;
                }
                else
                {
                    host = new GameObject(HorizonSkirtObjectName);
                    host.transform.SetParent(transform, worldPositionStays: false);
                }

                horizonSkirt = host.GetComponent<BlockiverseHorizonSkirt>();
                if (horizonSkirt == null)
                    horizonSkirt = host.AddComponent<BlockiverseHorizonSkirt>();
            }

            // World-space and static: unlike the deck it does NOT follow the head, because it is
            // pinned to the world's actual bounds rather than to a radius around the player.
            horizonSkirt.gameObject.SetActive(true);
            horizonSkirt.transform.localPosition = Vector3.zero;
            horizonSkirt.transform.localRotation = Quaternion.identity;
            horizonSkirt.Configure(world.Bounds, skirtMaterial, tile);
            lighting.ConfigureHorizonSkirt(horizonSkirt);
        }

        const string CloudDeckObjectName = "Blockiverse Cloud Deck";
        const string HorizonSkirtObjectName = "Blockiverse Horizon Skirt";
        BlockiverseCloudDeck cloudDeck;
        BlockiverseHorizonSkirt horizonSkirt;
        Material skyMaterial;

        void OnDestroy()
        {
            DestroyGenerated(skyMaterial);
            skyMaterial = null;
        }

        static void DestroyGenerated(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
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
            // BlockTextureSetIds.Normalize, NOT NormalizeToken, and deliberately so: this method
            // answers "which of the four BUILT-IN atlases do I bind", and only built-ins ship in
            // the serialized array. A `pack:` token therefore lands on the default set here, which
            // is the correct fallback until the compositor can build a pack atlas.
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

        void ConfigureMirrorSurfaces()
        {
            if (mirrorSurfaceManager == null)
                mirrorSurfaceManager = GetComponent<BlockiverseMirrorSurfaceManager>();

            if (mirrorSurfaceManager == null)
                mirrorSurfaceManager = gameObject.AddComponent<BlockiverseMirrorSurfaceManager>();

            mirrorSurfaceManager.Configure(world, registry);
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
