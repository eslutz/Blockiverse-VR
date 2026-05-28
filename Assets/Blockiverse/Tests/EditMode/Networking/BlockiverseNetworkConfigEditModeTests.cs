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
        }

        [Test]
        public void BlankAddressAndZeroPortFallBackToDefaults()
        {
            var config = new BlockiverseNetworkConfig(string.Empty, string.Empty, 0);

            Assert.That(config.Address, Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
            Assert.That(config.ListenAddress, Is.EqualTo(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(config.Port, Is.EqualTo(BlockiverseNetworkConfig.DefaultPort));
        }

        [Test]
        public void WithAddressAndWithPortPreserveOtherSettings()
        {
            BlockiverseNetworkConfig config = BlockiverseNetworkConfig.Default
                .WithAddress("192.168.1.47")
                .WithPort(7888);

            Assert.That(config.Address, Is.EqualTo("192.168.1.47"));
            Assert.That(config.ListenAddress, Is.EqualTo(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(config.Port, Is.EqualTo(7888));
        }
    }
}
