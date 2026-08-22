using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    /// <summary>
    /// Water audio decisions: when entry splashes, when the submerged loop runs, and how the
    /// splash scales with the fall behind it. Pure functions, so no rig or water needed.
    /// </summary>
    public sealed class BlockiverseSwimFeedbackEditModeTests
    {
        // ── Entry splash ────────────────────────────────────────────────────

        [TestCase(SwimState.Dry, SwimState.Surfaced)]
        [TestCase(SwimState.Dry, SwimState.Swimming)]
        [TestCase(SwimState.Wading, SwimState.Surfaced)]
        [TestCase(SwimState.Wading, SwimState.Swimming)]
        public void EnteringTheWaterSplashes(SwimState previous, SwimState next)
        {
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(previous, next, FluidFamily.Freshwater), Is.True);
        }

        [Test]
        public void WadingInFromDryDoesNotSplash()
        {
            // Shallow water is walked through, and the Water footstep bank already covers it.
            // A splash here would fire on top of every step at the water's edge.
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(SwimState.Dry, SwimState.Wading, FluidFamily.Freshwater),
                Is.False);
        }

        [TestCase(SwimState.Surfaced, SwimState.Swimming)]
        [TestCase(SwimState.Swimming, SwimState.Surfaced)]
        public void MovingBetweenSwimStatesDoesNotResplash(SwimState previous, SwimState next)
        {
            // Bobbing across the water line is one continuous swim, not repeated entries.
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(previous, next, FluidFamily.Freshwater), Is.False);
        }

        [TestCase(SwimState.Swimming, SwimState.Dry)]
        [TestCase(SwimState.Surfaced, SwimState.Wading)]
        public void LeavingTheWaterDoesNotSplash(SwimState previous, SwimState next)
        {
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(previous, next, FluidFamily.Freshwater), Is.False);
        }

        [Test]
        public void BrineSplashesLikeWater()
        {
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(SwimState.Dry, SwimState.Swimming, FluidFamily.Brine),
                Is.True);
        }

        [Test]
        public void EmberflowNeverSplashes()
        {
            // Lava is not water and must not sound like it; contact already has its own hurt cue.
            Assert.That(BlockiverseSwimFeedback.ShouldSplash(SwimState.Dry, SwimState.Swimming, FluidFamily.Emberflow),
                Is.False);
        }

        // ── Submerged loop ──────────────────────────────────────────────────

        [Test]
        public void SubmergedLoopRunsOnlyWithTheHeadUnder()
        {
            Assert.That(BlockiverseSwimFeedback.ShouldLoopSubmerged(SwimState.Swimming, FluidFamily.Freshwater), Is.True);

            // Surfaced is treading water — head in air, so the underwater sound would be wrong.
            Assert.That(BlockiverseSwimFeedback.ShouldLoopSubmerged(SwimState.Surfaced, FluidFamily.Freshwater), Is.False);
            Assert.That(BlockiverseSwimFeedback.ShouldLoopSubmerged(SwimState.Wading, FluidFamily.Freshwater), Is.False);
            Assert.That(BlockiverseSwimFeedback.ShouldLoopSubmerged(SwimState.Dry, FluidFamily.Freshwater), Is.False);
        }

        [Test]
        public void SubmergedLoopNeverRunsInEmberflow()
        {
            Assert.That(BlockiverseSwimFeedback.ShouldLoopSubmerged(SwimState.Swimming, FluidFamily.Emberflow), Is.False);
        }

        // ── Entry splash scaling ────────────────────────────────────────────

        [Test]
        public void FasterEntriesSplashLouder()
        {
            float step = BlockiverseSwimFeedback.EntrySplashScale(0.5f);
            float drop = BlockiverseSwimFeedback.EntrySplashScale(3.0f);
            float plunge = BlockiverseSwimFeedback.EntrySplashScale(8.0f);

            Assert.That(step, Is.LessThan(drop));
            Assert.That(drop, Is.LessThan(plunge));
        }

        [Test]
        public void SplashScaleStaysWithinTheAudibleBand()
        {
            // The floor matters: a gentle entry must still be heard, not silently swallowed.
            // The ceiling matters because volumeScale multiplies the mix, never bypasses it.
            foreach (float speed in new[] { 0f, 0.5f, 1f, 3f, 6f, 20f, 1000f })
            {
                float scale = BlockiverseSwimFeedback.EntrySplashScale(speed);
                Assert.That(scale, Is.InRange(0.45f, 1.0f), $"descent {speed} m/s produced {scale}");
            }
        }

        // ── Invariants the review pass established ──────────────────────────

        [Test]
        public void StrokeFloorSitsBelowPassiveSinkSpeed()
        {
            // The two were once equal at 0.35, and because the stroke test used TOTAL speed a
            // player sinking with no input at all stroked forever. The floor now applies to
            // horizontal travel only, and must stay clear of the passive sink rate so that a
            // future change to either constant cannot silently recreate the collision.
            Assert.That(BlockiverseSwimFeedback.StrokeMinimumSpeed,
                Is.LessThan(BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond),
                "a purely sinking player must not register as stroking");
        }

        [Test]
        public void SubmergedLoopHasAReleaseWindow()
        {
            // SwimState samples the head cell with no hysteresis, and stopping a loop destroys its
            // AudioSource while starting one recreates it from sample zero. Treading water at the
            // surface therefore flips state every frame; without a release window that is a click
            // train and per-frame GC rather than an underwater bed.
            Assert.That(BlockiverseSwimFeedback.SubmergedReleaseSeconds, Is.GreaterThan(0.0f));
            Assert.That(BlockiverseSwimFeedback.SubmergedReleaseSeconds,
                Is.GreaterThan(BlockiverseSwimMotion.SubmersionHysteresisMeters /
                               BlockiverseSwimMotion.PassiveSinkSpeedMetersPerSecond * 0.5f),
                "the window must outlast a passive-sink round trip across the surface band");
        }

        [Test]
        public void ResplashLockoutOutlastsASuppressedLocomotionRoundTrip()
        {
            // ExitSwimming forces the state to Dry whenever locomotion is suppressed or creative
            // flight takes over, so opening and closing a menu while afloat looks like an exit and
            // a fresh entry. The lockout is what stops that re-splashing.
            Assert.That(BlockiverseSwimFeedback.ResplashLockoutSeconds, Is.GreaterThan(1.0f));
        }

        [Test]
        public void TeleportSpeedThresholdIsAboveAnyRealSwimSpeed()
        {
            // A teleport or respawn resolves as one enormous frame delta. Left unguarded it reads
            // as a maximum-volume entry splash and a stroke on the same frame.
            Assert.That(BlockiverseSwimFeedback.TeleportSpeedThreshold,
                Is.GreaterThan(BlockiverseSwimMotion.SinkSpeedMetersPerSecond * 10.0f));
        }

        [Test]
        public void SplashScaleIsMonotonicAndClamped()
        {
            Assert.That(BlockiverseSwimFeedback.EntrySplashScale(0f),
                Is.EqualTo(BlockiverseSwimFeedback.EntrySplashScale(1.0f)).Within(0.0001f),
                "everything at or below the quiet threshold should land on the floor");
            Assert.That(BlockiverseSwimFeedback.EntrySplashScale(6.0f),
                Is.EqualTo(BlockiverseSwimFeedback.EntrySplashScale(50.0f)).Within(0.0001f),
                "everything at or above the loud threshold should land on the ceiling");
        }
    }
}
