using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Blockiverse.Gameplay
{
    public sealed class VoxelWorldRenderer : MonoBehaviour
    {
        const int LargeDirtyRebuildWarningThreshold = 8;

        static readonly ProfilerMarker RebuildAllMarker = new("Blockiverse.VoxelWorldRenderer.RebuildAll");
        static readonly ProfilerMarker RebuildDirtyMarker = new("Blockiverse.VoxelWorldRenderer.RebuildDirty");
        static readonly ProfilerMarker RebuildChunkMarker = new("Blockiverse.VoxelWorldRenderer.RebuildChunk");

        // MeshCollider cooking is the expensive part of a rebuild on Quest; cap how many colliders
        // are rebaked per RebuildDirty call / frame and let the rest catch up over later frames.
        public const int DefaultColliderRebuildBudget = 4;

        readonly Dictionary<ChunkCoordinate, GameObject> chunkObjects = new();
        readonly Dictionary<ChunkCoordinate, int> chunkTriangleCounts = new();
        readonly Queue<ChunkCoordinate> pendingColliderRebuilds = new();
        readonly HashSet<ChunkCoordinate> pendingColliderSet = new();
        readonly Dictionary<ChunkCoordinate, Mesh> colliderMeshByChunk = new();

        VoxelWorld world;
        BlockRegistry registry;
        ChunkRebuildQueue rebuildQueue;
        VoxelSkyLightMap skyLight;
        Material chunkMaterial;
        int interactionLayer = -1;
        int totalTriangleCount;
        VoxelRenderStats stats;

        public VoxelWorld World => world;
        public VoxelRenderStats Stats => stats;

        // The per-column sky map kept current by the rebuild queue; also consumable by
        // gameplay systems that need cheap "is this cell under open sky" answers.
        public VoxelSkyLightMap SkyLight => skyLight;

        // Colliders awaiting a (throttled) rebake. Visual meshes are always current.
        public int PendingColliderRebuildCount => pendingColliderRebuilds.Count;

        // Maximum MeshCollider rebakes performed per RebuildDirty call and per frame.
        public int ColliderRebuildBudget { get; set; } = DefaultColliderRebuildBudget;

        public void Configure(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            Material material,
            int layer)
        {
            // Reconfiguring onto a new world (new/load from the menus) must not leave the old
            // world's chunk meshes, queue subscription, or material behind.
            rebuildQueue?.Detach();
            DestroyGeneratedChunkContent();
            DestroyGeneratedObject(chunkMaterial);

            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            BlockVisualAtlas.ValidateRenderableBlockCoverage(registry);
            chunkMaterial = BlockVisualAtlas.CreateMaterial(material);
            interactionLayer = layer;
            skyLight = new VoxelSkyLightMap(world, registry);
            rebuildQueue = new ChunkRebuildQueue(world, skyLight);
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

            // A fresh world needs full collision immediately (spawn, teleport, walking), so flush
            // every queued collider rebuild rather than throttling the initial bake.
            ProcessPendingColliderRebuilds(int.MaxValue);

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

            // Visual meshes are now current; rebake colliders within this call's budget and leave the
            // remainder for the per-frame pump.
            ProcessPendingColliderRebuilds(ColliderRebuildBudget);

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

            // meshData aliases ChunkMeshBuilder's pooled lists, which the next Build call clears;
            // the Set* calls below copy everything into the Mesh before that can happen.
            ChunkMeshData meshData = ChunkMeshBuilder.Build(world, registry, chunk, skyLight);
            GameObject chunkObject = GetOrCreateChunkObject(chunk);

            Mesh mesh = new();
            mesh.name = $"Chunk {chunk}";
            mesh.SetVertices(meshData.Vertices);
            mesh.SetTriangles(meshData.Triangles, 0);
            mesh.SetUVs(0, meshData.Uvs);
            mesh.SetColors(meshData.Colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            Mesh previousMesh = filter.sharedMesh;
            filter.sharedMesh = mesh;

            // The collider rebake is throttled, so the previous visual mesh may still be in use by
            // the not-yet-updated collider; only destroy it once it is no longer referenced there.
            colliderMeshByChunk.TryGetValue(chunk, out Mesh colliderMesh);
            if (previousMesh != null && !ReferenceEquals(previousMesh, colliderMesh))
                DestroyGeneratedObject(previousMesh);

            EnqueueColliderRebuild(chunk);

            int previousTriangleCount = chunkTriangleCounts.TryGetValue(chunk, out int existingTriangleCount)
                ? existingTriangleCount
                : 0;

            chunkTriangleCounts[chunk] = meshData.TriangleCount;
            totalTriangleCount += meshData.TriangleCount - previousTriangleCount;

            return meshData.TriangleCount;
        }

        void EnqueueColliderRebuild(ChunkCoordinate chunk)
        {
            if (pendingColliderSet.Add(chunk))
                pendingColliderRebuilds.Enqueue(chunk);
        }

        // Rebakes up to budget pending colliders to the current chunk mesh, destroying the mesh the
        // collider previously used once it is released.
        public void ProcessPendingColliderRebuilds(int budget)
        {
            int processed = 0;
            while (processed < budget && pendingColliderRebuilds.Count > 0)
            {
                ChunkCoordinate chunk = pendingColliderRebuilds.Dequeue();
                pendingColliderSet.Remove(chunk);

                if (!chunkObjects.TryGetValue(chunk, out GameObject chunkObject) || chunkObject == null)
                    continue;

                Mesh currentMesh = chunkObject.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
                colliderMeshByChunk.TryGetValue(chunk, out Mesh previousColliderMesh);

                collider.sharedMesh = null;
                collider.sharedMesh = currentMesh;
                colliderMeshByChunk[chunk] = currentMesh;

                if (previousColliderMesh != null && !ReferenceEquals(previousColliderMesh, currentMesh))
                    DestroyGeneratedObject(previousColliderMesh);

                processed++;
            }
        }

        // Per-frame pump so a throttled collider backlog drains even without further edits.
        void Update()
        {
            if (pendingColliderRebuilds.Count > 0)
                ProcessPendingColliderRebuilds(ColliderRebuildBudget);
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
            // Voxel lighting is baked into vertex colors; Unity shadow passes would only add an
            // extra render pass per light on Quest (per Meta VRC guidance) for no visual gain.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (chunkMaterial != null)
                renderer.sharedMaterial = chunkMaterial;

            MeshCollider chunkCollider = chunkObject.AddComponent<MeshCollider>();
            VoxelChunkTarget target = chunkObject.AddComponent<VoxelChunkTarget>();
            target.Configure(world);

            ConfigureTeleportationArea(chunkObject, chunkCollider);

            chunkObjects.Add(chunk, chunkObject);
            return chunkObject;
        }

        // Makes the chunk surface a native teleport target so the XRI teleport ray can land the
        // player on the actual voxel terrain. Runtime-only so edit-mode rendering tests do not
        // spawn an XRInteractionManager.
        static void ConfigureTeleportationArea(GameObject chunkObject, Collider chunkCollider)
        {
            if (!Application.isPlaying)
                return;

            TeleportationArea area = chunkObject.GetComponent<TeleportationArea>();

            if (area == null)
                area = chunkObject.AddComponent<TeleportationArea>();

            if (!area.colliders.Contains(chunkCollider))
                area.colliders.Add(chunkCollider);

            area.matchOrientation = MatchOrientation.WorldSpaceUp;
            // OnSelectExited: the player releases the thumbstick to commit the teleport.
            // Teleport Mode and Teleport Select are both bound to thumbstick/y, so OnSelectEntered
            // would teleport instantly on aim; Exited gives hold-to-aim / release-to-land behavior.
            area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
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
            rebuildQueue?.Detach();
            DestroyGeneratedChunkContent();
            DestroyGeneratedObject(chunkMaterial);
        }

        // Destroys every generated chunk object and mesh and resets the bookkeeping — used on
        // teardown and when Configure swaps the renderer onto a different world.
        void DestroyGeneratedChunkContent()
        {
            foreach (GameObject chunkObject in chunkObjects.Values)
            {
                if (chunkObject == null)
                    continue;

                Mesh mesh = chunkObject.GetComponent<MeshFilter>()?.sharedMesh;
                DestroyGeneratedObject(mesh);
                DestroyGeneratedObject(chunkObject);
            }

            // Release any collider meshes still deferred behind the throttle (DestroyGeneratedObject
            // is null-safe, so meshes already freed via the filter loop are skipped).
            foreach (Mesh colliderMesh in colliderMeshByChunk.Values)
                DestroyGeneratedObject(colliderMesh);

            chunkObjects.Clear();
            chunkTriangleCounts.Clear();
            colliderMeshByChunk.Clear();
            pendingColliderRebuilds.Clear();
            pendingColliderSet.Clear();
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
