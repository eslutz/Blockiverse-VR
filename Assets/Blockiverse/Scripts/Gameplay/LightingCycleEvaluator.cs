using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public readonly struct LightingCycleState
    {
        public LightingCycleState(
            Quaternion sunRotation,
            float sunIntensity,
            Color sunColor,
            Quaternion moonRotation,
            float moonIntensity,
            Color moonColor,
            bool isMoonPrimary,
            Color ambientColor)
        {
            SunRotation = sunRotation;
            SunIntensity = sunIntensity;
            SunColor = sunColor;
            MoonRotation = moonRotation;
            MoonIntensity = moonIntensity;
            MoonColor = moonColor;
            IsMoonPrimary = isMoonPrimary;
            AmbientColor = ambientColor;
        }

        public Quaternion SunRotation { get; }
        public float SunIntensity { get; }
        public Color SunColor { get; }

        public Quaternion MoonRotation { get; }
        public float MoonIntensity { get; }
        public Color MoonColor { get; }

        // URP supports exactly one main directional light, so the sun and the moon share it:
        // whichever body is above the horizon drives the scene's single directional light.
        public bool IsMoonPrimary { get; }

        public Quaternion PrimaryRotation => IsMoonPrimary ? MoonRotation : SunRotation;
        public float PrimaryIntensity => IsMoonPrimary ? MoonIntensity : SunIntensity;
        public Color PrimaryColor => IsMoonPrimary ? MoonColor : SunColor;

        public Color AmbientColor { get; }
    }

    public static class LightingCycleEvaluator
    {
        public static readonly Color DaySunColor = new(1.0f, 0.95f, 0.82f, 1.0f);
        public static readonly Color NightSunColor = new(0.25f, 0.32f, 0.48f, 1.0f);
        public static readonly Color DayAmbientColor = new(0.22f, 0.24f, 0.25f, 1.0f);

        // Cool, desaturated moonlight (§19.1 "dark blue-black" night sky).
        public static readonly Color MoonColor = new(0.62f, 0.71f, 1.0f, 1.0f);

        // Full-moon ambient. voxel_world_environment_effects.md §4.4 puts a full-moon night at
        // sky light 4 of 15, so this carries exactly 4/15 of DayAmbientColor's radiance.
        // NOTE: the project renders in LINEAR colour space, so the ratio is 4/15 of the LINEAR
        // luminance (0.01214 vs the daytime 0.04570) even though these components are authored in
        // gamma. Deriving the values in gamma space instead lands night at ~11% and leaves it
        // roughly half as bright as the ruleset specifies.
        public static readonly Color FullMoonAmbientColor = new(0.099f, 0.112f, 0.153f, 1.0f);

        const float DaySunIntensity = 1.15f;

        // Full-moon directional intensity, derived in LINEAR space to match the renderer:
        // sun radiance = 1.15 x linearLuminance(DaySunColor 0.8952) = 1.0295, and the moon
        // 0.577 x linearLuminance(MoonColor 0.4757) = 0.2745, giving 0.2745 / 1.0295 = 4/15.
        // The raw number looks high next to 4/15 only because moonlight is tinted cooler, and cool
        // tints carry less luminance per unit intensity than the warm sun.
        const float FullMoonIntensity = 0.577f;

        // Half-width of the twilight band, in units of sine-of-sun-elevation, over which ambient
        // crossfades between night and day.
        const float TwilightBand = 0.25f;

        const float SunYawDegrees = -30.0f;

        public static LightingCycleState Evaluate(float normalizedTime) =>
            Evaluate(normalizedTime, EnvironmentLightComputer.FullMoonLightLevel);

        public static LightingCycleState Evaluate(float normalizedTime, int moonPhaseIndex)
        {
            float time = Normalize(normalizedTime);

            // +1 with the sun overhead at 0.25, -1 at midnight (0.75).
            float sunElevation = Mathf.Sin(time * Mathf.PI * 2.0f);
            float dayAmount = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(sunElevation));
            float moonAmount = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp01(-sunElevation));

            float sunPitch = time * 360.0f;
            Quaternion sunRotation = Quaternion.Euler(sunPitch, SunYawDegrees, 0.0f);
            // The moon rides the opposite half of the same arc, so it is overhead at midnight.
            Quaternion moonRotation = Quaternion.Euler(sunPitch + 180.0f, SunYawDegrees, 0.0f);

            // Phase scales both the moon light and the night ambient, so night radiance tracks the
            // canonical 1..4 of 15 sky-light ladder: new moon 1/15, crescent 2/15, quarter and
            // gibbous 3/15, full 4/15.
            int moonLightLevel = EnvironmentLightComputer.MoonLightLevel(moonPhaseIndex);
            float phaseScale = moonLightLevel / (float)EnvironmentLightComputer.FullMoonLightLevel;

            float sunIntensity = DaySunIntensity * dayAmount;
            float moonIntensity = FullMoonIntensity * phaseScale * moonAmount;

            // Scale in LINEAR space, then convert back. Multiplying the gamma components directly
            // is not a brightness scale: a quarter-strength new moon would land at 4.8% of daylight
            // instead of the canonical 1/15 (6.7%), because the sRGB curve is not linear.
            Color nightAmbient = (FullMoonAmbientColor.linear * phaseScale).gamma;
            nightAmbient.a = 1.0f;

            // Ambient gets its own, wider curve than the two bodies. Both directional intensities
            // legitimately fall to zero at the horizon (grazing light), but keying ambient off
            // dayAmount too made dawn and dusk DARKER than midnight, because ambient collapsed to
            // the night value while neither body was contributing. Twilight is ambient-dominated,
            // so ambient crossfades across the horizon instead.
            float ambientAmount = Mathf.SmoothStep(
                0.0f, 1.0f, Mathf.Clamp01((sunElevation + TwilightBand) / (TwilightBand * 2.0f)));

            return new LightingCycleState(
                sunRotation,
                sunIntensity,
                Color.Lerp(NightSunColor, DaySunColor, dayAmount),
                moonRotation,
                moonIntensity,
                MoonColor,
                isMoonPrimary: moonAmount > dayAmount,
                Color.Lerp(nightAmbient, DayAmbientColor, ambientAmount));
        }

        static float Normalize(float value)
        {
            value %= 1.0f;
            return value < 0.0f ? value + 1.0f : value;
        }
    }
}
