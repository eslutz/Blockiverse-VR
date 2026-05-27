using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class ChunkRenderingEditModeTests
    {
        [Test]
        public void MeshBuilderEmitsOnlyExteriorFacesForSingleSolidBlock()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 1);
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Slate, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Assert.That(mesh.FaceCount, Is.EqualTo(6));
            Assert.That(mesh.Vertices, Has.Count.EqualTo(24));
            Assert.That(mesh.Triangles, Has.Count.EqualTo(36));
            Assert.That(mesh.Uvs, Has.Count.EqualTo(24));
        }

        [Test]
        public void MeshBuilderRemovesInternalFacesBetweenAdjacentSolidBlocks()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 1);
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Slate, trackChange: false);
            world.SetBlock(new BlockPosition(2, 1, 1), BlockRegistry.Loam, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Assert.That(mesh.FaceCount, Is.EqualTo(10));
            Assert.That(mesh.Triangles, Has.Count.EqualTo(60));
        }

        [Test]
        public void DirtyChunkQueueMarksOnlyMutatedChunkAwayFromBorders()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(4, 1, 4), BlockRegistry.Slate);

            CollectionAssert.AreEquivalent(
                new[] { new ChunkCoordinate(0, 0, 0) },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void DirtyChunkQueueMarksNeighborChunkWhenBorderBlockChanges()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(15, 1, 4), BlockRegistry.Slate);

            CollectionAssert.AreEquivalent(
                new[] { new ChunkCoordinate(0, 0, 0), new ChunkCoordinate(1, 0, 0) },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void RenderStatsReportChunkTriangleAndQueueCounts()
        {
            var stats = new VoxelRenderStats(chunkCount: 4, triangleCount: 120, queuedRebuildCount: 2);

            Assert.That(stats.ChunkCount, Is.EqualTo(4));
            Assert.That(stats.TriangleCount, Is.EqualTo(120));
            Assert.That(stats.QueuedRebuildCount, Is.EqualTo(2));
        }

        [Test]
        public void RendererDestroysReplacedChunkMeshesAfterDirtyRebuild()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            var editedPosition = new BlockPosition(1, 0, 1);
            world.SetBlock(editedPosition, BlockRegistry.MeadowTurf, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;
            Mesh firstMesh = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                MeshFilter filter = worldObject.GetComponentInChildren<MeshFilter>();
                firstMesh = filter.sharedMesh;
                Assert.That(firstMesh, Is.Not.Null);

                world.SetBlock(editedPosition, BlockRegistry.Air);
                renderer.RebuildDirty();

                Assert.That(filter.sharedMesh, Is.Not.SameAs(firstMesh));
                Assert.That(firstMesh == null, Is.True, "Expected the replaced chunk mesh to be destroyed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);

                if (firstMesh != null)
                    UnityEngine.Object.DestroyImmediate(firstMesh);
            }
        }

        [Test]
        public void RendererConfigureLogsOneDevelopmentRebuildSummary()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.MeadowTurf, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            var sink = new CapturingLogSink();
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                BlockiverseLog.SetSinkForTesting(sink);
                BlockiverseLog.DevelopmentInfoEnabled = true;
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);

                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Renderer &&
                    log.Level == LogType.Log &&
                    log.Message.Contains("Rebuilt all chunks"));
                Assert.That(entry.Message, Does.Contain("chunks=1"));
                Assert.That(entry.Message, Does.Contain("triangles=12"));
                Assert.That(entry.Message, Does.Contain("queuedRebuilds=0"));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void RendererStatsRefreshDoesNotReadMeshTriangleArray()
        {
            MethodInfo refreshStats = typeof(VoxelWorldRenderer).GetMethod(
                "RefreshStats",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(refreshStats, Is.Not.Null);
            Assert.That(CallsMethod(refreshStats, typeof(Mesh), "get_triangles"), Is.False);
        }

        [Test]
        public void VisualAtlasContainsDistinctTilesForEveryRenderableBlock()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            BlockDefinition[] renderableBlocks = registry.All
                .Where(block => block.IsRenderable)
                .ToArray();

            Rect[] tileRects = renderableBlocks
                .Select(block => BlockVisualAtlas.GetTileRect(block.Id))
                .ToArray();

            Assert.That(tileRects, Has.Length.EqualTo(renderableBlocks.Length));
            Assert.That(tileRects.Distinct().Count(), Is.EqualTo(renderableBlocks.Length));
            Assert.That(tileRects.All(rect => rect.width > 0.0f && rect.height > 0.0f), Is.True);
        }

        [Test]
        public void VisualAtlasRejectsRenderableBlocksWithoutTileMapping()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            registry.Register(new BlockDefinition(new BlockId(99), "Missing Tile", BlockCategory.Crafted, isSolid: true, isRenderable: true));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                BlockVisualAtlas.ValidateRenderableBlockCoverage(registry));

            Assert.That(exception.Message, Does.Contain("Missing Tile"));
        }

        [Test]
        public void MeshBuilderUsesBlockSpecificAtlasUvs()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 16, seed: 1);
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.MeadowTurf, trackChange: false);
            world.SetBlock(new BlockPosition(5, 1, 1), BlockRegistry.Slate, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Rect meadowRect = BlockVisualAtlas.GetTileRect(BlockRegistry.MeadowTurf);
            Rect slateRect = BlockVisualAtlas.GetTileRect(BlockRegistry.Slate);

            Assert.That(mesh.Uvs.Any(uv => IsInside(uv, meadowRect)), Is.True);
            Assert.That(mesh.Uvs.Any(uv => IsInside(uv, slateRect)), Is.True);
        }

        [Test]
        public void MeshBuilderDoesNotAllocateUvArraysPerFace()
        {
            MethodInfo addFace = typeof(ChunkMeshBuilder).GetMethod(
                "AddFace",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(addFace, Is.Not.Null);
            Assert.That(ContainsNewArrayInstructionFor(addFace, typeof(Vector2)), Is.False);
        }

        static bool IsInside(Vector2 uv, Rect rect)
        {
            return uv.x >= rect.xMin &&
                   uv.x <= rect.xMax &&
                   uv.y >= rect.yMin &&
                   uv.y <= rect.yMax;
        }

        static bool ContainsNewArrayInstructionFor(MethodInfo method, Type elementType)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();

            for (int i = 0; i <= il.Length - 5; i++)
            {
                if (il[i] != 0x8D)
                    continue;

                int metadataToken = BitConverter.ToInt32(il, i + 1);

                try
                {
                    if (method.Module.ResolveType(metadataToken) == elementType)
                        return true;
                }
                catch (ArgumentException)
                {
                    // Operand bytes can look like opcodes when scanning raw IL.
                }
            }

            return false;
        }

        static bool CallsMethod(MethodInfo method, Type declaringType, string methodName)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();

            for (int i = 0; i <= il.Length - 5; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F)
                    continue;

                int metadataToken = BitConverter.ToInt32(il, i + 1);

                try
                {
                    MethodBase calledMethod = method.Module.ResolveMethod(metadataToken);
                    if (calledMethod.DeclaringType == declaringType && calledMethod.Name == methodName)
                        return true;
                }
                catch (ArgumentException)
                {
                    // Operand bytes can look like opcodes when scanning raw IL.
                }
            }

            return false;
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

        sealed class CapturingLogSink : IBlockiverseLogSink
        {
            public readonly List<BlockiverseLogEntry> Entries = new();

            public void Log(BlockiverseLogEntry entry)
            {
                Entries.Add(entry);
            }
        }
    }
}
