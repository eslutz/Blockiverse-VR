using System;
using Blockiverse.Core;
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

        IPlayerVitalsView vitals;
        ISurvivalVitalsView survivalVitals;

        // Last-displayed values; Refresh runs on a 0.5s HUD cadence plus health events, so the
        // TMP string rebuild/assignment is skipped while the inputs are unchanged (the same
        // gating pattern as BlockiverseStationPanel's lastContentVersion).
        int lastHealth = int.MinValue;
        int lastMaxHealth = int.MinValue;
        int lastHunger = int.MinValue;
        int lastThirst = int.MinValue;
        int lastStamina = int.MinValue;
        string lastBaseState;

        public void Configure(TMP_Text targetHealthLabel, Slider targetHealthSlider, TMP_Text targetStateLabel)
        {
            healthLabel = targetHealthLabel;
            healthSlider = targetHealthSlider;
            stateLabel = targetStateLabel;
            InvalidateDisplayCache();
            Refresh();
        }

        public void Bind(IPlayerVitalsView playerVitals)
        {
            if (vitals != null)
                vitals.HealthChanged -= OnHealthChanged;

            vitals = playerVitals ?? throw new ArgumentNullException(nameof(playerVitals));
            vitals.HealthChanged += OnHealthChanged;
            InvalidateDisplayCache();
            Refresh();
        }

        // Optionally binds the hunger/thirst/stamina vitals so the state line shows them. These
        // tick without events, so the HUD controller refreshes the panel on a cadence.
        public void BindSurvivalVitals(ISurvivalVitalsView playerSurvivalVitals)
        {
            survivalVitals = playerSurvivalVitals;
            InvalidateDisplayCache();
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

                InvalidateDisplayCache();
                return;
            }

            if (vitals.CurrentHealth != lastHealth || vitals.MaxHealth != lastMaxHealth)
            {
                lastHealth = vitals.CurrentHealth;
                lastMaxHealth = vitals.MaxHealth;

                if (healthLabel != null)
                    healthLabel.text = $"{vitals.CurrentHealth} / {vitals.MaxHealth}";

                if (healthSlider != null)
                {
                    healthSlider.minValue = 0f;
                    healthSlider.maxValue = vitals.MaxHealth;
                    healthSlider.value = vitals.CurrentHealth;
                }
            }

            if (stateLabel != null)
            {
                string baseState = GetStateTMP_Text(vitals);
                int hunger = survivalVitals != null ? survivalVitals.Hunger : int.MinValue;
                int thirst = survivalVitals != null ? survivalVitals.Thirst : int.MinValue;
                int stamina = survivalVitals != null ? survivalVitals.Stamina : int.MinValue;

                if (baseState != lastBaseState || hunger != lastHunger || thirst != lastThirst || stamina != lastStamina)
                {
                    lastBaseState = baseState;
                    lastHunger = hunger;
                    lastThirst = thirst;
                    lastStamina = stamina;

                    stateLabel.text = survivalVitals != null
                        ? BlockiverseLocalization.Format(
                            BlockiverseLocalization.Keys.HealthVitals,
                            baseState,
                            hunger,
                            thirst,
                            stamina)
                        : baseState;
                }
            }
        }

        void InvalidateDisplayCache()
        {
            lastHealth = int.MinValue;
            lastMaxHealth = int.MinValue;
            lastHunger = int.MinValue;
            lastThirst = int.MinValue;
            lastStamina = int.MinValue;
            lastBaseState = null;
        }

        void OnDestroy()
        {
            if (vitals != null)
                vitals.HealthChanged -= OnHealthChanged;
        }

        void OnHealthChanged()
        {
            Refresh();
        }

        static string GetStateTMP_Text(IPlayerVitalsView playerVitals)
        {
            if (playerVitals.IsDead)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthDown);

            return playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthCritical)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthStable);
        }
    }
}
