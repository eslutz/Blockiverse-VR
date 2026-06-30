using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEditor;
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
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Assert.That(mesh.FaceCount, Is.EqualTo(6));
            Assert.That(mesh.Vertices, Has.Count.EqualTo(24));
            Assert.That(mesh.Triangles, Has.Count.EqualTo(36));
            Assert.That(mesh.Uvs, Has.Count.EqualTo(24));
            Assert.That(mesh.Colors, Has.Count.EqualTo(24));
        }

        [Test]
        public void MeshBuilderReusesSolidAndFluidOutputLists()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 1);
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(2, 1, 1), BlockRegistry.Freshwater, trackChange: false);

            ChunkMeshData firstSolid = ChunkMeshBuilder.Build(
                world,
                registry,
                new ChunkCoordinate(0, 0, 0),
                out ChunkMeshData firstFluid);
            List<Vector3> solidVertices = firstSolid.Vertices;
            List<int> solidTriangles = firstSolid.Triangles;
            List<Vector2> solidUvs = firstSolid.Uvs;
            List<Color> solidColors = firstSolid.Colors;
            List<Vector3> fluidVertices = firstFluid.Vertices;
            List<int> fluidTriangles = firstFluid.Triangles;
            List<Vector2> fluidUvs = firstFluid.Uvs;
            List<Color> fluidColors = firstFluid.Colors;

            Assert.That(firstSolid.FaceCount, Is.GreaterThan(0));
            Assert.That(firstFluid.FaceCount, Is.GreaterThan(0));

            ChunkMeshData secondSolid = ChunkMeshBuilder.Build(
                world,
                registry,
                new ChunkCoordinate(0, 0, 0),
                out ChunkMeshData secondFluid);

            Assert.That(secondSolid.Vertices, Is.SameAs(solidVertices));
            Assert.That(secondSolid.Triangles, Is.SameAs(solidTriangles));
            Assert.That(secondSolid.Uvs, Is.SameAs(solidUvs));
            Assert.That(secondSolid.Colors, Is.SameAs(solidColors));
            Assert.That(secondFluid.Vertices, Is.SameAs(fluidVertices));
            Assert.That(secondFluid.Triangles, Is.SameAs(fluidTriangles));
            Assert.That(secondFluid.Uvs, Is.SameAs(fluidUvs));
            Assert.That(secondFluid.Colors, Is.SameAs(fluidColors));
        }

        [Test]
        public void MeshBuilderRemovesInternalFacesBetweenAdjacentSolidBlocks()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 1);
            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(2, 1, 1), BlockRegistry.LooseLoam, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Assert.That(mesh.FaceCount, Is.EqualTo(10));
            Assert.That(mesh.Triangles, Has.Count.EqualTo(60));
        }

        [Test]
        public void DirtyChunkQueueDoesNotMarkNeighborChunksOutsideLightProbeRange()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);

            CollectionAssert.AreEquivalent(
                new[] { new ChunkCoordinate(0, 0, 0) },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void DirtyChunkQueueMarksLightProbeAffectedNeighborChunks()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(4, 1, 4), BlockRegistry.Graystone);

            CollectionAssert.AreEquivalent(
                new[] { new ChunkCoordinate(0, 0, 0), new ChunkCoordinate(1, 0, 0) },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void DirtyChunkQueueMarksLowerChunksWhenSkyColumnChanges()
        {
            var world = new VoxelWorld(new WorldBounds(16, 48, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(1, 34, 1), BlockRegistry.Graystone);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new ChunkCoordinate(0, 0, 0),
                    new ChunkCoordinate(0, 1, 0),
                    new ChunkCoordinate(0, 2, 0)
                },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void DirtyChunkQueueMarksNeighborChunkWhenBorderBlockChanges()
        {
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            world.SetBlock(new BlockPosition(15, 1, 4), BlockRegistry.Graystone);

            CollectionAssert.AreEquivalent(
                new[] { new ChunkCoordinate(0, 0, 0), new ChunkCoordinate(1, 0, 0) },
                queue.DrainDirtyChunks().ToArray());
        }

        [Test]
        public void DirtyChunkQueueCanDrainAPartialBudget()
        {
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            queue.MarkDirty(new ChunkCoordinate(0, 0, 0));
            queue.MarkDirty(new ChunkCoordinate(1, 0, 0));
            queue.MarkDirty(new ChunkCoordinate(2, 0, 0));

            IReadOnlyCollection<ChunkCoordinate> firstDrain = queue.DrainDirtyChunks(maxCount: 2);

            Assert.That(firstDrain, Has.Count.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.DrainDirtyChunks(), Has.Count.EqualTo(1));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void DirtyChunkQueueConvenienceDrainReusesSnapshotCollection()
        {
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);

            queue.MarkDirty(new ChunkCoordinate(0, 0, 0));
            IReadOnlyCollection<ChunkCoordinate> firstDrain = queue.DrainDirtyChunks();

            queue.MarkDirty(new ChunkCoordinate(1, 0, 0));
            IReadOnlyCollection<ChunkCoordinate> secondDrain = queue.DrainDirtyChunks();

            Assert.That(secondDrain, Is.SameAs(firstDrain),
                "The convenience drain should reuse its per-queue snapshot list instead of allocating per tick.");
            Assert.That(secondDrain, Is.EquivalentTo(new[] { new ChunkCoordinate(1, 0, 0) }));
        }

        [Test]
        public void DirtyChunkQueueCanDrainIntoReusableScratchList()
        {
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 1);
            var queue = new ChunkRebuildQueue(world);
            var scratch = new List<ChunkCoordinate>
            {
                new(99, 99, 99)
            };

            queue.MarkDirty(new ChunkCoordinate(0, 0, 0));
            queue.MarkDirty(new ChunkCoordinate(1, 0, 0));

            int drained = queue.DrainDirtyChunks(scratch, maxCount: 1);

            Assert.That(drained, Is.EqualTo(1));
            Assert.That(scratch, Has.Count.EqualTo(1));
            Assert.That(scratch[0], Is.Not.EqualTo(new ChunkCoordinate(99, 99, 99)));
            Assert.That(queue.Count, Is.EqualTo(1));

            drained = queue.DrainDirtyChunks(scratch, maxCount: 8);

            Assert.That(drained, Is.EqualTo(1));
            Assert.That(scratch, Has.Count.EqualTo(1));
            Assert.That(queue.Count, Is.Zero);
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
        public void VisualAtlasTextureSetManifestIsCompleteAndValid()
        {
            foreach (string setId in BlockTextureSetIds.All)
            {
                string path = BlockVisualAtlas.AtlasPathForTextureSet(setId);
                Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                Assert.That(atlas, Is.Not.Null, $"Atlas texture for set '{setId}' must exist at {path}");
                Assert.That(atlas.width, Is.EqualTo(BlockVisualAtlas.AtlasWidthPixels), $"Atlas '{setId}' width mismatch.");
                Assert.That(atlas.height, Is.EqualTo(BlockVisualAtlas.AtlasHeightPixels), $"Atlas '{setId}' height mismatch.");
                Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point), $"Atlas '{setId}' must be point-filtered.");

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null);

                // Repo policy check: uncompressed RGB24 for pixel-art clarity.
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed),
                    $"Atlas '{setId}' should be uncompressed to match repo pixel-art policy.");

                // Ensure no ASTC overrides for Android.
                TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
                if (androidSettings.overridden)
                {
                    Assert.That(androidSettings.format, Is.Not.EqualTo(TextureImporterFormat.ASTC_4x4)
                        .And.Not.EqualTo(TextureImporterFormat.ASTC_5x5)
                        .And.Not.EqualTo(TextureImporterFormat.ASTC_6x6)
                        .And.Not.EqualTo(TextureImporterFormat.ASTC_8x8)
                        .And.Not.EqualTo(TextureImporterFormat.ASTC_10x10)
                        .And.Not.EqualTo(TextureImporterFormat.ASTC_12x12),
                        $"Atlas '{setId}' should not use ASTC compression on Android.");
                }
            }
        }

        [Test]
        public void VisualRebuildBudgetDrainIsPersistentAcrossMultipleCalls()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);
                renderer.VisualRebuildBudget = 1;

                // Mark 3 chunks dirty (each in a different 16x16 chunk).
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(17, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(33, 1, 1), BlockRegistry.Graystone);

                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(3));

                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(2));

                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(1));

                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidColliderThrottlingIsSeparateFromVisualRebuild()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);
                
                renderer.VisualRebuildBudget = 1;
                renderer.ColliderRebuildBudget = 0;

                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Freshwater);
                renderer.RebuildDirty();

                // Visual should be done (1 chunk rebuilt).
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(0));
                // Fluid collider should be pending (budget was 0).
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void RendererReusesThePooledChunkMeshAcrossDirtyRebuilds()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            var editedPosition = new BlockPosition(1, 0, 1);
            // A second block keeps the chunk non-empty across the edit, so it retains its render
            // object (empty chunks are released — see RendererReleasesChunkObjectWhenEditedToEmpty)
            // and the pooled-mesh-reuse invariant remains observable.
            world.SetBlock(editedPosition, BlockRegistry.MeadowTurf, trackChange: false);
            world.SetBlock(new BlockPosition(3, 0, 3), BlockRegistry.MeadowTurf, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                MeshFilter filter = worldObject.GetComponentInChildren<MeshFilter>();
                Mesh firstMesh = filter.sharedMesh;
                Assert.That(firstMesh, Is.Not.Null);
                Assert.That(firstMesh.vertexCount, Is.GreaterThan(0));

                // Edit the chunk (remove one of its two blocks) while it stays non-empty.
                world.SetBlock(editedPosition, BlockRegistry.Air);
                renderer.RebuildDirty();

                // One pooled Mesh per chunk: rebuilds refill the same instance (no allocation
                // churn), and the refilled geometry reflects the edit (one block remains).
                Assert.That(filter.sharedMesh, Is.SameAs(firstMesh));
                Assert.That(firstMesh.vertexCount, Is.GreaterThan(0), "Expected the pooled mesh to be refilled with the post-edit geometry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void RendererSkipsObjectsForAllAirChunks()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            // Three 16-wide chunks; put a solid block only in the middle chunk so the outer two
            // are entirely air.
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(20, 1, 1), BlockRegistry.Graystone, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                // Only the one non-empty chunk creates a GameObject/MeshFilter/MeshCollider; the
                // two all-air chunks create nothing.
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(1));
                Assert.That(worldObject.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(1));
                Assert.That(worldObject.GetComponentsInChildren<VoxelChunkTarget>().Length, Is.EqualTo(1));
                Assert.That(renderer.Stats.ChunkCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void RendererReleasesChunkObjectWhenEditedToEmpty()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 5);
            var only = new BlockPosition(1, 0, 1);
            world.SetBlock(only, BlockRegistry.MeadowTurf, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(1));
                Assert.That(renderer.Stats.ChunkCount, Is.EqualTo(1));

                // Mining out the chunk's only block makes it all-air: its object is released.
                world.SetBlock(only, BlockRegistry.Air);
                renderer.RebuildDirty();

                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.Zero,
                    "An edited-to-empty chunk should release its render object.");
                Assert.That(worldObject.GetComponentsInChildren<MeshCollider>().Length, Is.Zero);
                Assert.That(renderer.Stats.ChunkCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void VoidBuilderWorldCreatesObjectsOnlyForThePlatformRegion()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 48,
                height: 48,
                depth: 48,
                chunkSize: 16,
                seed: 2201,
                groundHeight: 24);
            VoxelWorld world = new VoidBuilderPreset(registry, settings).Generate();
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                // The void world is almost entirely air apart from a single small cutstone
                // platform, so only the chunk(s) holding the platform create render objects — far
                // fewer than the 3x3x3 = 27 total chunks.
                int created = renderer.Stats.ChunkCount;
                Assert.That(created, Is.GreaterThan(0), "The platform region must render.");
                Assert.That(created, Is.LessThanOrEqualTo(2),
                    "A mostly-air void world should create objects only for the platform region.");
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(created));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void ColliderRebuildsAreThrottledAndDrainViaPump()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            var editedPosition = new BlockPosition(1, 0, 1);
            world.SetBlock(editedPosition, BlockRegistry.MeadowTurf, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                // The initial bake flushes every collider so a fresh world has full collision.
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0));

                MeshFilter filter = worldObject.GetComponentInChildren<MeshFilter>();
                MeshCollider collider = worldObject.GetComponentInChildren<MeshCollider>();

                // Add a second block so the chunk stays non-empty (its pooled mesh keeps vertices,
                // so the collider holds a real mesh throughout). With a zero budget the edit
                // refills the visual mesh immediately but defers the collider rebake.
                renderer.ColliderRebuildBudget = 0;
                world.SetBlock(new BlockPosition(2, 0, 2), BlockRegistry.MeadowTurf);
                renderer.RebuildDirty();

                // The throttle is observable through the pending count (meshes are pooled, so the
                // filter and collider share the same instance regardless of bake timing).
                Assert.That(renderer.PendingColliderRebuildCount, Is.GreaterThan(0), "Collider rebakes should be throttled by the budget.");

                // Draining the backlog recooks the collider from the refilled non-empty mesh.
                renderer.ProcessPendingColliderRebuilds(int.MaxValue);

                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0));
                Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                Assert.That(collider.sharedMesh.vertexCount, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidColliderRebuildsUseTheColliderBudget()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.Freshwater, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0));

                MeshFilter fluidFilter = worldObject
                    .GetComponentsInChildren<MeshFilter>()
                    .Single(filter => filter.gameObject.name == "Fluid");
                MeshCollider fluidCollider = fluidFilter.GetComponent<MeshCollider>();
                Assert.That(fluidCollider.sharedMesh, Is.SameAs(fluidFilter.sharedMesh));

                renderer.ColliderRebuildBudget = 0;
                world.SetBlock(new BlockPosition(2, 0, 1), BlockRegistry.FreshwaterFlow);
                renderer.RebuildDirty();

                Assert.That(renderer.PendingColliderRebuildCount, Is.GreaterThan(0),
                    "Fluid collider recooks should be throttled by the same budget as solid colliders.");

                renderer.ProcessPendingColliderRebuilds(int.MaxValue);

                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0));
                Assert.That(fluidCollider.sharedMesh, Is.SameAs(fluidFilter.sharedMesh));
                Assert.That(fluidCollider.sharedMesh.vertexCount, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void DirtyVisualRebuildsAreBudgetedAcrossCalls()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);
                renderer.VisualRebuildBudget = 1;

                world.SetBlock(new BlockPosition(15, 1, 4), BlockRegistry.Graystone);
                renderer.RebuildDirty();

                Assert.That(renderer.Stats.QueuedRebuildCount, Is.GreaterThanOrEqualTo(1));

                renderer.RebuildDirty();

                Assert.That(renderer.Stats.QueuedRebuildCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void DeferredInitialRebuildQueuesChunksAndHonorsBudgets()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(32, 16, 16), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.MeadowTurf, trackChange: false);
            world.SetBlock(new BlockPosition(20, 0, 1), BlockRegistry.Graystone, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.VisualRebuildBudget = 1;
                renderer.ColliderRebuildBudget = 0;

                renderer.Configure(world, registry, blockMaterial, -1, deferInitialRebuild: true);

                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(2));
                Assert.That(renderer.Stats.QueuedRebuildCount, Is.EqualTo(2));
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.Zero);

                renderer.RebuildDirty();

                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(1));
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(1));
                Assert.That(renderer.PendingColliderRebuildCount, Is.GreaterThan(0));

                renderer.RebuildDirty();

                Assert.That(renderer.PendingVisualRebuildCount, Is.Zero);
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(2));
                Assert.That(renderer.PendingColliderRebuildCount, Is.GreaterThan(0));

                renderer.ProcessPendingColliderRebuilds(int.MaxValue);

                Assert.That(renderer.PendingColliderRebuildCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void RebuildSpawnRegionBakesOnlyTheSpawnNeighbourhoodAndLeavesTheRestQueued()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            // 5x1x5 = 25 chunks; a full ground layer makes every chunk carry geometry so the
            // queued count is the whole world and a bounded spawn bake is clearly a subset.
            var world = new VoxelWorld(new WorldBounds(80, 16, 80), chunkSize: 16, seed: 5);
            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                    world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.Graystone, trackChange: false);
            }

            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1, deferInitialRebuild: true);

                // Deferred Configure only queues; nothing is meshed or ready yet.
                Assert.That(renderer.SpawnRegionReady, Is.False);
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(25));
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.Zero);

                // Spawn at the centre chunk (2,0,2); radius 1 => a 3x1x3 = 9 chunk neighbourhood.
                renderer.RebuildSpawnRegion(new BlockPosition(40, 1, 40));

                Assert.That(renderer.SpawnRegionReady, Is.True, "Spawn-region bake must lift the world-ready gate.");
                // Only the 9 spawn chunks were meshed — the other 16 are still queued. This is the
                // behavioural proof the deferred path never performs an unbounded world-wide bake.
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(9));
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(16));
                // Spawn colliders are baked within their bounded budget; no world-wide flush leaves
                // the rest of the world's colliders unbaked (they have no GameObject yet).
                Assert.That(renderer.PendingColliderRebuildCount, Is.Zero);

                MeshCollider spawnCollider = null;
                foreach (MeshCollider collider in worldObject.GetComponentsInChildren<MeshCollider>())
                {
                    if (collider.gameObject.name == "Chunk 2,0,2")
                        spawnCollider = collider;
                }

                Assert.That(spawnCollider, Is.Not.Null, "The spawn chunk must have a render object with a collider.");
                Assert.That(spawnCollider.sharedMesh, Is.Not.Null);
                Assert.That(spawnCollider.sharedMesh.vertexCount, Is.GreaterThan(0),
                    "The spawn chunk's collider must be baked so the player lands on solid ground.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
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

            Assert.That(
                renderableBlocks.All(block =>
                {
                    Rect rect = BlockVisualAtlas.GetTileRect(block.Id);
                    return rect.width > 0.0f && rect.height > 0.0f;
                }),
                Is.True,
                "Every renderable block must map to a positive-area atlas tile.");

            // Flow cells intentionally render with their family's source tile (flowing water
            // reads as water), so they are the only permitted tile sharers. Every other
            // renderable block must own a distinct tile.
            BlockDefinition[] nonFlowBlocks = renderableBlocks
                .Where(block => !FluidBlocks.IsFlow(block.Id))
                .ToArray();
            Rect[] nonFlowRects = nonFlowBlocks
                .Select(block => BlockVisualAtlas.GetTileRect(block.Id))
                .ToArray();
            Assert.That(nonFlowRects.Distinct().Count(), Is.EqualTo(nonFlowBlocks.Length),
                "Non-flow renderable blocks must each have a distinct atlas tile.");

            foreach (BlockDefinition block in renderableBlocks.Where(b => FluidBlocks.IsFlow(b.Id)))
            {
                Assert.That(FluidBlocks.TryGetFamily(block.Id, out FluidFamily family), Is.True);
                Assert.That(
                    BlockVisualAtlas.GetTileRect(block.Id),
                    Is.EqualTo(BlockVisualAtlas.GetTileRect(FluidBlocks.SourceOf(family))),
                    $"{block.Name} should share its family source's atlas tile.");
            }
        }

        [Test]
        public void VisualAtlasRejectsRenderableBlocksWithoutTileMapping()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            registry.Register(new BlockDefinition(new BlockId(99), "test_missing_tile", "Missing Tile", BlockCategory.Crafted, isSolid: true, isRenderable: true));

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
            world.SetBlock(new BlockPosition(5, 1, 1), BlockRegistry.Graystone, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            Rect meadowRect = BlockVisualAtlas.GetTileRect(BlockRegistry.MeadowTurf);
            Rect slateRect = BlockVisualAtlas.GetTileRect(BlockRegistry.Graystone);

            Assert.That(mesh.Uvs.Any(uv => IsInside(uv, meadowRect)), Is.True);
            Assert.That(mesh.Uvs.Any(uv => IsInside(uv, slateRect)), Is.True);
        }

        [Test]
        public void MeshBuilderAppliesDarkerVertexColorsInsideTunnel()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);

            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int y = 0; y < world.Bounds.Height; y++)
                {
                    for (int z = 0; z < world.Bounds.Depth; z++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);
                }
            }

            for (int x = 0; x < 6; x++)
                world.SetBlock(new BlockPosition(x, 3, 3), BlockRegistry.Air, trackChange: false);

            ChunkMeshData mesh = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0));

            float brightest = mesh.Colors.Max(color => color.grayscale);
            float darkest = mesh.Colors.Min(color => color.grayscale);

            Assert.That(mesh.Colors, Has.Count.EqualTo(mesh.Vertices.Count));
            Assert.That(brightest, Is.GreaterThan(darkest));
            Assert.That(darkest, Is.LessThan(0.55f));
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

        [Test]
        public void VisualRebuildBudgetClampedToAtLeastOne()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);
                
                // Set budget to 0 or negative
                renderer.VisualRebuildBudget = 0;

                // Mark 3 chunks dirty
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(17, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(33, 1, 1), BlockRegistry.Graystone);

                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(3));

                // Should rebuild exactly 1 chunk even with 0 budget (clamped to 1)
                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(2));

                renderer.VisualRebuildBudget = -5;
                // Should rebuild exactly 1 chunk even with negative budget
                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void VisualRebuildBudgetDrainWithDynamicBudgets()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(64, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);
                
                // Mark 4 chunks dirty
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(17, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(33, 1, 1), BlockRegistry.Graystone);
                world.SetBlock(new BlockPosition(49, 1, 1), BlockRegistry.Graystone);

                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(4));

                // Call with budget 1
                renderer.VisualRebuildBudget = 1;
                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(3));

                // Change budget dynamically to 2
                renderer.VisualRebuildBudget = 2;
                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(1));

                // Change budget dynamically to 5
                renderer.VisualRebuildBudget = 5;
                renderer.RebuildDirty();
                Assert.That(renderer.PendingVisualRebuildCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidColliderThrottlingDrainsStepByStepUnderPositiveBudget()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(48, 16, 16), chunkSize: 16, seed: 5);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                // Clear initial bakes
                renderer.ProcessPendingColliderRebuilds(int.MaxValue);
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0));

                // Set collider budget to 1, visual budget to 3 (so visual updates happen all at once, but colliders are step-by-step)
                renderer.VisualRebuildBudget = 3;
                renderer.ColliderRebuildBudget = 1;

                // Edit blocks in 3 chunks to have fluid
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Freshwater);
                world.SetBlock(new BlockPosition(17, 1, 1), BlockRegistry.Freshwater);
                world.SetBlock(new BlockPosition(33, 1, 1), BlockRegistry.Freshwater);

                // This rebuilds all 3 visuals, but only bakes 1 fluid collider due to budget=1
                renderer.RebuildDirty();
                
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(0));
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(2), "Should have processed 1 and left 2 pending.");

                // RebuildDirty when nothing is visually dirty still pumps colliders
                renderer.RebuildDirty();
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(1), "Should have processed 1 more and left 1 pending.");

                renderer.RebuildDirty();
                Assert.That(renderer.PendingColliderRebuildCount, Is.EqualTo(0), "Should have processed the last pending collider.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(worldObject);
                UnityEngine.Object.DestroyImmediate(blockMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
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
