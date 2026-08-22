using Blockiverse.Core;
using Blockiverse.Editor;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    /// <summary>
    /// Pins the generated NetworkManager prefab's transport configuration. These values are the
    /// difference between a late-join snapshot arriving and being silently dropped, and they are
    /// easy to lose in a bootstrapper edit because nothing at runtime complains — the messages
    /// just stop being delivered.
    /// </summary>
    public sealed class BlockiverseNetworkManagerPrefabEditModeTests
    {
        [Test]
        public void GeneratedNetworkManagerPrefabCarriesExplicitTransportLimits()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkManagerPrefabPath);
            Assert.That(prefab, Is.Not.Null, "The bootstrapper should have generated the NetworkManager prefab.");

            var transport = prefab.GetComponent<UnityTransport>();
            Assert.That(transport, Is.Not.Null);

            Assert.That(transport.MaxPayloadSize, Is.EqualTo(BlockiverseProjectBootstrapper.TransportMaxPayloadBytes));
            Assert.That(transport.MaxPacketQueueSize, Is.EqualTo(BlockiverseProjectBootstrapper.TransportMaxPacketQueueSize));
            Assert.That(transport.HeartbeatTimeoutMS, Is.EqualTo(BlockiverseProjectBootstrapper.TransportHeartbeatTimeoutMs));
            Assert.That(transport.ConnectTimeoutMS, Is.EqualTo(BlockiverseProjectBootstrapper.TransportConnectTimeoutMs));
            Assert.That(transport.MaxConnectAttempts, Is.EqualTo(BlockiverseProjectBootstrapper.TransportMaxConnectAttempts));
            Assert.That(transport.DisconnectTimeoutMS, Is.EqualTo(BlockiverseProjectBootstrapper.TransportDisconnectTimeoutMs));
        }

        [Test]
        public void SnapshotBatchesFitInsideTheConfiguredTransportPayload()
        {
            // The batch size and the payload ceiling are set in different files; this is the
            // assertion that keeps them honest about each other.
            int batchBytes = MultiplayerChunkAuthoritySync.SnapshotBatchHeaderBytes +
                             MultiplayerChunkAuthoritySync.SnapshotBatchMaxBlocks *
                             MultiplayerChunkAuthoritySync.SnapshotBlockBytes;

            Assert.That(batchBytes, Is.LessThan(BlockiverseProjectBootstrapper.TransportMaxPayloadBytes));

            // And under the stock 6 KB default too, so a peer whose prefab predates this
            // configuration still receives snapshots.
            Assert.That(batchBytes, Is.LessThan(6 * 1024));
        }

        [Test]
        public void GeneratedNetworkManagerPrefabCarriesTheLanDiscoveryComponent()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.NetworkManagerPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            // Discovery lives here rather than on the LAN panel because a host has to keep
            // beaconing while the panel is closed.
            Assert.That(prefab.GetComponent<BlockiverseLanDiscovery>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkManager>(), Is.Not.Null);
        }
    }
}
