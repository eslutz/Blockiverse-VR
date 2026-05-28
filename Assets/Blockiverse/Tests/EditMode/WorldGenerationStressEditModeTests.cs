using System.Diagnostics;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    /// <summary>
    /// Stress coverage for the largest world the game ships (default survival-lite preset):
    /// generation and a full chunk-mesh pass must complete deterministically and stay bounded.
    /// This is the CPU-side proxy for the Quest performance budget; headset captures are recorded
    /// separately under docs/testing/performance.
    /// </summary>
    public sealed class WorldGenerationStressEditModeTests
    {
        [Test]
        public void DefaultSurvivalWorldGeneratesAndMeshesWithoutErrors()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite();
            var preset = new SurvivalLiteWorldPreset(registry, settings);

            var stopwatch = Stopwatch.StartNew();
            VoxelWorld world = preset.Generate();
            long totalTriangles = MeshEveryChunk(world, registry, out int chunkCount);
            stopwatch.Stop();

            Assert.That(chunkCount, Is.GreaterThan(0));
            Assert.That(totalTriangles, Is.GreaterThan(0));
            UnityEngine.Debug.Log(
                $"[StressTest] chunks={chunkCount} triangles={totalTriangles} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }

        [Test]
        public void ChunkMeshingIsDeterministicForRepeatedBuilds()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite();
            VoxelWorld world = new SurvivalLiteWorldPreset(registry, settings).Generate();
            var chunk = new ChunkCoordinate(
                settings.Bounds.Width / (2 * settings.ChunkSize),
                0,
                settings.Bounds.Depth / (2 * settings.ChunkSize));

            ChunkMeshData first = ChunkMeshBuilder.Build(world, registry, chunk);
            ChunkMeshData second = ChunkMeshBuilder.Build(world, registry, chunk);

            Assert.That(second.Vertices.Count, Is.EqualTo(first.Vertices.Count));
            Assert.That(second.Triangles.Count, Is.EqualTo(first.Triangles.Count));
            Assert.That(second.TriangleCount, Is.EqualTo(first.TriangleCount));
        }

        [Test]
        public void GeneratedWorldIsReproducibleForSameSeed()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed: 4242);

            long first = MeshEveryChunk(new SurvivalLiteWorldPreset(registry, settings).Generate(), registry, out _);
            long second = MeshEveryChunk(new SurvivalLiteWorldPreset(registry, settings).Generate(), registry, out _);

            Assert.That(second, Is.EqualTo(first));
        }

        static long MeshEveryChunk(VoxelWorld world, BlockRegistry registry, out int chunkCount)
        {
            int chunksX = Mathf.CeilToInt(world.Bounds.Width / (float)world.ChunkSize);
            int chunksY = Mathf.CeilToInt(world.Bounds.Height / (float)world.ChunkSize);
            int chunksZ = Mathf.CeilToInt(world.Bounds.Depth / (float)world.ChunkSize);

            long totalTriangles = 0;
            chunkCount = 0;

            for (int y = 0; y < chunksY; y++)
            {
                for (int z = 0; z < chunksZ; z++)
                {
                    for (int x = 0; x < chunksX; x++)
                    {
                        ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(x, y, z));
                        totalTriangles += mesh.TriangleCount;
                        chunkCount++;
                    }
                }
            }

            return totalTriangles;
        }
    }
}
