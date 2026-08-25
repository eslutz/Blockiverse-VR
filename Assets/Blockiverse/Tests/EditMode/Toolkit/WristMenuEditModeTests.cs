using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // The wrist menu's gesture, and the guaranteed fallback into the screens it owns.
    //
    // Written after an adversarial review found the gesture SIGN-INVERTED with nothing to catch it.
    // In this project a world-space panel is readable when its forward points AWAY from the viewer
    // (BlockiversePanelPlacement poses panels in front of the player with LookRotation(away), and
    // UiToolkitMenuHost.AttachHudPanel parents every HUD panel at +Z in front of the head) — so the
    // readable-face normal is -forward. Dotting against +forward asked whether the player was
    // looking at the panel's BACK, which the watch-check gesture never produces. The menu could not
    // be opened, its collider stayed disabled, and the entire suite stayed green because nothing
    // referenced EvaluateGesture at all.
    //
    // This panel is the only route to the shared crate and the block catalog, so "cannot be opened"
    // is a lockout, not a cosmetic bug. Every test here exists to make that failure loud.
    public sealed class WristMenuEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        (GameplayHudController controller, VisualElement root) CreateMenu()
        {
            var gameObject = new GameObject(nameof(GameplayHudController));
            objectsToDestroy.Add(gameObject);
            var controller = gameObject.AddComponent<GameplayHudController>();

            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(GameplayHudController), typeof(UiToolkitScreenAttribute));
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return (controller, root);
        }

        Transform CreateHeadAt(Vector3 position)
        {
            var gameObject = new GameObject("Head Under Test");
            objectsToDestroy.Add(gameObject);
            gameObject.transform.position = position;
            return gameObject.transform;
        }

        static VisualElement ActionsOf(VisualElement root) => root.Q<VisualElement>("bv-hud-actions");

        static bool Collapsed(VisualElement root) =>
            ActionsOf(root).ClassListContains("gh-actions--collapsed");

        // ── Gesture geometry ─────────────────────────────────────────────────

        // The regression test for the sign inversion. The panel's READABLE face is -forward, so a
        // head sitting on that side must open the menu.
        [Test]
        public void MenuOpensWhenTheReadableFaceIsTurnedTowardTheHead()
        {
            (GameplayHudController controller, VisualElement root) = CreateMenu();

            // Panel at the origin looking down +Z, so its readable face points along -Z.
            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Head on the readable side.
            controller.ConfigureHeadAnchor(CreateHeadAt(new Vector3(0f, 0f, -1f)));

            Assert.That(controller.IsWristMenuOpen, Is.True,
                "Turning the readable face toward the head must open the menu. If this fails the " +
                "dot product is inverted again and the menu cannot be opened at all.");
            Assert.That(Collapsed(root), Is.False);
        }

        // The other half, and the reason the test above is not vacuous: a head behind the panel
        // must NOT open it.
        [Test]
        public void MenuStaysClosedWhenOnlyTheBackIsTurnedTowardTheHead()
        {
            (GameplayHudController controller, VisualElement root) = CreateMenu();

            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            controller.ConfigureHeadAnchor(CreateHeadAt(new Vector3(0f, 0f, 1f)));

            Assert.That(controller.IsWristMenuOpen, Is.False,
                "The player is looking at the panel's back; it must stay collapsed.");
            Assert.That(Collapsed(root), Is.True);
        }

        // Hysteresis. A single threshold flickers the menu on and off while the wrist rests near
        // the boundary, which is the worst possible behaviour for the primary route to inventory.
        //
        // Both states are established EXPLICITLY before the in-between pose is applied. The first
        // draft of this test assumed a freshly attached controller starts closed; it does not —
        // OnAttach evaluates with no head anchor yet, which takes the deliberate fail-OPEN branch.
        // The test failed for that reason rather than for a real hysteresis fault, which is exactly
        // the kind of wrong precondition that makes a test report the wrong thing.
        [Test]
        public void GestureHasHysteresisSoItCannotFlickerAtTheBoundary()
        {
            Assert.That(GameplayHudController.HideFacingDot,
                Is.LessThan(GameplayHudController.ShowFacingDot),
                "The close threshold must be LOWER than the open threshold, or the menu chatters.");

            (GameplayHudController controller, _) = CreateMenu();
            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // An alignment strictly between the two thresholds: the same pose must be refused from
            // closed and held from open. That gap IS the hysteresis.
            float between = 0.5f * (GameplayHudController.ShowFacingDot +
                                    GameplayHudController.HideFacingDot);
            Assert.That(between, Is.GreaterThan(GameplayHudController.HideFacingDot));
            Assert.That(between, Is.LessThan(GameplayHudController.ShowFacingDot));

            // alignment = dot(-forward, toHead); -forward is (0,0,-1) here.
            float angle = Mathf.Acos(between);
            var betweenPosition = new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle));

            // From CLOSED, established explicitly by putting the head behind the panel.
            controller.ConfigureHeadAnchor(CreateHeadAt(new Vector3(0f, 0f, 1f)));
            Assert.That(controller.IsWristMenuOpen, Is.False, "precondition: closed");

            controller.ConfigureHeadAnchor(CreateHeadAt(betweenPosition));
            Assert.That(controller.IsWristMenuOpen, Is.False,
                "Between the thresholds and starting closed, it must stay closed — otherwise the " +
                "open threshold is not being applied and the menu pops early.");

            // From OPEN, established the same way.
            controller.ConfigureHeadAnchor(CreateHeadAt(new Vector3(0f, 0f, -1f)));
            Assert.That(controller.IsWristMenuOpen, Is.True, "precondition: open");

            controller.ConfigureHeadAnchor(CreateHeadAt(betweenPosition));
            Assert.That(controller.IsWristMenuOpen, Is.True,
                "Between the thresholds and already open, it must STAY open. Without that gap the " +
                "menu flickers as the wrist rests near the edge.");
        }

        // Fails OPEN, never closed. A panel that is wrongly visible is cosmetic; one that is
        // wrongly unreachable strands the player with no way to reach their own inventory.
        [Test]
        public void GestureFailsOpenWhenThereIsNoHeadToMeasureAgainst()
        {
            (GameplayHudController controller, VisualElement root) = CreateMenu();
            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            controller.ConfigureHeadAnchor(null);

            // Camera.main may or may not resolve in an EditMode fixture; either way the menu must
            // not end up closed-with-no-way-to-open.
            if (Camera.main == null)
            {
                Assert.That(controller.IsWristMenuOpen, Is.True,
                    "With no head anchor the menu must fail OPEN.");
                Assert.That(Collapsed(root), Is.False);
            }
            else
            {
                Assert.Pass("A scene camera resolved; the fail-open branch is not reachable here.");
            }
        }

        // ── Rebuild ──────────────────────────────────────────────────────────

        // A re-attach hands the controller brand-new elements carrying no classes, while its own
        // `facing` flag still holds the pre-rebuild value. Without a forced re-apply the fresh tree
        // renders the menu open regardless of where the wrist actually is.
        [Test]
        public void RebuildingTheDocumentReappliesTheCollapsedState()
        {
            (GameplayHudController controller, _) = CreateMenu();
            controller.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            controller.ConfigureHeadAnchor(CreateHeadAt(new Vector3(0f, 0f, 1f)));
            Assert.That(controller.IsWristMenuOpen, Is.False, "precondition: closed");

            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(GameplayHudController), typeof(UiToolkitScreenAttribute));
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            VisualElement rebuilt = tree.Instantiate();
            controller.AttachForTest(rebuilt);

            Assert.That(Collapsed(rebuilt), Is.True,
                "The rebuilt tree rendered the wrist menu open while the wrist was turned away.");
        }

        // ── The guaranteed fallback ──────────────────────────────────────────

        // The wrist menu depends on the support controller being tracked. Pause is bound to a
        // dedicated button and always answers, so it carries the last-resort route.
        [Test]
        public void PauseMenuCarriesOneRowIntoTheGameplayScreensHub()
        {
            IReadOnlyList<MenuAction> pause = MenuActions.PauseMenu(
                canToggleMode: true, canOpenCreativeTools: true);

            Assert.That(pause.Any(a => a.ActionId == MenuActions.PauseOpenScreens), Is.True,
                "Pause must offer the screens hub: the wrist menu needs a tracked support " +
                "controller, and without a fallback a dropped controller locks the player out of " +
                "their own inventory.");

            // ONE row, not one per destination — that is the whole point of the hub. If a future
            // change starts adding destinations straight to the pause menu, this fails.
            Assert.That(
                pause.Count(a => a.ActionId.StartsWith("gameplay_screens.", StringComparison.Ordinal)),
                Is.Zero,
                "Gameplay screens belong behind the hub row, not in the pause menu itself.");

            // Resume stays first — a fallback row must not displace the way out of the menu.
            Assert.That(pause[0].ActionId, Is.EqualTo(MenuActions.PauseResume));
        }

        // The hub must offer everything the wrist menu does. A fallback that offers LESS than the
        // route it stands in for is the one thing it must never be — and the shared crate and the
        // block catalog have no other entry point anywhere in the codebase.
        [Test]
        public void HubOffersEveryDestinationTheWristMenuDoes()
        {
            IReadOnlyList<MenuAction> hub = MenuActions.GameplayScreens();

            foreach (string required in new[]
                     {
                         MenuActions.ScreensOpenInventory,
                         MenuActions.ScreensOpenCrafting,
                         MenuActions.ScreensOpenCrate,
                         MenuActions.ScreensOpenCatalog,
                     })
            {
                Assert.That(hub.Any(a => a.ActionId == required), Is.True,
                    $"{required} is missing from the guaranteed route.");
            }

            // Close stays last: it is the way out, and a row after it puts the exit mid-list.
            Assert.That(hub[^1].ActionId, Is.EqualTo(MenuActions.ScreensClose));
        }

        // ── Placement contract ───────────────────────────────────────────────

        [Test]
        public void WristPanelIsSizedForItsOwnLabels()
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(GameplayHudController), typeof(UiToolkitScreenAttribute));

            // Base.uss: .hs-screen padding 24 a side; .hs-button padding 22 a side.
            const int screenPadding = 24 * 2;
            const int buttonPadding = 22 * 2;
            const int buttonMargin = 5 * 2;

            int textWidth = attribute.WidthPixels - screenPadding - buttonMargin - buttonPadding;

            // "Shared crate" is the longest label, ~165 px at the 28 px control font. The first
            // draft of this panel was 240 px wide in a 2x2 grid, which left 42 px — every label
            // would have wrapped or clipped, and no test would have noticed.
            Assert.That(textWidth, Is.GreaterThanOrEqualTo(170),
                $"{attribute.WidthPixels} px leaves only {textWidth} px of text; the longest " +
                "label needs about 165 px.");

            // Four stacked 64 px controls plus their margins.
            int contentHeight = attribute.HeightPixels - screenPadding;
            Assert.That(contentHeight, Is.GreaterThanOrEqualTo(4 * (64 + buttonMargin)),
                "Not tall enough for four full-height controls; the last one would be clipped.");
        }
    }
}
