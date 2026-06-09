using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Pure helper that turns live weather into runtime lighting/fog adjustments. Isolated from any
    // MonoBehaviour so the math is unit-testable. The lighting controller applies these on top of the
    // time-of-day cycle, so the values returned here are WEATHER-ONLY multipliers/overlays.
    public static class EnvironmentLightingSolver
    {
        // Never fully black out the scene from weather alone — keep a readable floor for VR comfort.
        public const float MinWeatherLightFactor = 0.35f;

        // Runtime has no lunar cycle yet; assume a full moon so night weather still reads.
        const int RuntimeMoonPhaseIndex = 4;

        // Returns a 0.35–1.0 multiplier for sun intensity and ambient colour. 1.0 = clear sky; lower
        // under cloud/precipitation/storm. Computed as the ratio of weather-penalised outdoor light to
        // the clear-sky light at the same time of day, so it isolates the weather contribution from
        // the day/night cycle the lighting controller already applies.
        public static float WeatherLightFactor(float normalizedTime, EnvironmentState environment)
        {
            int baseSky = EnvironmentLightComputer.GetBaseSkyLight(normalizedTime, RuntimeMoonPhaseIndex);
            if (baseSky <= 0)
                return 1f; // nothing to dim (deep night handled by the day/night cycle)

            int withWeather = EnvironmentLightComputer.GetAmbientLight(
                normalizedTime,
                RuntimeMoonPhaseIndex,
                environment.CloudCoverage,
                environment.PrecipitationIntensity,
                environment.StormIntensity);

            float ratio = Mathf.Clamp01(withWeather / (float)baseSky);
            return Mathf.Lerp(MinWeatherLightFactor, 1f, ratio);
        }

        // Unity fog density for the current weather (0 = no fog). Combines explicit fog states with a
        // lighter haze from heavy precipitation.
        public static float FogDensity(EnvironmentState environment)
        {
            float fromFog = environment.FogDensity * 0.04f;
            float fromPrecip = environment.PrecipitationIntensity * 0.012f;
            return Mathf.Max(fromFog, fromPrecip);
        }
    }
}
