using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Unity.Profiling;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class VoxelWorldRenderer : MonoBehaviour
    {
        const int LargeDirtyRebuildWarningThreshold = 8;

        static readonly ProfilerMarker RebuildAllMarker = new("Blockiverse.VoxelWorldRenderer.RebuildAll");
        static readonly ProfilerMarker RebuildDirtyMarker = new("Blockiverse.VoxelWorldRenderer.RebuildDirty");
        static readonly ProfilerMarker RebuildChunkMarker = new("Blockiverse.VoxelWorldRenderer.RebuildChunk");

        readonly Dictionary<ChunkCoordinate, GameObject> chunkObjects = new();
        readonly Dictionary<ChunkCoordinate, int> chunkTriangleCounts = new();

        VoxelWorld world;
        BlockRegistry registry;
        ChunkRebuildQueue rebuildQueue;
        Material chunkMaterial;
        int interactionLayer = -1;
        int totalTriangleCount;
        VoxelRenderStats stats;

        public VoxelWorld World => world;
        public VoxelRenderStats Stats => stats;

        public void Configure(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            Material material,
            int layer)
        {
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            BlockVisualAtlas.ValidateRenderableBlockCoverage(registry);
            chunkMaterial = BlockVisualAtlas.CreateMaterial(material);
            interactionLayer = layer;
            rebuildQueue = new ChunkRebuildQueue(world);
            RebuildAll();
        }

        public void RebuildAll()
        {
            EnsureConfigured();

            using ProfilerMarker.AutoScope scope = RebuildAllMarker.Auto();

            int chunkCount = 0;
            chunkTriangleCounts.Clear();
            totalTriangleCount = 0;

            for (int y = 0; y < ChunkCount(world.Bounds.Height); y++)
            {
                for (int z = 0; z < ChunkCount(world.Bounds.Depth); z++)
                {
                    for (int x = 0; x < ChunkCount(world.Bounds.Width); x++)
                    {
                        ChunkCoordinate chunk = new(x, y, z);
                        RebuildChunk(chunk);
                        chunkCount++;
                    }
                }
            }

            stats = new VoxelRenderStats(chunkCount, totalTriangleCount, rebuildQueue.Count);
            BlockiverseLog.Info(
                BlockiverseLogCategory.Renderer,
                $"Rebuilt all chunks: chunks={stats.ChunkCount} triangles={stats.TriangleCount} queuedRebuilds={stats.QueuedRebuildCount} bounds={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth} chunkSize={world.ChunkSize}",
                this);
        }

        public void RebuildDirty()
        {
            EnsureConfigured();

            using ProfilerMarker.AutoScope scope = RebuildDirtyMarker.Auto();

            IReadOnlyCollection<ChunkCoordinate> dirtyChunks = rebuildQueue.DrainDirtyChunks();

            foreach (ChunkCoordinate chunk in dirtyChunks)
                RebuildChunk(chunk);

            RefreshStats();

            if (dirtyChunks.Count >= LargeDirtyRebuildWarningThreshold)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Renderer,
                    $"Large dirty chunk rebuild: drainedChunks={dirtyChunks.Count} chunks={stats.ChunkCount} triangles={stats.TriangleCount} queuedRebuilds={stats.QueuedRebuildCount}",
                    this);
            }
        }

        int RebuildChunk(ChunkCoordinate chunk)
        {
            using ProfilerMarker.AutoScope scope = RebuildChunkMarker.Auto();

            ChunkMeshData meshData = ChunkMeshBuilder.Build(world, registry, chunk);
            GameObject chunkObject = GetOrCreateChunkObject(chunk);

            Mesh mesh = new();
            mesh.name = $"Chunk {chunk}";
            mesh.SetVertices(meshData.Vertices);
            mesh.SetTriangles(meshData.Triangles, 0);
            mesh.SetUVs(0, meshData.Uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            Mesh previousMesh = filter.sharedMesh;
            filter.sharedMesh = mesh;

            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;

            int previousTriangleCount = chunkTriangleCounts.TryGetValue(chunk, out int existingTriangleCount)
                ? existingTriangleCount
                : 0;

            chunkTriangleCounts[chunk] = meshData.TriangleCount;
            totalTriangleCount += meshData.TriangleCount - previousTriangleCount;

            DestroyGeneratedObject(previousMesh);

            return meshData.TriangleCount;
        }

        GameObject GetOrCreateChunkObject(ChunkCoordinate chunk)
        {
            if (chunkObjects.TryGetValue(chunk, out GameObject existing))
                return existing;

            var chunkObject = new GameObject($"Chunk {chunk.X},{chunk.Y},{chunk.Z}");
            chunkObject.transform.SetParent(transform, false);

            if (interactionLayer >= 0)
                chunkObject.layer = interactionLayer;

            chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();

            if (chunkMaterial != null)
                renderer.sharedMaterial = chunkMaterial;

            chunkObject.AddComponent<MeshCollider>();
            VoxelChunkTarget target = chunkObject.AddComponent<VoxelChunkTarget>();
            target.Configure(world);

            chunkObjects.Add(chunk, chunkObject);
            return chunkObject;
        }

        void RefreshStats()
        {
            stats = new VoxelRenderStats(chunkObjects.Count, totalTriangleCount, rebuildQueue?.Count ?? 0);
        }

        int ChunkCount(int axisLength)
        {
            return Mathf.CeilToInt(axisLength / (float)world.ChunkSize);
        }

        void EnsureConfigured()
        {
            if (world == null || registry == null)
                throw new InvalidOperationException("Voxel world renderer has not been configured.");
        }

        void OnDestroy()
        {
            foreach (GameObject chunkObject in chunkObjects.Values)
            {
                if (chunkObject == null)
                    continue;

                Mesh mesh = chunkObject.GetComponent<MeshFilter>()?.sharedMesh;
                DestroyGeneratedObject(mesh);
            }

            DestroyGeneratedObject(chunkMaterial);
            chunkObjects.Clear();
            chunkTriangleCounts.Clear();
            totalTriangleCount = 0;
        }

        static void DestroyGeneratedObject(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }

    public sealed class VoxelChunkTarget : MonoBehaviour
    {
        VoxelWorld world;

        public void Configure(VoxelWorld voxelWorld)
        {
            world = voxelWorld;
        }

        public bool TryGetHitBlock(RaycastHit hit, out BlockPosition position)
        {
            position = CreativeInteractionController.ComputeHitBlockPosition(hit.point, hit.normal);
            return world != null && world.Bounds.Contains(position);
        }
    }
}
