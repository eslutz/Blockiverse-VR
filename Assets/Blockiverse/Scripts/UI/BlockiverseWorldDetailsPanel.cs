using System.Globalization;
using TMPro;
using UnityEngine;

namespace Blockiverse.UI
{
    // World Details screen model/view (voxel_survival_menus §6.5): shows the selected save's
    // metadata and holds the pending rename text. The management buttons are a
    // BlockiverseActionMenu built by the bootstrapper; the session controller performs the
    // actual file operations.
    [DisallowMultipleComponent]
    public sealed class BlockiverseWorldDetailsPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text metadataLabel;
        [SerializeField] TMP_InputField renameField;

        public WorldSaveSummary? CurrentSave { get; private set; }

        // The rename input's current text (the new world name applied by world_details.rename).
        public string PendingRenameText => renameField != null ? renameField.text : string.Empty;

        public void Configure(TMP_Text metadata, TMP_InputField rename)
        {
            metadataLabel = metadata;
            renameField = rename;
        }

        public void ShowSave(WorldSaveSummary save)
        {
            CurrentSave = save;
            renameField?.SetTextWithoutNotify(save.Name);

            if (metadataLabel != null)
                metadataLabel.text = BuildMetadataText(save);
        }

        public void Clear()
        {
            CurrentSave = null;
            renameField?.SetTextWithoutNotify(string.Empty);
            if (metadataLabel != null)
                metadataLabel.text = string.Empty;
        }

        // §6.5 metadata block, limited to what the save manifest tracks today.
        public static string BuildMetadataText(WorldSaveSummary save)
        {
            string mode = BlockiverseLocalization.DisplayNameForCanonicalId(save.GameMode);
            string difficulty = BlockiverseLocalization.DisplayNameForCanonicalId(save.Difficulty);

            return BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.WorldDetailsMetadata,
                mode,
                difficulty,
                save.DayCount,
                save.Seed,
                FormatDate(save.CreatedUtc),
                FormatDate(save.LastPlayedUtc));
        }

        static string FormatDate(System.DateTime utc)
        {
            return utc == System.DateTime.MinValue
                ? "—"
                : utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        }

    }
}
