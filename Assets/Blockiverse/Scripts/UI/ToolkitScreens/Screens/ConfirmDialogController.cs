using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the confirm modal (uGUI: the "Confirm Dialog" BlockiverseActionMenu).
    // BlockiverseMenuController.RequestConfirm pushes the prompt as the SetActionMenu title and
    // the accept/cancel pair as the action list; this screen only renders that push and
    // dispatches action ids verbatim. The MENU CONTROLLER owns the confirm callback and its
    // re-entrancy discipline (swap to a local, null the field, PopModal, then invoke) — nothing
    // here may touch confirmation state beyond dispatching MenuActions.ConfirmAccept/Cancel.
    [UiToolkitScreen(MenuActions.ConfirmModal, "Assets/Blockiverse/UI/Documents/ConfirmDialog.uxml",
        600, 460, UiToolkitPlacementProfile.Menu)]
    public sealed class ConfirmDialogController : UiToolkitScreenController, IUiToolkitActionMenuScreen
    {
        Label promptLabel;
        VisualElement actionsContainer;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // Last push, kept so a SetActionMenu that lands before Attach (or across a panel
        // rebuild) renders on the next OnAttach. The uGUI menu kept this state in persistent
        // serialized components; UI Toolkit element trees are rebuilt behind the controller.
        string pendingTitle;
        IReadOnlyList<MenuAction> pendingActions;

        readonly List<string> renderedActionIds = new();
        readonly List<(Button button, EventCallback<ClickEvent> callback)> renderedButtons = new();

        public override string ScreenId => MenuActions.ConfirmModal;

        // Ordered ids of the rendered buttons (mirrors BlockiverseActionMenu.ActionIds).
        public IReadOnlyList<string> RenderedActionIds => renderedActionIds;

        public void SetActionMenu(string title, IReadOnlyList<MenuAction> actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            pendingTitle = title;
            pendingActions = actions;
            RenderMenu();
        }

        // Click seam shared by the ClickEvent callbacks and the EditMode tests: ClickEvent
        // delivery needs a live panel, which EditMode never builds, so tests invoke this
        // directly (mirrors driving BlockiverseActionMenu via onClick.Invoke()).
        public void InvokeRenderedAction(int index)
        {
            if (index < 0 || index >= renderedActionIds.Count)
                return;

            string actionId = renderedActionIds[index];
            if (string.IsNullOrEmpty(actionId))
                return;

            // THIS is where a confirm and a cancel sound belong — on an actual confirmation and an
            // actual cancel, not on navigation. The host no longer plays anything for a route
            // change (see UiToolkitMenuHost), so these two are the only place a player hears
            // either cue from a button press, which is what makes them mean something.
            //
            // Every other button in the game keeps the plain click.
            BlockiverseAudioCue cue = actionId switch
            {
                MenuActions.ConfirmAccept => BlockiverseAudioCue.UiConfirm,
                MenuActions.ConfirmCancel => BlockiverseAudioCue.UiCancel,
                _ => BlockiverseAudioCue.UiSelect,
            };

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
            DispatchAction(actionId);
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            promptLabel = Require<Label>(root, "bv-confirm-prompt", ref allFound);
            actionsContainer = Require<VisualElement>(root, "bv-confirm-actions", ref allFound);

            if (pendingActions != null)
                RenderMenu();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            // Buttons are dynamic: RenderMenu wires each ClickEvent callback as it builds the
            // button, and OnUnregisterCallbacks releases them, so nothing static registers here.
        }

        protected override void OnUnregisterCallbacks()
        {
            ReleaseRenderedButtons();
        }

        protected override void OnDetach()
        {
            promptLabel = null;
            actionsContainer = null;
            renderedActionIds.Clear();
        }

        // Full rebuild on every push, mirroring BlockiverseActionMenu.SetMenu: the same modal
        // is re-pushed with different labels (default Confirm/Cancel vs. per-flow copy) and
        // the button set must always reflect exactly the latest list.
        void RenderMenu()
        {
            if (pendingActions == null)
                return;

            if (promptLabel != null)
                promptLabel.text = pendingTitle ?? string.Empty;

            if (actionsContainer == null)
                return;

            ReleaseRenderedButtons();
            actionsContainer.Clear();
            renderedActionIds.Clear();

            for (int i = 0; i < pendingActions.Count; i++)
            {
                MenuAction action = pendingActions[i];

                Button button = new Button();
                button.name = $"bv-confirm-action-{i}";
                // MenuAction.Label resolves LabelKey through the shared localization source and
                // returns keyless custom labels (RequestConfirm's per-flow copy) verbatim.
                button.text = action.Label;
                button.AddToClassList("hs-button");

                int index = i;
                EventCallback<ClickEvent> callback = _ => InvokeRenderedAction(index);
                button.RegisterCallback(callback);

                actionsContainer.Add(button);
                renderedButtons.Add((button, callback));
                renderedActionIds.Add(action.ActionId);
            }
        }

        void ReleaseRenderedButtons()
        {
            foreach ((Button button, EventCallback<ClickEvent> callback) in renderedButtons)
                button.UnregisterCallback(callback);

            renderedButtons.Clear();
        }
    }
}
