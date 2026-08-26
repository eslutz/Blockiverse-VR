namespace Blockiverse.Core
{
    public static class WorldPresetIds
    {
        public const string SurvivalTerrain = "survival_terrain";
        public const string FlatBuilder = "flat_builder";

        public static readonly string[] MenuOptions =
        {
            SurvivalTerrain,
            FlatBuilder,
        };

        public static string Normalize(string presetId) =>
            string.IsNullOrWhiteSpace(presetId) ? SurvivalTerrain : presetId;
    }
}
