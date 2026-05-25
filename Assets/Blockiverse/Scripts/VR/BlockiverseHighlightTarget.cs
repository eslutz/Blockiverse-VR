using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseHighlightTarget : MonoBehaviour
    {
        [SerializeField] Renderer targetRenderer;
        [SerializeField] Material highlightMaterial;

        Material[] originalMaterials;
        bool hasOriginalMaterials;
        bool isHighlighted;

        public bool IsHighlighted => isHighlighted;

        public void Configure(Renderer renderer, Material material)
        {
            if (targetRenderer != renderer)
            {
                if (isHighlighted)
                    RestoreOriginalMaterials();

                originalMaterials = null;
                hasOriginalMaterials = false;
            }

            targetRenderer = renderer;
            highlightMaterial = material;
            CaptureOriginalMaterials();
            ApplyHighlightState();
        }

        public void SetHighlighted(bool highlighted)
        {
            if (isHighlighted == highlighted)
                return;

            isHighlighted = highlighted;
            ApplyHighlightState();
        }

        void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();

            CaptureOriginalMaterials();
            ApplyHighlightState();
        }

        void OnDisable()
        {
            ClearHighlightState();
        }

        void OnDestroy()
        {
            ClearHighlightState();
        }

        void ClearHighlightState()
        {
            if (!isHighlighted)
                return;

            RestoreOriginalMaterials();
            isHighlighted = false;
        }

        void CaptureOriginalMaterials()
        {
            if (hasOriginalMaterials || targetRenderer == null)
                return;

            originalMaterials = targetRenderer.sharedMaterials;
            hasOriginalMaterials = true;
        }

        void ApplyHighlightState()
        {
            if (targetRenderer == null)
                return;

            CaptureOriginalMaterials();

            if (!isHighlighted)
            {
                RestoreOriginalMaterials();
                return;
            }

            if (highlightMaterial == null)
                return;

            int materialCount = Mathf.Max(1, targetRenderer.sharedMaterials.Length);
            Material[] highlightedMaterials = new Material[materialCount];

            for (int index = 0; index < highlightedMaterials.Length; index++)
                highlightedMaterials[index] = highlightMaterial;

            targetRenderer.sharedMaterials = highlightedMaterials;
        }

        void RestoreOriginalMaterials()
        {
            if (targetRenderer == null || !hasOriginalMaterials || originalMaterials == null)
                return;

            targetRenderer.sharedMaterials = originalMaterials;
        }
    }
}
