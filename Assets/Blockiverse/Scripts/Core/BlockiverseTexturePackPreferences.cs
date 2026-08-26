using UnityEngine;

namespace Blockiverse.Core
{
    /// <summary>
    /// The player's own texture selection, stored per device.
    ///
    /// This exists because a texture token has two different scopes and conflating them breaks
    /// something in each direction:
    ///
    /// - A SAVED WORLD carries its own token, so different worlds can look different and reloading
    ///   one restores how it looked. That lives in the save manifest, not here.
    /// - A session with no save to consult — a fresh world, or joining someone else's multiplayer
    ///   game — needs a default, and the player's last choice is the only sensible one. That is
    ///   what this stores.
    ///
    /// It is emphatically NOT replicated. A multiplayer client resolves its own textures from this
    /// preference and never receives the host's, which is both the correct behaviour (each peer
    /// may have different packs installed) and the reason no pack bytes are ever sent between
    /// players.
    ///
    /// Uses PlayerPrefs to match <c>BlockiverseSettingsPersistence</c>, including its key prefix.
    /// It is deliberately a separate type rather than a field on that class: that one stores only
    /// ints and floats and detects changes by hashing them, and a string does not fit that scheme.
    /// </summary>
    public static class BlockiverseTexturePackPreferences
    {
        /// <summary>
        /// SHIPPED KEY — renaming it silently resets every player's choice, so treat the literal
        /// as permanent. Shares the prefix used by BlockiverseSettingsPersistence.
        /// </summary>
        public const string TokenPrefsKey = "Blockiverse.Settings.TexturePackToken";

        /// <summary>
        /// The token to use when nothing else supplies one. Always a valid token: a value written
        /// by an older build, hand-edited, or naming a pack that has since been deleted still
        /// normalizes rather than escaping as-is.
        ///
        /// Note this normalizes but does NOT resolve — a `pack:` token for an uninstalled pack is
        /// returned intact, exactly as it is from a save, so the caller can report the miss rather
        /// than the preference silently reverting.
        /// </summary>
        public static string Token
        {
            get => BlockiverseTextureSelection.NormalizeToken(PlayerPrefs.GetString(TokenPrefsKey, string.Empty));
            set
            {
                string normalized = BlockiverseTextureSelection.NormalizeToken(value);
                if (string.Equals(normalized, Token, System.StringComparison.Ordinal))
                    return;   // Avoid a disk write per frame if a slider-ish caller re-sets it.

                PlayerPrefs.SetString(TokenPrefsKey, normalized);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Drops the stored preference. Test hook and a plausible "reset settings" action.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(TokenPrefsKey);
            PlayerPrefs.Save();
        }
    }
}
