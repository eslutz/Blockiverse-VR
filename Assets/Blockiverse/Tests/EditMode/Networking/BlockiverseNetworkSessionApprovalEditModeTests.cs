using System.Collections.Generic;
using System.Text;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseNetworkSessionApprovalEditModeTests
    {
        readonly List<GameObject> sessionObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject sessionObject in sessionObjects)
                if (sessionObject != null)
                    Object.DestroyImmediate(sessionObject);

            sessionObjects.Clear();
        }

        [Test]
        public void ConfigureEnablesApprovalAndPublishesSignedPayload()
        {
            BlockiverseNetworkSession session = CreateSession();
            var config = BlockiverseNetworkConfig.Default
                .WithPort(7788)
                .WithMaxPlayers(2)
                .WithJoinCode("quest-room");

            session.Configure(config);

            Assert.That(session.NetworkManager.NetworkConfig.ConnectionApproval, Is.True);
            Assert.That(session.NetworkManager.ConnectionApprovalCallback, Is.Not.Null);
            CollectionAssert.AreEqual(session.CreateApprovalPayload(), session.NetworkManager.NetworkConfig.ConnectionData);
            Assert.That(session.ValidateConnectionRequest(session.CreateApprovalPayload(), 1, out string reason), Is.True);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void ApprovalRejectsWrongJoinPayloadAndFullSession()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room").WithMaxPlayers(2));

            Assert.That(
                session.ValidateConnectionRequest(Encoding.UTF8.GetBytes("wrong"), 1, out string wrongReason),
                Is.False);
            Assert.That(wrongReason, Is.EqualTo("InvalidJoinPayload"));

            Assert.That(
                session.ValidateConnectionRequest(session.CreateApprovalPayload(), 2, out string fullReason),
                Is.False);
            Assert.That(fullReason, Is.EqualTo("SessionFull"));
        }

        [Test]
        public void ApprovalDoesNotRequireClientToMirrorHostCapacity()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default
                .WithPort(7788)
                .WithJoinCode("quest-room")
                .WithMaxPlayers(2));
            byte[] defaultCapacityPayload = session.CreateApprovalPayload();

            session.Configure(BlockiverseNetworkConfig.Default
                .WithPort(7788)
                .WithJoinCode("quest-room")
                .WithMaxPlayers(4));

            Assert.That(
                session.ValidateConnectionRequest(defaultCapacityPayload, 2, out string reason),
                Is.True);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void EncryptedTransportRequestFailsClosedWithoutPemMaterial()
        {
            BlockiverseNetworkSession session = CreateSession();

            session.ConfigureTransportSecurity(
                enabled: true,
                serverCertificate: string.Empty,
                serverPrivateKey: string.Empty,
                serverCommonName: "blockiverse-lan",
                clientCaCertificate: string.Empty);

            Assert.That(session.IsTransportEncryptionRequested, Is.True);
            Assert.That(session.IsTransportEncryptionConfigured, Is.False);
            Assert.That(session.StartHost(), Is.False);
            Assert.That(session.CurrentState, Is.EqualTo(BlockiverseConnectionState.Failed));
            Assert.That(session.LastDisconnectReason, Does.Contain("Encrypted LAN transport requires"));
            Assert.That(session.UnityTransport.UseEncryption, Is.False);
        }

        BlockiverseNetworkSession CreateSession()
        {
            GameObject sessionObject = new("Network Session");
            sessionObjects.Add(sessionObject);
            var networkManager = sessionObject.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig();
            sessionObject.AddComponent<UnityTransport>();
            BlockiverseNetworkSession session = sessionObject.AddComponent<BlockiverseNetworkSession>();
            session.Configure(BlockiverseNetworkConfig.Default);
            return session;
        }
    }
}
