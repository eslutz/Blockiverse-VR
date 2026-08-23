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
        // Per-chunk foliage child: cross-quad and decal vegetation. Same shape as the fluid child —
        // rendered, ray-targetable so plants stay harvestable, but excluded from physics contacts
        // so the player walks through grass instead of into it.
        readonly Dictionary<ChunkCoordinate, GameObject> foliageObjects = new();
        readonly Queue<ChunkCoordinate> pendingFoliageColliderRebuilds = new();
        readonly HashSet<ChunkCoordinate> pendingFoliageColliderSet = new();
        readonly List<ChunkCoordinate> dirtyChunkScratch = new();
        // Reused by ApplyChunkMaterials so reading a renderer's material count costs no allocation.
        readonly List<Material> sharedMaterialScratch = new();

        VoxelWorld world;
        BlockRegistry registry;
        ChunkRebuildQueue rebuildQueue;
        VoxelSkyLightMap skyLight;
        VoxelEmitterIndex emitterIndex;
        Material chunkMaterial;
        // Water renders from a second, transparent clone of the same authored atlas material, drawn
        // after a depth-only prime of the same geometry so exactly one water layer blends per pixel.
        Material fluidMaterial;
        Material fluidDepthPrimeMaterial;
        // Alpha-cutout clone of the same authored atlas material, shared by leaf canopies
        // (submesh 1 of the chunk mesh) and the foliage child's cross quads.
        Material cutoutMaterial;
        int interactionLayer = -1;
        // Fluid geometry sits on its own layer so gravity's ground sphere-cast never sees it.
        // Resolved by name at Configure time, falling back to the canonical index.
        int fluidLayer = -1;
        // Passable geometry (vegetation today, any walk-through block later) sits on its own layer
        // for the same reason as fluid: scene queries ignore Collider.excludeLayers, so contact
        // filtering alone would still let gravity's sphere-cast read grass as solid ground.
        int passableLayer = -1;
        int totalTriangleCount;
        VoxelRenderStats stats;

        public VoxelWorld World => world;
        public VoxelRenderStats Stats => stats;

        // The layer fluid chunk children are placed on. Exposed so rig/prefab tests can assert the
        // gravity mask excludes it and the targeting masks include it.
        public int FluidLayer => fluidLayer;

        // Deepest the water shader's wave can pull a surface vertex below its voxel face plane.
        // FillMesh pads the fluid mesh bounds by this much so troughs are not frustum-culled and
        // popped at the edge of vision -- very visible in VR, where head rotation sweeps geometry
        // across the screen edge constantly. Must stay >= 2x the largest _Wave*.x amplitude.
        public const float MaxWaveDipMeters = 0.05f;

        // The per-column sky map kept current by the rebuild queue; also consumable by
        // gameplay systems that need cheap "is this cell under open sky" answers.
        public VoxelSkyLightMap SkyLight => skyLight;
        public VoxelEmitterIndex EmitterIndex => emitterIndex;

        // Colliders awaiting a (throttled) rebake. Visual meshes are always current.
        public int PendingColliderRebuildCount =>
            pendingColliderRebuilds.Count + pendingFluidColliderRebuilds.Count + pendingFoliageColliderRebuilds.Count;
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
            bool deferInitialRebuild = false,
            // Supplied by the world simulation, which owns sky occlusion because crop growth and
            // cave detection read it. Optional so renderer-only tests keep their existing calls;
            // when null the renderer builds its own map as it always did.
            VoxelSkyLightMap sharedSkyLight = null)
        {
            // Reconfiguring onto a new world (new/load from the menus) must not leave the old
            // world's chunk meshes, queue subscription, or material behind.
            rebuildQueue?.Detach();
            DestroyGeneratedChunkContent();
            DestroyGeneratedObject(chunkMaterial);
            DestroyGeneratedObject(fluidMaterial);
            DestroyGeneratedObject(fluidDepthPrimeMaterial);
            DestroyGeneratedObject(cutoutMaterial);

            world = voxelWorld ?? throw new ArgumentNullException(nameof(voxelWorld));
            registry = blockRegistry ?? throw new ArgumentNullException(nameof(blockRegistry));
            BlockVisualAtlas.ValidateRenderableBlockCoverage(registry);
            chunkMaterial = BlockVisualAtlas.CreateMaterial(material, selectedAtlas, textureSetId);
            // All materials are created before any rebuild: CreateFluidObject reads fluidMaterial
            // lazily during the rebuild below, and a null there renders water with no material.
            fluidMaterial = BlockVisualAtlas.CreateFluidMaterial(material, selectedAtlas, textureSetId);
            fluidDepthPrimeMaterial = BlockVisualAtlas.CreateFluidDepthPrimeMaterial(material, selectedAtlas, textureSetId);
            cutoutMaterial = BlockVisualAtlas.CreateCutoutMaterial(material, selectedAtlas, textureSetId);
            interactionLayer = layer;
            fluidLayer = ResolveFluidLayer();
            passableLayer = ResolvePassableLayer();
            skyLight = sharedSkyLight ?? new VoxelSkyLightMap(world, registry);
            emitterIndex = new VoxelEmitterIndex(world, registry);
            rebuildQueue = new ChunkRebuildQueue(world, skyLight, emitterIndex);

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
                        ChunkCoordinate chunk = new(x, y, z);
                        RebuildChunk(chunk);
                    }
                }
            }

            // A fresh world needs full collision immediately (spawn, teleport, walking), so flush
            // every queued collider rebuild rather than throttling the initial bake.
            ProcessPendingColliderRebuilds(int.MaxValue);

            SpawnRegionReady = true;
            RefreshStats();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Renderer,
                $"Rebuilt all chunks: chunks={stats.ChunkCount} triangles={stats.TriangleCount} queuedRebuilds={stats.QueuedRebuildCount} bounds={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth} chunkSize={world.ChunkSize}",
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
            ChunkMeshData meshData = ChunkMeshBuilder.Build(
                world, registry, chunk, out ChunkMeshData fluidData, out ChunkMeshData foliageData, skyLight, emitterIndex);

            // R1: a chunk with no rendered faces is either all-air or fully buried — it has no
            // visible mesh and no reachable collision surface, so it needs no GameObject. Don't
            // spawn one (saves memory, culling, scene-graph cost, and a MeshCollider cook), and
            // release any object a prior state had created (e.g. a chunk just mined out to air),
            // which also deregisters its runtime TeleportationArea as the object is destroyed.
            // Foliage counts here too: a chunk holding only grass has no terrain and no fluid, and
            // releasing it would delete the vegetation the builder just produced.
            if (meshData.FaceCount == 0 && fluidData.FaceCount == 0 && foliageData.FaceCount == 0)
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

            // Submesh 1 exists only when the chunk holds leaves, so a leaf-free chunk keeps exactly
            // one material and its renderer is byte-identical to before this feature.
            ApplyChunkMaterials(chunkObject, meshData.CutoutTriangleCount > 0);

            // The chunk's MeshCollider is fed only by the solid mesh. A fluid-only chunk has an
            // empty solid mesh, so queuing its solid collider would schedule a no-op recook that
            // needlessly inflates the throttled backlog (and the pending count tests observe).
            // Still queue when the collider holds stale geometry that must be cleared (e.g. the
            // last solid block in the chunk was mined out, leaving only fluid behind).
            MeshCollider solidCollider = chunkObject.GetComponent<MeshCollider>();
            if (meshData.FaceCount > 0 || (solidCollider != null && solidCollider.sharedMesh != null))
                EnqueueColliderRebuild(chunk);

            UpdateFluidChunkMesh(chunk, chunkObject, fluidData);
            UpdateFoliageChunkMesh(chunk, chunkObject, foliageData);

            int previousTriangleCount = chunkTriangleCounts.TryGetValue(chunk, out int existingTriangleCount)
                ? existingTriangleCount
                : 0;

            int triangleCount =
                meshData.TriangleCount + meshData.CutoutTriangleCount +
                fluidData.TriangleCount + foliageData.TriangleCount;
            chunkTriangleCounts[chunk] = triangleCount;
            totalTriangleCount += triangleCount - previousTriangleCount;

            return triangleCount;
        }

        static void FillMesh(Mesh mesh, ChunkMeshData data, float boundsPaddingDownY = 0.0f)
        {
            mesh.Clear();
            mesh.SetVertices(data.Vertices);

            // Submesh 0 is always the opaque stream. Submesh 1, when the chunk holds any, carries
            // alpha-cutout indices (leaf canopies) into the SAME vertex buffer, drawn by a second
            // entry in the renderer's shared materials. A chunk with no leaves keeps exactly one
            // submesh and one material, so nothing changes for the common case.
            int cutoutIndexCount = data.CutoutTriangles == null ? 0 : data.CutoutTriangles.Count;
            mesh.subMeshCount = cutoutIndexCount > 0 ? 2 : 1;
            mesh.SetTriangles(data.Triangles, 0);
            if (cutoutIndexCount > 0)
                mesh.SetTriangles(data.CutoutTriangles, 1);

            mesh.SetUVs(0, data.Uvs);
            if (data.FluidVertexData != null && data.FluidVertexData.Count > 0)
                mesh.SetUVs(1, data.FluidVertexData);
            mesh.SetColors(data.Colors);

            // Use the stream's own normals when it supplies them. RecalculateNormals on the foliage
            // mesh would derive sideways normals from two intersecting vertical quads, lighting
            // grass as a pair of walls that read near-black from above; the builder writes explicit
            // up-normals instead. Cube geometry is still happy to be recalculated.
            if (data.Normals != null && data.Normals.Count == data.Vertices.Count)
                mesh.SetNormals(data.Normals);
            else
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            if (boundsPaddingDownY <= 0.0f)
                return;

            // RecalculateBounds measured the undisplaced vertices; the wave only ever moves the
            // surface DOWN, so the bounds only need to grow downward.
            Bounds bounds = mesh.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            min.y -= boundsPaddingDownY;
            bounds.SetMinMax(min, max);
            mesh.bounds = bounds;
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

            FillMesh(mesh, fluidData, MaxWaveDipMeters);
            EnqueueFluidColliderRebuild(chunk);
        }

        // Resolves the dedicated fluid layer by name, falling back to the canonical index when the
        // project's TagManager has not been regenerated yet (fresh clone before a bootstrapper run).
        static int ResolveFluidLayer()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.FluidLayerName);
            return layer >= 0 ? layer : BlockiverseProject.FluidLayerIndex;
        }

        // Same fallback as ResolveFluidLayer: a fresh clone that has not run the bootstrapper yet
        // has no named layer, and the canonical index keeps foliage off the ground mask regardless.
        static int ResolvePassableLayer()
        {
            int layer = LayerMask.NameToLayer(BlockiverseProject.PassableLayerName);
            return layer >= 0 ? layer : BlockiverseProject.PassableLayerIndex;
        }

        // Refills the chunk's pooled foliage mesh in place. Mirrors UpdateFluidChunkMesh, including
        // the "most chunks have none, so never create the child" rule — without that, every chunk
        // in the world would gain a GameObject and the MeshFilter-count tests would rightly fail.
        void UpdateFoliageChunkMesh(ChunkCoordinate chunk, GameObject chunkObject, ChunkMeshData foliageData)
        {
            foliageObjects.TryGetValue(chunk, out GameObject foliageObject);

            if (foliageData.FaceCount == 0)
            {
                if (foliageObject == null)
                    return;

                foliageObject.GetComponent<MeshFilter>().sharedMesh?.Clear();
                foliageObject.GetComponent<MeshCollider>().sharedMesh = null;
                return;
            }

            if (foliageObject == null)
                foliageObject = CreateFoliageObject(chunk, chunkObject);

            MeshFilter filter = foliageObject.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = $"Chunk {chunk} Foliage" };
                filter.sharedMesh = mesh;
            }

            FillMesh(mesh, foliageData);
            EnqueueFoliageColliderRebuild(chunk);
        }

        GameObject CreateFoliageObject(ChunkCoordinate chunk, GameObject chunkObject)
        {
            var foliageObject = new GameObject("Foliage");
            foliageObject.transform.SetParent(chunkObject.transform, false);

            if (passableLayer >= 0)
                foliageObject.layer = passableLayer;

            foliageObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = foliageObject.AddComponent<MeshRenderer>();
            // Grass does not cast. An alpha-tested shadow caster is the most expensive kind on a
            // tile GPU, and the 30 m shadow band is exactly where foliage is densest on screen.
            // Leaf canopies DO cast — they live in the chunk mesh, not here.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            if (cutoutMaterial != null)
                renderer.sharedMaterial = cutoutMaterial;

            // Contact-excluded, exactly like fluid: rays still hit it, so plants stay targetable
            // and harvestable, but nothing ever stands on or is stopped by it. The passable LAYER
            // is what actually keeps the player walking through — excludeLayers is defence in
            // depth, because scene queries ignore it.
            MeshCollider collider = foliageObject.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            collider.excludeLayers = ~0;

            // Deliberately NO TeleportationArea. XRI stops the arc at the first collider with no
            // registered interactable, so leaving it off is what lets the arc pass through grass
            // and land on the ground beneath (§4a.4) — the opposite of the water behaviour.

            foliageObjects[chunk] = foliageObject;
            return foliageObject;
        }

        void EnqueueFoliageColliderRebuild(ChunkCoordinate chunk)
        {
            if (pendingFoliageColliderSet.Add(chunk))
                pendingFoliageColliderRebuilds.Enqueue(chunk);
        }

        bool ProcessNextFoliageColliderRebuild()
        {
            ChunkCoordinate chunk = pendingFoliageColliderRebuilds.Dequeue();
            pendingFoliageColliderSet.Remove(chunk);

            if (!foliageObjects.TryGetValue(chunk, out GameObject foliageObject) || foliageObject == null)
                return false;

            Mesh currentMesh = foliageObject.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = foliageObject.GetComponent<MeshCollider>();

            AssignColliderMesh(collider, currentMesh);
            return true;
        }

        GameObject CreateFluidObject(ChunkCoordinate chunk, GameObject chunkObject)
        {
            var fluidObject = new GameObject("Fluid");
            fluidObject.transform.SetParent(chunkObject.transform, false);

            if (fluidLayer >= 0)
                fluidObject.layer = fluidLayer;

            fluidObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = fluidObject.AddComponent<MeshRenderer>();
            // Fluid never casts: a translucent sheet throwing a solid shadow reads as a bug, and
            // skipping the cast keeps the shadow pass cheaper. (receiveShadows is set for intent
            // only — URP gates shadow receipt on the _RECEIVE_SHADOWS_OFF material keyword, which
            // the voxel shader never declares, so everything using it receives regardless.)
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            // Two materials over a single-submesh mesh: Unity draws the mesh once per material, and
            // the materials' render queues put the depth prime first. Array order here is
            // documentation -- the queues are what actually sequence the draws.
            if (fluidMaterial != null && fluidDepthPrimeMaterial != null)
                renderer.sharedMaterials = new[] { fluidDepthPrimeMaterial, fluidMaterial };
            else if (fluidMaterial != null)
                renderer.sharedMaterial = fluidMaterial;

            // The fluid layer is what actually keeps players out of water: it is absent from
            // GravityProvider's ground sphere-cast mask (scene queries ignore excludeLayers, so the
            // old contact-only approach still read water as ground) and its physics collision-matrix
            // row is cleared by the bootstrapper. excludeLayers is retained as defence in depth.
            // Ray queries still hit the surface, so block targeting, drink/fill, and teleport
            // landing all work. Block targeting resolves through the parent's VoxelChunkTarget.
            MeshCollider collider = fluidObject.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            collider.excludeLayers = ~0;

            // Water is a teleport destination: the ray stops at the surface and the player lands
            // treading, rather than passing through to the seabed.
            ConfigureTeleportationArea(fluidObject, collider);

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

            if (foliageObjects.TryGetValue(chunk, out GameObject foliageObject))
            {
                // Same as fluid: the child goes down with its parent, but its pooled mesh is ours
                // to destroy or it leaks.
                if (foliageObject != null)
                    DestroyGeneratedObject(foliageObject.GetComponent<MeshFilter>()?.sharedMesh);

                foliageObjects.Remove(chunk);
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
            pendingFoliageColliderSet.Remove(chunk);
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
            while (processed < budget &&
                   (pendingColliderRebuilds.Count > 0 ||
                    pendingFluidColliderRebuilds.Count > 0 ||
                    pendingFoliageColliderRebuilds.Count > 0))
            {
                if (pendingColliderRebuilds.Count > 0)
                {
                    if (ProcessNextSolidColliderRebuild())
                        processed++;
                    continue;
                }

                if (pendingFluidColliderRebuilds.Count > 0)
                {
                    if (ProcessNextFluidColliderRebuild())
                        processed++;
                    continue;
                }

                // Foliage is last: it never blocks movement, so a frame that runs out of budget
                // should spend it on geometry the player can actually walk into.
                if (ProcessNextFoliageColliderRebuild())
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

        // A chunk renderer carries one material normally, two when the chunk holds leaves (submesh
        // 1). Kept in one place because the array length is an observable invariant: the water
        // tests assert it, and silently growing it for every chunk would cost a draw call per
        // chunk for geometry most chunks do not have.
        void ApplyChunkMaterials(GameObject chunkObject, bool hasCutoutGeometry)
        {
            var renderer = chunkObject.GetComponent<MeshRenderer>();
            if (renderer == null || chunkMaterial == null)
                return;

            // GetSharedMaterials fills a reusable list; the `sharedMaterials` PROPERTY allocates a
            // fresh Material[] on every read, which on this path would be one allocation per chunk
            // per rebuild — a GC hitch source on Quest for a value we only want the length of.
            renderer.GetSharedMaterials(sharedMaterialScratch);
            int wanted = hasCutoutGeometry && cutoutMaterial != null ? 2 : 1;
            if (sharedMaterialScratch.Count == wanted)
                return;

            renderer.sharedMaterials = wanted == 2
                ? new[] { chunkMaterial, cutoutMaterial }
                : new[] { chunkMaterial };
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
            // Vertex colours bake only static sky exposure, so terrain still needs real shadow
            // maps for the sun/moon and for placed emitters to read as light sources. The cost is
            // bounded by the short shadow distance in the Quest URP asset rather than by disabling
            // the passes outright.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

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
            DestroyGeneratedObject(fluidMaterial);
            DestroyGeneratedObject(fluidDepthPrimeMaterial);
            DestroyGeneratedObject(cutoutMaterial);
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

            foreach (GameObject foliageObject in foliageObjects.Values)
            {
                if (foliageObject == null)
                    continue;

                DestroyGeneratedObject(foliageObject.GetComponent<MeshFilter>()?.sharedMesh);
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
            foliageObjects.Clear();
            chunkTriangleCounts.Clear();
            pendingColliderRebuilds.Clear();
            pendingColliderSet.Clear();
            pendingFluidColliderRebuilds.Clear();
            pendingFluidColliderSet.Clear();
            pendingFoliageColliderRebuilds.Clear();
            pendingFoliageColliderSet.Clear();
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
