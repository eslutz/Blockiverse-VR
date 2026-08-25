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

        // Seconds to cross most of the way to a new emission rate. Weather changes should arrive
        // as a front, not as a switch being thrown.
        public const float RampSeconds = 2.5f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        Transform head;
        PrecipitationKind activeKind = PrecipitationKind.None;
        float activeIntensity = -1.0f;
        float targetRate;
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
            noise.strength = 0.55f;
            noise.frequency = 0.25f;

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

            ApplyKind(activeKind, Mathf.Max(activeIntensity, 0.0f));
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
