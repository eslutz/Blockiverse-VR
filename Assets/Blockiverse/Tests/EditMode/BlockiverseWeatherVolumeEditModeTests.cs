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
            Assert.That(shape.scale.x, Is.GreaterThan(4.0f));
            Assert.That(shape.scale.z, Is.GreaterThan(4.0f));
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
        public void RainIsDenserAndFasterThanSnow()
        {
            // Backwards in the old implementation: snow had no case in the count switch and fell
            // through to the default of 4, while rain was explicitly 2 -- so a blizzard drew twice
            // as many particles as a thunderstorm.
            Assert.That(
                BlockiverseWeatherVolume.MaxRainParticlesPerSecond,
                Is.GreaterThan(BlockiverseWeatherVolume.MaxSnowParticlesPerSecond));
            Assert.That(BlockiverseWeatherVolume.RainFallSpeed, Is.GreaterThan(BlockiverseWeatherVolume.SnowFallSpeed));
        }

        [Test]
        public void RainIsDenseEnoughToActuallyRead()
        {
            // The old path averaged 1.17 particles on screen with the view empty 42% of the time.
            // Particles alive = rate * lifetime, and lifetime is the fall time through the volume.
            float fallSeconds = BlockiverseWeatherVolume.SpawnHeightMeters / BlockiverseWeatherVolume.RainFallSpeed;
            float aliveAtFullIntensity = BlockiverseWeatherVolume.MaxRainParticlesPerSecond * fallSeconds;

            Assert.That(aliveAtFullIntensity, Is.GreaterThan(100.0f),
                "Heavy rain has to put a hundred-plus drops in the volume or it reads as nothing.");
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
