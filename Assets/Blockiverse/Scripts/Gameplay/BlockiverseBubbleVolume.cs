using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Bubbles rising past a submerged player.
    //
    // Underwater had fog and a camera-clear swap and nothing else, so being under the surface read
    // as "the screen went blue" rather than as being in water. Bubbles are the cheapest cue that
    // sells it: they establish which way is up, they give the water a sense of depth and motion,
    // and they only exist while the player is actually under.
    //
    // Built like the precipitation volume and for the same reason: ONE continuous emitter in WORLD
    // simulation space, following the head in position only. World space is what lets bubbles rise
    // PAST the player instead of travelling with them, and taking position without rotation is what
    // stops a snap turn dragging the whole column around.
    [DisallowMultipleComponent]
    public sealed class BlockiverseBubbleVolume : MonoBehaviour
    {
        // Bubbles start below and beside the player and rise through their view. Narrow, because a
        // wide column reads as a curtain rather than as something coming off you.
        public const float VolumeWidthMeters = 2.6f;
        public const float VolumeDepthMeters = 2.6f;
        public const float SpawnDepthMeters = 2.2f;

        public const float MaxBubblesPerSecond = 14.0f;
        public const float RiseSpeedMetersPerSecond = 0.75f;

        // Long enough to travel well past the head from below, so they read as continuous rather
        // than as popping into existence at eye level.
        public const float LifetimeSeconds = 5.5f;

        public const float RampSeconds = 0.6f;

        // Emberflow is molten rock. Bubbles in it are sparse, slow and wrong-looking if they are
        // the same cheerful stream freshwater gets.
        public const float EmberflowRateScale = 0.35f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        Transform head;
        Sprite bubbleSprite;
        MaterialPropertyBlock propertyBlock;
        float targetRate;
        float currentRate;
        FluidFamily activeFamily = FluidFamily.Freshwater;

        public float CurrentEmissionRate => currentRate;

        public void Configure(Transform headTransform, Material particleMaterial, Sprite sprite)
        {
            head = headTransform;
            bubbleSprite = sprite;
            EnsureParticles(particleMaterial);
        }

        // `submergedBlend` is the water view's 0..1, so bubbles fade in with the rest of the
        // underwater treatment rather than snapping on at the waterline.
        public void SetSubmerged(float submergedBlend, FluidFamily family)
        {
            submergedBlend = Mathf.Clamp01(submergedBlend);

            if (family != activeFamily)
            {
                activeFamily = family;
                ApplyFamily(family);
            }

            float scale = family == FluidFamily.Emberflow ? EmberflowRateScale : 1.0f;
            targetRate = MaxBubblesPerSecond * submergedBlend * scale;
        }

        void LateUpdate()
        {
            if (particles == null)
                return;

            if (head != null)
                transform.position = head.position;

            currentRate = Mathf.MoveTowards(
                currentRate, targetRate, (MaxBubblesPerSecond / RampSeconds) * Time.deltaTime);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = currentRate;

            bool shouldPlay = currentRate > 0.01f;

            if (shouldPlay && !particles.isPlaying)
                particles.Play();
            else if (!shouldPlay && particles.isPlaying)
                particles.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
        }

        void ApplyFamily(FluidFamily family)
        {
            if (particles == null)
                return;

            ParticleSystem.MainModule main = particles.main;

            main.startColor = family switch
            {
                // Bright and lightly tinted by the fluid they are rising through, so they read
                // against the fog rather than disappearing into it.
                FluidFamily.Brine => new Color(0.78f, 0.95f, 0.92f, 0.62f),
                FluidFamily.Emberflow => new Color(1.0f, 0.72f, 0.42f, 0.55f),
                _ => new Color(0.82f, 0.93f, 1.0f, 0.60f),
            };

            main.startSpeed = family == FluidFamily.Emberflow
                ? RiseSpeedMetersPerSecond * 0.45f
                : RiseSpeedMetersPerSecond;
        }

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

            // WORLD. In Local space every bubble is welded to the rig and rises with the player
            // rather than past them, which is the opposite of the cue.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.gravityModifier = 0.0f;
            main.startLifetime = LifetimeSeconds;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(VolumeWidthMeters, 0.4f, VolumeDepthMeters);
            shape.position = new Vector3(0.0f, -SpawnDepthMeters, 0.0f);
            // Straight up. The default cone fires along local +Z, i.e. sideways.
            shape.rotation = new Vector3(-90.0f, 0.0f, 0.0f);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;

            // Bubbles wobble on their way up. Without it a vertical stream of dots reads as rain
            // running backwards.
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.22f;
            noise.frequency = 0.6f;

            particleRenderer = gameObject.GetComponent<ParticleSystemRenderer>();

            if (particleRenderer != null)
            {
                if (particleMaterial != null)
                    particleRenderer.sharedMaterial = particleMaterial;

                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                particleRenderer.alignment = ParticleSystemRenderSpace.View;
                particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;

                if (bubbleSprite != null && bubbleSprite.texture != null)
                {
                    propertyBlock ??= new MaterialPropertyBlock();
                    propertyBlock.Clear();
                    particleRenderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetTexture(BaseMapId, bubbleSprite.texture);
                    propertyBlock.SetTexture(MainTexId, bubbleSprite.texture);
                    particleRenderer.SetPropertyBlock(propertyBlock);
                }
            }

            ApplyFamily(activeFamily);
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
