using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class VoxelWorldRenderer : MonoBehaviour
    {
        readonly Dictionary<ChunkCoordinate, GameObject> chunkObjects = new();

        VoxelWorld world;
        BlockRegistry registry;
        ChunkRebuildQueue rebuildQueue;
        Material chunkMaterial;
        int interactionLayer = -1;
        VoxelRenderStats stats;

        public VoxelWorld World => world;
        public VoxelRenderStats Stats => stats;

        public void Configure(VoxelWorld voxelWorld, BlockRegistry blockRegistry, Material material, int layer)
        {
            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            chunkMaterial = material;
            interactionLayer = layer;
            rebuildQueue = new ChunkRebuildQueue(world);
            RebuildAll();
        }

        public void RebuildAll()
        {
            EnsureConfigured();

            int chunkCount = 0;
            int triangleCount = 0;

            for (int y = 0; y < ChunkCount(world.Bounds.Height); y++)
            {
                for (int z = 0; z < ChunkCount(world.Bounds.Depth); z++)
                {
                    for (int x = 0; x < ChunkCount(world.Bounds.Width); x++)
                    {
                        ChunkCoordinate chunk = new(x, y, z);
                        triangleCount += RebuildChunk(chunk);
                        chunkCount++;
                    }
                }
            }

            stats = new VoxelRenderStats(chunkCount, triangleCount, rebuildQueue.Count);
        }

        public void RebuildDirty()
        {
            EnsureConfigured();

            foreach (ChunkCoordinate chunk in rebuildQueue.DrainDirtyChunks())
                RebuildChunk(chunk);

            RefreshStats();
        }

        int RebuildChunk(ChunkCoordinate chunk)
        {
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
            filter.sharedMesh = mesh;

            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
            collider.sharedMesh = mesh;

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
            int triangleCount = 0;

            foreach (GameObject chunkObject in chunkObjects.Values)
            {
                Mesh mesh = chunkObject.GetComponent<MeshFilter>()?.sharedMesh;

                if (mesh != null)
                    triangleCount += mesh.triangles.Length / 3;
            }

            stats = new VoxelRenderStats(chunkObjects.Count, triangleCount, rebuildQueue?.Count ?? 0);
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
