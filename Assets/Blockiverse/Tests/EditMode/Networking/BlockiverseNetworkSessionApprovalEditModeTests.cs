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
        public void ApprovalPayloadCarriesTheCompatibilityFields()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room"));

            string[] parts = Encoding.UTF8.GetString(session.CreateApprovalPayload()).Split('|');

            Assert.That(parts.Length, Is.EqualTo(13), "Payload should carry 12 signed fields plus a signature.");
            Assert.That(parts[1], Is.EqualTo(BlockiverseNetworkSession.ApprovalPayloadProtocolVersion.ToString()));
            Assert.That(parts[7], Is.EqualTo(BlockiverseNetworkSession.LocalGameVersion));
            Assert.That(parts[9], Is.EqualTo(BlockiverseNetworkSession.LocalBlockRegistryHash));
            Assert.That(parts[10], Is.EqualTo(BlockiverseNetworkSession.LocalItemRegistryHash));
            Assert.That(parts[11], Is.EqualTo(BlockiverseNetworkSession.LocalRecipeRegistryHash));
            Assert.That(parts[9], Is.Not.Empty);
            Assert.That(parts[10], Is.Not.EqualTo(parts[9]));
            Assert.That(parts[11], Is.Not.EqualTo(parts[10]));
        }

        [Test]
        public void OlderProtocolPayloadIsRejectedAsProtocolMismatch()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room"));

            // A version-1 peer's payload has a different field count, so the protocol check has to
            // run before the shape check to produce an actionable reason.
            byte[] legacyPayload = Encoding.UTF8.GetBytes(
                "blockiverse_lan|1|voxel-networking-1|7777|2|lan_host_authoritative|meta_quest_party_chat_external|sig");

            Assert.That(
                session.ValidateConnectionRequest(legacyPayload, 0, out BlockiverseJoinRejectionReason reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(BlockiverseJoinRejectionReason.ProtocolMismatch));
        }

        [Test]
        public void MismatchedRegistryHashesAreRejectedWithTheSpecificReason()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room"));

            Assert.That(
                session.ValidateConnectionRequest(
                    ResignedPayloadWithField(session, fieldIndex: 9, value: "deadbeef", joinCode: "quest-room"),
                    0,
                    out BlockiverseJoinRejectionReason blockReason),
                Is.False);
            Assert.That(blockReason, Is.EqualTo(BlockiverseJoinRejectionReason.BlockRegistryMismatch));

            Assert.That(
                session.ValidateConnectionRequest(
                    ResignedPayloadWithField(session, fieldIndex: 10, value: "deadbeef", joinCode: "quest-room"),
                    0,
                    out BlockiverseJoinRejectionReason itemReason),
                Is.False);
            Assert.That(itemReason, Is.EqualTo(BlockiverseJoinRejectionReason.ItemRegistryMismatch));

            Assert.That(
                session.ValidateConnectionRequest(
                    ResignedPayloadWithField(session, fieldIndex: 11, value: "deadbeef", joinCode: "quest-room"),
                    0,
                    out BlockiverseJoinRejectionReason recipeReason),
                Is.False);
            Assert.That(recipeReason, Is.EqualTo(BlockiverseJoinRejectionReason.RecipeRegistryMismatch));
        }

        [Test]
        public void MismatchedGameVersionAndWorldSchemaAreRejectedWithTheSpecificReason()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room"));

            Assert.That(
                session.ValidateConnectionRequest(
                    ResignedPayloadWithField(session, fieldIndex: 7, value: "0.0.0-other", joinCode: "quest-room"),
                    0,
                    out BlockiverseJoinRejectionReason versionReason),
                Is.False);
            Assert.That(versionReason, Is.EqualTo(BlockiverseJoinRejectionReason.GameVersionMismatch));

            Assert.That(
                session.ValidateConnectionRequest(
                    ResignedPayloadWithField(session, fieldIndex: 8, value: "999", joinCode: "quest-room"),
                    0,
                    out BlockiverseJoinRejectionReason schemaReason),
                Is.False);
            Assert.That(schemaReason, Is.EqualTo(BlockiverseJoinRejectionReason.UnsupportedWorldVersion));
        }

        [Test]
        public void TamperedFieldWithoutAValidSignatureIsRejectedBeforeAnyContentComparison()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room"));

            // Same mutation as the registry test, but signed with the wrong join code. An
            // unauthenticated payload must never produce a specific "your blocks differ" answer.
            byte[] tampered = ResignedPayloadWithField(session, fieldIndex: 9, value: "deadbeef", joinCode: "wrong-code");

            Assert.That(
                session.ValidateConnectionRequest(tampered, 0, out BlockiverseJoinRejectionReason reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(BlockiverseJoinRejectionReason.InvalidJoinPayload));
        }

        [Test]
        public void SessionFullIsReportedAheadOfAnIncompatibleBuild()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(BlockiverseNetworkConfig.Default.WithJoinCode("quest-room").WithMaxPlayers(2));

            byte[] incompatible = ResignedPayloadWithField(session, fieldIndex: 9, value: "deadbeef", joinCode: "quest-room");

            Assert.That(
                session.ValidateConnectionRequest(incompatible, 2, out BlockiverseJoinRejectionReason reason),
                Is.False);
            Assert.That(reason, Is.EqualTo(BlockiverseJoinRejectionReason.SessionFull));
        }

        // Rebuilds the session's own payload with one field replaced, re-signed with the given
        // join code, so a test can isolate a single mismatch without hard-coding the layout.
        static byte[] ResignedPayloadWithField(
            BlockiverseNetworkSession session,
            int fieldIndex,
            string value,
            string joinCode)
        {
            string[] parts = Encoding.UTF8.GetString(session.CreateApprovalPayload()).Split('|');
            parts[fieldIndex] = value;
            string body = string.Join("|", parts, 0, parts.Length - 1);
            string signature = BlockiverseLanPayloadSigning.ComputeSignatureBase64(body, joinCode);
            return Encoding.UTF8.GetBytes(body + "|" + signature);
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
