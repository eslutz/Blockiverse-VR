using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CreativeWorldManagerCropTrackingEditModeTests
    {
        static readonly BlockPosition SoilPos = new(2, 3, 2);
        static readonly BlockPosition CropPos = new(2, 4, 2);

        [Test]
        public void CropStageAdvanceThroughBlockChangedDoesNotReanchorGrowthInterval()
        {
            // A world seed whose favorable deterministic rolls (grain base chance 0.35, see
            // FarmingService.GrainProfile) pass at interval 1 for stage 0 and at interval 2 for
            // stage 1, so a crop with a preserved anchor must advance exactly one stage per
            // interval. The roll recipe matches FarmingService.DeterministicRoll.
            int seed = -1;
            for (int candidate = 0; candidate < 1024; candidate++)
            {
                if (DeterministicHash.UnitRoll(candidate, CropPos.X, CropPos.Y, CropPos.Z, BlockRegistry.GrainStalk.Value, 1L) < 0.35 &&
                    DeterministicHash.UnitRoll(candidate, CropPos.X, CropPos.Y, CropPos.Z, BlockRegistry.GrainStalk_S1.Value, 2L) < 0.35)
                {
                    seed = candidate;
                    break;
                }
            }

            Assert.That(seed, Is.GreaterThanOrEqualTo(0), "Expected a seed whose growth rolls pass at intervals 1 and 2.");

            var clockObject = new GameObject("Crop Tracking World Time Clock");
            var worldObject = new GameObject("Crop Tracking Creative World");
            worldObject.SetActive(false);
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                WorldTimeClock clock = clockObject.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 1200.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                manager.Configure(blockMaterial, -1);

                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: seed,
                    groundHeight: 4);
                VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    world,
                    CreativeWorldGenerationPreset.SurvivalLite));

                Assert.That(manager.WorldTimeClock, Is.Not.Null,
                    "The manager should wire its farming/vegetation services to a scene WorldTimeClock.");

                // EditMode never runs WorldTimeClock.Update, so drive the manager's Ticked handler
                // directly while keeping the clock's absolute tick count consistent — the same
                // (advance ticks, raise handler) sequence the runtime Update performs.
                MethodInfo onWorldTick = typeof(CreativeWorldManager).GetMethod(
                    "OnWorldTick",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(onWorldTick, Is.Not.Null, "CreativeWorldManager should keep its world-tick handler.");

                void AdvanceWorldTicks(int ticks)
                {
                    WorldTimeClock activeClock = manager.WorldTimeClock;
                    activeClock.RestoreElapsedTicks(activeClock.TotalElapsedTicks + ticks);
                    onWorldTick.Invoke(manager, new object[] { ticks });
                }

                // Plant through the live world so World.BlockChanged drives the manager's crop
                // tracking, exactly like a player planting at runtime.
                world.SetBlock(SoilPos, BlockRegistry.TendedSoil);
                world.SetBlock(CropPos, BlockRegistry.GrainStalk);

                // The first tick after planting anchors the crop at interval 0 without crossing
                // a growth boundary (see FarmingService.TickGrowth).
                AdvanceWorldTicks(100);
                Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                    "No growth boundary has been crossed yet.");

                // Crossing the first interval boundary advances stage 0 → 1. That stage advance is
                // a crop→crop SetBlock raised through World.BlockChanged, which must NOT re-anchor
                // the crop's growth interval.
                AdvanceWorldTicks(FarmingService.GrowthIntervalTicks - 100);
                Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1),
                    "The first interval boundary should advance the crop to stage 1.");

                // Exactly one more interval must yield exactly one more stage. If the crop→crop
                // change had re-anchored (the OnBlockChanged regression this guards against), the
                // pending anchor would swallow this boundary and the crop would stay at stage 1.
                AdvanceWorldTicks(FarmingService.GrowthIntervalTicks);
                Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S2),
                    "A crop→crop stage advance must not re-anchor the growth interval.");
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(clockObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                // InitializeGeneratedWorld ensures scene lighting, which creates the sun object
                // (and its own WorldTimeClock); remove it so later tests see a clean scene.
                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        static Material CreateBlockAtlasMaterial(out Texture2D atlasTexture)
        {
            atlasTexture = new Texture2D(
                BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels,
                BlockVisualAtlas.Rows * BlockVisualAtlas.TilePixels,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = BlockVisualAtlas.AuthoredAtlasName
            };

            Material material = new(Shader.Find("Sprites/Default"));
            material.mainTexture = atlasTexture;
            return material;
        }
    }
}
