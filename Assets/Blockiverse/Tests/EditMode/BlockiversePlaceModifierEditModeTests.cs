using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // The grip stopped being a "place" button and became a modifier that selects what the trigger
    // does. These pin the resolution rules; the bridge wiring that consumes them is exercised by
    // the creative interaction PlayMode tests.
    public sealed class BlockiversePlaceModifierEditModeTests
    {
        [Test]
        public void HoldModeFollowsTheButtonAndIgnoresTheLatch()
        {
            // Default. The trigger places only while the grip is physically down.
            Assert.That(BlockiverseInputRig.ResolveModifierActive(false, held: true, toggled: false), Is.True);
            Assert.That(BlockiverseInputRig.ResolveModifierActive(false, held: false, toggled: false), Is.False);

            // A stale latch from a previous stint in toggle mode must not leak into hold mode.
            Assert.That(BlockiverseInputRig.ResolveModifierActive(false, held: false, toggled: true), Is.False,
                "Hold mode must ignore the toggle latch, or switching the setting off strands the " +
                "player in place mode with no button that turns it off.");
        }

        [Test]
        public void ToggleModeFollowsTheLatchAndIgnoresTheButton()
        {
            Assert.That(BlockiverseInputRig.ResolveModifierActive(true, held: false, toggled: true), Is.True);
            Assert.That(BlockiverseInputRig.ResolveModifierActive(true, held: true, toggled: false), Is.False,
                "In toggle mode the physical hold is irrelevant — only the latch decides.");
        }

        [Test]
        public void PlaceModifierUsesTheSameResolutionAsSprintAndCrouch()
        {
            // Not a tautology: it is the reason the setting behaves the way players already expect
            // from the two comfort toggles that shipped before it. If the place modifier ever grew
            // its own resolution rule, the three would drift apart and only one would be documented.
            foreach (bool toggleMode in new[] { false, true })
            {
                foreach (bool held in new[] { false, true })
                {
                    foreach (bool toggled in new[] { false, true })
                    {
                        bool expected = toggleMode ? toggled : held;
                        Assert.That(BlockiverseInputRig.ResolveModifierActive(toggleMode, held, toggled),
                            Is.EqualTo(expected),
                            $"toggleMode={toggleMode} held={held} toggled={toggled}");
                    }
                }
            }
        }
    }
}
