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
    // The vitals readout of the gameplay HUD (matrix row 21; uGUI: SurvivalHealthPanel bound by
    // SurvivalHudController).
    //
    // Split out of GameplayHudController so the readout and the action bar can sit at opposite
    // edges of vision — they shared one panel parked dead centre, which Eric reported as making
    // things hard to see.
    //
    // ── Meters, not a sentence (FPV HUD report, ADR 0010 amendment 2026-08-25) ─
    //
    // This used to be a health bar plus one caption: "Stable — 80/60/40". The report asks for
    // vitals a player can read WITHOUT reading, so each of the four is now a row carrying its
    // state in four channels at once: an ASCII marker, the word, the number, and a filled bar.
    // The level is computed ONCE per row and drives all four, which is what makes it impossible
    // for them to disagree — and what keeps the warning legible for a player who cannot separate
    // ochre from oxide.
    //
    // ── The two behaviours the port must not lose (matrix §4 items 15 and 16) ─
    //
    //  - Every write is gated on the last-displayed VALUES, never on locale. Text assignment
    //    allocates in retained mode and this panel is visible the whole session, so an ungated
    //    refresh is a permanent per-frame cost. A locale change with unchanged vitals must
    //    therefore invalidate the cache explicitly or the previous language stays on screen.
    //  - Hunger, thirst and stamina tick without events (only health has one), so they refresh on
    //    the same 0.5 s cadence SurvivalHudController used.
    //
    // NonInteractive: nothing here is clickable, so it generates no collider and cannot intercept
    // the XRI ray.
    //
    // ── Placement: lower-LEFT, per the FPV report ────────────────────────────
    //
    // The report's composition table says "Vitals | Lower-left or lower-peripheral cluster". This
    // panel was originally at X +0.40 / Y +0.14 — 20 deg RIGHT of centre and 7 deg ABOVE eye level,
    // which is close to the opposite corner from what the report asks for. Live validation in the
    // simulator flagged the whole persistent HUD as clumped in front of the view, and Eric
    // confirmed it; this was the clearest single contradiction.
    //
    // Now 16-35 deg left, 2-15 deg below: out of the central cone the report wants left as world,
    // and far enough from the hotbar band (15-19 deg below) to clear it by 4 mm.
    //
    // The 460x250 SIZE is deliberately unchanged. A smaller box was the obvious way to fit it
    // further left, but live validation confirmed the vitals ratio renders on one line at this size
    // with the widened 130 px value box, under both daylight and a night+snow transition. Shrinking
    // it would risk re-breaking the wrapping defect that widening just fixed, for no gain the extra
    // horizontal offset does not already give.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayStats.uxml",
        460, 250, UiToolkitPlacementProfile.Hud, HudLocalX = -0.55f, HudLocalY = -0.165f, HudLocalZ = 1.10f, NonInteractive = true)]
    public sealed class GameplayStatsController : UiToolkitScreenController
    {
        public const float VitalsRefreshIntervalSeconds = 0.5f;

        // Fractions of max at which a vital changes how it reads. Policy, not presentation: the
        // same two numbers decide the meter colour, the marker and the value colour.
        public const float LowFraction = 0.5f;
        public const float CriticalFraction = 0.25f;

        public enum VitalLevel
        {
            Ok = 0,
            Low = 1,
            Critical = 2,
        }

        // Row order is fixed and matches the document. Health first because it is the only vital
        // that exists in every mode.
        enum Row
        {
            Health = 0,
            Hunger = 1,
            Thirst = 2,
            Stamina = 3,
        }

        const int RowCount = 4;

        static class Keys
        {
            public const string Ratio = "ui.value.vitals_ratio";
            public const string MarkerLow = "ui.screen.vitals.marker.low";
            public const string MarkerCritical = "ui.screen.vitals.marker.critical";
        }

        static readonly string[] RowNames = { "health", "hunger", "thirst", "stamina" };

        readonly VisualElement[] rows = new VisualElement[RowCount];
        readonly Label[] markers = new Label[RowCount];
        readonly Label[] values = new Label[RowCount];
        readonly VisualElement[] fills = new VisualElement[RowCount];

        readonly int[] lastValues = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        readonly int[] lastMaxima = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        readonly VitalLevel[] lastLevels = new VitalLevel[RowCount];
        readonly bool[] lastLevelValid = new bool[RowCount];

        SurvivalVitalsRuntime vitalsRuntime;
        IPlayerVitalsView vitals;
        ISurvivalVitalsView survivalVitals;

        // The exact subscribed view, stored so a later bind detaches the previous handler instead
        // of leaking it (SurvivalHudController's SelectionChanged bookkeeping).
        IPlayerVitalsView healthChangedSource;

        bool lastSurvivalVitalsPresent;
        bool lastSurvivalVitalsPresentValid;

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

        // Optional hunger/thirst/stamina source; these vitals tick without events, so the Update
        // cadence keeps them current. Null in Creative, which hides those three rows entirely.
        public void BindSurvivalVitals(ISurvivalVitalsView playerSurvivalVitals)
        {
            survivalVitals = playerSurvivalVitals;
            InvalidateDisplayCache();
            Refresh();
        }

        // Exposed so tests can assert the policy without reaching through the view.
        public static VitalLevel LevelFor(int current, int max)
        {
            if (max <= 0)
                return VitalLevel.Ok;

            float fraction = Mathf.Clamp01((float)current / max);

            if (fraction <= CriticalFraction)
                return VitalLevel.Critical;

            return fraction <= LowFraction ? VitalLevel.Low : VitalLevel.Ok;
        }

        public void Refresh()
        {
            if (rows[0] == null)
                return;

            if (vitals == null)
            {
                for (int i = 0; i < RowCount; i++)
                    ClearRow(i);

                InvalidateDisplayCache();
                return;
            }

            // Creative has health and nothing else. Hiding rather than emptying is the only honest
            // rendering: an unbound meter draws as empty, which reads as starvation.
            bool survivalPresent = survivalVitals != null;

            if (!lastSurvivalVitalsPresentValid || lastSurvivalVitalsPresent != survivalPresent)
            {
                lastSurvivalVitalsPresent = survivalPresent;
                lastSurvivalVitalsPresentValid = true;

                for (int i = (int)Row.Hunger; i < RowCount; i++)
                    rows[i]?.EnableInClassList("gs-vital--absent", !survivalPresent);
            }

            ApplyRow((int)Row.Health, vitals.CurrentHealth, vitals.MaxHealth);

            if (!survivalPresent)
                return;

            int max = survivalVitals.Max;
            ApplyRow((int)Row.Hunger, survivalVitals.Hunger, max);
            ApplyRow((int)Row.Thirst, survivalVitals.Thirst, max);
            ApplyRow((int)Row.Stamina, survivalVitals.Stamina, max);
        }

        void ApplyRow(int index, int current, int max)
        {
            VitalLevel level = LevelFor(current, max);
            bool valueChanged = lastValues[index] != current || lastMaxima[index] != max;
            bool levelChanged = !lastLevelValid[index] || lastLevels[index] != level;

            if (!valueChanged && !levelChanged)
                return;

            if (valueChanged)
            {
                lastValues[index] = current;
                lastMaxima[index] = max;

                if (values[index] != null)
                    values[index].text = UiText.Format(Keys.Ratio, current, max);

                if (fills[index] != null)
                {
                    // Percentage rather than pixels, so the bar tracks any panel resize without
                    // this controller knowing the meter's measured width.
                    float percent = max > 0 ? Mathf.Clamp01((float)current / max) * 100f : 0f;
                    fills[index].style.width = Length.Percent(percent);
                }
            }

            if (!levelChanged)
                return;

            lastLevels[index] = level;
            lastLevelValid[index] = true;

            fills[index]?.EnableInClassList("gs-vital__fill--low", level == VitalLevel.Low);
            fills[index]?.EnableInClassList("gs-vital__fill--critical", level == VitalLevel.Critical);

            rows[index]?.EnableInClassList("gs-vital--low", level == VitalLevel.Low);
            rows[index]?.EnableInClassList("gs-vital--critical", level == VitalLevel.Critical);

            // Written from the same level the colours came from, so the two can never disagree.
            if (markers[index] != null)
            {
                markers[index].text = level switch
                {
                    VitalLevel.Critical => UiText.Get(Keys.MarkerCritical),
                    VitalLevel.Low => UiText.Get(Keys.MarkerLow),
                    _ => string.Empty,
                };
            }
        }

        void ClearRow(int index)
        {
            if (values[index] != null)
                values[index].text = string.Empty;

            if (markers[index] != null)
                markers[index].text = string.Empty;

            if (fills[index] != null)
                fills[index].style.width = Length.Percent(0f);
        }

        protected override void OnAwake() => BindFromScene();

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            for (int i = 0; i < RowCount; i++)
            {
                string prefix = "bv-vital-" + RowNames[i];
                rows[i] = Require<VisualElement>(root, prefix, ref allFound);
                markers[i] = Require<Label>(root, prefix + "-marker", ref allFound);
                values[i] = Require<Label>(root, prefix + "-value", ref allFound);
                fills[i] = Require<VisualElement>(root, prefix + "-fill", ref allFound);
            }

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
            for (int i = 0; i < RowCount; i++)
            {
                rows[i] = null;
                markers[i] = null;
                values[i] = null;
                fills[i] = null;
            }
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

        void OnHealthChanged() => Refresh();

        void OnSelectedLocaleChanged(Locale locale)
        {
            InvalidateDisplayCache();
            Refresh();
        }

        void InvalidateDisplayCache()
        {
            for (int i = 0; i < RowCount; i++)
            {
                lastValues[i] = int.MinValue;
                lastMaxima[i] = int.MinValue;
                lastLevelValid[i] = false;
            }

            lastSurvivalVitalsPresentValid = false;
        }
    }
}
