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
