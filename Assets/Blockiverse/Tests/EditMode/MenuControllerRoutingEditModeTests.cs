using System;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // What survived MenuRuntimeWiringEditModeTests. That file was 39 tests over the uGUI panel
    // objects, but most of them only used the panels as inert scaffolding and asserted on
    // BlockiverseMenuController — which survives whole. These are those assertions, re-driven
    // through the surviving public surface (Router verbs, DispatchAction, the frontend seam)
    // with no menu objects at all.
    //
    // Four behaviours here have no other coverage in the repo and are the reason this file
    // exists rather than the original being deleted outright: EnterGameplay(),
    // SetSaveAvailability(), the cleared-modal confirm-callback ownership, and the
    // respawn-before-save ordering in the DeathReturnToTitle handler.
    public sealed class MenuControllerRoutingEditModeTests
    {
        // A plain-C# frontend double. EditMode never runs Awake/OnEnable, so RegisterFrontend
        // is also how the router gets initialised — the same reason the Toolkit screen tests
        // call it. Recording the pushes is what makes the action-list assertions possible
        // without standing up a UIDocument.
        sealed class RecordingFrontend : IBlockiverseMenuFrontend
        {
            public readonly Dictionary<string, IReadOnlyList<MenuAction>> Menus = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Statuses = new(StringComparer.Ordinal);

            public WorldSaveSummary? DetailsSave;

            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions) =>
                Menus[screenId] = actions;

            public void SetScreenStatus(string screenId, string message) => Statuses[screenId] = message;

            public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
            {
            }

            public void ShowWorldDetails(WorldSaveSummary save) => DetailsSave = save;

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
            public WorldSaveSummary? PendingDetailsSave => DetailsSave;
            public string PendingDetailsRenameText => string.Empty;

            public bool IsStationOpenAt(BlockPosition position) => false;

            public void CloseStationView()
            {
            }
        }

        sealed class FakeInputRig : IBlockiverseInputRig
        {
            public bool LocomotionSuppressed { get; set; }
            public UnityEngine.Events.UnityEvent MenuPressed { get; } = new();
            public UnityEngine.Events.UnityEvent QuickMenuPressed { get; } = new();
            public UnityEngine.Events.UnityEvent BreakPressed { get; } = new();

            public bool TryGetActiveInteractionRayPose(out Vector3 rayOrigin, out Vector3 rayDirection)
            {
                rayOrigin = default;
                rayDirection = default;
                return false;
            }

            public bool TryGetInteractionRayPose(BlockiverseControllerRole hand, out Vector3 rayOrigin, out Vector3 rayDirection)
            {
                rayOrigin = default;
                rayDirection = default;
                return false;
            }
        }

        readonly List<UnityEngine.Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object target in objectsToDestroy)
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        (BlockiverseMenuController controller, RecordingFrontend frontend) CreateController()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            var frontend = new RecordingFrontend();
            controller.RegisterFrontend(frontend);
            return (controller, frontend);
        }

        static void StartBehaviour(MonoBehaviour behaviour)
        {
            MethodInfo start = behaviour
                .GetType()
                .GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(start, Is.Not.Null, $"{behaviour.GetType().Name} must expose a Start method for this wiring test.");
            start.Invoke(behaviour, null);
        }

        static void EnableBehaviour(MonoBehaviour behaviour)
        {
            MethodInfo onEnable = behaviour
                .GetType()
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(onEnable, Is.Not.Null, $"{behaviour.GetType().Name} must expose an OnEnable method for this wiring test.");
            onEnable.Invoke(behaviour, null);
        }

        static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} must expose private field '{fieldName}' for this wiring test.");
            field.SetValue(target, value);
        }

        static string[] ActionIdsOf(IReadOnlyList<MenuAction> actions)
        {
            var ids = new string[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                ids[i] = actions[i].ActionId;
            return ids;
        }

        [Test]
        public void SettingsSubPanelsReturnToCorrectPreviousRoute()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);

            // 1. From Title -> Settings -> sub-screens.
            controller.Router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
            controller.Router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
            AssertReturnToHub(MenuActions.AudioSettingsScreen, controller.CloseAudioSettingsScreen);
            AssertReturnToHub(MenuActions.ControlsScreen, controller.CloseControlsScreen);
            AssertReturnToHub(MenuActions.ComfortSettingsScreen, controller.CloseComfortSettingsScreen);

            controller.Router.PopScreen(); // Return to Title
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));

            // 2. From Pause -> Settings -> sub-screens. The hub has two entry points and the
            // close verb must land back on the hub from either, not on whatever was underneath.
            controller.EnterGameplay();
            controller.OnMenuPressed(); // Pause
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));

            controller.Router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
            AssertReturnToHub(MenuActions.AudioSettingsScreen, controller.CloseAudioSettingsScreen);

            controller.Router.PopScreen(); // Return to Pause
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));

            void AssertReturnToHub(string subScreenId, Action closeAction)
            {
                controller.Router.PushScreen(new ScreenRoute(subScreenId, pauseGame: true));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(subScreenId));
                closeAction();
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen),
                    $"Closing {subScreenId} must return to the Settings hub.");
            }
        }

        [Test]
        public void WorldInputDisabledWheneverRoutedMenuOrModalOwnsInput()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);

            // Title screen
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            // Gameplay
            controller.EnterGameplay();
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);

            // Pause
            controller.OnMenuPressed();
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            // Confirm modal over gameplay
            controller.EnterGameplay();
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });
            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
        }

        [Test]
        public void MenuRouterPublishesPauseAndWorldInputState()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.EnterGameplay();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.False);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);

            controller.OnMenuPressed();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
        }

        [Test]
        public void WorldLoadingRoutePausesAndReleasesOnEnterGameplay()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);

            controller.ShowWorldLoadingScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldLoadingScreen));
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.EnterGameplay();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void ErrorModalTakesInputPriorityOverTheRoutedScreen()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateController();
            StartBehaviour(controller);

            controller.ShowError("Message", "Title");

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ErrorModal));
            Assert.That(frontend.Statuses[MenuActions.ErrorModal], Is.EqualTo("Message"));
        }

        [Test]
        public void LanSessionEndedRouteClosesThroughRouter()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);
            controller.EnterGameplay();

            controller.ShowLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LanMultiplayerScreen));

            controller.CloseLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void DeathWhilePausedRoutesToDeathScreen()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            SurvivalVitalsRuntime vitals = controller.gameObject.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);

            StartBehaviour(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.IsGamePaused, Is.True);
        }

        [Test]
        public void DeathWhileModalIsOpenClearsModalAndRoutesToDeathScreen()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            SurvivalVitalsRuntime vitals = controller.gameObject.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);

            StartBehaviour(controller);
            controller.EnterGameplay();
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            Assert.That(controller.Router.HasModal, Is.True);

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.DeathScreen));
        }

        [Test]
        public void ClearingModalStackClearsPendingConfirmCallback()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);

            bool accepted = false;
            controller.RequestConfirm("Quit?", "Quit", "Cancel", value => accepted = value);
            controller.EnterGameplay();

            // The modal is gone but the accept action can still arrive — a stale button, a
            // queued dispatch. The callback must not survive behind hidden UI.
            controller.DispatchAction(MenuActions.ConfirmAccept);

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(accepted, Is.False, "A confirm callback from a cleared modal must not survive behind hidden UI.");
        }

        [Test]
        public void DeathReturnToTitleRespawnsBeforeSaveActionIsRaised()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            SurvivalVitalsRuntime vitals = controller.gameObject.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);

            StartBehaviour(controller);
            controller.EnterGameplay();
            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(vitals.Vitals.IsDead, Is.True);

            string requestedAction = null;
            bool wasAliveWhenActionRaised = false;
            controller.ActionRequested += actionId =>
            {
                requestedAction = actionId;
                wasAliveWhenActionRaised = !vitals.Vitals.IsDead;
            };

            controller.DispatchAction(MenuActions.DeathReturnToTitle);

            Assert.That(requestedAction, Is.EqualTo(MenuActions.DeathReturnToTitle));
            Assert.That(wasAliveWhenActionRaised, Is.True,
                "The session save handler must observe post-respawn vitals.");
            Assert.That(vitals.Vitals.IsDead, Is.False);
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
        }

        // SetSaveAvailability has no other coverage in the repo, and it is what decides whether
        // Continue and Load World exist on the title screen at all. The push, not the return
        // value, is the contract: the frontend only ever sees the list the controller sends it.
        [Test]
        public void SetSaveAvailabilityRepushesTheTitleActionList()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateController();
            StartBehaviour(controller);

            // Establish the baseline explicitly rather than assuming it.
            //
            // BlockiverseMenuController is [RequireComponent(typeof(BlockiverseWorldSessionController))],
            // so AddComponent silently brings a session controller along, and RegisterFrontend calls
            // its RefreshSaveList — which enumerates the REAL Application.persistentDataPath/Saves.
            // The starting availability is therefore whatever worlds happen to exist on the machine
            // running the suite (507 of them on the author's, hence a red negative control), while a
            // clean CI container has none and the same test passes. A test that flips on developer
            // state and stays green in CI is worse than no test: it only ever fails where nobody is
            // watching the pipeline.
            controller.SetSaveAvailability(latestSaveExists: false, anySaveExists: false);

            Assert.That(ActionIdsOf(frontend.Menus[MenuActions.TitleScreen]), Does.Not.Contain(MenuActions.TitleContinue),
                "Negative control: with no saves reported the title screen must not offer Continue.");

            controller.SetSaveAvailability(latestSaveExists: true, anySaveExists: true);

            string[] ids = ActionIdsOf(frontend.Menus[MenuActions.TitleScreen]);
            Assert.That(ids[0], Is.EqualTo(MenuActions.TitleContinue));
            Assert.That(ids, Does.Contain(MenuActions.TitleLoadWorld));

            controller.SetSaveAvailability(latestSaveExists: false, anySaveExists: false);

            Assert.That(ActionIdsOf(frontend.Menus[MenuActions.TitleScreen]), Does.Not.Contain(MenuActions.TitleLoadWorld),
                "Deleting the last save must take Load World back off the title screen.");
        }

        [Test]
        public void DestructiveActionsAreGatedByConfirmationModal()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateController();
            StartBehaviour(controller);

            // 1. Title Quit
            AssertGated(MenuActions.TitleQuit);

            // 2. Pause Return to Title
            controller.EnterGameplay();
            controller.OnMenuPressed();
            AssertGated(MenuActions.PauseReturnToTitle);

            // 3. Pause Quit
            AssertGated(MenuActions.PauseQuit);

            // 4. World Details Delete — gated only once a save is actually selected.
            frontend.DetailsSave = new WorldSaveSummary("Test", "1", "s", "n", 1, DateTime.UtcNow, DateTime.UtcNow);
            controller.Router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
            AssertGated(MenuActions.WorldDetailsDeleteRequested);

            void AssertGated(string actionId)
            {
                var requestedActions = new List<string>();
                void Record(string id) => requestedActions.Add(id);
                controller.ActionRequested += Record;

                controller.DispatchAction(actionId);

                Assert.That(controller.Router.HasModal, Is.True, $"Action {actionId} must be gated by a modal.");
                Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
                Assert.That(requestedActions, Is.Empty, $"Action {actionId} must not be emitted until confirmed.");

                controller.Router.PopModal();
                controller.ActionRequested -= Record;
            }
        }

        [Test]
        public void CancellingTheQuitConfirmationSuppressesTheSaveAction()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            StartBehaviour(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            controller.DispatchAction(MenuActions.PauseQuit);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));

            controller.DispatchAction(MenuActions.ConfirmCancel);

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty,
                "Cancelling must not raise the save action the confirmed path would have raised.");
        }

        [Test]
        public void MenusNeverSuppressLocomotionButPauseRoutesStillPauseSinglePlayerClock()
        {
            (BlockiverseMenuController controller, _) = CreateController();
            var inputRig = new FakeInputRig();
            // BlockiverseMenuController's [RequireComponent] already auto-added the session
            // controller; AddComponent would return null under [DisallowMultipleComponent].
            var session = controller.GetComponent<BlockiverseWorldSessionController>();
            SetPrivateField(session, "currentSavePath", "/tmp/test-world.vxlworld");
            // The controller resolves its rig from the hierarchy; a plain-C# double has to be
            // injected. ResolveRuntimeReferences only fills the field when it is null, so this
            // survives the Start() call below.
            SetPrivateField(controller, "inputRig", (IBlockiverseInputRig)inputRig);

            StartBehaviour(controller);

            Assert.That(inputRig.LocomotionSuppressed, Is.False,
                "The title mini-world remains explorable even though the title route is paused.");

            controller.EnterGameplay();
            Assert.That(inputRig.LocomotionSuppressed, Is.False);
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.False);

            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));
            Assert.That(inputRig.LocomotionSuppressed, Is.False,
                "In-session menus lazily follow the player; movement stays free while a menu is open.");
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False,
                "Block editing stays gated while a menu has focus.");
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True,
                "With no LAN session live, a pause route still freezes the single-player world clock.");
        }

        // First-run entry, the half Toolkit/ControlsScreensEditModeTests does not cover (it owns
        // the close verb). The gate changed with the cutover: it used to be "is the popup object
        // in the rig", it is now "did the host register a controller-mapping screen", so a
        // frontend that renders no such screen must never strand the player on a route nothing
        // draws.
        [Test]
        public void FirstRunRoutesControllerMappingOnlyWhileTheScreenIsRegisteredAndUnseen()
        {
            string key = BlockiverseMenuController.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                // Negative control: a frontend with no controller-mapping screen stays on title.
                (BlockiverseMenuController bareController, _) = CreateController();
                StartBehaviour(bareController);
                Assert.That(bareController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen),
                    "Without a registered controller-mapping screen the first-run route must stay inert.");

                (BlockiverseMenuController controller, UiToolkitMenuHost host) = CreateHostedController();
                Assert.That(HostRegisters(host, MenuActions.ControllerMappingScreen), Is.True,
                    "Fixture no longer registers the controller-mapping screen.");

                StartBehaviour(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(controller.Router.ScreenDepth, Is.EqualTo(1),
                    "On first launch the mapping screen IS the root; the title menu must not sit under it as a poppable route.");

                PlayerPrefs.SetInt(key, 1);

                (BlockiverseMenuController seenController, _) = CreateHostedController();
                StartBehaviour(seenController);

                Assert.That(seenController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        static bool HostRegisters(UiToolkitMenuHost host, string screenId)
        {
            foreach ((string id, UiToolkitScreenController _) in host.Screens)
                if (string.Equals(id, screenId, StringComparison.Ordinal))
                    return true;

            return false;
        }

        // The host discovers its screens in Awake, which EditMode never runs, so the discovery
        // pass is invoked directly. It is idempotent (it clears its own lists first), so this is
        // safe whether or not the editor happened to run Awake.
        (BlockiverseMenuController controller, UiToolkitMenuHost host) CreateHostedController()
        {
            GameObject rig = CreateRoot("Hosted Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();

            GameObject hostObject = CreateRoot("Menu Host");
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            GameObject screenObject = new("Controller Mapping Screen");
            screenObject.transform.SetParent(hostObject.transform, false);
            screenObject.AddComponent<ControllerMappingScreenController>();

            MethodInfo discover = typeof(UiToolkitMenuHost)
                .GetMethod("DiscoverScreens", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(discover, Is.Not.Null, "UiToolkitMenuHost must expose DiscoverScreens for this fixture.");
            discover.Invoke(host, null);

            host.Configure(controller);
            controller.RegisterFrontend(host);
            return (controller, host);
        }
    }
}
