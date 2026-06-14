using System;

namespace Blockiverse.Core
{
    public static class BlockTextureSetIds
    {
        public const string Original = "original";
        public const string Enhanced = "enhanced";
        public const string AiSimplified = "ai_simplified";
        public const string Ai = "ai";

        public const string Default = Enhanced;

        public static readonly string[] MenuOptions =
        {
            Enhanced,
            AiSimplified,
            Ai,
            Original,
        };

        public static readonly string[] All =
        {
            Original,
            Enhanced,
            AiSimplified,
            Ai,
        };

        public static string Normalize(string textureSet)
        {
            if (string.IsNullOrWhiteSpace(textureSet))
                return Default;

            string trimmed = textureSet.Trim();
            foreach (string option in All)
                if (string.Equals(trimmed, option, StringComparison.OrdinalIgnoreCase))
                    return option;

            return Default;
        }
    }
}
