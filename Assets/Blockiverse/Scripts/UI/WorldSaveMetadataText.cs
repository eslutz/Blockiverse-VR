using System;
using System.Globalization;

namespace Blockiverse.UI
{
    // The §6.5 World Details metadata block, formatted from a save manifest.
    //
    // Extracted from BlockiverseWorldDetailsPanel, where it was a static method on a uGUI
    // MonoBehaviour. WorldDetailsScreenController (UI Toolkit) called it there, which was the one
    // hard code dependency from the new menu stack onto a legacy panel — the panel could not be
    // deleted while it held the only copy of this formatting. Nothing here touches a view.
    public static class WorldSaveMetadataText
    {
        public static string Build(WorldSaveSummary save)
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

        static string FormatDate(DateTime utc)
        {
            return utc == DateTime.MinValue
                ? "—"
                : utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        }
    }
}
