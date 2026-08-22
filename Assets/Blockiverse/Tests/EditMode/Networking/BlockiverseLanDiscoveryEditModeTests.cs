using System.Collections.Generic;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    /// <summary>
    /// Covers the discovered-session list itself — tracking, replacement, capacity, and expiry —
    /// by feeding beacons through <see cref="BlockiverseLanDiscovery.ApplyBeacon"/> rather than a
    /// real socket. Broadcast delivery is an environment behaviour and belongs in the on-device
    /// pass; the list logic is what regresses silently.
    /// </summary>
    public sealed class BlockiverseLanDiscoveryEditModeTests
    {
        const string JoinCode = "quest-room";

        readonly List<GameObject> objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objects)
                if (target != null)
                    Object.DestroyImmediate(target);

            objects.Clear();
        }

        [Test]
        public void ValidBeaconAppearsInTheSessionList()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();

            discovery.ApplyBeacon(Beacon("Host One", playerCount: 1), "192.168.1.20");

            Assert.That(discovery.ReceivedBeaconCount, Is.EqualTo(1));
            Assert.That(discovery.DiscoveredSessions, Has.Count.EqualTo(1));
            Assert.That(discovery.DiscoveredSessions[0].Address, Is.EqualTo("192.168.1.20"));
            Assert.That(discovery.DiscoveredSessions[0].HostName, Is.EqualTo("Host One"));
            Assert.That(discovery.DiscoveredSessions[0].PlayerCount, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedBeaconsFromOneHostDoNotDuplicateTheEntry()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();

            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");
            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");
            discovery.ApplyBeacon(Beacon("Host One", 2), "192.168.1.20");

            Assert.That(discovery.DiscoveredSessions, Has.Count.EqualTo(1));
            Assert.That(discovery.DiscoveredSessions[0].PlayerCount, Is.EqualTo(2), "The entry should refresh in place.");
        }

        [Test]
        public void BeaconsSignedWithADifferentJoinCodeAreCounted_ButNotListed()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();

            discovery.ApplyBeacon(
                BlockiverseLanDiscoveryBeacon.Encode(7777, 0, 2, "Stranger", "some-other-code"),
                "192.168.1.31");

            Assert.That(discovery.DiscoveredSessions, Is.Empty);
            Assert.That(discovery.RejectedBeaconCount, Is.EqualTo(1));
            Assert.That(discovery.ReceivedBeaconCount, Is.Zero);
        }

        [Test]
        public void SessionsExpireAfterTheirBeaconsStop()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();
            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");

            discovery.TickSessionExpiry(BlockiverseLanDiscovery.SessionExpirySeconds * 0.5f);
            Assert.That(discovery.DiscoveredSessions, Has.Count.EqualTo(1), "A session should survive a single missed beacon.");

            // A fresh beacon resets the clock — a live host must not drop off mid-browse.
            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");
            discovery.TickSessionExpiry(BlockiverseLanDiscovery.SessionExpirySeconds * 0.9f);
            Assert.That(discovery.DiscoveredSessions, Has.Count.EqualTo(1));

            discovery.TickSessionExpiry(BlockiverseLanDiscovery.SessionExpirySeconds);
            Assert.That(discovery.DiscoveredSessions, Is.Empty, "A host that stops beaconing should leave the list.");
        }

        [Test]
        public void SessionListIsCapped()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();

            for (int index = 0; index < BlockiverseLanDiscovery.MaxTrackedSessions + 5; index++)
                discovery.ApplyBeacon(Beacon($"Host {index}", 0), $"192.168.1.{40 + index}");

            Assert.That(
                discovery.DiscoveredSessions,
                Has.Count.EqualTo(BlockiverseLanDiscovery.MaxTrackedSessions),
                "A noisy network must not grow the list without bound.");
        }

        [Test]
        public void ChangeNotificationsFireOnlyWhenTheListActuallyChanges()
        {
            BlockiverseLanDiscovery discovery = CreateDiscovery();
            int changes = 0;
            discovery.DiscoveredSessionsChanged += () => changes++;

            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");
            Assert.That(changes, Is.EqualTo(1));

            // An identical repeat beacon is the common case at 1 Hz; it should not churn the UI.
            discovery.ApplyBeacon(Beacon("Host One", 1), "192.168.1.20");
            Assert.That(changes, Is.EqualTo(1));

            discovery.ApplyBeacon(Beacon("Host One", 2), "192.168.1.20");
            Assert.That(changes, Is.EqualTo(2), "A changed player count should refresh the list.");
        }

        static byte[] Beacon(string hostName, int playerCount) =>
            BlockiverseLanDiscoveryBeacon.Encode(7777, playerCount, 2, hostName, JoinCode);

        BlockiverseLanDiscovery CreateDiscovery()
        {
            GameObject sessionObject = new("Discovery Session");
            objects.Add(sessionObject);
            var networkManager = sessionObject.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig();
            sessionObject.AddComponent<UnityTransport>();
            BlockiverseNetworkSession session = sessionObject.AddComponent<BlockiverseNetworkSession>();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode(JoinCode));

            BlockiverseLanDiscovery discovery = sessionObject.AddComponent<BlockiverseLanDiscovery>();
            discovery.Configure(session);
            return discovery;
        }
    }
}
