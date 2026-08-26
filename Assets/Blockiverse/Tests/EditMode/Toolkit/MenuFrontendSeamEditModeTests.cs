using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The coexistence seam (ADR 0010): while a frontend is registered,
    // BlockiverseMenuController must mirror every outward push to it, answer pending-state
    // reads from it, and keep the router/action semantics identical. These tests drive the
    // controller with a recording fake — the real UiToolkitMenuHost is covered by its own
    // screen tests; what is under test here is the CONTROLLER's half of the contract.
    public sealed class MenuFrontendSeamEditModeTests
    {
        sealed class RecordingFrontend : IBlockiverseMenuFrontend
        {
            public readonly List<(string screenId, string title, IReadOnlyList<MenuAction> actions)> ActionMenus = new();
            public readonly List<(string screenId, string message)> Statuses = new();
            public readonly List<IEnumerable<WorldSaveSummary>> SaveLists = new();
            public readonly List<WorldSaveSummary> ShownDetails = new();
            public readonly List<Pose> TitlePoses = new();
            public int CreativeRefreshCount;
            public int QuickMenuToggleCount;
            public int QuickMenuHideCount;
            public int NewWorldResetCount;
            public NewWorldConfig NewWorldConfigToReturn = new();
            public WorldSaveSummary? LoadSaveToReturn;
            public WorldSaveSummary? DetailsSaveToReturn;
            public string RenameTextToReturn = string.Empty;
            public bool StationOpen;
            public int StationCloseCount;

            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions) =>
                ActionMenus.Add((screenId, title, actions));

            public void SetScreenStatus(string screenId, string message) => Statuses.Add((screenId, message));

            public void SetSaveList(IEnumerable<WorldSaveSummary> saves) => SaveLists.Add(saves);

            public void ShowWorldDetails(WorldSaveSummary save) => ShownDetails.Add(save);

            public void SetTitleMenuPose(Pose pose) => TitlePoses.Add(pose);

            public void RefreshCreativeEnvironmentControls() => CreativeRefreshCount++;

            public void ToggleQuickBlockMenu() => QuickMenuToggleCount++;

            public void HideQuickBlockMenu() => QuickMenuHideCount++;
            public int HotbarCycleDelta { get; private set; }
            public int HotbarCycleCount { get; private set; }

            public void CycleHotbarSlot(int delta)
            {
                HotbarCycleDelta = delta;
                HotbarCycleCount++;
            }

            public void ResetNewWorldScreen() => NewWorldResetCount++;

            public NewWorldConfig PendingNewWorldConfig => NewWorldConfigToReturn;
            public WorldSaveSummary? PendingLoadSave => LoadSaveToReturn;
            public WorldSaveSummary? PendingDetailsSave => DetailsSaveToReturn;
            public string PendingDetailsRenameText => RenameTextToReturn;

            public bool IsStationOpenAt(BlockPosition position) => StationOpen;

            public void CloseStationView() => StationCloseCount++;
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

        (BlockiverseMenuController controller, RecordingFrontend frontend) CreateRegistered()
        {
            var gameObject = new GameObject("Menu Controller Under Test");
            objectsToDestroy.Add(gameObject);
            BlockiverseMenuController controller = gameObject.AddComponent<BlockiverseMenuController>();
            var frontend = new RecordingFrontend();
            controller.RegisterFrontend(frontend);
            return (controller, frontend);
        }

        [Test]
        public void RegistrationReplaysTheStaticMenusAndSaveList()
        {
            (BlockiverseMenuController _, RecordingFrontend frontend) = CreateRegistered();

            // RefreshStaticMenus mirrors title/pause/settings/world-details.
            Assert.That(frontend.ActionMenus, Has.Some.Matches<(string screenId, string title, IReadOnlyList<MenuAction> actions)>(
                push => push.screenId == MenuActions.TitleScreen));
            Assert.That(frontend.ActionMenus, Has.Some.Matches<(string screenId, string title, IReadOnlyList<MenuAction> actions)>(
                push => push.screenId == MenuActions.PauseScreen));
            Assert.That(frontend.ActionMenus, Has.Some.Matches<(string screenId, string title, IReadOnlyList<MenuAction> actions)>(
                push => push.screenId == MenuActions.SettingsScreen));

            // The session controller's RefreshSaveList ran and its push was mirrored.
            Assert.That(frontend.SaveLists, Is.Not.Empty);
        }

        // The hotbar-cycle seam. RecordingFrontend has captured HotbarCycleDelta/HotbarCycleCount
        // since the seam was added, but nothing read them — the whole path from the support hand's
        // face buttons to the strip could have been deleted with this suite still green, in the
        // file whose entire purpose is asserting that seam.
        [Test]
        public void HotbarCyclePassesTheDirectionThroughToTheFrontend()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            Assert.That(frontend.HotbarCycleCount, Is.Zero, "positive control: nothing cycled yet");

            InvokePrivate(controller, "OnHotbarNextPressed");
            Assert.That(frontend.HotbarCycleCount, Is.EqualTo(1));
            Assert.That(frontend.HotbarCycleDelta, Is.EqualTo(1),
                "the support hand's forward button must cycle forward");

            // Direction matters and is easy to invert: both handlers are one line apart and differ
            // only in sign, so a copy-paste leaves the strip cycling one way for both buttons.
            InvokePrivate(controller, "OnHotbarPreviousPressed");
            Assert.That(frontend.HotbarCycleCount, Is.EqualTo(2));
            Assert.That(frontend.HotbarCycleDelta, Is.EqualTo(-1),
                "the back button cycled forward — the two handlers share a sign");
        }

        static void InvokePrivate(object target, string methodName)
        {
            System.Reflection.MethodInfo method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            Assert.That(method, Is.Not.Null, $"{target.GetType().Name} has no {methodName}()");
            method.Invoke(target, null);
        }

        [Test]
        public void DispatchActionRoutesLikeAUguiButton()
        {
            (BlockiverseMenuController controller, RecordingFrontend _) = CreateRegistered();

            controller.DispatchAction(MenuActions.TitleSettings);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen));
            Assert.That(controller.Router.IsGamePaused, Is.True);
        }

        [Test]
        public void TitleNewWorldResetsTheFrontendScreen()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            controller.DispatchAction(MenuActions.TitleNewWorld);

            Assert.That(frontend.NewWorldResetCount, Is.EqualTo(1));
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));
        }

        [Test]
        public void ShowErrorMirrorsTheMenuAndTheMessageAndPushesTheModal()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            controller.ShowError("it broke");

            Assert.That(frontend.ActionMenus, Has.Some.Matches<(string screenId, string title, IReadOnlyList<MenuAction> actions)>(
                push => push.screenId == MenuActions.ErrorModal));
            Assert.That(frontend.Statuses, Has.Some.Matches<(string screenId, string message)>(
                push => push.screenId == MenuActions.ErrorModal && push.message == "it broke"));
            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ErrorModal));
        }

        [Test]
        public void ConfirmFlowMirrorsThePromptAndInvokesTheCallbackOnce()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();
            int accepted = 0;

            controller.RequestConfirm("Really?", "Yes", "No", value => { if (value) accepted++; });

            (string screenId, string title, IReadOnlyList<MenuAction> actions) confirmPush = default;
            foreach (var push in frontend.ActionMenus)
            {
                if (push.screenId == MenuActions.ConfirmModal)
                    confirmPush = push;
            }

            Assert.That(confirmPush.title, Is.EqualTo("Really?"));
            Assert.That(controller.Router.HasModal, Is.True);

            controller.DispatchAction(MenuActions.ConfirmAccept);

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(controller.Router.HasModal, Is.False);

            // Re-accept must not fire the (cleared) callback again.
            controller.DispatchAction(MenuActions.ConfirmAccept);
            Assert.That(accepted, Is.EqualTo(1));
        }

        [Test]
        public void PendingStateReadsComeFromTheFrontend()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            frontend.RenameTextToReturn = "Renamed Camp";

            Assert.That(controller.PendingNewWorldConfig, Is.SameAs(frontend.NewWorldConfigToReturn));
            Assert.That(controller.PendingDetailsRenameText, Is.EqualTo("Renamed Camp"));
            Assert.That(controller.PendingLoadSave, Is.Null);
        }

        [Test]
        public void LoadWorldDetailsUsesTheFrontendSelectionAndMirrorsTheSummary()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();
            var summary = new WorldSaveSummary(
                "Camp", "42", "survival", "normal", dayCount: 3,
                lastPlayedUtc: System.DateTime.UtcNow, createdUtc: System.DateTime.UtcNow);
            frontend.LoadSaveToReturn = summary;

            controller.DispatchAction(MenuActions.TitleLoadWorld);
            controller.DispatchAction(MenuActions.LoadWorldDetails);

            Assert.That(frontend.ShownDetails, Has.Count.EqualTo(1));
            Assert.That(frontend.ShownDetails[0].Name, Is.EqualTo("Camp"));
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldDetailsScreen));
        }

        [Test]
        public void LoadWorldDetailsWithoutASelectionDoesNothing()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            controller.DispatchAction(MenuActions.TitleLoadWorld);
            controller.DispatchAction(MenuActions.LoadWorldDetails);

            Assert.That(frontend.ShownDetails, Is.Empty);
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LoadWorldScreen));
        }

        [Test]
        public void TitlePoseIsMirroredIncludingTheRegistrationReplay()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();
            var pose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

            controller.SetTitleMenuPose(pose);

            Assert.That(frontend.TitlePoses, Has.Count.EqualTo(1));
            Assert.That(frontend.TitlePoses[0].position, Is.EqualTo(pose.position));

            // A frontend registered AFTER the pose was set must still receive it.
            var lateFrontend = new RecordingFrontend();
            controller.RegisterFrontend(lateFrontend);
            Assert.That(lateFrontend.TitlePoses, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnregisteringRestoresUguiPendingReads()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            Assert.That(controller.HasFrontend, Is.True);

            controller.UnregisterFrontend(frontend);

            Assert.That(controller.HasFrontend, Is.False);
            // No uGUI panels are wired in this fixture, so the uGUI path answers null/empty.
            Assert.That(controller.PendingNewWorldConfig, Is.Null);
            Assert.That(controller.PendingDetailsRenameText, Is.EqualTo(string.Empty));
        }

        // Control: unregistering a frontend that is not the registered one must not clear the
        // real one — two hosts in a scene is a config error, not a licence to fight.
        [Test]
        public void UnregisteringAStrangerIsIgnored()
        {
            (BlockiverseMenuController controller, RecordingFrontend frontend) = CreateRegistered();

            controller.UnregisterFrontend(new RecordingFrontend());

            Assert.That(controller.HasFrontend, Is.True);
            Assert.That(controller.PendingNewWorldConfig, Is.SameAs(frontend.NewWorldConfigToReturn));
        }
    }
}
