using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseQuickMenuPlaceholder : MonoBehaviour
    {
        [SerializeField] Canvas targetCanvas;

        public bool IsVisible => targetCanvas != null && targetCanvas.enabled;

        public void Configure(Canvas canvas)
        {
            targetCanvas = canvas;
            Hide();
        }

        public void ToggleVisible()
        {
            if (IsVisible)
            {
                Hide();
                return;
            }

            Show();
        }

        public void Show()
        {
            if (targetCanvas != null)
                targetCanvas.enabled = true;
        }

        public void Hide()
        {
            if (targetCanvas != null)
                targetCanvas.enabled = false;
        }

        void Awake()
        {
            if (targetCanvas == null)
                targetCanvas = GetComponent<Canvas>();

            Hide();
        }
    }
}
