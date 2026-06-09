using Blockiverse.WorldGen;
using NUnit.Framework;
using System;

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
        public void LargeDeltaTicksAdvancesMultipleTransitions()
        {
            // A single Tick() with a large delta must advance through multiple state
            // transitions, not just one.
            var service = new WeatherService(seed: 77, WeatherState.Clear);

            // 999999 ticks far exceeds any single state's minimum duration (max 6000),
            // so the service must advance through many states.
            service.Tick(999999);

            // The result should match two separate services that together consumed
            // the same total ticks — deterministic regardless of how many batches.
            var service2 = new WeatherService(seed: 77, WeatherState.Clear);
            service2.Tick(500000);
            service2.Tick(499999);

            Assert.That(service.CurrentState, Is.EqualTo(service2.CurrentState));
        }

        [Test]
        public void LargeDeltaTicksInOneCallMatchesSameTotalInManySmallCalls()
        {
            // Tick() with a large delta must be equivalent to many small Tick() calls
            // totalling the same amount — confirming all intervals are consumed.
            var single = new WeatherService(seed: 55, WeatherState.Clear);
            var batched = new WeatherService(seed: 55, WeatherState.Clear);

            single.Tick(30000);

            for (int i = 0; i < 30; i++)
                batched.Tick(1000);

            Assert.That(single.CurrentState, Is.EqualTo(batched.CurrentState));
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

        // ── M4-C: Environment effects plumbing ───────────────────────────────

        [Test]
        public void CloudCoverageIsHighForThunderstorm()
        {
            var service = new WeatherService(seed: 1, WeatherState.Thunderstorm);
            Assert.That(service.CloudCoverage, Is.EqualTo(1.0f));
        }

        [Test]
        public void CloudCoverageIsLowForClear()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            Assert.That(service.CloudCoverage, Is.LessThan(0.25f));
        }

        [Test]
        public void EnvironmentStateIncludesCloudCoverage()
        {
            var service = new WeatherService(seed: 1, WeatherState.HeavyRain);
            EnvironmentState env = service.Evaluate(0.25f, WorldConstants.SeaLevel);
            Assert.That(env.CloudCoverage, Is.GreaterThan(0.5f));
        }

        [Test]
        public void GetBaseSkyLightReturnsFifteenDuringDay()
        {
            Assert.That(EnvironmentLightComputer.GetBaseSkyLight(0.25f), Is.EqualTo(15));
        }

        [Test]
        public void GetBaseSkyLightReturnsLowValueAtNight()
        {
            int nightLight = EnvironmentLightComputer.GetBaseSkyLight(0.70f, moonPhaseIndex: 0);
            Assert.That(nightLight, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(4));
        }

        [Test]
        public void GetAmbientLightIsReducedByWeatherPenalty()
        {
            // Full moon, midday, thunderstorm
            int withStorm = EnvironmentLightComputer.GetAmbientLight(
                normalizedTime: 0.25f, moonPhaseIndex: 4,
                cloudCoverage: 1.0f, precipitationIntensity: 0.9f, stormIntensity: 1.0f);
            // Same time, clear sky
            int clearSky = EnvironmentLightComputer.GetAmbientLight(
                normalizedTime: 0.25f, moonPhaseIndex: 4,
                cloudCoverage: 0.1f, precipitationIntensity: 0f, stormIntensity: 0f);

            Assert.That(withStorm, Is.LessThan(clearSky));
        }

        // ── M8-4: Environment snapshot sync ──────────────────────────────────

        [Test]
        public void RestoreStatePreservesWeatherStateTicksAndRng()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            service.Tick(3000); // accumulate some ticks

            service.RestoreState(WeatherState.Thunderstorm, 800, rng: 0xABCDEF01u);

            Assert.That(service.CurrentState, Is.EqualTo(WeatherState.Thunderstorm));
            Assert.That(service.TicksInCurrentState, Is.EqualTo(800));
            Assert.That(service.RngState, Is.EqualTo(0xABCDEF01u));
        }

        [Test]
        public void RestoreStateClampsNegativeTicksToZero()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            service.RestoreState(WeatherState.Fog, -500, rng: 12345u);
            Assert.That(service.TicksInCurrentState, Is.EqualTo(0));
        }

        [Test]
        public void RestoreStateNormalizesZeroRngToValidState()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            service.RestoreState(WeatherState.Overcast, 0, rng: 0u);
            // A zero xorshift state is degenerate; it must be normalized to a non-zero value.
            Assert.That(service.RngState, Is.Not.EqualTo(0u));
        }

        [Test]
        public void TicksInCurrentStateMatchesAccumulatedTicks()
        {
            var service = new WeatherService(seed: 99, WeatherState.Clear);
            service.Tick(2000);
            // Clear min duration is 6000; no transition yet, so accumulated = 2000.
            Assert.That(service.TicksInCurrentState, Is.EqualTo(2000));
        }

        [Test]
        public void RestoringFullStateKeepsTwoServicesInLockstep()
        {
            // Simulates a host that has run for a while and a client that joins and restores the
            // host's full weather snapshot (state + ticks + RNG). After restore, ticking both by the
            // same total must produce identical weather sequences — no divergence.
            var host = new WeatherService(seed: 7777, WeatherState.Clear);
            for (int i = 0; i < 25; i++)
                host.Tick(3000);

            var client = new WeatherService(seed: 1, WeatherState.Clear); // different seed on purpose
            client.RestoreState(host.CurrentState, host.TicksInCurrentState, host.RngState);

            for (int i = 0; i < 40; i++)
            {
                host.Tick(2500);
                client.Tick(2500);
                Assert.That(client.CurrentState, Is.EqualTo(host.CurrentState),
                    $"Weather diverged at iteration {i}; RNG sync should keep them locked.");
            }
        }
    }
}
