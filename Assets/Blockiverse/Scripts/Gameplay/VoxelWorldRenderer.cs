using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Blockiverse.Gameplay
{
    public sealed class VoxelWorldRenderer : MonoBehaviour, Blockiverse.Networking.IVoxelWorldRenderer
    {
        const int LargeDirtyRebuildWarningThreshold = 8;

        static readonly ProfilerMarker RebuildAllMarker = new("Blockiverse.VoxelWorldRenderer.RebuildAll");
        static readonly ProfilerMarker RebuildDirtyMarker = new("Blockiverse.VoxelWorldRenderer.RebuildDirty");
        static readonly ProfilerMarker RebuildChunkMarker = new("Blockiverse.VoxelWorldRenderer.RebuildChunk");

        // MeshCollider cooking is the expensive part of a rebuild on Quest; cap how many colliders
        // are rebaked per RebuildDirty call / frame and let the rest catch up over later frames.
        public const int DefaultColliderRebuildBudget = 4;
        public const int DefaultVisualRebuildBudget = 8;

        // How many chunks out from the spawn chunk the deferred initial render eagerly bakes (with
        // colliders) before the loading screen lifts, so the player lands on solid, collidable
        // ground while the rest of the world fills in incrementally under the per-frame budgets.
        public const int DefaultSpawnRegionRadiusChunks = 1;

        readonly Dictionary<ChunkCoordinate, GameObject> chunkObjects = new();
        // Per-chunk fluid child: renders fluid faces and carries a contact-excluded collider so
        // rays can target water (drink/fill/scoop) while players and props pass through it.
        readonly Dictionary<ChunkCoordinate, GameObject> fluidObjects = new();
        readonly Dictionary<ChunkCoordinate, int> chunkTriangleCounts = new();
        readonly Queue<ChunkCoordinate> pendingColliderRebuilds = new();
        readonly HashSet<ChunkCoordinate> pendingColliderSet = new();
        readonly Queue<ChunkCoordinate> pendingFluidColliderRebuilds = new();
        readonly HashSet<ChunkCoordinate> pendingFluidColliderSet = new();
        readonly List<ChunkCoordinate> dirtyChunkScratch = new();

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
        public int PendingColliderRebuildCount => pendingColliderRebuilds.Count + pendingFluidColliderRebuilds.Count;
        public int PendingVisualRebuildCount => rebuildQueue?.Count ?? 0;

        // Maximum MeshCollider rebakes performed per RebuildDirty call and per frame.
        public int ColliderRebuildBudget { get; set; } = DefaultColliderRebuildBudget;
        public int VisualRebuildBudget { get; set; } = DefaultVisualRebuildBudget;

        // World-ready gate: true once the spawn area is meshed and collidable. The synchronous
        // RebuildAll path sets it when the whole world is built; the deferred path sets it after
        // RebuildSpawnRegion bakes just the spawn neighbourhood, letting the loading screen lift
        // before the rest of the world has drained in. Configure resets it for the new world.
        public bool SpawnRegionReady { get; private set; }

        public void Configure(
            VoxelWorld voxelWorld,
            BlockRegistry blockRegistry,
            Material material,
            int layer,
            Texture2D selectedAtlas = null,
            string textureSetId = BlockTextureSetIds.Default,
            bool deferInitialRebuild = false)
        {
            // Reconfiguring onto a new world (new/load from the menus) must not leave the old
            // world's chunk meshes, queue subscription, or material behind.
            rebuildQueue?.Detach();
            DestroyGeneratedChunkContent();
            DestroyGeneratedObject(chunkMaterial);

            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            BlockVisualAtlas.ValidateRenderableBlockCoverage(registry);
            chunkMaterial = BlockVisualAtlas.CreateMaterial(material, selectedAtlas, textureSetId);
            interactionLayer = layer;
            skyLight = new VoxelSkyLightMap(world, registry);
            rebuildQueue = new ChunkRebuildQueue(world, skyLight);

            // The new world is not walkable until either RebuildAll (synchronous) or
            // RebuildSpawnRegion (deferred) bakes its collision; gate consumers on that.
            SpawnRegionReady = false;

            if (deferInitialRebuild)
                QueueFullRebuild();
            else
                RebuildAll();
        }

        public void RebuildAll()
        {
            EnsureConfigured();

            using ProfilerMarker.AutoScope scope = RebuildAllMarker.Auto();

            chunkTriangleCounts.Clear();
            totalTriangleCount = 0;

            for (int y = 0; y < ChunkCount(world.Bounds.Height); y++)
            {
                for (int z = 0; z < ChunkCount(world.Bounds.Depth); z++)
                {
                    for (int x = 0; x < ChunkCount(world.Bounds.Width); x++)
                    {
                        rebuildQueue.MarkDirty(new ChunkCoordinate(x, y, z));
                    }
                }
            }

            // A deferred world is not ready until the spawn region is meshed or the queue drains.
            SpawnRegionReady = false;
            RefreshStats();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Renderer,
                $"Queued all chunks for incremental rebuild: queuedRebuilds={stats.QueuedRebuildCount} bounds={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth} chunkSize={world.ChunkSize}",
                this);
        }

        public void QueueFullRebuild()
        {
            EnsureConfigured();

            for (int y = 0; y < ChunkCount(world.Bounds.Height); y++)
            {
                for (int z = 0; z < ChunkCount(world.Bounds.Depth); z++)
                {
                    for (int x = 0; x < ChunkCount(world.Bounds.Width); x++)
                        rebuildQueue.MarkDirty(new ChunkCoordinate(x, y, z));
                }
            }

            RefreshStats();
            BlockiverseLog.Info(
                BlockiverseLogCategory.Renderer,
                $"Queued full chunk rebuild: queuedRebuilds={stats.QueuedRebuildCount} bounds={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth} chunkSize={world.ChunkSize}",
                this);
        }

        // Eagerly meshes and collider-bakes just the chunks around the spawn so the deferred
        // initial render can drop the player onto solid ground immediately, then lift the loading
        // screen. The rest of the queued world keeps draining incrementally under the per-frame
        // budgets. Unlike RebuildAll this deliberately never flushes the whole world's colliders —
        // only the spawn neighbourhood's, which is what PendingColliderRebuildCount counts here.
        public void RebuildSpawnRegion(BlockPosition spawn, int radiusChunks = DefaultSpawnRegionRadiusChunks)
        {
            EnsureConfigured();

            ChunkCoordinate center = ChunkCoordinate.FromBlockPosition(spawn, world.ChunkSize);
            int maxChunkX = ChunkCount(world.Bounds.Width) - 1;
            int maxChunkY = ChunkCount(world.Bounds.Height) - 1;
            int maxChunkZ = ChunkCount(world.Bounds.Depth) - 1;

            int minX = Mathf.Clamp(center.X - radiusChunks, 0, maxChunkX);
            int maxX = Mathf.Clamp(center.X + radiusChunks, 0, maxChunkX);
            int minY = Mathf.Clamp(center.Y - radiusChunks, 0, maxChunkY);
            int maxY = Mathf.Clamp(center.Y + radiusChunks, 0, maxChunkY);
            int minZ = Mathf.Clamp(center.Z - radiusChunks, 0, maxChunkZ);
            int maxZ = Mathf.Clamp(center.Z + radiusChunks, 0, maxChunkZ);

            int bakedChunks = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var chunk = new ChunkCoordinate(x, y, z);
                        // Claim the chunk out of the dirty queue so the later incremental drain
                        // does not rebuild it a second time.
                        rebuildQueue.ClearDirty(chunk);
                        RebuildChunk(chunk);
                        bakedChunks++;
                    }
                }
            }

            // Bake colliders for exactly the spawn chunks just meshed (a bounded budget equal to
            // the currently-pending count): a fresh deferred world has no other pending colliders,
            // and we must never trigger an unbounded world-wide flush on this path.
            ProcessPendingColliderRebuilds(PendingColliderRebuildCount);

            RefreshStats();
            SpawnRegionReady = true;
            BlockiverseLog.Info(
                BlockiverseLogCategory.Renderer,
                $"Baked spawn region: spawnChunks={bakedChunks} radiusChunks={radiusChunks} queuedRemaining={stats.QueuedRebuildCount} center={center.X},{center.Y},{center.Z}",
                this);
        }

        public void RebuildDirty()
        {
            EnsureConfigured();

            using ProfilerMarker.AutoScope scope = RebuildDirtyMarker.Auto();

            int visualBudget = Math.Max(1, VisualRebuildBudget);
            rebuildQueue.DrainDirtyChunks(dirtyChunkScratch, visualBudget);

            foreach (ChunkCoordinate chunk in dirtyChunkScratch)
                RebuildChunk(chunk);

            // Visual meshes are now current; rebake colliders within this call's budget and leave the
            // remainder for the per-frame pump.
            ProcessPendingColliderRebuilds(ColliderRebuildBudget);

            RefreshStats();

            if (!Application.isBatchMode && dirtyChunkScratch.Count >= LargeDirtyRebuildWarningThreshold)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Renderer,
                    $"Large dirty chunk rebuild: drainedChunks={dirtyChunkScratch.Count} chunks={stats.ChunkCount} triangles={stats.TriangleCount} queuedRebuilds={stats.QueuedRebuildCount}",
                    this);
            }
        }

        int RebuildChunk(ChunkCoordinate chunk)
        {
            using ProfilerMarker.AutoScope scope = RebuildChunkMarker.Auto();

            if (world.IsChunkEmpty(chunk))
            {
                ReleaseChunkObject(chunk);
                return 0;
            }

            // meshData aliases ChunkMeshBuilder's pooled lists, which the next Build call clears;
            // the Set* calls below copy everything into the Mesh before that can happen.
            ChunkMeshData meshData = ChunkMeshBuilder.Build(world, registry, chunk, out ChunkMeshData fluidData, skyLight);

            // R1: a chunk with no rendered faces is either all-air or fully buried — it has no
            // visible mesh and no reachable collision surface, so it needs no GameObject. Don't
            // spawn one (saves memory, culling, scene-graph cost, and a MeshCollider cook), and
            // release any object a prior state had created (e.g. a chunk just mined out to air),
            // which also deregisters its runtime TeleportationArea as the object is destroyed.
            if (meshData.FaceCount == 0 && fluidData.FaceCount == 0)
            {
                ReleaseChunkObject(chunk);
                return 0;
            }

            GameObject chunkObject = GetOrCreateChunkObject(chunk);

            // One pooled Mesh per chunk, cleared and refilled on every rebuild: no per-rebuild
            // Mesh allocation/destroy churn. The throttled MeshCollider keeps serving its cooked
            // snapshot of the old geometry until its rebake reassigns this same instance.
            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = $"Chunk {chunk}" };
                filter.sharedMesh = mesh;
            }

            FillMesh(mesh, meshData);

            // The chunk's MeshCollider is fed only by the solid mesh. A fluid-only chunk has an
            // empty solid mesh, so queuing its solid collider would schedule a no-op recook that
            // needlessly inflates the throttled backlog (and the pending count tests observe).
            // Still queue when the collider holds stale geometry that must be cleared (e.g. the
            // last solid block in the chunk was mined out, leaving only fluid behind).
            MeshCollider solidCollider = chunkObject.GetComponent<MeshCollider>();
            if (meshData.FaceCount > 0 || (solidCollider != null && solidCollider.sharedMesh != null))
                EnqueueColliderRebuild(chunk);

            UpdateFluidChunkMesh(chunk, chunkObject, fluidData);

            int previousTriangleCount = chunkTriangleCounts.TryGetValue(chunk, out int existingTriangleCount)
                ? existingTriangleCount
                : 0;

            int triangleCount = meshData.TriangleCount + fluidData.TriangleCount;
            chunkTriangleCounts[chunk] = triangleCount;
            totalTriangleCount += triangleCount - previousTriangleCount;

            return triangleCount;
        }

        static void FillMesh(Mesh mesh, ChunkMeshData data)
        {
            mesh.Clear();
            mesh.SetVertices(data.Vertices);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.SetUVs(0, data.Uvs);
            mesh.SetColors(data.Colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // Refills the chunk's pooled fluid mesh in place. Fluid colliders are queued through the
        // same throttle as solid colliders so flowing water cannot force synchronous PhysX recooks
        // on every fluid simulation step.
        void UpdateFluidChunkMesh(ChunkCoordinate chunk, GameObject chunkObject, ChunkMeshData fluidData)
        {
            fluidObjects.TryGetValue(chunk, out GameObject fluidObject);

            if (fluidData.FaceCount == 0)
            {
                // Most chunks hold no fluid; never create the child for them, and empty the
                // pooled mesh when the last fluid block in the chunk goes away.
                if (fluidObject == null)
                    return;

                fluidObject.GetComponent<MeshFilter>().sharedMesh?.Clear();
                fluidObject.GetComponent<MeshCollider>().sharedMesh = null;
                return;
            }

            if (fluidObject == null)
                fluidObject = CreateFluidObject(chunk, chunkObject);

            MeshFilter filter = fluidObject.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = $"Chunk {chunk} Fluid" };
                filter.sharedMesh = mesh;
            }

            FillMesh(mesh, fluidData);
            EnqueueFluidColliderRebuild(chunk);
        }

        GameObject CreateFluidObject(ChunkCoordinate chunk, GameObject chunkObject)
        {
            var fluidObject = new GameObject("Fluid");
            fluidObject.transform.SetParent(chunkObject.transform, false);

            if (interactionLayer >= 0)
                fluidObject.layer = interactionLayer;

            fluidObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = fluidObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (chunkMaterial != null)
                renderer.sharedMaterial = chunkMaterial;

            // Contact-excluded: raycast queries (block targeting, drink/fill) still hit the fluid
            // surface, but the character controller and props never collide with it. The fluid
            // child is deliberately not registered as a TeleportationArea — no teleporting onto
            // water. Block targeting resolves through the parent's VoxelChunkTarget.
            MeshCollider collider = fluidObject.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            collider.excludeLayers = ~0;

            fluidObjects.Add(chunk, fluidObject);
            return fluidObject;
        }

        // Destroys the chunk's render object (solid + fluid child) and clears all per-chunk
        // bookkeeping. Used when a chunk has (or has become) no render geometry. Any queued
        // collider rebake for the chunk becomes a no-op via the missing-object guard in
        // ProcessNext*ColliderRebuild, and is removed from the pending sets so a later edit can
        // re-enqueue it cleanly.
        void ReleaseChunkObject(ChunkCoordinate chunk)
        {
            if (chunkTriangleCounts.TryGetValue(chunk, out int previousTriangleCount))
            {
                totalTriangleCount -= previousTriangleCount;
                chunkTriangleCounts.Remove(chunk);
            }

            if (fluidObjects.TryGetValue(chunk, out GameObject fluidObject))
            {
                // The fluid child object itself goes down with its parent below; destroy its
                // pooled mesh here so it is not leaked.
                if (fluidObject != null)
                    DestroyGeneratedObject(fluidObject.GetComponent<MeshFilter>()?.sharedMesh);

                fluidObjects.Remove(chunk);
            }

            if (chunkObjects.TryGetValue(chunk, out GameObject chunkObject))
            {
                if (chunkObject != null)
                {
                    // One pooled mesh per chunk, shared by the filter and collider — destroy it
                    // once, then the object (its fluid child + TeleportationArea go with it).
                    DestroyGeneratedObject(chunkObject.GetComponent<MeshFilter>()?.sharedMesh);
                    DestroyGeneratedObject(chunkObject);
                }

                chunkObjects.Remove(chunk);
            }

            pendingColliderSet.Remove(chunk);
            pendingFluidColliderSet.Remove(chunk);
        }

        void EnqueueColliderRebuild(ChunkCoordinate chunk)
        {
            if (pendingColliderSet.Add(chunk))
                pendingColliderRebuilds.Enqueue(chunk);
        }

        void EnqueueFluidColliderRebuild(ChunkCoordinate chunk)
        {
            if (pendingFluidColliderSet.Add(chunk))
                pendingFluidColliderRebuilds.Enqueue(chunk);
        }

        // Rebakes up to budget pending colliders against the chunk's pooled mesh (the reassign
        // forces PhysX to recook from the refilled geometry).
        public void ProcessPendingColliderRebuilds(int budget)
        {
            int processed = 0;
            while (processed < budget && (pendingColliderRebuilds.Count > 0 || pendingFluidColliderRebuilds.Count > 0))
            {
                if (pendingColliderRebuilds.Count > 0)
                {
                    if (ProcessNextSolidColliderRebuild())
                        processed++;
                    continue;
                }

                if (ProcessNextFluidColliderRebuild())
                    processed++;
            }
        }

        bool ProcessNextSolidColliderRebuild()
        {
            ChunkCoordinate chunk = pendingColliderRebuilds.Dequeue();
            pendingColliderSet.Remove(chunk);

            if (!chunkObjects.TryGetValue(chunk, out GameObject chunkObject) || chunkObject == null)
                return false;

            Mesh currentMesh = chunkObject.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();

            AssignColliderMesh(collider, currentMesh);
            return true;
        }

        bool ProcessNextFluidColliderRebuild()
        {
            ChunkCoordinate chunk = pendingFluidColliderRebuilds.Dequeue();
            pendingFluidColliderSet.Remove(chunk);

            if (!fluidObjects.TryGetValue(chunk, out GameObject fluidObject) || fluidObject == null)
                return false;

            Mesh currentMesh = fluidObject.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = fluidObject.GetComponent<MeshCollider>();

            AssignColliderMesh(collider, currentMesh);
            return true;
        }

        static void AssignColliderMesh(MeshCollider collider, Mesh currentMesh)
        {
            // An empty chunk's pooled mesh has no vertices; assigning it to a MeshCollider logs a
            // PhysX error and cooks nothing, so detach instead. The reassign forces a recook from
            // the refilled geometry for non-empty chunks.
            collider.sharedMesh = null;
            if (currentMesh != null && currentMesh.vertexCount > 0)
                collider.sharedMesh = currentMesh;
        }

        // Per-frame pump so a throttled collider backlog drains even without further edits.
        void Update()
        {
            if (rebuildQueue != null && rebuildQueue.Count > 0)
            {
                RebuildDirty();
                return;
            }

            if (PendingColliderRebuildCount > 0)
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
            chunkCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.CookForFasterSimulation;
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
            // Pooled fluid meshes first: the child objects themselves go down with their parents.
            foreach (GameObject fluidObject in fluidObjects.Values)
            {
                if (fluidObject == null)
                    continue;

                DestroyGeneratedObject(fluidObject.GetComponent<MeshFilter>()?.sharedMesh);
            }

            foreach (GameObject chunkObject in chunkObjects.Values)
            {
                if (chunkObject == null)
                    continue;

                // One pooled mesh per chunk, shared by the filter and collider — destroy it once.
                DestroyGeneratedObject(chunkObject.GetComponent<MeshFilter>()?.sharedMesh);
                DestroyGeneratedObject(chunkObject);
            }

            chunkObjects.Clear();
            fluidObjects.Clear();
            chunkTriangleCounts.Clear();
            pendingColliderRebuilds.Clear();
            pendingColliderSet.Clear();
            pendingFluidColliderRebuilds.Clear();
            pendingFluidColliderSet.Clear();
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
