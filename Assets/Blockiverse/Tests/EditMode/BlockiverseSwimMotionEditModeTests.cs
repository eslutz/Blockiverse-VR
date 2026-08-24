using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseSwimMotionEditModeTests
    {
        [Test]
        public void NoInputSinksAtThePassiveSinkSpeedOnceGenuinelySurfaced()
        {
            // The ratified default: the player sinks whenever they are not actively swimming, so
            // the surface is not a resting state and treading water is an active act. This applies
            // once the body has actually reached that resting depth -- bodySubmerged: true.
            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false,
                sinkHeld: false,
                passiveSinkEnabled: true,
                bodySubmerged: true,
                family: FluidFamily.Freshwater);

            Assert.That(target, Is.EqualTo(-BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond).Within(0.0001f));
            Assert.That(target, Is.LessThan(0.0f), "Negative buoyancy means down, not neutral.");
        }

        [Test]
        public void DisablingPassiveSinkGivesExactlyNeutralBuoyancyOnceSurfaced()
        {
            // The comfort accommodation, and it has to be exact: with no input the app must move
            // the player vertically by zero, so loading a save submerged, respawning underwater, or
            // water flowing into your cell produce no unrequested motion at all.
            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false,
                sinkHeld: false,
                passiveSinkEnabled: false,
                bodySubmerged: true,
                family: FluidFamily.Freshwater);

            Assert.That(target, Is.EqualTo(0.0f).Within(float.Epsilon));
        }

        [Test]
        public void UnsupportedEntryFallsAtTheFallRateRegardlessOfThePassiveSinkComfortSetting()
        {
            // Eric's report (2026-08-23): walking off solid ground onto deep water flush with the
            // ground sank him at the gentle passive rate instead of a real fall. Unsupported entry
            // is not the optional idle drift, so it must not be gated on the comfort toggle that
            // turns THAT off.
            float withPassiveSinkOn = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false, sinkHeld: false, passiveSinkEnabled: true,
                bodySubmerged: false, family: FluidFamily.Freshwater);
            float withPassiveSinkOff = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false, sinkHeld: false, passiveSinkEnabled: false,
                bodySubmerged: false, family: FluidFamily.Freshwater);

            Assert.That(withPassiveSinkOn,
                Is.EqualTo(-BlockiverseSwimMotion.NaturalDescentSpeedFor(FluidFamily.Freshwater)).Within(0.0001f));
            Assert.That(withPassiveSinkOff, Is.EqualTo(withPassiveSinkOn).Within(0.0001f),
                "Turning off the comfort drift must not turn off falling into unsupported water.");
        }

        [Test]
        public void UnsupportedEntryFallsFasterThanTheIdlePassiveDrift()
        {
            // The whole bug: it must not read like the same gentle sink a treading player gets.
            float fallTarget = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false, sinkHeld: false, passiveSinkEnabled: true,
                bodySubmerged: false, family: FluidFamily.Freshwater);

            Assert.That(-fallTarget, Is.GreaterThan(BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond));
        }

        [Test]
        public void RiseBeatsSinkAndBothBeatPassiveSink()
        {
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(true, true, true, true, FluidFamily.Freshwater),
                Is.EqualTo(BlockiverseSwimMotion.RiseSpeedMetersPerSecond).Within(0.0001f),
                "Holding both must rise: up is the way out of trouble.");
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(false, true, true, true, FluidFamily.Freshwater),
                Is.EqualTo(-BlockiverseSwimMotion.SinkSpeedMetersPerSecond).Within(0.0001f),
                "Held crouch descends faster than the passive drift, or the input would read as doing nothing.");
        }

        [Test]
        public void RiseAndSinkInputOverrideTheUnsupportedEntryFallToo()
        {
            // Input must always win, even mid-fall into unsupported water.
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(true, false, true, false, FluidFamily.Freshwater),
                Is.EqualTo(BlockiverseSwimMotion.RiseSpeedMetersPerSecond).Within(0.0001f));
            Assert.That(
                BlockiverseSwimMotion.ResolveVerticalTarget(false, true, true, false, FluidFamily.Freshwater),
                Is.EqualTo(-BlockiverseSwimMotion.SinkSpeedMetersPerSecond).Within(0.0001f));
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
            // Feet cell 10 throughout. Depth is what separates wading from swimming, so the
            // surface cell is the discriminator: at or below the feet you are standing on the
            // bottom of a single block of water; above it, submersion decides.
            Assert.That(State(inFluid: false, feet: false, body: false, head: false, surfaceCellY: 0),
                Is.EqualTo(SwimState.Dry));
            Assert.That(State(inFluid: true, feet: true, body: false, head: false, surfaceCellY: 10),
                Is.EqualTo(SwimState.Wading));
            Assert.That(State(inFluid: true, feet: true, body: true, head: false, surfaceCellY: 12),
                Is.EqualTo(SwimState.Surfaced));
            Assert.That(State(inFluid: true, feet: true, body: true, head: true, surfaceCellY: 14),
                Is.EqualTo(SwimState.Swimming));

            // The case that used to be unreachable: body submerged, but the water is only one
            // block deep. That is a puddle, and it is walked through, not swum.
            Assert.That(State(inFluid: true, feet: true, body: true, head: false, surfaceCellY: 10),
                Is.EqualTo(SwimState.Wading),
                "one block of water is wading however much of a short or crouched player is in it");

            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Wading), Is.False,
                "Wading keeps gravity on, or every puddle and the one-block shore step would become swimmable.");
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Dry), Is.False);
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Surfaced), Is.True);
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Swimming), Is.True);
        }

        const int FeetCellY = 10;

        static SwimState State(bool inFluid, bool feet, bool body, bool head, int surfaceCellY)
        {
            var submersion = new FluidSubmersionState(
                inFluid: inFluid,
                family: FluidFamily.Freshwater,
                immersion: head ? FluidImmersion.Head : body ? FluidImmersion.Body : FluidImmersion.Feet,
                feetSubmerged: feet,
                bodySubmerged: body,
                headSubmerged: head,
                hasSurface: inFluid,
                surfaceCellY: surfaceCellY);

            return BlockiverseSwimMotion.ResolveState(submersion, FeetCellY);
        }

        [Test]
        public void WalkingOntoDeepWaterFlushWithTheGroundFallsInsteadOfSinking()
        {
            // Eric's exact report, end to end: step off solid ground onto a deep water column
            // whose surface is level with the ground -- feet just touched the top, body and head
            // are both still dry, and there is no floor beneath (FluidBelowFeet). ResolveState
            // classifies this as Surfaced (BlockiverseSwimMotion.cs:105-112) because it is the
            // same signature a player settling after a rise produces -- but a player at THIS
            // instant has not sunk to a natural depth at all, and ResolveVerticalTarget is what
            // tells the two apart via bodySubmerged.
            var submersion = new FluidSubmersionState(
                inFluid: true,
                family: FluidFamily.Freshwater,
                immersion: FluidImmersion.Feet,
                feetSubmerged: true,
                bodySubmerged: false,
                headSubmerged: false,
                hasSurface: true,
                surfaceCellY: FeetCellY,
                fluidBelowFeet: true);

            SwimState state = BlockiverseSwimMotion.ResolveState(submersion, FeetCellY);
            Assert.That(state, Is.EqualTo(SwimState.Surfaced),
                "Fixture guard: this must be the exact state Eric's report reaches.");
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(state), Is.True,
                "Vertical motion is swim-owned from the moment there is no floor -- gravity does " +
                "not get a frame to itself here, ResolveVerticalTarget's fall branch does the job.");

            float target = BlockiverseSwimMotion.ResolveVerticalTarget(
                riseHeld: false,
                sinkHeld: false,
                passiveSinkEnabled: true,
                bodySubmerged: submersion.BodySubmerged,
                family: submersion.Family);

            // Updated 2026-08-24: this asserted the fall equalled SinkSpeedMetersPerSecond, back
            // when NaturalDescentSpeedFor aliased it. Eric reported the result STILL felt like
            // slow sinking, so the fall got its own faster constant and the ratified crouch-
            // descend rate stayed put. Asserting the alias would now re-pin the bug.
            Assert.That(target,
                Is.EqualTo(-BlockiverseSwimMotion.NaturalDescentSpeedFor(submersion.Family)).Within(0.0001f),
                "Must fall at the unsupported-entry FALL rate.");
            Assert.That(-target, Is.GreaterThan(BlockiverseSwimMotion.SinkSpeedMetersPerSecond),
                "The bug, pinned directly: falling must outrun even a deliberately held crouch, "
                + "let alone a treading player's gentle drift.");
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

        [Test]
        public void FallingIntoWaterCarriesTheDescentUnderTheSurface()
        {
            // Eric's report: dropping into water deeper than a block left him stopped dead ON the
            // surface. The plunge is what makes the entry read as an entry.
            float plunge = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-6.0f, FluidFamily.Freshwater);

            Assert.That(plunge, Is.LessThan(0.0f), "A fall must carry the player downward, not stop them.");
            Assert.That(plunge, Is.LessThan(-BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond),
                "A plunge that is no faster than the idle drift is not a plunge -- this is exactly "
                + "the bug, since the old behaviour began the drift from a standing start.");
        }

        [Test]
        public void ALongFallIsCappedSoNobodyIsSentToTheSeabed()
        {
            float terminal = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-40.0f, FluidFamily.Freshwater);

            Assert.That(terminal,
                Is.EqualTo(-BlockiverseSwimMotion.MaximumEntryPlungeSpeedMetersPerSecond).Within(0.0001f),
                "Descent is capped, so a terminal-velocity drop plunges to a readable depth.");
        }

        [Test]
        public void EntryVelocityIsContinuousInTheArrivalSpeed()
        {
            // THE dead-stop regression, pinned. A threshold used to zero any descent below it,
            // and because the provider ASSIGNS this to verticalVelocity, "no plunge" meant the
            // player stopped dead on the water line. Walking off ground flush with the surface
            // arrives at ~1.4 m/s (a 0.10 m fall to the feet sample) and was pinned to zero —
            // which is what Eric felt as "slowly sinking" (2026-08-24).
            float walkOff = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-1.4f, FluidFamily.Freshwater);
            Assert.That(walkOff, Is.EqualTo(-1.4f).Within(0.0001f),
                "A small arrival must carry through, not be zeroed into a hard stop.");

            // Monotonic with no cliff: a hair more descent must never produce LESS entry speed.
            float slower = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-3.49f, FluidFamily.Freshwater);
            float faster = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-3.51f, FluidFamily.Freshwater);
            Assert.That(faster, Is.LessThanOrEqualTo(slower),
                "The old threshold made 3.49 -> 0 and 3.51 -> -3.51; entry must be continuous.");
        }

        [Test]
        public void AOneBlockLedgeDropCarriesItsFullArrivalSpeed()
        {
            // sqrt(2 * 9.81 * 1.0) = 4.43 m/s -- Eric's "drop into water" case, under the 5.0 cap.
            float drop = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-4.43f, FluidFamily.Freshwater);

            Assert.That(drop, Is.EqualTo(-4.43f).Within(0.0001f));
        }

        [Test]
        public void TheEntryFallAcceleratesUnderGravityAndOnlyEasesOnceTheBodyIsUnder()
        {
            // The other half of "it felt like being lowered": constant velocity reads as being
            // lowered, acceleration reads as falling. The entry must use real gravity.
            Assert.That(BlockiverseSwimMotion.VerticalAccelerationFor(bodySubmerged: false),
                Is.EqualTo(BlockiverseSwimMotion.EntryFallAccelerationMetersPerSecondSquared).Within(0.0001f));
            Assert.That(BlockiverseSwimMotion.VerticalAccelerationFor(bodySubmerged: true),
                Is.EqualTo(BlockiverseSwimMotion.VerticalAccelerationMetersPerSecondSquared).Within(0.0001f));
            Assert.That(BlockiverseSwimMotion.EntryFallAccelerationMetersPerSecondSquared,
                Is.GreaterThan(BlockiverseSwimMotion.VerticalAccelerationMetersPerSecondSquared),
                "Falling in must accelerate harder than the swim ramp, or it reads as a winch.");
        }

        [Test]
        public void TheUnsupportedFallRateIsItsOwnConstantNotTheRatifiedCrouchDescend()
        {
            // voxel_survival_ruleset.md pins crouch-held descend at 1.2 m/s (0.6 emberflow). The
            // fall must be faster than that AND must not be implemented by moving that constant,
            // or the ratified input speed changes as a side effect.
            Assert.That(BlockiverseSwimMotion.SinkSpeedMetersPerSecond, Is.EqualTo(1.2f).Within(0.0001f),
                "Ratified crouch-descend rate; the fall model must not have moved it.");
            Assert.That(BlockiverseSwimMotion.NaturalDescentSpeedFor(FluidFamily.Freshwater),
                Is.GreaterThan(BlockiverseSwimMotion.SinkSpeedMetersPerSecond),
                "An unsupported fall outruns a held crouch, or falling reads as slower than input.");
            Assert.That(BlockiverseSwimMotion.NaturalDescentSpeedFor(FluidFamily.Emberflow),
                Is.LessThan(BlockiverseSwimMotion.NaturalDescentSpeedFor(FluidFamily.Freshwater)),
                "Emberflow keeps the ruleset's half-speed relationship.");
        }

        [Test]
        public void RisingIntoAFluidCellNeverPlunges()
        {
            // Swimming UP into fluid, or being carried up into it, arrives with a positive
            // velocity. Feeding that through unguarded would flip the sign and yank the player
            // downward at the exact moment they were trying to surface.
            float ascending = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(6.0f, FluidFamily.Freshwater);

            Assert.That(ascending, Is.EqualTo(0.0f).Within(0.0001f));
        }

        [Test]
        public void EmberflowBarelyTakesThePlunge()
        {
            float water = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-6.0f, FluidFamily.Freshwater);
            float lava = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-6.0f, FluidFamily.Emberflow);

            Assert.That(lava, Is.GreaterThan(water),
                "Emberflow is thick: the same fall must not sink you as deep as water does.");
            Assert.That(lava, Is.LessThan(0.0f), "It should still break the surface.");
        }

        [Test]
        public void TheDragCurveArrestsAPlungeInsteadOfLettingItRun()
        {
            // Guards the half of this that lives in AdvanceVerticalVelocity: the plunge is only
            // safe because the approach to the passive-sink target acts as drag. If that coupling
            // ever breaks, a seeded entry velocity becomes a permanent dive.
            float velocity = BlockiverseSwimMotion.ResolveEntryPlungeVelocity(-6.0f, FluidFamily.Freshwater);
            float target = -BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond;

            for (int step = 0; step < 200; step++)
                velocity = BlockiverseSwimMotion.AdvanceVerticalVelocity(velocity, target, 0.02f);

            Assert.That(velocity, Is.EqualTo(target).Within(0.001f),
                "Within a few seconds the plunge must settle back to the ordinary passive drift.");
        }
    }
}
