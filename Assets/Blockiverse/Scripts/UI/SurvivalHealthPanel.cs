using System;
using Blockiverse.Survival;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalHealthPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text healthLabel;
        [SerializeField] Slider healthSlider;
        [SerializeField] TMP_Text stateLabel;

        PlayerVitals vitals;
        SurvivalVitals survivalVitals;

        public void Configure(TMP_Text targetHealthLabel, Slider targetHealthSlider, TMP_Text targetStateLabel)
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

        // Optionally binds the hunger/thirst/stamina vitals so the state line shows them. These
        // tick without events, so the HUD controller refreshes the panel on a cadence.
        public void BindSurvivalVitals(SurvivalVitals playerSurvivalVitals)
        {
            survivalVitals = playerSurvivalVitals;
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
            {
                string state = GetStateTMP_Text(vitals);
                if (survivalVitals != null)
                    state = $"{state} · Hunger {survivalVitals.Hunger} · Thirst {survivalVitals.Thirst} · Stamina {survivalVitals.Stamina}";
                stateLabel.text = state;
            }
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

        static string GetStateTMP_Text(PlayerVitals playerVitals)
        {
            if (playerVitals.IsDead)
                return "Down";

            return playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4 ? "Critical" : "Stable";
        }
    }
}
