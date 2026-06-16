namespace Blockiverse.MetaPlatform
{
    public static class BlockiversePlatformFeaturePolicy
    {
        public static bool ShouldAvoidMetaProfileLookup(BlockiverseUserAgeCategory category) =>
            category == BlockiverseUserAgeCategory.Child;

        public static bool CanUseMetaSocialFeature(BlockiverseUserAgeCategory category) =>
            category != BlockiverseUserAgeCategory.Child;

        public static bool ShouldKeepBaseGamePlayable(BlockiverseUserAgeCategory category) => true;

        public static string AvatarFallbackReason(BlockiverseUserAgeCategoryState state)
        {
            if (state.Category == BlockiverseUserAgeCategory.Child)
                return "Meta profile avatar is unavailable for child accounts; fallback avatar remains active.";

            if (state.Category == BlockiverseUserAgeCategory.Unknown)
                return "Meta age category is unavailable; fallback avatar remains active until platform services are ready.";

            return string.Empty;
        }
    }
}
