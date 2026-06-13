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
        GameObject sessionObject;

        [TearDown]
        public void TearDown()
        {
            if (sessionObject != null)
                Object.DestroyImmediate(sessionObject);
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
            sessionObject = new GameObject("Network Session");
            var networkManager = sessionObject.AddComponent<NetworkManager>();
            networkManager.NetworkConfig = new NetworkConfig();
            sessionObject.AddComponent<UnityTransport>();
            BlockiverseNetworkSession session = sessionObject.AddComponent<BlockiverseNetworkSession>();
            session.Configure(BlockiverseNetworkConfig.Default);
            return session;
        }
    }
}
