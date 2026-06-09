using Blockiverse.WorldGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Gameplay
{
    public sealed class BlockiverseLightingCycleController : MonoBehaviour
    {
        [SerializeField] WorldTimeClock worldTimeClock;
        [SerializeField] Light sunLight;
        [SerializeField] CreativeWorldManager environmentSource;

        static readonly Color ClearFogColor = new(0.62f, 0.70f, 0.80f);

        public WorldTimeClock Clock => worldTimeClock;
        public Light SunLight => sunLight;

        public void Configure(WorldTimeClock clock, Light sun, CreativeWorldManager environment = null)
        {
            worldTimeClock = clock;
            sunLight = sun;
            if (environment != null)
                environmentSource = environment;
            ApplyCurrentLighting();
        }

        void Awake()
        {
            if (worldTimeClock == null)
                worldTimeClock = GetComponent<WorldTimeClock>();

            if (sunLight == null)
                sunLight = GetComponent<Light>();

            if (environmentSource == null)
                environmentSource = FindFirstObjectByType<CreativeWorldManager>();

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

            // Fold live weather into the day/night cycle: dim sun + ambient under cloud/precip/storm
            // and raise fog. This is what connects the weather simulation to what the player sees.
            float weatherFactor = 1f;
            bool applyFog = false;
            float fogDensity = 0f;
            if (environmentSource != null &&
                environmentSource.TryEvaluateEnvironment(WorldConstants.SeaLevel, out EnvironmentState environment))
            {
                weatherFactor = EnvironmentLightingSolver.WeatherLightFactor(worldTimeClock.NormalizedTime, environment);
                fogDensity = EnvironmentLightingSolver.FogDensity(environment);
                applyFog = fogDensity > 0f;
            }

            transform.rotation = state.SunRotation;
            sunLight.type = LightType.Directional;
            sunLight.intensity = state.SunIntensity * weatherFactor;
            sunLight.color = state.SunColor;
            sunLight.shadows = LightShadows.Hard;
            sunLight.shadowStrength = 0.85f;
            sunLight.renderMode = LightRenderMode.ForcePixel;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = state.AmbientColor * weatherFactor;
            RenderSettings.sun = sunLight;

            RenderSettings.fog = applyFog;
            if (applyFog)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = state.AmbientColor * weatherFactor + ClearFogColor * 0.25f;
                RenderSettings.fogDensity = fogDensity;
            }
        }
    }
}
