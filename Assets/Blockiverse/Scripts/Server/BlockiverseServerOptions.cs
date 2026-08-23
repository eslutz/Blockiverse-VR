using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Blockiverse.Server
{
    public enum BlockiverseServerLogFormat
    {
        Text = 0,
        Json = 1
    }

    public enum BlockiverseServerLogLevel
    {
        Error = 0,
        Warn = 1,
        Info = 2,
        Debug = 3
    }

    // Fully resolved server configuration. Treated as immutable after resolution: everything is
    // decided once at startup so nothing later can quietly change where saves go or who may join.
    // Plain setters rather than `init` because `init` needs IsExternalInit, which this project's
    // .NET Standard profile does not provide. BlockiverseServerOptionsResolver is the only thing
    // that should ever write these.
    public sealed class BlockiverseServerOptions
    {
        public ushort Port { get; set; } = 7777;
        public string ListenAddress { get; set; } = "0.0.0.0";
        public string AdvertisedAddress { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 4;
        public string ServerName { get; set; } = "Blockiverse Server";
        public string Secret { get; set; } = string.Empty;
        public int FrameRate { get; set; } = 60;

        public string WorldDirectory { get; set; } = "./world";
        public string WorldName { get; set; } = "Blockiverse World";
        public int? WorldSeed { get; set; }
        public string WorldPreset { get; set; } = "survival_terrain";
        public string GameMode { get; set; } = "survival";

        public int AutoSaveSeconds { get; set; } = 60;
        public bool SaveOnStop { get; set; } = true;
        public int MaxStashedPlayers { get; set; } = 64;

        public bool RequireSecret { get; set; }
        public string AllowlistPath { get; set; } = string.Empty;
        public string BanlistPath { get; set; } = string.Empty;
        public bool TlsEnabled { get; set; }
        public string TlsCertificatePath { get; set; } = string.Empty;
        public string TlsKeyPath { get; set; } = string.Empty;
        public string TlsServerName { get; set; } = "blockiverse-server";

        // "none" or "meta". With "meta", clients must prove a Meta account (Users.GetUserProof)
        // and the server validates the proof against Meta's endpoint, giving real per-account
        // identity and bans by "meta:<userId>" entries in the ban list.
        public string IdentityProvider { get; set; } = "none";
        public string MetaAppId { get; set; } = string.Empty;
        // A PATH, not the secret itself: the secret must not sit in a config file that operators
        // paste into bug reports, so it lives in its own tightly-permissioned file.
        public string MetaAppSecretPath { get; set; } = string.Empty;

        public bool RequiresMetaIdentity =>
            string.Equals(IdentityProvider, "meta", StringComparison.OrdinalIgnoreCase);

        public BlockiverseServerLogLevel LogLevel { get; set; } = BlockiverseServerLogLevel.Info;
        public BlockiverseServerLogFormat LogFormat { get; set; } = BlockiverseServerLogFormat.Text;

        public bool AdminStdinEnabled { get; set; } = true;
        public string AdminSocketPath { get; set; } = string.Empty;

        // Player counts above this are honoured but unmeasured and unsupported: late join sends a
        // whole-world delta snapshot per joiner, and inventory snapshots broadcast per client at
        // 4 KB reliable-fragmented. Neither is profiled beyond four.
        public const int SupportedMaxPlayers = 4;

        public bool ExceedsSupportedPlayerCount => MaxPlayers > SupportedMaxPlayers;

        // The boot banner. Deliberately never prints Secret.
        public string Describe()
        {
            var text = new StringBuilder();
            void Line(string key, object value) =>
                text.Append("  ").Append(key.PadRight(22)).Append(value).Append('\n');

            text.Append("Blockiverse Dedicated Server\n");
            Line("name", ServerName);
            Line("listen", $"{ListenAddress}:{Port.ToString(CultureInfo.InvariantCulture)}/udp");
            if (!string.IsNullOrEmpty(AdvertisedAddress))
                Line("advertised", AdvertisedAddress);
            Line("max players", MaxPlayers + (ExceedsSupportedPlayerCount ? "  (UNSUPPORTED above 4)" : string.Empty));
            Line("world dir", WorldDirectory);
            Line("world name", WorldName);
            Line("preset", WorldPreset);
            Line("game mode", GameMode);
            Line("seed", WorldSeed.HasValue ? WorldSeed.Value.ToString(CultureInfo.InvariantCulture) : "(random on create)");
            Line("autosave", AutoSaveSeconds + "s");
            Line("secret", string.IsNullOrEmpty(Secret) ? "(default -- NOT private)" : "(set)");
            Line("require secret", RequireSecret);
            Line("tls", TlsEnabled);
            Line("identity", RequiresMetaIdentity ? $"meta (app {MetaAppId})" : "none");
            Line("log", $"{LogLevel.ToString().ToLowerInvariant()} / {LogFormat.ToString().ToLowerInvariant()}");
            Line("admin socket", string.IsNullOrEmpty(AdminSocketPath) ? "(default)" : AdminSocketPath);
            return text.ToString();
        }

        // Warnings worth printing at boot but which are not fatal.
        public IReadOnlyList<string> Advisories()
        {
            var advisories = new List<string>();

            if (ExceedsSupportedPlayerCount)
            {
                advisories.Add(
                    $"server.max_players is {MaxPlayers}. Counts above {SupportedMaxPlayers} are honoured but " +
                    "unmeasured and unsupported; late-join snapshots and inventory broadcasts are not profiled there.");
            }

            if (string.IsNullOrEmpty(Secret))
            {
                advisories.Add(
                    "No server.secret is set: anyone with a Blockiverse client can join. Players enter the " +
                    "secret in the multiplayer panel's password field; it is verified with a per-connection " +
                    "challenge, never sent in the clear.");
            }

            if (!TlsEnabled)
            {
                advisories.Add(
                    "Transport encryption is off. Traffic, including the identity token that grants inventory " +
                    "ownership on reconnect, is visible to anyone on the network path. With a DNS name and an " +
                    "ACME certificate, clients validate it with no setup on their side.");
            }

            return advisories;
        }
    }
}
