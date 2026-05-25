using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class PlacementPreview : MonoBehaviour
    {
        [SerializeField] Renderer targetRenderer;

        public bool IsVisible => gameObject.activeSelf;
        public BlockPosition CurrentPosition { get; private set; }

        public void Configure(Renderer renderer)
        {
            targetRenderer = renderer;
            Hide();
        }

        public void ShowAt(BlockPosition position, bool canPlace)
        {
            CurrentPosition = position;
            transform.position = new Vector3(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
            transform.localScale = Vector3.one * 0.98f;
            gameObject.SetActive(true);

            if (targetRenderer != null)
            {
                targetRenderer.enabled = true;
                targetRenderer.material.color = canPlace
                    ? new Color(0.34f, 0.84f, 0.52f, 0.42f)
                    : new Color(0.95f, 0.25f, 0.20f, 0.42f);
            }
        }

        public void Hide()
        {
            if (targetRenderer != null)
                targetRenderer.enabled = false;

            gameObject.SetActive(false);
        }

        void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
        }
    }
}
