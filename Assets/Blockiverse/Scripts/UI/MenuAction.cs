using System;

namespace Blockiverse.UI
{
    // A single labelled action presented by the UI Toolkit menu surface
    // (voxel_survival_menus section 9).
    public readonly struct MenuAction
    {
        public MenuAction(string actionId, string label)
            : this(actionId, null, label)
        {
        }

        public MenuAction(string actionId, string labelKey, string defaultLabel)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Menu action ids must be non-empty.", nameof(actionId));
            if (string.IsNullOrWhiteSpace(labelKey) && string.IsNullOrWhiteSpace(defaultLabel))
                throw new ArgumentException("Menu action labels must be non-empty.", nameof(defaultLabel));

            ActionId = actionId;
            LabelKey = labelKey;
            this.defaultLabel = defaultLabel;
        }

        readonly string defaultLabel;

        public string ActionId { get; }
        public string LabelKey { get; }
        public string Label => BlockiverseLocalization.Text(LabelKey, defaultLabel);
    }
}
