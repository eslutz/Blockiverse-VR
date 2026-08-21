using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseSwimMotionEditModeTests
    {
        [Test]
        public void NoInputSinksAtThePassiveSinkSpeed()
        {
            // The ratified default: the player sinks whenever they are not actively swimming, so
            // the surface is not a resting state and treading water is an active act.
            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false,
                sinkHeld: false,
                passiveSinkEnabled: true,
                family: FluidFamily.Freshwater);

            Assert.That(target, Is.EqualTo(-BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond).Within(0.0001f));
            Assert.That(target, Is.LessThan(0.0f), "Negative buoyancy means down, not neutral.");
        }

        [Test]
        public void DisablingPassiveSinkGivesExactlyNeutralBuoyancy()
        {
            // The comfort accommodation, and it has to be exact: with no input the app must move
            // the player vertically by zero, so loading a save submerged, respawning underwater, or
            // water flowing into your cell produce no unrequested motion at all.
            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false,
                sinkHeld: false,
                passiveSinkEnabled: false,
                family: FluidFamily.Freshwater);

            Assert.That(target, Is.EqualTo(0.0f).Within(float.Epsilon));
        }

        [Test]
        public void RiseBeatsSinkAndBothBeatPassiveSink()
        {
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(true, true, true, FluidFamily.Freshwater),
                Is.EqualTo(BlockiverseSwimMotion.RiseSpeedMetersPerSecond).Within(0.0001f),
                "Holding both must rise: up is the way out of trouble.");
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(false, true, true, FluidFamily.Freshwater),
                Is.EqualTo(-BlockiverseSwimMotion.SinkSpeedMetersPerSecond).Within(0.0001f),
                "Held crouch descends faster than the passive drift, or the input would read as doing nothing.");
        }

        [Test]
        public void EmberflowIsSlowerThanWaterInEveryDirection()
        {
            // Emberflow flows slowly, so it is thicker: it still sinks you, but slowly enough that
            // reacting within a second or two gets you out.
            Assert.That(
                BlockiverseSwimMotion.PassiveSinkSpeedFor(FluidFamily.Emberflow),
                Is.LessThan(BlockiverseSwimMotion.PassiveSinkSpeedFor(FluidFamily.Freshwater)));
            Assert.That(
                BlockiverseSwimMotion.SinkSpeedFor(FluidFamily.Emberflow),
                Is.LessThan(BlockiverseSwimMotion.SinkSpeedFor(FluidFamily.Freshwater)));
            Assert.That(
                BlockiverseSwimMotion.RiseSpeedFor(FluidFamily.Emberflow),
                Is.LessThan(BlockiverseSwimMotion.RiseSpeedFor(FluidFamily.Freshwater)));
            Assert.That(
                BlockiverseSwimMotion.PassiveSinkSpeedFor(FluidFamily.Emberflow),
                Is.GreaterThan(0.0f),
                "A molten pool is still not a place to float.");
            Assert.That(
                BlockiverseSwimMotion.PassiveSinkSpeedFor(FluidFamily.Brine),
                Is.EqualTo(BlockiverseSwimMotion.PassiveSinkSpeedFor(FluidFamily.Freshwater)).Within(0.0001f),
                "Brine is water; only emberflow is thick.");
        }

        [Test]
        public void SamplesMapToStatesAndOnlySwimmingStatesOwnVerticalMotion()
        {
            Assert.That(BlockiverseSwimMotion.ResolveState(false, false, false), Is.EqualTo(SwimState.Dry));
            Assert.That(BlockiverseSwimMotion.ResolveState(true, false, false), Is.EqualTo(SwimState.Wading));
            Assert.That(BlockiverseSwimMotion.ResolveState(true, true, false), Is.EqualTo(SwimState.Surfaced));
            Assert.That(BlockiverseSwimMotion.ResolveState(true, true, true), Is.EqualTo(SwimState.Swimming));

            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Wading), Is.False,
                "Wading keeps gravity on, or every puddle and the one-block shore step would become swimmable.");
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Dry), Is.False);
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Surfaced), Is.True);
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Swimming), Is.True);
        }

        [Test]
        public void WadingKeepsFullWalkingSpeedAndSwimmingTakesTheComfortFactor()
        {
            Assert.That(
                BlockiverseSwimMotion.HorizontalSpeedFactor(SwimState.Wading, FluidFamily.Freshwater, 0.55f),
                Is.EqualTo(1.0f).Within(0.0001f),
                "No ruleset slows wading; walking through shallow water stays walking.");
            Assert.That(
                BlockiverseSwimMotion.HorizontalSpeedFactor(SwimState.Swimming, FluidFamily.Freshwater, 0.55f),
                Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(
                BlockiverseSwimMotion.HorizontalSpeedFactor(SwimState.Swimming, FluidFamily.Emberflow, 0.55f),
                Is.LessThan(BlockiverseSwimMotion.HorizontalSpeedFactor(SwimState.Swimming, FluidFamily.Freshwater, 0.55f)),
                "Lava is thicker to move through than water.");
        }

        [Test]
        public void TheSpeedFactorIsClampedToItsComfortRange()
        {
            Assert.That(BlockiverseSwimMotion.ClampSpeedFactor(5.0f),
                Is.EqualTo(BlockiverseSwimMotion.MaximumSwimSpeedFactor).Within(0.0001f));
            Assert.That(BlockiverseSwimMotion.ClampSpeedFactor(0.0f),
                Is.EqualTo(BlockiverseSwimMotion.MinimumSwimSpeedFactor).Within(0.0001f),
                "A zero factor would strand the player motionless in water with no way to tell why.");
            Assert.That(BlockiverseSwimMotion.DefaultSwimSpeedFactor,
                Is.InRange(BlockiverseSwimMotion.MinimumSwimSpeedFactor, BlockiverseSwimMotion.MaximumSwimSpeedFactor));
        }

        [Test]
        public void VerticalVelocityRampsAtTheAccelerationAndSettlesWithoutOvershooting()
        {
            // A velocity target reached by MoveTowards, never a spring: no overshoot, no bob, and
            // holding still can never accumulate into a fall.
            float velocity = 0.0f;
            const float step = 1.0f / 60.0f;
            float target = -BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond;

            velocity = BlockiverseSwimMotion.AdvanceVerticalVelocity(velocity, target, step);

            Assert.That(velocity, Is.GreaterThan(target),
                "One frame must not jump straight to the target, or entering water would snap.");
            Assert.That(velocity, Is.LessThan(0.0f));

            for (int frame = 0; frame < 120; frame++)
                velocity = BlockiverseSwimMotion.AdvanceVerticalVelocity(velocity, target, step);

            Assert.That(velocity, Is.EqualTo(target).Within(0.0001f),
                "The descent settles at exactly the passive sink speed and stays there.");

            for (int frame = 0; frame < 120; frame++)
                velocity = BlockiverseSwimMotion.AdvanceVerticalVelocity(velocity, target, step);

            Assert.That(velocity, Is.EqualTo(target).Within(0.0001f),
                "Holding still for seconds must not accelerate: the cap is what stops a drift becoming a fall.");
        }

        [Test]
        public void AZeroOrNegativeTimeStepCannotMoveTheVelocity()
        {
            // A paused frame or a clamped delta must not lurch the player.
            Assert.That(BlockiverseSwimMotion.AdvanceVerticalVelocity(-0.2f, -1.0f, 0.0f),
                Is.EqualTo(-0.2f).Within(0.0001f));
            Assert.That(BlockiverseSwimMotion.AdvanceVerticalVelocity(-0.2f, -1.0f, -1.0f),
                Is.EqualTo(-0.2f).Within(0.0001f));
        }

        [Test]
        public void TheSwimProviderReadsTheJumpActionRatherThanTheJumpProvider()
        {
            // The softlock this guards: jump is gated by locomotion mode, so a teleport-mode player
            // who ends up submerged could swim DOWN (crouch is not mode-gated) and not up, while
            // passive sink pulled them deeper. Depending on jumpProvider.enabled would reintroduce
            // it, and nothing about the resulting bug is visible until someone is stuck under a
            // lake in the comfort locomotion mode.
            string source = System.IO.File.ReadAllText("Assets/Blockiverse/Scripts/VR/BlockiverseSwimProvider.cs");

            Assert.That(source, Does.Contain("ResolveJumpActionForCurrentControls"),
                "Swim-up resolves the jump action directly.");
            // Matched against the dependency as it would actually be written, not the bare word:
            // the word appears in the comment that explains why the dependency is absent.
            Assert.That(source, Does.Not.Contain("inputRig.JumpProvider"),
                "The swim provider must not consult the jump provider component; it is disabled underwater and in teleport mode.");
            Assert.That(source, Does.Not.Contain("GetComponent<JumpProvider>"),
                "Nor find one for itself.");
            Assert.That(source, Does.Contain("CrouchActive"),
                "Swim-down reuses the existing crouch input rather than claiming a new binding.");
        }

        [Test]
        public void HeadSubmersionHysteresisSeparatesTheThresholdsRatherThanOverlappingThem()
        {
            // The sign here is load-bearing and easy to get backwards: while dry the eye must be a
            // full band BELOW the surface to count as submerged, and while submerged it must be a
            // full band ABOVE to count as surfaced. Biasing the other way overlaps the thresholds
            // and turns a head bobbing at the waterline into a screen-wide strobe.
            const float surface = 64.0f;
            float band = BlockiverseSwimMotion.SubmersionHysteresisMeters;

            Assert.That(BlockiverseSwimMotion.ResolveHeadSubmerged(false, surface - band * 0.5f, surface), Is.False,
                "Just under the line while dry is not yet submerged.");
            Assert.That(BlockiverseSwimMotion.ResolveHeadSubmerged(false, surface - band * 2.0f, surface), Is.True);
            Assert.That(BlockiverseSwimMotion.ResolveHeadSubmerged(true, surface + band * 0.5f, surface), Is.True,
                "Just above the line while submerged is not yet surfaced.");
            Assert.That(BlockiverseSwimMotion.ResolveHeadSubmerged(true, surface + band * 2.0f, surface), Is.False);
        }

        [Test]
        public void TheHysteresisBandOutlastsTheUnderwaterFogFade()
        {
            // Passive sink means a surfaced player re-crosses the water line every time they stop
            // holding rise. That is intended, but the round trip has to be slower than the fog
            // cross-fade or the transition reads as a flicker instead of a deliberate fade.
            float roundTripSeconds =
                BlockiverseSwimMotion.SubmersionHysteresisMeters * 2.0f /
                BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond;

            Assert.That(roundTripSeconds, Is.GreaterThan(Blockiverse.Gameplay.BlockiverseWaterView.SubmergeBlendSeconds),
                "Crossing the full hysteresis band must take longer than the underwater fog fade.");
        }
    }
}
