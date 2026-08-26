using System;
using System.Text;

namespace Blockiverse.Core
{
    /// <summary>
    /// `blockiverse-pack.json` — the declaration at the root of a user-supplied texture pack.
    ///
    /// Serialized with <c>JsonUtility</c> to match the rest of this project's persistence, which
    /// constrains the shape: a flat object of public fields, no dictionaries, no nullable value
    /// types, and JSON key == field name.
    ///
    /// JsonUtility also fails SILENTLY in two ways that shape every rule in
    /// <see cref="TryValidate"/>: it ignores unknown keys (which is what makes the format
    /// forward-compatible), and it leaves a missing field at its default rather than reporting it.
    /// So a missing <c>formatVersion</c> arrives as 0 and a missing string as null — every
    /// requirement below is therefore an explicit post-parse check, never an assumption that
    /// parsing succeeded means the document was complete.
    ///
    /// This is UNTRUSTED INPUT. It is read from a directory the player can write to, so every
    /// field is bounded and every string is sanitised before it reaches a label or a log line.
    /// </summary>
    [Serializable]
    public sealed class BlockiverseTexturePackManifest
    {
        /// <summary>The only format version this build understands.</summary>
        public const int CurrentFormatVersion = 1;

        public const int MaxDisplayNameLength = 48;
        public const int MaxAuthorLength = 48;
        public const int MaxPackVersionLength = 16;
        public const int MaxDescriptionLength = 240;
        public const int MaxLicenseLength = 64;
        public const int MaxAttributionLength = 240;

        /// <summary>
        /// Tile sizes a pack may declare. Capped at 64 because the composited atlas scales with
        /// it: 64 px tiles already mean a 1152x960 atlas, and 128 would put the transient cost of
        /// building one at roughly 54 MiB while a world is resident on a Quest — for a resolution
        /// the game's own 32 px art direction never targets.
        /// </summary>
        public static readonly int[] SupportedTilePixels = { 16, 32, 64 };

        public int formatVersion;
        public string packId;
        public string displayName;
        public string author;
        public string packVersion;
        public string description;
        public string license;
        public string attribution;
        public int tilePixels;
        public string baseTextureSet;
        public string minGameVersion;

        /// <summary>
        /// Checks every rule the parser cannot. <paramref name="expectedPackId"/> is the directory
        /// name the manifest was found in; the two must agree so that "is this pack installed?"
        /// can be answered by a directory probe rather than by parsing every manifest on disk, and
        /// so two directories cannot both claim one id.
        ///
        /// Returns false with a player-facing <paramref name="error"/> describing the specific
        /// rule broken — a pack author's most common need is to be told which field is wrong.
        /// </summary>
        public bool TryValidate(string expectedPackId, out string error)
        {
            if (formatVersion != CurrentFormatVersion)
            {
                error = formatVersion == 0
                    ? "missing or zero \"formatVersion\" (expected 1)"
                    : $"unsupported \"formatVersion\" {formatVersion} (this build understands {CurrentFormatVersion})";
                return false;
            }

            if (!BlockiverseTextureSelection.IsValidPackId(packId))
            {
                error = $"invalid \"packId\": expected 1-{BlockiverseTextureSelection.MaxPackIdLength} characters of a-z, 0-9 or underscore";
                return false;
            }

            if (!string.IsNullOrEmpty(expectedPackId)
                && !string.Equals(packId, expectedPackId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"\"packId\" is '{packId}' but the folder is named '{expectedPackId}'; they must match";
                return false;
            }

            string cleanedDisplayName = Sanitize(displayName, MaxDisplayNameLength);
            if (string.IsNullOrEmpty(cleanedDisplayName))
            {
                error = "missing \"displayName\"";
                return false;
            }

            if (!IsSupportedTileSize(tilePixels))
            {
                error = tilePixels == 0
                    ? "missing \"tilePixels\" (expected 16, 32 or 64)"
                    : $"unsupported \"tilePixels\" {tilePixels} (expected 16, 32 or 64)";
                return false;
            }

            // Everything below is advisory: a pack that gets these wrong still loads, because none
            // of them can make the atlas wrong. Only the fields above can.
            packId = packId.ToLowerInvariant();
            displayName = cleanedDisplayName;
            author = Sanitize(author, MaxAuthorLength);
            packVersion = Sanitize(packVersion, MaxPackVersionLength);
            description = Sanitize(description, MaxDescriptionLength);
            license = Sanitize(license, MaxLicenseLength);
            attribution = Sanitize(attribution, MaxAttributionLength);

            // An unknown base coerces rather than failing. Unlike a pack id, a built-in set id has
            // exactly four legal values and no information worth preserving in a wrong one -- and
            // refusing to load a whole pack over the choice of FALLBACK texture would be absurd.
            baseTextureSet = BlockTextureSetIds.Normalize(baseTextureSet);

            error = null;
            return true;
        }

        public static bool IsSupportedTileSize(int candidate)
        {
            foreach (int supported in SupportedTilePixels)
                if (candidate == supported)
                    return true;

            return false;
        }

        /// <summary>
        /// Trims, strips control characters, and truncates. Pack metadata is rendered verbatim in
        /// the UI and must never be looked up as a localization key, so a pack named
        /// <c>ui.status.crate.shared</c> stays that literal string. Control characters are removed
        /// because a newline or an ANSI escape in a display name corrupts both the UI layout and
        /// any log line that quotes it.
        /// </summary>
        public static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (char character in value)
            {
                if (char.IsControl(character))
                    continue;

                builder.Append(character);
                if (builder.Length >= maxLength)
                    break;
            }

            return builder.ToString().Trim();
        }
    }
}
