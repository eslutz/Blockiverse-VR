using UnityEngine;

namespace Blockiverse.Gameplay
{
    public readonly struct LightingCycleState
    {
        public LightingCycleState(Quaternion sunRotation, float sunIntensity, Color sunColor, Color ambientColor)
        {
            SunRotation = sunRotation;
            SunIntensity = sunIntensity;
            SunColor = sunColor;
            AmbientColor = ambientColor;
        }

        public Quaternion SunRotation { get; }
        public float SunIntensity { get; }
        public Color SunColor { get; }
        public Color AmbientColor { get; }
    }

    public static class LightingCycleEvaluator
    {
        public static readonly Color DaySunColor = new(1.0f, 0.95f, 0.82f, 1.0f);
        public static readonly Color NightSunColor = new(0.25f, 0.32f, 0.48f, 1.0f);
        public static readonly Color DayAmbientColor = new(0.22f, 0.24f, 0.25f, 1.0f);
        public static readonly Color NightAmbientColor = new(0.025f, 0.03f, 0.045f, 1.0f);

        const float DaySunIntensity = 1.15f;
        const float NightSunIntensity = 0.025f;
        const float SunYawDegrees = -30.0f;

        public static LightingCycleState Evaluate(float normalizedTime)
        {
            float dayAmount = Mathf.Clamp01(Mathf.Sin(Normalize(normalizedTime) * Mathf.PI * 2.0f));
            dayAmount = Mathf.SmoothStep(0.0f, 1.0f, dayAmount);
            float sunPitch = Normalize(normalizedTime) * 360.0f;

            return new LightingCycleState(
                Quaternion.Euler(sunPitch, SunYawDegrees, 0.0f),
                Mathf.Lerp(NightSunIntensity, DaySunIntensity, dayAmount),
                Color.Lerp(NightSunColor, DaySunColor, dayAmount),
                Color.Lerp(NightAmbientColor, DayAmbientColor, dayAmount));
        }

        static float Normalize(float value)
        {
            value %= 1.0f;
            return value < 0.0f ? value + 1.0f : value;
        }
    }
}
