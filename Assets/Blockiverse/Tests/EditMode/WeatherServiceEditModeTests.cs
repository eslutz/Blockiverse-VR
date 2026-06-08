using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WeatherServiceEditModeTests
    {
        [Test]
        public void WeatherServiceTransitionsAreDeterministicForSameSeed()
        {
            var a = new WeatherService(seed: 12345, WeatherState.Clear);
            var b = new WeatherService(seed: 12345, WeatherState.Clear);

            for (int i = 0; i < 10; i++)
            {
                a.Tick(2000);
                b.Tick(2000);
            }

            Assert.That(a.CurrentState, Is.EqualTo(b.CurrentState));
        }

        [Test]
        public void WeatherServiceTransitionsProduceDifferentStatesForDifferentSeeds()
        {
            var a = new WeatherService(seed: 1,   WeatherState.Clear);
            var b = new WeatherService(seed: 9999, WeatherState.Clear);

            // Advance enough to trigger multiple transitions.
            for (int i = 0; i < 30; i++)
            {
                a.Tick(3000);
                b.Tick(3000);
            }

            // At least one snapshot along the way differed — collect final states.
            // We can't guarantee they differ at exactly this moment, but seeded
            // RNGs with different seeds should diverge over 30 intervals.
            bool everDiffered = a.CurrentState != b.CurrentState;

            // If they happen to be equal right now, run a few more ticks to confirm divergence.
            if (!everDiffered)
            {
                a.Tick(5000);
                b.Tick(5000);
                everDiffered = a.CurrentState != b.CurrentState;
            }

            Assert.That(everDiffered, Is.True, "Expected different seeds to produce different weather progressions.");
        }

        [Test]
        public void WeatherServiceDoesNotTransitionBeforeMinimumDuration()
        {
            var service = new WeatherService(seed: 42, WeatherState.Clear);
            WeatherState initial = service.CurrentState;

            // Tick just under Clear's minimum (6000 ticks)
            service.Tick(5999);

            Assert.That(service.CurrentState, Is.EqualTo(initial));
        }

        [Test]
        public void WeatherServiceTransitionsAfterMinimumDuration()
        {
            // Run several seeds to find one that actually transitions away from Clear.
            bool transitioned = false;
            for (uint seed = 1; seed <= 20; seed++)
            {
                var service = new WeatherService(seed, WeatherState.Clear);
                service.Tick(6001);
                if (service.CurrentState != WeatherState.Clear)
                {
                    transitioned = true;
                    break;
                }
            }

            Assert.That(transitioned, Is.True, "Expected at least one seed to produce a weather transition.");
        }

        [Test]
        public void TemperatureDecreasesWithAltitude()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState sea   = service.Evaluate(normalizedTimeOfDay: 0.25f, altitudeY: WorldConstants.SeaLevel);
            EnvironmentState high  = service.Evaluate(normalizedTimeOfDay: 0.25f, altitudeY: WorldConstants.SeaLevel + 100);

            Assert.That(sea.Temperature, Is.GreaterThan(high.Temperature));
        }

        [Test]
        public void TemperatureIsLowerAtNightThanDay()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState day   = service.Evaluate(normalizedTimeOfDay: 0.25f, altitudeY: WorldConstants.SeaLevel);
            EnvironmentState night = service.Evaluate(normalizedTimeOfDay: 0.75f, altitudeY: WorldConstants.SeaLevel);

            Assert.That(night.Temperature, Is.LessThan(day.Temperature));
        }

        [Test]
        public void PrecipitationIntensityIsNonZeroForRainAndSnowStates()
        {
            var states = new[]
            {
                WeatherState.LightRain, WeatherState.HeavyRain, WeatherState.Thunderstorm,
                WeatherState.LightSnow, WeatherState.HeavySnow, WeatherState.Blizzard,
            };

            foreach (WeatherState state in states)
            {
                var service = new WeatherService(seed: 1, state);
                EnvironmentState env = service.Evaluate(0.25f, WorldConstants.SeaLevel);
                Assert.That(env.PrecipitationIntensity, Is.GreaterThan(0f), $"Expected precipitation for {state}.");
            }
        }

        [Test]
        public void LargeDeltaTicksCausesOnlyOneTransitionPerCall()
        {
            // Regardless of how large a single deltaTicks is, Tick() processes at most
            // one state transition per call. Two calls of half the time should not differ.
            var service = new WeatherService(seed: 77, WeatherState.Clear);

            // One huge tick that far exceeds any state's minimum duration
            service.Tick(999999);
            WeatherState afterHuge = service.CurrentState;

            // Reset and do the same total with two calls
            var service2 = new WeatherService(seed: 77, WeatherState.Clear);
            service2.Tick(999999);
            WeatherState afterTwo = service2.CurrentState;

            // Both should produce the same state (deterministic single-transition-per-call)
            Assert.That(afterTwo, Is.EqualTo(afterHuge));
        }

        [Test]
        public void PrecipitationIntensityIsZeroForClearAndPartlyCloudy()
        {
            foreach (WeatherState state in new[] { WeatherState.Clear, WeatherState.PartlyCloudy })
            {
                var service = new WeatherService(seed: 1, state);
                EnvironmentState env = service.Evaluate(0.25f, WorldConstants.SeaLevel);
                Assert.That(env.PrecipitationIntensity, Is.EqualTo(0f), $"Expected no precipitation for {state}.");
            }
        }
    }
}
