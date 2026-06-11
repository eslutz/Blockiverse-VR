namespace Blockiverse.Gameplay
{
    public static class GameModeConstants
    {
        public const int CreativeUndoHistoryLimit = 50;

        // Region-edit volume caps (voxel_creative_ruleset §12.1). Delete shares the fill cap
        // (it is a fill with air).
        public const int CreativeMaxFillVolume = 32768;
        public const int CreativeMaxReplaceVolume = 32768;
        public const int CreativeMaxCopyVolume = 65536;
    }
}
