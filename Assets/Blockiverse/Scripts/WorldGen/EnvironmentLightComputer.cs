using System;

namespace Blockiverse.WorldGen
{
    // Pure-C# helper that derives sky light and ambient light from time-of-day and weather state.
    // Rules: voxel_world_environment_effects.md §4.3 (sky light by phase) and §5.4 (weather penalty).
    public static class EnvironmentLightComputer
    {
        // Returns the base outdoor sky light (0–15) for the given normalized time of day (0.0–1.0)
        // and moon phase index (0=New → 4=Full → 7=Waning Crescent).
        public static int GetBaseSkyLight(float normalizedTime, int moonPhaseIndex = 4)
        {
            int moonLight = MoonLightLevel(moonPhaseIndex);
            // Dawn: 0.000–0.083
            if (normalizedTime < 0.083f)
                return (int)Math.Round(Lerp(5f, 15f, normalizedTime / 0.083f));
            // Day: 0.083–0.416
            if (normalizedTime < 0.416f)
                return 15;
            // Dusk: 0.416–0.500
            if (normalizedTime < 0.500f)
                return (int)Math.Round(Lerp(15f, 5f, (normalizedTime - 0.416f) / 0.084f));
            // Night: 0.500–0.916
            if (normalizedTime < 0.916f)
                return moonLight;
            // Pre-Dawn: 0.916–1.000
            return (int)Math.Round(Lerp(moonLight, 5f, (normalizedTime - 0.916f) / 0.084f));
        }

        // Combines sky light with weather state to produce the final outdoor ambient light (0–15).
        public static int GetAmbientLight(
            float normalizedTime,
            int moonPhaseIndex,
            float cloudCoverage,
            float precipitationIntensity,
            float stormIntensity)
        {
            int baseSkyLight = GetBaseSkyLight(normalizedTime, moonPhaseIndex);
            float penalty = cloudCoverage * 3f + precipitationIntensity * 2f + stormIntensity * 2f;
            return Math.Max(0, Math.Min(15, baseSkyLight - (int)Math.Round(penalty)));
        }

        // Moon night-sky light level per 8-day cycle index (voxel_world_environment_effects.md §4.4).
        static int MoonLightLevel(int moonPhaseIndex) => (moonPhaseIndex & 7) switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 3,
            4 => 4,
            5 => 3,
            6 => 3,
            7 => 2,
            _ => 1,
        };

        static float Lerp(float a, float b, float t) =>
            a + (b - a) * Math.Max(0f, Math.Min(1f, t));
    }
}
