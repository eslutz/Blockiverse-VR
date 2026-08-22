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

        // Records a successful join. Existing entries for the same address are moved to the front
        // rather than duplicated, so the list reflects recency without growing.
        public static void Remember(string address, string nickname = null)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            try
            {
                var servers = new List<BlockiverseServerBookmark>(Load());
                servers.RemoveAll(entry =>
                    entry != null && string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase));

                servers.Insert(0, new BlockiverseServerBookmark(
                    string.IsNullOrWhiteSpace(nickname) ? address : nickname.Trim(),
                    address,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

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
