namespace Blockiverse.Networking
{
    /// <summary>
    /// Why a LAN join was refused (ruleset §5). The name of the value is sent as the Netcode
    /// connection-approval reason, arrives on the client as
    /// <c>NetworkManager.DisconnectReason</c>, and is mapped to player-facing text by the
    /// multiplayer session menu — the Networking assembly has no access to localization.
    /// </summary>
    public enum BlockiverseJoinRejectionReason
    {
        None = 0,

        /// <summary>Payload was missing, malformed, over-long, or failed its signature check.</summary>
        InvalidJoinPayload,

        /// <summary>The joining build speaks a different handshake protocol version.</summary>
        ProtocolMismatch,

        /// <summary>The joining build reports a different application version.</summary>
        GameVersionMismatch,

        /// <summary>Block registry contents differ, so block ids would not resolve identically.</summary>
        BlockRegistryMismatch,

        /// <summary>Item registry contents differ, so item ids would not resolve identically.</summary>
        ItemRegistryMismatch,

        /// <summary>Crafting recipes differ, so crafting results would diverge.</summary>
        RecipeRegistryMismatch,

        /// <summary>The joining build reads or writes a different world save schema.</summary>
        UnsupportedWorldVersion,

        /// <summary>The session already holds its configured maximum number of players.</summary>
        SessionFull,
    }
}
