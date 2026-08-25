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

        // SPLASHES. A SECOND, much sparser rain emitter whose drops actually collide with the
        // world; each one that lands spawns a splash through a collision sub-emitter, so the
        // splash is at the point a drop you can watch arrived at, on whatever surface it hit.
        //
        // The first attempt was a statistical scatter instead: a flat disc of sprites on a single
        // height from one downward raycast under the head. It failed on both counts Eric named.
        // The disc is FLAT and the world is not, so on any slope or step it hung in mid-air or
        // sank into the ground ("across an invisible surface that can stretch out into thin air
        // instead of tracking the actual surface below it"); and nothing connected a splash to a
        // drop, so the eye read two unrelated systems ("having the effect not linked to the actual
        // raindrops breaks the immersion").
        //
        // What made that attempt look necessary was the cost of collision, and the mistake was
        // treating it as all-or-nothing. Colliding all ~1900 live drops is indeed far too
        // expensive on a tile GPU; colliding a SAMPLE of them is not, because a splash does not
        // have to belong to every drop, only to a drop. These constants keep the live colliding
        // count near 50, i.e. ~50 raycasts a frame, while the other ~97% of the rain stays free.
        public const float MaxSplashSamplersPerSecond = 24.0f;
        // Narrower than the main volume: a splash is only legible within a few metres, so the
        // sampled drops are spent close to the player rather than spread over 30 m.
        public const float SplashSamplerWidthMeters = 16.0f;
        // How far a sampled drop is allowed to fall before giving up. Sized to reach a few metres
        // BELOW the player's feet — enough for the ground they are standing on and the step they
        // are about to take, not enough to keep raycasting all the way down a ravine.
        public const float SplashSamplerFallMeters = 20.0f;
        public const float SplashLifetimeSeconds = 0.35f;
        // A splash is a BALLISTIC ARC that must land inside its own lifetime.
        //
        // The first attempt used gravityModifier 2.2 against a 0.6-1.3 m/s hop, which is an apex of
        // 2.3 cm reached in 0.05 s and then y(0.35) = -97 cm. Depth testing hides the sunken part
        // behind the terrain, so it did not read as particles under the floor — it read as a 2 cm
        // twitch, with 74% of every splash's life spent as an invisible particle still costing
        // simulation and a draw.
        //
        // These give an 8-14 cm arc back on the surface at t = 0.28-0.38 s. Pinned by
        // BlockiverseWeatherVolumeEditModeTests.ASplashLandsBackOnTheSurfaceWithinItsOwnLifetime.
        public const float SplashGravityModifier = 0.8f;
        public const float SplashRiseSpeedMin = 1.1f;
        public const float SplashRiseSpeedMax = 1.5f;
        public const int MaxLiveSplashes = 120;
        // Splashes per landing. Two reads as a splash; more reads as a puff of smoke.
        public const int SplashesPerImpact = 2;

        // Sideways drive at full intensity. A blizzard that falls straight down is just heavy
        // snow; the horizontal component is most of what reads as "blizzard".
        public const float BlizzardWindMetersPerSecond = 7.0f;

        // The drift range is [DriftMinFraction, 1] x wind, so the mean is this.
        public const float DriftMinFraction = 0.55f;
        public const float MeanDriftFraction = (DriftMinFraction + 1.0f) * 0.5f;
        // Cap on how far upwind the spawn ceiling is pushed. Past this the box has left the
        // player's own weather rather than feeding it.
        public const float MaxUpwindOffsetMeters = 24.0f;

        // Seconds to cross most of the way to a new emission rate. Weather changes should arrive
        // as a front, not as a switch being thrown.
        public const float RampSeconds = 2.5f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        ParticleSystem samplerParticles;
        ParticleSystemRenderer samplerRenderer;
        ParticleSystem splashParticles;
        ParticleSystemRenderer splashRenderer;
        Transform head;
        PrecipitationKind activeKind = PrecipitationKind.None;
        float activeIntensity = -1.0f;
        float targetRate;
        float samplerTargetRate;
        float currentRate;

        public PrecipitationKind ActiveKind => activeKind;

        public float CurrentEmissionRate => currentRate;

        /// <summary>Emission rate of the colliding sample, before the weather-front ramp. Zero for
        /// anything but rain.</summary>
        public float SplashSamplerRate => samplerTargetRate;

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

            if (samplerParticles != null)
            {
                // Scaled by the SAME ramp fraction as the falling particles, so the splashes
                // arrive and leave with the weather front instead of snapping on.
                float rampFraction = targetRate > 0.01f ? Mathf.Clamp01(currentRate / targetRate) : 0.0f;
                ParticleSystem.EmissionModule samplerEmission = samplerParticles.emission;
                samplerEmission.rateOverTime = samplerTargetRate * rampFraction;

                bool samplerShouldPlay = samplerTargetRate * rampFraction > 0.01f;

                // withChildren FALSE, and NOT because stopping would discard live particles —
                // StopEmitting leaves those alone, so that reasoning (an earlier version of this
                // comment) was simply wrong. It is false because the splash system emits only on a
                // collision event from this sampler: once the sampler stops, no further collisions
                // occur and the splash system has nothing left to do, so reaching into it adds a
                // state change that can only ever go out of step with the parent. Note the whole
                // volume stopping (`particles.Stop(withChildren: true)` above) DOES recurse into
                // both of these, which is what should happen when the weather clears.
                if (samplerShouldPlay && !samplerParticles.isPlaying)
                    samplerParticles.Play();
                else if (!samplerShouldPlay && samplerParticles.isPlaying)
                    samplerParticles.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
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
            drift.x = new ParticleSystem.MinMaxCurve(wind * DriftMinFraction, wind);
            drift.z = new ParticleSystem.MinMaxCurve(-wind * 0.3f, wind * 0.3f);

            // THE SPAWN CEILING MOVES UPWIND, and without this the drift is not merely subtle,
            // it removes the snow. A flake spawns 12 m above the head and takes SpawnHeight /
            // fallSpeed to reach eye level -- 3.3 s in a blizzard -- during which the wind above
            // carries it UpwindOffsetMeters sideways. At 7 m/s that is 18 m, and the ceiling is
            // only 15 m in half-width: every flake that would have been beside you at eye level
            // was seeded outside the box and never existed, and every flake that WAS seeded
            // overhead is 18 m downwind by the time it gets down to you. The player stands in a
            // hole in their own blizzard, which is what "I didn't see the sideways drift" looks
            // like from the inside -- less snow, not slanted snow. Seeding upwind by exactly the
            // distance the wind will undo puts the flakes back where they are seen falling.
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.position = new Vector3(-UpwindOffsetMeters(wind, fallSpeed), SpawnHeightMeters, 0.0f);

            samplerTargetRate = ApplySamplerKind(kind, intensity, fallSpeed);

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

            EnsureSplashSystems(particleMaterial);
            ApplyKind(activeKind, Mathf.Max(activeIntensity, 0.0f));
        }

        /// <summary>
        /// How far upwind of the head the spawn ceiling sits, for a given wind and fall speed.
        /// </summary>
        /// <remarks>
        /// Public and static so the arithmetic can be pinned without a headset. The failure it
        /// prevents is invisible to every test that only asks whether the drift module is on:
        /// the drift can be configured perfectly and still empty the volume around the player,
        /// because the ceiling seeds the flakes and the wind then carries them off before they
        /// have fallen far enough to be seen.
        /// </remarks>
        public static float UpwindOffsetMeters(float windMetersPerSecond, float fallSpeedMetersPerSecond)
        {
            if (windMetersPerSecond <= 0.0f || fallSpeedMetersPerSecond <= 0.01f)
                return 0.0f;

            float secondsToEyeLevel = SpawnHeightMeters / fallSpeedMetersPerSecond;
            return Mathf.Min(
                windMetersPerSecond * MeanDriftFraction * secondsToEyeLevel,
                MaxUpwindOffsetMeters);
        }

        // The sampled-collision splash pair: a sparse rain emitter that collides, and the splash
        // system it triggers. Two GameObjects because a GameObject holds one ParticleSystem, and
        // the splash MUST be a child of the system that spawns it — Unity refuses a sub-emitter
        // that is not.
        void EnsureSplashSystems(Material particleMaterial)
        {
            if (samplerParticles == null)
            {
                GameObject host = FindOrCreateChild(transform, SplashSamplerObjectName);
                ParticleSystem found = host.GetComponent<ParticleSystem>();
                samplerParticles = found != null ? found : host.AddComponent<ParticleSystem>();
            }

            if (splashParticles == null)
            {
                GameObject host = FindOrCreateChild(samplerParticles.transform, SplashObjectName);
                ParticleSystem found = host.GetComponent<ParticleSystem>();
                splashParticles = found != null ? found : host.AddComponent<ParticleSystem>();
            }

            ConfigureSplash(particleMaterial);
            ConfigureSampler(particleMaterial);
        }

        static GameObject FindOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
                return existing.gameObject;

            var created = new GameObject(name);
            created.transform.SetParent(parent, worldPositionStays: false);
            return created;
        }

        // The drop that gets checked. Identical to the rain around it in everything the eye can
        // see, and different only in that it is rare and that it stops where it lands.
        void ConfigureSampler(Material particleMaterial)
        {
            ParticleSystem.MainModule main = samplerParticles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxLiveSplashes;
            main.gravityModifier = 0.0f;
            main.startRotation3D = false;
            main.startSpeed = RainFallSpeed;
            main.startLifetime = SplashSamplerFallMeters / RainFallSpeed;
            main.startColor = new Color(0.62f, 0.78f, 0.95f, 0.55f);

            ParticleSystem.ShapeModule shape = samplerParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Same axis-order trap as the main ceiling: rotation moves the box geometry too, so
            // the depth component goes in Y and the thin one in Z. See EnsureParticles.
            shape.scale = new Vector3(SplashSamplerWidthMeters, SplashSamplerWidthMeters, 0.1f);
            shape.position = new Vector3(0.0f, SpawnHeightMeters, 0.0f);
            shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);

            ParticleSystem.EmissionModule emission = samplerParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;

            ParticleSystem.CollisionModule collision = samplerParticles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            // HIGH, and this is the one setting that cannot be traded away. Medium and Low do not
            // raycast per particle; they collide against a small cached set of PLANES, which is
            // the flat-surface approximation this whole rework exists to get rid of. The budget is
            // held by keeping the particle count low instead.
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = BlockiverseProject.VoxelGroundLayerMask;
            collision.enableDynamicColliders = false;
            collision.sendCollisionMessages = false;
            collision.bounce = 0.0f;
            collision.dampen = 1.0f;
            // The drop is consumed by landing; what continues is the splash.
            collision.lifetimeLoss = 1.0f;

            ParticleSystem.SubEmittersModule subEmitters = samplerParticles.subEmitters;
            subEmitters.enabled = true;

            if (subEmitters.subEmittersCount == 0)
                subEmitters.AddSubEmitter(
                    splashParticles,
                    ParticleSystemSubEmitterType.Collision,
                    ParticleSystemSubEmitterProperties.InheritNothing);

            samplerRenderer = samplerParticles.GetComponent<ParticleSystemRenderer>();

            if (samplerRenderer != null)
            {
                if (particleMaterial != null)
                    samplerRenderer.sharedMaterial = particleMaterial;

                samplerRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                samplerRenderer.velocityScale = 0.06f;
                samplerRenderer.lengthScale = 2.5f;
                samplerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                samplerRenderer.receiveShadows = false;
                samplerRenderer.alignment = ParticleSystemRenderSpace.View;
            }
        }

        // What a landing looks like. Emission is driven entirely by the parent's collision event,
        // so the rate is zero and the burst is what fires.
        void ConfigureSplash(Material particleMaterial)
        {
            ParticleSystem.MainModule main = splashParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxLiveSplashes * SplashesPerImpact;
            main.startLifetime = SplashLifetimeSeconds;
            main.startSpeed = new ParticleSystem.MinMaxCurve(SplashRiseSpeedMin, SplashRiseSpeedMax);
            main.startSize = 0.06f;
            main.startColor = new Color(0.78f, 0.88f, 1.0f, 0.55f);
            // Pops up and falls back inside its third of a second, which is what separates a
            // splash from a puff of smoke sitting on the ground. See SplashGravityModifier.
            main.gravityModifier = SplashGravityModifier;

            ParticleSystem.EmissionModule emission = splashParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, (short)SplashesPerImpact) });

            ParticleSystem.ShapeModule shape = splashParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55.0f;
            shape.radius = 0.02f;
            // Cone fires along local +Z, so the same +90 about X that aims the ceiling downward
            // aims this one UP, out of the surface the drop hit.
            shape.rotation = new Vector3(-90.0f, 0.0f, 0.0f);

            splashRenderer = splashParticles.GetComponent<ParticleSystemRenderer>();

            if (splashRenderer != null)
            {
                if (particleMaterial != null)
                    splashRenderer.sharedMaterial = particleMaterial;

                splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                splashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                splashRenderer.receiveShadows = false;
                ApplySprite(splashRenderer, rain);
            }
        }

        // Sets the sampler's kind-specific look and returns its emission rate. Snow gets none:
        // snow landing on snow has nothing to show, and the drifting ground layer that used to
        // stand in for it was the same invented-surface artefact as the splash disc.
        float ApplySamplerKind(PrecipitationKind kind, float intensity, float fallSpeed)
        {
            if (samplerParticles == null || kind != PrecipitationKind.Rain)
                return 0.0f;

            ParticleSystem.MainModule main = samplerParticles.main;
            main.startSize = Mathf.Lerp(RainSizeLight, RainSizeHeavy, intensity);
            main.startSpeed = fallSpeed;
            main.startLifetime = SplashSamplerFallMeters / Mathf.Max(0.5f, fallSpeed);

            ApplySprite(samplerRenderer, rain);
            // The splash system is the ONLY emitter that had no sprite of its own, so every impact
            // drew two untextured quads of whatever the shared particle material happened to carry.
            // It is re-applied here rather than only at construction because Configure supplies the
            // sprites AFTER EnsureParticles has already built the systems.
            ApplySprite(splashRenderer, rain);

            return MaxSplashSamplersPerSecond * intensity;
        }

        void ApplySprite(ParticleSystemRenderer target, Sprite sprite)
        {
            if (target == null || sprite == null || sprite.texture == null)
                return;

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMapId, sprite.texture);
            propertyBlock.SetTexture(MainTexId, sprite.texture);
            target.SetPropertyBlock(propertyBlock);
        }

        const string SplashSamplerObjectName = "Weather Splash Sampler";
        const string SplashObjectName = "Weather Splash";

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
