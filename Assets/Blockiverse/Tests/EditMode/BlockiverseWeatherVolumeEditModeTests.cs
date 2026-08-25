using Blockiverse.Gameplay;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Precipitation was drawn by the one-shot burst pool: two 4.5 cm particles every 0.6 seconds,
    // which measures out at roughly one raindrop on screen at a time against a spec asking for a
    // couple of hundred -- and in Local simulation space, so nothing ever fell past the player.
    // These pin the continuous volume that replaced it.
    public sealed class BlockiverseWeatherVolumeEditModeTests
    {
        GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null)
                Object.DestroyImmediate(host);
        }

        BlockiverseWeatherVolume CreateVolume()
        {
            host = new GameObject("Weather Volume Under Test");
            BlockiverseWeatherVolume volume = host.AddComponent<BlockiverseWeatherVolume>();
            volume.Configure(null, null, null, null);
            return volume;
        }

        [Test]
        public void ParticlesSimulateInWorldSpaceSoTheyFallPastThePlayer()
        {
            // The single most important property here. In Local space every drop is welded to the
            // XR origin, travels with the player and swings 45 degrees on every snap turn -- which
            // is exactly what the old implementation did, because it never set this at all.
            CreateVolume();
            ParticleSystem particles = host.GetComponent<ParticleSystem>();

            Assert.That(particles, Is.Not.Null);
            Assert.That(particles.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
        }

        [Test]
        public void PrecipitationFallsDownwardFromAboveTheHead()
        {
            CreateVolume();
            ParticleSystem particles = host.GetComponent<ParticleSystem>();
            ParticleSystem.ShapeModule shape = particles.shape;

            Assert.That(shape.enabled, Is.True);
            Assert.That(shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Box),
                "A box overhead, not the default cone -- which fires along local +Z, i.e. sideways.");
            Assert.That(shape.position.y, Is.GreaterThan(1.0f), "Precipitation has to start above the player.");
            Assert.That(shape.rotation.x, Is.EqualTo(90.0f).Within(0.01f), "Emission must point straight down.");

            // The extent that matters is the one AFTER shape.rotation, not the raw local scale.
            // shape.rotation turns the box geometry as well as the emission direction, so a box
            // authored as (width, thin, depth) comes out standing on edge.
            //
            // This is why the previous form of this test -- scale.x > 4 and scale.z > 4 -- passed
            // against a shipped bug: the local scale really was (14, 0.1, 14), so both raw axes
            // looked fine while the emitter was actually a 14x14x0.1 VERTICAL sheet through the
            // player's head, which reads in the headset as rain falling in a single line ahead.
            Vector3 worldExtent = Quaternion.Euler(shape.rotation) * shape.scale;
            Assert.That(Mathf.Abs(worldExtent.y), Is.LessThan(1.0f),
                "The emission box must be a THIN ceiling. A tall Y extent means it is standing on edge, " +
                "so precipitation spawns in a vertical curtain instead of across the sky.");
            Assert.That(Mathf.Abs(worldExtent.x), Is.GreaterThan(8.0f),
                "The ceiling must be wide enough that drops enter from the periphery.");
            Assert.That(Mathf.Abs(worldExtent.z), Is.GreaterThan(8.0f),
                "The ceiling must be deep enough to cover ahead and behind, not just a strip.");
        }

        [Test]
        public void EmissionScalesWithIntensityAndStopsWhenDry()
        {
            BlockiverseWeatherVolume volume = CreateVolume();

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(volume.ActiveKind, Is.EqualTo(PrecipitationKind.Rain));

            volume.SetPrecipitation(PrecipitationKind.Rain, 0.3f);
            volume.SetPrecipitation(PrecipitationKind.None, 0.0f);
            Assert.That(volume.ActiveKind, Is.EqualTo(PrecipitationKind.None));
        }

        [Test]
        public void ClearingPrecipitationStopsTheVolume()
        {
            // The volume is unparented with its own LateUpdate, so it does not stop merely because
            // the weather controller stopped polling. Menus taking world input, returning to the
            // title, or losing the environment query all bail before the volume is updated -- so
            // whatever stops feedback has to clear this explicitly, or it rains on the title
            // screen forever.
            BlockiverseWeatherVolume volume = CreateVolume();

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(volume.ActiveKind, Is.EqualTo(PrecipitationKind.Rain));

            volume.SetPrecipitation(PrecipitationKind.None, 0.0f);

            Assert.That(volume.ActiveKind, Is.EqualTo(PrecipitationKind.None));
            Assert.That(host.GetComponent<ParticleSystem>().emission.rateOverTime.constant,
                Is.EqualTo(0.0f).Within(0.001f).Or.LessThan(volume.CurrentEmissionRate + 1.0f),
                "A cleared volume must ramp to zero rather than hold its last rate.");
        }

        [Test]
        public void RainIsDenserAndFasterThanSnow()
        {
            // Backwards in the old implementation: snow had no case in the count switch and fell
            // through to the default of 4, while rain was explicitly 2 -- so a blizzard drew twice
            // as many particles as a thunderstorm.
            Assert.That(
                BlockiverseWeatherVolume.MaxRainParticlesPerSecond,
                Is.GreaterThan(BlockiverseWeatherVolume.MaxSnowParticlesPerSecond));
            // Compared against the FASTEST snow (blizzard), so this still holds at the extreme
            // rather than only for a flurry.
            Assert.That(BlockiverseWeatherVolume.RainFallSpeed,
                Is.GreaterThan(BlockiverseWeatherVolume.SnowFallSpeedHeavy));
            Assert.That(BlockiverseWeatherVolume.SnowFallSpeedHeavy,
                Is.GreaterThan(BlockiverseWeatherVolume.SnowFallSpeedLight),
                "Heavier snow must fall faster, or light snow and a blizzard read identically.");
        }

        [Test]
        public void RainIsDenseEnoughToActuallyRead()
        {
            // The old path averaged 1.17 particles on screen with the view empty 42% of the time.
            // Particles alive = rate * lifetime, and lifetime is the fall time through the volume.
            // Lifetime is the FALL DISTANCE over speed, not the spawn height over speed. Those are
            // different numbers and the difference was a shipped bug -- see
            // PrecipitationOutlivesItsFallToWellBelowThePlayer below.
            float fallSeconds = BlockiverseWeatherVolume.FallDistanceMeters / BlockiverseWeatherVolume.RainFallSpeed;
            float aliveAtFullIntensity = BlockiverseWeatherVolume.MaxRainParticlesPerSecond * fallSeconds;

            Assert.That(aliveAtFullIntensity, Is.GreaterThan(800.0f),
                "Heavy rain has to fill a 30x30 volume, which needs far more than the old few hundred drops.");
            Assert.That(aliveAtFullIntensity, Is.LessThanOrEqualTo(BlockiverseWeatherVolume.MaxLiveParticles),
                "Emission must not outrun maxParticles, or the system silently stops emitting.");
        }

        [Test]
        public void PrecipitationOutlivesItsFallToWellBelowThePlayer()
        {
            // Lifetime was SpawnHeight/speed, i.e. exactly enough to fall back to the height it
            // started from relative to the head. Drops therefore died about a metre BELOW eye
            // level, and standing anywhere elevated you could watch rain stop in mid-air just
            // under your feet instead of reaching the ground.
            Assert.That(BlockiverseWeatherVolume.FallDistanceMeters,
                Is.GreaterThan(BlockiverseWeatherVolume.SpawnHeightMeters * 2.0f),
                "A particle must travel far enough to pass the player and keep going to the ground below.");

            float belowHead = BlockiverseWeatherVolume.FallDistanceMeters - BlockiverseWeatherVolume.SpawnHeightMeters;
            Assert.That(belowHead, Is.GreaterThan(20.0f),
                "Precipitation has to survive at least a good drop below eye level to cover elevated ground.");
        }

        [Test]
        public void TheHeaviestSnowStillFitsTheParticleBudget()
        {
            // Snow is slow, so it stays alive far longer than rain and IT is the worst case for
            // the live count -- easy to miss when reasoning about "rain is the heavy one".
            float lifetime = BlockiverseWeatherVolume.FallDistanceMeters / BlockiverseWeatherVolume.SnowFallSpeedHeavy;
            float alive = BlockiverseWeatherVolume.MaxSnowParticlesPerSecond * lifetime;

            Assert.That(alive, Is.LessThanOrEqualTo(BlockiverseWeatherVolume.MaxLiveParticles),
                $"Blizzard would need {alive:F0} live particles against a cap of " +
                $"{BlockiverseWeatherVolume.MaxLiveParticles}; emission would silently clip.");
        }

        [Test]
        public void SnowDriftsAndRainDoesNot()
        {
            BlockiverseWeatherVolume volume = CreateVolume();
            ParticleSystem particles = host.GetComponent<ParticleSystem>();

            volume.SetPrecipitation(PrecipitationKind.Snow, 1.0f);
            Assert.That(particles.noise.enabled, Is.True, "Snow should wander on its way down.");

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(particles.noise.enabled, Is.False, "Rain that wanders reads as ash.");
        }

        [Test]
        public void RainRendersAsAStreakAndSnowAsABillboard()
        {
            BlockiverseWeatherVolume volume = CreateVolume();
            var particleRenderer = host.GetComponent<ParticleSystemRenderer>();

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(particleRenderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Stretch),
                "The streak is most of what makes rain read as rain rather than as floating dots.");

            volume.SetPrecipitation(PrecipitationKind.Snow, 1.0f);
            Assert.That(particleRenderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Billboard));
        }

        [Test]
        public void ABlizzardSeedsItsFlakesUPWINDOfThePlayer()
        {
            // The reason "I didn't see the sideways drift with the blizzard" was not a subtle
            // effect — it was NO snow beside the player, which reads as ordinary weather rather
            // than as a bug.
            //
            // A flake takes SpawnHeight / fallSpeed to come down to eye level, and the wind moves
            // it sideways the whole way. If the spawn ceiling stays centred on the head, every
            // flake that should be beside you at eye level had to be seeded further upwind than
            // the ceiling reaches — so it was never emitted — while the ones actually seeded
            // overhead are already well downwind by the time they get down to you.
            float wind = BlockiverseWeatherVolume.BlizzardWindMetersPerSecond;
            float drift = BlockiverseWeatherVolume.UpwindOffsetMeters(
                wind, BlockiverseWeatherVolume.SnowFallSpeedHeavy);

            Assert.That(drift, Is.GreaterThan(BlockiverseWeatherVolume.VolumeWidthMeters * 0.5f),
                $"A blizzard flake drifts {drift:F1} m before it reaches eye level against a " +
                $"{BlockiverseWeatherVolume.VolumeWidthMeters * 0.5f:F1} m half-width ceiling. If this " +
                "ever stops being true the offset is no longer load-bearing and can go — but while " +
                "it IS true, seeding on centre empties the volume the player is standing in.");

            Assert.That(drift, Is.LessThanOrEqualTo(BlockiverseWeatherVolume.MaxUpwindOffsetMeters));

            // Rain has no drift, so its ceiling must not move at all.
            Assert.That(
                BlockiverseWeatherVolume.UpwindOffsetMeters(0.0f, BlockiverseWeatherVolume.RainFallSpeed),
                Is.EqualTo(0.0f));
        }

        [Test]
        public void TheSpawnCeilingActuallyMovesUpwindWhenItSnowsHard()
        {
            // The arithmetic above proves the offset is needed; this proves it is applied. Both
            // are required: the previous ground-effect bug shipped with correct constants and an
            // unapplied rotation.
            BlockiverseWeatherVolume volume = CreateVolume();
            ParticleSystem particles = host.GetComponent<ParticleSystem>();

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(particles.shape.position.x, Is.EqualTo(0.0f).Within(0.01f),
                "Rain does not drift, so its ceiling must sit over the player.");

            volume.SetPrecipitation(PrecipitationKind.Snow, 1.0f);
            float expected = BlockiverseWeatherVolume.UpwindOffsetMeters(
                BlockiverseWeatherVolume.BlizzardWindMetersPerSecond,
                BlockiverseWeatherVolume.SnowFallSpeedHeavy);

            Assert.That(particles.shape.position.x, Is.EqualTo(-expected).Within(0.01f));
            Assert.That(particles.shape.position.y, Is.GreaterThan(1.0f),
                "Moving the ceiling upwind must not move it out of the sky.");
        }

        [Test]
        public void SplashesComeFromDropsThatActuallyLandRatherThanFromADiscUnderThePlayer()
        {
            // What this replaces: a flat disc of sprites placed by a single downward raycast under
            // the head. Flat against a world that is not, so on any slope it hung in the air or
            // sank into the ground, and nothing tied a splash to a drop.
            BlockiverseWeatherVolume volume = CreateVolume();
            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);

            Assert.That(host.transform.Find("Weather Ground Effect"), Is.Null,
                "The invented ground plane must be gone, not merely hidden.");

            Transform sampler = host.transform.Find("Weather Splash Sampler");
            Assert.That(sampler, Is.Not.Null, "There must be a colliding rain emitter.");

            ParticleSystem.CollisionModule collision = sampler.GetComponent<ParticleSystem>().collision;
            Assert.That(collision.enabled, Is.True);
            Assert.That(collision.type, Is.EqualTo(ParticleSystemCollisionType.World));
            Assert.That(collision.quality, Is.EqualTo(ParticleSystemCollisionQuality.High),
                "Medium and Low collide against a small cached set of PLANES — the same flat-surface " +
                "approximation this rework exists to remove.");
            // lifetimeLoss is a MinMaxCurve, not a float — reading `.constant` is what actually
            // inspects the value we set.
            Assert.That(collision.lifetimeLoss.constant, Is.EqualTo(1.0f).Within(0.001f),
                "The drop is spent by landing; what continues is the splash.");

            ParticleSystem.SubEmittersModule subEmitters = sampler.GetComponent<ParticleSystem>().subEmitters;
            Assert.That(subEmitters.enabled, Is.True);
            Assert.That(subEmitters.subEmittersCount, Is.EqualTo(1),
                "The splash must be spawned BY the collision, or it is decoration again.");
            Assert.That(subEmitters.GetSubEmitterType(0), Is.EqualTo(ParticleSystemSubEmitterType.Collision));

            Assert.That(sampler.Find("Weather Splash"), Is.Not.Null,
                "Unity requires a sub-emitter to be a child of the system that spawns it.");
        }

        [Test]
        public void TheCollidingSampleStaysASmallFractionOfTheRain()
        {
            // The whole reason a sample works is that collision is per-particle raycasting: it is
            // affordable for tens of drops and not for two thousand. This is the budget.
            float live = BlockiverseWeatherVolume.MaxSplashSamplersPerSecond
                * (BlockiverseWeatherVolume.SplashSamplerFallMeters / BlockiverseWeatherVolume.RainFallSpeed);

            Assert.That(live, Is.LessThan(80.0f),
                $"{live:F0} colliding drops means {live:F0} raycasts a frame at 72 Hz.");
            Assert.That(live, Is.LessThanOrEqualTo(BlockiverseWeatherVolume.MaxLiveSplashes),
                "Emission would silently clip against the particle cap.");
            Assert.That(
                BlockiverseWeatherVolume.MaxSplashSamplersPerSecond,
                Is.LessThan(BlockiverseWeatherVolume.MaxRainParticlesPerSecond * 0.1f),
                "The sampled drops must disappear into the rain, not add a visible second layer.");
        }

        [Test]
        public void EverySplashRendererGetsTheRainSprite()
        {
            // The splash system was the ONE emitter nothing ever handed a texture: the falling rain
            // and the colliding sampler both get the sprite through a property block, and the
            // splash drew two untextured quads of whatever the shared material carried.
            //
            // Worth a test of its own because it is invisible to every other assertion here — the
            // collision wiring, the ballistics and the emission rates are all correct with or
            // without a sprite, and EditMode renders nothing.
            var rain = Sprite.Create(
                new Texture2D(4, 4), new Rect(0.0f, 0.0f, 4.0f, 4.0f), new Vector2(0.5f, 0.5f));
            var snow = Sprite.Create(
                new Texture2D(4, 4), new Rect(0.0f, 0.0f, 4.0f, 4.0f), new Vector2(0.5f, 0.5f));

            try
            {
                host = new GameObject("Weather Volume Under Test");
                BlockiverseWeatherVolume volume = host.AddComponent<BlockiverseWeatherVolume>();
                volume.Configure(null, null, rain, snow);
                volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);

                Transform sampler = host.transform.Find("Weather Splash Sampler");
                Assert.That(sampler, Is.Not.Null);
                Transform splash = sampler.Find("Weather Splash");
                Assert.That(splash, Is.Not.Null);

                var block = new MaterialPropertyBlock();
                splash.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(block);

                Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(rain.texture),
                    "The splash renderer never received a sprite, so every impact draws untextured quads.");

                sampler.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(block);
                Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(rain.texture),
                    "The sampled drops must look exactly like the rain they are drawn from.");
            }
            finally
            {
                Object.DestroyImmediate(rain);
                Object.DestroyImmediate(snow);
            }
        }

        [Test]
        public void ASplashLandsBackOnTheSurfaceWithinItsOwnLifetime()
        {
            // A splash is a ballistic arc, and the first attempt was not one. gravityModifier 2.2
            // against a 0.6-1.3 m/s hop gives an apex of 2.3 cm reached at t = 0.05 s and then
            // y(0.35) = -97 cm: the particle spends 74% of its life below the surface it landed on.
            // Depth testing hides that rather than showing particles underground, so what shipped
            // read as a 2 cm twitch with most of the particle budget spent invisible.
            //
            // Asserted on the arc, not on the constants, so any future retune has to stay an arc.
            float g = Mathf.Abs(Physics.gravity.y) * BlockiverseWeatherVolume.SplashGravityModifier;
            float life = BlockiverseWeatherVolume.SplashLifetimeSeconds;

            foreach (float v in new[]
                     {
                         BlockiverseWeatherVolume.SplashRiseSpeedMin,
                         BlockiverseWeatherVolume.SplashRiseSpeedMax,
                     })
            {
                float apex = v * v / (2.0f * g);
                float landsAt = 2.0f * v / g;

                Assert.That(apex, Is.InRange(0.04f, 0.25f),
                    $"A {apex * 100.0f:F0} cm apex is a twitch or a fountain, not a splash.");
                Assert.That(landsAt, Is.LessThanOrEqualTo(life + 0.05f),
                    $"The particle is still {(-(v * life - 0.5f * g * life * life)) * 100.0f:F0} cm " +
                    "below the surface when its life ends.");
                Assert.That(landsAt, Is.GreaterThan(life * 0.5f),
                    "Landing in the first half of its life leaves the rest of the particle wasted.");
            }
        }

        [Test]
        public void SnowGetsNoSplashesAndDryWeatherGetsNoRaycasts()
        {
            // Snow landing on snow has nothing to show, and the drifting ground layer that used to
            // stand in for it was the same invented-surface artefact as the splash disc. Dry
            // weather matters more: leaving the sampler emitting would keep tens of raycasts a
            // frame running under a clear sky.
            BlockiverseWeatherVolume volume = CreateVolume();

            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(volume.SplashSamplerRate, Is.GreaterThan(0.0f));

            volume.SetPrecipitation(PrecipitationKind.Snow, 1.0f);
            Assert.That(volume.SplashSamplerRate, Is.EqualTo(0.0f));

            volume.SetPrecipitation(PrecipitationKind.None, 0.0f);
            Assert.That(volume.SplashSamplerRate, Is.EqualTo(0.0f));

            // And it scales with the weather rather than being on or off.
            volume.SetPrecipitation(PrecipitationKind.Rain, 0.25f);
            float light = volume.SplashSamplerRate;
            volume.SetPrecipitation(PrecipitationKind.Rain, 1.0f);
            Assert.That(light, Is.LessThan(volume.SplashSamplerRate));
        }

        [Test]
        public void ParticlesNeverCastShadows()
        {
            CreateVolume();
            var particleRenderer = host.GetComponent<ParticleSystemRenderer>();

            Assert.That(
                particleRenderer.shadowCastingMode,
                Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off),
                "Hundreds of shadow-casting particles would be a serious cost for no visible gain.");
        }
    }
}
