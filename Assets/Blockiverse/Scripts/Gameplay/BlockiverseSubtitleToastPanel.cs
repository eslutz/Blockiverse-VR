using TMPro;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseSubtitleToastPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text messageLabel;
        [SerializeField] float visibleSeconds = 2.5f;

        float visibleUntil;

        public string CurrentMessage => messageLabel != null ? messageLabel.text : string.Empty;
        public bool IsVisible => messageLabel != null && messageLabel.gameObject.activeSelf;

        public void Configure(TMP_Text targetMessageLabel, float targetVisibleSeconds = 2.5f)
        {
            messageLabel = targetMessageLabel;
            visibleSeconds = Mathf.Max(0.1f, targetVisibleSeconds);
            Clear();
        }

        public void ShowToast(string message)
        {
            if (messageLabel == null || string.IsNullOrWhiteSpace(message))
                return;

            messageLabel.text = message;
            messageLabel.gameObject.SetActive(true);
            visibleUntil = Time.unscaledTime + Mathf.Max(0.1f, visibleSeconds);
        }

        public void Clear()
        {
            visibleUntil = 0f;
            if (messageLabel == null)
                return;

            messageLabel.text = string.Empty;
            messageLabel.gameObject.SetActive(false);
        }

        void Update()
        {
            if (visibleUntil <= 0f || Time.unscaledTime < visibleUntil)
                return;

            Clear();
        }
    }
}
