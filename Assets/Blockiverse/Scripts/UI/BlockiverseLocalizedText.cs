using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

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

            // Live language switching: these components previously refreshed only on enable, so
            // a runtime locale change left every visible label stale. Guarded on HasSettings so
            // scenes and tests that never touch localization never force the package awake.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        void OnDisable()
        {
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        void OnSelectedLocaleChanged(UnityEngine.Localization.Locale locale)
        {
            RefreshText();
        }
    }
}
