using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class MultiplayerChunkAuthoritySnapshotSerializationEditModeTests
    {
        [Test]
        public void WorldSnapshotHeaderRoundTripsSpawnPosition()
        {
            var expected = new MultiplayerChunkAuthoritySync.WorldSnapshotHeader(
                CreativeWorldGenerationPreset.SurvivalLite,
                width: 128,
                height: 256,
                depth: 128,
                chunkSize: 16,
                seed: 4404,
                groundHeight: 64,
                spawnPosition: new BlockPosition(44, 65, 80),
                hostDeltaSequence: 9u,
                changedBlockCount: 3);

            var writer = new FastBufferWriter(MultiplayerChunkAuthoritySync.WorldSnapshotHeaderBytes, Allocator.Temp);
            try
            {
                MultiplayerChunkAuthoritySync.WriteWorldSnapshotHeader(ref writer, expected);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    bool read = MultiplayerChunkAuthoritySync.TryReadWorldSnapshotHeader(ref reader, out MultiplayerChunkAuthoritySync.WorldSnapshotHeader actual);

                    Assert.That(read, Is.True);
                    Assert.That(actual.GenerationPreset, Is.EqualTo(expected.GenerationPreset));
                    Assert.That(actual.Width, Is.EqualTo(expected.Width));
                    Assert.That(actual.Height, Is.EqualTo(expected.Height));
                    Assert.That(actual.Depth, Is.EqualTo(expected.Depth));
                    Assert.That(actual.ChunkSize, Is.EqualTo(expected.ChunkSize));
                    Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
                    Assert.That(actual.GroundHeight, Is.EqualTo(expected.GroundHeight));
                    Assert.That(actual.SpawnPosition, Is.EqualTo(expected.SpawnPosition));
                    Assert.That(actual.HostDeltaSequence, Is.EqualTo(expected.HostDeltaSequence));
                    Assert.That(actual.ChangedBlockCount, Is.EqualTo(expected.ChangedBlockCount));
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void EnvironmentSnapshotRoundTripsWeatherAndWorldTime()
        {
            var expected = new MultiplayerChunkAuthoritySync.EnvironmentSnapshotState(
                WeatherState.Thunderstorm,
                weatherTicks: 1234,
                weatherRngState: 0x00C0FFEEu,
                worldTimeTicks: 987654L);

            var writer = new FastBufferWriter(MultiplayerChunkAuthoritySync.EnvironmentSnapshotBytes, Allocator.Temp);
            try
            {
                MultiplayerChunkAuthoritySync.WriteEnvironmentSnapshot(ref writer, expected);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    bool read = MultiplayerChunkAuthoritySync.TryReadEnvironmentSnapshot(
                        ref reader,
                        out MultiplayerChunkAuthoritySync.EnvironmentSnapshotState actual);

                    Assert.That(read, Is.True);
                    Assert.That(actual.WeatherState, Is.EqualTo(expected.WeatherState));
                    Assert.That(actual.WeatherTicks, Is.EqualTo(expected.WeatherTicks));
                    Assert.That(actual.WeatherRngState, Is.EqualTo(expected.WeatherRngState));
                    Assert.That(actual.WorldTimeTicks, Is.EqualTo(expected.WorldTimeTicks));
                    Assert.That(MultiplayerChunkAuthoritySync.EnvironmentResyncIntervalSeconds, Is.InRange(1.0f, 10.0f));
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void EnvironmentSnapshotHandlerRestoresWeatherRngAndWorldTimeForClientLockstep()
        {
            var clockObject = new GameObject("Environment Snapshot World Time Clock");
            var worldObject = new GameObject("Environment Snapshot Creative World");
            var syncObject = new GameObject("Environment Snapshot Authority Sync");
            worldObject.SetActive(false);
            syncObject.SetActive(false);
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

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
                    seed: 5505,
                    groundHeight: 4);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                manager.Configure(blockMaterial, -1);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    new FlatBuilderPreset(registry, settings).Generate(),
                    CreativeWorldGenerationPreset.SurvivalLite));

                MultiplayerChunkAuthoritySync sync = syncObject.AddComponent<MultiplayerChunkAuthoritySync>();
                sync.Configure(null, manager);
                SetBoundary(sync, ChunkAuthorityBoundary.ForClient(localClientId: 2, hostClientId: 0));

                var snapshot = new MultiplayerChunkAuthoritySync.EnvironmentSnapshotState(
                    WeatherState.HeavySnow,
                    weatherTicks: 3210,
                    weatherRngState: 0x00BADF00u,
                    worldTimeTicks: 98765L);

                var writer = new FastBufferWriter(MultiplayerChunkAuthoritySync.EnvironmentSnapshotBytes, Allocator.Temp);
                try
                {
                    MultiplayerChunkAuthoritySync.WriteEnvironmentSnapshot(ref writer, snapshot);
                    var reader = new FastBufferReader(writer, Allocator.Temp);
                    try
                    {
                        MethodInfo handler = typeof(MultiplayerChunkAuthoritySync).GetMethod(
                            "HandleEnvironmentSnapshotMessage",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        Assert.That(handler, Is.Not.Null, "The environment snapshot message handler should remain present.");
                        handler.Invoke(sync, new object[] { 0ul, reader });
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
                finally
                {
                    writer.Dispose();
                }

                CreativeWorldManager.WeatherSyncState restored = manager.GetWeatherSyncState();
                Assert.That(sync.Diagnostics.AppliedEnvironmentSnapshotCount, Is.EqualTo(1));
                Assert.That(restored.State, Is.EqualTo(snapshot.WeatherState));
                Assert.That(restored.Ticks, Is.EqualTo(snapshot.WeatherTicks));
                Assert.That(restored.RngState, Is.EqualTo(snapshot.WeatherRngState));
                Assert.That(manager.WorldTimeClock.TotalElapsedTicks, Is.EqualTo(snapshot.WorldTimeTicks));

                var hostWeather = new WeatherService(unchecked((uint)settings.Seed), WeatherState.Clear);
                hostWeather.RestoreState(snapshot.WeatherState, snapshot.WeatherTicks, snapshot.WeatherRngState);

                AdvanceWorldTicks(manager, 2500);
                hostWeather.Tick(2500);

                CreativeWorldManager.WeatherSyncState advanced = manager.GetWeatherSyncState();
                Assert.That(advanced.State, Is.EqualTo(hostWeather.CurrentState));
                Assert.That(advanced.Ticks, Is.EqualTo(hostWeather.TicksInCurrentState));
                Assert.That(advanced.RngState, Is.EqualTo(hostWeather.RngState));
                Assert.That(manager.WorldTimeClock.TotalElapsedTicks, Is.EqualTo(snapshot.WorldTimeTicks + 2500L));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
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
        public void ChunkDeltaSequenceGuardWaitsForFutureDeltaAndIgnoresStaleReplay()
        {
            var worldObject = new GameObject("Chunk Delta Guard Creative World");
            var syncObject = new GameObject("Chunk Delta Guard Authority Sync");
            worldObject.SetActive(false);
            syncObject.SetActive(false);
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
                    seed: 6606,
                    groundHeight: 4);
                VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();
                var position = new BlockPosition(2, 2, 2);
                BlockId original = world.GetBlock(position);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                manager.Configure(blockMaterial, -1);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    world,
                    CreativeWorldGenerationPreset.SurvivalLite));

                MultiplayerChunkAuthoritySync sync = syncObject.AddComponent<MultiplayerChunkAuthoritySync>();
                sync.Configure(null, manager);

                MethodInfo tryApply = typeof(MultiplayerChunkAuthoritySync).GetMethod(
                    "TryApplyChunkDelta",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(tryApply, Is.Not.Null, "The chunk delta sequence guard should remain present.");

                ChunkCoordinate chunk = ChunkCoordinate.FromBlockPosition(position, world.ChunkSize);
                var futureDelta = new ChunkDelta(
                    2u,
                    chunk,
                    new BlockChange(position, original, BlockRegistry.LumenQuartzCluster));
                object futureResult = tryApply.Invoke(sync, new object[] { futureDelta });

                Assert.That(futureResult.ToString(), Is.EqualTo("WaitingForEarlierDelta"));
                Assert.That(world.GetBlock(position), Is.EqualTo(original));
                Assert.That(sync.Diagnostics.LastAppliedChunkDeltaSequence, Is.EqualTo(0u));
                Assert.That(sync.Diagnostics.AppliedChunkDeltaCount, Is.EqualTo(0));

                var firstDelta = new ChunkDelta(
                    1u,
                    chunk,
                    new BlockChange(position, original, BlockRegistry.Graystone));
                object firstResult = tryApply.Invoke(sync, new object[] { firstDelta });

                Assert.That(firstResult.ToString(), Is.EqualTo("Applied"));
                Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Graystone));
                Assert.That(sync.Diagnostics.LastAppliedChunkDeltaSequence, Is.EqualTo(1u));
                Assert.That(sync.Diagnostics.AppliedChunkDeltaCount, Is.EqualTo(1));

                object staleResult = tryApply.Invoke(sync, new object[] { firstDelta });

                Assert.That(staleResult.ToString(), Is.EqualTo("IgnoredStale"));
                Assert.That(sync.Diagnostics.IgnoredOutOfOrderChunkDeltaCount, Is.EqualTo(1));
                Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Graystone));

                object secondResult = tryApply.Invoke(sync, new object[] { futureDelta });

                Assert.That(secondResult.ToString(), Is.EqualTo("Applied"));
                Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
                Assert.That(sync.Diagnostics.LastAppliedChunkDeltaSequence, Is.EqualTo(2u));
                Assert.That(sync.Diagnostics.AppliedChunkDeltaCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
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

        static void SetBoundary(MultiplayerChunkAuthoritySync sync, ChunkAuthorityBoundary boundary)
        {
            FieldInfo field = typeof(MultiplayerChunkAuthoritySync).GetField(
                "<CurrentBoundary>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "CurrentBoundary should stay backed by an auto-property.");
            field.SetValue(sync, boundary);
        }

        static void AdvanceWorldTicks(CreativeWorldManager manager, int ticks)
        {
            manager.WorldTimeClock.RestoreElapsedTicks(manager.WorldTimeClock.TotalElapsedTicks + ticks);

            MethodInfo onWorldTick = typeof(CreativeWorldManager).GetMethod(
                "OnWorldTick",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(onWorldTick, Is.Not.Null, "CreativeWorldManager should keep its world-tick handler.");
            onWorldTick.Invoke(manager, new object[] { ticks });
        }
    }
}
