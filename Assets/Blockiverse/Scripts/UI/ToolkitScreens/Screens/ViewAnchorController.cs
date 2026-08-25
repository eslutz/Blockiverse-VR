using Blockiverse.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The comfort view anchor: one static dot at the centre of vision, off by default.
    //
    // See ViewAnchor.uxml for why this is not an aiming reticle. In short: this game aims with the
    // controller ray, PlacementPreview already marks the target in world space, and a dot in the
    // middle of the view would claim to mark something it does not. What it actually does is give
    // the vestibular system a fixed reference during continuous locomotion, which is a comfort
    // setting and lives beside the vignette.
    //
    // Visibility is the COMFORT SETTING, not the route. Every other HUD-family panel is shown by
    // the router and hidden with it; this one additionally collapses whenever the player has the
    // option off, which is most of the time. Polled rather than evented because
    // BlockiverseComfortSettings raises nothing — the same reason the vitals readout polls.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/ViewAnchor.uxml",
        64, 64, UiToolkitPlacementProfile.Hud,
        HudLocalX = 0f, HudLocalY = 0f, HudLocalZ = 1.10f, HudPitchDegrees = 0f, NonInteractive = true)]
    public sealed class ViewAnchorController : UiToolkitScreenController
    {
        // Cheap enough that a tighter cadence would be pointless: this reads one bool and compares
        // it to the last one.
        public const float SettingPollIntervalSeconds = 0.25f;

        // Applied to the DOT (bv-view-anchor), not to bv-screen-root. The base class writes an
        // inline style.display onto the root when the router shows the HUD, and a UI Toolkit
        // inline style outranks every USS rule, so a hidden class on the root never applies.
        const string HiddenClass = "va-dot--hidden";

        VisualElement anchorDot;
        BlockiverseComfortSettings comfortSettings;

        bool lastEnabled;
        bool lastEnabledValid;
        float nextPollTime;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public bool IsAnchorVisible => lastEnabledValid && lastEnabled;

        // Test seam: bind settings directly rather than discovering them from a scene.
        public void ConfigureComfortSettings(BlockiverseComfortSettings settings)
        {
            comfortSettings = settings;
            lastEnabledValid = false;
            Apply();
        }

        protected override void OnAwake() => BindFromScene();

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            anchorDot = Require<VisualElement>(root, "bv-view-anchor", ref allFound);

            // Brand-new element instances: re-apply from the setting, not from the cache.
            lastEnabledValid = false;
            Apply();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
        }

        protected override void OnUnregisterCallbacks()
        {
        }

        protected override void OnDetach()
        {
            anchorDot = null;
        }

        protected override void OnShown()
        {
            BindFromScene();
            Apply();
        }

        void Update()
        {
            if (!IsVisible || Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + SettingPollIntervalSeconds;
            Apply();
        }

        void BindFromScene()
        {
            comfortSettings ??= BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);
        }

        void Apply()
        {
            if (anchorDot == null)
                return;

            // Absent settings means absent anchor. Failing closed matters here more than usual:
            // the failure mode of failing open is a permanent dot in the middle of the view that
            // the player never asked for and cannot find a switch for.
            bool enabled = comfortSettings != null && comfortSettings.ViewAnchorEnabled;

            if (lastEnabledValid && lastEnabled == enabled)
                return;

            lastEnabled = enabled;
            lastEnabledValid = true;
            anchorDot.EnableInClassList(HiddenClass, !enabled);
        }
    }
}
