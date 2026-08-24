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
        [Test]
        public void FlightCruisesAtTheLandSprintSpeedAndSprintsFasterStill()
        {
            // Eric's ruling (2026-08-24): "normal speed should be what land sprinting speed is and
            // flying sprint speed should be faster than that."
            //
            // Horizontal flight is the ORDINARY move provider — the flight controller owns only
            // vertical — so before this, flying forward moved at plain walking pace.
            const float baseSpeed = 1.8f;

            float landWalk = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                baseSpeed, sprintActive: false, flightActive: false);
            float landSprint = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                baseSpeed, sprintActive: true, flightActive: false);
            float flightCruise = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                baseSpeed, sprintActive: false, flightActive: true);
            float flightSprint = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                baseSpeed, sprintActive: true, flightActive: true);

            Assert.That(flightCruise, Is.EqualTo(landSprint).Within(1e-4f),
                "Flight cruise IS land sprint — the literal ruling.");
            Assert.That(flightSprint, Is.GreaterThan(flightCruise),
                "Sprinting in the air must be faster than cruising in the air.");
            Assert.That(flightCruise, Is.GreaterThan(landWalk),
                "Guard against the regression: flying forward used to be plain walking pace.");
        }

        [Test]
        public void GroundMovementIsUnchangedByTheFlightSpeedRule()
        {
            // The flight rule must not leak into walking, which Eric did not ask to change.
            const float baseSpeed = 1.8f;

            Assert.That(
                BlockiverseInputRig.ResolveHorizontalMoveSpeed(baseSpeed, sprintActive: false, flightActive: false),
                Is.EqualTo(baseSpeed).Within(1e-4f));
            Assert.That(
                BlockiverseInputRig.ResolveHorizontalMoveSpeed(baseSpeed, sprintActive: true, flightActive: false),
                Is.EqualTo(BlockiverseInputRig.ResolveSprintMoveSpeed(baseSpeed, sprintActive: true)).Within(1e-4f));
        }

        [Test]
        public void FlightSpeedScalesWithTheComfortMoveSpeedSetting()
        {
            // Derived from the comfort-adjusted base, so a player who slowed movement down for
            // comfort keeps that in the air rather than being flung about by a fixed constant.
            float slow = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                1.0f, sprintActive: false, flightActive: true);
            float fast = BlockiverseInputRig.ResolveHorizontalMoveSpeed(
                2.0f, sprintActive: false, flightActive: true);

            Assert.That(fast, Is.EqualTo(slow * 2.0f).Within(1e-4f));
        }

    }
}
