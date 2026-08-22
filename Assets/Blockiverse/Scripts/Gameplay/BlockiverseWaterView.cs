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

        public float UnderwaterFogDensity => FogDensityFor(SubmergedFamily);

        // At density 0.35 an exp-squared fog reaches 39% at 2 m and 95% at 5 m: a strong,
        // distance-dependent underwater read that a flat tint cannot produce. Emberflow is dense
        // enough to be near-opaque at arm's length, which is the point.
        public static Color FogColorFor(FluidFamily family) => family switch
        {
            FluidFamily.Brine => new Color(0.08f, 0.26f, 0.26f),
            FluidFamily.Emberflow => new Color(0.45f, 0.10f, 0.03f),
            _ => new Color(0.12f, 0.30f, 0.42f)
        };

        public static float FogDensityFor(FluidFamily family) => family switch
        {
            FluidFamily.Brine => 0.42f,
            FluidFamily.Emberflow => 1.20f,
            _ => 0.35f
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
            return 1.0f;
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
