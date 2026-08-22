using System.Collections.Generic;
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
        public void LightingCycleAppliesShadowCastingSun()
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
                Assert.That(light.shadows, Is.Not.EqualTo(LightShadows.None),
                    "The sun must cast shadows; the controller re-applies this every LateUpdate.");
                Assert.That(light.shadowStrength, Is.GreaterThan(0.0f));
                Assert.That(controller.IsMoonPrimary, Is.False, "Midday should be driven by the sun.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LightingCycleHandsTheDirectionalLightToTheMoonAtNight()
        {
            var host = new GameObject("Lighting Cycle");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 10.0f, startNormalizedTime: 0.75f, timeScale: 1.0f);
                Light light = host.AddComponent<Light>();
                BlockiverseLightingCycleController controller = host.AddComponent<BlockiverseLightingCycleController>();

                controller.Configure(clock, light);

                Assert.That(controller.IsMoonPrimary, Is.True, "Midnight should be driven by the moon.");
                Assert.That(light.intensity, Is.GreaterThan(0.0f),
                    "A moonlit night must not be pitch black.");

                // The moon has to light the ground from ABOVE. The regression this guards is the
                // single light pitching a full 360 degrees, which put it under the terrain at
                // midnight so N dot L was zero on every upward face no matter its intensity.
                Vector3 towardLight = -(host.transform.rotation * Vector3.forward);
                Assert.That(towardLight.y, Is.GreaterThan(0.95f));

                // A fresh clock sits on day 0, i.e. a NEW moon -- the dimmest phase. It must still
                // LIGHT the world, or the darkest night is the unplayable one again.
                Assert.That(controller.MoonPhaseIndex, Is.EqualTo(0));
                Assert.That(light.intensity, Is.GreaterThan(0.0f));

                // It no longer casts, and that is deliberate. Once a full moon renders at 1/15 of
                // noon a new moon sits at a quarter of that, below the intensity where a shadow
                // pass buys anything visible -- so the whole shadow-caster sweep over every loaded
                // chunk is skipped. The brighter phases still cast; see the assertion below.
                Assert.That(light.intensity,
                    Is.LessThan(BlockiverseLightingCycleController.MinimumShadowCastingIntensity));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MoonlitNightIsNavigableAndScalesWithMoonPhase()
        {
            LightingCycleState day = LightingCycleEvaluator.Evaluate(0.25f);
            LightingCycleState fullMoon = LightingCycleEvaluator.Evaluate(0.75f, moonPhaseIndex: 4);
            LightingCycleState newMoon = LightingCycleEvaluator.Evaluate(0.75f, moonPhaseIndex: 0);

            // Asserted against the evaluator's own constant rather than a copy of the number, so
            // retuning the moon updates one place.
            //
            // The ratios MUST be taken in linear space -- the project renders in Linear colour
            // space, and doing this arithmetic on gamma components is the bug that once left night
            // at roughly half its intended radiance.
            //
            // Note this is deliberately NOT the gameplay sky-light ladder's 4/15. That ladder is a
            // 0-15 visibility/spawn/crop scale, and adopting it as a render target put a full-moon
            // night at ~55% of noon once displayed -- brighter than an overcast afternoon.
            // EnvironmentLightComputer still owns the gameplay ladder and is unchanged.
            float expected = LightingCycleEvaluator.FullMoonRadianceFraction;

            float directionalRatio = fullMoon.MoonIntensity * LinearLuminance(fullMoon.MoonColor) /
                                     (day.SunIntensity * LinearLuminance(day.SunColor));
            Assert.That(directionalRatio, Is.EqualTo(expected).Within(0.005f));

            float ambientRatio = LinearLuminance(fullMoon.AmbientColor) / LinearLuminance(day.AmbientColor);
            Assert.That(ambientRatio, Is.EqualTo(expected).Within(0.005f),
                "Ambient and directional must agree on how bright a full-moon night is.");

            Assert.That(expected, Is.LessThan(4.0f / 15.0f),
                "The render target is deliberately dimmer than the gameplay sky-light ladder.");

            Assert.That(fullMoon.IsMoonPrimary, Is.True);
            Assert.That(newMoon.MoonIntensity, Is.LessThan(fullMoon.MoonIntensity));
            Assert.That(newMoon.MoonIntensity, Is.GreaterThan(0.0f),
                "Even a new moon keeps a floor so night is never unplayable.");

            // The shadow-pass boundary now falls BETWEEN the phases, which is the intended
            // consequence of dimming the moon: a bright night still casts, the darkest one skips
            // the whole shadow-caster sweep over every loaded chunk.
            Assert.That(
                fullMoon.MoonIntensity,
                Is.GreaterThan(BlockiverseLightingCycleController.MinimumShadowCastingIntensity),
                "A full moon must still cast, or moonlit shadows are gone from the game entirely.");
            Assert.That(
                newMoon.MoonIntensity,
                Is.LessThan(BlockiverseLightingCycleController.MinimumShadowCastingIntensity));

            // Still unmistakably night, and phase still reads.
            Assert.That(ambientRatio, Is.LessThan(0.12f));
            Assert.That(fullMoon.AmbientColor.grayscale, Is.GreaterThan(newMoon.AmbientColor.grayscale));
        }

        [Test]
        public void TwilightIsNotDarkerThanMidnight()
        {
            // Regression: ambient used to be keyed off the same curve as the sun, so at the exact
            // horizon crossing neither body contributed AND ambient had already collapsed to the
            // night value — making dawn and dusk darker than midnight.
            LightingCycleState midnight = LightingCycleEvaluator.Evaluate(0.75f);
            LightingCycleState dusk = LightingCycleEvaluator.Evaluate(0.5f);
            LightingCycleState dawn = LightingCycleEvaluator.Evaluate(0.0f);

            Assert.That(
                LinearLuminance(dusk.AmbientColor),
                Is.GreaterThan(LinearLuminance(midnight.AmbientColor)));
            Assert.That(
                LinearLuminance(dawn.AmbientColor),
                Is.GreaterThan(LinearLuminance(midnight.AmbientColor)));
        }

        // Rec. 709 luminance of the colour as the renderer actually sees it.
        static float LinearLuminance(Color color)
        {
            Color linear = color.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        [Test]
        public void MoonPhaseAdvancesOncePerGameDayOnAnEightDayCycle()
        {
            var host = new GameObject("World Time Clock");

            try
            {
                WorldTimeClock clock = host.AddComponent<WorldTimeClock>();
                clock.Configure(WorldTimeClock.DefaultDayLengthSeconds, 0.75f, timeScale: 1.0f);

                clock.RestoreElapsedTicks(0);
                Assert.That(BlockiverseLightingCycleController.ResolveMoonPhaseIndex(clock), Is.EqualTo(0));

                clock.RestoreElapsedTicks(WorldConstants.TicksPerDay * 4L);
                Assert.That(BlockiverseLightingCycleController.ResolveMoonPhaseIndex(clock), Is.EqualTo(4));

                // Wraps every eight days, so day 8 is a new moon again.
                clock.RestoreElapsedTicks(WorldConstants.TicksPerDay * 8L);
                Assert.That(BlockiverseLightingCycleController.ResolveMoonPhaseIndex(clock), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void EmitterLightRangeAndIntensityFollowCanonicalEmissiveLevels()
        {
            // 5.3 propagates block light one level per block, and one block is one Unity unit,
            // so a level-9 glowwick must actually reach nine blocks.
            Assert.That(GlowwickLightManager.GetLightRange(9), Is.EqualTo(9.0f).Within(0.001f));
            Assert.That(GlowwickLightManager.GetLightRange(14), Is.EqualTo(14.0f).Within(0.001f));

            // Brightness has to preserve the canonical ladder rather than compress it.
            Assert.That(
                GlowwickLightManager.GetLightIntensity(15),
                Is.GreaterThan(GlowwickLightManager.GetLightIntensity(9)));
            Assert.That(
                GlowwickLightManager.GetLightIntensity(9) / GlowwickLightManager.GetLightIntensity(15),
                Is.EqualTo(9.0f / 15.0f).Within(0.001f));
        }

        [Test]
        public void NearbyEmittersWinLightSlotsOverDistantOnes()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(64, 64, 64), chunkSize: 16, seed: 7);
            var host = new GameObject("Glowwick Light Manager");
            host.transform.position = Vector3.zero;

            try
            {
                // Fill every slot with emitters far from the viewer, the way world-generated
                // emberflow deep underground used to starve every torch the player placed.
                int placed = 0;
                for (int x = 0; x < 63 && placed < GlowwickLightManager.MaxRuntimePointLights; x += 2)
                {
                    world.SetBlock(new BlockPosition(x, 60, 60), BlockRegistry.Emberflow, trackChange: false);
                    placed++;
                }

                GlowwickLightManager manager = host.AddComponent<GlowwickLightManager>();
                manager.Configure(world, registry);

                Assert.That(manager.ActiveLightCount, Is.EqualTo(GlowwickLightManager.MaxRuntimePointLights));

                var nearPosition = new BlockPosition(1, 1, 1);
                world.SetBlock(nearPosition, BlockRegistry.Glowwick);

                Assert.That(manager.TryGetLight(nearPosition, out Light light), Is.True,
                    "A torch placed next to the player must take a light slot from a distant emitter.");
                Assert.That(light, Is.Not.Null);
                Assert.That(manager.ActiveLightCount, Is.EqualTo(GlowwickLightManager.MaxRuntimePointLights),
                    "The budget must stay capped.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ExactlyOneEmitterOwnsTheShadowSlotSoTheShaderCanTrustTheSliceIndex()
        {
            // The voxel shader picks an occlusion term per light: a real shadow map for a light
            // that owns a shadow slice, the baked per-face emitterReach gate for one that does
            // not. That split is only correct because the shadow slot is rationed here -- if two
            // emitters cast at once, two of them would bypass the bake, and if none did, the
            // sub-block shadow the fix exists to show would never be drawn.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 31);
            var host = new GameObject("Glowwick Light Manager");
            host.transform.position = Vector3.zero;

            var nearPosition = new BlockPosition(2, 2, 2);
            var farPosition = new BlockPosition(28, 2, 28);

            try
            {
                world.SetBlock(nearPosition, BlockRegistry.Glowwick, trackChange: false);
                world.SetBlock(farPosition, BlockRegistry.LumenLamp, trackChange: false);

                GlowwickLightManager manager = host.AddComponent<GlowwickLightManager>();
                manager.Configure(world, registry);

                Assert.That(manager.ActiveLightCount, Is.EqualTo(2),
                    "Both emitters are well inside the runtime light budget.");
                Assert.That(manager.TryGetLight(nearPosition, out Light nearLight), Is.True);
                Assert.That(manager.TryGetLight(farPosition, out Light farLight), Is.True);

                int casters = 0;
                if (nearLight.shadows != LightShadows.None)
                    casters++;
                if (farLight.shadows != LightShadows.None)
                    casters++;

                // Asserted as a count rather than naming which light wins: selection ranks by
                // distance to Camera.main when one exists, and EditMode gives no guarantee about
                // that. The count is the invariant the shader actually depends on.
                Assert.That(casters, Is.EqualTo(GlowwickLightManager.MaxShadowCastingLights),
                    "Exactly MaxShadowCastingLights emitters may cast. Any other number breaks the "
                    + "shader's assumption that a shadow slice identifies the one emitter whose "
                    + "occlusion is resolved by its cube map instead of the block-resolution bake.");
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
        public void VoxelLightSamplerGivesSealedRoomNoSkyLight()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 31);
            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);

            var room = new BlockPosition(4, 4, 4);
            world.SetBlock(room, BlockRegistry.Air, trackChange: false);

            // "A room with no windows or light source should be dark."
            Assert.That(VoxelLightSampler.SampleSkyExposure(world, registry, room), Is.EqualTo(0.0f));
            Assert.That(VoxelLightSampler.SampleAirLight(world, registry, room), Is.EqualTo(0.0f));
        }

        [Test]
        public void VoxelLightSamplerTunnelFadesToNoLightBeyondProbeDistance()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 8, 8), chunkSize: 8, seed: 32);
            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);

            // A tunnel from the open x=0 edge running 24 blocks in.
            for (int x = 0; x < 24; x++)
                world.SetBlock(new BlockPosition(x, 3, 3), BlockRegistry.Air, trackChange: false);

            float mouth = VoxelLightSampler.SampleSkyExposure(world, registry, new BlockPosition(0, 3, 3));
            float mid = VoxelLightSampler.SampleSkyExposure(world, registry, new BlockPosition(6, 3, 3));
            float deep = VoxelLightSampler.SampleSkyExposure(world, registry, new BlockPosition(20, 3, 3));

            // "A cave or tunnel should get darker the deeper you go in until there is no light."
            Assert.That(mouth, Is.GreaterThan(mid));
            Assert.That(mid, Is.GreaterThan(deep));
            Assert.That(deep, Is.EqualTo(0.0f));
        }

        [Test]
        public void EmitterReachIsBlockedByASolidWall()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 8, 8), chunkSize: 16, seed: 33);

            // Floor at y=0, wall at x=8, glowwick on the floor at x=4.
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 8; z++)
                world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.Graystone, trackChange: false);
            for (int y = 1; y < 8; y++)
            for (int z = 0; z < 8; z++)
                world.SetBlock(new BlockPosition(8, y, z), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(4, 1, 4), BlockRegistry.Glowwick, trackChange: false);

            var emitters = new List<BlockPosition> { new(4, 1, 4) };
            var up = new BlockPosition(0, 1, 0);

            // Floor cell beside the torch (the face's air neighbour is the cell above the floor).
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(3, 1, 4), up, emitters),
                Is.EqualTo(1.0f));
            // Floor cell on the far side of the wall: in range (8 blocks of a 9-block torch) but occluded.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(12, 1, 4), up, emitters),
                Is.EqualTo(0.0f), "Light must not pass through a solid wall.");
        }

        [Test]
        public void EmitterReachIsBlockedByTheGround()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 16, 8), chunkSize: 8, seed: 34);

            // Solid ground from y=0..7, a one-block cave at y=3, a torch on the surface at y=8.
            for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            for (int z = 0; z < 8; z++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(4, 3, 4), BlockRegistry.Air, trackChange: false);
            world.SetBlock(new BlockPosition(4, 8, 4), BlockRegistry.Glowwick, trackChange: false);

            var emitters = new List<BlockPosition> { new(4, 8, 4) };

            // The cave's floor face (looking up into the cave cell) is 5 blocks below the torch —
            // well inside range — with solid rock in between.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(4, 3, 4), new BlockPosition(0, 1, 0), emitters),
                Is.EqualTo(0.0f), "Light must not pass through the ground into a cave.");

            // Whereas the surface right next to the torch is lit.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(3, 8, 4), new BlockPosition(0, 1, 0), emitters),
                Is.EqualTo(1.0f));
        }

        [Test]
        public void EmitterReachIgnoresEmittersBehindTheFace()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 35);
            world.SetBlock(new BlockPosition(4, 0, 4), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(4, 3, 4), BlockRegistry.Glowwick, trackChange: false);

            var emitters = new List<BlockPosition> { new(4, 3, 4) };

            // The stone's TOP face looks up toward the torch: reachable.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(4, 1, 4), new BlockPosition(0, 1, 0), emitters),
                Is.EqualTo(1.0f));
            // Its BOTTOM face looks away from the torch: the realtime light's N·L is zero there,
            // so the bake must agree and not mark it reachable.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(4, -1, 4), new BlockPosition(0, -1, 0), emitters),
                Is.EqualTo(0.0f));
        }

        [Test]
        public void EmitterReachRespectsEachEmittersOwnRange()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 4, 4), chunkSize: 32, seed: 36);
            for (int x = 0; x < 32; x++)
                world.SetBlock(new BlockPosition(x, 0, 1), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(0, 1, 1), BlockRegistry.Glowwick, trackChange: false);

            var emitters = new List<BlockPosition> { new(0, 1, 1) };
            var up = new BlockPosition(0, 1, 0);

            // A level-9 glowwick reaches 9 blocks (+1 margin so the bake never cuts inside the
            // realtime light's own falloff); clear sight all the way but 20 blocks is simply too far.
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(8, 1, 1), up, emitters), Is.EqualTo(1.0f));
            Assert.That(VoxelLightSampler.SampleEmitterReach(world, registry, new BlockPosition(20, 1, 1), up, emitters), Is.EqualTo(0.0f));
        }

        [Test]
        public void VoxelEmitterIndexTracksPlacementRemovalAndRangeQueries()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 37);
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Glowwick, trackChange: false);
            world.SetBlock(new BlockPosition(30, 30, 30), BlockRegistry.Campfire, trackChange: false);

            var index = new VoxelEmitterIndex(world, registry);
            Assert.That(index.Count, Is.EqualTo(2));
            Assert.That(index.Contains(new BlockPosition(2, 2, 2)), Is.True);

            var near = new List<BlockPosition>();
            index.CollectInRange(new BlockPosition(0, 0, 0), new BlockPosition(15, 15, 15), near);
            Assert.That(near, Is.EquivalentTo(new[] { new BlockPosition(2, 2, 2) }),
                "Range queries must not return emitters outside the box even when their chunk overlaps it.");

            // Live changes route through ApplyChange (as ChunkRebuildQueue does).
            world.BlockChanged += change => index.ApplyChange(change);
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Air);
            world.SetBlock(new BlockPosition(5, 5, 5), BlockRegistry.LumenLamp);

            Assert.That(index.Count, Is.EqualTo(2));
            Assert.That(index.Contains(new BlockPosition(2, 2, 2)), Is.False);
            Assert.That(index.Contains(new BlockPosition(5, 5, 5)), Is.True);
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

            registry.TryGet(BlockRegistry.Glowwick,           out BlockDefinition glowwick);
            registry.TryGet(BlockRegistry.Campfire,           out BlockDefinition campfire);
            registry.TryGet(BlockRegistry.LumenLamp,          out BlockDefinition lumenLamp);
            registry.TryGet(BlockRegistry.SparkFlare,         out BlockDefinition sparkFlare);
            registry.TryGet(BlockRegistry.LumenQuartzCluster, out BlockDefinition lumenQuartz);
            registry.TryGet(BlockRegistry.StaropalGeode,      out BlockDefinition staropal);
            registry.TryGet(BlockRegistry.Graystone,          out BlockDefinition graystone);

            Assert.That(glowwick.EmissiveLight,   Is.EqualTo(9));
            Assert.That(campfire.EmissiveLight,   Is.EqualTo(12));
            Assert.That(lumenLamp.EmissiveLight,  Is.EqualTo(14));
            Assert.That(sparkFlare.EmissiveLight, Is.EqualTo(15));

            // Natural cave light (voxel_world_environment_effects.md §5.3). These shipped at 0 for a
            // long time, so caves stayed dark even though GlowwickLightManager already carried
            // bespoke colours for both.
            Assert.That(lumenQuartz.EmissiveLight, Is.EqualTo(7));
            Assert.That(staropal.EmissiveLight,    Is.EqualTo(5));

            Assert.That(graystone.EmissiveLight,  Is.EqualTo(0));
        }

        [Test]
        public void AllCanonicalEmissiveBlocksAreRecognizedAsLightEmitters()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Glowwick,           registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Campfire,           registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.LumenLamp,          registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.SparkFlare,         registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.Emberflow,          registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.LumenQuartzCluster, registry), Is.True);
            Assert.That(GlowwickLightManager.IsLightEmitter(BlockRegistry.StaropalGeode,      registry), Is.True);
        }

        [Test]
        public void CaveCrystalsLightCropsEnoughForReedsAndBerriesButNotGrain()
        {
            // Adopting the canonical 7/5 is a deliberate GAMEPLAY change: ResolveCropGrowthConditions
            // feeds SampleAirLight into CropGrowthConditions.LightLevel and FarmingService gates on
            // MinLight (grain 8, berry 7, reed 5). This pins how far that reaches so it cannot drift
            // into "grain grows underground" unnoticed.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 44);

            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);

            var air = new BlockPosition(4, 4, 4);
            world.SetBlock(air, BlockRegistry.Air, trackChange: false);
            world.SetBlock(new BlockPosition(4, 4, 5), BlockRegistry.LumenQuartzCluster, trackChange: false);

            int quartzLight = Mathf.RoundToInt(
                Mathf.Clamp01(VoxelLightSampler.SampleAirLight(world, registry, air)) * 15.0f);
            Assert.That(quartzLight, Is.EqualTo(7),
                "Lumen quartz should light an adjacent cell to exactly its canonical level.");

            world.SetBlock(new BlockPosition(4, 4, 5), BlockRegistry.StaropalGeode, trackChange: false);
            int staropalLight = Mathf.RoundToInt(
                Mathf.Clamp01(VoxelLightSampler.SampleAirLight(world, registry, air)) * 15.0f);
            Assert.That(staropalLight, Is.EqualTo(5));

            // Berry MinLight 7 and reed MinLight 5 are reachable; grain MinLight 8 is not.
            Assert.That(quartzLight, Is.LessThan(8));
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
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard),
                    "The nearest emitter must own the shadow slot: the voxel shader routes exactly "
                    + "the lights that have a shadow slice past the baked per-face gate.");
                Assert.That(light.shadowStrength, Is.EqualTo(1.0f).Within(0.001f),
                    "The shadow map is this light's only occluder now, so its strength alone "
                    + "decides how dark an emitter shadow is. Anything below 1 leaks punctual "
                    + "light through walls.");

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
