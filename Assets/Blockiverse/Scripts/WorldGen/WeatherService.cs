using System;

namespace Blockiverse.WorldGen
{
    // What is actually falling at one queried location (voxel_world_environment_effects.md §6.3).
    // This is a LOCAL DERIVATION, never a weather-state change: it is recomputed on every Evaluate
    // from the synced weather state plus the local temperature, is never stored on the service,
    // and never appears in WeatherSyncState or a save. Two peers standing in the same spot derive
    // the same answer from the same synced state with zero extra network traffic.
    public enum PrecipitationKind
    {
        None,
        Rain,
        Snow,
    }

    public struct EnvironmentState
    {
        public WeatherState Weather;
        public float Temperature;
        public float PrecipitationIntensity;
        public float FogDensity;
        public float StormIntensity;
        public float CloudCoverage;
        // Rain vs. snow at the queried location — see PrecipitationKind. Decided against the
        // PRE-modifier temperature, so a published Temperature at or below freezing can
        // legitimately accompany Rain: Precipitation is authoritative and the pair is allowed to
        // disagree by design (see Evaluate's two-pass note; pinned by
        // WeatherServiceEditModeTests.PublishedTemperatureMayDisagreeWithPrecipitationKindByDesign).
        public PrecipitationKind Precipitation;
    }

    public sealed class WeatherService
    {
        // §6.2 temperature modifiers. The lapse rate is 0.15 °C per block above sea level: the
        // playable altitude band is only 0–48 blocks (SurvivalBiomeResolver peaks terrain at
        // SeaLevel + 48, ~y=112 under WorldMaxY 127), so 0.15 yields -7.2 °C at the tallest peak —
        // comparable to the night modifier (-5) and enough to push Highlands (biome base 8) below
        // SurvivalVitals.ColdExposureTemperatureThresholdC (2.0) on high ground in clear daylight,
        // and below freezing there at night. The previous 0.05 produced only -2.4 °C across the
        // entire world, making elevation thermally irrelevant next to the 42 °C biome spread
        // (Tundra -8 → Dunes 34).
        public const float AltitudeLapseRatePerBlockC = 0.15f;
        public const float NightTemperatureModifierC = -5f;
        public const float RainTemperatureModifierC = -2f;
        public const float SnowTemperatureModifierC = -4f;
        public const float FreezingTemperatureC = 0f;

        // Minimum ticks before a transition can occur, per state.
        static readonly int[] MinDurationTicks =
        {
            6000,  // Clear
            4000,  // PartlyCloudy
            3000,  // Overcast
            2400,  // LightRain
            1800,  // HeavyRain
            1200,  // Thunderstorm
            3600,  // LightSnow
            2400,  // HeavySnow
            1800,  // Blizzard
            2400,  // Fog
        };

        // How long a state runs before its next transition is rolled. Exposed so presentation can
        // place "where in this weather am I" on a 0..1 arc -- lightning intensity reads it to make
        // a storm build and taper rather than striking at one flat rate.
        public static int MinimumStateDurationTicks(WeatherState state)
        {
            int index = (int)state;
            return index >= 0 && index < MinDurationTicks.Length ? MinDurationTicks[index] : 0;
        }

        // Transition weights: for each current state, the probability weights for each next state.
        // Row = current, Column = next. Zero means impossible transition.
        static readonly int[,] TransitionWeights =
        {
            //Clr  PCl  Ovr  LRn  HRn  Thr  LSn  HSn  Blz  Fog
            { 40,  30,  15,   5,   2,   0,   3,   1,   0,   4 }, // Clear
            { 30,  35,  20,   8,   3,   1,   2,   0,   0,   1 }, // PartlyCloudy
            { 10,  20,  25,  20,   8,   4,   5,   3,   1,   4 }, // Overcast
            {  5,  10,  20,  30,  20,   5,   3,   2,   0,   5 }, // LightRain
            {  2,   5,  15,  25,  30,  12,   2,   4,   1,   4 }, // HeavyRain
            {  3,   5,  20,  25,  25,  10,   2,   3,   2,   5 }, // Thunderstorm
            { 10,  15,  20,   5,   2,   0,  25,  15,   5,   3 }, // LightSnow
            {  3,   5,  15,   5,   3,   1,  25,  28,  12,   3 }, // HeavySnow
            {  2,   3,  10,   5,   3,   2,  20,  30,  20,   5 }, // Blizzard
            { 15,  20,  25,  15,   5,   2,   5,   3,   0,  10 }, // Fog
        };

        uint rngState;
        WeatherState currentState;
        int ticksInCurrentState;

        public WeatherService(uint seed, WeatherState initialState = WeatherState.Clear)
        {
            rngState = seed == 0 ? 1u : seed;
            currentState = initialState;
            ticksInCurrentState = 0;
        }

        public WeatherState CurrentState => currentState;

        // Ticks the service has accumulated in the current state — used for environment sync snapshots.
        public int TicksInCurrentState => ticksInCurrentState;

        // Current xorshift RNG position — part of the environment sync snapshot so a late-joining
        // client resumes the host's exact transition stream and stays in deterministic lockstep.
        public uint RngState => rngState;

        // Restore weather state received from a host snapshot (multiplayer late-join / reconnect).
        // Restoring rngState as well keeps client and host weather sequences identical going forward.
        public void RestoreState(WeatherState state, int ticks, uint rng)
        {
            currentState = state;
            ticksInCurrentState = Math.Max(0, ticks);
            rngState = rng == 0 ? 1u : rng;
        }

        public float CloudCoverage => TargetCloudCoverage(currentState);

        // The runtime weather→light penalty lives in EnvironmentLightComputer.GetAmbientLight, which the
        // lighting controller consumes via EnvironmentLightingSolver; CloudCoverage feeds that path.

        // Evaluates the environment at one location (voxel_world_environment_effects.md §6.2).
        //
        // Temperature is biome-led, not weather-led: the base comes from the column's biome and the
        // weather contributes only through the typed precipitation modifiers below. `biomeIndex` is
        // a SurvivalBiomeResolver biome index — the int idiom that keeps the internal TerrainBiome
        // enum out of other assemblies. SurvivalBiomeResolver.AnyBiomeIndex (or any out-of-range
        // value) means "unknown" and falls back to Meadow's temperate base, which is what the
        // positionless global sky query and the biome-less creative presets pass.
        //
        // TWO PASSES, deliberately. Rain-vs-snow depends on temperature and the precipitation
        // modifier depends on rain-vs-snow, so the circularity is broken explicitly: pass 1 builds
        // the temperature WITHOUT any precipitation term, pass 2 decides the precipitation kind
        // from that preliminary value and only then applies its modifier. The modifier is excluded
        // from the rain/snow test on purpose — folding it back in would let a location a fraction
        // of a degree above freezing flip to snow, drop -4 °C, and then flip back, oscillating
        // between the two answers on successive queries. Only the published Temperature carries it.
        //
        // §6.2 also lists a preDawnModifier keyed off a PRE_DAWN day phase. It is deliberately not
        // implemented: WorldTimeClock exposes normalized time and IsDay only — there is no day-phase
        // enum to key it off. Add it here if the clock ever grows one.
        public EnvironmentState Evaluate(float normalizedTimeOfDay, int altitudeY, int biomeIndex)
        {
            float biomeBase = BiomeBaseTemperature(biomeIndex);
            float altitudeModifier = -AltitudeLapseRatePerBlockC * Math.Max(0, altitudeY - WorldConstants.SeaLevel);
            float nightModifier = IsNight(normalizedTimeOfDay) ? NightTemperatureModifierC : 0f;

            // Pass 1: the temperature the rain/snow decision is made against.
            float preliminaryTemperature = biomeBase + altitudeModifier + nightModifier;

            // Pass 2: kind first, then its modifier.
            float intensity = PrecipitationIntensityFor(currentState);
            PrecipitationKind precipitation = ResolvePrecipitationKind(currentState, preliminaryTemperature);
            float precipitationModifier = precipitation switch
            {
                PrecipitationKind.Rain => RainTemperatureModifierC * intensity,
                PrecipitationKind.Snow => SnowTemperatureModifierC * intensity,
                _                      => 0f,
            };

            return new EnvironmentState
            {
                Weather                = currentState,
                Temperature            = preliminaryTemperature + precipitationModifier,
                PrecipitationIntensity = intensity,
                Precipitation          = precipitation,
                FogDensity             = FogDensityFor(currentState),
                StormIntensity         = StormIntensityFor(currentState),
                CloudCoverage          = CloudCoverage,
            };
        }

        // Rain vs. snow at one location (§6.3, and §8.2's temperature precipitation conversion).
        // Purely derived — it reads the weather state but never changes it, so the Markov chain,
        // its transition weights, minimum durations, RNG stream, and the WeatherSyncState payload
        // are all untouched by snowfall appearing in a cold place.
        public static PrecipitationKind ResolvePrecipitationKind(WeatherState state, float temperature)
        {
            switch (state)
            {
                // Inherently cold states always fall as snow regardless of local temperature — a
                // blizzard blowing over the dunes is still a blizzard.
                case WeatherState.LightSnow:
                case WeatherState.HeavySnow:
                case WeatherState.Blizzard:
                    return PrecipitationKind.Snow;

                // Rain states freeze into snow at or below freezing. §6.3 notes mixed precipitation
                // is out of scope: it is either rain or snow per location, from local temperature.
                case WeatherState.LightRain:
                case WeatherState.HeavyRain:
                case WeatherState.Thunderstorm:
                    return temperature <= FreezingTemperatureC ? PrecipitationKind.Snow : PrecipitationKind.Rain;

                // Clear, PartlyCloudy, Overcast, Fog — nothing falls.
                default:
                    return PrecipitationKind.None;
            }
        }

        // Whether a weather state drops anything at all, at any temperature. This is the cheap
        // sky-level gate: a caller that would otherwise resolve a temperature per location (snow
        // accumulation sampling, precipitation presentation) skips that work entirely while the
        // sky is clear. Rain-vs-snow still needs a temperature — see ResolvePrecipitationKind.
        // Derived from the intensity table so the two can never disagree about what is falling.
        public static bool IsPrecipitating(WeatherState state) => PrecipitationIntensityFor(state) > 0f;

        public void Tick(int deltaTicks)
        {
            if (deltaTicks <= 0)
                return;

            ticksInCurrentState += deltaTicks;

            while (ticksInCurrentState >= MinDurationTicks[(int)currentState])
            {
                ticksInCurrentState -= MinDurationTicks[(int)currentState];
                currentState = PickNextState(currentState);
            }
        }

        WeatherState PickNextState(WeatherState from)
        {
            int row = (int)from;
            int totalWeight = 0;
            for (int i = 0; i < 10; i++)
                totalWeight += TransitionWeights[row, i];

            int roll = (int)(NextRng() % (uint)totalWeight);
            int accumulated = 0;
            for (int i = 0; i < 10; i++)
            {
                accumulated += TransitionWeights[row, i];
                if (roll < accumulated)
                    return (WeatherState)i;
            }

            return from;
        }

        uint NextRng()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return rngState;
        }

        // §6.1 biome base temperatures. These replaced the old per-weather-state bases: place, not
        // sky, sets how cold somewhere is, and weather then pushes it around via §6.2's modifiers.
        // AnyBiomeIndex and any out-of-range index fall back to Meadow's temperate 18 °C.
        public static float BiomeBaseTemperature(int biomeIndex)
        {
            return biomeIndex switch
            {
                SurvivalBiomeResolver.DunesBiomeIndex     =>  34f,
                SurvivalBiomeResolver.DrybrushBiomeIndex  =>  26f,
                SurvivalBiomeResolver.MeadowBiomeIndex    =>  18f,
                SurvivalBiomeResolver.WetlandBiomeIndex   =>  16f,
                SurvivalBiomeResolver.PinewildBiomeIndex  =>  10f,
                SurvivalBiomeResolver.HighlandsBiomeIndex =>   8f,
                SurvivalBiomeResolver.TundraBiomeIndex    =>  -8f,
                _                                         =>  18f,
            };
        }

        static float PrecipitationIntensityFor(WeatherState state)
        {
            return state switch
            {
                WeatherState.LightRain    => 0.3f,
                WeatherState.HeavyRain    => 0.7f,
                WeatherState.Thunderstorm => 1.0f,
                WeatherState.LightSnow    => 0.3f,
                WeatherState.HeavySnow    => 0.7f,
                WeatherState.Blizzard     => 1.0f,
                _                         => 0f,
            };
        }

        static float FogDensityFor(WeatherState state)
        {
            return state switch
            {
                WeatherState.Fog          => 0.8f,
                WeatherState.HeavyRain    => 0.3f,
                WeatherState.Thunderstorm => 0.4f,
                WeatherState.HeavySnow    => 0.4f,
                WeatherState.Blizzard     => 0.9f,
                _                         => 0f,
            };
        }

        static float StormIntensityFor(WeatherState state)
        {
            return state switch
            {
                WeatherState.Thunderstorm => 1.0f,
                WeatherState.Blizzard     => 0.8f,
                WeatherState.HeavyRain    => 0.4f,
                WeatherState.HeavySnow    => 0.3f,
                _                         => 0f,
            };
        }

        // Night is WorldConstants' one definition, not a second opinion. This used to read
        // `t > 0.6 || t < 0.1`, which disagreed with WorldTimeClock.IsDay (0.05 / 0.55) over
        // [0.05, 0.10) and [0.55, 0.60]. That was harmless while temperature was weather-derived
        // and never sat near a threshold; once temperature became biome-led it decided whether a
        // player took cold damage, so the -5 °C modifier and SurvivalVitals' night-cold threshold
        // were being gated by two predicates that disagreed for 10% of every day.
        static bool IsNight(float normalizedTime) => WorldConstants.IsNight(normalizedTime);

        // Target cloud coverage per state (voxel_world_environment_effects.md §8.3 target values).
        static float TargetCloudCoverage(WeatherState state) => state switch
        {
            WeatherState.Clear        => 0.10f,
            WeatherState.PartlyCloudy => 0.45f,
            WeatherState.Overcast     => 0.80f,
            WeatherState.LightRain    => 0.85f,
            WeatherState.HeavyRain    => 0.95f,
            WeatherState.Thunderstorm => 1.00f,
            WeatherState.LightSnow    => 0.85f,
            WeatherState.HeavySnow    => 0.95f,
            WeatherState.Blizzard     => 1.00f,
            WeatherState.Fog          => 0.65f,
            _                         => 0.50f,
        };
    }
}
