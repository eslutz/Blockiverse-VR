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

            return submersion.FeetSubmerged ? SwimState.Wading : SwimState.Dry;
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
        public static float ResolveVerticalTarget(
            bool riseHeld,
            bool sinkHeld,
            bool passiveSinkEnabled,
            FluidFamily family)
        {
            if (riseHeld)
                return RiseSpeedFor(family);

            if (sinkHeld)
                return -SinkSpeedFor(family);

            return passiveSinkEnabled ? -PassiveSinkSpeedFor(family) : 0.0f;
        }

        public static float AdvanceVerticalVelocity(float currentVelocity, float targetVelocity, float deltaSeconds) =>
            Mathf.MoveTowards(
                currentVelocity,
                targetVelocity,
                VerticalAccelerationMetersPerSecondSquared * Mathf.Max(0.0f, deltaSeconds));

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
