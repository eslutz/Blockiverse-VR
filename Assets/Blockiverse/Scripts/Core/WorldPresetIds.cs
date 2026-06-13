namespace Blockiverse.Core
{
    public static class WorldPresetIds
    {
        public const string SurvivalTerrain = "survival_terrain";
        public const string FlatBuilder = "flat_builder";
        public const string VoidBuilder = "void_builder";

        public static readonly string[] MenuOptions =
        {
            SurvivalTerrain,
            FlatBuilder,
            VoidBuilder,
        };

        public static string Normalize(string presetId) =>
            string.IsNullOrWhiteSpace(presetId) ? SurvivalTerrain : presetId;
    }
}
