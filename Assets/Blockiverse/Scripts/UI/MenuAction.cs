using System;

namespace Blockiverse.UI
{
    // A single labelled action presented by a button-list menu (voxel_survival_menus §9).
    //
    // This lived inside BlockiverseActionMenu.cs next to the uGUI presenter that consumed it,
    // which made the file look deletable along with the rest of the uGUI menus. It is not: the
    // struct is the action-list vocabulary shared by every UI Toolkit action screen, the menu
    // router, and IBlockiverseMenuFrontend. It has no presentation dependency of its own.
    public readonly struct MenuAction
    {
        public MenuAction(string actionId, string label)
            : this(actionId, null, label)
        {
        }

        public MenuAction(string actionId, string labelKey, string fallbackLabel)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Menu action ids must be non-empty.", nameof(actionId));
            if (string.IsNullOrWhiteSpace(labelKey) && string.IsNullOrWhiteSpace(fallbackLabel))
                throw new ArgumentException("Menu action labels must be non-empty.", nameof(fallbackLabel));

            ActionId = actionId;
            LabelKey = labelKey;
            this.fallbackLabel = fallbackLabel;
        }

        readonly string fallbackLabel;

        public string ActionId { get; }
        public string LabelKey { get; }
        public string Label => BlockiverseLocalization.Text(LabelKey, fallbackLabel);
    }
}
