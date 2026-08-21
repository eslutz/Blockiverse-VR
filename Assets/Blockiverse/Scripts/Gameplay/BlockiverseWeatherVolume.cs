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
        // The box the player carries. Wide enough that drops enter from the periphery rather than
        // popping in ahead, shallow enough that none of it is wasted behind them.
        public const float VolumeWidthMeters = 14.0f;
        public const float VolumeDepthMeters = 14.0f;
        public const float SpawnHeightMeters = 7.0f;

        // Emission at full intensity. Rain reads as rain somewhere north of a couple of hundred
        // particles alive at once; at 1.4 s of fall time this rate keeps roughly that many.
        public const float MaxRainParticlesPerSecond = 220.0f;
        public const float MaxSnowParticlesPerSecond = 90.0f;

        public const float RainFallSpeed = 9.0f;
        public const float SnowFallSpeed = 1.1f;

        // Seconds to cross most of the way to a new emission rate. Weather changes should arrive
        // as a front, not as a switch being thrown.
        public const float RampSeconds = 2.5f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        Transform head;
        PrecipitationKind activeKind = PrecipitationKind.None;
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

            if (kind != activeKind)
            {
                activeKind = kind;
                ApplyKind(kind);
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

        void ApplyKind(PrecipitationKind kind)
        {
            if (particles == null)
                return;

            bool isSnow = kind == PrecipitationKind.Snow;

            ParticleSystem.MainModule main = particles.main;
            main.startSpeed = isSnow ? SnowFallSpeed : RainFallSpeed;
            main.startSize = isSnow ? 0.09f : 0.055f;
            main.startLifetime = SpawnHeightMeters / (isSnow ? SnowFallSpeed : RainFallSpeed) * 1.15f;
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

            particles = gameObject.GetComponent<ParticleSystem>();

            if (particles == null)
                particles = gameObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;

            // WORLD, not Local. This is the single most important line in the file: in Local space
            // every drop is welded to the XR origin and travels and rotates with the player, so
            // nothing ever falls past them.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = 0.0f;
            main.startRotation3D = false;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(VolumeWidthMeters, 0.1f, VolumeDepthMeters);
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

            ApplyKind(activeKind);
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
