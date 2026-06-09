using System;

namespace Blockiverse.WorldGen
{
    public enum WeatherState
    {
        Clear,
        PartlyCloudy,
        Overcast,
        LightRain,
        HeavyRain,
        Thunderstorm,
        LightSnow,
        HeavySnow,
        Blizzard,
        Fog,
    }

    public struct EnvironmentState
    {
        public WeatherState Weather;
        public float Temperature;
        public float PrecipitationIntensity;
        public float FogDensity;
        public float StormIntensity;
        public float CloudCoverage;
    }

    public sealed class WeatherService
    {
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

        public float CloudCoverage => TargetCloudCoverage(currentState);

        // Returns the ambient light level (0–15) after applying weather penalties to the base sky light.
        // weatherLightPenalty = round(cloudCoverage×3 + precipIntensity×2 + stormIntensity×2)
        // per voxel_world_environment_effects.md §5.4
        public int AmbientLightLevel(int baseSkyLight)
        {
            float penalty = CloudCoverage * 3f
                + PrecipitationIntensityFor(currentState) * 2f
                + StormIntensityFor(currentState) * 2f;
            return Math.Max(0, Math.Min(15, baseSkyLight - (int)Math.Round(penalty)));
        }

        public EnvironmentState Evaluate(float normalizedTimeOfDay, int altitudeY)
        {
            float baseTemp = ComputeBaseTemperature(currentState);
            float altitudePenalty = Math.Max(0, altitudeY - WorldConstants.SeaLevel) * 0.05f;
            float nightPenalty = IsNight(normalizedTimeOfDay) ? 5f : 0f;
            float precipPenalty = IsPrecipitating(currentState) ? 3f : 0f;

            return new EnvironmentState
            {
                Weather              = currentState,
                Temperature          = baseTemp - altitudePenalty - nightPenalty - precipPenalty,
                PrecipitationIntensity = PrecipitationIntensityFor(currentState),
                FogDensity           = FogDensityFor(currentState),
                StormIntensity       = StormIntensityFor(currentState),
                CloudCoverage        = CloudCoverage,
            };
        }

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

        static float ComputeBaseTemperature(WeatherState state)
        {
            return state switch
            {
                WeatherState.Clear        => 20f,
                WeatherState.PartlyCloudy => 18f,
                WeatherState.Overcast     => 14f,
                WeatherState.LightRain    => 12f,
                WeatherState.HeavyRain    => 10f,
                WeatherState.Thunderstorm => 9f,
                WeatherState.LightSnow    => 0f,
                WeatherState.HeavySnow    => -4f,
                WeatherState.Blizzard     => -8f,
                WeatherState.Fog          => 10f,
                _                         => 15f,
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

        static bool IsPrecipitating(WeatherState state)
        {
            return state is WeatherState.LightRain or WeatherState.HeavyRain or WeatherState.Thunderstorm
                       or WeatherState.LightSnow  or WeatherState.HeavySnow  or WeatherState.Blizzard;
        }

        static bool IsNight(float normalizedTime)
        {
            return normalizedTime > 0.6f || normalizedTime < 0.1f;
        }

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
