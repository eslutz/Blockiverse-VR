using TMPro;
using UnityEngine;

namespace Blockiverse.UI
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseLocalizedText : MonoBehaviour
    {
        [SerializeField] TMP_Text targetText;
        [SerializeField] string localizationKey;
        [SerializeField] string fallbackText;

        public string LocalizationKey => localizationKey;
        public string FallbackText => fallbackText;

        public void Configure(string key, string fallback)
        {
            localizationKey = key;
            fallbackText = fallback;
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();
            RefreshText();
        }

        public void RefreshText()
        {
            if (targetText == null)
                targetText = GetComponent<TMP_Text>();

            if (targetText != null)
                targetText.text = BlockiverseLocalization.Text(localizationKey, fallbackText);
        }

        void Awake()
        {
            RefreshText();
        }

        void OnEnable()
        {
            RefreshText();
        }
    }
}
