using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The health/vitals half of the gameplay HUD (matrix row 21; uGUI: SurvivalHealthPanel bound
    // by SurvivalHudController).
    //
    // Split out of GameplayHudController so the readout and the action bar can sit at opposite
    // edges of vision. They shared one panel parked dead centre 1.15 m ahead — the Hud profile's
    // default HudLocalX/Y/Z — which put the health bar and four buttons over the middle of the
    // world. A HUD wants the perimeter, and two things cannot occupy two corners while they share
    // a document.
    //
    // The two behaviours this port exists to not lose (matrix §4 items 15 and 16):
    //  - Every label write is gated on the last-displayed VALUES (health/hunger/thirst/stamina),
    //    never on locale — text assignment still allocates in retained mode, and the readout
    //    refreshes on a 0.5 s cadence, so ungated writes are a per-frame cost. A locale change
    //    with unchanged vitals must therefore invalidate the cache explicitly or the previous
    //    language stays on screen.
    //  - Vitals tick without events (only health has one), so the state line refreshes on the
    //    same 0.5 s cadence SurvivalHudController used.
    //
    // NonInteractive: nothing here is clickable, so it generates no collider and cannot intercept
    // the XRI ray — which matters more now that it sits near the edge of view rather than dead
    // ahead, where a stray trigger volume would be permanently in the way.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayStats.uxml",
        460, 190, UiToolkitPlacementProfile.Hud, HudLocalX = 0.40f, HudLocalY = 0.24f, HudLocalZ = 1.10f, NonInteractive = true)]
    public sealed class GameplayStatsController : UiToolkitScreenController
    {
        public const float VitalsRefreshIntervalSeconds = 0.5f;

        // Table keys shared with the uGUI panel — the copy contract (values match
        // BlockiverseLocalization.Keys verbatim; screens in this assembly stay off the shim).
        static class Keys
        {
            public const string HealthVitalsRatio = "ui.value.vitals_ratio";
            public const string HealthVitals = "ui.status.health.vitals";
            public const string HealthDown = "ui.status.health.down";
            public const string HealthCritical = "ui.status.health.critical";
            public const string HealthStable = "ui.status.health.stable";
        }

        Label healthRatioLabel;
        VisualElement healthFill;
        Label vitalsStateLabel;

        SurvivalVitalsRuntime vitalsRuntime;
        IPlayerVitalsView vitals;
        ISurvivalVitalsView survivalVitals;
        // The exact subscribed view, stored so a later bind detaches the previous handler
        // instead of leaking it (SurvivalHudController's SelectionChanged bookkeeping).
        IPlayerVitalsView healthChangedSource;

        int lastHealth = int.MinValue;
        int lastMaxHealth = int.MinValue;
        int lastHunger = int.MinValue;
        int lastThirst = int.MinValue;
        int lastStamina = int.MinValue;
        string lastBaseState;

        float nextVitalsRefreshTime;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public IPlayerVitalsView Vitals => vitals;

        public void Bind(IPlayerVitalsView playerVitals)
        {
            if (playerVitals == null)
                throw new ArgumentNullException(nameof(playerVitals));

            if (healthChangedSource != null)
                healthChangedSource.HealthChanged -= OnHealthChanged;

            vitals = playerVitals;
            healthChangedSource = playerVitals;
            playerVitals.HealthChanged += OnHealthChanged;
            InvalidateDisplayCache();
            Refresh();
        }

        // Optional hunger/thirst/stamina source for the state line; these vitals tick without
        // events, so the Update cadence keeps them current.
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
                if (healthRatioLabel != null)
                    healthRatioLabel.text = string.Empty;

                if (vitalsStateLabel != null)
                    vitalsStateLabel.text = string.Empty;

                if (healthFill != null)
                    healthFill.style.width = Length.Percent(0f);

                InvalidateDisplayCache();
                return;
            }

            if (vitals.CurrentHealth != lastHealth || vitals.MaxHealth != lastMaxHealth)
            {
                lastHealth = vitals.CurrentHealth;
                lastMaxHealth = vitals.MaxHealth;

                if (healthRatioLabel != null)
                    healthRatioLabel.text = UiText.Format(Keys.HealthVitalsRatio, vitals.CurrentHealth, vitals.MaxHealth);

                if (healthFill != null)
                {
                    float percent = vitals.MaxHealth > 0
                        ? Mathf.Clamp01((float)vitals.CurrentHealth / vitals.MaxHealth) * 100f
                        : 0f;
                    healthFill.style.width = Length.Percent(percent);
                }
            }

            if (vitalsStateLabel != null)
            {
                string baseState = GetStateText(vitals);
                int hunger = survivalVitals != null ? survivalVitals.Hunger : int.MinValue;
                int thirst = survivalVitals != null ? survivalVitals.Thirst : int.MinValue;
                int stamina = survivalVitals != null ? survivalVitals.Stamina : int.MinValue;

                if (baseState != lastBaseState || hunger != lastHunger || thirst != lastThirst || stamina != lastStamina)
                {
                    lastBaseState = baseState;
                    lastHunger = hunger;
                    lastThirst = thirst;
                    lastStamina = stamina;

                    vitalsStateLabel.text = survivalVitals != null
                        ? UiText.Format(Keys.HealthVitals, baseState, hunger, thirst, stamina)
                        : baseState;
                }
            }
        }

        protected override void OnAwake()
        {
            BindFromScene();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            healthRatioLabel = Require<Label>(root, "bv-health-ratio", ref allFound);
            healthFill = Require<VisualElement>(root, "bv-health-fill", ref allFound);
            vitalsStateLabel = Require<Label>(root, "bv-vitals-state", ref allFound);

            // Brand-new element instances: repaint from the bound vitals, not the cache.
            InvalidateDisplayCache();
            Refresh();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            // Dynamic text set through UiText goes stale on a live language switch.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            healthRatioLabel = null;
            healthFill = null;
            vitalsStateLabel = null;
        }

        protected override void OnShown()
        {
            BindFromScene();
            Refresh();
        }

        void OnDestroy()
        {
            if (healthChangedSource != null)
                healthChangedSource.HealthChanged -= OnHealthChanged;
        }

        void Update()
        {
            if (!IsVisible || vitalsRuntime == null || Time.time < nextVitalsRefreshTime)
                return;

            nextVitalsRefreshTime = Time.time + VitalsRefreshIntervalSeconds;
            Refresh();
        }

        // Binds to the runtime-owned vitals when present (mirrors SurvivalHudController's
        // BindValidationState). Re-run on every show so a replaced runtime instance rebinds.
        void BindFromScene()
        {
            SurvivalVitalsRuntime runtime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            if (runtime == null)
                return;

            vitalsRuntime = runtime;

            if (!ReferenceEquals(healthChangedSource, runtime.HealthView))
                Bind(runtime.HealthView);

            if (!ReferenceEquals(survivalVitals, runtime.SurvivalVitalsView))
                BindSurvivalVitals(runtime.SurvivalVitalsView);
        }

        void OnHealthChanged()
        {
            Refresh();
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            InvalidateDisplayCache();
            Refresh();
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

        static string GetStateText(IPlayerVitalsView playerVitals)
        {
            if (playerVitals.IsDead)
                return UiText.Get(Keys.HealthDown);

            return playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4
                ? UiText.Get(Keys.HealthCritical)
                : UiText.Get(Keys.HealthStable);
        }
    }
}
