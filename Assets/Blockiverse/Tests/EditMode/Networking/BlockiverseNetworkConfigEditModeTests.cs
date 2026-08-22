using Blockiverse.Networking;
using NUnit.Framework;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseNetworkConfigEditModeTests
    {
        [Test]
        public void DefaultConfigUsesLanHostClientSettings()
        {
            BlockiverseNetworkConfig config = BlockiverseNetworkConfig.Default;

            Assert.That(config.Address, Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
            Assert.That(config.ListenAddress, Is.EqualTo(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(config.Port, Is.EqualTo(BlockiverseNetworkConfig.DefaultPort));
            Assert.That(config.MaxPlayers, Is.EqualTo(BlockiverseNetworkConfig.DefaultMaxPlayers));
            Assert.That(config.JoinCode, Is.EqualTo(BlockiverseNetworkConfig.DefaultJoinCode));
        }

        [Test]
        public void BlankAddressAndZeroPortFallBackToDefaults()
        {
            var config = new BlockiverseNetworkConfig(string.Empty, string.Empty, 0, 0, string.Empty);

            Assert.That(config.Address, Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
            Assert.That(config.ListenAddress, Is.EqualTo(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(config.Port, Is.EqualTo(BlockiverseNetworkConfig.DefaultPort));
            Assert.That(config.MaxPlayers, Is.EqualTo(BlockiverseNetworkConfig.DefaultMaxPlayers));
            Assert.That(config.JoinCode, Is.EqualTo(BlockiverseNetworkConfig.DefaultJoinCode));
        }

        [Test]
        public void WithAddressPortMaxPlayersAndJoinCodePreserveOtherSettings()
        {
            BlockiverseNetworkConfig config = BlockiverseNetworkConfig.Default
                .WithAddress("192.168.1.47")
                .WithPort(7888)
                .WithMaxPlayers(3)
                .WithJoinCode("room-42");

            Assert.That(config.Address, Is.EqualTo("192.168.1.47"));
            Assert.That(config.ListenAddress, Is.EqualTo(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(config.Port, Is.EqualTo(7888));
            Assert.That(config.MaxPlayers, Is.EqualTo(3));
            Assert.That(config.JoinCode, Is.EqualTo("room-42"));

            BlockiverseNetworkConfig changedAddress = config.WithAddress("192.168.1.99");

            Assert.That(changedAddress.Port, Is.EqualTo(7888));
            Assert.That(changedAddress.MaxPlayers, Is.EqualTo(3));
            Assert.That(changedAddress.JoinCode, Is.EqualTo("room-42"));
        }

        [Test]
        public void MaxPlayersIsOperatorConfiguredAboveTheSupportedCount()
        {
            // A nonsensical count still falls back to the default: zero or negative players is a
            // mistake, not a choice.
            Assert.That(BlockiverseNetworkConfig.Default.WithMaxPlayers(-1).MaxPlayers, Is.EqualTo(BlockiverseNetworkConfig.DefaultMaxPlayers));
            Assert.That(BlockiverseNetworkConfig.Default.WithMaxPlayers(0).MaxPlayers, Is.EqualTo(BlockiverseNetworkConfig.DefaultMaxPlayers));

            // But a deliberate count above the supported one is HONOURED rather than clamped. A
            // dedicated-server operator sets what they want; above MaxSupportedPlayers it is
            // unmeasured and unsupported, and the server warns at boot rather than silently
            // seating fewer players than the operator configured.
            Assert.That(BlockiverseNetworkConfig.Default.WithMaxPlayers(99).MaxPlayers, Is.EqualTo(99));
            Assert.That(BlockiverseNetworkConfig.MaxSupportedPlayers, Is.EqualTo(4),
                "The supported count is still 4 and is what the boot warning is measured against.");
        }
    }
}
