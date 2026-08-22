using System.Collections.Generic;

namespace Blockiverse.Networking
{
    // Counts protocol violations per client and says when one has earned a disconnect.
    //
    // Rate limits alone only drop the excess: a client can hammer a channel forever and the server
    // quietly absorbs it, which on a LAN is a non-issue and on an internet-facing server is a free
    // denial of service. Violations were already counted; nothing acted on them.
    //
    // Violations decay, so a client that trips a limit once during a bad network moment is not
    // eventually disconnected for it hours later. Only sustained abuse accumulates.
    public sealed class BlockiverseAbuseLedger
    {
        public const int DefaultDisconnectThreshold = 60;
        public const double DefaultDecaySeconds = 30.0;

        readonly Dictionary<ulong, Entry> entries = new();
        readonly int disconnectThreshold;
        readonly double decaySeconds;

        struct Entry
        {
            public double Score;
            public double LastUpdated;
        }

        public BlockiverseAbuseLedger(
            int disconnectThreshold = DefaultDisconnectThreshold,
            double decaySeconds = DefaultDecaySeconds)
        {
            this.disconnectThreshold = disconnectThreshold > 0 ? disconnectThreshold : DefaultDisconnectThreshold;
            this.decaySeconds = decaySeconds > 0.0 ? decaySeconds : DefaultDecaySeconds;
        }

        // Records one violation. Returns true when this client has crossed the threshold and
        // should be disconnected.
        public bool RecordViolation(ulong clientId, double now, int weight = 1)
        {
            entries.TryGetValue(clientId, out Entry entry);

            // Linear decay toward zero since the last violation.
            if (entry.LastUpdated > 0.0)
            {
                double elapsed = now - entry.LastUpdated;
                if (elapsed > 0.0)
                    entry.Score -= entry.Score * (elapsed / decaySeconds);
                if (entry.Score < 0.0)
                    entry.Score = 0.0;
            }

            entry.Score += weight;
            entry.LastUpdated = now;
            entries[clientId] = entry;

            return entry.Score >= disconnectThreshold;
        }

        public double ScoreFor(ulong clientId) =>
            entries.TryGetValue(clientId, out Entry entry) ? entry.Score : 0.0;

        public void RemoveClient(ulong clientId) => entries.Remove(clientId);

        public void Clear() => entries.Clear();
    }
}
