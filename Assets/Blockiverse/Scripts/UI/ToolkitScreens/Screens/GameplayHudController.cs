using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // The action bar of the gameplay HUD (matrix row 21): panel-open shortcuts into the routed
    // inventory / crafting / shared-crate / block-catalog screens.
    //
    // These four buttons are the ONLY way into inventory and crafting — neither has a controller
    // binding, which is why the Controller Map popup does not mention them. That makes this the
    // one HUD panel that must stay reachable by the XRI ray.
    //
    // The health readout used to live here too, in one panel at the Hud profile's default
    // placement: dead centre, 1.15 m ahead, eye height. Eric's report was that the HUD sat in the
    // middle of his view and made things hard to see. It is now two panels — GameplayStatsController
    // top-right, this bar low and tilted up — because a HUD belongs at the perimeter and one
    // document cannot be in two corners.
    //
    // Both halves keep the same screen id: the host maps many controllers onto one route, which is
    // how MiningProgress and StatusToast already share it.
    // ── Moved off the HUD onto the wrist (2026-08-25) ────────────────────────
    //
    // This was a 590x150 bar pinned across the lower-centre of view for the entire session, sitting
    // directly under the hotbar. Live validation in the simulator reported the two "read as a
    // continuous block, not two separable things", and that nothing in an hour of play ever made
    // the tester want to look at it — it registered as visual weight, not information. Eric raised
    // the same complaint independently, and the FPV report is explicit that inventory and crafting
    // entry points "belong in routed screens", not in permanent chrome.
    //
    // So it is now a transient panel on the SUPPORT wrist, shown by turning that wrist toward your
    // face and collapsed otherwise. Central vision keeps the world; the panel appears where the
    // player asks for it.
    //
    // ── Sizing ───────────────────────────────────────────────────────────────
    //
    // 300x350, one column of four full-width buttons. NOT the 240x240 2x2 grid this was first
    // written as — that does not fit, and the arithmetic is worth recording because it is invisible
    // until something renders:
    //
    //   240 panel - 48 (.hs-screen padding, 24 a side) = 192 content
    //   two per row with 5 px margins  -> 86 px a button
    //   86 - 44 (.hs-button padding)   -> 42 px of text at a 28 px font
    //
    // "Inventory" needs about 130 px. Every label would have wrapped or clipped. One column at
    // 300 wide gives 242 px a button and 198 px of text, which clears the longest label
    // ("Shared crate", ~165 px) with room to spare, and 350 tall fits four 64 px controls plus
    // their margins.
    //
    // 5 px margins top and bottom put 10 mm between adjacent targets, inside the report's 8-12 mm
    // minimum — flagged during live validation as something the old row may have violated between
    // "Crafting" and "Shared crate".
    //
    // ── These offsets NEED live tuning ───────────────────────────────────────
    //
    // The local pose below is reasoned, not measured: 6 cm above the controller origin, 10 cm back
    // toward the elbow, pitched 50 deg. It is also authored against the GRIP pose — the anchor is
    // driven by devicePosition/deviceRotation, not the aim ray, and on Quest hardware those differ
    // by a large angle (the ray origin is a separate child transform). Whether this lands
    // comfortably on a forearm can only be judged in a headset; it is the one part of this change
    // no test can settle, and it should be measured rather than argued about.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/GameplayHud.uxml",
        300, 350, UiToolkitPlacementProfile.Wrist,
        HudLocalX = 0f, HudLocalY = 0.06f, HudLocalZ = -0.10f, HudPitchDegrees = 50f)]
    public sealed class GameplayHudController : UiToolkitScreenController
    {
        Button openInventoryButton;
        Button openCraftingButton;
        Button openCrateButton;
        Button openCatalogButton;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        // ── Wrist gesture ────────────────────────────────────────────────────
        //
        // Shown while the panel faces the player's head, which is what turning your wrist over to
        // look at a watch produces. One dot product per frame.
        //
        // TWO thresholds, not one. A single threshold makes the panel flicker on and off while the
        // player holds their hand near the boundary — the worst possible behaviour for the only
        // route into inventory. It opens at ~53 deg and does not close until ~63 deg.
        public const float ShowFacingDot = 0.60f;
        public const float HideFacingDot = 0.45f;

        // Polled rather than evented: there is no wrist-orientation event to subscribe to, and a
        // dot product is cheaper than the machinery that would deliver one.
        const float GesturePollIntervalSeconds = 0.05f;

        const string CollapsedClass = "gh-actions--collapsed";

        VisualElement actionsBody;
        Transform headAnchor;
        bool facing;
        float nextGesturePollTime;

        public bool IsWristMenuOpen => facing;

        // Consumed by the base class to gate the collider: an invisible panel strapped to a moving
        // hand must not sweep an interaction volume through everything the dominant hand points at.
        protected override bool AcceptsInputNow => facing;

        // Test seam: EditMode has no rig, so the head anchor is injectable.
        public void ConfigureHeadAnchor(Transform head)
        {
            headAnchor = head;
            EvaluateGesture(force: true);
        }

        void Update()
        {
            if (!IsVisible || Time.unscaledTime < nextGesturePollTime)
                return;

            nextGesturePollTime = Time.unscaledTime + GesturePollIntervalSeconds;
            EvaluateGesture(force: false);
        }

        public void EvaluateGesture(bool force)
        {
            if (headAnchor == null)
            {
                Camera main = Camera.main;
                headAnchor = main != null ? main.transform : null;
            }

            bool nextFacing = facing;

            if (headAnchor == null)
            {
                // No head to measure against. Fail OPEN, not closed: this panel is the only route
                // into inventory and crafting, and a closed panel with no way to open it strands
                // the player with no way to manage items. A panel that is wrongly visible is a
                // cosmetic problem; one that is wrongly unreachable is not recoverable in-session.
                nextFacing = true;
            }
            else
            {
                Vector3 toHead = headAnchor.position - transform.position;

                if (toHead.sqrMagnitude > 1e-6f)
                {
                    // MINUS forward. In this project a world-space panel is readable when its
                    // forward points AWAY from the viewer — BlockiversePanelPlacement puts a panel
                    // in front of the player and rotates it LookRotation(away), and AttachHudPanel
                    // parents every HUD panel at +Z in front of the head with Euler(pitch, 0, 0).
                    // Those panels render readable, so the readable face normal is -forward.
                    //
                    // Dotting with +forward asks "is the player looking at this panel's BACK", which
                    // is never true during the gesture: turning the wrist over brings the readable
                    // face round, driving Dot(+forward, toHead) to about -0.77 — so the menu stayed
                    // shut, its collider stayed disabled, and the only route to Shared crate and the
                    // block catalog was closed for the whole session. Nothing failed, because no
                    // test covers the geometry.
                    float alignment = Vector3.Dot(-transform.forward, toHead.normalized);

                    // Hysteresis: which threshold applies depends on the state we are in.
                    nextFacing = facing
                        ? alignment > HideFacingDot
                        : alignment > ShowFacingDot;
                }
            }

            if (!force && nextFacing == facing)
                return;

            facing = nextFacing;
            actionsBody?.EnableInClassList(CollapsedClass, !facing);
            Root?.EnableInClassList("hs-screen--unpainted", !facing);
            RefreshInputCollider();
        }

        protected override void OnShown()
        {
            EvaluateGesture(force: true);
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

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            actionsBody = Require<VisualElement>(root, "bv-hud-actions", ref allFound);
            openInventoryButton = Require<Button>(root, "bv-hud-open-inventory", ref allFound);
            openCraftingButton = Require<Button>(root, "bv-hud-open-crafting", ref allFound);
            openCrateButton = Require<Button>(root, "bv-hud-open-crate", ref allFound);
            openCatalogButton = Require<Button>(root, "bv-hud-open-catalog", ref allFound);

            // force: the elements are brand-new and carry NO classes, while `facing` still holds
            // whatever it was before the rebuild. EvaluateGesture early-returns when the value has
            // not changed, so without forcing here a re-attach while the menu happened to be
            // collapsed would leave the fresh tree uncollapsed — the wrist menu rendering open,
            // permanently, regardless of where the player's wrist is.
            EvaluateGesture(force: true);
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

            // No locale subscription here any more: every label on this panel is a static UXML
            // binding, which updates natively. Only the stats readout writes dynamic text.
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
        }

        protected override void OnDetach()
        {
            actionsBody = null;
            openInventoryButton = null;
            openCraftingButton = null;
            openCrateButton = null;
            openCatalogButton = null;
        }
    }
}
