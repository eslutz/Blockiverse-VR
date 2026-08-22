using System;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    // Where lightning lands, relative to a player.
    //
    // Strikes used to be drawn uniformly from the entire world and then rejected within 8 blocks of
    // a head, which meant essentially every strike happened somewhere nobody was looking. Nothing
    // about the bolt or the flash mattered because nobody ever saw one. This biases the draw into a
    // ring centred on a player instead.
    //
    // The band is WIDE and the draw across it is UNIFORM IN RADIUS, deliberately. Distance is the
    // point: consecutive strikes should differ, some close enough to fill the view and some distant
    // silhouettes on the horizon, and the flash and thunder are both scaled from that distance.
    // Area-uniform sampling (radius = sqrt(u)) was rejected -- it piles strikes up against the outer
    // edge where storm fog washes them out, which is close to the problem being fixed.
    public static class LightningStrikeSelector
    {
        // Just outside EnvironmentDynamicsController.StrikePlayerExclusionRadius, so the comfort
        // exclusion stays an invariant of the selection rather than a filter applied after it.
        public const int MinRingRadius = 10;
        public const int MaxRingRadius = 96;

        // Candidates drawn per check. Every one is drawn up front whether or not earlier ones were
        // usable, so the RNG stream advances by a fixed amount no matter how the rejections fall.
        public const int MaxSelectionAttempts = 8;

        // Projects a unit angle/radius pair onto a world column around the anchor. Split out from
        // the draw so tests can pin the geometry without an RNG.
        public static void ProjectCandidate(
            double angleUnit,
            double radiusUnit,
            int anchorX,
            int anchorZ,
            out int x,
            out int z)
        {
            double angle = angleUnit * Math.PI * 2.0;
            double radius = MinRingRadius + radiusUnit * (MaxRingRadius - MinRingRadius);

            x = anchorX + (int)Math.Round(Math.Cos(angle) * radius);
            z = anchorZ + (int)Math.Round(Math.Sin(angle) * radius);
        }

        // Fills xs/zs with the in-bounds candidates around the anchor and returns how many there
        // are. Both buffers must hold at least MaxSelectionAttempts entries.
        //
        // Consumes exactly 2 * MaxSelectionAttempts draws every call. That is not incidental: it is
        // what lets lightning share a deterministic world without a variable number of rejections
        // shifting every roll that follows.
        public static int DrawRingCandidates(
            Random random,
            int anchorX,
            int anchorZ,
            WorldBounds bounds,
            int[] xs,
            int[] zs)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (xs == null || xs.Length < MaxSelectionAttempts)
                throw new ArgumentException($"Needs room for {MaxSelectionAttempts} candidates.", nameof(xs));
            if (zs == null || zs.Length < MaxSelectionAttempts)
                throw new ArgumentException($"Needs room for {MaxSelectionAttempts} candidates.", nameof(zs));

            int count = 0;

            for (int attempt = 0; attempt < MaxSelectionAttempts; attempt++)
            {
                // Both draws happen before any rejection, so the stream position after this loop
                // depends only on MaxSelectionAttempts.
                double angleUnit = random.NextDouble();
                double radiusUnit = random.NextDouble();

                ProjectCandidate(angleUnit, radiusUnit, anchorX, anchorZ, out int x, out int z);

                if (x < 0 || x >= bounds.Width || z < 0 || z >= bounds.Depth)
                    continue;

                xs[count] = x;
                zs[count] = z;
                count++;
            }

            return count;
        }

        // Squared-distance test on the horizontal plane, used for the spawn and player exclusions.
        public static bool IsInsideExclusion(int x, int z, int centerX, int centerZ, int radius)
        {
            int dx = x - centerX;
            int dz = z - centerZ;
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
