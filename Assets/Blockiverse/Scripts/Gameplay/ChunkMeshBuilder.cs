using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class ChunkMeshData
    {
        public ChunkMeshData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, int faceCount)
        {
            Vertices = vertices;
            Triangles = triangles;
            Uvs = uvs;
            FaceCount = faceCount;
        }

        public List<Vector3> Vertices { get; }
        public List<int> Triangles { get; }
        public List<Vector2> Uvs { get; }
        public int FaceCount { get; }

        public int TriangleCount => Triangles.Count / 3;
    }

    public static class ChunkMeshBuilder
    {
        static readonly BlockPosition[] NeighborOffsets =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 1, 0),
            new(0, -1, 0),
            new(0, 0, 1),
            new(0, 0, -1)
        };

        static readonly Vector3[,] FaceVertices =
        {
            { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) },
            { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) },
            { new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0) },
            { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) },
            { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) },
            { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) }
        };

        static readonly Vector2[] FaceUvs =
        {
            new(0, 0),
            new(0, 1),
            new(1, 1),
            new(1, 0)
        };

        public static ChunkMeshData Build(VoxelWorld world, BlockRegistry registry, ChunkCoordinate chunk)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            int faceCount = 0;

            int startX = chunk.X * world.ChunkSize;
            int startY = chunk.Y * world.ChunkSize;
            int startZ = chunk.Z * world.ChunkSize;
            int endX = Math.Min(startX + world.ChunkSize, world.Bounds.Width);
            int endY = Math.Min(startY + world.ChunkSize, world.Bounds.Height);
            int endZ = Math.Min(startZ + world.ChunkSize, world.Bounds.Depth);

            for (int y = Math.Max(0, startY); y < endY; y++)
            {
                for (int z = Math.Max(0, startZ); z < endZ; z++)
                {
                    for (int x = Math.Max(0, startX); x < endX; x++)
                    {
                        var position = new BlockPosition(x, y, z);
                        BlockDefinition definition = registry.Get(world.GetBlock(position));

                        if (!definition.IsRenderable)
                            continue;

                        for (int face = 0; face < NeighborOffsets.Length; face++)
                        {
                            BlockPosition neighbor = position + NeighborOffsets[face];

                            if (!ShouldRenderFace(world, registry, neighbor))
                                continue;

                            AddFace(vertices, triangles, uvs, position, face);
                            faceCount++;
                        }
                    }
                }
            }

            return new ChunkMeshData(vertices, triangles, uvs, faceCount);
        }

        static bool ShouldRenderFace(VoxelWorld world, BlockRegistry registry, BlockPosition neighbor)
        {
            if (!world.Bounds.Contains(neighbor))
                return true;

            BlockDefinition neighborDefinition = registry.Get(world.GetBlock(neighbor));
            return !neighborDefinition.IsRenderable || !neighborDefinition.IsSolid;
        }

        static void AddFace(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, BlockPosition position, int faceIndex)
        {
            int vertexStart = vertices.Count;

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = FaceVertices[faceIndex, i];
                vertices.Add(new Vector3(position.X, position.Y, position.Z) + corner);
                uvs.Add(FaceUvs[i]);
            }

            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);
        }
    }

    public sealed class ChunkRebuildQueue
    {
        readonly VoxelWorld world;
        readonly HashSet<ChunkCoordinate> dirtyChunks = new();

        public ChunkRebuildQueue(VoxelWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            world.BlockChanged += OnBlockChanged;
        }

        public int Count => dirtyChunks.Count;

        public void MarkDirty(ChunkCoordinate chunk)
        {
            dirtyChunks.Add(chunk);
        }

        public IReadOnlyCollection<ChunkCoordinate> DrainDirtyChunks()
        {
            var drained = new List<ChunkCoordinate>(dirtyChunks);
            dirtyChunks.Clear();
            return drained;
        }

        void OnBlockChanged(BlockChange change)
        {
            ChunkCoordinate changedChunk = world.GetChunkCoordinate(change.Position);
            MarkDirty(changedChunk);

            BlockPosition local = ChunkCoordinate.LocalPositionFromBlockPosition(change.Position, world.ChunkSize);
            MarkNeighborIfNeeded(local.X == 0, change.Position + new BlockPosition(-1, 0, 0));
            MarkNeighborIfNeeded(local.X == world.ChunkSize - 1, change.Position + new BlockPosition(1, 0, 0));
            MarkNeighborIfNeeded(local.Y == 0, change.Position + new BlockPosition(0, -1, 0));
            MarkNeighborIfNeeded(local.Y == world.ChunkSize - 1, change.Position + new BlockPosition(0, 1, 0));
            MarkNeighborIfNeeded(local.Z == 0, change.Position + new BlockPosition(0, 0, -1));
            MarkNeighborIfNeeded(local.Z == world.ChunkSize - 1, change.Position + new BlockPosition(0, 0, 1));
        }

        void MarkNeighborIfNeeded(bool condition, BlockPosition neighbor)
        {
            if (!condition || !world.Bounds.Contains(neighbor))
                return;

            MarkDirty(world.GetChunkCoordinate(neighbor));
        }
    }

    public readonly struct VoxelRenderStats
    {
        public VoxelRenderStats(int chunkCount, int triangleCount, int queuedRebuildCount)
        {
            ChunkCount = chunkCount;
            TriangleCount = triangleCount;
            QueuedRebuildCount = queuedRebuildCount;
        }

        public int ChunkCount { get; }
        public int TriangleCount { get; }
        public int QueuedRebuildCount { get; }
    }
}
