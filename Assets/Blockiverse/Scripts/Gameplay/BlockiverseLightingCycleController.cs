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

        // Two close strikes must not compound into a strobe, so a flash cannot retrigger inside
        // this window. It is longer than the flash itself for exactly that reason.
        public const float MinimumFlashRetriggerSeconds = 0.35f;

        // Once elapsed passes this, both the flash and the retrigger gate are long over; the timer
        // stops advancing so it cannot drift into float noise over a long session.
        const float FlashTrackingWindowSeconds = 1.0f;

        static readonly Color ClearFogColor = new(0.62f, 0.70f, 0.80f);

        // How fast the cloud deck drifts, in shader UV units per second.
        const float CloudScrollSpeed = 0.004f;

        [SerializeField] Material skyMaterial;
        Vector2 cloudScroll;

        // Sky-flash state lives HERE rather than on the bolt view, and is deliberately not pulled
        // from anywhere. ApplyCurrentLighting rewrites ambient every LateUpdate, so an external
        // component poking RenderSettings would be erased within a frame; and the bolt view is
        // created at runtime, after this component's Awake lookups have already run, so a pull
        // would find null forever.
        bool ownsSkyInstance;
        float skyFlashStrength;
        float skyFlashElapsed = FlashTrackingWindowSeconds;

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

            if (skyMaterial == null)
                skyMaterial = RenderSettings.skybox;

            EnsureRuntimeSkyInstance();
            ApplyCurrentLighting();
        }

        void LateUpdate()
        {
            if (skyFlashElapsed < FlashTrackingWindowSeconds)
                skyFlashElapsed += Time.deltaTime;

            ApplyCurrentLighting();
        }

        // Starts a sky flash at `strength` (0 = nothing, 1 = a strike close enough to wash out the
        // sky). Ignored while a recent flash is still inside the retrigger window.
        public void PulseSkyFlash(float strength)
        {
            if (strength <= 0.0f || skyFlashElapsed < MinimumFlashRetriggerSeconds)
                return;

            skyFlashStrength = Mathf.Clamp01(strength);
            skyFlashElapsed = 0.0f;
        }

        // What the flash is contributing right now, for tests and for on-device tracing.
        public float ActiveSkyFlashIntensity =>
            skyFlashStrength * LightningFlashSolver.Intensity(skyFlashElapsed);

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
            float cloudCoverage = 0f;
            if (environmentSource != null &&
                environmentSource.TryEvaluateEnvironment(WorldConstants.SeaLevel, out EnvironmentState environment))
            {
                weatherFactor = EnvironmentLightingSolver.WeatherLightFactor(worldTimeClock.NormalizedTime, environment);
                fogDensity = EnvironmentLightingSolver.FogDensity(environment);
                applyFog = fogDensity > 0f;
                cloudCoverage = Mathf.Clamp01(environment.CloudCoverage);
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

            // The flash modulates AMBIENT and never the sun. At night the sun sits below
            // MinimumShadowCastingIntensity, so raising it for two frames would flip the entire
            // shadow pass on and off -- a full shadow-caster sweep over every loaded chunk, with
            // every shadow in the scene snapping in and out. Ambient is Flat, so an additive term
            // is free and lifts everything uniformly, which is what a flash looks like anyway.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = LightningFlashSolver.AmbientBoost(
                state.AmbientColor * weatherFactor, skyFlashStrength, skyFlashElapsed);
            RenderSettings.sun = sunLight;

            ApplyFog(applyFog, state.AmbientColor * weatherFactor + ClearFogColor * 0.25f, fogDensity, submergedBlend);
            ApplySky(worldTimeClock.NormalizedTime, cloudCoverage);
        }

        // Drives the generated sky material. This exists because the stock procedural skybox
        // derives everything from the direction of RenderSettings.sun, and this project points one
        // shared light down from overhead at night so the ground stays lit -- so that skybox drew a
        // full noon sky at midnight behind a correctly dark world. It also had nowhere to put
        // clouds, which is why every weather state changed the light and left the sky untouched.
        //
        // Elevation comes from the CLOCK, never from the light's rotation, for exactly that reason.
        void ApplySky(float normalizedTime, float cloudCoverage)
        {
            // ONLY ever writes a material this component minted for itself.
            //
            // Outside play mode there is no instance, and skyMaterial falls back to
            // RenderSettings.skybox -- the GENERATED ASSET. Writing there dirties
            // BlockiverseSky.mat with whatever time of day and cloud-scroll offset the caller
            // happened to produce, which is how the committed asset ended up carrying Clear
            // weather's coverage and a mid-drift scroll. EditMode tests that call Configure() are
            // enough to trigger it: Awake runs on AddComponent in the editor, sees
            // Application.isPlaying == false, and skips the instance.
            //
            // Skipping the write outside play mode costs nothing real -- the asset keeps its
            // authored midday values, which is what a generated asset should show in the editor.
            if (!ownsSkyInstance || skyMaterial == null)
                return;

            float moonPhaseScale = MoonPhaseIndex / (float)EnvironmentLightComputer.FullMoonLightLevel;

            skyMaterial.SetColor(ZenithColorId, SkyGradientSolver.ZenithColor(normalizedTime, cloudCoverage, moonPhaseScale));
            skyMaterial.SetColor(HorizonColorId, SkyGradientSolver.HorizonColor(normalizedTime, cloudCoverage, moonPhaseScale));
            skyMaterial.SetColor(GroundColorId, SkyGradientSolver.GroundColor(normalizedTime, cloudCoverage, moonPhaseScale));
            skyMaterial.SetColor(SunColorId, SkyGradientSolver.SunDiskColor(normalizedTime, moonPhaseScale));
            skyMaterial.SetColor(CloudColorId, SkyGradientSolver.CloudColor(normalizedTime, cloudCoverage));
            skyMaterial.SetFloat(CloudCoverageId, cloudCoverage);

            // The disk follows the shared light so it lines up with the shadows it casts, but it
            // is hidden below the horizon by the colour solver rather than by rotation.
            if (sunLight != null)
                skyMaterial.SetVector(SunDirectionId, -sunLight.transform.forward);

            cloudScroll += new Vector2(CloudScrollSpeed, CloudScrollSpeed * 0.6f) * Time.deltaTime;
            skyMaterial.SetVector(CloudScrollId, cloudScroll);
        }

        public void ConfigureSky(Material sky)
        {
            if (sky != null)
                skyMaterial = sky;
        }

        // ApplySky writes the gradient, the cloud colour and a continuously advancing cloud scroll
        // into the sky material EVERY LateUpdate. Pointed at the generated .mat asset that is a
        // real defect rather than a cosmetic one: in the editor those writes land in the ASSET, so
        // every Play-mode session leaves BlockiverseSky.mat dirty carrying whatever time of day
        // and scroll offset the session happened to end on. A stray `git add -A` then bakes a
        // random cloud offset into the repo, and the bootstrapper's authored defaults are lost.
        //
        // So at runtime the controller drives its own instance and points RenderSettings at that.
        // The asset keeps the authored midday values and is never written to while playing.
        void EnsureRuntimeSkyInstance()
        {
            if (!Application.isPlaying || skyMaterial == null || ownsSkyInstance)
                return;

            skyMaterial = new Material(skyMaterial) { name = skyMaterial.name + " (Runtime)" };
            ownsSkyInstance = true;
            RenderSettings.skybox = skyMaterial;
        }

        void OnDestroy()
        {
            if (!ownsSkyInstance || skyMaterial == null)
                return;

            // Only the instance this component minted; the generated asset must survive.
            Destroy(skyMaterial);
            skyMaterial = null;
            ownsSkyInstance = false;
        }

        static readonly int ZenithColorId = Shader.PropertyToID("_ZenithColor");
        static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
        static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        static readonly int CloudCoverageId = Shader.PropertyToID("_CloudCoverage");
        static readonly int CloudScrollId = Shader.PropertyToID("_CloudScroll");

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
