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

        const float DaySunIntensity = 1.15f;

        // How bright a FULL moon is relative to noon, as a fraction of LINEAR radiance. Both the
        // directional intensity and the night ambient are derived from this one number, so the two
        // can never drift apart.
        //
        // This was 4/15, taken from the gameplay sky-light ladder in
        // voxel_world_environment_effects.md §4.4 -- a 0-15 visibility/spawn/crop scale that ADR
        // 0006 adopted as a RENDER target. It was never a photometric ratio, and it does not work
        // as one: 4/15 of linear radiance presents as roughly 55% of noon after the sRGB transfer,
        // so a full-moon night rendered about as bright as an overcast afternoon.
        //
        // 1/15 makes a full moon exactly as bright as the old NEW moon -- the one phase that never
        // read as too bright. The gameplay ladder in EnvironmentLightComputer is deliberately NOT
        // changed: it is engine-free WorldGen and remains the authority for crop growth and spawn
        // gating, which must not move because the renderer did.
        public const float FullMoonRadianceFraction = 1.0f / 15.0f;

        // Ambient never falls below this fraction of daylight radiance, whatever the moon phase.
        //
        // Dimming the full moon to 1/15 drags every other phase down with it, and a NEW moon at a
        // quarter of that lands at 1/60 -- half the brightness a previous build was called
        // unnavigable at. Ambient is what decides whether you can see anything at all, so it gets
        // a floor; the DIRECTIONAL moon still scales the full four-to-one with phase, which is
        // where phase actually reads (moonlight direction, shading, whether shadows are cast).
        // The cost is that ambient varies about two-to-one across the phase cycle instead of
        // four-to-one.
        public const float MinimumNightRadianceFraction = 1.0f / 25.0f;

        // Both derived in LINEAR space, which is what the renderer works in. Deriving them in
        // gamma instead is the bug documented in this file's history: it lands night at roughly
        // half the intended radiance.
        public static readonly Color FullMoonAmbientColor = ScaleRadiance(DayAmbientColor, FullMoonRadianceFraction);

        // The raw number looks small next to the fraction only because moonlight is tinted cooler,
        // and cool tints carry less luminance per unit intensity than the warm sun.
        static readonly float FullMoonIntensity =
            DaySunIntensity * LinearLuminance(DaySunColor) * FullMoonRadianceFraction / LinearLuminance(MoonColor);

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
            // Floored, unlike the directional term above: see MinimumNightRadianceFraction.
            float ambientFraction = Mathf.Max(
                FullMoonRadianceFraction * phaseScale, MinimumNightRadianceFraction);
            Color nightAmbient = ScaleRadiance(DayAmbientColor, ambientFraction);
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

        // Rec. 709 luminance of a gamma-authored colour, measured in linear space.
        public static float LinearLuminance(Color gammaColor)
        {
            Color linear = gammaColor.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        // Scales a gamma-authored colour to a fraction of its LINEAR radiance and returns it back
        // in gamma. Alpha is carried through untouched.
        public static Color ScaleRadiance(Color gammaColor, float fraction)
        {
            Color scaled = gammaColor.linear * fraction;
            scaled.a = gammaColor.a;
            return scaled.gamma;
        }

    }
}
