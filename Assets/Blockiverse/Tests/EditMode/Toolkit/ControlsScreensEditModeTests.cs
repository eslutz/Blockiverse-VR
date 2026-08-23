using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Migration matrix rows 5 and 13: the first-run controller-mapping screen and the
    // settings-hub controls reference, both showing the canonical mapping copy with a single
    // Close. The uGUI oracles are MenuRuntimeWiringEditModeTests' controller-mapping suite
    // (seen-flag + depth-aware close routing) — re-asserted here through the UI Toolkit
    // controllers against their real instantiated documents.
    public sealed class ControlsScreensEditModeTests
    {
        const string ControllerMappingDocumentPath = "Assets/Blockiverse/UI/Documents/ControllerMappingScreen.uxml";
        const string ControlsDocumentPath = "Assets/Blockiverse/UI/Documents/ControlsScreen.uxml";

        // First and last lines of the canonical 11-line mapping copy
        // (BlockiverseProjectBootstrapper.GameMenus.cs, ControllerMappingText). Asserting both
        // ends pins that the whole block arrived, not a truncation.
        const string FirstMappingLine = "Dominant trigger: press UI / break";
        const string LastMappingLine = "Either stick hold up: teleport aim, release to land";

        sealed class RecordingLogSink : IBlockiverseLogSink
        {
            public readonly List<BlockiverseLogEntry> Entries = new();

            public void Log(BlockiverseLogEntry entry) => Entries.Add(entry);
        }

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        static VisualElement InstantiateDocument(string path)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(tree, Is.Not.Null, $"Document missing from disk: '{path}'.");
            return tree.Instantiate();
        }

        // The host GameObject stays INACTIVE so the host never runs Awake/OnEnable — no scene
        // scans, no frontend registration. The screen controllers only need Host.MenuController,
        // which is plain field access; the menu controller must be active because its Awake
        // builds the router.
        (UiToolkitMenuHost host, BlockiverseMenuController menuController) CreateMenuStack()
        {
            var controllerObject = new GameObject("Menu Controller Under Test");
            objectsToDestroy.Add(controllerObject);
            BlockiverseMenuController menuController = controllerObject.AddComponent<BlockiverseMenuController>();

            var hostObject = new GameObject("Menu Host Under Test");
            objectsToDestroy.Add(hostObject);
            hostObject.SetActive(false);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);

            return (host, menuController);
        }

        T CreateScreen<T>(UiToolkitMenuHost host) where T : UiToolkitScreenController
        {
            var screenObject = new GameObject(typeof(T).Name + " Under Test");
            objectsToDestroy.Add(screenObject);
            T screen = screenObject.AddComponent<T>();

            if (host != null)
                screen.ConfigureHost(host);

            return screen;
        }

        [Test]
        public void ControllerMappingScreenBindsItsDocumentAndRendersTheCanonicalCopy()
        {
            ControllerMappingScreenController screen = CreateScreen<ControllerMappingScreenController>(host: null);
            VisualElement root = InstantiateDocument(ControllerMappingDocumentPath);

            screen.AttachForTest(root);

            Assert.That(screen.IsBound, Is.True,
                "Element names drifted between ControllerMappingScreenController and its UXML.");

            // Byte-parity pin: the authored preview text must equal the en table values.
            Assert.That(root.Q<Label>("bv-mapping-title").text, Is.EqualTo("Controller Map"));
            Assert.That(root.Q<Button>(ControllerMappingScreenController.CloseButtonElementName).text,
                Is.EqualTo("Close"));

            Label body = root.Q<Label>(ControllerMappingScreenController.BodyElementName);
            Assert.That(body.text, Is.Not.Empty);
            Assert.That(body.text, Does.Contain(FirstMappingLine),
                $"The body must carry the canonical mapping copy. If it shows the raw key, the " +
                $"'{ControllerMappingScreenController.BodyTextKey}' table entry has not been added centrally yet.");
            Assert.That(body.text, Does.Contain(LastMappingLine));
        }

        [Test]
        public void ControlsScreenBindsItsDocumentAndRendersTheCanonicalCopy()
        {
            ControlsScreenController screen = CreateScreen<ControlsScreenController>(host: null);
            VisualElement root = InstantiateDocument(ControlsDocumentPath);

            screen.AttachForTest(root);

            Assert.That(screen.IsBound, Is.True,
                "Element names drifted between ControlsScreenController and its UXML.");

            Assert.That(root.Q<Label>("bv-controls-title").text, Is.EqualTo("Controls"));
            Assert.That(root.Q<Button>(ControlsScreenController.CloseButtonElementName).text,
                Is.EqualTo("Close"));

            Label body = root.Q<Label>(ControlsScreenController.BodyElementName);
            Assert.That(body.text, Is.Not.Empty);
            Assert.That(body.text, Does.Contain(FirstMappingLine),
                $"The body must carry the canonical mapping copy. If it shows the raw key, the " +
                $"'{ControlsScreenController.BodyTextKey}' table entry has not been added centrally yet.");
            Assert.That(body.text, Does.Contain(LastMappingLine));
        }

        // uGUI oracle: MenuRuntimeWiringEditModeTests.ControllerMappingRouteOwnsFirstLaunchBeforeTitleMenu.
        // First-run shape — controller_mapping IS the root, so closing must ClearToRoot(title)
        // and set the seen flag. Both effects belong to CloseControllerMappingScreen; this test
        // proves the screen's Close reaches that verb.
        [Test]
        public void ControllerMappingCloseRunsTheFirstRunVerbOnTheMenuController()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                (UiToolkitMenuHost host, BlockiverseMenuController menuController) = CreateMenuStack();
                ControllerMappingScreenController screen = CreateScreen<ControllerMappingScreenController>(host);
                screen.AttachForTest(InstantiateDocument(ControllerMappingDocumentPath));

                menuController.Router.ClearToRoot(new ScreenRoute(MenuActions.ControllerMappingScreen, pauseGame: true));

                // Negative control: nothing may close the screen or stamp the flag before the click.
                Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.Zero);

                screen.SimulateClose();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1),
                    "Closing the first-run mapping screen must stamp the seen flag (via the controller verb).");
                Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(menuController.Router.ScreenDepth, Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        // The verb's other branch: pushed over an existing stack (depth > 1) it pops exactly
        // one screen instead of clearing to the title root.
        [Test]
        public void ControllerMappingClosePopsWhenStackedInsteadOfClearingToRoot()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                (UiToolkitMenuHost host, BlockiverseMenuController menuController) = CreateMenuStack();
                ControllerMappingScreenController screen = CreateScreen<ControllerMappingScreenController>(host);
                screen.AttachForTest(InstantiateDocument(ControllerMappingDocumentPath));

                menuController.Router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                menuController.Router.PushScreen(new ScreenRoute(MenuActions.ControllerMappingScreen, pauseGame: true));

                screen.SimulateClose();

                Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen),
                    "With depth > 1 the close must pop back to the screen underneath, not clear to the title.");
                Assert.That(menuController.Router.ScreenDepth, Is.EqualTo(2));
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        // uGUI parity: the settings hub pushes controls, and the panel's Close persistent
        // listener runs HandleAction(controls.close), which pops. The toolkit screen reaches
        // the same switch case through DispatchAction.
        [Test]
        public void ControlsCloseDispatchesTheCanonicalActionAndPops()
        {
            (UiToolkitMenuHost host, BlockiverseMenuController menuController) = CreateMenuStack();
            ControlsScreenController screen = CreateScreen<ControlsScreenController>(host);
            screen.AttachForTest(InstantiateDocument(ControlsDocumentPath));

            menuController.DispatchAction(MenuActions.SettingsOpenControls);

            // Negative control: the route in must land on controls before the click.
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControlsScreen));
            Assert.That(menuController.Router.IsGamePaused, Is.True);

            screen.SimulateClose();

            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            Assert.That(menuController.Router.ScreenDepth, Is.EqualTo(1));
        }

        // Defensive control shared by both screens: without a host there is no menu
        // controller, so Close must be inert — and in particular the mapping screen must NOT
        // write the seen flag itself. The flag belongs to
        // BlockiverseMenuController.CloseControllerMappingScreen (matrix §4 item 18); a screen
        // that stamps it locally would pass the close tests above while silently breaking the
        // first-run flow when routing is refused.
        [Test]
        public void CloseWithoutAHostIsInertAndNeverStampsTheSeenFlag()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                ControllerMappingScreenController mapping = CreateScreen<ControllerMappingScreenController>(host: null);
                mapping.AttachForTest(InstantiateDocument(ControllerMappingDocumentPath));

                Assert.DoesNotThrow(mapping.SimulateClose);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.Zero,
                    "The seen flag is the menu controller's to write, never the screen's.");

                ControlsScreenController controls = CreateScreen<ControlsScreenController>(host: null);
                controls.AttachForTest(InstantiateDocument(ControlsDocumentPath));

                Assert.DoesNotThrow(controls.SimulateClose);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        // Sabotage control for the binding checks: IsBound=true against the WRONG document
        // would mean Require is not actually checking element names, and every IsBound
        // assertion above would be vacuous. The sink swap keeps the deliberate loud errors
        // out of the Unity console, where the test runner would fail the test on them.
        [Test]
        public void AControllerAttachedToTheWrongDocumentReportsUnbound()
        {
            var sink = new RecordingLogSink();
            BlockiverseLog.SetSinkForTesting(sink);

            try
            {
                ControlsScreenController screen = CreateScreen<ControlsScreenController>(host: null);
                screen.AttachForTest(InstantiateDocument(ControllerMappingDocumentPath));

                Assert.That(screen.IsBound, Is.False,
                    "ControlsScreenController claimed to bind against ControllerMappingScreen.uxml — " +
                    "element lookups are not being verified.");
                Assert.That(sink.Entries, Has.Some.Matches<BlockiverseLogEntry>(
                    entry => entry.Level == LogType.Error),
                    "A failed bind must be loud, not silent.");
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
            }
        }
    }
}
