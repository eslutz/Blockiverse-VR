using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit ports of the two modal dialogs (ConfirmDialogController / ErrorDialogController),
    // mirroring the uGUI oracles in ActionMenuEditModeTests: prompt/message rendering, verbatim
    // action-id dispatch, custom-label re-pushes, surplus-click rejection. Dispatch is asserted
    // end-to-end through a real BlockiverseMenuController so the confirm-callback ownership
    // (controller swaps to a local and nulls it before PopModal) is exercised, not simulated.
    //
    // Clicks are driven through InvokeRenderedAction, the controllers' public click seam:
    // ClickEvent delivery requires a live runtime panel, which EditMode never builds.
    public sealed class ModalDialogsEditModeTests
    {
        const string ConfirmDocumentPath = "Assets/Blockiverse/UI/Documents/ConfirmDialog.uxml";
        const string ErrorDocumentPath = "Assets/Blockiverse/UI/Documents/ErrorDialog.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void ConfirmDialogRendersPromptAndDefaultActionButtons()
        {
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            Attach(dialog, ConfirmDocumentPath);

            IReadOnlyList<MenuAction> actions = MenuActions.Confirm();
            dialog.SetActionMenu("Return to Title? Progress will be saved.", actions);

            Label prompt = dialog.Root.Q<Label>("bv-confirm-prompt");
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt.text, Is.EqualTo("Return to Title? Progress will be saved."));

            List<Button> buttons = QueryActionButtons(dialog.Root, "bv-confirm-actions");
            Assert.That(buttons, Has.Count.EqualTo(2));
            Assert.That(buttons[0].text, Is.EqualTo(actions[0].Label));
            Assert.That(buttons[1].text, Is.EqualTo(actions[1].Label));
            Assert.That(dialog.RenderedActionIds,
                Is.EqualTo(new[] { MenuActions.ConfirmAccept, MenuActions.ConfirmCancel }));
        }

        // RequestConfirm re-pushes the same modal with per-flow keyless labels
        // (MenuActions.Confirm(confirmLabel, cancelLabel)); they must render verbatim and the
        // rebuild must replace the old buttons, never append to them.
        [Test]
        public void ConfirmDialogRePushRendersCustomLabelsVerbatimWithoutDuplicates()
        {
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            Attach(dialog, ConfirmDocumentPath);

            dialog.SetActionMenu("First push", MenuActions.Confirm());
            dialog.SetActionMenu("Delete 'Camp'?", MenuActions.Confirm("Delete", "Keep"));

            Label prompt = dialog.Root.Q<Label>("bv-confirm-prompt");
            Assert.That(prompt.text, Is.EqualTo("Delete 'Camp'?"));

            List<Button> buttons = QueryActionButtons(dialog.Root, "bv-confirm-actions");
            Assert.That(buttons, Has.Count.EqualTo(2), "Re-push must rebuild the row, not append.");
            Assert.That(buttons[0].text, Is.EqualTo("Delete"));
            Assert.That(buttons[1].text, Is.EqualTo("Keep"));
            Assert.That(dialog.RenderedActionIds,
                Is.EqualTo(new[] { MenuActions.ConfirmAccept, MenuActions.ConfirmCancel }));
        }

        // The frontend push can land before the panel attaches (RegisterFrontend replays state
        // during scene bring-up); the pending push must render on attach instead of being lost.
        [Test]
        public void ConfirmDialogPushedBeforeAttachRendersOnAttach()
        {
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            dialog.SetActionMenu("Quit Game?", MenuActions.Confirm());

            Attach(dialog, ConfirmDocumentPath);

            Assert.That(dialog.Root.Q<Label>("bv-confirm-prompt").text, Is.EqualTo("Quit Game?"));
            Assert.That(QueryActionButtons(dialog.Root, "bv-confirm-actions"), Has.Count.EqualTo(2));
        }

        [Test]
        public void ConfirmDialogRejectsANullActionList()
        {
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            Attach(dialog, ConfirmDocumentPath);

            Assert.Throws<ArgumentNullException>(() => dialog.SetActionMenu("Prompt", null));
        }

        [Test]
        public void ConfirmAcceptDispatchesVerbatimAndFiresTheControllerOwnedCallback()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuHub();
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            dialog.ConfigureHost(host);
            Attach(dialog, ConfirmDocumentPath);

            bool? confirmed = null;
            menuController.RequestConfirm("Return to Title?", null, null, accepted => confirmed = accepted);
            Assert.That(menuController.Router.HasModal, Is.True);
            Assert.That(menuController.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));

            // The same push the frontend mirror would deliver for this RequestConfirm.
            dialog.SetActionMenu("Return to Title?", MenuActions.Confirm());
            dialog.InvokeRenderedAction(0);

            Assert.That(confirmed, Is.True,
                "confirm.accept must reach the menu controller verbatim and fire the stored callback with true.");
            Assert.That(menuController.Router.HasModal, Is.False, "Accepting must pop the confirm modal.");
        }

        [Test]
        public void ConfirmCancelPopsTheModalWithoutFiringTheCallback()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuHub();
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            dialog.ConfigureHost(host);
            Attach(dialog, ConfirmDocumentPath);

            int callbackCount = 0;
            menuController.RequestConfirm("Quit Game?", null, null, _ => callbackCount++);
            dialog.SetActionMenu("Quit Game?", MenuActions.Confirm());

            dialog.InvokeRenderedAction(1);

            Assert.That(callbackCount, Is.Zero, "Cancel must clear the callback, not invoke it.");
            Assert.That(menuController.Router.HasModal, Is.False, "Cancelling must pop the confirm modal.");

            // The menu controller owns the callback: once cancel cleared it, a stale accept
            // dispatch must find nothing to invoke.
            dialog.InvokeRenderedAction(0);
            Assert.That(callbackCount, Is.Zero, "A stale accept after cancel must not fire the cleared callback.");
        }

        // Mirrors the uGUI oracle's surplus-button check: indices outside the rendered list
        // dispatch nothing and leave the modal (and its callback) intact.
        [Test]
        public void ConfirmDialogIgnoresOutOfRangeActionIndices()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuHub();
            ConfirmDialogController dialog = CreateController<ConfirmDialogController>();
            dialog.ConfigureHost(host);
            Attach(dialog, ConfirmDocumentPath);

            int callbackCount = 0;
            menuController.RequestConfirm("Prompt", null, null, _ => callbackCount++);
            dialog.SetActionMenu("Prompt", MenuActions.Confirm());

            dialog.InvokeRenderedAction(-1);
            dialog.InvokeRenderedAction(5);

            Assert.That(callbackCount, Is.Zero);
            Assert.That(menuController.Router.HasModal, Is.True);

            // Positive control: a valid index still dispatches after the ignored ones.
            dialog.InvokeRenderedAction(0);
            Assert.That(callbackCount, Is.EqualTo(1));
        }

        [Test]
        public void ErrorDialogRendersTitleMessageAndCloseButton()
        {
            ErrorDialogController dialog = CreateController<ErrorDialogController>();
            Attach(dialog, ErrorDocumentPath);

            IReadOnlyList<MenuAction> actions = MenuActions.Error();
            dialog.SetActionMenu("Error", actions);
            dialog.SetStatus("Save file could not be read.");

            Assert.That(dialog.Root.Q<Label>("bv-error-title").text, Is.EqualTo("Error"));
            Assert.That(dialog.Root.Q<Label>("bv-error-message").text, Is.EqualTo("Save file could not be read."));

            List<Button> buttons = QueryActionButtons(dialog.Root, "bv-error-actions");
            Assert.That(buttons, Has.Count.EqualTo(1));
            Assert.That(buttons[0].text, Is.EqualTo(actions[0].Label));
            Assert.That(dialog.RenderedActionIds, Is.EqualTo(new[] { MenuActions.ErrorClose }));
        }

        [Test]
        public void ErrorDialogRePushKeepsASingleButtonAndTheLatestMessage()
        {
            ErrorDialogController dialog = CreateController<ErrorDialogController>();
            Attach(dialog, ErrorDocumentPath);

            dialog.SetActionMenu("Error", MenuActions.Error());
            dialog.SetStatus("First failure.");
            dialog.SetActionMenu("Load Failed", MenuActions.Error());
            dialog.SetStatus("Second failure.");

            Assert.That(dialog.Root.Q<Label>("bv-error-title").text, Is.EqualTo("Load Failed"));
            Assert.That(dialog.Root.Q<Label>("bv-error-message").text, Is.EqualTo("Second failure."));
            Assert.That(QueryActionButtons(dialog.Root, "bv-error-actions"), Has.Count.EqualTo(1),
                "Re-push must rebuild the row, not append.");
        }

        [Test]
        public void ErrorDialogStatusPushedBeforeAttachRendersOnAttach()
        {
            ErrorDialogController dialog = CreateController<ErrorDialogController>();
            dialog.SetActionMenu("Error", MenuActions.Error());
            dialog.SetStatus("Could not host: port already in use.");

            Attach(dialog, ErrorDocumentPath);

            Assert.That(dialog.Root.Q<Label>("bv-error-message").text,
                Is.EqualTo("Could not host: port already in use."));
            Assert.That(QueryActionButtons(dialog.Root, "bv-error-actions"), Has.Count.EqualTo(1));
        }

        [Test]
        public void ErrorCloseDispatchesVerbatimAndPopsTheModal()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuHub();
            ErrorDialogController dialog = CreateController<ErrorDialogController>();
            dialog.ConfigureHost(host);
            Attach(dialog, ErrorDocumentPath);

            menuController.ShowError("Save file could not be read.");
            Assert.That(menuController.Router.HasModal, Is.True);
            Assert.That(menuController.Router.InputTarget, Is.EqualTo(MenuActions.ErrorModal));

            // The same push the frontend mirror would deliver for this ShowError.
            dialog.SetActionMenu("Error", MenuActions.Error());
            dialog.SetStatus("Save file could not be read.");
            dialog.InvokeRenderedAction(0);

            Assert.That(menuController.Router.HasModal, Is.False,
                "error_dialog.close must reach the menu controller verbatim and pop the error modal.");
        }

        [Test]
        public void ModalScreenDeclarationsMatchTheContract()
        {
            var confirmAttribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(ConfirmDialogController), typeof(UiToolkitScreenAttribute));
            Assert.That(confirmAttribute, Is.Not.Null);
            Assert.That(confirmAttribute.ScreenId, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(confirmAttribute.DocumentAssetPath, Is.EqualTo(ConfirmDocumentPath));
            Assert.That(confirmAttribute.WidthPixels, Is.EqualTo(600));
            Assert.That(confirmAttribute.HeightPixels, Is.EqualTo(460));
            Assert.That(confirmAttribute.PlacementProfile, Is.EqualTo(UiToolkitPlacementProfile.Menu));

            var errorAttribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(ErrorDialogController), typeof(UiToolkitScreenAttribute));
            Assert.That(errorAttribute, Is.Not.Null);
            Assert.That(errorAttribute.ScreenId, Is.EqualTo(MenuActions.ErrorModal));
            Assert.That(errorAttribute.DocumentAssetPath, Is.EqualTo(ErrorDocumentPath));
            Assert.That(errorAttribute.WidthPixels, Is.EqualTo(600));
            Assert.That(errorAttribute.HeightPixels, Is.EqualTo(460));
            Assert.That(errorAttribute.PlacementProfile, Is.EqualTo(UiToolkitPlacementProfile.Menu));

            // The host indexes by the instance property; it must agree with the declaration.
            Assert.That(CreateController<ConfirmDialogController>().ScreenId, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(CreateController<ErrorDialogController>().ScreenId, Is.EqualTo(MenuActions.ErrorModal));
        }

        T CreateController<T>() where T : UiToolkitScreenController
        {
            var gameObject = new GameObject(typeof(T).Name);
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<UIDocument>();
            return gameObject.AddComponent<T>();
        }

        // A real menu controller plus host so DispatchAction routes exactly like runtime:
        // screen -> host -> BlockiverseMenuController.DispatchAction -> HandleAction.
        // RegisterFrontend initializes the router outside Play mode and mirrors the
        // registration the host performs in Start.
        (BlockiverseMenuController menuController, UiToolkitMenuHost host) CreateMenuHub()
        {
            var hubObject = new GameObject("Menu Hub");
            objectsToDestroy.Add(hubObject);
            BlockiverseMenuController menuController = hubObject.AddComponent<BlockiverseMenuController>();
            UiToolkitMenuHost host = hubObject.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);
            menuController.RegisterFrontend(host);
            return (menuController, host);
        }

        static void Attach(UiToolkitScreenController controller, string documentPath)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(documentPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing or failed to import: {documentPath}");

            controller.AttachForTest(tree.Instantiate());

            Assert.That(controller.IsBound, Is.True,
                $"{controller.GetType().Name} could not find its elements — UXML names drifted.");
        }

        static List<Button> QueryActionButtons(VisualElement root, string containerName)
        {
            VisualElement container = root.Q<VisualElement>(containerName);
            Assert.That(container, Is.Not.Null, $"Container '{containerName}' not found.");
            return container.Query<Button>().ToList();
        }
    }
}
