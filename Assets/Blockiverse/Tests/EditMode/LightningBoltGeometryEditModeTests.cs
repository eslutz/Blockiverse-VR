using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Pins the bolt's shape. Two of these guard bugs that would otherwise only be findable on
    // device: the foot has to sit exactly on the scorched block, and the ribbon has to stay wider
    // than a pixel at the far end of the strike ring or distant bolts shimmer and drop out.
    public sealed class LightningBoltGeometryEditModeTests
    {
        const float Height = 42.0f;

        static Vector3[] BuildMain(int seed, int segments = LightningBoltGeometry.DefaultSegments)
        {
            var points = new Vector3[segments + 1];
            LightningBoltGeometry.BuildPolyline(new System.Random(seed), Height, segments, points);
            return points;
        }

        [Test]
        public void TheFootIsPinnedToTheStruckBlock()
        {
            // The view positions the whole bolt at the struck block, so a non-zero first point
            // would leave the bolt hanging beside the scorch mark rather than in it.
            for (int seed = 0; seed < 32; seed++)
                Assert.That(BuildMain(seed)[0], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void HeightIncreasesStrictlyAndReachesTheCloudDeck()
        {
            Vector3[] points = BuildMain(1234);

            for (int i = 1; i < points.Length; i++)
            {
                // Strictly increasing, so the ribbon extrusion can never fold back on itself and
                // produce degenerate triangles.
                Assert.That(points[i].y, Is.GreaterThan(points[i - 1].y));
            }

            Assert.That(points[^1].y, Is.EqualTo(Height).Within(1e-3f));
        }

        [Test]
        public void LateralWanderStaysWithinItsCap()
        {
            // 11% of height per step is what made the accepted render read as lightning rather
            // than as bent wire; the clamp stops a long walk drifting into a diagonal streak.
            float perStepCap = Height * LightningBoltGeometry.LateralWanderFraction;

            for (int seed = 0; seed < 64; seed++)
            {
                Vector3[] points = BuildMain(seed);

                for (int i = 1; i < points.Length; i++)
                {
                    float step = Mathf.Abs(points[i].x - points[i - 1].x);
                    Assert.That(step, Is.LessThanOrEqualTo(perStepCap * 1.5f), $"Seed {seed} jumped {step} at {i}.");
                }
            }
        }

        [Test]
        public void TheSameSeedDrawsTheSameBolt()
        {
            // Clients build the bolt themselves from a relayed strike, so two peers must agree.
            Vector3[] first = BuildMain(777);
            Vector3[] again = BuildMain(777);

            Assert.That(again, Is.EqualTo(first));
            Assert.That(BuildMain(778), Is.Not.EqualTo(first), "Different strikes should not draw the same bolt.");
        }

        [Test]
        public void ForksStartOnTheChannelAndTravelDownward()
        {
            Vector3[] main = BuildMain(4242);
            var fork = new Vector3[24];
            var random = new System.Random(99);

            for (int startIndex = 1; startIndex < main.Length; startIndex++)
            {
                int count = LightningBoltGeometry.BuildFork(random, main, main.Length, startIndex, Height, fork);

                Assert.That(count, Is.GreaterThanOrEqualTo(3));
                Assert.That(fork[0], Is.EqualTo(main[startIndex]),
                    "A fork that begins in mid-air reads as a second bolt, not as a branch.");

                for (int i = 1; i < count; i++)
                {
                    Assert.That(fork[i].y, Is.LessThan(fork[i - 1].y), "Forks travel downward.");
                    Assert.That(fork[i].y, Is.GreaterThanOrEqualTo(-1e-3f), "A fork must not go below the strike point.");
                }
            }
        }

        [Test]
        public void RibbonUvsSpanTheWidthSoTheGradientWorks()
        {
            // The soft edge comes entirely from sampling the alpha ramp across u. If u did not
            // span 0..1 the bolt would be a hard-edged band, which is the look that was rejected.
            Vector3[] points = BuildMain(5);
            var vertices = new Vector3[points.Length * 2];
            var uvs = new Vector2[points.Length * 2];
            var indices = new int[(points.Length - 1) * 6];

            LightningBoltGeometry.BuildRibbon(
                points, points.Length, LightningBoltGeometry.HalfWidthBlocks, vertices, uvs, indices);

            for (int i = 0; i < points.Length; i++)
            {
                Assert.That(uvs[i * 2].x, Is.EqualTo(0.0f));
                Assert.That(uvs[i * 2 + 1].x, Is.EqualTo(1.0f));
                Assert.That(vertices[i * 2 + 1].x - vertices[i * 2].x,
                    Is.EqualTo(LightningBoltGeometry.HalfWidthBlocks * 2.0f).Within(1e-4f));
            }

            Assert.That(uvs[0].y, Is.EqualTo(0.0f));
            Assert.That(uvs[^1].y, Is.EqualTo(1.0f));
        }

        [Test]
        public void RibbonsAppendIntoOneMeshWithoutOverwritingEachOther()
        {
            // Main channel, three forks and the impact glow all share one mesh so the whole bolt
            // is a single draw call.
            Vector3[] points = BuildMain(11, segments: 3);
            var vertices = new Vector3[32];
            var uvs = new Vector2[32];
            var indices = new int[64];

            LightningBoltGeometry.BuildRibbon(points, 4, 0.5f, vertices, uvs, indices, 0, 0);
            LightningBoltGeometry.BuildRibbon(points, 4, 0.5f, vertices, uvs, indices, 8, 18);

            for (int i = 0; i < 18; i++)
                Assert.That(indices[18 + i], Is.EqualTo(indices[i] + 8), "The second ribbon must index its own vertices.");
        }

        [Test]
        public void DistantBoltsAreWidenedToSurviveRasterisation()
        {
            // A ribbon thinner than a pixel does not anti-alias -- it drops out and shimmers frame
            // to frame. The comparison renders only stayed legible at 90 blocks because a software
            // rasteriser floored sub-pixel coverage, which hardware will not do.
            const float fov = 90.0f;
            const float screenHeight = 1832.0f;

            float near = LightningBoltGeometry.ResolveHalfWidth(LightningStrikeSelector.MinRingRadius, fov, screenHeight);
            float far = LightningBoltGeometry.ResolveHalfWidth(LightningStrikeSelector.MaxRingRadius, fov, screenHeight);

            // Worth recording: at Quest framebuffer sizes the authored 0.38-block half-width
            // already clears the minimum across the entire strike ring, so this floor does not
            // currently bind at either end. It is a guard against a future wider ring or a
            // lower-resolution eye buffer, not a correction being applied today.
            Assert.That(near, Is.EqualTo(LightningBoltGeometry.HalfWidthBlocks));
            Assert.That(far, Is.EqualTo(LightningBoltGeometry.HalfWidthBlocks));

            // Whatever the distance, the on-screen width must clear the minimum.
            for (float distance = 10.0f; distance <= 400.0f; distance += 5.0f)
            {
                float halfWidth = LightningBoltGeometry.ResolveHalfWidth(distance, fov, screenHeight);
                float worldHeight = 2.0f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                float pixels = halfWidth * 2.0f / (worldHeight / screenHeight);

                Assert.That(pixels, Is.GreaterThanOrEqualTo(LightningBoltGeometry.MinimumScreenWidthPixels - 1e-3f),
                    $"A bolt at {distance} blocks would render {pixels:0.00}px wide.");
            }

            // A degenerate camera must fall back rather than divide by zero.
            Assert.That(
                LightningBoltGeometry.ResolveHalfWidth(0.0f, fov, screenHeight),
                Is.EqualTo(LightningBoltGeometry.HalfWidthBlocks));
        }

        [Test]
        public void TheGradientRunsFromAnOpaqueCoreToTransparentEdges()
        {
            Color[] pixels = LightningBoltGeometry.BuildGradientPixels(64);

            Assert.That(pixels[0].a, Is.LessThan(0.05f), "The ribbon's edges must fade out, not stop.");
            Assert.That(pixels[^1].a, Is.LessThan(0.05f));
            Assert.That(pixels[32].a, Is.GreaterThan(0.9f), "The core has to be effectively opaque.");

            // White-hot core, blue edge -- what reads as lightning against a storm sky.
            Assert.That(pixels[32].r, Is.GreaterThan(0.95f));
            Assert.That(pixels[32].g, Is.GreaterThan(0.95f));
            Assert.That(pixels[4].b, Is.GreaterThan(pixels[4].r), "The outer ribbon should be blue, not white.");

            // Monotonic from the centre out: any bump would read as a banding artefact.
            for (int i = 1; i <= 32; i++)
                Assert.That(pixels[i].a, Is.GreaterThanOrEqualTo(pixels[i - 1].a - 1e-4f));
        }
    }
}
