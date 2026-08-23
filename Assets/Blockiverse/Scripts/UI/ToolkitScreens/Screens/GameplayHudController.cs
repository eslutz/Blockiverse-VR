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
    // UI Toolkit port of the gameplay HUD's health readout (matrix row 21; uGUI:
    // SurvivalHealthPanel bound by SurvivalHudController) plus panel-open shortcuts into the
    // routed inventory / crafting / shared-crate / block-catalog screens.
    //
    // The two behaviours this port exists to not lose (matrix §4 items 15 and 16):
    //  - Every label write is gated on the last-displayed VALUES (health/hunger/thirst/
    //    stamina), never on locale — text assignment still allocates in retained mode, and
    //    the readout refreshes on a 0.5 s cadence, so ungated writes are a per-frame cost.
    //    A locale change with unchanged vitals must therefore invalidate the cache
    //    explicitly or the previous language stays on screen.
    //  - Vitals tick without events (only health has one), so the state line refreshes on
    //    the same 0.5 s cadence SurvivalHudController used.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayHud.uxml",
        590, 190, UiToolkitPlacementProfile.Hud)]
    public sealed class GameplayHudController : UiToolkitScreenController
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
        Button openInventoryButton;
        Button openCraftingButton;
        Button openCrateButton;
        Button openCatalogButton;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

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

        // Public click seams: EditMode cannot deliver a ClickEvent without a runtime panel.
        public void OpenInventory()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenInventoryScreen();
        }

        public void OpenCrafting()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenCraftingScreen();
        }

        // No Open verb exists for the crate screen (uGUI wires only CloseStationCrateScreen),
        // so the canonical route is pushed directly, shaped exactly like OpenInventoryScreen.
        public void OpenCrate()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null || controller.Router == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.Router.PushScreen(new ScreenRoute(MenuActions.StationCrateScreen));
        }

        public void OpenCatalog()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller == null)
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            controller.OpenCatalogScreen();
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
            openInventoryButton = Require<Button>(root, "bv-hud-open-inventory", ref allFound);
            openCraftingButton = Require<Button>(root, "bv-hud-open-crafting", ref allFound);
            openCrateButton = Require<Button>(root, "bv-hud-open-crate", ref allFound);
            openCatalogButton = Require<Button>(root, "bv-hud-open-catalog", ref allFound);

            // Brand-new element instances: repaint from the bound vitals, not the cache.
            InvalidateDisplayCache();
            Refresh();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (openInventoryButton != null)
                openInventoryButton.clicked += OpenInventory;

            if (openCraftingButton != null)
                openCraftingButton.clicked += OpenCrafting;

            if (openCrateButton != null)
                openCrateButton.clicked += OpenCrate;

            if (openCatalogButton != null)
                openCatalogButton.clicked += OpenCatalog;

            // Dynamic text set through UiText goes stale on a live language switch; static
            // button labels update through their native bindings and need nothing here.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (openInventoryButton != null)
                openInventoryButton.clicked -= OpenInventory;

            if (openCraftingButton != null)
                openCraftingButton.clicked -= OpenCrafting;

            if (openCrateButton != null)
                openCrateButton.clicked -= OpenCrate;

            if (openCatalogButton != null)
                openCatalogButton.clicked -= OpenCatalog;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            healthRatioLabel = null;
            healthFill = null;
            vitalsStateLabel = null;
            openInventoryButton = null;
            openCraftingButton = null;
            openCrateButton = null;
            openCatalogButton = null;
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
