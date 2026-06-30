using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CreativeWorldManagerCropTrackingEditModeTests
    {
        static readonly BlockPosition SoilPos = new(2, 3, 2);
        static readonly BlockPosition CropPos = new(2, 4, 2);
        static readonly BlockPosition SaplingPos = new(3, 4, 3);
        static readonly BlockPosition WildPlantPos = new(4, 4, 4);
        static readonly BlockPosition BerrybushPos = new(5, 4, 5);
        static readonly BlockPosition GeneratedContainerPos = new(6, 4, 6);
        static readonly BlockPosition SavedContainerPos = new(7, 4, 7);

        [Test]
        public void NetworkWeatherSnapshotSurvivesAuthoritativeWorldReplacement()
        {
            var clockObject = new GameObject("Weather Sync World Time Clock");
            var worldObject = new GameObject("Weather Sync Creative World");
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
                var initialSettings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: 101,
                    groundHeight: 4);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    initialSettings,
                    new FlatBuilderPreset(registry, initialSettings).Generate(),
                    CreativeWorldGenerationPreset.SurvivalLite));

                var networkWeather = new WeatherSyncState(
                    WeatherState.HeavyRain,
                    ticks: 4321,
                    rngState: 0x12345678u);
                manager.RestoreWeatherSyncState(networkWeather, preserveForNextWorldInitialization: true);

                var authoritativeSettings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: 202,
                    groundHeight: 4);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    authoritativeSettings,
                    new FlatBuilderPreset(registry, authoritativeSettings).Generate(),
                    CreativeWorldGenerationPreset.SurvivalLite));

                WeatherSyncState restored = manager.GetWeatherSyncState();
                Assert.That(restored.State, Is.EqualTo(networkWeather.State));
                Assert.That(restored.Ticks, Is.EqualTo(networkWeather.Ticks));
                Assert.That(restored.RngState, Is.EqualTo(networkWeather.RngState));
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(clockObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

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
                world.SetBlock(new BlockPosition(SoilPos.X + 1, SoilPos.Y, SoilPos.Z), BlockRegistry.Freshwater);
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

        [Test]
        public void CropGrowthUsesWorldMoistureConditionsInsteadOfAlwaysFavorableFallback()
        {
            // Pick a seed whose first grain roll would pass under favorable conditions (0.35) but
            // fail under dry-soil conditions (0.35 * 0.25). If the manager ever falls back to
            // always-favorable conditions again, this crop will incorrectly advance.
            int seed = -1;
            double dryChance = 0.35 * FarmingService.UnfavorableGrowthMultiplier;
            for (int candidate = 0; candidate < 1024; candidate++)
            {
                double roll = DeterministicHash.UnitRoll(candidate, CropPos.X, CropPos.Y, CropPos.Z, BlockRegistry.GrainStalk.Value, 1L);
                if (roll >= dryChance && roll < 0.35)
                {
                    seed = candidate;
                    break;
                }
            }

            Assert.That(seed, Is.GreaterThanOrEqualTo(0), "Expected a seed that distinguishes favorable and dry growth.");

            var clockObject = new GameObject("Dry Crop World Time Clock");
            var worldObject = new GameObject("Dry Crop Creative World");
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

                world.SetBlock(SoilPos, BlockRegistry.TendedSoil);
                Assert.That(FarmingService.HasFreshwaterNearby(world, SoilPos), Is.False);
                world.SetBlock(CropPos, BlockRegistry.GrainStalk);

                AdvanceWorldTicks(manager, 1);
                AdvanceWorldTicks(manager, FarmingService.GrowthIntervalTicks - 1);

                Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                    "Dry soil should apply the unfavorable growth multiplier instead of using the always-favorable fallback.");
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(clockObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void SaveExtrasRoundTripWorldSimulationQueues()
        {
            var clockObject = new GameObject("Save Extras World Time Clock");
            var worldObject = new GameObject("Save Extras Creative World");
            var restoredObject = new GameObject("Restored Save Extras Creative World");
            worldObject.SetActive(false);
            restoredObject.SetActive(false);
            Texture2D atlasTexture = null;
            Texture2D restoredAtlasTexture = null;
            Material blockMaterial = null;
            Material restoredBlockMaterial = null;

            try
            {
                WorldTimeClock clock = clockObject.AddComponent<WorldTimeClock>();
                clock.Configure(dayLengthSeconds: 1200.0f, startNormalizedTime: 0.25f, timeScale: 1.0f);

                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: 303,
                    groundHeight: 4);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                manager.Configure(blockMaterial, -1);
                VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    world,
                    CreativeWorldGenerationPreset.SurvivalLite));

                world.SetBlock(SaplingPos, BlockRegistry.Sapling);
                AdvanceWorldTicks(manager, 37);
                world.SetBlock(WildPlantPos, BlockRegistry.Reedgrass);
                world.SetBlock(WildPlantPos, BlockRegistry.Air);
                world.SetBlock(BerrybushPos, BlockRegistry.Berrybush_S5);
                world.SetBlock(BerrybushPos, BlockRegistry.Air);
                AdvanceWorldTicks(manager, 45);

                var weather = new WeatherSyncState(
                    WeatherState.HeavyRain,
                    ticks: 4321,
                    rngState: 0x00ABCDEFu);
                manager.RestoreWeatherSyncState(weather);

                var extras = new WorldSaveExtras();
                manager.FillSaveExtras(extras);

                Assert.That(extras.WeatherTicksInState, Is.EqualTo(weather.Ticks));
                Assert.That(extras.WeatherRngState, Is.EqualTo(weather.RngState));
                VxlwSaplingProgress savedSapling = extras.Saplings.Single();
                Assert.That(new BlockPosition(savedSapling.X, savedSapling.Y, savedSapling.Z), Is.EqualTo(SaplingPos));
                Assert.That(savedSapling.AccumulatedTicks, Is.EqualTo(82));
                VxlwWildRegrowthMarker savedWild = extras.WildRegrowth.Single();
                Assert.That(savedWild.CanonicalId, Is.EqualTo("reedgrass"));
                Assert.That(new BlockPosition(savedWild.X, savedWild.Y, savedWild.Z), Is.EqualTo(WildPlantPos));
                Assert.That(savedWild.RegrowAfterTick, Is.GreaterThan(0L));
                VxlwBerrybushRegrowth savedBerrybush = extras.BerrybushRegrowth.Single();
                Assert.That(new BlockPosition(savedBerrybush.X, savedBerrybush.Y, savedBerrybush.Z), Is.EqualTo(BerrybushPos));
                Assert.That(savedBerrybush.AccumulatedTicks, Is.EqualTo(45));

                CreativeWorldManager restored = restoredObject.AddComponent<CreativeWorldManager>();
                restoredBlockMaterial = CreateBlockAtlasMaterial(out restoredAtlasTexture);
                restored.Configure(restoredBlockMaterial, -1);
                restored.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    new FlatBuilderPreset(registry, settings).Generate(),
                    CreativeWorldGenerationPreset.SurvivalLite));
                restored.RestoreSimulationState(new WorldSaveData
                {
                    WeatherState = weather.State.ToString(),
                    WeatherTicksInState = extras.WeatherTicksInState,
                    WeatherRngState = extras.WeatherRngState,
                    Saplings = extras.Saplings,
                    WildRegrowth = extras.WildRegrowth,
                    BerrybushRegrowth = extras.BerrybushRegrowth
                });

                WeatherSyncState restoredWeather = restored.GetWeatherSyncState();
                Assert.That(restoredWeather.State, Is.EqualTo(weather.State));
                Assert.That(restoredWeather.Ticks, Is.EqualTo(weather.Ticks));
                Assert.That(restoredWeather.RngState, Is.EqualTo(weather.RngState));

                var restoredExtras = new WorldSaveExtras();
                restored.FillSaveExtras(restoredExtras);
                Assert.That(restoredExtras.Saplings.Single().AccumulatedTicks, Is.EqualTo(savedSapling.AccumulatedTicks));
                Assert.That(restoredExtras.WildRegrowth.Single().CanonicalId, Is.EqualTo(savedWild.CanonicalId));
                Assert.That(restoredExtras.WildRegrowth.Single().RegrowAfterTick, Is.EqualTo(savedWild.RegrowAfterTick));
                Assert.That(restoredExtras.BerrybushRegrowth.Single().AccumulatedTicks, Is.EqualTo(savedBerrybush.AccumulatedTicks));
            }
            finally
            {
                Object.DestroyImmediate(restoredObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(clockObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);
                if (restoredBlockMaterial != null)
                    Object.DestroyImmediate(restoredBlockMaterial);
                if (restoredAtlasTexture != null)
                    Object.DestroyImmediate(restoredAtlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void ContainerStoreRestoreReplacesGeneratedLootAndLootFlowRaisesEvent()
        {
            var worldObject = new GameObject("Container Creative World");
            worldObject.SetActive(false);
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: 404,
                    groundHeight: 4);
                VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();
                world.SetBlock(GeneratedContainerPos, BlockRegistry.StorageCrate);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                manager.Configure(blockMaterial, -1);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    world,
                    CreativeWorldGenerationPreset.SurvivalLite,
                    new[]
                    {
                        new StructureContainerLoot(
                            GeneratedContainerPos,
                            new[] { new ContainerLootItem(ItemId.ReedFiber.Value, 5) })
                    }));

                Assert.That(manager.ContainerStore.Contains(GeneratedContainerPos), Is.True);
                Assert.That(manager.ContainerStore.GetOrNull(GeneratedContainerPos).CountOf(ItemId.ReedFiber), Is.EqualTo(5));

                var savedContainers = new List<(BlockPosition position, IEnumerable<(string itemId, int count, int durability)> items)>
                {
                    (SavedContainerPos, new (string itemId, int count, int durability)[] { (ItemId.StoutPole.Value, 2, 0) })
                };
                manager.RestoreContainerStore(savedContainers);

                Assert.That(manager.ContainerStore.Contains(GeneratedContainerPos), Is.False);
                Assert.That(manager.ContainerStore.Contains(SavedContainerPos), Is.True);
                Assert.That(manager.ContainerStore.GetOrNull(SavedContainerPos).CountOf(ItemId.StoutPole), Is.EqualTo(2));

                int lootEvents = 0;
                BlockPosition lootedPosition = default;
                manager.ContainerLooted += position =>
                {
                    lootEvents++;
                    lootedPosition = position;
                };

                var target = new Inventory(ItemRegistry.CreateDefault(), slotCount: 10, hotbarSlotCount: 1);
                Assert.That(manager.TryLootContainerInto(SavedContainerPos, target), Is.True);
                Assert.That(target.CountOf(ItemId.StoutPole), Is.EqualTo(2));
                Assert.That(manager.ContainerStore.Contains(SavedContainerPos), Is.False);
                Assert.That(lootEvents, Is.EqualTo(1));
                Assert.That(lootedPosition, Is.EqualTo(SavedContainerPos));

                manager.RestoreContainerStore(null);
                Assert.That(manager.ContainerStore.Count, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(worldObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        static Material CreateBlockAtlasMaterial(out Texture2D atlasTexture)
        {
            atlasTexture = new Texture2D(
                BlockVisualAtlas.AtlasWidthPixels,
                BlockVisualAtlas.AtlasHeightPixels,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = BlockVisualAtlas.AuthoredAtlasName
            };

            Material material = new(Shader.Find("Sprites/Default"));
            material.mainTexture = atlasTexture;
            return material;
        }

        static void AdvanceWorldTicks(CreativeWorldManager manager, int ticks)
        {
            WorldTimeClock activeClock = manager.WorldTimeClock;
            activeClock.RestoreElapsedTicks(activeClock.TotalElapsedTicks + ticks);

            MethodInfo onWorldTick = typeof(CreativeWorldManager).GetMethod(
                "OnWorldTick",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(onWorldTick, Is.Not.Null, "CreativeWorldManager should keep its world-tick handler.");
            onWorldTick.Invoke(manager, new object[] { ticks });
        }
    }
}
