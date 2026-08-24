using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the error modal (uGUI: the "Error Dialog" BlockiverseActionMenu).
    // BlockiverseMenuController.ShowError pushes the (already localized) title with the
    // close-only action list via SetActionMenu and the message via SetScreenStatus; this
    // screen renders both and dispatches action ids verbatim (MenuActions.ErrorClose pops
    // the modal in the menu controller).
    [UiToolkitScreen(MenuActions.ErrorModal, "Assets/Blockiverse/UI/Documents/ErrorDialog.uxml",
        600, 460, UiToolkitPlacementProfile.Menu)]
    public sealed class ErrorDialogController : UiToolkitScreenController, IUiToolkitActionMenuScreen, IUiToolkitStatusScreen
    {
        Label titleLabel;
        Label messageLabel;
        VisualElement actionsContainer;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // Last pushes, kept so a SetActionMenu/SetStatus that lands before Attach (or across
        // a panel rebuild) renders on the next OnAttach. The uGUI menu kept this state in
        // persistent serialized components; UI Toolkit element trees are rebuilt behind the
        // controller.
        string pendingTitle;
        string pendingMessage;
        IReadOnlyList<MenuAction> pendingActions;

        readonly List<string> renderedActionIds = new();
        readonly List<(Button button, EventCallback<ClickEvent> callback)> renderedButtons = new();

        public override string ScreenId => MenuActions.ErrorModal;

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

        public void SetStatus(string message)
        {
            pendingMessage = message;

            if (messageLabel != null)
                messageLabel.text = message ?? string.Empty;
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

            // Same cue at the same moment as BlockiverseActionMenu.InvokeActionAt; the
            // show/hide cues stay with the host's visibility transitions.
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            DispatchAction(actionId);
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            titleLabel = Require<Label>(root, "bv-error-title", ref allFound);
            messageLabel = Require<Label>(root, "bv-error-message", ref allFound);
            actionsContainer = Require<VisualElement>(root, "bv-error-actions", ref allFound);

            if (pendingActions != null)
                RenderMenu();
            if (messageLabel != null && pendingMessage != null)
                messageLabel.text = pendingMessage;

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
            titleLabel = null;
            messageLabel = null;
            actionsContainer = null;
            renderedActionIds.Clear();
        }

        // Full rebuild on every push, mirroring BlockiverseActionMenu.SetMenu. The error list
        // is close-only today, but the rebuild keeps this screen correct if the pushed list
        // ever changes shape.
        void RenderMenu()
        {
            if (pendingActions == null)
                return;

            if (titleLabel != null)
                titleLabel.text = pendingTitle ?? string.Empty;

            if (actionsContainer == null)
                return;

            ReleaseRenderedButtons();
            actionsContainer.Clear();
            renderedActionIds.Clear();

            for (int i = 0; i < pendingActions.Count; i++)
            {
                MenuAction action = pendingActions[i];

                Button button = new Button();
                button.name = $"bv-error-action-{i}";
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
