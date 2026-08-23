using System;
using System.Globalization;

namespace Blockiverse.Core
{
    // Parses what a player types into the join field.
    //
    // One field carrying "host" or "host:port" rather than two inputs, because every extra field is
    // another pass with the system keyboard in a headset, and a dedicated server on a non-default
    // port is the whole reason the port needs entering at all.
    //
    // Pure and engine-free so the parsing rules are testable without a scene.
    public readonly struct BlockiverseServerAddress
    {
        public const ushort DefaultPort = 7777;

        public string Host { get; }
        public ushort Port { get; }
        public bool HasExplicitPort { get; }

        public BlockiverseServerAddress(string host, ushort port, bool hasExplicitPort)
        {
            Host = host;
            Port = port;
            HasExplicitPort = hasExplicitPort;
        }

        // "host", "host:port", "[v6]:port" or "[v6]". Returns false on anything it cannot read,
        // rather than silently falling back to a default that would connect somewhere unintended.
        public static bool TryParse(string text, out BlockiverseServerAddress address)
        {
            address = default;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text.Trim();

            // Bracketed IPv6 keeps its own colons out of the host/port split.
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                int closing = trimmed.IndexOf(']');
                if (closing < 0)
                    return false;

                string v6Host = trimmed.Substring(1, closing - 1);
                if (v6Host.Length == 0)
                    return false;

                string v6Rest = trimmed.Substring(closing + 1);
                if (v6Rest.Length == 0)
                {
                    address = new BlockiverseServerAddress(v6Host, DefaultPort, hasExplicitPort: false);
                    return true;
                }

                if (!v6Rest.StartsWith(":", StringComparison.Ordinal))
                    return false;

                if (!TryParsePort(v6Rest.Substring(1), out ushort v6Port))
                    return false;

                address = new BlockiverseServerAddress(v6Host, v6Port, hasExplicitPort: true);
                return true;
            }

            int separator = trimmed.LastIndexOf(':');

            // More than one colon and no brackets is a bare IPv6 literal. Accept it as a host
            // rather than mangling its last group into a port.
            if (separator < 0 || trimmed.IndexOf(':') != separator)
            {
                address = new BlockiverseServerAddress(trimmed, DefaultPort, hasExplicitPort: false);
                return true;
            }

            string host = trimmed.Substring(0, separator);
            if (host.Length == 0)
                return false;

            if (!TryParsePort(trimmed.Substring(separator + 1), out ushort port))
                return false;

            address = new BlockiverseServerAddress(host, port, hasExplicitPort: true);
            return true;
        }

        static bool TryParsePort(string text, out ushort port)
        {
            port = 0;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return false;

            if (parsed < 1 || parsed > 65535)
                return false;

            port = (ushort)parsed;
            return true;
        }

        // Round-trips through TryParse. The port is shown only when it is not the default, so the
        // common case stays short in a headset.
        public override string ToString()
        {
            bool needsBrackets = Host != null && Host.IndexOf(':') >= 0;
            string host = needsBrackets ? "[" + Host + "]" : Host;
            return Port == DefaultPort && !HasExplicitPort
                ? host
                : host + ":" + Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
