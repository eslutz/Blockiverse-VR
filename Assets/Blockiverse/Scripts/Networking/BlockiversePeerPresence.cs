using System.Collections.Generic;

namespace Blockiverse.Networking
{
    /// <summary>
    /// Tracks which remote peers this seat currently believes are in the session, so that
    /// join/leave notifications describe real arrivals and departures.
    ///
    /// Netcode raises its connection callbacks more loosely than a notification needs:
    /// a host is told about a client it refused during approval, a host is told twice about
    /// the same peer (once as a client event and once as a peer event), and a joining client
    /// learns about the peers who were already present as part of its own connect event.
    /// Deduplicating against a set of known-present ids turns all of those into the one
    /// question a notification actually asks — did this peer's presence just change?
    /// </summary>
    public sealed class BlockiversePeerPresence
    {
        readonly HashSet<ulong> present = new HashSet<ulong>();

        public int Count => present.Count;

        public bool Contains(ulong clientId) => present.Contains(clientId);

        /// <summary>
        /// Records a peer as present. Returns true only the first time, i.e. only when the
        /// caller should announce an arrival. The local seat is never its own peer.
        /// </summary>
        public bool TryAddPeer(ulong clientId, ulong localClientId)
        {
            if (clientId == localClientId)
                return false;

            return present.Add(clientId);
        }

        /// <summary>
        /// Records a peer as gone. Returns true only if that peer was previously present —
        /// a connection that never completed approval was never present, so it cannot leave.
        /// </summary>
        public bool TryRemovePeer(ulong clientId, ulong localClientId)
        {
            if (clientId == localClientId)
                return false;

            return present.Remove(clientId);
        }

        /// <summary>
        /// Seeds a peer that was already in the session before this seat arrived. Nothing is
        /// announced — they did not just join — but they are now tracked, so their eventual
        /// departure is announced.
        /// </summary>
        public void AddKnownPeer(ulong clientId, ulong localClientId)
        {
            if (clientId != localClientId)
                present.Add(clientId);
        }

        public void Clear() => present.Clear();
    }
}
