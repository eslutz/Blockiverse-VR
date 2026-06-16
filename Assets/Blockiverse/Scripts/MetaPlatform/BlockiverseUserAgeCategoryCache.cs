using System;
using UnityEngine;

namespace Blockiverse.MetaPlatform
{
    public static class BlockiverseUserAgeCategoryCache
    {
        const string CategoryKey = "Blockiverse.MetaPlatform.UserAgeCategory";
        const string TimestampKey = "Blockiverse.MetaPlatform.UserAgeCategoryTimestamp";

        public static bool TryLoad(out BlockiverseUserAgeCategoryState state)
        {
            if (!PlayerPrefs.HasKey(CategoryKey))
            {
                state = default;
                return false;
            }

            string rawCategory = PlayerPrefs.GetString(CategoryKey, string.Empty);
            if (!Enum.TryParse(rawCategory, out BlockiverseUserAgeCategory category) ||
                category == BlockiverseUserAgeCategory.Unknown)
            {
                state = default;
                return false;
            }

            string rawTimestamp = PlayerPrefs.GetString(TimestampKey, "0");
            if (!long.TryParse(rawTimestamp, out long unixSeconds))
                unixSeconds = 0;

            state = new BlockiverseUserAgeCategoryState(
                category,
                BlockiverseUserAgeCategorySource.Cached,
                unixSeconds,
                "Using cached Meta user age category because live category is unavailable.");
            return true;
        }

        public static void Save(BlockiverseUserAgeCategoryState state)
        {
            if (!state.HasKnownCategory)
                return;

            PlayerPrefs.SetString(CategoryKey, state.Category.ToString());
            PlayerPrefs.SetString(TimestampKey, state.UnixSeconds.ToString());
            PlayerPrefs.Save();
        }

        public static void ClearForTests()
        {
            PlayerPrefs.DeleteKey(CategoryKey);
            PlayerPrefs.DeleteKey(TimestampKey);
        }
    }
}
