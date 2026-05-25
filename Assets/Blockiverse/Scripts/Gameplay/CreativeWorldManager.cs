using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
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
            Registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultCreative();
            World = new FlatCreativeWorldPreset(Registry, settings).Generate();

            Renderer = GetComponent<VoxelWorldRenderer>();

            if (Renderer == null)
                Renderer = gameObject.AddComponent<VoxelWorldRenderer>();

            Renderer.Configure(World, Registry, chunkMaterial != null ? chunkMaterial : CreateFallbackMaterial(), interactionLayer);

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
                    new Bounds(new Vector3(settings.SpawnPosition.X + 0.5f, settings.SpawnPosition.Y + 0.5f, settings.SpawnPosition.Z + 0.5f), Vector3.one));
            }
        }

        void Awake()
        {
            if (World == null)
                InitializeDefaultWorld();
        }

        static Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard") ??
                            Shader.Find("Sprites/Default");
            var material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.32f, 0.55f, 0.38f, 1.0f));
            else
                material.color = new Color(0.32f, 0.55f, 0.38f, 1.0f);

            return material;
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
