using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Underwater presentation: exponential-squared fog plus a camera clear swap, and nothing else.
    //
    // Deliberately NOT a camera-attached tint quad. Every routed menu in this build is a world
    // space canvas on the interaction layer rendered by the same camera, so a near-clip quad would
    // draw over the pause and quit routes and tint them at emberflow's density -- taking away the
    // escape hatch from the exact situation that is hurting the player. Fog is not applied to
    // canvas UI, so menus and the survival HUD stay legible underwater, which is a feature.
    //
    // This component owns the sampling and the camera clear. The fog write lives in
    // BlockiverseLightingCycleController because that is the one place RenderSettings.fog is
    // written, and two writers would fight for it every frame.
    [DisallowMultipleComponent]
    public sealed class BlockiverseWaterView : MonoBehaviour
    {
        // Long enough not to snap, short enough that surfacing for air feels responsive.
        public const float SubmergeBlendSeconds = 0.25f;

        // The eye must cross this far past a cell boundary before the state flips, so bobbing
        // exactly at the waterline cannot strobe the whole screen.
        public const float SubmergeHysteresisMeters = 0.02f;

        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] Camera headCamera;

        Camera overriddenCamera;
        CameraClearFlags cachedClearFlags;
        Color cachedBackgroundColor;
        BlockiverseBubbleVolume bubbleVolume;
        bool clearFlagsOverridden;

        // 0 = fully above water, 1 = fully submerged. Fades across SubmergeBlendSeconds.
        public float SubmergedBlend { get; private set; }

        // The family being blended toward or away from; meaningful whenever SubmergedBlend > 0.
        public FluidFamily SubmergedFamily { get; private set; }

        public bool IsSubmerged => SubmergedBlend > 0.0f;

        public BlockiverseBubbleVolume BubbleVolume => bubbleVolume;

        public Color UnderwaterFogColor => FogColorFor(SubmergedFamily);

        // Scaled by depth — see FogDepthRampMeters. Only at or past the ramp floor is this the
        // family's full FogDensityFor value; nearer the surface it is a fraction of it, which is
        // the whole point. The PlayMode fog assertions therefore compare against THIS property
        // rather than the raw family constant, because the raw constant is no longer what a
        // submerged eye gets at arbitrary depth.
        public float UnderwaterFogDensity
        {
            get
            {
                float density = FogDensityFor(SubmergedFamily);

                // The ramp is a WATER model — light failing with depth. Molten rock is opaque at
                // any depth, and every emberflow pool that worldgen actually places is shallow,
                // so ramping it made lava effectively transparent everywhere it exists. The
                // "start almost clear at the surface" ruling was about swimming, not standing in
                // lava.
                if (SubmergedFamily == FluidFamily.Emberflow)
                    return density;

                return density * Mathf.Lerp(
                    MinimumDepthDensityScale,
                    1.0f,
                    Mathf.Clamp01(EyeDepthMeters / FogDepthRampMeters));
            }
        }

        // Exp-squared fog: opacity is 1 - exp(-(density * distance)^2), so halving the density
        // roughly doubles the distance at which the water closes in.
        //
        // Freshwater was 0.35 (95% opaque at 5 m) and Brine 0.42 (95% at 4.1 m), which Eric found
        // too restrictive on device. At 0.22 freshwater reaches 95% at about 7.9 m and brine at
        // 0.28 reaches it at about 6.2 m — further, while keeping brine murkier than freshwater,
        // which is the ordering the ruleset cares about. Emberflow is untouched: being near-opaque
        // at arm's length is the entire point of standing in molten rock.
        //
        // Fog was only half the cause. The tunnelling vignette was engaged for the whole dive
        // (see BlockiverseSwimProvider.UpdateVignette) and narrowed the field of view on top of
        // this; changing density alone would have moved the number without fixing what was seen.
        //
        // These values are chosen from the opacity arithmetic, not measured in the headset. If
        // they are still wrong, the arithmetic is not the thing to re-derive — look at it on
        // device, the way the title menu's height was settled.
        public static Color FogColorFor(FluidFamily family) => family switch
        {
            FluidFamily.Brine => new Color(0.08f, 0.26f, 0.26f),
            FluidFamily.Emberflow => new Color(0.45f, 0.10f, 0.03f),
            _ => new Color(0.12f, 0.30f, 0.42f)
        };

        public static float FogDensityFor(FluidFamily family) => family switch
        {
            FluidFamily.Brine => 0.28f,
            FluidFamily.Emberflow => 1.20f,
            _ => 0.22f
        };

        // Null-tolerant on both arguments: the bootstrapper wires this at scene-generation time,
        // when the world manager may not exist in the scene yet, and Awake resolves what is missing.
        public void Configure(CreativeWorldManager manager, Camera camera)
        {
            if (manager != null)
                worldManager = manager;

            if (camera != null)
                headCamera = camera;
        }

        void Awake()
        {
            ResolveDependencies();
        }

        // Update, not LateUpdate: BlockiverseLightingCycleController reads SubmergedBlend from its
        // own LateUpdate, and Update always runs first, so fog never lags the sample by a frame.
        void Update()
        {
            ResolveDependencies();

            // Boot.unity never unloads, so New World, Load and Return-to-Title do not fire
            // OnDisable or OnDestroy here -- the world simply goes away underneath this component.
            // Fading out over a quarter second there would tint the title menu on the way back, so
            // losing the world snaps the view clear in the same frame.
            if (!HasWorld())
            {
                SubmergedBlend = 0.0f;
                RestoreCameraClear();
                UpdateBubbles();
                return;
            }

            float target = EvaluateSubmergedTarget();
            float step = Time.deltaTime / SubmergeBlendSeconds;
            SubmergedBlend = Mathf.MoveTowards(SubmergedBlend, target, step);

            ApplyCameraClear();
            UpdateBubbles();
        }

        // Bubbles rise past a submerged player. Driven from here because this component already
        // owns the submersion blend and the family, and driving them off the blend rather than a
        // boolean means they fade in with the rest of the underwater treatment instead of
        // snapping on at the waterline.
        void UpdateBubbles()
        {
            if (bubbleVolume == null)
            {
                // Nothing to create until there is something to be submerged in.
                if (SubmergedBlend <= 0.0f)
                    return;

                Camera camera = headCamera != null ? headCamera : Camera.main;

                if (camera == null)
                    return;

                // Created at runtime and parented to nothing: it follows the head in POSITION
                // only. Parenting would inherit rotation and drag the whole column round on every
                // snap turn.
                var host = new GameObject("Bubble Volume");
                bubbleVolume = host.AddComponent<BlockiverseBubbleVolume>();

                BlockiverseVfxPool pool = FindFirstObjectByType<BlockiverseVfxPool>(FindObjectsInactive.Include);

                bubbleVolume.Configure(
                    camera.transform,
                    pool != null ? pool.ParticleMaterial : null,
                    pool != null ? pool.BubbleSprite : null);
            }

            bubbleVolume.SetSubmerged(SubmergedBlend, SubmergedFamily);
        }

        bool HasWorld() => worldManager != null && worldManager.World != null;

        // Same ownership problem as the weather volume: created at runtime, parented to nothing so
        // a snap turn cannot drag it round, and therefore owned by nobody unless this says so.
        void OnDestroy()
        {
            if (bubbleVolume == null)
                return;

            GameObject host = bubbleVolume.gameObject;
            bubbleVolume = null;

            if (Application.isPlaying)
                Destroy(host);
            else
                DestroyImmediate(host);
        }

        void OnDisable()
        {
            SubmergedBlend = 0.0f;
            RestoreCameraClear();
        }

        void ResolveDependencies()
        {
            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>();

            // Camera.main is re-resolved whenever it goes missing rather than cached once: the rig
            // is generated, and PlayMode fixtures swap cameras in and out under this component.
            if (headCamera == null)
                headCamera = Camera.main;
        }

        float EvaluateSubmergedTarget()
        {
            if (headCamera == null)
                return 0.0f;

            // Asymmetric probe, and the sign matters: while dry the probe sits ABOVE the eye, so
            // the eye must be a full hysteresis under the surface before it reads as water; while
            // submerged it sits BELOW, so the eye must clear the surface by the same margin before
            // it reads as air. Biasing the other way overlaps the two thresholds instead of
            // separating them, which turns a head bobbing at the waterline into a screen-wide
            // strobe -- the opposite of what the hysteresis is for.
            float bias = IsSubmerged ? -SubmergeHysteresisMeters : SubmergeHysteresisMeters;
            Vector3 eye = headCamera.transform.position;
            eye.y += bias;

            if (!worldManager.TryGetFluidFamilyAt(eye, out FluidFamily family))
                return 0.0f;

            SubmergedFamily = family;
            EyeDepthMeters = EvaluateEyeDepth(headCamera.transform.position);
            return 1.0f;
        }

        /// <summary>
        /// How far the eye sits below the water line, capped at <see cref="FogDepthRampMeters"/>.
        /// </summary>
        public float EyeDepthMeters { get; private set; }

        // Depth at which the fog reaches its family's full density, and the probe granularity.
        //
        // Eric: "I can see much further out of the water looking down into it than I can from
        // inside the water while swimming, which doesn't make sense logically." He is right, and
        // the cause is that the two views were never one model. Looking IN from above, the surface
        // is an alpha-blended quad with a constant tint and no distance term at all, so the bottom
        // stays crisp. Looking OUT from under, fog was applied at full family density the instant
        // the eye crossed the line — exponential in distance but BINARY in depth. Ducking your head
        // ten centimetres under therefore swapped a perfectly clear view for a heavily fogged one.
        //
        // Ramping density with depth makes the two agree where they meet: at the surface the view
        // is nearly as open as it looks from above, and it closes in as you actually descend, which
        // is both what water does and what the player expects. It is not a substitute for the
        // density tuning below — it fixes the DISCONTINUITY, not the reach.
        public const float FogDepthRampMeters = 14.0f;
        const float DepthProbeStepMeters = 1.0f;

        // Near-clear at the line, and only closing in with real depth.
        //
        // Eric's ruling after trying the first version: the depth falloff is right and reads as
        // light failing to reach the deep, but it started FAR too murky and reached full density
        // over 1.5 m — so every swim was fogged from the first moment. Start almost clear at the
        // surface and let the ramp run over a swimmable depth instead.
        //
        // 0.08 of freshwater's 0.22 is ~0.018, which puts 95% opacity near 100 m: effectively
        // clear. At the 14 m floor it is the full 0.22, i.e. 95% at ~7.9 m.
        public const float MinimumDepthDensityScale = 0.08f;

        float EvaluateEyeDepth(Vector3 eye)
        {
            // Probes upward only as far as the ramp needs — a handful of samples, not a search
            // for the real surface, which could be many metres up in a deep sea and is not worth
            // finding.
            //
            // CONTINUOUS, deliberately: an earlier version returned the raw probe step count,
            // which quantised depth to whole metres — density then jumped a visible notch each
            // metre of descent. The probes still walk cell by cell, but the returned depth is
            // measured to the TOP FACE of the topmost fluid cell found (cells sit on integer
            // boundaries), so it tracks the eye smoothly between probes.
            // The submersion decision uses a slightly BIASED sample (hysteresis against
            // strobing at the line), so during the exit band the raw eye can sit in the air
            // cell above the water. Seeding highestFluidY with that air position would make
            // floor(eye.y)+1 the top of the AIR cell — depth would jump from ~0 to ~1 m at the
            // exact moment of surfacing, the discontinuity this method exists to remove. An eye
            // above the fluid is at depth zero, full stop.
            if (!worldManager.TryGetFluidFamilyAt(eye, out _))
                return 0.0f;

            float highestFluidY = eye.y;

            for (float step = DepthProbeStepMeters; step <= FogDepthRampMeters; step += DepthProbeStepMeters)
            {
                Vector3 probe = eye;
                probe.y += step;

                if (!worldManager.TryGetFluidFamilyAt(probe, out _))
                    return Mathf.Min(Mathf.Floor(highestFluidY) + 1.0f - eye.y, FogDepthRampMeters);

                highestFluidY = probe.y;
            }

            return FogDepthRampMeters;
        }

        void ApplyCameraClear()
        {
            if (headCamera == null)
            {
                RestoreCameraClear();
                return;
            }

            if (!IsSubmerged)
            {
                RestoreCameraClear();
                return;
            }

            if (!clearFlagsOverridden || overriddenCamera != headCamera)
            {
                RestoreCameraClear();
                overriddenCamera = headCamera;
                cachedClearFlags = headCamera.clearFlags;
                cachedBackgroundColor = headCamera.backgroundColor;
                clearFlagsOverridden = true;
            }

            // URP does not fog the skybox, so a skybox clear would leave a crisp horizon visible
            // from the bottom of a lake. Clearing to the fog colour is what closes the world off.
            //
            // The colour is set outright rather than faded up from the cached background: that
            // cached value is whatever the camera happens to carry behind a skybox clear, has never
            // been on screen, and fading through it would pop the sky to an arbitrary colour on the
            // first submerged frame. The clear swap is tied to crossing the surface, which is the
            // moment the player expects the view to change.
            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = UnderwaterFogColor;
        }

        void RestoreCameraClear()
        {
            if (!clearFlagsOverridden)
                return;

            if (overriddenCamera != null)
            {
                overriddenCamera.clearFlags = cachedClearFlags;
                overriddenCamera.backgroundColor = cachedBackgroundColor;
            }

            overriddenCamera = null;
            clearFlagsOverridden = false;
        }
    }
}
