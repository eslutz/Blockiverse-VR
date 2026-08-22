using Blockiverse.Core;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode.Server
{
    // What a player types into the join field. The rules matter because a wrong parse connects
    // somewhere unintended rather than failing visibly.
    public sealed class BlockiverseServerAddressEditModeTests
    {
        [Test]
        public void BareHostUsesTheDefaultPort()
        {
            Assert.That(BlockiverseServerAddress.TryParse("192.168.1.20", out BlockiverseServerAddress address), Is.True);
            Assert.That(address.Host, Is.EqualTo("192.168.1.20"));
            Assert.That(address.Port, Is.EqualTo(BlockiverseServerAddress.DefaultPort));
            Assert.That(address.HasExplicitPort, Is.False);
        }

        [Test]
        public void HostAndPortAreSplit()
        {
            Assert.That(BlockiverseServerAddress.TryParse("play.example.com:7788", out BlockiverseServerAddress address), Is.True);
            Assert.That(address.Host, Is.EqualTo("play.example.com"));
            Assert.That(address.Port, Is.EqualTo(7788));
            Assert.That(address.HasExplicitPort, Is.True);
        }

        [Test]
        public void SurroundingWhitespaceIsIgnored()
        {
            Assert.That(BlockiverseServerAddress.TryParse("  10.0.0.5:7000  ", out BlockiverseServerAddress address), Is.True);
            Assert.That(address.Host, Is.EqualTo("10.0.0.5"));
            Assert.That(address.Port, Is.EqualTo(7000));
        }

        [Test]
        public void BareIpv6IsTreatedAsAHostNotAPort()
        {
            // "fe80::1" ends in ":1"; splitting on the last colon would silently connect to port 1.
            Assert.That(BlockiverseServerAddress.TryParse("fe80::1", out BlockiverseServerAddress address), Is.True);
            Assert.That(address.Host, Is.EqualTo("fe80::1"));
            Assert.That(address.Port, Is.EqualTo(BlockiverseServerAddress.DefaultPort));
            Assert.That(address.HasExplicitPort, Is.False);
        }

        [Test]
        public void BracketedIpv6SeparatesItsPort()
        {
            Assert.That(BlockiverseServerAddress.TryParse("[fe80::1]:7788", out BlockiverseServerAddress address), Is.True);
            Assert.That(address.Host, Is.EqualTo("fe80::1"));
            Assert.That(address.Port, Is.EqualTo(7788));

            Assert.That(BlockiverseServerAddress.TryParse("[fe80::1]", out BlockiverseServerAddress bare), Is.True);
            Assert.That(bare.Host, Is.EqualTo("fe80::1"));
            Assert.That(bare.Port, Is.EqualTo(BlockiverseServerAddress.DefaultPort));
        }

        [Test]
        public void UnreadableInputIsRefusedRatherThanDefaulted()
        {
            // Falling back to a default here would connect somewhere the player did not ask for.
            Assert.That(BlockiverseServerAddress.TryParse(null, out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse("   ", out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse("host:notaport", out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse("host:0", out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse("host:70000", out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse(":7777", out _), Is.False);
            Assert.That(BlockiverseServerAddress.TryParse("[fe80::1:7788", out _), Is.False);
        }

        [Test]
        public void ToStringRoundTripsAndHidesTheDefaultPort()
        {
            BlockiverseServerAddress.TryParse("example.com", out BlockiverseServerAddress bare);
            Assert.That(bare.ToString(), Is.EqualTo("example.com"),
                "The common case should stay short; a headset keyboard makes every character cost.");

            BlockiverseServerAddress.TryParse("example.com:7788", out BlockiverseServerAddress explicitPort);
            Assert.That(explicitPort.ToString(), Is.EqualTo("example.com:7788"));

            BlockiverseServerAddress.TryParse("[fe80::1]:7788", out BlockiverseServerAddress v6);
            Assert.That(v6.ToString(), Is.EqualTo("[fe80::1]:7788"));

            foreach (string text in new[] { "example.com", "example.com:7788", "[fe80::1]:7788" })
            {
                BlockiverseServerAddress.TryParse(text, out BlockiverseServerAddress first);
                Assert.That(BlockiverseServerAddress.TryParse(first.ToString(), out BlockiverseServerAddress second), Is.True);
                Assert.That(second.ToString(), Is.EqualTo(first.ToString()), $"'{text}' must round-trip");
            }
        }
    }
}
