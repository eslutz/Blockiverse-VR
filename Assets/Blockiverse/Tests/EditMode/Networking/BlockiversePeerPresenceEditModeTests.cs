using Blockiverse.Networking;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    /// <summary>
    /// Join/leave notifications are only correct if "a peer arrived" and "a peer left" are
    /// answered from tracked presence rather than from a raw Netcode callback. Netcode reports
    /// a refused connection as a disconnect, reports the same host-side arrival twice, and tells
    /// a joining client about peers who were already there — each of which used to reach the
    /// player as a stinger and a toast.
    /// </summary>
    public sealed class BlockiversePeerPresenceEditModeTests
    {
        const ulong LocalId = 0;
        const ulong PeerId = 7;

        static BlockiversePeerPresence NewPresence() => new BlockiversePeerPresence();

        [Test]
        public void AFirstArrivalIsAnnounced()
        {
            BlockiversePeerPresence presence = NewPresence();
            Assert.That(presence.TryAddPeer(PeerId, LocalId), Is.True);
            Assert.That(presence.Contains(PeerId), Is.True);
            Assert.That(presence.Count, Is.EqualTo(1));
        }

        [Test]
        public void ADepartureIsAnnouncedOnlyForAPeerThatWasPresent()
        {
            BlockiversePeerPresence presence = NewPresence();
            presence.TryAddPeer(PeerId, LocalId);

            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.True);
            Assert.That(presence.Contains(PeerId), Is.False);
        }

        [Test]
        public void ARefusedJoinNeverAnnouncesADeparture()
        {
            // The host refuses a peer during connection approval, and Netcode then disconnects it.
            // The peer never became present, so the disconnect must not read as "player left".
            BlockiversePeerPresence presence = NewPresence();

            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.False,
                "a connection refused during approval was never present and cannot leave");
            Assert.That(presence.Count, Is.EqualTo(0));
        }

        [Test]
        public void TheHostsDuplicateArrivalNotificationIsAnnouncedOnce()
        {
            // A host receives both a client event and a peer event for the same arrival.
            BlockiversePeerPresence presence = NewPresence();

            Assert.That(presence.TryAddPeer(PeerId, LocalId), Is.True);
            Assert.That(presence.TryAddPeer(PeerId, LocalId), Is.False, "the second notification is the same arrival");
            Assert.That(presence.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheHostsDuplicateDepartureNotificationIsAnnouncedOnce()
        {
            BlockiversePeerPresence presence = NewPresence();
            presence.TryAddPeer(PeerId, LocalId);

            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.True);
            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.False, "the second notification is the same departure");
        }

        [Test]
        public void TheLocalSeatIsNeverItsOwnPeer()
        {
            BlockiversePeerPresence presence = NewPresence();

            Assert.That(presence.TryAddPeer(LocalId, LocalId), Is.False);
            Assert.That(presence.TryRemovePeer(LocalId, LocalId), Is.False);
            presence.AddKnownPeer(LocalId, LocalId);
            Assert.That(presence.Count, Is.EqualTo(0));
        }

        [Test]
        public void PeersAlreadyPresentOnJoinAreTrackedWithoutBeingAnnounced()
        {
            // A client that joins a world with two people already in it should hear nothing on
            // arrival, but must still hear each of them leave later.
            BlockiversePeerPresence presence = NewPresence();
            const ulong joiningClientId = 3;

            presence.AddKnownPeer(1, joiningClientId);
            presence.AddKnownPeer(2, joiningClientId);
            Assert.That(presence.Count, Is.EqualTo(2));

            Assert.That(presence.TryRemovePeer(1, joiningClientId), Is.True);
            Assert.That(presence.TryRemovePeer(2, joiningClientId), Is.True);
        }

        [Test]
        public void SeedingAPeerTwiceDoesNotDoubleCountIt()
        {
            BlockiversePeerPresence presence = NewPresence();
            presence.AddKnownPeer(PeerId, LocalId);
            presence.AddKnownPeer(PeerId, LocalId);

            Assert.That(presence.Count, Is.EqualTo(1));
            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.True);
            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.False);
        }

        [Test]
        public void ClearingDropsEveryPeerWithoutAnnouncingDepartures()
        {
            // Our own session ending is one event, not one departure per peer still in the world.
            BlockiversePeerPresence presence = NewPresence();
            presence.TryAddPeer(1, LocalId);
            presence.TryAddPeer(2, LocalId);

            presence.Clear();

            Assert.That(presence.Count, Is.EqualTo(0));
            Assert.That(presence.TryRemovePeer(1, LocalId), Is.False);
            Assert.That(presence.TryRemovePeer(2, LocalId), Is.False);
        }

        [Test]
        public void APeerCanRejoinAfterLeaving()
        {
            BlockiversePeerPresence presence = NewPresence();

            Assert.That(presence.TryAddPeer(PeerId, LocalId), Is.True);
            Assert.That(presence.TryRemovePeer(PeerId, LocalId), Is.True);
            Assert.That(presence.TryAddPeer(PeerId, LocalId), Is.True, "a reconnect is a fresh arrival");
        }
    }
}
