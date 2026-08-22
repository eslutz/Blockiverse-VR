using UnityEngine;

namespace Blockiverse.Gameplay
{
    // What colour the sky is, given the time of day, the moon phase and the weather.
    //
    // Pure statics so every value is testable without a scene. The sky used to be Unity's stock
    // procedural skybox, which derives everything from the direction of RenderSettings.sun --
    // and since this project points one shared light DOWN FROM OVERHEAD at night so the ground
    // stays lit, that skybox drew a full noon sky at midnight. Nothing modulated it, so the sky
    // had no idea what time it was and no idea what the weather was.
    public static class SkyGradientSolver
    {
        public static readonly Color DayZenith = new(0.16f, 0.36f, 0.68f, 1.0f);
        public static readonly Color DayHorizon = new(0.66f, 0.78f, 0.92f, 1.0f);
        public static readonly Color DayGround = new(0.30f, 0.30f, 0.31f, 1.0f);

        // Deep blue-black rather than pure black: a black sky reads as a rendering failure, and
        // the ruleset asks for "dark blue-black".
        public static readonly Color NightZenith = new(0.015f, 0.022f, 0.052f, 1.0f);
        public static readonly Color NightHorizon = new(0.045f, 0.058f, 0.098f, 1.0f);
        public static readonly Color NightGround = new(0.012f, 0.013f, 0.016f, 1.0f);

        // Dawn and dusk. Warm at the horizon, still dark above.
        public static readonly Color TwilightZenith = new(0.10f, 0.13f, 0.30f, 1.0f);
        public static readonly Color TwilightHorizon = new(0.72f, 0.44f, 0.30f, 1.0f);

        public static readonly Color DaySunColor = new(1.0f, 0.94f, 0.80f, 1.0f);
        public static readonly Color MoonSunColor = new(0.68f, 0.76f, 0.98f, 1.0f);

        public static readonly Color DayCloudColor = new(0.96f, 0.96f, 0.97f, 1.0f);
        public static readonly Color StormCloudColor = new(0.28f, 0.29f, 0.33f, 1.0f);

        // Half-width of the twilight band in units of sun elevation, matching the ambient
        // crossfade in LightingCycleEvaluator so the sky and the ground never disagree about
        // whether it is night.
        public const float TwilightBand = 0.25f;

        // Below this the sun disk is hidden entirely. Without it the "sun" sits at the zenith at
        // midnight, because the shared directional light is pointed straight down then.
        public const float SunDiskElevationFloor = -0.05f;

        // How dark a fully overcast sky gets, as a multiplier on the gradient.
        public const float OvercastDarkening = 0.45f;

        // A value in [-1, 1]: +1 is the sun overhead, 0 is the horizon, -1 is midnight.
        // Derived from the clock rather than from the shared light's rotation, which is the whole
        // point -- that light lies about elevation at night by design.
        // The clock's convention: 0.25 is midday and 0.75 is midnight, matching
        // LightingCycleEvaluator, so elevation peaks at 0.25 and bottoms at 0.75.
        public static float SunElevation(float normalizedTime) =>
            Mathf.Sin(normalizedTime * 2.0f * Mathf.PI);

        // 0 at full night, 1 at full day, crossfading across the twilight band.
        public static float DayAmount(float normalizedTime) =>
            Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(-TwilightBand, TwilightBand, SunElevation(normalizedTime)));

        // Peaks at 1 in the middle of the twilight band and is 0 in full day or full night.
        public static float TwilightAmount(float normalizedTime)
        {
            float elevation = SunElevation(normalizedTime);
            float distance = Mathf.Abs(elevation) / TwilightBand;
            return Mathf.Clamp01(1.0f - distance);
        }

        public static Color ZenithColor(float normalizedTime, float cloudCoverage, float moonPhaseScale)
        {
            Color night = ScaleRgb(NightZenith, MoonlitNightScale(moonPhaseScale));
            Color baseColor = Color.Lerp(night, DayZenith, DayAmount(normalizedTime));
            baseColor = Color.Lerp(baseColor, TwilightZenith, TwilightAmount(normalizedTime) * DayAmount(normalizedTime));
            return Overcast(baseColor, cloudCoverage);
        }

        public static Color HorizonColor(float normalizedTime, float cloudCoverage, float moonPhaseScale)
        {
            Color night = ScaleRgb(NightHorizon, MoonlitNightScale(moonPhaseScale));
            Color baseColor = Color.Lerp(night, DayHorizon, DayAmount(normalizedTime));
            baseColor = Color.Lerp(baseColor, TwilightHorizon, TwilightAmount(normalizedTime));
            return Overcast(baseColor, cloudCoverage);
        }

        public static Color GroundColor(float normalizedTime, float cloudCoverage, float moonPhaseScale)
        {
            Color night = ScaleRgb(NightGround, MoonlitNightScale(moonPhaseScale));
            return Overcast(Color.Lerp(night, DayGround, DayAmount(normalizedTime)), cloudCoverage);
        }

        // The disk's colour follows whichever body is up, and it is hidden outright once the real
        // sun elevation drops below the horizon.
        public static Color SunDiskColor(float normalizedTime, float moonPhaseScale)
        {
            float elevation = SunElevation(normalizedTime);

            if (elevation <= SunDiskElevationFloor)
                return ScaleRgb(MoonSunColor, Mathf.Clamp01(moonPhaseScale));

            return Color.Lerp(MoonSunColor, DaySunColor, DayAmount(normalizedTime));
        }

        public static Color CloudColor(float normalizedTime, float cloudCoverage)
        {
            Color lit = Color.Lerp(StormCloudColor, DayCloudColor, 1.0f - Mathf.Clamp01(cloudCoverage));
            // Clouds are lit by the sky, so they go dark at night along with everything else --
            // but never fully black, or an overcast night becomes a flat void.
            float brightness = Mathf.Lerp(0.10f, 1.0f, DayAmount(normalizedTime));
            return ScaleRgb(lit, brightness);
        }

        // A moonless night is darker than a full-moon one, matching the directional term.
        static float MoonlitNightScale(float moonPhaseScale) => Mathf.Lerp(0.45f, 1.0f, Mathf.Clamp01(moonPhaseScale));

        static Color Overcast(Color color, float cloudCoverage) =>
            ScaleRgb(color, Mathf.Lerp(1.0f, OvercastDarkening, Mathf.Clamp01(cloudCoverage)));

        static Color ScaleRgb(Color color, float scale) => new(color.r * scale, color.g * scale, color.b * scale, color.a);
    }
}
