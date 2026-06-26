using System.Globalization;

namespace Blockiverse.UI
{
    public static class WorldDetailsMetadataFormatter
    {
        // Section 6.5 metadata block, limited to what the save manifest tracks today.
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
                ? "-"
                : utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        }
    }
}
