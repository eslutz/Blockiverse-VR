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
        // Aerial perspective is never zero. Clear, PartlyCloudy and Overcast all report
        // FogDensity 0 and PrecipitationIntensity 0, so without a floor this returned 0 and the
        // caller switched RenderSettings.fog OFF entirely — which is why clear weather had no
        // haze at any distance and the only fade visible was the sky gradient behind the world
        // edge, reading as "something far off in the distance" with clear air up close.
        // 0.006 read as visible haze at arm's length. This is tuned so a clear day is
        // effectively transparent up close — ~1% at 10 m, which the eye cannot separate from
        // nothing — while still accumulating enough over a couple of hundred metres to keep the
        // far terrain from sitting flat against the sky.
        public const float ClearAirDensity = 0.0012f;

        public static float FogDensity(EnvironmentState environment)
        {
            // Multipliers are tuned for FogMode.Exponential (opacity = 1 - exp(-rho*d)), which is
            // FIRST order in distance so it reads from a few metres out. The previous values were
            // tuned against ExponentialSquared, whose (rho*d)^2 term is ~flat near the viewer and
            // then knees hard — a band far away rather than a gradient.
            float fromFog = environment.FogDensity * 0.075f;
            float fromPrecip = environment.PrecipitationIntensity * 0.025f;
            return Mathf.Max(ClearAirDensity, Mathf.Max(fromFog, fromPrecip));
        }
    }
}
