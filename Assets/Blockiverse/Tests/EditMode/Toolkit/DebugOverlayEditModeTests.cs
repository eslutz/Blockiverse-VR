using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // The diagnostic readout, the comfort view anchor, and the Settings row that toggles the
    // readout.
    //
    // Both panels are visibility-driven by a SETTING rather than by the route, which is the thing
    // most worth pinning: every other HUD-family panel appears with the gameplay route, and these
    // two must additionally stay collapsed until the player asks for them. Failing open would put a
    // twelve-line debug block, or a dot, permanently in the middle of a shipped game.
    public sealed class DebugOverlayEditModeTests
    {
        // Satisfies RegisterFrontend so a router exists. Records nothing: this suite asserts on the
        // comfort setting the action writes, not on anything pushed to a frontend.
        sealed class InertFrontend : IBlockiverseMenuFrontend
        {
            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions) { }
            public void SetScreenStatus(string screenId, string message) { }
            public void SetSaveList(IEnumerable<WorldSaveSummary> saves) { }
            public void ShowWorldDetails(WorldSaveSummary save) { }
            public void SetTitleMenuPose(Pose pose) { }
            public void RefreshCreativeEnvironmentControls() { }
            public void CycleHotbarSlot(int delta) { }
            public void ResetNewWorldScreen() { }
            public NewWorldConfig PendingNewWorldConfig => null;
            public WorldSaveSummary? PendingLoadSave => null;
            public WorldSaveSummary? PendingDetailsSave => null;
            public string PendingDetailsRenameText => string.Empty;
            public bool IsStationOpenAt(Blockiverse.Voxel.BlockPosition position) => false;
            public void CloseStationView() { }
        }

        // Records every SetActionMenu push, keyed by screen id, so a test can inspect what a route
        // was actually pushed with rather than only what MenuActions.Settings(bool) would produce
        // in isolation.
        //
        // A standalone implementation, not an InertFrontend subclass: SetActionMenu is not virtual,
        // and BlockiverseMenuController holds its frontend through the IBlockiverseMenuFrontend
        // interface, so a `new`-hiding override in a derived class would never actually be called —
        // the interface dispatch stays bound to whichever class first implemented the member.
        sealed class RecordingFrontend : IBlockiverseMenuFrontend
        {
            public readonly Dictionary<string, (string title, IReadOnlyList<MenuAction> actions)> Pushed = new();

            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions) =>
                Pushed[screenId] = (title, actions);

            public void SetScreenStatus(string screenId, string message) { }
            public void SetSaveList(IEnumerable<WorldSaveSummary> saves) { }
            public void ShowWorldDetails(WorldSaveSummary save) { }
            public void SetTitleMenuPose(Pose pose) { }
            public void RefreshCreativeEnvironmentControls() { }
            public void CycleHotbarSlot(int delta) { }
            public void ResetNewWorldScreen() { }
            public NewWorldConfig PendingNewWorldConfig => null;
            public WorldSaveSummary? PendingLoadSave => null;
            public WorldSaveSummary? PendingDetailsSave => null;
            public string PendingDetailsRenameText => string.Empty;
            public bool IsStationOpenAt(Blockiverse.Voxel.BlockPosition position) => false;
            public void CloseStationView() { }
        }

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

        TController CreateScreen<TController>() where TController : UiToolkitScreenController
        {
            var gameObject = new GameObject(typeof(TController).Name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<TController>();
        }

        BlockiverseComfortSettings CreateSettings()
        {
            var gameObject = new GameObject("Comfort Settings Under Test");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseComfortSettings>();
        }

        static VisualElement AttachFreshTree(UiToolkitScreenController controller)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controller.GetType(), typeof(UiToolkitScreenAttribute));
            Assert.That(attribute, Is.Not.Null);

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return root;
        }

        // Resolves the element a controller actually collapses. Two traps live here, and this
        // suite hit both:
        //
        //  1. tree.Instantiate() returns a TemplateContainer WRAPPING the document root, so
        //     asserting a class on the returned element tests something nothing paints.
        //  2. NOT bv-screen-root. Both controllers deliberately collapse a CHILD, because the base
        // class writes an inline style.display onto bv-screen-root and an inline style outranks
        // every USS rule in UI Toolkit — a hidden class on the root would never apply. Asserting
        // on the root would therefore test an element nothing paints.
        static VisualElement CollapsingElementOf(VisualElement instantiated, string name)
        {
            VisualElement target = instantiated.Q(name);
            Assert.That(target, Is.Not.Null, $"{name} is missing from the document.");
            return target;
        }

        // ── Default-off ──────────────────────────────────────────────────────

        // The single most important property of both panels. A diagnostic block that shipped
        // visible would be in front of every player who never opens Settings.
        [Test]
        public void DebugOverlayIsHiddenUntilTheSettingIsOn()
        {
            GameplayDebugController controller = CreateScreen<GameplayDebugController>();
            VisualElement body = CollapsingElementOf(AttachFreshTree(controller), "bv-debug-body");
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.DebugOverlayEnabled, Is.False, "The setting must default to off.");

            controller.ConfigureComfortSettings(settings);

            Assert.That(body.ClassListContains("dbg-body--hidden"), Is.True);
            Assert.That(controller.IsOverlayVisible, Is.False);

            settings.DebugOverlayEnabled = true;
            controller.ConfigureComfortSettings(settings);

            Assert.That(body.ClassListContains("dbg-body--hidden"), Is.False);
            Assert.That(controller.IsOverlayVisible, Is.True);
        }

        [Test]
        public void ViewAnchorIsHiddenUntilTheSettingIsOn()
        {
            ViewAnchorController controller = CreateScreen<ViewAnchorController>();
            VisualElement dot = CollapsingElementOf(AttachFreshTree(controller), "bv-view-anchor");
            BlockiverseComfortSettings settings = CreateSettings();

            Assert.That(settings.ViewAnchorEnabled, Is.False, "The setting must default to off.");

            controller.ConfigureComfortSettings(settings);
            Assert.That(dot.ClassListContains("va-dot--hidden"), Is.True);

            settings.ViewAnchorEnabled = true;
            controller.ConfigureComfortSettings(settings);
            Assert.That(dot.ClassListContains("va-dot--hidden"), Is.False);
        }

        // Absent settings must fail CLOSED. The failure mode of failing open is a permanent
        // overlay the player never asked for and cannot find a switch for.
        [Test]
        public void BothPanelsFailClosedWithoutSettings()
        {
            GameplayDebugController debug = CreateScreen<GameplayDebugController>();
            VisualElement debugBody = CollapsingElementOf(AttachFreshTree(debug), "bv-debug-body");
            debug.ConfigureComfortSettings(null);
            Assert.That(debugBody.ClassListContains("dbg-body--hidden"), Is.True);
            Assert.That(debug.IsOverlayVisible, Is.False);

            ViewAnchorController anchor = CreateScreen<ViewAnchorController>();
            VisualElement anchorDot = CollapsingElementOf(AttachFreshTree(anchor), "bv-view-anchor");
            anchor.ConfigureComfortSettings(null);
            Assert.That(anchorDot.ClassListContains("va-dot--hidden"), Is.True);
        }

        // ── Content ──────────────────────────────────────────────────────────

        // Refresh must survive a scene with none of its sources present — the overlay is bound by
        // discovery, and an unbound field has to print a placeholder rather than throw.
        [Test]
        public void RefreshWithoutAWorldRendersPlaceholdersRatherThanThrowing()
        {
            GameplayDebugController controller = CreateScreen<GameplayDebugController>();
            VisualElement root = AttachFreshTree(controller);
            BlockiverseComfortSettings settings = CreateSettings();
            settings.DebugOverlayEnabled = true;
            controller.ConfigureComfortSettings(settings);

            Assert.DoesNotThrow(() => controller.Refresh());

            foreach (string line in new[]
            {
                "bv-debug-position", "bv-debug-chunk", "bv-debug-facing", "bv-debug-biome",
                "bv-debug-target", "bv-debug-time", "bv-debug-weather", "bv-debug-climate",
                "bv-debug-place", "bv-debug-session", "bv-debug-perf", "bv-debug-world",
            })
            {
                Assert.That(root.Q<Label>(line), Is.Not.Null, $"{line} is missing from the document.");
            }

            // Session and performance do not depend on a world, so they carry real content even in
            // an empty scene. If these were blank the readout would be reporting nothing at all.
            Assert.That(root.Q<Label>("bv-debug-session").text, Does.Contain("session"));
            Assert.That(root.Q<Label>("bv-debug-perf").text, Does.Contain("frame"));
        }

        // The overlay's own cost is part of its contract: it reports frame time, so it must not be
        // the reason frame time moved. A second refresh with nothing changed must assign no text.
        [Test]
        public void RepeatedRefreshWithUnchangedStateRewritesNothing()
        {
            GameplayDebugController controller = CreateScreen<GameplayDebugController>();
            VisualElement root = AttachFreshTree(controller);
            BlockiverseComfortSettings settings = CreateSettings();
            settings.DebugOverlayEnabled = true;
            controller.ConfigureComfortSettings(settings);

            controller.Refresh();

            // Asserted on the controller's own write counter, NOT on label.text reference equality.
            // UI Toolkit's TextElement setter returns early when the incoming string is equal, so
            // the old reference survives whether or not the gate exists — the reference-equality
            // form of this test passed with the gate deleted, which is to say it tested nothing.
            int writesAfterFirst = controller.TextWriteCount;

            Assert.That(writesAfterFirst, Is.GreaterThan(0),
                "The first refresh wrote no lines at all — positive control failed, so a zero " +
                "delta below would prove nothing.");

            controller.Refresh();

            Assert.That(controller.TextWriteCount, Is.EqualTo(writesAfterFirst),
                "A refresh with nothing changed still assigned text. Every assignment allocates in " +
                "retained mode, on the one panel whose job is to report allocation.");

            // The gate must not be a permanent mute. Re-attaching hands the controller brand-new
            // element instances whose text is empty, so the cache MUST be invalidated or the fresh
            // labels stay blank for the rest of the session — the failure mode a last-value cache
            // introduces if OnAttach forgets to clear it.
            VisualElement rebuilt = AttachFreshTree(controller);
            controller.Refresh();

            Assert.That(controller.TextWriteCount, Is.GreaterThan(writesAfterFirst),
                "A rebuilt element tree was not repainted — the cache outlived the elements it " +
                "described, so every line would render blank.");
            Assert.That(rebuilt.Q<Label>("bv-debug-session").text, Is.Not.Empty);
        }

        // ── Settings row ─────────────────────────────────────────────────────

        [Test]
        public void SettingsListCarriesTheDebugToggle()
        {
            IReadOnlyList<MenuAction> off = MenuActions.Settings(debugOverlayEnabled: false);

            Assert.That(off.Any(a => a.ActionId == MenuActions.SettingsToggleDebugOverlay), Is.True,
                "The settings list has no debug-overlay row.");

            // Close stays last: it is the way out, and a row appended after it would put the exit
            // in the middle of the list.
            Assert.That(off[^1].ActionId, Is.EqualTo(MenuActions.SettingsClose));
        }

        // DISPATCHES the action through the menu controller, rather than asserting the pure
        // MenuActions.Settings(bool) factory in isolation.
        //
        // The two tests below check only that the factory produces a row. That row could be wired
        // to nothing at all — no case in HandleAction, no setting written — and they would both
        // still pass. This is the test that fails if the toggle is unplugged.
        [Test]
        public void DispatchingTheToggleActionFlipsThePersistedSetting()
        {
            var rig = new GameObject("Menu Rig");
            objectsToDestroy.Add(rig);
            var controller = rig.AddComponent<BlockiverseMenuController>();

            // HandleAction returns immediately while router is null, and the router is created by
            // RegisterFrontend. Without this the dispatch below is a no-op and the test fails
            // claiming the row is unwired when it is only unrouted.
            controller.RegisterFrontend(new InertFrontend());

            BlockiverseComfortSettings settings = CreateSettings();
            typeof(BlockiverseMenuController)
                .GetField("comfortSettings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, settings);

            Assert.That(settings.DebugOverlayEnabled, Is.False, "must start off");

            MethodInfo handleAction = typeof(BlockiverseMenuController)
                .GetMethod("HandleAction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handleAction, Is.Not.Null,
                "HandleAction is the dispatch seam this test exists to exercise.");

            handleAction.Invoke(controller, new object[] { MenuActions.SettingsToggleDebugOverlay });
            Assert.That(settings.DebugOverlayEnabled, Is.True,
                "dispatching the toggle action did not reach the setting — the row is unwired");

            // Idempotent it is not: the row is a toggle, so a second dispatch must turn it back off.
            handleAction.Invoke(controller, new object[] { MenuActions.SettingsToggleDebugOverlay });
            Assert.That(settings.DebugOverlayEnabled, Is.False,
                "the toggle only worked in one direction");
        }

        // The label reports the CURRENT state, so it has to differ between the two. Identical
        // labels would leave the player unable to tell whether the overlay is on without turning
        // it on.
        [Test]
        public void DebugToggleLabelReflectsState()
        {
            MenuAction on = MenuActions.Settings(debugOverlayEnabled: true)
                .First(a => a.ActionId == MenuActions.SettingsToggleDebugOverlay);
            MenuAction off = MenuActions.Settings(debugOverlayEnabled: false)
                .First(a => a.ActionId == MenuActions.SettingsToggleDebugOverlay);

            Assert.That(on.Label, Is.Not.EqualTo(off.Label));
            Assert.That(on.Label, Does.Contain("On"));
            Assert.That(off.Label, Does.Contain("Off"));
        }

        // Flagged by review: the Settings row is populated once by RefreshStaticMenus, which runs
        // from this controller's own Start/RegisterFrontend — but the persisted flag it reads loads
        // in a DIFFERENT component's Start (BlockiverseSettingsPersistence), whose ordering relative
        // to this one is not guaranteed. A save loaded AFTER the first push left the row reading
        // "Off" while the overlay was actually on, and pressing it then turned the overlay off while
        // the label — now genuinely matching the flipped value — looked unchanged to the player.
        [Test]
        public void OpeningSettingsRebuildsTheRowRatherThanTrustingTheEarlierPush()
        {
            var rig = new GameObject("Menu Rig");
            objectsToDestroy.Add(rig);
            var controller = rig.AddComponent<BlockiverseMenuController>();

            var frontend = new RecordingFrontend();
            controller.RegisterFrontend(frontend);

            // The comfort settings object does not exist yet at registration time, so the first
            // push resolves the setting as absent and reads "Off" — this is the race, reproduced.
            (string title, IReadOnlyList<MenuAction> actions) firstPush = frontend.Pushed[MenuActions.SettingsScreen];
            MenuAction firstRow = firstPush.actions.First(a => a.ActionId == MenuActions.SettingsToggleDebugOverlay);
            Assert.That(firstRow.Label, Does.Contain("Off"),
                "positive control: the race must actually reproduce, or the fix below proves nothing");

            // Now the "other component's Start" runs late and loads a saved on-flag.
            BlockiverseComfortSettings settings = CreateSettings();
            settings.DebugOverlayEnabled = true;

            MethodInfo handleAction = typeof(BlockiverseMenuController)
                .GetMethod("HandleAction", BindingFlags.Instance | BindingFlags.NonPublic);
            handleAction.Invoke(controller, new object[] { MenuActions.PauseSettings });

            (string title, IReadOnlyList<MenuAction> actions) secondPush = frontend.Pushed[MenuActions.SettingsScreen];
            MenuAction secondRow = secondPush.actions.First(a => a.ActionId == MenuActions.SettingsToggleDebugOverlay);

            Assert.That(secondRow.Label, Does.Contain("On"),
                "opening Settings must rebuild the row from the current setting, not replay the " +
                "stale push from before the setting finished loading");
        }

        // The support grip's dispatch (formerly the Creative quick block menu's toggle, retired
        // once the catalog screen covered the same job from the wrist menu / this hub). This is
        // the ONE call site for OnScreensPressed, so a broken gate here breaks the entire
        // guaranteed fallback with nothing else to catch it.
        [Test]
        public void ScreensPressedOpensTheHubOnlyOverThePlainGameplayRoute()
        {
            var rig = new GameObject("Menu Rig");
            objectsToDestroy.Add(rig);
            var controller = rig.AddComponent<BlockiverseMenuController>();

            var frontend = new RecordingFrontend();
            controller.RegisterFrontend(frontend);

            MethodInfo onScreensPressed = typeof(BlockiverseMenuController)
                .GetMethod("OnScreensPressed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onScreensPressed, Is.Not.Null,
                "OnScreensPressed is the seam the support grip's input event calls.");

            // Registration leaves the router at the title screen — the grip must be a no-op there,
            // or every title/menu screen would suddenly answer to a gameplay-only button.
            onScreensPressed.Invoke(controller, null);
            Assert.That(frontend.Pushed.ContainsKey(MenuActions.GameplayScreensScreen), Is.False,
                "positive control: the grip must not open the hub off the gameplay route");

            controller.Router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen));
            onScreensPressed.Invoke(controller, null);

            Assert.That(frontend.Pushed.ContainsKey(MenuActions.GameplayScreensScreen), Is.True,
                "pressing the grip over the plain gameplay HUD must open the screens hub");
            Assert.That(controller.Router.ActiveScreen.ScreenId,
                Is.EqualTo(MenuActions.GameplayScreensScreen),
                "the grip must actually PUSH the route, not just refresh the frontend's label");
        }
    }
}
