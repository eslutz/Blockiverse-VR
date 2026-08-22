using Blockiverse.WorldGen;
using Blockiverse.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Gameplay
{
    public sealed class BlockiverseLightingCycleController : MonoBehaviour
    {
        [SerializeField] WorldTimeClock worldTimeClock;
        [SerializeField] Light sunLight;
        [SerializeField] CreativeWorldManager environmentSource;
        [SerializeField] BlockiverseWaterView waterView;

        [Header("Shadows")]
        [Tooltip("Shadow style for the sun/moon directional light. Hard is the Quest default; the URP asset ships with soft shadows unsupported, which would silently downgrade Soft anyway.")]
        [SerializeField] LightShadows directionalShadows = LightShadows.Hard;
        [Range(0f, 1f)][SerializeField] float daytimeShadowStrength = 0.80f;
        [Range(0f, 1f)][SerializeField] float nighttimeShadowStrength = 0.45f;

        // Effective directional intensity below which the shadow pass is skipped entirely. Set
        // below the dimmest real key light — a new moon is 0.577 x 1/4 = 0.144 — so it only catches
        // the near-zero twilight window, where a full shadow-caster sweep buys nothing visible.
        public const float MinimumShadowCastingIntensity = 0.05f;

        static readonly Color ClearFogColor = new(0.62f, 0.70f, 0.80f);

        public WorldTimeClock Clock => worldTimeClock;
        public Light SunLight => sunLight;

        // Whether the moon (rather than the sun) is currently driving the directional light.
        public bool IsMoonPrimary { get; private set; }

        // Moon phase index (0 = new, 4 = full) for the clock's current day.
        public int MoonPhaseIndex { get; private set; } = EnvironmentLightComputer.FullMoonLightLevel;

        public void Configure(
            WorldTimeClock clock,
            Light sun,
            CreativeWorldManager environment = null,
            BlockiverseWaterView water = null)
        {
            worldTimeClock = clock;
            sunLight = sun;
            if (environment != null)
                environmentSource = environment;
            if (water != null)
                waterView = water;
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

            if (waterView == null)
                waterView = FindFirstObjectByType<BlockiverseWaterView>();

            ApplyCurrentLighting();
        }

        void LateUpdate()
        {
            ApplyCurrentLighting();
        }

        // Moon phase advances once per game day and is derived purely from the clock's absolute
        // elapsed ticks, so every peer and every reloaded save agrees without extra synced state.
        public static int ResolveMoonPhaseIndex(WorldTimeClock clock)
        {
            if (clock == null)
                return EnvironmentLightComputer.FullMoonLightLevel;

            // Keyed off the canonical 24000-tick day rather than the clock's configurable
            // dayLengthSeconds, so the phase matches the save file and the host's snapshot even
            // when a world runs a non-default day length.
            return EnvironmentLightComputer.MoonPhaseIndexForDay(
                clock.TotalElapsedTicks / WorldConstants.TicksPerDay);
        }

        public void ApplyCurrentLighting()
        {
            float submergedBlend = waterView != null ? waterView.SubmergedBlend : 0.0f;

            // Underwater fog is resolved above the clock/sun guard on purpose. A world can be
            // running -- and the player already swimming -- while this returns early, because
            // CreativeWorldManager.ConfigureEnvironmentServices itself bails when it finds no
            // WorldTimeClock. Losing fog there would mean surfacing into clear air underwater.
            if (worldTimeClock == null || sunLight == null)
            {
                ApplyFog(applyWeatherFog: false, RenderSettings.fogColor, weatherFogDensity: 0.0f, submergedBlend);
                return;
            }

            MoonPhaseIndex = ResolveMoonPhaseIndex(worldTimeClock);
            LightingCycleState state = LightingCycleEvaluator.Evaluate(worldTimeClock.NormalizedTime, MoonPhaseIndex);
            IsMoonPrimary = state.IsMoonPrimary;

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

            // One directional light serves as both bodies — URP only ever promotes a single
            // directional light to the main light, and a second one would cost an additional-light
            // slot on every object for no visual gain.
            transform.rotation = state.PrimaryRotation;
            sunLight.type = LightType.Directional;
            sunLight.intensity = state.PrimaryIntensity * weatherFactor;
            sunLight.color = state.PrimaryColor;
            sunLight.renderMode = LightRenderMode.ForcePixel;

            // Below this the key light is too dim for its shadows to be perceptible, so the whole
            // shadow-caster sweep over loaded chunks is wasted work — a new-moon night otherwise
            // pays the same shadow pass as noon.
            bool shadowsWorthDrawing = sunLight.intensity >= MinimumShadowCastingIntensity;
            sunLight.shadows = shadowsWorthDrawing ? directionalShadows : LightShadows.None;
            sunLight.shadowStrength = Mathf.Clamp01(
                (state.IsMoonPrimary ? nighttimeShadowStrength : daytimeShadowStrength) * weatherFactor);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = state.AmbientColor * weatherFactor;
            RenderSettings.sun = sunLight;

            ApplyFog(applyFog, state.AmbientColor * weatherFactor + ClearFogColor * 0.25f, fogDensity, submergedBlend);
        }

        // The single writer of RenderSettings.fog in the project. Weather and submersion both land
        // here so they cannot fight each other frame to frame.
        void ApplyFog(bool applyWeatherFog, Color weatherFogColor, float weatherFogDensity, float submergedBlend)
        {
            if (submergedBlend <= 0.0f)
            {
                RenderSettings.fog = applyWeatherFog;
                if (applyWeatherFog)
                {
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = weatherFogColor;
                    RenderSettings.fogDensity = weatherFogDensity;
                }

                return;
            }

            // Submerged: fog is forced on regardless of weather. Clear conditions produce a zero
            // density and `applyFog` false, which is precisely when the player would otherwise
            // swim through perfectly clear water, so this cannot be folded into the weather path.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(weatherFogColor, waterView.UnderwaterFogColor, submergedBlend);
            RenderSettings.fogDensity = Mathf.Lerp(
                applyWeatherFog ? weatherFogDensity : 0.0f,
                waterView.UnderwaterFogDensity,
                submergedBlend);
        }
    }
}
