using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.WorldGen;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WeatherServiceEditModeTests
    {
        const float Midday = 0.25f;
        const float Midnight = 0.75f;
        // The tallest natural terrain column: SurvivalBiomeResolver peaks relief at SeaLevel + 48.
        const int PeakAltitude = WorldConstants.SeaLevel + 48;

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

            EnvironmentState sea = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState high = service.Evaluate(Midday, PeakAltitude, SurvivalBiomeResolver.MeadowBiomeIndex);

            Assert.That(sea.Temperature, Is.GreaterThan(high.Temperature));
        }

        [Test]
        public void TemperatureIsLowerAtNightThanDay()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState day = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState night = service.Evaluate(Midnight, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);

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
                EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
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
                EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
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
            EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
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

        // ── Biome-based temperature model (voxel_world_environment_effects.md §6.1/§6.2) ──────

        [Test]
        public void BiomeBaseTemperaturesMatchTheRuleset()
        {
            // §6.1: place sets the base temperature, not the weather state. Clear sky, sea level,
            // midday isolates the base with every §6.2 modifier at zero.
            var expected = new (int biomeIndex, float baseTemperature, string name)[]
            {
                (SurvivalBiomeResolver.DunesBiomeIndex,      34f, "dunes"),
                (SurvivalBiomeResolver.DrybrushBiomeIndex,   26f, "drybrush"),
                (SurvivalBiomeResolver.MeadowBiomeIndex,     18f, "meadow"),
                (SurvivalBiomeResolver.WetlandBiomeIndex,    16f, "wetland"),
                (SurvivalBiomeResolver.PinewildBiomeIndex,   10f, "pinewild"),
                (SurvivalBiomeResolver.HighlandsBiomeIndex,   8f, "highlands"),
                (SurvivalBiomeResolver.TundraBiomeIndex,     -8f, "tundra"),
            };

            var service = new WeatherService(seed: 1, WeatherState.Clear);

            foreach ((int biomeIndex, float baseTemperature, string name) in expected)
            {
                EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, biomeIndex);
                Assert.That(env.Temperature, Is.EqualTo(baseTemperature).Within(0.001f),
                    $"The {name} base temperature is what makes that biome feel like itself; §6.1 pins it.");
            }
        }

        [Test]
        public void UnknownBiomeFallsBackToTheTemperateBase()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState unknown = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.AnyBiomeIndex);
            EnvironmentState meadow = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);

            Assert.That(unknown.Temperature, Is.EqualTo(meadow.Temperature).Within(0.001f),
                "Positionless queries (global sky lighting) and biome-less creative presets must land on the temperate default, not a random extreme.");
        }

        [Test]
        public void AltitudeLapseRateIsFifteenHundredthsOfADegreePerBlock()
        {
            // 0.15 C/block over the 0–48 block playable band is -7.2 C at the tallest natural peak.
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            foreach (int biomeIndex in new[] { SurvivalBiomeResolver.MeadowBiomeIndex, SurvivalBiomeResolver.HighlandsBiomeIndex })
            {
                EnvironmentState sea = service.Evaluate(Midday, WorldConstants.SeaLevel, biomeIndex);
                EnvironmentState peak = service.Evaluate(Midday, PeakAltitude, biomeIndex);

                Assert.That(peak.Temperature, Is.EqualTo(sea.Temperature - 7.2f).Within(0.001f),
                    "Elevation has to be worth climbing away from; a weaker lapse rate makes altitude thermally irrelevant next to the biome spread.");
            }
        }

        [Test]
        public void AltitudeBelowSeaLevelDoesNotWarmTheWorld()
        {
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState sea = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState deep = service.Evaluate(Midday, WorldConstants.SeaLevel - 40, SurvivalBiomeResolver.MeadowBiomeIndex);

            Assert.That(deep.Temperature, Is.EqualTo(sea.Temperature).Within(0.001f),
                "The lapse rate is clamped at sea level; caves must not become tropical the deeper you dig.");
        }

        [Test]
        public void HighlandPeaksAreColdEnoughToBiteInClearDaylight()
        {
            // The reason the lapse rate was raised: highlands high ground should cross
            // SurvivalVitals' cold-exposure threshold in daylight and freeze outright at night.
            var service = new WeatherService(seed: 1, WeatherState.Clear);

            EnvironmentState day = service.Evaluate(Midday, PeakAltitude, SurvivalBiomeResolver.HighlandsBiomeIndex);
            EnvironmentState night = service.Evaluate(Midnight, PeakAltitude, SurvivalBiomeResolver.HighlandsBiomeIndex);

            Assert.That(day.Temperature, Is.LessThan(2.0f),
                "Highland peaks in clear daylight must sit under the 2.0 C cold-exposure threshold, or altitude never threatens the player.");
            Assert.That(night.Temperature, Is.LessThan(0f),
                "The same peak at night must be below freezing, so rain falling there turns to snow.");
        }

        [Test]
        public void RainAndSnowModifiersScaleWithPrecipitationIntensity()
        {
            // §6.2: rain is -2 * intensity, snow is -4 * intensity, applied on top of the
            // precipitation-free temperature.
            var thunderstorm = new WeatherService(seed: 1, WeatherState.Thunderstorm); // rain, intensity 1.0
            var blizzard = new WeatherService(seed: 1, WeatherState.Blizzard);         // snow, intensity 1.0

            EnvironmentState wet = thunderstorm.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState frozen = blizzard.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.DunesBiomeIndex);

            Assert.That(wet.Temperature, Is.EqualTo(18f - 2f).Within(0.001f),
                "A full-intensity rain storm cools a meadow by exactly the §6.2 rain modifier.");
            Assert.That(frozen.Temperature, Is.EqualTo(34f - 4f).Within(0.001f),
                "A full-intensity blizzard cools by the §6.2 snow modifier, even over the dunes.");
        }

        // ── Precipitation kind is derived per location, never a weather-state change ──────────

        [Test]
        public void RainConvertsToSnowBelowFreezing()
        {
            foreach (WeatherState state in new[] { WeatherState.LightRain, WeatherState.HeavyRain, WeatherState.Thunderstorm })
            {
                var service = new WeatherService(seed: 1, state);

                EnvironmentState cold = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.TundraBiomeIndex);
                EnvironmentState warm = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.DunesBiomeIndex);

                Assert.That(cold.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                    $"{state} over the freezing tundra must fall as snow (§6.3), not rain.");
                Assert.That(warm.Precipitation, Is.EqualTo(PrecipitationKind.Rain),
                    $"{state} over the warm dunes must stay rain — the same synced weather state, a different place.");
            }
        }

        [Test]
        public void RainConvertsToSnowOnHighGroundInTheSameBiome()
        {
            // The per-location rule has to bite on altitude too, not just biome: one storm, rain in
            // the valley and snow on the peak of the same pinewild forest.
            var service = new WeatherService(seed: 1, WeatherState.HeavyRain);

            EnvironmentState valley = service.Evaluate(Midnight, WorldConstants.SeaLevel, SurvivalBiomeResolver.PinewildBiomeIndex);
            EnvironmentState peak = service.Evaluate(Midnight, PeakAltitude, SurvivalBiomeResolver.PinewildBiomeIndex);

            Assert.That(valley.Precipitation, Is.EqualTo(PrecipitationKind.Rain),
                "A pinewild valley at night is still above freezing, so the storm falls as rain.");
            Assert.That(peak.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                "The same storm on the peak of the same forest must fall as snow.");
        }

        [Test]
        public void SnowStatesStaySnowEvenInTheHottestBiome()
        {
            foreach (WeatherState state in new[] { WeatherState.LightSnow, WeatherState.HeavySnow, WeatherState.Blizzard })
            {
                var service = new WeatherService(seed: 1, state);
                EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.DunesBiomeIndex);

                Assert.That(env.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                    $"{state} is an inherently cold state; it must not silently become rain over a 34 C biome.");
            }
        }

        [Test]
        public void NonPrecipitatingStatesYieldNoPrecipitation()
        {
            var states = new[] { WeatherState.Clear, WeatherState.PartlyCloudy, WeatherState.Overcast, WeatherState.Fog };

            foreach (WeatherState state in states)
            {
                var service = new WeatherService(seed: 1, state);
                EnvironmentState env = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.TundraBiomeIndex);

                Assert.That(env.Precipitation, Is.EqualTo(PrecipitationKind.None),
                    $"{state} drops nothing; a freezing biome must not conjure snowfall out of a clear sky.");
            }
        }

        [Test]
        public void PrecipitationDecisionIsStableAcrossRepeatedQueries()
        {
            // The two-pass split exists so the precipitation modifier can never feed back into the
            // rain/snow test. Evaluate() is a pure function of its arguments, so re-querying a
            // location whose temperature sits just above freezing must never flip the answer.
            var service = new WeatherService(seed: 1, WeatherState.LightRain);

            EnvironmentState first = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.PinewildBiomeIndex);

            for (int i = 0; i < 8; i++)
            {
                EnvironmentState repeat = service.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.PinewildBiomeIndex);
                Assert.That(repeat.Precipitation, Is.EqualTo(first.Precipitation),
                    $"Precipitation kind oscillated on query {i}; the modifier must stay out of the rain/snow decision.");
                Assert.That(repeat.Temperature, Is.EqualTo(first.Temperature).Within(0.0001f),
                    $"Temperature drifted on query {i}; Evaluate must be a pure function of its arguments.");
            }
        }

        // ── One definition of night, and the thresholds that hang off it ─────────────────────

        [Test]
        public void NightIsTheWorldClockDefinitionNotASecondOpinion()
        {
            // WeatherService used to carry its own `t > 0.6 || t < 0.1` night test, which disagreed
            // with WorldTimeClock.IsDay (0.05 / 0.55) over [0.05, 0.10) and [0.55, 0.60]. While
            // temperature was weather-derived it never sat near a threshold and the gap was
            // invisible; now the -5 °C modifier and SurvivalVitals' night-cold threshold both hang
            // off "is it night", so two disagreeing predicates offset cold onset from visible dusk.
            float[] samples = { 0.0f, 0.04f, 0.05f, 0.09f, 0.10f, 0.25f, 0.54f, 0.55f, 0.59f, 0.60f, 0.75f, 0.99f };
            foreach (float t in samples)
            {
                Assert.That(WorldConstants.IsNight(t), Is.EqualTo(!WorldTimeClock.IsDay(t)),
                    $"The temperature night modifier and the world clock must agree about t={t}.");
            }

            // And the modifier must switch exactly on that boundary rather than merely near it.
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            float lastDay = service
                .Evaluate(WorldConstants.NightStartNormalizedTime - 0.001f, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex)
                .Temperature;
            float firstNight = service
                .Evaluate(WorldConstants.NightStartNormalizedTime, WorldConstants.SeaLevel, SurvivalBiomeResolver.MeadowBiomeIndex)
                .Temperature;

            Assert.That(lastDay - firstNight, Is.EqualTo(-WeatherService.NightTemperatureModifierC).Within(0.001f),
                "The night modifier must land on WorldConstants.NightStartNormalizedTime, not a nearby value of its own.");
        }

        [Test]
        public void TemperateForestOnAClearNightIsNotInColdPressure()
        {
            // Pinned at representative forest-floor altitudes, not at the sea-level boundary. A
            // pinewild clear night is 10 - 5 - 0.15 °C per block (§6.1/§6.2), so a threshold that
            // only spares sea level taxes effectively the whole biome: the old 5.0 °C threshold
            // was met exactly at +0 and crossed from +1 up, chipping 20 HP per clear night on
            // open ground anywhere in a starter forest. Whether that biome bleeds every night is
            // a design decision, so it is pinned across altitudes players actually stand on
            // rather than left to a float boundary.
            var service = new WeatherService(seed: 1, WeatherState.Clear);
            foreach (int height in new[] { 1, 10 })
            {
                EnvironmentState night = service.Evaluate(
                    Midnight, WorldConstants.SeaLevel + height, SurvivalBiomeResolver.PinewildBiomeIndex);
                var exposure = new SurvivalEnvironmentExposure(
                    night.Temperature,
                    skyExposed: true,
                    isNight: true,
                    night.PrecipitationIntensity,
                    night.StormIntensity);

                Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(exposure), Is.EqualTo(0),
                    $"A clear night on the pinewild floor (+{height} blocks) must be safe; nightly chip damage in a starter biome is not the intent.");
            }

            // The forest is not unconditionally safe: high pinewild ground still freezes. At +14
            // blocks a clear night sits at 10 - 5 - 0.15 × 14 = 2.9 °C, under the threshold, so
            // altitude — not the night itself — is what turns the biome hostile.
            EnvironmentState highNight = service.Evaluate(
                Midnight, WorldConstants.SeaLevel + 14, SurvivalBiomeResolver.PinewildBiomeIndex);
            var highExposure = new SurvivalEnvironmentExposure(
                highNight.Temperature,
                skyExposed: true,
                isNight: true,
                highNight.PrecipitationIntensity,
                highNight.StormIntensity);

            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(highExposure), Is.EqualTo(1),
                "High pinewild ground on a clear night is genuinely cold; the forest floor's safety must not blanket the peaks.");
        }

        [Test]
        public void FreezingBiomeInCalmDaylightAppliesNoColdPressure()
        {
            // Tundra is a selectable starting biome and the spawn search deliberately lands on one.
            // Its -8 °C base must not mean a new world starts draining health with nothing the
            // player can do about it: cold bites at night or under falling weather, per §12.5.
            var clear = new WeatherService(seed: 1, WeatherState.Clear);
            EnvironmentState day = clear.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.TundraBiomeIndex);

            Assert.That(day.Temperature, Is.LessThan(0f), "The tundra is still genuinely freezing.");

            var calmDaylight = new SurvivalEnvironmentExposure(
                day.Temperature, skyExposed: true, isNight: false, day.PrecipitationIntensity, day.StormIntensity);
            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(calmDaylight), Is.EqualTo(0),
                "A tundra spawn in clear daylight must be survivable long enough to build shelter.");

            // The same spot under snowfall, and the same spot at night, both bite.
            var snowing = new WeatherService(seed: 1, WeatherState.LightSnow);
            EnvironmentState snowfall = snowing.Evaluate(Midday, WorldConstants.SeaLevel, SurvivalBiomeResolver.TundraBiomeIndex);
            var snowExposure = new SurvivalEnvironmentExposure(
                snowfall.Temperature, skyExposed: true, isNight: false, snowfall.PrecipitationIntensity, snowfall.StormIntensity);

            EnvironmentState tundraNight = clear.Evaluate(Midnight, WorldConstants.SeaLevel, SurvivalBiomeResolver.TundraBiomeIndex);
            var nightExposure = new SurvivalEnvironmentExposure(
                tundraNight.Temperature, skyExposed: true, isNight: true, tundraNight.PrecipitationIntensity, tundraNight.StormIntensity);

            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(snowExposure), Is.GreaterThan(0),
                "Snowfall on the tundra is exactly the exposure the ruleset asks for.");
            Assert.That(SurvivalVitals.ComputeEnvironmentPressureSources(nightExposure), Is.GreaterThan(0),
                "A tundra night is still dangerous; only calm daylight is not.");
        }

        [Test]
        public void PublishedTemperatureMayDisagreeWithPrecipitationKindByDesign()
        {
            // Rain heavy enough to push its own location below freezing STAYS rain: the kind is
            // decided against the pre-modifier temperature so it cannot oscillate. That makes
            // EnvironmentState.Precipitation authoritative and Temperature unusable for
            // re-deriving it. Pinned so nobody later "fixes" the pair into agreement and
            // reintroduces the feedback loop the two-pass split exists to prevent.
            var service = new WeatherService(seed: 1, WeatherState.HeavyRain);
            EnvironmentState env = service.Evaluate(Midnight, WorldConstants.SeaLevel + 26, SurvivalBiomeResolver.PinewildBiomeIndex);

            Assert.That(env.Precipitation, Is.EqualTo(PrecipitationKind.Rain),
                "The pre-modifier temperature was above freezing, so this location is raining.");
            Assert.That(env.Temperature, Is.LessThanOrEqualTo(WeatherService.FreezingTemperatureC),
                "...while the published temperature, which carries the rain modifier, is at or below freezing. This pair is allowed to disagree.");
        }

        // ── Determinism: the Markov chain is untouched by the biome/precipitation work ────────

        [Test]
        public void RestoredSyncStateProducesIdenticalEnvironmentForTheSameQuery()
        {
            // Snowfall derived from local temperature must cost zero extra sync traffic: a client
            // holding only the host's WeatherSyncState has to reach the same answer for the same
            // place, or two peers standing together would see different weather.
            var host = new WeatherService(seed: 31337, WeatherState.Clear);

            // Drive the host to a state that is actually dropping something. A dry sky makes every
            // assertion below vacuous — both peers would trivially agree on None and 0 — which is
            // precisely the failure this test exists to catch, so the fixture guard is asserted.
            for (int i = 0; i < 200 && !WeatherService.IsPrecipitating(host.CurrentState); i++)
                host.Tick(2500);

            Assert.That(WeatherService.IsPrecipitating(host.CurrentState), Is.True,
                "Fixture guard: this test cannot detect a rain/snow disagreement under a dry sky.");

            var client = new WeatherService(seed: 31337, WeatherState.Clear);
            client.RestoreState(host.CurrentState, host.TicksInCurrentState, host.RngState);

            // A freezing highland peak, so the answer is a real Snow rather than an absent one.
            EnvironmentState hostEnv = host.Evaluate(Midnight, PeakAltitude, SurvivalBiomeResolver.HighlandsBiomeIndex);
            EnvironmentState clientEnv = client.Evaluate(Midnight, PeakAltitude, SurvivalBiomeResolver.HighlandsBiomeIndex);

            Assert.That(hostEnv.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                "Fixture guard: a sub-zero peak under any precipitating state must resolve to snow.");
            Assert.That(hostEnv.PrecipitationIntensity, Is.GreaterThan(0f),
                "Fixture guard: the intensity comparison below must compare something to something.");

            Assert.That(clientEnv.Weather, Is.EqualTo(hostEnv.Weather), "Restored peers must agree on the weather state.");
            Assert.That(clientEnv.Temperature, Is.EqualTo(hostEnv.Temperature).Within(0f), "Restored peers must agree on local temperature.");
            Assert.That(clientEnv.Precipitation, Is.EqualTo(hostEnv.Precipitation), "Restored peers must agree on rain vs. snow at the same spot.");
            Assert.That(clientEnv.PrecipitationIntensity, Is.EqualTo(hostEnv.PrecipitationIntensity).Within(0f), "Restored peers must agree on precipitation intensity.");
        }

        [Test]
        public void MarkovTransitionSequenceIsUnchangedByTheTemperatureRework()
        {
            // Regression pin. Biome temperatures and per-location rain/snow are derived on read;
            // they must not perturb the transition table, the minimum durations, or the RNG stream.
            // If this sequence ever changes, saved worlds and in-flight sync snapshots have shifted
            // onto a different weather timeline.
            var expected = new[]
            {
                WeatherState.Clear,
                WeatherState.Clear,
                WeatherState.PartlyCloudy,
                WeatherState.PartlyCloudy,
                WeatherState.LightRain,
                WeatherState.LightRain,
                WeatherState.HeavyRain,
                WeatherState.LightRain,
                WeatherState.Fog,
                WeatherState.Fog,
                WeatherState.PartlyCloudy,
                WeatherState.PartlyCloudy,
                WeatherState.Overcast,
                WeatherState.HeavyRain,
                WeatherState.HeavySnow,
                WeatherState.LightSnow,
            };

            var service = new WeatherService(seed: 11, WeatherState.Clear);
            var actual = new List<WeatherState>(expected.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                service.Tick(2000);
                actual.Add(service.CurrentState);
            }

            Assert.That(actual, Is.EqualTo(expected),
                "The weather Markov chain must be byte-identical to its pre-rework behaviour; sync payloads and saves depend on it.");
            Assert.That(service.TicksInCurrentState, Is.EqualTo(600),
                "Accumulated ticks are part of the sync payload and must land where they always did.");
            Assert.That(service.RngState, Is.EqualTo(3401379233u),
                "The RNG position travels in WeatherSyncState; drifting it would desync every late joiner.");
        }
    }
}
