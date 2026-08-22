namespace Blockiverse.Core
{
    /// <summary>
    /// Interaction reach limits shared by the local preview path and the host authority check.
    /// This lives in Core because <c>Blockiverse.Networking</c> validates client edit distance
    /// server-side and cannot reference <c>Blockiverse.Gameplay</c>, where the local reach check
    /// lives — both must agree on the same number.
    /// </summary>
    public static class BlockiverseInteractionLimits
    {
        /// <summary>Maximum distance from the player's head to an edited block (ruleset §16).</summary>
        public const float MaxBlockInteractionReachMeters = 6.0f;

        /// <summary>
        /// Slack the host adds on top of the local reach limit before rejecting an edit. Remote
        /// head poses arrive at 30 Hz over unreliable delivery, so the host's view of a client's
        /// head trails the client's own view by up to a frame or two of locomotion. Rejecting at
        /// exactly the local limit would drop legitimate edits made while moving.
        /// </summary>
        public const float HostReachToleranceMeters = 1.5f;

        /// <summary>The distance the host actually enforces for client-requested block edits.</summary>
        public const float MaxHostValidatedReachMeters =
            MaxBlockInteractionReachMeters + HostReachToleranceMeters;

        /// <summary>
        /// Distance test from a world-space point to the unit cube occupying a block cell —
        /// nearest point on the block's box, not its centre, so a reach limit means the same
        /// thing regardless of which face the player is looking at. Takes loose floats rather
        /// than BlockPosition/Vector3 because Core sits below both Voxel and the engine types.
        /// </summary>
        public static bool IsWithinReach(
            float originX,
            float originY,
            float originZ,
            int blockX,
            int blockY,
            int blockZ,
            float maxReachMeters)
        {
            float distanceX = DistanceOutsideAxis(originX, blockX, blockX + 1);
            float distanceY = DistanceOutsideAxis(originY, blockY, blockY + 1);
            float distanceZ = DistanceOutsideAxis(originZ, blockZ, blockZ + 1);

            return distanceX * distanceX + distanceY * distanceY + distanceZ * distanceZ <=
                   maxReachMeters * maxReachMeters;
        }

        static float DistanceOutsideAxis(float value, int minInclusive, int maxExclusive)
        {
            if (value < minInclusive)
                return minInclusive - value;

            if (value > maxExclusive)
                return value - maxExclusive;

            return 0.0f;
        }
    }
}
