namespace Blockiverse.Core
{
    /// <summary>What resolving a texture token against the installed packs concluded.</summary>
    public enum BlockiverseTextureSelectionStatus
    {
        /// <summary>A built-in texture set. Always available; nothing can go wrong.</summary>
        BuiltIn,

        /// <summary>A pack token whose pack is installed and whose manifest validated.</summary>
        PackInstalled,

        /// <summary>
        /// A pack token naming a pack that is not installed. Ordinary and recoverable — the player
        /// moved it, renamed it, or has not reinstalled it yet — so it is reported, not swallowed.
        /// </summary>
        PackMissing,

        /// <summary>
        /// The pack directory exists but its manifest is absent, unparseable, or breaks a rule.
        /// Distinct from <see cref="PackMissing"/> because the fix is different: the player must
        /// repair the pack rather than reinstall it, and the message can say which field is wrong.
        /// </summary>
        PackInvalid,
    }

    /// <summary>
    /// The outcome of resolving a texture token, carrying BOTH what was asked for and what will
    /// actually be drawn.
    ///
    /// Keeping the two apart is the whole point of the type, and skipping it is how a naive
    /// implementation destroys player data: the requested token is what must be written back to
    /// the save, while the effective token is what the renderer binds. Collapse them and the first
    /// autosave after a failed resolve overwrites the player's pack selection with the fallback.
    /// </summary>
    public readonly struct BlockiverseTextureResolution
    {
        BlockiverseTextureResolution(
            BlockiverseTextureSelectionStatus status,
            string requestedToken,
            string effectiveToken,
            string requestedPackId,
            string failureDetail)
        {
            Status = status;
            RequestedToken = requestedToken;
            EffectiveToken = effectiveToken;
            RequestedPackId = requestedPackId;
            FailureDetail = failureDetail;
        }

        public BlockiverseTextureSelectionStatus Status { get; }

        /// <summary>The token as asked for, normalized. THIS is what round-trips to the save.</summary>
        public string RequestedToken { get; }

        /// <summary>The token that can actually be drawn right now. This is what the renderer gets.</summary>
        public string EffectiveToken { get; }

        /// <summary>The pack id that was asked for, or null for a built-in. Survives a failure so
        /// the player can be told WHICH pack is missing.</summary>
        public string RequestedPackId { get; }

        /// <summary>For <see cref="BlockiverseTextureSelectionStatus.PackInvalid"/>, the specific
        /// rule that was broken. Null otherwise.</summary>
        public string FailureDetail { get; }

        /// <summary>True when the requested selection could not be honoured and something else is
        /// being drawn instead — i.e. when the player is owed an explanation.</summary>
        public bool FellBack =>
            Status == BlockiverseTextureSelectionStatus.PackMissing
            || Status == BlockiverseTextureSelectionStatus.PackInvalid;

        public static BlockiverseTextureResolution BuiltIn(string token) =>
            new(BlockiverseTextureSelectionStatus.BuiltIn, token, token, null, null);

        public static BlockiverseTextureResolution PackInstalled(string token, string packId) =>
            new(BlockiverseTextureSelectionStatus.PackInstalled, token, token, packId, null);

        public static BlockiverseTextureResolution PackMissing(string token, string packId) =>
            new(BlockiverseTextureSelectionStatus.PackMissing, token, BlockTextureSetIds.Default, packId, null);

        public static BlockiverseTextureResolution PackInvalid(string token, string packId, string detail) =>
            new(BlockiverseTextureSelectionStatus.PackInvalid, token, BlockTextureSetIds.Default, packId, detail);
    }
}
