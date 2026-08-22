using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;

namespace Blockiverse.Server
{
    // Allowlist and banlist, backed by plain text files an operator can edit with any editor.
    //
    // One line per player identifier, '#' for comments. Files are re-read on change rather than
    // cached forever, so editing the banlist does not require a restart.
    //
    // These are the only moderation surface the server has. There is no reporting, no chat
    // filtering, and no automatic abuse detection -- documented rather than implied.
    public sealed class BlockiverseServerAccessControl
    {
        readonly BlockiverseServerOptions options;
        readonly HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> banned = new(StringComparer.OrdinalIgnoreCase);
        DateTime allowlistStamp;
        DateTime banlistStamp;

        public BlockiverseServerAccessControl(BlockiverseServerOptions options)
        {
            this.options = options;
            Reload();
        }

        public bool HasAllowlist => !string.IsNullOrEmpty(options.AllowlistPath);

        // A player may join when they are not banned, and either there is no allowlist or they are
        // on it. Checked in that order so a ban always wins over an allowlist entry.
        public bool IsAllowed(string playerId)
        {
            Reload();

            if (string.IsNullOrEmpty(playerId))
                return !HasAllowlist;

            if (banned.Contains(playerId))
                return false;

            return !HasAllowlist || allowed.Contains(playerId);
        }

        public string Ban(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return "usage: ban <playerId>";

            if (string.IsNullOrEmpty(options.BanlistPath))
                return "no security.banlist_path is configured; nothing to write to";

            banned.Add(playerId);
            return Persist(options.BanlistPath, banned, out string failure)
                ? $"banned {playerId}"
                : $"could not write the ban list: {failure}";
        }

        public string Unban(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return "usage: unban <playerId>";

            if (string.IsNullOrEmpty(options.BanlistPath))
                return "no security.banlist_path is configured";

            if (!banned.Remove(playerId))
                return $"{playerId} was not banned";

            return Persist(options.BanlistPath, banned, out string failure)
                ? $"unbanned {playerId}"
                : $"could not write the ban list: {failure}";
        }

        void Reload()
        {
            ReloadInto(options.AllowlistPath, allowed, ref allowlistStamp);
            ReloadInto(options.BanlistPath, banned, ref banlistStamp);
        }

        static void ReloadInto(string path, HashSet<string> target, ref DateTime stamp)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (!File.Exists(path))
                {
                    target.Clear();
                    return;
                }

                DateTime written = File.GetLastWriteTimeUtc(path);
                if (written == stamp)
                    return;

                stamp = written;
                target.Clear();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                        target.Add(line);
                }
            }
            catch (Exception exception)
            {
                // An unreadable list must not silently become an empty one: on an allowlist that
                // would open the server, which is the opposite of what the operator asked for.
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Networking,
                    $"Could not read access list '{path}': {exception.Message}. Keeping the previous contents.");
            }
        }

        static bool Persist(string path, HashSet<string> values, out string failureReason)
        {
            failureReason = null;
            try
            {
                var lines = new List<string>(values);
                lines.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(path, lines);
                return true;
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                return false;
            }
        }
    }
}
