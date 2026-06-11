using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class LightingCycleEditModeTests
    {
        [Test]
        public void WorldTimeClockWrapsNormalizedTime()
        {
            var host = new GameObject("World Time Clock");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.9f, timeScale: 1.0f);

                clock.Tick(2.5f);

                Assert.That(clock.NormalizedTime, Is.EqualTo(0.15f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void WorldTimeClockRestoreElapsedTicksMatchesContinuousTickAcrossDayWraps()
        {
            const float dayLengthSeconds = 10.0f;
            const float startNormalizedTime = 0.9f;
            const long ticks = 730; // ticksPerDay = 200, so this spans three full day wraps.

            var restoredHost = new GameObject("Restored World Time Clock");
            var tickedHost = new GameObject("Ticked World Time Clock");

            try
            {
                WorldTimeClock restored = restoredHost.AddComponent<WorldTimeClock>();
                restored.Configure(dayLengthSeconds, startNormalizedTime, timeScale: 1.0f);
                restored.RestoreElapsedTicks(ticks);

                long ticksPerDay = (long)(dayLengthSeconds * WorldConstants.TicksPerSecond);
                float expected = (startNormalizedTime + (ticks % ticksPerDay) / (float)ticksPerDay) % 1.0f;

                Assert.That(restored.NormalizedTime, Is.EqualTo(expected).Within(0.001f));
                Assert.That(restored.TotalElapsedTicks, Is.EqualTo(ticks));

                WorldTimeClock ticked = tickedHost.AddComponent<WorldTimeClock>();
                ticked.Configure(dayLengthSeconds, startNormalizedTime, timeScale: 1.0f);
                ticked.Tick(ticks / (float)WorldConstants.TicksPerSecond);

                Assert.That(restored.NormalizedTime, Is.EqualTo(ticked.NormalizedTime).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(restoredHost);
                Object.DestroyImmediate(tickedHost);
            }
        }

        [Test]
        public void WorldTimeClockRestoreElapsedTicksZeroReturnsStartPhase()
        {
            var host = new GameObject("World Time Clock");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.9f, timeScale: 1.0f);

                clock.RestoreElapsedTicks(0);

                Assert.That(clock.NormalizedTime, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(clock.TotalElapsedTicks, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LightingCycleEvaluatorKeepsNightDimmerThanDay()
        {
            LightingCycleState day = LightingCycleEvaluator.Evaluate(0.25f);
            LightingCycleState night = LightingCycleEvaluator.Evaluate(0.75f);

            Assert.That(day.SunIntensity, Is.GreaterThan(0.9f));
            Assert.That(night.SunIntensity, Is.LessThan(0.1f));
            Assert.That(day.AmbientColor.grayscale, Is.LessThan(0.35f));
            Assert.That(night.AmbientColor.grayscale, Is.LessThan(day.AmbientColor.grayscale));
            Assert.That(day.SunRotation.eulerAngles, Is.Not.EqualTo(night.SunRotation.eulerAngles));
        }

        [Test]
        public void VoxelLightSamplerDarkensTunnelWithDistanceFromOpening()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);

            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int y = 0; y < world.Bounds.Height; y++)
                {
                    for (int z = 0; z < world.Bounds.Depth; z++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);
                }
            }

            for (int x = 0; x < 6; x++)
                world.SetBlock(new BlockPosition(x, 3, 3), BlockRegistry.Air, trackChange: false);

            float entrance = VoxelLightSampler.SampleAirLight(world, registry, new BlockPosition(0, 3, 3));
            float interior = VoxelLightSampler.SampleAirLight(world, registry, new BlockPosition(5, 3, 3));

            Assert.That(entrance, Is.GreaterThan(interior));
            Assert.That(interior, Is.LessThan(0.55f));
        }

        [Test]
        public void TorchbudLightManagerPlacesPointLightAtFlameEnd()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            Vector3 position = TorchbudLightManager.GetLightPosition(new BlockPosition(2, 4, 6));

            Assert.That(position.x, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(position.y, Is.GreaterThan(4.75f));
            Assert.That(position.z, Is.EqualTo(6.5f).Within(0.001f));
            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.Glowwick,   registry), Is.True);
            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.BuildTable, registry), Is.False);
        }

        [Test]
        public void BlockEmissiveLightValuesMatchCanonical()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            registry.TryGet(BlockRegistry.Glowwick,   out BlockDefinition glowwick);
            registry.TryGet(BlockRegistry.Campfire,   out BlockDefinition campfire);
            registry.TryGet(BlockRegistry.LumenLamp,  out BlockDefinition lumenLamp);
            registry.TryGet(BlockRegistry.SparkFlare, out BlockDefinition sparkFlare);
            registry.TryGet(BlockRegistry.Graystone,  out BlockDefinition graystone);

            Assert.That(glowwick.EmissiveLight,   Is.EqualTo(9));
            Assert.That(campfire.EmissiveLight,   Is.EqualTo(12));
            Assert.That(lumenLamp.EmissiveLight,  Is.EqualTo(14));
            Assert.That(sparkFlare.EmissiveLight, Is.EqualTo(15));
            Assert.That(graystone.EmissiveLight,  Is.EqualTo(0));
        }

        [Test]
        public void AllFourEmissiveBlocksAreRecognizedAsLightEmitters()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.Glowwick,   registry), Is.True);
            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.Campfire,   registry), Is.True);
            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.LumenLamp,  registry), Is.True);
            Assert.That(TorchbudLightManager.IsLightEmitter(BlockRegistry.SparkFlare, registry), Is.True);
        }
    }
}
