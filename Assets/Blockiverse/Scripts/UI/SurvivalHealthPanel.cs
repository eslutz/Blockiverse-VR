using System;
using Blockiverse.Survival;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalHealthPanel : MonoBehaviour
    {
        [SerializeField] Text healthLabel;
        [SerializeField] Slider healthSlider;
        [SerializeField] Text stateLabel;

        PlayerVitals vitals;

        public void Configure(Text targetHealthLabel, Slider targetHealthSlider, Text targetStateLabel)
        {
            healthLabel = targetHealthLabel;
            healthSlider = targetHealthSlider;
            stateLabel = targetStateLabel;
            Refresh();
        }

        public void Bind(PlayerVitals playerVitals)
        {
            if (vitals != null)
                vitals.HealthChanged -= OnHealthChanged;

            vitals = playerVitals ?? throw new ArgumentNullException(nameof(playerVitals));
            vitals.HealthChanged += OnHealthChanged;
            Refresh();
        }

        public void Refresh()
        {
            if (vitals == null)
            {
                if (healthLabel != null)
                    healthLabel.text = string.Empty;

                if (stateLabel != null)
                    stateLabel.text = string.Empty;

                if (healthSlider != null)
                    healthSlider.value = 0f;

                return;
            }

            if (healthLabel != null)
                healthLabel.text = $"{vitals.CurrentHealth} / {vitals.MaxHealth}";

            if (healthSlider != null)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = vitals.MaxHealth;
                healthSlider.value = vitals.CurrentHealth;
            }

            if (stateLabel != null)
                stateLabel.text = GetStateText(vitals);
        }

        void OnDestroy()
        {
            if (vitals != null)
                vitals.HealthChanged -= OnHealthChanged;
        }

        void OnHealthChanged(HealthChangeResult result)
        {
            Refresh();
        }

        static string GetStateText(PlayerVitals playerVitals)
        {
            if (playerVitals.IsDead)
                return "Down";

            return playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4 ? "Critical" : "Stable";
        }
    }
}
