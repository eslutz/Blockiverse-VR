using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit mirrors of the ActionMenuEditModeTests behaviours for the four action-list
    // screens (title / pause / death / settings hub). UIDocument never builds a panel in
    // EditMode, so trees are instantiated from the VisualTreeAsset and attached through
    // AttachForTest; clicks are driven through each controller's public PressAction seam
    // because ClickEvent dispatch requires a runtime panel.
    public sealed class ActionMenuScreensEditModeTests
    {
        // Router-initialising stand-in: EditMode never runs Awake, and RegisterFrontend is the
        // public path that initialises the router (same pattern as MenuFrontendSeamEditModeTests).
        sealed class StubMenuFrontend : IBlockiverseMenuFrontend
        {
            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions)
            {
            }

            public void SetScreenStatus(string screenId, string message)
            {
            }

            public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
            {
            }

            public void ShowWorldDetails(WorldSaveSummary save)
            {
            }

            public void SetTitleMenuPose(Pose pose)
            {
            }

            public void RefreshCreativeEnvironmentControls()
            {
            }

            public void ToggleQuickBlockMenu()
            {
            }

            public void HideQuickBlockMenu()
            {
            }

            public void ResetNewWorldScreen()
            {
            }

            public NewWorldConfig PendingNewWorldConfig => null;
            public WorldSaveSummary? PendingLoadSave => null;
            public WorldSaveSummary? PendingDetailsSave => null;
            public string PendingDetailsRenameText => string.Empty;

            public bool IsStationOpenAt(BlockPosition position) => false;

            public void CloseStationView()
            {
            }
        }

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            BlockiverseLocalization.ClearOverridesForTesting();

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

        static VisualElement AttachFreshTree(UiToolkitScreenController controller)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controller.GetType(), typeof(UiToolkitScreenAttribute));
            Assert.That(attribute, Is.Not.Null, $"{controller.GetType().Name} has no [UiToolkitScreen] attribute.");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return root;
        }

        (BlockiverseMenuController menuController, UiToolkitMenuHost host) CreateMenuControllerWithHost()
        {
            var controllerObject = new GameObject("Menu Controller Under Test");
            objectsToDestroy.Add(controllerObject);
            BlockiverseMenuController menuController = controllerObject.AddComponent<BlockiverseMenuController>();
            menuController.RegisterFrontend(new StubMenuFrontend());

            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);
            return (menuController, host);
        }

        [TestCase(typeof(TitleScreenController), MenuActions.TitleScreen)]
        [TestCase(typeof(PauseScreenController), MenuActions.PauseScreen)]
        [TestCase(typeof(DeathScreenController), MenuActions.DeathScreen)]
        [TestCase(typeof(SettingsHubScreenController), MenuActions.SettingsScreen)]
        public void ScreenAttributesDeclareTheCanonicalRoutesAndDocuments(Type controllerType, string expectedScreenId)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controllerType, typeof(UiToolkitScreenAttribute));

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.ScreenId, Is.EqualTo(expectedScreenId));
            Assert.That(attribute.WidthPixels, Is.EqualTo(570));
            Assert.That(attribute.HeightPixels, Is.EqualTo(700));
            Assert.That(attribute.PlacementProfile, Is.EqualTo(UiToolkitPlacementProfile.Menu));

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");
        }

        [Test]
        public void TitleScreenRendersTheActionListAndRuntimeTitle()
        {
            TitleScreenController controller = CreateScreen<TitleScreenController>();
            VisualElement root = AttachFreshTree(controller);

            Assert.That(controller.IsBound, Is.True);
            // Negative control: buttons come from pushes, never from the document itself.
            Assert.That(root.Query<Button>().ToList(), Is.Empty);

            IReadOnlyList<MenuAction> actions = MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true);
            controller.SetActionMenu(BlockiverseProject.ProductName, actions);

            Assert.That(root.Q<Label>("bv-title").text, Is.EqualTo(BlockiverseProject.ProductName));

            List<Button> buttons = root.Query<Button>().ToList();
            Assert.That(buttons, Has.Count.EqualTo(actions.Count));
            for (int i = 0; i < actions.Count; i++)
            {
                Assert.That(buttons[i].text, Is.EqualTo(actions[i].Label), $"Label mismatch at index {i}.");
                Assert.That(buttons[i].ClassListContains("hs-button"), Is.True, $"Button {i} is missing hs-button.");
            }
        }

        [Test]
        public void RePushReplacesTheButtonListInsteadOfAppending()
        {
            TitleScreenController controller = CreateScreen<TitleScreenController>();
            VisualElement root = AttachFreshTree(controller);

            controller.SetActionMenu(
                BlockiverseProject.ProductName,
                MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true));
            Assert.That(root.Query<Button>().ToList(), Has.Count.EqualTo(6));

            IReadOnlyList<MenuAction> minimal = MenuActions.Title(hasLatestSave: false, hasAnySave: false, canQuit: false);
            controller.SetActionMenu(BlockiverseProject.ProductName, minimal);

            List<Button> buttons = root.Query<Button>().ToList();
            Assert.That(buttons, Has.Count.EqualTo(minimal.Count));
            // Surplus buttons do not exist at all in the UI Toolkit port (the uGUI menu merely
            // deactivated them).
            Assert.That(root.Q<Button>("bv-action-3"), Is.Null);
            Assert.That(buttons[0].text, Is.EqualTo(minimal[0].Label));
        }

        [Test]
        public void TitleScreenPressRoutesThroughTheMenuControllerRouter()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuControllerWithHost();
            TitleScreenController controller = CreateScreen<TitleScreenController>();
            AttachFreshTree(controller);
            controller.ConfigureHost(host);
            controller.SetActionMenu(
                BlockiverseProject.ProductName,
                MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true));

            // Positive control on the precondition so the assertion below cannot pass vacuously.
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));

            controller.PressAction(MenuActions.TitleSettings);

            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen));
            Assert.That(menuController.Router.IsGamePaused, Is.True);
        }

        [Test]
        public void PauseScreenPressForwardsTheActionRequestedEvent()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuControllerWithHost();
            PauseScreenController controller = CreateScreen<PauseScreenController>();
            AttachFreshTree(controller);
            controller.ConfigureHost(host);
            controller.SetActionMenu("Paused", MenuActions.PauseMenu(canToggleMode: false, canOpenCreativeTools: false));

            var requested = new List<string>();
            menuController.ActionRequested += requested.Add;

            controller.PressAction(MenuActions.PauseSaveGame);
            Assert.That(requested, Is.EqualTo(new[] { MenuActions.PauseSaveGame }));

            // Negative control: a null or empty press must not dispatch anything.
            controller.PressAction(null);
            controller.PressAction(string.Empty);
            Assert.That(requested, Has.Count.EqualTo(1));
        }

        [Test]
        public void DeathScreenBedrollOptionTracksSpawnAvailability()
        {
            DeathScreenController controller = CreateScreen<DeathScreenController>();
            VisualElement root = AttachFreshTree(controller);

            IReadOnlyList<MenuAction> withoutBedroll = MenuActions.Death(hasBedrollSpawn: false);
            controller.SetActionMenu("You Died", withoutBedroll);

            List<Button> buttons = root.Query<Button>().ToList();
            Assert.That(buttons, Has.Count.EqualTo(2));
            Assert.That(root.Q<Label>("bv-title").text, Is.EqualTo("You Died"));
            Assert.That(buttons[0].text, Is.EqualTo(withoutBedroll[0].Label));

            IReadOnlyList<MenuAction> withBedroll = MenuActions.Death(hasBedrollSpawn: true);
            controller.SetActionMenu("You Died", withBedroll);

            buttons = root.Query<Button>().ToList();
            Assert.That(buttons, Has.Count.EqualTo(3));
            Assert.That(buttons[0].text, Is.EqualTo(withBedroll[0].Label));
        }

        [Test]
        public void DeathScreenRespawnPressForwardsTheActionAndEntersGameplay()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuControllerWithHost();
            DeathScreenController controller = CreateScreen<DeathScreenController>();
            AttachFreshTree(controller);
            controller.ConfigureHost(host);
            controller.SetActionMenu("You Died", MenuActions.Death(hasBedrollSpawn: false));

            var requested = new List<string>();
            menuController.ActionRequested += requested.Add;

            controller.PressAction(MenuActions.DeathRespawnWorldSpawn);

            Assert.That(requested, Is.EqualTo(new[] { MenuActions.DeathRespawnWorldSpawn }));
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
        }

        [Test]
        public void SettingsHubScreenRendersTheHubListAndOpensTheComfortScreen()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuControllerWithHost();
            SettingsHubScreenController controller = CreateScreen<SettingsHubScreenController>();
            VisualElement root = AttachFreshTree(controller);
            controller.ConfigureHost(host);

            controller.SetActionMenu("Settings", MenuActions.Settings);

            List<Button> buttons = root.Query<Button>().ToList();
            Assert.That(buttons, Has.Count.EqualTo(MenuActions.Settings.Count));
            for (int i = 0; i < MenuActions.Settings.Count; i++)
                Assert.That(buttons[i].text, Is.EqualTo(MenuActions.Settings[i].Label));

            controller.PressAction(MenuActions.SettingsOpenComfort);

            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ComfortSettingsScreen));
        }

        [Test]
        public void StatusPushesRenderAndSurviveReattach()
        {
            TitleScreenController title = CreateScreen<TitleScreenController>();

            // A status pushed before the document attaches must not be lost.
            title.SetStatus("Continue ready.");
            VisualElement root = AttachFreshTree(title);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("Continue ready."));

            title.SetStatus("Saved.");
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("Saved."));

            VisualElement secondRoot = AttachFreshTree(title);
            Assert.That(secondRoot.Q<Label>("bv-status").text, Is.EqualTo("Saved."));

            PauseScreenController pause = CreateScreen<PauseScreenController>();
            VisualElement pauseRoot = AttachFreshTree(pause);
            pause.SetStatus("Autosaved.");
            Assert.That(pauseRoot.Q<Label>("bv-status").text, Is.EqualTo("Autosaved."));
        }

        [Test]
        public void MenuPushedBeforeAttachRendersOnAttach()
        {
            PauseScreenController controller = CreateScreen<PauseScreenController>();
            controller.SetActionMenu("Paused", MenuActions.PauseMenu(canToggleMode: true, canOpenCreativeTools: true));

            VisualElement root = AttachFreshTree(controller);

            Assert.That(root.Q<Label>("bv-title").text, Is.EqualTo("Paused"));
            Assert.That(root.Query<Button>().ToList(), Has.Count.EqualTo(7));
        }

        [Test]
        public void ReattachRebuildsTheListWithoutDriftingTheCallbackBalance()
        {
            TitleScreenController controller = CreateScreen<TitleScreenController>();
            VisualElement firstRoot = AttachFreshTree(controller);
            controller.SetActionMenu(
                BlockiverseProject.ProductName,
                MenuActions.Title(hasLatestSave: true, hasAnySave: true, canQuit: true));

            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(firstRoot.Query<Button>().ToList(), Has.Count.EqualTo(6));

            VisualElement secondRoot = AttachFreshTree(controller);

            Assert.That(controller.AttachCount, Is.EqualTo(2));
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            // The cached menu re-renders into the fresh tree without waiting for a new push,
            // and re-attaching the SAME tree again must not duplicate buttons either.
            Assert.That(secondRoot.Query<Button>().ToList(), Has.Count.EqualTo(6));

            controller.AttachForTest(secondRoot);
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(secondRoot.Query<Button>().ToList(), Has.Count.EqualTo(6));
        }

        [Test]
        public void SetActionMenuRejectsANullActionList()
        {
            TitleScreenController controller = CreateScreen<TitleScreenController>();
            AttachFreshTree(controller);

            Assert.That(() => controller.SetActionMenu("Title", null), Throws.ArgumentNullException);
        }

        [Test]
        public void AttachingADocumentWithoutTheNamedElementsReportsUnbound()
        {
            TitleScreenController controller = CreateScreen<TitleScreenController>();

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                controller.AttachForTest(new VisualElement());
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }

            Assert.That(controller.IsBound, Is.False);

            // Positive control: the real document binds.
            AttachFreshTree(controller);
            Assert.That(controller.IsBound, Is.True);
        }

        [Test]
        public void ActionLabelsResolveThroughLocalizationOverridesAtRenderTime()
        {
            BlockiverseLocalization.SetOverrideForTesting(BlockiverseLocalization.Keys.PauseResume, "Continuar");

            PauseScreenController controller = CreateScreen<PauseScreenController>();
            VisualElement root = AttachFreshTree(controller);
            controller.SetActionMenu("Paused", MenuActions.PauseMenu(canToggleMode: false, canOpenCreativeTools: false));

            Assert.That(root.Query<Button>().ToList()[0].text, Is.EqualTo("Continuar"));
        }
    }
}
