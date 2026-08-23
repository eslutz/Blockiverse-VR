using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Persistence
{
    [Serializable]
    public sealed class BlockiverseServerBookmark
    {
        public string nickname;
        public string address;
        public long lastJoinedUnixSeconds;

        // The join secret for this server, plaintext by decision: it is a room key shared by the
        // whole server, the headset is a single-user device, and encrypting it client-side with a
        // key stored beside it would be obfuscation sold as protection. Empty means none. It is
        // remembered only after the server accepted it, so a typo is never persisted.
        public string secret;

        // Transport security for this server. useTls turns DTLS on; tlsServerName is the
        // certificate name to validate (defaults to the bookmark host when empty). Validation is
        // against the CA bundle shipped in the client build -- UnityTLS has no OS trust store.
        public bool useTls;
        public string tlsServerName;

        // Operator-provided CA certificate (PEM) for servers not using a publicly trusted
        // certificate. Empty means validate against the client's shipped root bundle.
        public string tlsPinnedCaPem;

        public BlockiverseServerBookmark() { }

        public BlockiverseServerBookmark(string nickname, string address, long lastJoinedUnixSeconds)
        {
            this.nickname = nickname;
            this.address = address;
            this.lastJoinedUnixSeconds = lastJoinedUnixSeconds;
        }
    }

    [Serializable]
    sealed class BlockiverseServerBookmarkFile
    {
        public List<BlockiverseServerBookmark> servers = new();
    }

    // Remembered servers, most recently joined first.
    //
    // LAN discovery finds servers on the same subnet; it cannot find one across the internet, which
    // is the case a dedicated server exists for. Without this a player retypes an address with the
    // system keyboard every session, which in a headset is genuinely unpleasant.
    //
    // Written atomically through a temp file, matching how world saves are written: a torn
    // bookmark file after a crash would lose every remembered server at once.
    public static class BlockiverseServerBookmarkStore
    {
        public const int MaxBookmarks = 16;
        public const string FileName = "servers.json";

        static string ResolvePath() => Path.Combine(Application.persistentDataPath, FileName);

        public static IReadOnlyList<BlockiverseServerBookmark> Load()
        {
            try
            {
                string path = ResolvePath();
                if (!File.Exists(path))
                    return Array.Empty<BlockiverseServerBookmark>();

                var file = JsonUtility.FromJson<BlockiverseServerBookmarkFile>(File.ReadAllText(path));
                return file?.servers ?? new List<BlockiverseServerBookmark>();
            }
            catch (Exception exception)
            {
                // A corrupt bookmark file must not stop a player reaching the menu; they can still
                // type an address.
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Could not read remembered servers: {exception.Message}");
                return Array.Empty<BlockiverseServerBookmark>();
            }
        }

        /// <summary>Bookmark for an address, or null. Address comparison is case-insensitive.</summary>
        public static BlockiverseServerBookmark Find(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            foreach (BlockiverseServerBookmark entry in Load())
            {
                if (entry != null && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }

        // Records a successful join. Existing entries for the same address are moved to the front
        // rather than duplicated, so the list reflects recency without growing. Security fields
        // (secret, TLS) survive the move: passing null keeps what the entry already holds, so a
        // plain re-join never wipes a stored secret.
        public static void Remember(string address, string nickname = null, string secret = null,
            bool? useTls = null, string tlsServerName = null, string tlsPinnedCaPem = null)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            try
            {
                var servers = new List<BlockiverseServerBookmark>(Load());
                BlockiverseServerBookmark existing = null;
                foreach (BlockiverseServerBookmark entry in servers)
                {
                    if (entry != null && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = entry;
                        break;
                    }
                }

                servers.RemoveAll(entry =>
                    entry != null && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase));

                var refreshed = new BlockiverseServerBookmark(
                    string.IsNullOrWhiteSpace(nickname) ? (existing?.nickname ?? address) : nickname.Trim(),
                    address,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    secret = secret ?? existing?.secret ?? string.Empty,
                    useTls = useTls ?? existing?.useTls ?? false,
                    tlsServerName = tlsServerName ?? existing?.tlsServerName ?? string.Empty,
                    tlsPinnedCaPem = tlsPinnedCaPem ?? existing?.tlsPinnedCaPem ?? string.Empty,
                };
                servers.Insert(0, refreshed);

                if (servers.Count > MaxBookmarks)
                    servers.RemoveRange(MaxBookmarks, servers.Count - MaxBookmarks);

                Save(servers);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Could not remember server '{address}': {exception.Message}");
            }
        }

        public static void Forget(string address)
        {
            var servers = new List<BlockiverseServerBookmark>(Load());
            if (servers.RemoveAll(entry =>
                    entry != null && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                Save(servers);
            }
        }

        static void Save(List<BlockiverseServerBookmark> servers)
        {
            string path = ResolvePath();
            string temporary = path + ".tmp";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(temporary, JsonUtility.ToJson(
                    new BlockiverseServerBookmarkFile { servers = servers }, prettyPrint: true));

                // Replace rather than overwrite: an interrupted write leaves the previous list
                // intact instead of a truncated one.
                if (File.Exists(path))
                    File.Replace(temporary, path, destinationBackupFileName: null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Could not write remembered servers: {exception.Message}");

                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch (Exception)
                {
                    // Nothing further to do; the previous file is still the good one.
                }
            }
        }
    }
}
