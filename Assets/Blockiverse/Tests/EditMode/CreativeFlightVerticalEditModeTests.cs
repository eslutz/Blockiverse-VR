using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Flight's vertical verbs, which now mirror swimming's: jump/A to rise, crouch/B to descend.
    // The one deliberate difference is the resting state -- swimming sinks, flight hovers.
    public sealed class CreativeFlightVerticalEditModeTests
    {
        [Test]
        public void NoInputHovers()
        {
            // The exact inverse of swimming's negative buoyancy, and the reason flight cannot just
            // reuse BlockiverseSwimMotion.
            Assert.That(
                BlockiverseCreativeFlightController.ResolveVerticalTarget(
                    riseHeld: false, sinkHeld: false, sprintActive: false),
                Is.EqualTo(0.0f));
        }

        [Test]
        public void RiseAndSinkAreOppositeAndEqual()
        {
            float up = BlockiverseCreativeFlightController.ResolveVerticalTarget(true, false, false);
            float down = BlockiverseCreativeFlightController.ResolveVerticalTarget(false, true, false);

            Assert.That(up, Is.GreaterThan(0.0f));
            Assert.That(down, Is.EqualTo(-up));
        }

        [Test]
        public void HoldingBothCancels()
        {
            // Rather than fighting or picking a winner: pressing both is the player asking to stop.
            Assert.That(
                BlockiverseCreativeFlightController.ResolveVerticalTarget(true, true, false),
                Is.EqualTo(0.0f));
        }

        [Test]
        public void SprintRaisesTheClimbAndDescentRate()
        {
            float normal = BlockiverseCreativeFlightController.ResolveVerticalTarget(true, false, false);
            float sprinting = BlockiverseCreativeFlightController.ResolveVerticalTarget(true, false, true);

            Assert.That(sprinting, Is.GreaterThan(normal));
            Assert.That(
                BlockiverseCreativeFlightController.ResolveVerticalTarget(false, true, true),
                Is.EqualTo(-sprinting));
        }
    }
}
