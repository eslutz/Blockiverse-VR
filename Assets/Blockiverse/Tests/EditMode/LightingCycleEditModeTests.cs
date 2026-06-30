using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
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
        public void RuntimeAdvanceStopsWhileGameIsPaused()
        {
            var host = new GameObject("World Time Clock");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);

                BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);
                clock.AdvanceRuntime(1.0f);

                Assert.That(clock.NormalizedTime, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(clock.TotalElapsedTicks, Is.EqualTo(0));

                BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
                clock.AdvanceRuntime(1.0f);

                Assert.That(clock.NormalizedTime, Is.EqualTo(0.35f).Within(0.001f));
                Assert.That(clock.TotalElapsedTicks, Is.EqualTo(WorldConstants.TicksPerSecond));
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
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
        public void LightingCycleEvaluatorMovesSunAcrossOppositeHorizons()
        {
            Vector3 sunriseDirection = SunDirection(LightingCycleEvaluator.Evaluate(0.0f).SunRotation);
            Vector3 noonDirection = SunDirection(LightingCycleEvaluator.Evaluate(0.25f).SunRotation);
            Vector3 sunsetDirection = SunDirection(LightingCycleEvaluator.Evaluate(0.5f).SunRotation);
            Vector3 midnightDirection = SunDirection(LightingCycleEvaluator.Evaluate(0.75f).SunRotation);

            Assert.That(Mathf.Abs(sunriseDirection.y), Is.LessThan(0.35f));
            Assert.That(noonDirection.y, Is.GreaterThan(0.95f));
            Assert.That(Mathf.Abs(sunsetDirection.y), Is.LessThan(0.35f));
            Assert.That(midnightDirection.y, Is.LessThan(-0.95f));
            Assert.That(
                Vector3.Dot(HorizontalDirection(sunriseDirection), HorizontalDirection(sunsetDirection)),
                Is.LessThan(-0.95f));
        }

        static Vector3 SunDirection(Quaternion sunRotation)
        {
            return -(sunRotation * Vector3.forward);
        }

        static Vector3 HorizontalDirection(Vector3 direction)
        {
            direction.y = 0.0f;
            return direction.normalized;
        }

        [Test]
        public void LightingCycleAppliesNonShadowCastingSun()
        {
            var host = new GameObject("Lighting Cycle");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);
                Light light = host.AddComponent<Light>();
                BlockiverseLightingCycleController controller = host.AddComponent<BlockiverseLightingCycleController>();

                controller.Configure(clock, light);

                Assert.That(light.type, Is.EqualTo(LightType.Directional));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(light.shadowStrength, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
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
        public void VoxelLightSamplerBrightensTunnelNearEmissiveBlocks()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 12);
            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);

            var air = new BlockPosition(4, 4, 4);
            world.SetBlock(air, BlockRegistry.Air, trackChange: false);
            float unlit = VoxelLightSampler.SampleAirLight(world, registry, air);

            world.SetBlock(new BlockPosition(4, 4, 5), BlockRegistry.LumenLamp, trackChange: false);
            float lit = VoxelLightSampler.SampleAirLight(world, registry, air);

            Assert.That(lit, Is.GreaterThan(unlit));
            Assert.That(lit, Is.GreaterThanOrEqualTo(registry.Get(BlockRegistry.LumenLamp).EmissiveLight / 15.0f));
        }

        [Test]
        public void GlowwickLightManagerIdentifiesEmissiveBlocksAndEffectPosition()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            Vector3 position = GlowwickLightManager.GetLightPosition(new BlockPosition(2, 4, 6));

            Assert.That(position.x, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(position.y, Is.GreaterThan(4.75f));
            Assert.That(position.z, Is.EqualTo(6.5f).Within(0.001f));
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Glowwick,   registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.BuildTable, registry), Is.False);
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

            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Glowwick,   registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Campfire,   registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.LumenLamp,  registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.SparkFlare, registry), Is.True);
        }

        [Test]
        public void GlowwickLightManagerCreatesPointLightsForPlacedEmissiveBlocks()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 4, seed: 19);
            var host = new GameObject("Glowwick Light Manager");
            var lightPosition = new BlockPosition(1, 1, 1);

            try
            {
                world.SetBlock(lightPosition, BlockRegistry.LumenLamp, trackChange: false);

                GlowwickLightManager manager = host.AddComponent<GlowwickLightManager>();
                manager.Configure(world, registry);

                Assert.That(manager.ActiveEmitterCount, Is.EqualTo(1));
                Assert.That(manager.ActiveLightCount, Is.EqualTo(1));
                Assert.That(manager.TryGetLight(lightPosition, out Light light), Is.True);
                Assert.That(light, Is.Not.Null);
                Assert.That(light.type, Is.EqualTo(LightType.Point));
                Assert.That(light.intensity, Is.GreaterThan(0.0f));
                Assert.That(light.range, Is.GreaterThanOrEqualTo(4.0f));

                world.SetBlock(lightPosition, BlockRegistry.Air);

                Assert.That(manager.ActiveEmitterCount, Is.EqualTo(0));
                Assert.That(manager.ActiveLightCount, Is.EqualTo(0));
                Assert.That(manager.TryGetLight(lightPosition, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
