using System;
using System.IO;
using System.Text;

namespace Blockiverse.Persistence
{
    public static class WorldSaveSlotService
    {
        public const string SaveDirectoryExtension = ".vxlworld";

        // Allocates a unique save directory for a world name (existing paths get " (2)", " (3)"...)
        // and returns both the path and the Unicode-preserving, uniquified display name.
        public static (string path, string worldName) AllocateSavePath(string savesRoot, string worldName)
        {
            if (string.IsNullOrWhiteSpace(savesRoot))
                throw new ArgumentException("Saves root must be non-empty.", nameof(savesRoot));

            Directory.CreateDirectory(savesRoot);
            string displayBaseName = NormalizeDisplayWorldName(worldName);
            string directoryBaseName = SanitizeFileName(displayBaseName);

            // Bounded so a broken filesystem (or thousands of stale slots) fails loudly instead
            // of spinning forever; callers surface the failure through their UI/status channel.
            const int maxSuffix = 9999;
            for (int suffix = 1; suffix <= maxSuffix; suffix++)
            {
                string directoryName = suffix == 1 ? directoryBaseName : $"{directoryBaseName} ({suffix})";
                string candidate = Path.Combine(savesRoot, directoryName + SaveDirectoryExtension);
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                {
                    string displayName = suffix == 1 ? displayBaseName : $"{displayBaseName} ({suffix})";
                    return (candidate, displayName);
                }
            }

            throw new InvalidOperationException(
                $"Unable to allocate a save slot for \"{directoryBaseName}\" after {maxSuffix} attempts.");
        }

        // Path.GetInvalidFileNameChars() returns an empty array on Android, so '/' and '\' would
        // pass through and let a directory name escape the Saves root. Sanitize the directory stem
        // with a platform-independent allowlist, but keep the manifest/display name separate so
        // localized or emoji world names remain player-visible.
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "world";

            var builder = new StringBuilder(name.Length);
            foreach (char character in name)
                builder.Append(IsSafeFileNameChar(character) ? character : '_');

            string sanitized = builder.ToString().Trim();
            if (string.IsNullOrEmpty(sanitized) || sanitized == "." || sanitized == "..")
                return "world";

            return sanitized;
        }

        static string NormalizeDisplayWorldName(string name)
        {
            string trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) ? "world" : trimmed;
        }

        static bool IsSafeFileNameChar(char character)
        {
            return (character >= 'a' && character <= 'z') ||
                   (character >= 'A' && character <= 'Z') ||
                   (character >= '0' && character <= '9') ||
                   character == ' ' || character == '_' || character == '-' ||
                   character == '.' || character == '(' || character == ')';
        }
    }
}
