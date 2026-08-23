using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the title menu (uGUI: BlockiverseActionMenu on "Title Menu",
    // voxel_survival_menus §6.2). The controller pushes the title (BlockiverseProject.ProductName)
    // and the availability-filtered action list through SetActionMenu, and the continue/load
    // status line through SetStatus; nothing on this screen is static.
    [UiToolkitScreen(MenuActions.TitleScreen, "Assets/Blockiverse/UI/Documents/TitleScreen.uxml",
        570, 860, UiToolkitPlacementProfile.Menu)]
    public sealed class TitleScreenController : UiToolkitScreenController,
        IUiToolkitActionMenuScreen, IUiToolkitStatusScreen
    {
        Label titleLabel;
        ScrollView actionsView;
        Label statusLabel;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // The last pushes, cached so a re-attach (domain reload rebuilds the element tree) can
        // re-render without waiting for the controller's next push.
        string pendingTitle;
        readonly List<MenuAction> pendingActions = new();
        bool hasPendingMenu;
        string pendingStatus;

        readonly List<(Button button, EventCallback<ClickEvent> callback)> renderedButtons = new();

        public override string ScreenId => MenuActions.TitleScreen;

        public void SetActionMenu(string title, IReadOnlyList<MenuAction> actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            pendingTitle = title ?? string.Empty;
            pendingActions.Clear();
            for (int i = 0; i < actions.Count; i++)
                pendingActions.Add(actions[i]);
            hasPendingMenu = true;

            if (titleLabel != null)
                titleLabel.text = pendingTitle;

            RebuildActionButtons();
        }

        public void SetStatus(string message)
        {
            pendingStatus = message ?? string.Empty;

            if (statusLabel != null)
                statusLabel.text = pendingStatus;
        }

        // The click path shared by every rendered action button. Public because EditMode tests
        // cannot deliver a ClickEvent without a runtime panel; this is the seam they press.
        // Cue parity with BlockiverseActionMenu.InvokeActionAt: UiSelect, then the action.
        public void PressAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            DispatchAction(actionId);
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            titleLabel = Require<Label>(root, "bv-title", ref allFound);
            actionsView = Require<ScrollView>(root, "bv-actions", ref allFound);
            statusLabel = Require<Label>(root, "bv-status", ref allFound);

            if (titleLabel != null && hasPendingMenu)
                titleLabel.text = pendingTitle;

            if (statusLabel != null && pendingStatus != null)
                statusLabel.text = pendingStatus;

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (hasPendingMenu)
                RebuildActionButtons();
        }

        protected override void OnUnregisterCallbacks() => UnregisterActionButtonCallbacks();

        protected override void OnDetach()
        {
            titleLabel = null;
            actionsView = null;
            statusLabel = null;
        }

        void UnregisterActionButtonCallbacks()
        {
            foreach ((Button button, EventCallback<ClickEvent> callback) in renderedButtons)
                button.UnregisterCallback(callback);

            renderedButtons.Clear();
        }

        // Availability re-pushes replace the whole list (mirrors BlockiverseActionMenu.SetMenu);
        // the old buttons' callbacks are unregistered before the container is cleared so the
        // registration balance can never drift across rebuilds.
        void RebuildActionButtons()
        {
            if (actionsView == null)
                return;

            UnregisterActionButtonCallbacks();
            actionsView.Clear();

            for (int i = 0; i < pendingActions.Count; i++)
            {
                MenuAction action = pendingActions[i];
                var button = new Button
                {
                    name = $"bv-action-{i}",
                    text = action.Label,
                };
                button.AddToClassList("hs-button");

                string actionId = action.ActionId;
                EventCallback<ClickEvent> callback = _ => PressAction(actionId);
                button.RegisterCallback(callback);
                renderedButtons.Add((button, callback));
                actionsView.Add(button);
            }
        }
    }
}
