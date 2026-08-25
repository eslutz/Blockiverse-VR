using Blockiverse.Core;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Rain and snow you can actually see.
    //
    // Precipitation used to be drawn by the same one-shot burst pool the block-break puffs use:
    // two particles every 0.6 seconds, 4.5 cm across, 0.35 s long, spawned at a random offset
    // around the head. Measured, that is 1.17 raindrops on screen on average, subtending about
    // 0.7 degrees, with the screen completely empty of rain 42% of the time -- against a spec
    // asking for 150-250. Worse, the pool never set simulationSpace, so every particle inherited
    // Local and was rigidly glued to the XR origin: "rain" drifted sideways at 0.25 m/s, never
    // fell, and swung 45 degrees with every snap turn. The flicker in the corner of the eye was
    // this: a burst appearing outside the forward view and dying 0.35 s later.
    //
    // The standard VR answer, and what this is: ONE continuous emitter parented to the head, in
    // WORLD simulation space, raining into a box the player carries with them. World space is
    // what lets drops fall past you instead of travelling with you; parenting to the head is what
    // keeps the volume populated wherever you walk without simulating weather for the whole map.
    [DisallowMultipleComponent]
    public sealed class BlockiverseWeatherVolume : MonoBehaviour
    {
        // The box the player carries. At 14x14 the edge sat 7 m away on every side, close enough
        // to look past — precipitation visibly stopped a few paces out. 30 m puts the boundary
        // beyond where the eye reads individual drops, and rain-weather fog now covers the rest.
        public const float VolumeWidthMeters = 30.0f;
        public const float VolumeDepthMeters = 30.0f;
        public const float SpawnHeightMeters = 12.0f;

        // How far a particle travels before it dies, INDEPENDENT of where it spawned.
        //
        // Lifetime used to be SpawnHeight/speed, i.e. exactly enough to fall back to the height it
        // started from relative to the head — so drops died about a metre BELOW eye level and
        // precipitation visibly stopped just under you whenever you stood anywhere elevated. It
        // has to outrun the drop to the ground, not to your feet.
        public const float FallDistanceMeters = 40.0f;

        // Emission at full intensity.
        public const float MaxRainParticlesPerSecond = 420.0f;
        public const float MaxSnowParticlesPerSecond = 260.0f;

        public const float RainFallSpeed = 9.0f;

        // Snow speed scales with intensity: a flurry drifts, a blizzard drives. Rain does not —
        // real rain reaches terminal velocity almost immediately regardless of how hard it falls.
        public const float SnowFallSpeedLight = 2.0f;
        public const float SnowFallSpeedHeavy = 3.6f;

        // Size scales with intensity too. A light shower made of fat drops reads as heavy rain no
        // matter how few of them there are, which is what "the raindrops are too heavy" was.
        public const float RainSizeLight = 0.020f;
        public const float RainSizeHeavy = 0.042f;
        public const float SnowSizeLight = 0.065f;
        public const float SnowSizeHeavy = 0.105f;

        // Headroom for the worst case: blizzard snow is slow enough to stay alive ~11 s, so the
        // live count is far higher than rain's despite a lower rate.
        public const int MaxLiveParticles = 3000;

        // Ground effect: rain splashes, and snow that drifts along the surface. One emitter serves
        // both because they are the same thing structurally — a flat disc of short-lived sprites
        // on the ground the player is standing over.
        //
        // Deliberately NOT driven by per-particle collision. Collision on ~1900 live drops is far
        // too expensive on a tile GPU even at Unity's lowest quality, and a statistical scatter
        // over the same footprint is indistinguishable once drops are landing several times a
        // second per square metre.
        public const float GroundEffectRadiusMeters = 9.0f;
        public const float MaxSplashesPerSecond = 260.0f;
        public const float MaxGroundSnowPerSecond = 90.0f;
        public const float SplashLifetimeSeconds = 0.35f;
        public const int MaxLiveGroundParticles = 260;
        // How far down to look for the surface the effect sits on.
        public const float GroundProbeMeters = 40.0f;

        // Sideways drive at full intensity. A blizzard that falls straight down is just heavy
        // snow; the horizontal component is most of what reads as "blizzard".
        public const float BlizzardWindMetersPerSecond = 5.5f;

        // Seconds to cross most of the way to a new emission rate. Weather changes should arrive
        // as a front, not as a switch being thrown.
        public const float RampSeconds = 2.5f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        ParticleSystem groundParticles;
        ParticleSystemRenderer groundRenderer;
        Transform groundTransform;
        float groundY;
        bool hasGroundY;
        Transform head;
        PrecipitationKind activeKind = PrecipitationKind.None;
        float activeIntensity = -1.0f;
        float targetRate;
        float groundTargetRate;
        float currentRate;

        public PrecipitationKind ActiveKind => activeKind;

        public float CurrentEmissionRate => currentRate;

        public void Configure(Transform headTransform, Material particleMaterial, Sprite rainSprite, Sprite snowSprite)
        {
            head = headTransform;
            EnsureParticles(particleMaterial);
            rain = rainSprite;
            snow = snowSprite;
        }

        Sprite rain;
        Sprite snow;

        // Sets what is falling and how hard. `intensity` is the weather service's 0..1.
        public void SetPrecipitation(PrecipitationKind kind, float intensity)
        {
            intensity = Mathf.Clamp01(intensity);

            // Re-apply on an intensity change too, not just a kind change: size and fall speed
            // both scale with intensity now, and applying them only when the KIND changed left a
            // blizzard rendering with flurry-sized flakes at flurry speed.
            if (kind != activeKind || !Mathf.Approximately(intensity, activeIntensity))
            {
                activeKind = kind;
                activeIntensity = intensity;
                ApplyKind(kind, intensity);
            }

            targetRate = kind switch
            {
                PrecipitationKind.Rain => MaxRainParticlesPerSecond * intensity,
                PrecipitationKind.Snow => MaxSnowParticlesPerSecond * intensity,
                _ => 0.0f,
            };
        }

        void LateUpdate()
        {
            if (particles == null)
                return;

            // Follows the head in position only. Inheriting rotation would spin the whole volume
            // on every snap turn, which is the artefact the old implementation had.
            if (head != null)
                transform.position = head.position;

            currentRate = Mathf.MoveTowards(
                currentRate, targetRate, (MaxRainParticlesPerSecond / RampSeconds) * Time.deltaTime);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = currentRate;

            bool shouldPlay = currentRate > 0.01f;

            if (shouldPlay && !particles.isPlaying)
                particles.Play();
            else if (!shouldPlay && particles.isPlaying)
                particles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);

            UpdateGroundHeight();

            if (groundParticles != null)
            {
                // Scaled by the SAME ramp fraction as the falling particles, so the ground effect
                // arrives and leaves with the weather front instead of snapping on.
                float rampFraction = targetRate > 0.01f ? Mathf.Clamp01(currentRate / targetRate) : 0.0f;
                ParticleSystem.EmissionModule groundEmission = groundParticles.emission;
                groundEmission.rateOverTime = groundTargetRate * rampFraction;

                bool groundShouldPlay = groundTargetRate * rampFraction > 0.01f;

                if (groundShouldPlay && !groundParticles.isPlaying)
                    groundParticles.Play();
                else if (!groundShouldPlay && groundParticles.isPlaying)
                    groundParticles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void ApplyKind(PrecipitationKind kind, float intensity)
        {
            if (particles == null)
                return;

            bool isSnow = kind == PrecipitationKind.Snow;

            float fallSpeed = isSnow
                ? Mathf.Lerp(SnowFallSpeedLight, SnowFallSpeedHeavy, intensity)
                : RainFallSpeed;

            ParticleSystem.MainModule main = particles.main;
            main.startSpeed = fallSpeed;
            main.startSize = isSnow
                ? Mathf.Lerp(SnowSizeLight, SnowSizeHeavy, intensity)
                : Mathf.Lerp(RainSizeLight, RainSizeHeavy, intensity);
            // Distance-based, so a drop keeps falling until it is well below the player rather
            // than dying level with them.
            main.startLifetime = FallDistanceMeters / fallSpeed;
            main.startColor = isSnow
                ? new Color(1.0f, 1.0f, 1.0f, 0.85f)
                : new Color(0.62f, 0.78f, 0.95f, 0.55f);

            // Snow drifts; rain does not. Rain that wanders reads as ash.
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = isSnow;
            noise.strength = isSnow ? Mathf.Lerp(0.55f, 1.6f, intensity) : 0.55f;
            noise.frequency = 0.25f;

            // Horizontal drive, scaled by intensity. Snow that falls straight down is just heavy
            // snow no matter how much of it there is -- the sideways component is most of what
            // makes a blizzard read as a blizzard, and it costs one module.
            ParticleSystem.VelocityOverLifetimeModule drift = particles.velocityOverLifetime;
            drift.enabled = isSnow;
            drift.space = ParticleSystemSimulationSpace.World;
            float wind = isSnow ? BlizzardWindMetersPerSecond * intensity : 0.0f;
            drift.x = new ParticleSystem.MinMaxCurve(wind * 0.55f, wind);
            drift.z = new ParticleSystem.MinMaxCurve(-wind * 0.3f, wind * 0.3f);

            groundTargetRate = ApplyGroundKind(kind, intensity);

            if (particleRenderer != null)
            {
                // Rain is a stretched streak, which is most of what makes it read as rain rather
                // than as floating dots. Snow stays a billboard.
                particleRenderer.renderMode = isSnow
                    ? ParticleSystemRenderMode.Billboard
                    : ParticleSystemRenderMode.Stretch;
                particleRenderer.velocityScale = isSnow ? 0.0f : 0.06f;
                particleRenderer.lengthScale = isSnow ? 1.0f : 2.5f;

                Sprite sprite = isSnow ? snow : rain;

                if (sprite != null && sprite.texture != null)
                {
                    propertyBlock ??= new MaterialPropertyBlock();
                    propertyBlock.Clear();
                    particleRenderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetTexture(BaseMapId, sprite.texture);
                    propertyBlock.SetTexture(MainTexId, sprite.texture);
                    particleRenderer.SetPropertyBlock(propertyBlock);
                }
            }
        }

        MaterialPropertyBlock propertyBlock;

        void EnsureParticles(Material particleMaterial)
        {
            if (particles != null)
                return;

            // NOT `??`. The null-coalescing operator uses plain reference equality, which bypasses
            // UnityEngine.Object's overloaded == -- so a destroyed-or-missing component comes back
            // as a "fake null" that ?? happily accepts, AddComponent never runs, and the first
            // module write throws "Do not create your own module instances".
            ParticleSystem existing = gameObject.GetComponent<ParticleSystem>();
            particles = existing != null ? existing : gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;

            // WORLD, not Local. This is the single most important line in the file: in Local space
            // every drop is welded to the XR origin and travels and rotates with the player, so
            // nothing ever falls past them.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxLiveParticles;
            main.gravityModifier = 0.0f;
            main.startRotation3D = false;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Axis order is (width, DEPTH, thickness) and not the obvious (width, thickness, depth)
            // because shape.rotation below rotates the box GEOMETRY as well as the emission
            // direction. Unity builds the shape transform as translate * rotate * scale, so a
            // +90 degrees rotation about X maps local Y onto world Z and local Z onto world -Y.
            //
            // Written the obvious way, the intended 14x14 ceiling came out as a 14 wide by 14 tall
            // by 0.1 thin VERTICAL sheet standing at z=0 — a curtain passing left-to-right through
            // the player's own head, seen edge-on as a single line of rain directly ahead. That is
            // exactly what shipped: "the rain is coming down in a line instead of spread across
            // the space of the player's FOV".
            //
            // Swapping the last two components makes the post-rotation box the horizontal ceiling
            // that was always intended: world X = width, world Y = 0.1 (thin), world Z = depth.
            shape.scale = new Vector3(VolumeWidthMeters, VolumeDepthMeters, 0.1f);
            // NOT rotated by shape.rotation — position is applied in the system's local space — so
            // this stays a ceiling SpawnHeightMeters above the head rather than sliding forward.
            shape.position = new Vector3(0.0f, SpawnHeightMeters, 0.0f);
            // Straight down. The default cone fires along local +Z, i.e. sideways, which is what
            // the burst implementation was silently doing.
            shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;

            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;

            particleRenderer = gameObject.GetComponent<ParticleSystemRenderer>();

            if (particleRenderer != null)
            {
                if (particleMaterial != null)
                    particleRenderer.sharedMaterial = particleMaterial;

                particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;
                particleRenderer.alignment = ParticleSystemRenderSpace.View;
            }

            EnsureGroundParticles(particleMaterial);
            ApplyKind(activeKind, Mathf.Max(activeIntensity, 0.0f));
        }

        // A flat disc of short-lived sprites sitting on whatever surface is under the player.
        void EnsureGroundParticles(Material particleMaterial)
        {
            if (groundParticles == null)
            {
                Transform existing = transform.Find(GroundEffectObjectName);
                GameObject host;

                if (existing != null)
                {
                    host = existing.gameObject;
                }
                else
                {
                    host = new GameObject(GroundEffectObjectName);
                    host.transform.SetParent(transform, worldPositionStays: false);
                }

                groundTransform = host.transform;
                ParticleSystem found = host.GetComponent<ParticleSystem>();
                groundParticles = found != null ? found : host.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = groundParticles.main;
            main.playOnAwake = false;
            main.loop = true;
            // World space for the same reason the falling particles use it: a splash belongs to the
            // ground it landed on, not to the player who walked away from it.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxLiveGroundParticles;
            main.gravityModifier = 0.0f;
            main.startLifetime = SplashLifetimeSeconds;

            ParticleSystem.ShapeModule shape = groundParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = GroundEffectRadiusMeters;
            // Lay the disc flat. Circle emits in its local XY plane, so it needs the same +90 about
            // X that the ceiling box needs — and unlike the box its scale is uniform, so there is
            // no axis-order trap here.
            shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);

            ParticleSystem.EmissionModule emission = groundParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;

            groundRenderer = groundParticles.GetComponent<ParticleSystemRenderer>();

            if (groundRenderer != null)
            {
                if (particleMaterial != null)
                    groundRenderer.sharedMaterial = particleMaterial;

                // Flat against the ground rather than facing the camera: a splash ring seen
                // edge-on as a billboard reads as a floating tick mark.
                groundRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                groundRenderer.receiveShadows = false;
            }
        }

        // Sets the ground effect's kind-specific look and returns its emission rate.
        float ApplyGroundKind(PrecipitationKind kind, float intensity)
        {
            if (groundParticles == null)
                return 0.0f;

            bool isSnow = kind == PrecipitationKind.Snow;

            ParticleSystem.MainModule main = groundParticles.main;
            main.startLifetime = isSnow ? SplashLifetimeSeconds * 3.0f : SplashLifetimeSeconds;
            main.startSize = isSnow ? 0.16f : Mathf.Lerp(0.05f, 0.11f, intensity);
            // Splashes pop upward a little; ground snow is blown sideways by the same wind that
            // drives the falling flakes, so the two agree instead of looking like separate systems.
            main.startSpeed = isSnow ? BlizzardWindMetersPerSecond * intensity : 0.5f;
            main.startColor = isSnow
                ? new Color(1.0f, 1.0f, 1.0f, 0.5f * intensity)
                : new Color(0.78f, 0.88f, 1.0f, 0.5f);

            ParticleSystem.ShapeModule shape = groundParticles.shape;
            // Snow blows outward along the surface; splashes rise from where they land.
            shape.rotation = isSnow ? new Vector3(0.0f, 0.0f, 0.0f) : new Vector3(90.0f, 0.0f, 0.0f);

            return kind switch
            {
                PrecipitationKind.Rain => MaxSplashesPerSecond * intensity,
                // Ground snow only reads once it is actually blowing, so it ramps in late.
                PrecipitationKind.Snow => MaxGroundSnowPerSecond * Mathf.Max(0.0f, intensity - 0.35f) / 0.65f,
                _ => 0.0f,
            };
        }

        // Keeps the disc on the surface under the player. One raycast per frame, against the same
        // mask gravity uses, so it lands on terrain and ignores fluid and passable vegetation.
        void UpdateGroundHeight()
        {
            if (head == null || groundTransform == null)
                return;

            Vector3 origin = head.position;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundProbeMeters,
                    BlockiverseProject.VoxelGroundLayerMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                hasGroundY = true;
            }

            // Keep the last known height when the probe misses (mid-air, over a void) rather than
            // snapping the whole effect to the player's feet.
            groundTransform.position = new Vector3(
                origin.x, hasGroundY ? groundY + GroundEffectLiftMeters : origin.y, origin.z);
        }

        const string GroundEffectObjectName = "Weather Ground Effect";
        // Just clear of the surface so the sprites are not z-fighting the block face they sit on.
        const float GroundEffectLiftMeters = 0.03f;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
