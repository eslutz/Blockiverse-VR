using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseSubtitleToastPanel : MonoBehaviour
    {
        [SerializeField] BlockiverseHudToolkitSurface hudSurface;
        [SerializeField] float visibleSeconds = 2.5f;

        string currentMessage = string.Empty;
        bool visible;
        float visibleUntil;

        public string CurrentMessage => currentMessage;
        public bool IsVisible => visible;

        public void Configure(BlockiverseHudToolkitSurface targetHudSurface, float targetVisibleSeconds = 2.5f)
        {
            hudSurface = targetHudSurface;
            visibleSeconds = Mathf.Max(0.1f, targetVisibleSeconds);
            Clear();
        }

        public void ShowToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            EnsureSurface();
            currentMessage = message;
            visible = true;
            visibleUntil = Time.unscaledTime + Mathf.Max(0.1f, visibleSeconds);
            hudSurface?.ShowToast(message, visibleSeconds);
        }

        public void Clear()
        {
            currentMessage = string.Empty;
            visible = false;
            visibleUntil = 0.0f;
            hudSurface?.SetStatus(string.Empty);
        }

        void Update()
        {
            if (visibleUntil <= 0.0f || Time.unscaledTime < visibleUntil)
                return;

            Clear();
        }

        void EnsureSurface()
        {
            if (hudSurface == null)
                hudSurface = FindAnyObjectByType<BlockiverseHudToolkitSurface>(FindObjectsInactive.Include);
        }
    }
}
