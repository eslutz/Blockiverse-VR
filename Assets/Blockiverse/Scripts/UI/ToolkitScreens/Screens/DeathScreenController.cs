using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the death screen (uGUI: BlockiverseActionMenu on the death menu,
    // voxel_survival_menus §6.21). The controller pushes the title ("You Died") and the respawn
    // options through SetActionMenu — the bedroll entry is present only when a bedroll spawn is
    // set. The uGUI death menu has no status line, so this screen is not a status screen.
    [UiToolkitScreen(MenuActions.DeathScreen, "Assets/Blockiverse/UI/Documents/DeathScreen.uxml",
        570, 700, UiToolkitPlacementProfile.Menu)]
    public sealed class DeathScreenController : UiToolkitScreenController, IUiToolkitActionMenuScreen
    {
        Label titleLabel;
        ScrollView actionsView;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // The last push, cached so a re-attach (domain reload rebuilds the element tree) can
        // re-render without waiting for the controller's next push.
        string pendingTitle;
        readonly List<MenuAction> pendingActions = new();
        bool hasPendingMenu;

        readonly List<(Button button, EventCallback<ClickEvent> callback)> renderedButtons = new();

        public override string ScreenId => MenuActions.DeathScreen;

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

            if (titleLabel != null && hasPendingMenu)
                titleLabel.text = pendingTitle;

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
