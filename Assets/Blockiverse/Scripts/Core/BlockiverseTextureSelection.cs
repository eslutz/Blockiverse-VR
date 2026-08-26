using System;

namespace Blockiverse.Core
{
    /// <summary>
    /// The vocabulary for "which block textures is this world drawn with".
    ///
    /// A <b>token</b> is either a built-in texture set id (<see cref="BlockTextureSetIds"/>) or a
    /// user-supplied pack, written <c>pack:&lt;pack_id&gt;</c>. Tokens are what the save manifest
    /// stores and what the world manager is set to; they are never sent over the network.
    ///
    /// WHY THE <c>pack:</c> PREFIX EXISTS. <see cref="BlockTextureSetIds.Normalize"/> coerces
    /// anything it does not recognise to <see cref="BlockTextureSetIds.Default"/>. That is right
    /// for a built-in id -- there are exactly four and an unknown one is corruption -- and wrong
    /// for a pack, because "the pack this world used is not installed right now" is an ordinary,
    /// recoverable situation that the player must be told about rather than silently robbed of.
    /// The prefix is what makes those two cases distinguishable: with it, a resolver can tell a
    /// missing pack from a bad built-in without consulting the filesystem, and the save can carry
    /// the player's real choice back out again after they reinstall the pack.
    ///
    /// This type is deliberately PURE SYNTAX and never touches the filesystem, so it is usable
    /// from Persistence and Networking and is trivially testable. Whether a pack is actually
    /// installed is a separate question, answered by the pack library.
    /// </summary>
    public static class BlockiverseTextureSelection
    {
        /// <summary>Marks a token as naming a user-supplied pack rather than a built-in set.</summary>
        public const string PackTokenPrefix = "pack:";

        /// <summary>
        /// Upper bound on a pack id, matching the pack-format spec. Long enough for a readable
        /// name, short enough that a token stays a sane length in a save manifest and a log line.
        /// </summary>
        public const int MaxPackIdLength = 48;

        /// <summary>The token every fallback lands on: the default built-in texture set.</summary>
        public static string DefaultToken => BlockTextureSetIds.Default;

        /// <summary>
        /// Canonicalises a token from a save, a preference, or a menu.
        ///
        /// Built-in ids and pack ids are both matched case-insensitively and returned lowercase,
        /// so a hand-edited manifest saying <c>Enhanced</c> or <c>pack:Mossy_Stones</c> still
        /// resolves. Anything that is neither a known built-in nor a syntactically valid pack
        /// token becomes <see cref="DefaultToken"/>.
        ///
        /// Note the asymmetry, which is the whole point of the type: an unknown BUILT-IN id is
        /// coerced away (it can only be corruption), whereas a well-formed pack token is preserved
        /// verbatim EVEN IF NO SUCH PACK EXISTS. Resolving that against what is installed happens
        /// later; losing it here would overwrite the player's choice on the next autosave.
        /// </summary>
        public static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return DefaultToken;

            string trimmed = token.Trim();

            if (trimmed.StartsWith(PackTokenPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string packId = trimmed.Substring(PackTokenPrefix.Length);
                return TryNormalizePackId(packId, out string normalized)
                    ? PackTokenPrefix + normalized
                    : DefaultToken;
            }

            // Not a pack token, so it must be a built-in id or it is corruption. Normalize already
            // has exactly the right behaviour for that case; do not reimplement it here.
            return BlockTextureSetIds.Normalize(trimmed);
        }

        /// <summary>True when the token names a user-supplied pack. Does not check installation.</summary>
        public static bool IsPackToken(string token)
        {
            return TryGetPackId(token, out _);
        }

        /// <summary>
        /// Extracts the pack id from a pack token, lowercased. False for a built-in token, for
        /// null/blank, and for a malformed pack token such as <c>pack:</c> or <c>pack:../x</c>.
        /// </summary>
        public static bool TryGetPackId(string token, out string packId)
        {
            packId = null;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            string trimmed = token.Trim();
            if (!trimmed.StartsWith(PackTokenPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return TryNormalizePackId(trimmed.Substring(PackTokenPrefix.Length), out packId);
        }

        /// <summary>
        /// Builds the token for a pack id. Throws on an id that would not survive
        /// <see cref="NormalizeToken"/>, so a bad id fails at the call site that invented it
        /// rather than silently becoming the default three layers away.
        /// </summary>
        public static string ForPack(string packId)
        {
            if (!TryNormalizePackId(packId, out string normalized))
                throw new ArgumentException(
                    $"Invalid texture pack id. Expected 1-{MaxPackIdLength} characters of a-z, 0-9 or underscore.",
                    nameof(packId));

            return PackTokenPrefix + normalized;
        }

        /// <summary>
        /// Whether a pack id is well formed: 1-48 characters of <c>a-z</c>, <c>0-9</c> or <c>_</c>,
        /// compared case-insensitively.
        /// </summary>
        public static bool IsValidPackId(string packId)
        {
            return TryNormalizePackId(packId, out _);
        }

        // The single definition of a well-formed pack id, so the validator and every caller that
        // needs the canonical form cannot disagree about it.
        //
        // The character set is restrictive ON PURPOSE. A pack id reaches the filesystem as a
        // directory name and reaches logs as a bare string, so `.`, `/` and `\` are excluded to
        // keep `pack:../../etc` from ever becoming a path, and the whole id is safe to log without
        // sanitising. Rejecting here means the pack library never has to defend against it.
        static bool TryNormalizePackId(string packId, out string normalized)
        {
            normalized = null;

            if (string.IsNullOrEmpty(packId))
                return false;

            if (packId.Length > MaxPackIdLength)
                return false;

            foreach (char character in packId)
            {
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '_';

                if (!allowed)
                    return false;
            }

            normalized = packId.ToLowerInvariant();
            return true;
        }
    }
}
