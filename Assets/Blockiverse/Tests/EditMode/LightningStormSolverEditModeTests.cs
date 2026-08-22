using Blockiverse.Gameplay;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Pins the storm-intensity model that replaced the flat 35% strike chance. Two properties
    // matter and both are easy to break with a tuning pass: storms must actually DIFFER from one
    // another (that is the whole feature), and every peer must derive the same storm from the
    // same synced inputs, because a client that disagreed would predict strikes the host never
    // makes.
    public sealed class LightningStormSolverEditModeTests
    {
        const int ThunderstormDurationTicks = 1200;

        [Test]
        public void StormDurationFixtureMatchesTheWeatherService()
        {
            // Guards the constant this file reasons about: if the thunderstorm duration is
            // retuned, the progress cases below are measuring the wrong arc.
            Assert.That(
                WeatherService.MinimumStateDurationTicks(WeatherState.Thunderstorm),
                Is.EqualTo(ThunderstormDurationTicks));
        }

        [Test]
        public void CharacterIsStableForAStormAndVariesBetweenStorms()
        {
            const int seed = 4242;

            float first = LightningStormSolver.ResolveCharacter(seed, 12000);
            float again = LightningStormSolver.ResolveCharacter(seed, 12000);

            // Same storm, same answer -- this is what lets a client agree with the host without
            // the character ever being sent.
            Assert.That(again, Is.EqualTo(first));

            // Different storms must not all land on the same violence. Sampling a spread rather
            // than a single pair, because two adjacent rolls could coincide by chance.
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 64; i++)
                seen.Add(Mathf10(LightningStormSolver.ResolveCharacter(seed, 1200L * i)));

            Assert.That(seen.Count, Is.GreaterThan(5), "Storm character barely varies between storms.");
        }

        [Test]
        public void CharacterStaysInUnitRange()
        {
            for (int i = 0; i < 512; i++)
            {
                float character = LightningStormSolver.ResolveCharacter(worldSeed: -991, stormStartTick: 7L * i);
                Assert.That(character, Is.GreaterThanOrEqualTo(0.0f).And.LessThan(1.0f));
            }
        }

        [Test]
        public void ViolentStormsStrikeMoreOftenThanLightOnes()
        {
            int light = LightningStormSolver.StrikeChancePercent(character: 0.0f, progress: 0.5f);
            int violent = LightningStormSolver.StrikeChancePercent(character: 1.0f, progress: 0.5f);

            Assert.That(light, Is.EqualTo(LightningStormSolver.MinPeakChancePercent));
            Assert.That(violent, Is.EqualTo(LightningStormSolver.MaxPeakChancePercent));
            Assert.That(violent, Is.GreaterThan(light * 2), "A violent storm should feel categorically busier.");
        }

        [Test]
        public void TheArcBuildsPeaksAndTapers()
        {
            float start = LightningStormSolver.ResolveArcScale(0.0f);
            float quarter = LightningStormSolver.ResolveArcScale(0.25f);
            float middle = LightningStormSolver.ResolveArcScale(0.5f);
            float threeQuarters = LightningStormSolver.ResolveArcScale(0.75f);
            float end = LightningStormSolver.ResolveArcScale(1.0f);

            Assert.That(start, Is.LessThan(quarter));
            Assert.That(quarter, Is.LessThan(middle));
            Assert.That(threeQuarters, Is.LessThan(middle));
            Assert.That(end, Is.LessThan(threeQuarters));
            Assert.That(middle, Is.EqualTo(1.0f).Within(1e-5f));

            // The edges never go silent: a storm that stops striking entirely reads as broken
            // weather rather than as a storm tapering off.
            Assert.That(start, Is.EqualTo(LightningStormSolver.MinArcScale).Within(1e-5f));
            Assert.That(end, Is.EqualTo(LightningStormSolver.MinArcScale).Within(1e-5f));
        }

        [Test]
        public void ProgressClampsToTheStormsLife()
        {
            Assert.That(LightningStormSolver.ResolveProgress(0, ThunderstormDurationTicks), Is.EqualTo(0.0f));
            Assert.That(LightningStormSolver.ResolveProgress(600, ThunderstormDurationTicks), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(LightningStormSolver.ResolveProgress(1200, ThunderstormDurationTicks), Is.EqualTo(1.0f));

            // Overrun and a missing duration both have to stay in range rather than producing a
            // chance percent outside 0..100 downstream.
            Assert.That(LightningStormSolver.ResolveProgress(99999, ThunderstormDurationTicks), Is.EqualTo(1.0f));
            Assert.That(LightningStormSolver.ResolveProgress(600, 0), Is.EqualTo(0.0f));
        }

        [Test]
        public void StrikeChanceAlwaysStaysAPlausiblePercent()
        {
            for (int c = 0; c <= 10; c++)
            {
                for (int p = 0; p <= 10; p++)
                {
                    int chance = LightningStormSolver.StrikeChancePercent(c / 10.0f, p / 10.0f);
                    Assert.That(chance, Is.InRange(1, 100));
                }
            }
        }

        static int Mathf10(float value) => (int)(value * 10.0f);
    }
}
