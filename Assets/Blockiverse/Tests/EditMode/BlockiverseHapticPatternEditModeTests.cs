using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseHapticPatternEditModeTests
    {
        [Test]
        public void ClampsAmplitudeAndDurationToValidRanges()
        {
            var loud = new BlockiverseHapticPattern(2f, 1f);
            var negative = new BlockiverseHapticPattern(-1f, -1f);

            Assert.That(loud.Amplitude, Is.EqualTo(1f));
            Assert.That(loud.DurationSeconds, Is.EqualTo(1f));
            Assert.That(negative.Amplitude, Is.EqualTo(0f));
            Assert.That(negative.DurationSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void NamedPatternsAreDistinctAndWithinRange()
        {
            BlockiverseHapticPattern[] patterns =
            {
                BlockiverseHapticPattern.BlockBreak,
                BlockiverseHapticPattern.BlockPlace,
                BlockiverseHapticPattern.UiTick
            };

            foreach (BlockiverseHapticPattern pattern in patterns)
            {
                Assert.That(pattern.Amplitude, Is.InRange(0f, 1f));
                Assert.That(pattern.DurationSeconds, Is.GreaterThan(0f));
            }

            Assert.That(BlockiverseHapticPattern.BlockBreak.Amplitude,
                Is.GreaterThan(BlockiverseHapticPattern.BlockPlace.Amplitude));
        }
    }
}
