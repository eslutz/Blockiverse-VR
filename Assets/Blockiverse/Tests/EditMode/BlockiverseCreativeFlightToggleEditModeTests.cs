using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Double-tap flight toggling has regressed twice: once by never arming the double-tap while
    // flying (could enter but never exit), once by depending on release edges that do not fire
    // reliably on device (could not enter at all). These pin the decision rules.
    public sealed class BlockiverseCreativeFlightToggleEditModeTests
    {
        [Test]
        public void QuickPressIsATapAndAHoldIsNot()
        {
            Assert.That(BlockiverseCreativeFlightController.IsTapPress(0.05f), Is.True);
            Assert.That(BlockiverseCreativeFlightController.IsTapPress(0.20f), Is.True);
            Assert.That(BlockiverseCreativeFlightController.IsTapPress(0.6f), Is.False,
                "Holding the button is how the player flies; it must never count as a tap.");
            Assert.That(BlockiverseCreativeFlightController.IsTapPress(float.MaxValue), Is.False);
        }

        [Test]
        public void SecondQuickTapCompletesTheDoubleTap()
        {
            Assert.That(
                BlockiverseCreativeFlightController.CompletesDoubleTap(previousPressWasTap: true, secondsSincePreviousPress: 0.15f),
                Is.True,
                "Two quick taps toggle flight — both to enter and to exit.");
        }

        [Test]
        public void SlowSecondTapDoesNotToggle()
        {
            Assert.That(
                BlockiverseCreativeFlightController.CompletesDoubleTap(previousPressWasTap: true, secondsSincePreviousPress: 1.5f),
                Is.False);
        }

        [Test]
        public void PressFollowingAHoldNeverToggles()
        {
            // The regression that switched flight off mid-air: bursts of hold-to-fly must not
            // chain into a double-tap.
            Assert.That(
                BlockiverseCreativeFlightController.CompletesDoubleTap(previousPressWasTap: false, secondsSincePreviousPress: 0.05f),
                Is.False);
        }
    }
}
