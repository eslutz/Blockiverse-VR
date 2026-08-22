using System;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Pins the ring that decides where lightning lands. Three properties are load-bearing and each
    // is easy to break silently: the comfort exclusion must survive any retuning of the ring, the
    // spread must genuinely cover near-to-far (that is the entire visible point of the feature),
    // and the draw must consume a fixed number of RNG values so rejections cannot shift the snow
    // stream that shares the world seed.
    public sealed class LightningStrikeSelectorEditModeTests
    {
        static readonly WorldBounds LargeBounds = new(512, 128, 512);

        [Test]
        public void TheRingStartsOutsideThePlayerComfortExclusion()
        {
            // Asserted directly rather than inferred, because a tuning pass that lowered
            // MinRingRadius would otherwise start dropping bolts on the player's head and no test
            // would notice.
            Assert.That(
                LightningStrikeSelector.MinRingRadius,
                Is.GreaterThan(EnvironmentDynamicsController.StrikePlayerExclusionRadius));
        }

        [Test]
        public void EveryCandidateLandsInsideTheRing()
        {
            var random = new Random(20260821);
            var xs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var zs = new int[LightningStrikeSelector.MaxSelectionAttempts];

            double closest = double.MaxValue;
            double farthest = 0.0;

            for (int i = 0; i < 500; i++)
            {
                int count = LightningStrikeSelector.DrawRingCandidates(random, 256, 256, LargeBounds, xs, zs);

                for (int c = 0; c < count; c++)
                {
                    double distance = Math.Sqrt((xs[c] - 256.0) * (xs[c] - 256.0) + (zs[c] - 256.0) * (zs[c] - 256.0));
                    closest = Math.Min(closest, distance);
                    farthest = Math.Max(farthest, distance);
                }
            }

            // Rounding to a block can pull a candidate a fraction under the nominal radius, which
            // is fine -- what matters is that it can never reach the exclusion.
            Assert.That(closest, Is.GreaterThan(EnvironmentDynamicsController.StrikePlayerExclusionRadius));
            Assert.That(closest, Is.GreaterThan(LightningStrikeSelector.MinRingRadius - 1.0));
            Assert.That(farthest, Is.LessThan(LightningStrikeSelector.MaxRingRadius + 1.0));
        }

        [Test]
        public void CandidatesSpreadAcrossTheWholeDistanceBand()
        {
            // The property the whole feature rests on: strikes must vary from close enough to fill
            // the view to distant enough to be a silhouette. A draw that quietly concentrated at
            // one end would still pass every other test here.
            var random = new Random(7717);
            var xs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var zs = new int[LightningStrikeSelector.MaxSelectionAttempts];

            int near = 0;
            int mid = 0;
            int far = 0;
            const double band = LightningStrikeSelector.MaxRingRadius - LightningStrikeSelector.MinRingRadius;

            for (int i = 0; i < 5000; i++)
            {
                int count = LightningStrikeSelector.DrawRingCandidates(random, 256, 256, LargeBounds, xs, zs);

                for (int c = 0; c < count; c++)
                {
                    double distance = Math.Sqrt((xs[c] - 256.0) * (xs[c] - 256.0) + (zs[c] - 256.0) * (zs[c] - 256.0));
                    double t = (distance - LightningStrikeSelector.MinRingRadius) / band;

                    if (t < 1.0 / 3.0)
                        near++;
                    else if (t < 2.0 / 3.0)
                        mid++;
                    else
                        far++;
                }
            }

            int total = near + mid + far;
            Assert.That(total, Is.GreaterThan(30000), "Fixture guard: expected the full draw to survive in-bounds.");

            // Uniform-in-radius means roughly a third each. The bound is loose enough to survive
            // rounding but tight enough to catch a switch to area-uniform sampling, which would
            // push the far third well past half.
            foreach (int bucket in new[] { near, mid, far })
                Assert.That(bucket / (double)total, Is.InRange(0.25, 0.42));
        }

        [Test]
        public void EveryAngleIsReachable()
        {
            var random = new Random(31337);
            var xs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var zs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var sectors = new bool[12];

            for (int i = 0; i < 2000; i++)
            {
                int count = LightningStrikeSelector.DrawRingCandidates(random, 256, 256, LargeBounds, xs, zs);

                for (int c = 0; c < count; c++)
                {
                    double angle = Math.Atan2(zs[c] - 256.0, xs[c] - 256.0);
                    if (angle < 0.0)
                        angle += Math.PI * 2.0;

                    sectors[(int)(angle / (Math.PI * 2.0) * 12.0) % 12] = true;
                }
            }

            Assert.That(sectors, Is.All.True, "Some compass sectors never receive a strike.");
        }

        [Test]
        public void TheDrawConsumesTheSameRngRegardlessOfRejections()
        {
            // The test that protects the snow stream. A corner anchor rejects most of its
            // candidates; the middle of the world rejects none. Both must leave the RNG in the
            // same place, or a storm would silently shift every snow column that follows it.
            var xs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var zs = new int[LightningStrikeSelector.MaxSelectionAttempts];

            var corner = new Random(99);
            LightningStrikeSelector.DrawRingCandidates(corner, 0, 0, LargeBounds, xs, zs);
            int afterCorner = corner.Next();

            var centre = new Random(99);
            LightningStrikeSelector.DrawRingCandidates(centre, 256, 256, LargeBounds, xs, zs);
            int afterCentre = centre.Next();

            Assert.That(afterCorner, Is.EqualTo(afterCentre));
        }

        [Test]
        public void CornerAnchorsNeverProduceOutOfBoundsCandidates()
        {
            var random = new Random(5150);
            var xs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var zs = new int[LightningStrikeSelector.MaxSelectionAttempts];
            var small = new WorldBounds(64, 128, 64);

            int produced = 0;

            foreach ((int ax, int az) in new[] { (0, 0), (63, 0), (0, 63), (63, 63) })
            {
                for (int i = 0; i < 500; i++)
                {
                    int count = LightningStrikeSelector.DrawRingCandidates(random, ax, az, small, xs, zs);
                    produced += count;

                    for (int c = 0; c < count; c++)
                    {
                        Assert.That(xs[c], Is.InRange(0, small.Width - 1));
                        Assert.That(zs[c], Is.InRange(0, small.Depth - 1));
                    }
                }
            }

            Assert.That(produced, Is.GreaterThan(0), "A 64-block world should still place some strikes.");
        }

        [Test]
        public void ProjectionPlacesTheCandidateAtTheRequestedAngleAndRadius()
        {
            LightningStrikeSelector.ProjectCandidate(0.0, 1.0, 100, 100, out int east, out int eastZ);
            Assert.That(east, Is.EqualTo(100 + LightningStrikeSelector.MaxRingRadius));
            Assert.That(eastZ, Is.EqualTo(100));

            LightningStrikeSelector.ProjectCandidate(0.25, 0.0, 100, 100, out int northX, out int north);
            Assert.That(northX, Is.EqualTo(100));
            Assert.That(north, Is.EqualTo(100 + LightningStrikeSelector.MinRingRadius));
        }

        [Test]
        public void ExclusionIsAnInclusiveRadiusTest()
        {
            Assert.That(LightningStrikeSelector.IsInsideExclusion(10, 0, 0, 0, 10), Is.True);
            Assert.That(LightningStrikeSelector.IsInsideExclusion(11, 0, 0, 0, 10), Is.False);
            Assert.That(LightningStrikeSelector.IsInsideExclusion(0, 0, 0, 0, 0), Is.True);
        }
    }
}
