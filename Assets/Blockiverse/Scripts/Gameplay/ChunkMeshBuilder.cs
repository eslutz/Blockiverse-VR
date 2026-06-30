using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using Unity.Profiling;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Views over ChunkMeshBuilder's pooled per-thread lists; the contents are valid only until
    // the next Build call on the same thread. Consumers must copy the data out (e.g. into a Mesh)
    // before triggering another rebuild.
    public sealed class ChunkMeshData
    {
        public ChunkMeshData(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Color> colors, int faceCount)
        {
            Vertices = vertices;
            Triangles = triangles;
            Uvs = uvs;
            Colors = colors;
            FaceCount = faceCount;
        }

        public List<Vector3> Vertices { get; }
        public List<int> Triangles { get; }
        public List<Vector2> Uvs { get; }
        public List<Color> Colors { get; }
        public int FaceCount { get; }

        public int TriangleCount => Triangles.Count / 3;
    }

    public static class ChunkMeshBuilder
    {
        static readonly ProfilerMarker BuildMarker = new("Blockiverse.ChunkMeshBuilder.Build");

        // Build output lists are pooled per thread so chunk rebuilds do not allocate every call
        // (GC hitches on Quest). [ThreadStatic] keeps the pool safe should Build ever run off the
        // main thread; the returned ChunkMeshData aliases these lists, so each result must be
        // consumed before the next Build call on the same thread clears and reuses them.
        [ThreadStatic] static List<Vector3> pooledVertices;
        [ThreadStatic] static List<int> pooledTriangles;
        [ThreadStatic] static List<Vector2> pooledUvs;
        [ThreadStatic] static List<Color> pooledColors;
        [ThreadStatic] static List<Vector3> pooledFluidVertices;
        [ThreadStatic] static List<int> pooledFluidTriangles;
        [ThreadStatic] static List<Vector2> pooledFluidUvs;
        [ThreadStatic] static List<Color> pooledFluidColors;

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

        public static ChunkMeshData Build(VoxelWorld world, BlockRegistry registry, ChunkCoordinate chunk, VoxelSkyLightMap skyLight = null)
        {
            return Build(world, registry, chunk, out _, skyLight);
        }

        // Builds the chunk's render geometry in one walk, split into two meshes: solid faces
        // (rendered and collidable) and fluid faces (rendered, ray-targetable, but excluded from
        // physics contacts so players wade through water instead of walking on it).
        public static ChunkMeshData Build(VoxelWorld world, BlockRegistry registry, ChunkCoordinate chunk, out ChunkMeshData fluidMesh, VoxelSkyLightMap skyLight = null)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            using ProfilerMarker.AutoScope buildScope = BuildMarker.Auto();

            List<Vector3> vertices = pooledVertices ??= new();
            List<int> triangles = pooledTriangles ??= new();
            List<Vector2> uvs = pooledUvs ??= new();
            List<Color> colors = pooledColors ??= new();
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();
            int faceCount = 0;

            List<Vector3> fluidVertices = pooledFluidVertices ??= new();
            List<int> fluidTriangles = pooledFluidTriangles ??= new();
            List<Vector2> fluidUvs = pooledFluidUvs ??= new();
            List<Color> fluidColors = pooledFluidColors ??= new();
            fluidVertices.Clear();
            fluidTriangles.Clear();
            fluidUvs.Clear();
            fluidColors.Clear();
            int fluidFaceCount = 0;

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

                        bool isFluid = definition.Category == BlockCategory.Fluid;

                        for (int face = 0; face < NeighborOffsets.Length; face++)
                        {
                            BlockPosition neighbor = position + NeighborOffsets[face];

                            if (!ShouldRenderFace(world, registry, definition, neighbor))
                                continue;

                            float light = VoxelLightSampler.SampleAirLight(world, registry, neighbor, skyLight: skyLight);

                            if (isFluid)
                            {
                                AddFace(fluidVertices, fluidTriangles, fluidUvs, fluidColors, position, face, definition.Id, light);
                                fluidFaceCount++;
                            }
                            else
                            {
                                AddFace(vertices, triangles, uvs, colors, position, face, definition.Id, light);
                                faceCount++;
                            }
                        }
                    }
                }
            }

            fluidMesh = new ChunkMeshData(fluidVertices, fluidTriangles, fluidUvs, fluidColors, fluidFaceCount);
            return new ChunkMeshData(vertices, triangles, uvs, colors, faceCount);
        }

        static bool ShouldRenderFace(VoxelWorld world, BlockRegistry registry, BlockDefinition current, BlockPosition neighbor)
        {
            if (!world.Bounds.Contains(neighbor))
                return true;

            BlockDefinition neighborDefinition = registry.Get(world.GetBlock(neighbor));

            // Adjacent cells of the same fluid family (source or flowing) merge into one volume —
            // internal faces between them would otherwise z-fight inside every lake and stream.
            if (current.Category == BlockCategory.Fluid &&
                FluidBlocks.TryGetFamily(current.Id, out FluidFamily currentFamily) &&
                FluidBlocks.TryGetFamily(neighborDefinition.Id, out FluidFamily neighborFamily) &&
                currentFamily == neighborFamily)
            {
                return false;
            }

            return !neighborDefinition.IsRenderable || !neighborDefinition.IsSolid;
        }

        static void AddFace(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            BlockPosition position,
            int faceIndex,
            BlockId blockId,
            float light)
        {
            int vertexStart = vertices.Count;
            Rect uvRect = BlockVisualAtlas.GetTileRect(blockId);
            var origin = new Vector3(position.X, position.Y, position.Z);
            Color vertexColor = VoxelLightSampler.ToVertexColor(light);

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = FaceVertices[faceIndex, i];
                vertices.Add(origin + corner);
                colors.Add(vertexColor);
            }

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));

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
        const int LightingProbeInvalidationPadding = VoxelLightSampler.DefaultProbeDistance + 1;

        readonly VoxelWorld world;
        readonly VoxelSkyLightMap skyLight;
        readonly HashSet<ChunkCoordinate> dirtyChunks = new();
        readonly List<ChunkCoordinate> drainSnapshot = new();

        public ChunkRebuildQueue(VoxelWorld world, VoxelSkyLightMap skyLight = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.skyLight = skyLight;
            world.BlockChanged += OnBlockChanged;
        }

        public int Count => dirtyChunks.Count;

        // Unsubscribes from the world; call when the renderer is reconfigured onto a new world
        // so the stale queue does not keep marking chunks for it.
        public void Detach()
        {
            world.BlockChanged -= OnBlockChanged;
        }

        public void MarkDirty(ChunkCoordinate chunk)
        {
            dirtyChunks.Add(chunk);
        }

        // Drops a single chunk from the dirty set without rebuilding it. The eager spawn-region
        // bake uses this to claim its chunks so the later incremental drain does not rebuild them
        // again. Returns true if the chunk was queued.
        public bool ClearDirty(ChunkCoordinate chunk) => dirtyChunks.Remove(chunk);

        public IReadOnlyCollection<ChunkCoordinate> DrainDirtyChunks()
        {
            return DrainDirtyChunks(int.MaxValue);
        }

        public IReadOnlyCollection<ChunkCoordinate> DrainDirtyChunks(int maxCount)
        {
            // The per-world-tick RebuildDirty pump drains this every tick even when nothing is
            // dirty; return the shared empty array in that case so a static world allocates no
            // garbage per tick.
            if (dirtyChunks.Count == 0 || maxCount <= 0)
                return Array.Empty<ChunkCoordinate>();

            DrainDirtyChunks(drainSnapshot, maxCount);
            return drainSnapshot;
        }

        public int DrainDirtyChunks(List<ChunkCoordinate> destination, int maxCount)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (dirtyChunks.Count == 0 || maxCount <= 0)
                return 0;

            if (maxCount >= dirtyChunks.Count)
            {
                destination.AddRange(dirtyChunks);
                dirtyChunks.Clear();
                return destination.Count;
            }

            foreach (ChunkCoordinate chunk in dirtyChunks)
            {
                destination.Add(chunk);
                if (destination.Count >= maxCount)
                    break;
            }

            foreach (ChunkCoordinate chunk in destination)
                dirtyChunks.Remove(chunk);

            return destination.Count;
        }

        void OnBlockChanged(BlockChange change)
        {
            ChunkCoordinate changedChunk = world.GetChunkCoordinate(change.Position);
            MarkDirty(changedChunk);

            if (skyLight == null)
            {
                // No sky map (isolated construction): conservatively invalidate the whole lit
                // column below the edit, as before.
                MarkLightingAffectedChunks(change.Position.X, change.Position.Z, 0, change.Position.Y);
            }
            else if (skyLight.ApplyChange(change, out int previousTop, out int newTop))
            {
                // The column's sky profile moved (a surface block was added/removed): every cell
                // between the ground and the higher of the two tops may change classification.
                int maxY = Math.Max(change.Position.Y, Math.Max(previousTop, newTop));
                MarkLightingAffectedChunks(change.Position.X, change.Position.Z, 0, maxY);
            }
            else
            {
                // Sky profile unchanged (typical mining/building underground or beneath cover):
                // light can only differ within probe range of the edit, not all the way down.
                MarkLightingAffectedChunks(
                    change.Position.X,
                    change.Position.Z,
                    Math.Max(0, change.Position.Y - LightingProbeInvalidationPadding),
                    Math.Min(world.Bounds.Height - 1, change.Position.Y + LightingProbeInvalidationPadding));
            }

            BlockPosition local = ChunkCoordinate.LocalPositionFromBlockPosition(change.Position, world.ChunkSize);
            MarkNeighborIfNeeded(local.X == 0, change.Position + new BlockPosition(-1, 0, 0));
            MarkNeighborIfNeeded(local.X == world.ChunkSize - 1, change.Position + new BlockPosition(1, 0, 0));
            MarkNeighborIfNeeded(local.Y == 0, change.Position + new BlockPosition(0, -1, 0));
            MarkNeighborIfNeeded(local.Y == world.ChunkSize - 1, change.Position + new BlockPosition(0, 1, 0));
            MarkNeighborIfNeeded(local.Z == 0, change.Position + new BlockPosition(0, 0, -1));
            MarkNeighborIfNeeded(local.Z == world.ChunkSize - 1, change.Position + new BlockPosition(0, 0, 1));
        }

        void MarkLightingAffectedChunks(int x, int z, int minY, int maxY)
        {
            int minX = Math.Max(0, x - LightingProbeInvalidationPadding);
            int maxX = Math.Min(world.Bounds.Width - 1, x + LightingProbeInvalidationPadding);
            int minZ = Math.Max(0, z - LightingProbeInvalidationPadding);
            int maxZ = Math.Min(world.Bounds.Depth - 1, z + LightingProbeInvalidationPadding);

            ChunkCoordinate minChunk = ChunkCoordinate.FromBlockPosition(new BlockPosition(minX, minY, minZ), world.ChunkSize);
            ChunkCoordinate maxChunk = ChunkCoordinate.FromBlockPosition(new BlockPosition(maxX, maxY, maxZ), world.ChunkSize);

            for (int chunkY = minChunk.Y; chunkY <= maxChunk.Y; chunkY++)
            {
                for (int chunkZ = minChunk.Z; chunkZ <= maxChunk.Z; chunkZ++)
                {
                    for (int chunkX = minChunk.X; chunkX <= maxChunk.X; chunkX++)
                        MarkDirty(new ChunkCoordinate(chunkX, chunkY, chunkZ));
                }
            }
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
