using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Gameplay
{
    public sealed class BlockiverseLightingCycleController : MonoBehaviour
    {
        [SerializeField] WorldTimeClock worldTimeClock;
        [SerializeField] Light sunLight;

        public WorldTimeClock Clock => worldTimeClock;
        public Light SunLight => sunLight;

        public void Configure(WorldTimeClock clock, Light sun)
        {
            worldTimeClock = clock;
            sunLight = sun;
            ApplyCurrentLighting();
        }

        void Awake()
        {
            if (worldTimeClock == null)
                worldTimeClock = GetComponent<WorldTimeClock>();

            if (sunLight == null)
                sunLight = GetComponent<Light>();

            ApplyCurrentLighting();
        }

        void LateUpdate()
        {
            ApplyCurrentLighting();
        }

        public void ApplyCurrentLighting()
        {
            if (worldTimeClock == null || sunLight == null)
                return;

            LightingCycleState state = LightingCycleEvaluator.Evaluate(worldTimeClock.NormalizedTime);
            transform.rotation = state.SunRotation;
            sunLight.type = LightType.Directional;
            sunLight.intensity = state.SunIntensity;
            sunLight.color = state.SunColor;
            sunLight.shadows = LightShadows.Hard;
            sunLight.shadowStrength = 0.85f;
            sunLight.renderMode = LightRenderMode.ForcePixel;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = state.AmbientColor;
            RenderSettings.sun = sunLight;
        }
    }
}
