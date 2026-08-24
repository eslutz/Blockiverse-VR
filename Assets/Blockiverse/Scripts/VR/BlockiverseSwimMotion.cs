using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    public enum SwimState
    {
        // No fluid at any sample point. XRI owns gravity and vertical motion.
        Dry = 0,

        // Feet in fluid, body dry. Gravity deliberately stays ON so a puddle and the one-block
        // shore step remain walkable rather than becoming swimmable.
        Wading = 1,

        // Body in fluid, head under. Gravity is locked off and the swim provider owns vertical.
        Swimming = 2,

        // Body in fluid, head in air: treading water. Same ownership as Swimming -- the difference
        // matters to the underwater view, not to the motion.
        Surfaced = 3
    }

    // All swim motion maths, as pure functions, so the numbers can be pinned in EditMode without a
    // rig, an XR origin, or a play session -- the same shape as BlockiverseCreativeFlightController's
    // displacement helper and BlockiversePlayerBodyManipulator's capsule resolution.
    public static class BlockiverseSwimMotion
    {
        // Passive descent with no input: the DEFAULT. About one block every three seconds -- clearly
        // readable as sinking, slow enough to stay a constant-velocity drift rather than a fall.
        public const float PassiveSinkSpeedMetersPerSecond = 0.35f;
        public const float SinkSpeedMetersPerSecond = 1.2f;
        public const float RiseSpeedMetersPerSecond = 1.0f;

        // Reached in about a tenth of a second, so the transition is smooth without feeling springy.
        public const float VerticalAccelerationMetersPerSecondSquared = 6.0f;

        public const float DefaultSwimSpeedFactor = 0.55f;
        public const float MinimumSwimSpeedFactor = 0.30f;
        public const float MaximumSwimSpeedFactor = 1.00f;

        // Wading is walking through shallow water; the rulesets give no basis for slowing it.
        public const float WadeSpeedFactor = 1.00f;

        // Emberflow flows slowly (ruleset section 5.4), so it is thicker in every direction: it
        // sinks you more slowly than water, but it does sink you. A player who reacts within a
        // second or two can still hold rise and climb out.
        public const float EmberflowPassiveSinkSpeedMetersPerSecond = 0.20f;
        public const float EmberflowSinkSpeedMetersPerSecond = 0.6f;
        public const float EmberflowRiseSpeedMetersPerSecond = 0.7f;
        public const float EmberflowSpeedFactor = 0.5f;

        // Wider than the underwater view's own hysteresis because passive sink means a surfaced
        // player re-crosses the line every time they stop holding rise. At 0.35 m/s a 2 x 0.06 m
        // band is a 0.34 s round trip, longer than the 0.25 s fog fade, so tapping the water line
        // reads as a slow pulse and can never strobe.
        public const float SubmersionHysteresisMeters = 0.06f;


        /// <summary>
        /// Resolves the swim state from how DEEP the water is, falling back to the sample flags
        /// only once it is deeper than one block.
        /// </summary>
        /// <remarks>
        /// Wading is a question about the water, not about the player: water one block deep is
        /// walkable because you are standing on the bottom of it, whoever you are — which means
        /// the answer needs the cell BELOW the feet as well as the surface above them. Deciding that
        /// from where a fraction of the capsule happens to land makes it a question about height
        /// instead, and gets it wrong for everyone — the body sample sits at 0.55 x capsule
        /// height, which is 0.99 m for the default 1.8 m capsule and 0.50 m crouched, both inside
        /// a one-block water cell. Every player at default height therefore read as Surfaced in
        /// ankle-deep water, which locks gravity off and lets them swim up out of a puddle.
        ///
        /// Note this deliberately leaves a crouching player's head underwater in one block of
        /// water still Wading: they are standing on the bottom and can stand up. The underwater
        /// VIEW is a separate question and BlockiverseWaterView answers it from its own head
        /// sample, so the picture still changes without the motion doing so.
        /// </remarks>
        public static SwimState ResolveState(in FluidSubmersionState submersion, int feetCellY)
        {
            if (!submersion.InFluid)
                return SwimState.Dry;

            // Surface at or below the feet cell AND nothing but ground under them: one block of
            // water, standing on the bottom of it.
            //
            // Both halves are load-bearing. The surface test alone is also true at the TOP of a
            // deep column — a player who has risen until only their feet are still under the water
            // line has the surface in their feet cell exactly as a puddle-stander does. Calling
            // that Wading hands vertical motion back to gravity mid-swim, so they sink, re-enter
            // Surfaced, get buoyed back up, and oscillate at the water line instead of treading.
            if (submersion.FeetSubmerged &&
                submersion.HasSurface &&
                submersion.SurfaceCellY <= feetCellY &&
                !submersion.FluidBelowFeet)
                return SwimState.Wading;

            // Deeper than one block: how much of the player is under decides whether they are
            // swimming or treading.
            if (submersion.HeadSubmerged)
                return SwimState.Swimming;

            if (submersion.BodySubmerged)
                return SwimState.Surfaced;

            // Feet wet, body and head dry. Whether that is standing in a puddle or floating with
            // only the feet under the line is the SAME question the guard above asks, so it needs
            // the same answer — the guard alone moved the flip point by 0.11 m rather than removing
            // it. The body sample sits 0.89 m above the feet sample, so it clears the water line
            // well before the feet do: for all but about one percent of vertical positions a
            // player treading deep water arrives here, not at the guard.
            if (submersion.FeetSubmerged)
                return submersion.FluidBelowFeet ? SwimState.Surfaced : SwimState.Wading;

            return SwimState.Dry;
        }

        // Swimming and Surfaced are the states where the swim provider owns vertical motion and
        // gravity must be locked off. Wading deliberately is not one of them.
        public static bool OwnsVerticalMotion(SwimState state) =>
            state == SwimState.Swimming || state == SwimState.Surfaced;

        public static float PassiveSinkSpeedFor(FluidFamily family) =>
            family == FluidFamily.Emberflow
                ? EmberflowPassiveSinkSpeedMetersPerSecond
                : PassiveSinkSpeedMetersPerSecond;

        public static float SinkSpeedFor(FluidFamily family) =>
            family == FluidFamily.Emberflow ? EmberflowSinkSpeedMetersPerSecond : SinkSpeedMetersPerSecond;

        public static float RiseSpeedFor(FluidFamily family) =>
            family == FluidFamily.Emberflow ? EmberflowRiseSpeedMetersPerSecond : RiseSpeedMetersPerSecond;

        public static float ClampSpeedFactor(float speedFactor) =>
            Mathf.Clamp(speedFactor, MinimumSwimSpeedFactor, MaximumSwimSpeedFactor);

        // The horizontal multiplier applied to the move stick. Wading is unmodified; swimming takes
        // the comfort factor, and emberflow is thicker still.
        public static float HorizontalSpeedFactor(SwimState state, FluidFamily family, float comfortSpeedFactor)
        {
            if (state == SwimState.Dry)
                return 1.0f;

            if (state == SwimState.Wading)
                return WadeSpeedFactor;

            float factor = ClampSpeedFactor(comfortSpeedFactor);

            return family == FluidFamily.Emberflow ? factor * EmberflowSpeedFactor : factor;
        }

        // The vertical VELOCITY the player is asking for -- never a position and never an
        // acceleration, so the descent is constant-speed after a short ramp. There is no spring, no
        // overshoot and no bob, and holding still can never accumulate into a fall.
        //
        // With no input at all the answer is a downward drift: negative buoyancy is the default and
        // the surface is not a resting state. Turning passive sink off in the Comfort menu restores
        // exact neutral buoyancy -- with no input the app moves the player vertically by zero, so
        // loading a save submerged, respawning underwater, or water flowing into your cell produce
        // no unrequested motion at all.
        //
        // That default only applies once the player has actually REACHED the surface, though --
        // see bodySubmerged below.
        public static float ResolveVerticalTarget(
            bool riseHeld,
            bool sinkHeld,
            bool passiveSinkEnabled,
            bool bodySubmerged,
            FluidFamily family)
        {
            if (riseHeld)
                return RiseSpeedFor(family);

            if (sinkHeld)
                return -SinkSpeedFor(family);

            // Eric's report (2026-08-23): walking off solid ground onto deep water flush with the
            // ground -- no drop, no fall momentum to seed an entry plunge from -- sank him at the
            // gentle passive rate the instant his feet touched the surface, as if he had already
            // been treading there. He should fall at THE FALL RATE until just his head clears the
            // water, and only then does the slow idle drift apply.
            //
            // "Only feet wet, body/head still dry" is exactly the state that is true here: the
            // player has no floor under them (this is the FluidBelowFeet branch of ResolveState,
            // not the one-block Wading case) but has not yet sunk to a natural floating depth.
            // bodySubmerged is what tells the two apart from a plain target/passive check --
            // there is no dedicated fall model in the swim system, so this reuses the SAME rate as
            // a held sink: unsupported entry is a fall, not a choice, and it should read like one.
            // NOT gated on passiveSinkEnabled -- that toggle is about the optional idle drift once
            // genuinely surfaced, not about whether falling into unsupported water happens at all.
            if (!bodySubmerged)
                return -NaturalDescentSpeedFor(family);

            return passiveSinkEnabled ? -PassiveSinkSpeedFor(family) : 0.0f;
        }

        /// <summary>
        /// Speed used while the body has not yet reached its natural floating depth -- only the
        /// feet are in the fluid, and nothing supports them. Deliberately the SAME rate as a held
        /// sink, not the passive drift: this is a fall, not an idle choice.
        /// </summary>
        public static float NaturalDescentSpeedFor(FluidFamily family) =>
            family == FluidFamily.Emberflow
                ? EmberflowUnsupportedEntryFallSpeedMetersPerSecond
                : UnsupportedEntryFallSpeedMetersPerSecond;

        /// <summary>
        /// Fastest descent an entry plunge may carry into the fluid, in metres per second.
        /// </summary>
        /// <remarks>
        /// A long fall must not send a player to the seabed. Capped rather than scaled so a
        /// terminal-velocity drop and a two-storey drop plunge to a similar, readable depth.
        /// </remarks>
        public const float MaximumEntryPlungeSpeedMetersPerSecond = 5.0f;

        /// <summary>Emberflow is thick: the same fall barely breaks its surface.</summary>
        public const float EmberflowEntryPlungeScale = 0.35f;

        /// <summary>
        /// Acceleration applied while the body has not yet reached its floating depth: real
        /// gravity, because this is a fall.
        /// </summary>
        /// <remarks>
        /// The swim ramp (<see cref="VerticalAccelerationMetersPerSecondSquared"/>, 6 m/s^2) is a
        /// deliberate feel for SWIMMING — a soft approach to a chosen speed. Reusing it for an
        /// entry made the descent read as being lowered on a winch. Constant velocity is the
        /// perceptual tell: the body reads sustained constant speed as "being lowered" and
        /// acceleration as "falling", so no amount of tuning the target speed alone fixes it.
        /// </remarks>
        public const float EntryFallAccelerationMetersPerSecondSquared = 9.81f;

        /// <summary>
        /// Terminal descent of an unsupported entry, before buoyancy takes hold.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT <see cref="SinkSpeedMetersPerSecond"/>. That constant is the ratified
        /// crouch-held descend rate (voxel_survival_ruleset.md: 1.2 m/s water / 0.6 emberflow) and
        /// must keep meaning exactly that; an unsupported fall is not a held input. Emberflow
        /// keeps the same half-speed relationship the ruleset gives the pair.
        /// </remarks>
        public const float UnsupportedEntryFallSpeedMetersPerSecond = 3.5f;
        public const float EmberflowUnsupportedEntryFallSpeedMetersPerSecond =
            UnsupportedEntryFallSpeedMetersPerSecond
                * (EmberflowSinkSpeedMetersPerSecond / SinkSpeedMetersPerSecond);

        /// <summary>
        /// Vertical velocity to carry into the fluid on the frame swimming takes over, given the
        /// descent the player arrived with. Negative is downward; zero means no plunge.
        /// </summary>
        /// <remarks>
        /// Eric's report (2026-08-23): "when you drop into water over 1 block deep you should fall
        /// deeper into the water." You did not, and the reason was that swimming began from a
        /// standing start -- verticalVelocity is reset on exit, so all the fall momentum was
        /// discarded the instant the surface was crossed and a 0.35 m/s passive drift began. The
        /// player stopped dead ON the water. Seeding the entry frame with the arrival speed lets
        /// the existing 6 m/s^2 approach to the passive-sink target act as the drag that arrests it.
        ///
        /// An upward arrival (swimming up into a fluid cell, an ascending platform) plunges by
        /// definition never: only a descent is carried.
        ///
        /// CONTINUOUS in the arrival speed, deliberately. This used to zero any descent under a
        /// threshold, to keep a step-in from reading as a dunk — but the provider ASSIGNS this
        /// return value to verticalVelocity, so "no plunge" meant a hard stop at the water line:
        /// walking off ground flush with the surface arrives at ~1.4 m/s and was pinned to 0
        /// before the ramp started. That dead stop, not the ramp, is what Eric felt as "slowly
        /// sinking" (2026-08-24). Gentleness for a small entry now comes from the arrival speed
        /// genuinely BEING small, which is also what removes the discontinuity the threshold had
        /// either side of it. A true wade-in never reaches here — that is SwimState.Wading.
        /// </remarks>
        public static float ResolveEntryPlungeVelocity(float impactVerticalVelocity, FluidFamily family)
        {
            float descent = Mathf.Max(0.0f, -impactVerticalVelocity);

            if (descent <= 0.0f)
                return 0.0f;

            float scale = family == FluidFamily.Emberflow ? EmberflowEntryPlungeScale : 1.0f;
            return -Mathf.Min(descent, MaximumEntryPlungeSpeedMetersPerSecond) * scale;
        }

        /// <summary>
        /// How far the mid-body sample must stay below the water line while rising, in metres.
        /// </summary>
        public const float SurfaceBodyMarginMeters = 0.10f;

        /// <summary>
        /// Caps an upward swim velocity so this frame's step cannot lift the mid-body sample out
        /// of the fluid. Returns <paramref name="targetVelocity"/> unchanged for any descent, and
        /// for any ascent that stays under the line.
        /// </summary>
        /// <remarks>
        /// There was no surface clamp at all — ResolveVerticalTarget has no depth or position
        /// parameter, so rise applied at full speed for as long as the button was held, and the
        /// only thing that ended it was the swim STATE. That state is driven by the FEET sample
        /// (capsule base + 0.10 m), and ResolveState deliberately falls through to Surfaced while
        /// the feet are wet and fluid is below them — a load-bearing branch that stops a treading
        /// player oscillating at the water line. The consequence is that the rise ceiling was the
        /// player's feet: equilibrium sat the capsule base ~0.10 m under with the head 1.6 m above
        /// it, which is Eric's "it's like I'm standing on top of the water".
        ///
        /// Clamping at the BODY sample implements the ruleset rather than inventing design: the
        /// state table defines Surfaced as "body in fluid, head in air", and the shipped behaviour
        /// let the body sit almost a metre clear of the water while still reporting Surfaced.
        /// Climbing out at a bank is a separate, specified path (FluidLedge.TryResolveClimbOut,
        /// checked earlier and returning before this is reached), so this cannot block it.
        /// </remarks>
        public static float ClampRiseToSurface(
            float targetVelocity,
            float bodySampleWorldY,
            float surfaceWorldY,
            float deltaSeconds)
        {
            if (targetVelocity <= 0.0f || deltaSeconds <= 0.0f)
                return targetVelocity;

            // Headroom the body sample still has before it breaks the line.
            float allowedRise = (surfaceWorldY - SurfaceBodyMarginMeters) - bodySampleWorldY;

            if (allowedRise <= 0.0f)
                return 0.0f;

            return Mathf.Min(targetVelocity, allowedRise / deltaSeconds);
        }

        public static float AdvanceVerticalVelocity(
            float currentVelocity,
            float targetVelocity,
            float deltaSeconds,
            float accelerationMetersPerSecondSquared = VerticalAccelerationMetersPerSecondSquared) =>
            Mathf.MoveTowards(
                currentVelocity,
                targetVelocity,
                accelerationMetersPerSecondSquared * Mathf.Max(0.0f, deltaSeconds));

        /// <summary>
        /// Acceleration for this frame: gravity while falling into the fluid, the swim ramp once
        /// the body is under and buoyancy owns the motion.
        /// </summary>
        public static float VerticalAccelerationFor(bool bodySubmerged) =>
            bodySubmerged
                ? VerticalAccelerationMetersPerSecondSquared
                : EntryFallAccelerationMetersPerSecondSquared;

        // Whether the head is under the water line, with hysteresis: entering needs the eye a full
        // band below the surface and leaving needs it a full band above, so a head bobbing exactly
        // at the line cannot flip state twice in a frame.
        public static bool ResolveHeadSubmerged(
            bool currentlySubmerged,
            float headWorldY,
            float surfaceWorldY)
        {
            float threshold = currentlySubmerged
                ? surfaceWorldY + SubmersionHysteresisMeters
                : surfaceWorldY - SubmersionHysteresisMeters;

            return headWorldY < threshold;
        }
    }
}
