using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Pins the sky flash's shape and, more importantly, its distance pairing: the flash has to
    // fall to exactly nothing at the outer edge of the strike ring, or a bolt on the horizon
    // arrives with a flash that says it was overhead.
    public sealed class LightningFlashSolverEditModeTests
    {
        [Test]
        public void TheFlashRangeIsPinnedToTheStrikeRing()
        {
            // Asserted against the selector's own constant rather than a copy of the number, so
            // the two cannot drift apart when either is retuned.
            Assert.That(
                LightningFlashSolver.NoStrengthDistance,
                Is.EqualTo((float)LightningStrikeSelector.MaxRingRadius));

            Assert.That(
                LightningFlashSolver.FullStrengthDistance,
                Is.GreaterThan(LightningStrikeSelector.MinRingRadius),
                "Some part of the ring should be close enough to earn a full-strength flash.");
        }

        [Test]
        public void DistanceStrengthRunsFromFullToExactlyZero()
        {
            Assert.That(LightningFlashSolver.DistanceStrength(0.0f), Is.EqualTo(1.0f));
            Assert.That(
                LightningFlashSolver.DistanceStrength(LightningStrikeSelector.MinRingRadius), Is.EqualTo(1.0f),
                "A strike at the ring's inner edge should wash out the sky.");
            Assert.That(
                LightningFlashSolver.DistanceStrength(LightningFlashSolver.NoStrengthDistance), Is.EqualTo(0.0f),
                "A bolt on the horizon must produce no flash at all, not a residual glow.");
            Assert.That(LightningFlashSolver.DistanceStrength(500.0f), Is.EqualTo(0.0f));
        }

        [Test]
        public void DistanceStrengthDecreasesMonotonically()
        {
            float previous = LightningFlashSolver.DistanceStrength(0.0f);

            for (float distance = 1.0f; distance <= 120.0f; distance += 1.0f)
            {
                float current = LightningFlashSolver.DistanceStrength(distance);
                Assert.That(current, Is.LessThanOrEqualTo(previous), $"Strength rose at {distance} blocks.");
                previous = current;
            }
        }

        [Test]
        public void MidRangeStrikesAreDimmerThanALinearRampWouldMakeThem()
        {
            // Quadratic falloff, not linear: real brightness falls with distance squared, and a
            // linear ramp reads as far too bright out in the middle of the ring.
            const float midpoint = (LightningFlashSolver.FullStrengthDistance + LightningFlashSolver.NoStrengthDistance) * 0.5f;

            Assert.That(LightningFlashSolver.DistanceStrength(midpoint), Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void IntensityRampsInThenDecaysToExactlyZero()
        {
            Assert.That(LightningFlashSolver.Intensity(-1.0f), Is.EqualTo(0.0f));
            Assert.That(LightningFlashSolver.Intensity(0.0f), Is.EqualTo(0.0f));

            // The attack exists so the flash is not a single-frame pop at 72 Hz, which reads as a
            // dropped frame rather than as lightning.
            Assert.That(
                LightningFlashSolver.Intensity(LightningFlashSolver.AttackSeconds * 0.5f),
                Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(
                LightningFlashSolver.Intensity(LightningFlashSolver.AttackSeconds),
                Is.EqualTo(1.0f).Within(1e-4f));

            // Exactly zero at and past the duration: any residual would bleed permanently into
            // ambient, because the lighting cycle adds this term every single frame.
            Assert.That(LightningFlashSolver.Intensity(LightningFlashSolver.FlashDurationSeconds), Is.EqualTo(0.0f));
            Assert.That(LightningFlashSolver.Intensity(5.0f), Is.EqualTo(0.0f));
        }

        [Test]
        public void IntensityDecaysMonotonicallyAfterTheAttack()
        {
            float previous = LightningFlashSolver.Intensity(LightningFlashSolver.AttackSeconds);

            for (float t = LightningFlashSolver.AttackSeconds + 0.005f;
                 t < LightningFlashSolver.FlashDurationSeconds;
                 t += 0.005f)
            {
                float current = LightningFlashSolver.Intensity(t);
                Assert.That(current, Is.LessThanOrEqualTo(previous + 1e-5f), $"Intensity rose at {t}s.");
                previous = current;
            }
        }

        [Test]
        public void AmbientBoostNeverExceedsWhiteAndReturnsToBaseline()
        {
            var brightDay = new Color(0.85f, 0.88f, 0.92f, 1.0f);

            Color peak = LightningFlashSolver.AmbientBoost(brightDay, 1.0f, LightningFlashSolver.AttackSeconds);
            Assert.That(peak.r, Is.LessThanOrEqualTo(1.0f));
            Assert.That(peak.g, Is.LessThanOrEqualTo(1.0f));
            Assert.That(peak.b, Is.LessThanOrEqualTo(1.0f));
            Assert.That(peak.a, Is.EqualTo(brightDay.a), "Alpha is not part of the flash.");
            Assert.That(peak.b, Is.GreaterThan(brightDay.b), "A flash at full strength has to be visible.");

            Color after = LightningFlashSolver.AmbientBoost(brightDay, 1.0f, 5.0f);
            Assert.That(after, Is.EqualTo(brightDay));

            Color none = LightningFlashSolver.AmbientBoost(brightDay, 0.0f, LightningFlashSolver.AttackSeconds);
            Assert.That(none, Is.EqualTo(brightDay), "A zero-strength strike must leave ambient untouched.");
        }

        [Test]
        public void ADistantStrikeLeavesTheSkyAlone()
        {
            var night = new Color(0.08f, 0.09f, 0.14f, 1.0f);
            float strength = LightningFlashSolver.DistanceStrength(LightningStrikeSelector.MaxRingRadius);

            Assert.That(
                LightningFlashSolver.AmbientBoost(night, strength, LightningFlashSolver.AttackSeconds),
                Is.EqualTo(night));
        }
    }
}
