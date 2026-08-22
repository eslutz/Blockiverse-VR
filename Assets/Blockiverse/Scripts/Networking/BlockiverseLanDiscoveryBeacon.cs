using System;
using System.Globalization;
using System.Text;

namespace Blockiverse.Networking
{
    /// <summary>A LAN session advertised by a host beacon.</summary>
    public readonly struct BlockiverseDiscoveredSession : IEquatable<BlockiverseDiscoveredSession>
    {
        public BlockiverseDiscoveredSession(
            string address,
            ushort port,
            string hostName,
            int playerCount,
            int maxPlayers)
        {
            Address = address;
            Port = port;
            HostName = hostName;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
        }

        /// <summary>The host's IPv4 address, taken from the packet source rather than its contents.</summary>
        public string Address { get; }

        /// <summary>The game port to join on, which is not the discovery port.</summary>
        public ushort Port { get; }

        public string HostName { get; }
        public int PlayerCount { get; }
        public int MaxPlayers { get; }

        public bool HasRoom => MaxPlayers <= 0 || PlayerCount < MaxPlayers;

        public bool Equals(BlockiverseDiscoveredSession other) =>
            Address == other.Address && Port == other.Port;

        public override bool Equals(object obj) => obj is BlockiverseDiscoveredSession other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Address, Port);
    }

    /// <summary>
    /// Encodes and decodes the UDP beacon a LAN host broadcasts so clients can find it without
    /// typing an IP address on a VR keyboard.
    ///
    /// The payload is signed with the session's join code, exactly like the connection-approval
    /// payload: a beacon is an unauthenticated broadcast that anything on the network can send,
    /// so an unsigned one must never make it into the join list. The signature proves the sender
    /// knows the join code; it is not a secrecy mechanism, and the beacon carries nothing
    /// sensitive.
    /// </summary>
    public static class BlockiverseLanDiscoveryBeacon
    {
        public const ushort DefaultDiscoveryPort = 7778;
        public const int ProtocolVersion = 1;
        public const int MaxPayloadBytes = 512;
        public const int MaxHostNameLength = 32;

        const string Magic = "blockiverse_discovery";
        const char Separator = '|';
        const int FieldCount = 6;
        const int PartCount = FieldCount + 1;

        public static byte[] Encode(
            ushort gamePort,
            int playerCount,
            int maxPlayers,
            string hostName,
            string joinCode)
        {
            string body = string.Join(
                Separator.ToString(),
                Magic,
                ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                gamePort.ToString(CultureInfo.InvariantCulture),
                playerCount.ToString(CultureInfo.InvariantCulture),
                maxPlayers.ToString(CultureInfo.InvariantCulture),
                SanitizeHostName(hostName));

            string signature = BlockiverseLanPayloadSigning.ComputeSignatureBase64(body, joinCode);
            return Encoding.UTF8.GetBytes(body + Separator + signature);
        }

        /// <summary>
        /// Parses a received beacon. <paramref name="sourceAddress"/> comes from the UDP packet's
        /// sender, never from the payload — a beacon must not be able to point joiners at a
        /// third-party address.
        /// </summary>
        public static bool TryDecode(
            byte[] payload,
            string sourceAddress,
            string joinCode,
            out BlockiverseDiscoveredSession session)
        {
            session = default;

            if (payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes)
                return false;

            if (string.IsNullOrWhiteSpace(sourceAddress))
                return false;

            string text;
            try
            {
                text = Encoding.UTF8.GetString(payload);
            }
            catch (ArgumentException)
            {
                return false;
            }

            string[] parts = text.Split(Separator);
            if (parts.Length != PartCount ||
                parts[0] != Magic ||
                parts[1] != ProtocolVersion.ToString(CultureInfo.InvariantCulture))
                return false;

            string body = string.Join(Separator.ToString(), parts, 0, FieldCount);
            if (!BlockiverseLanPayloadSigning.VerifySignatureBase64(body, joinCode, parts[FieldCount]))
                return false;

            if (!ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort gamePort) ||
                gamePort == 0)
                return false;

            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int playerCount) ||
                playerCount < 0)
                return false;

            if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxPlayers) ||
                maxPlayers < 0)
                return false;

            session = new BlockiverseDiscoveredSession(
                sourceAddress,
                gamePort,
                SanitizeHostName(parts[5]),
                playerCount,
                maxPlayers);
            return true;
        }

        /// <summary>
        /// Host names are player-visible and arrive from the network, so they are stripped of the
        /// field separator and anything non-printable, then truncated.
        /// </summary>
        public static string SanitizeHostName(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName))
                return "LAN Host";

            var builder = new StringBuilder(MaxHostNameLength);
            foreach (char character in hostName.Trim())
            {
                if (builder.Length >= MaxHostNameLength)
                    break;

                if (character == Separator || char.IsControl(character))
                    continue;

                builder.Append(character);
            }

            return builder.Length == 0 ? "LAN Host" : builder.ToString();
        }
    }
}
