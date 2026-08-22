using System.Text;
using Blockiverse.Networking;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseLanDiscoveryBeaconEditModeTests
    {
        const string JoinCode = "quest-room";

        [Test]
        public void BeaconRoundTripsSessionDetails()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(
                gamePort: 7777,
                playerCount: 1,
                maxPlayers: 2,
                hostName: "Living Room Quest",
                joinCode: JoinCode);

            bool decoded = BlockiverseLanDiscoveryBeacon.TryDecode(
                payload,
                "192.168.1.44",
                JoinCode,
                out BlockiverseDiscoveredSession session);

            Assert.That(decoded, Is.True);
            Assert.That(session.Address, Is.EqualTo("192.168.1.44"));
            Assert.That(session.Port, Is.EqualTo((ushort)7777));
            Assert.That(session.HostName, Is.EqualTo("Living Room Quest"));
            Assert.That(session.PlayerCount, Is.EqualTo(1));
            Assert.That(session.MaxPlayers, Is.EqualTo(2));
            Assert.That(session.HasRoom, Is.True);
        }

        [Test]
        public void FullSessionIsDecodedButReportsNoRoom()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(7777, 2, 2, "Full Host", JoinCode);

            Assert.That(
                BlockiverseLanDiscoveryBeacon.TryDecode(payload, "10.0.0.5", JoinCode, out BlockiverseDiscoveredSession session),
                Is.True);
            Assert.That(session.HasRoom, Is.False);
        }

        [Test]
        public void BeaconSignedWithADifferentJoinCodeIsRejected()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(7777, 0, 2, "Someone Else", "other-room");

            Assert.That(
                BlockiverseLanDiscoveryBeacon.TryDecode(payload, "192.168.1.9", JoinCode, out _),
                Is.False);
        }

        [Test]
        public void TamperedBeaconIsRejected()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(7777, 0, 2, "Host", JoinCode);
            string text = Encoding.UTF8.GetString(payload);
            string[] parts = text.Split('|');

            // Re-point the join port while leaving the original signature in place: a beacon must
            // not be able to redirect joiners to a port the host never advertised.
            parts[2] = "9999";
            byte[] tampered = Encoding.UTF8.GetBytes(string.Join("|", parts));

            Assert.That(
                BlockiverseLanDiscoveryBeacon.TryDecode(tampered, "192.168.1.9", JoinCode, out _),
                Is.False);
        }

        [Test]
        public void MalformedAndOversizedBeaconsAreRejected()
        {
            Assert.That(BlockiverseLanDiscoveryBeacon.TryDecode(null, "192.168.1.9", JoinCode, out _), Is.False);
            Assert.That(
                BlockiverseLanDiscoveryBeacon.TryDecode(System.Array.Empty<byte>(), "192.168.1.9", JoinCode, out _),
                Is.False);
            Assert.That(
                BlockiverseLanDiscoveryBeacon.TryDecode(Encoding.UTF8.GetBytes("nonsense"), "192.168.1.9", JoinCode, out _),
                Is.False);

            byte[] oversized = new byte[BlockiverseLanDiscoveryBeacon.MaxPayloadBytes + 1];
            Assert.That(BlockiverseLanDiscoveryBeacon.TryDecode(oversized, "192.168.1.9", JoinCode, out _), Is.False);
        }

        [Test]
        public void DecodeRequiresAnActualSourceAddress()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(7777, 0, 2, "Host", JoinCode);

            Assert.That(BlockiverseLanDiscoveryBeacon.TryDecode(payload, null, JoinCode, out _), Is.False);
            Assert.That(BlockiverseLanDiscoveryBeacon.TryDecode(payload, "   ", JoinCode, out _), Is.False);
        }

        [Test]
        public void HostNamesAreSanitizedAndTruncated()
        {
            // The separator would split the payload into extra fields, and control characters
            // would render as garbage in the join list.
            Assert.That(BlockiverseLanDiscoveryBeacon.SanitizeHostName("a|b\nc"), Is.EqualTo("abc"));
            Assert.That(BlockiverseLanDiscoveryBeacon.SanitizeHostName("   "), Is.EqualTo("LAN Host"));
            Assert.That(BlockiverseLanDiscoveryBeacon.SanitizeHostName(null), Is.EqualTo("LAN Host"));
            Assert.That(
                BlockiverseLanDiscoveryBeacon.SanitizeHostName(new string('q', 100)).Length,
                Is.EqualTo(BlockiverseLanDiscoveryBeacon.MaxHostNameLength));
        }

        [Test]
        public void EncodedBeaconStaysWellUnderTheDatagramCap()
        {
            byte[] payload = BlockiverseLanDiscoveryBeacon.Encode(
                7777,
                4,
                4,
                new string('q', BlockiverseLanDiscoveryBeacon.MaxHostNameLength),
                JoinCode);

            Assert.That(payload.Length, Is.LessThan(BlockiverseLanDiscoveryBeacon.MaxPayloadBytes));
        }
    }
}
