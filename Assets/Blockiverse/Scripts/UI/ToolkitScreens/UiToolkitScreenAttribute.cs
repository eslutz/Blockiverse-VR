using System;

namespace Blockiverse.UI
{
    // How the host places a screen's panel in the world. Values mirror the headset-validated
    // uGUI presenter behaviour (menu-migration-matrix §4, items 11–13).
    public enum UiToolkitPlacementProfile
    {
        // Routed anchored menu: WorldFixed at the spawn-relative title pose outside a
        // session, LazyFollow (30° / 1.5 m thresholds, 0.35 s glide) inside one. Navigating
        // between anchored menus inherits the visible menu's pose rather than recentering.
        Menu = 0,

        // Gameplay HUD family: rig-relative at a fixed local pose (1.15 m forward,
        // −0.30 m vertical, 12° pitch), never recentered.
        Hud = 1,

        // World-loading overlay: placed like Menu but never accepts input.
        Overlay = 2,

        // Wrist-anchored transient panel: parented to the SUPPORT hand (the one that does not
        // aim), so the dominant hand's ray can reach it. Shown only while the player turns that
        // wrist toward their face — the "check your watch" gesture — and collapsed the rest of the
        // time.
        //
        // Exists because the FPV report is explicit that inventory-and-crafting entry points
        // "belong in routed screens", not in permanent chrome, and live validation confirmed the
        // old always-on action bar read as visual weight nobody looked at. HudLocal* and the
        // rotation fields below are interpreted relative to the hand anchor rather than the head.
        Wrist = 3,
    }

    // Declares a UI Toolkit screen controller to the generation and hosting layers. The
    // bootstrapper enumerates these attributes (TypeCache) to generate one world-space
    // UIDocument panel per screen in the Boot scene; UiToolkitMenuHost indexes the
    // instantiated controllers by ScreenId. Adding a screen therefore never edits the
    // bootstrapper or the host — the attribute is the whole registration.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UiToolkitScreenAttribute : Attribute
    {
        public UiToolkitScreenAttribute(
            string screenId,
            string documentAssetPath,
            int widthPixels,
            int heightPixels,
            UiToolkitPlacementProfile placementProfile)
        {
            ScreenId = screenId;
            DocumentAssetPath = documentAssetPath;
            WidthPixels = widthPixels;
            HeightPixels = heightPixels;
            PlacementProfile = placementProfile;
        }

        // A MenuActions screen-id constant, verbatim (ADR 0010 §4).
        public string ScreenId { get; }

        // Project-relative UXML path, e.g. "Assets/Blockiverse/UI/Documents/TitleScreen.uxml".
        public string DocumentAssetPath { get; }

        // Panel size in document pixels. Physical size follows the world-space formula:
        // metres = pixels / PanelSettings.pixelsPerUnit × transform.localScale
        // (project-wide: 100 ppu, 0.1 scale, so 1000 px = 1.00 m).
        public int WidthPixels { get; }
        public int HeightPixels { get; }

        public UiToolkitPlacementProfile PlacementProfile { get; }

        // Hud- and Wrist-profile panels are parented at this local pose relative to their ANCHOR
        // (the head for Hud, the support hand for Wrist)
        // (metres / degrees) instead of being world-placed — the uGUI HUD behaves the same
        // way: rig-relative, never recentered. Ignored for other profiles. Defaults put a
        // panel 1.15 m forward at chest-to-eye height with a 12° downward tilt.
        public float HudLocalX { get; set; } = 0f;
        public float HudLocalY { get; set; } = 0f;   // eye level: HudLocal* are HEAD-relative
        public float HudLocalZ { get; set; } = 1.15f;
        public float HudPitchDegrees { get; set; } = 12f;

        // Anchor-local yaw and roll. The HUD family needs neither (a panel that faces the player
        // head-on wants pitch only), but a wrist panel does: it has to lie along the forearm and
        // tilt toward the face rather than sit axis-aligned in front of the hand.
        public float HudYawDegrees { get; set; }
        public float HudRollDegrees { get; set; }

        // A panel whose every element is picking-mode Ignore gets NO collider at all: the
        // HUD-family strips share the routed gameplay screen, and an enabled trigger collider
        // on the interaction layer would sit in front of the player's face intercepting the
        // XRI UI ray the whole session (validator: DocumentColliderMissing exempts nothing,
        // but a read-only strip blocking rays is the worse failure).
        public bool NonInteractive { get; set; }
    }
}
