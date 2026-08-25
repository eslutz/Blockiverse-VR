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

        // How fast the cloud deck drifts, in shader UV units per second.
        // Planar units per second. At 0.004 one noise cell (~1 planar unit) took over four minutes
        // to pass overhead, so the deck read as painted on rather than drifting. 0.02 moves a cell
        // in ~50 s: visible motion when you watch for it, not distracting when you do not.
        const float CloudScrollSpeed = 0.02f;

        [SerializeField] Material skyMaterial;

        // What share of the coverage signal the skybox veil takes. The rest belongs to the
        // geometry deck; see ApplySky.
        public const float SkyVeilShare = 0.35f;

        BlockiverseCloudDeck cloudDeck;
        BlockiverseHorizonSkirt horizonSkirt;
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

            // THE AERIAL COLOUR: what anything infinitely far away looks like. Fog, the skybox's
            // below-horizon band, the cloud deck's rim and the horizon skirt's rim are all driven
            // from this ONE value, and that is the point of it.
            //
            // It used to be a blend of the ambient tint toward the horizon (0.65 of the way), on
            // the reasoning that distant terrain should melt into the sky. Correct as far as it
            // went — but the skybox is deliberately never fogged (Background queue, no MixFog),
            // so every surface that has to disappear against the sky disappears against a colour
            // slightly different from its own, and each such surface draws a seam. With three of
            // them now (fogged terrain, the deck rim, the skirt rim) meeting an unfogged sky, any
            // deviation at all is visible somewhere. The sky's own horizon colour is the only
            // choice that makes all four agree, and it already carries time of day and the
            // overcast darkening, so nothing is lost by dropping the separate tint.
            Color skyHorizon = SkyGradientSolver.HorizonColor(
                worldTimeClock.NormalizedTime,
                cloudCoverage,
                MoonPhaseIndex / (float)EnvironmentLightComputer.FullMoonLightLevel);
            ApplyFog(applyFog, skyHorizon, fogDensity, submergedBlend);
            ApplySky(worldTimeClock.NormalizedTime, cloudCoverage, skyHorizon);
        }

        // Drives the generated sky material. This exists because the stock procedural skybox
        // derives everything from the direction of RenderSettings.sun, and this project points one
        // shared light down from overhead at night so the ground stays lit -- so that skybox drew a
        // full noon sky at midnight behind a correctly dark world. It also had nowhere to put
        // clouds, which is why every weather state changed the light and left the sky untouched.
        //
        // Elevation comes from the CLOCK, never from the light's rotation, for exactly that reason.
        /// <summary>Attaches the geometry cloud deck this controller drives. Optional: with no deck
        /// the skybox simply keeps a larger share of the coverage, which is the pre-deck look.</summary>
        public void ConfigureCloudDeck(BlockiverseCloudDeck deck)
        {
            cloudDeck = deck;
        }

        /// <summary>Attaches the sea-level horizon plane this controller recolours. Optional: with
        /// no skirt the world simply ends at its own edge, which is the pre-skirt look.</summary>
        public void ConfigureHorizonSkirt(BlockiverseHorizonSkirt skirt)
        {
            horizonSkirt = skirt;
        }

        void ApplySky(float normalizedTime, float cloudCoverage, Color aerial)
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
            // Below the horizon is, by definition, infinitely distant ground, so it takes the
            // aerial colour like everything else at that distance. It used to have its own opinion
            // (a flat mid grey) which is what the world's edge stood against as a visible cliff in
            // a visible void; the horizon skirt now covers most of that band and matches its far
            // rim to this, so the two have to be the same colour or the skirt's rim is a line.
            skyMaterial.SetColor(GroundColorId, aerial);
            skyMaterial.SetColor(SunColorId, SkyGradientSolver.SunDiskColor(normalizedTime, moonPhaseScale));
            skyMaterial.SetColor(CloudColorId, SkyGradientSolver.CloudColor(normalizedTime, cloudCoverage));

            // Coverage is SPLIT between the two layers, never applied twice. The geometry deck
            // carries the weather; the skybox keeps a thin high veil that thickens only slightly.
            // Driving both from the raw value would stack an opaque deck under an opaque veil and
            // read as muddy soup rather than as overcast.
            skyMaterial.SetFloat(CloudCoverageId, cloudCoverage * SkyVeilShare);

            if (cloudDeck != null)
            {
                Color deckColor = SkyGradientSolver.CloudColor(normalizedTime, cloudCoverage);

                // Underside darker than the top, which is most of what gives a flat-bottomed deck
                // its volume from below — the angle a player on the ground always sees it from.
                //
                // These are LITERAL colours now. Through the lit path they were not: vertex colour
                // there is baked light data, so the deck rendered as its white texel at a
                // brightness derived from these, and every weather state drew the same white cloud
                // slightly dimmer or brighter. The sky shader variant is unlit and takes the
                // vertex colour as written, so the storm grey in CloudColor finally arrives — and
                // the top/underside split has to carry its own contrast rather than borrowing the
                // sun's, hence the darkening here.
                Color underside = Color.Lerp(
                    new Color(deckColor.r * 0.70f, deckColor.g * 0.70f, deckColor.b * 0.72f, 1.0f),
                    aerial,
                    0.25f);

                // The fourth colour is what the deck's rim dissolves into, and it is the aerial
                // colour rather than another cloud tone for the same reason the skirt's is.
                cloudDeck.SetSky(cloudCoverage, deckColor, underside, aerial);
            }

            if (horizonSkirt != null)
                horizonSkirt.SetSky(aerial);

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
            // Same guard, and the same reason, as ApplySky above: RenderSettings fog is SERIALISED
            // INTO THE SCENE, so writing it outside play mode bakes whatever weather the caller
            // happened to be simulating into Boot.unity and MultiplayerTest.unity, and the
            // bootstrapper's scene save commits it.
            //
            // This was invisible until fog became unconditional. Before that the write was
            // `RenderSettings.fog = applyWeatherFog`, which is false in clear weather and therefore
            // matched the scenes' committed `m_Fog: 0` -- a no-op that left no diff. Making fog
            // always-on turned the same code path into a guaranteed two-scene diff on every build,
            // with a computed fog colour whose alpha was above 1, which no one would author.
            //
            // Runtime lighting belongs to the runtime. The scenes keep their authored baseline.
            if (!Application.isPlaying)
                return;

            if (submergedBlend <= 0.0f)
            {
                // Unconditional. EnvironmentLightingSolver.FogDensity now floors at
                // ClearAirDensity, so there is always some aerial perspective; gating on
                // "density > 0" previously switched fog off completely in Clear, PartlyCloudy and
                // Overcast — the three longest-dwelling weather states.
                RenderSettings.fog = true;
                // Exponential, not ExponentialSquared: the squared term is ~flat for the first
                // tens of metres and then knees, which put a haze BAND out at the world edge with
                // clear air near the player instead of a gradient starting at arm's length.
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = weatherFogColor;
                RenderSettings.fogDensity = weatherFogDensity;

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
