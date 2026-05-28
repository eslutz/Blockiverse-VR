using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class CreativeInteractionPlayModeTests
    {
        [Test]
        public void FaceNormalPlacementChoosesAdjacentCoordinate()
        {
            Assert.That(
                CreativeInteractionController.ComputePlacementPosition(new BlockPosition(1, 1, 1), Vector3.right),
                Is.EqualTo(new BlockPosition(2, 1, 1)));
            Assert.That(
                CreativeInteractionController.ComputePlacementPosition(new BlockPosition(1, 1, 1), Vector3.down),
                Is.EqualTo(new BlockPosition(1, 0, 1)));
            Assert.That(
                CreativeInteractionController.ComputePlacementPosition(new BlockPosition(1, 1, 1), Vector3.back),
                Is.EqualTo(new BlockPosition(1, 1, 0)));
        }

        [Test]
        public void FakeTargetCanBreakAndPlaceExpectedBlocks()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.MeadowTurf, trackChange: false);

            var controllerObject = new GameObject("Creative Controller");
            var hotbarObject = new GameObject("Hotbar");

            try
            {
                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(BlockRegistry.CreateDefault(), new[] { BlockRegistry.Loam, BlockRegistry.Clearstone }, null);
                hotbar.SelectIndex(1);

                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                controller.Configure(world, BlockRegistry.CreateDefault(), hotbar, null, null);

                Assert.That(controller.TryPlaceBlock(new BlockPosition(1, 0, 1), Vector3.up), Is.True);
                Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Clearstone));

                Assert.That(controller.TryBreakBlock(new BlockPosition(1, 1, 1)), Is.True);
                Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Air));

                Assert.That(controller.UndoLast(), Is.True);
                Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Clearstone));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(hotbarObject);
            }
        }

        [Test]
        public void BlockMutationsRaiseAppliedEventForFeedbackSystems()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.MeadowTurf, trackChange: false);

            var controllerObject = new GameObject("Creative Controller");
            var hotbarObject = new GameObject("Hotbar");

            try
            {
                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(BlockRegistry.CreateDefault(), new[] { BlockRegistry.Clearstone }, null);

                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                controller.Configure(world, BlockRegistry.CreateDefault(), hotbar, null, null);

                var observed = new System.Collections.Generic.List<BlockChange>();
                controller.BlockMutationApplied += change => observed.Add(change);

                Assert.That(controller.TryPlaceBlock(new BlockPosition(1, 0, 1), Vector3.up), Is.True);
                Assert.That(controller.TryBreakBlock(new BlockPosition(1, 1, 1)), Is.True);
                Assert.That(controller.UndoLast(), Is.True);

                Assert.That(observed, Has.Count.EqualTo(3));
                Assert.That(observed[0].NewBlock, Is.EqualTo(BlockRegistry.Clearstone));
                Assert.That(observed[1].NewBlock, Is.EqualTo(BlockRegistry.Air));
                Assert.That(observed[2].NewBlock, Is.EqualTo(BlockRegistry.Clearstone));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(hotbarObject);
            }
        }

        [Test]
        public void BlockMutationsRebuildDirtyChunkMeshes()
        {
            var registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            BlockPosition editedPosition = new(1, 0, 1);
            world.SetBlock(editedPosition, BlockRegistry.MeadowTurf, trackChange: false);

            var worldObject = new GameObject("Creative World");
            var hotbarObject = new GameObject("Hotbar");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, -1);

                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(registry, new[] { BlockRegistry.Loam }, null);

                CreativeInteractionController controller = worldObject.AddComponent<CreativeInteractionController>();
                controller.Configure(world, registry, hotbar, null, null, renderer);

                Assert.That(renderer.Stats.TriangleCount, Is.GreaterThan(0));

                Assert.That(controller.TryBreakBlock(editedPosition), Is.True);
                Assert.That(renderer.Stats.TriangleCount, Is.EqualTo(0));

                Assert.That(controller.UndoLast(), Is.True);
                Assert.That(renderer.Stats.TriangleCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(hotbarObject);
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void PlacementRejectsOutsideWorldBoundsAndPlayerCollision()
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 5);
            world.SetBlock(new BlockPosition(3, 1, 1), BlockRegistry.Slate, trackChange: false);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.Slate, trackChange: false);

            var controllerObject = new GameObject("Creative Controller");
            var hotbarObject = new GameObject("Hotbar");

            try
            {
                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(BlockRegistry.CreateDefault(), new[] { BlockRegistry.Loam }, null);

                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                controller.Configure(
                    world,
                    BlockRegistry.CreateDefault(),
                    hotbar,
                    null,
                    new Bounds(new Vector3(1.5f, 1.5f, 1.5f), Vector3.one));

                Assert.That(controller.TryPlaceBlock(new BlockPosition(3, 1, 1), Vector3.right), Is.False);
                Assert.That(controller.TryPlaceBlock(new BlockPosition(1, 0, 1), Vector3.up), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(hotbarObject);
            }
        }

        [Test]
        public void ClientProxyInteractionCannotCommitAuthoritativeMutation()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 6);
            var breakPosition = new BlockPosition(1, 1, 1);
            var placePosition = new BlockPosition(2, 1, 1);
            world.SetBlock(breakPosition, BlockRegistry.Slate, trackChange: false);

            var controllerObject = new GameObject("Creative Controller");
            var hotbarObject = new GameObject("Hotbar");

            try
            {
                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(registry, new[] { BlockRegistry.Loam }, null);

                BlockMutationAuthority clientAuthority = BlockMutationAuthority.CreateClientProxy(
                    world,
                    registry,
                    localClientId: 7);
                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                controller.Configure(world, registry, hotbar, null, null, authority: clientAuthority);

                Assert.That(controller.TryBreakBlock(breakPosition), Is.False);
                Assert.That(controller.LastMutationResult.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState));
                Assert.That(world.GetBlock(breakPosition), Is.EqualTo(BlockRegistry.Slate));

                Assert.That(controller.TryPlaceAt(placePosition), Is.False);
                Assert.That(controller.LastMutationResult.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState));
                Assert.That(world.GetBlock(placePosition), Is.EqualTo(BlockRegistry.Air));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(hotbarObject);
            }
        }

        [Test]
        public void ClientProxyInteractionCannotUndoAuthoritativeMutationHistory()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 7);
            var position = new BlockPosition(1, 1, 1);
            world.SetBlock(position, BlockRegistry.Slate, trackChange: false);

            var controllerObject = new GameObject("Creative Controller");

            try
            {
                CreativeInteractionController controller = controllerObject.AddComponent<CreativeInteractionController>();
                controller.Configure(world, registry, null, null, null);

                Assert.That(controller.TryBreakBlock(position), Is.True);
                Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Air));

                BlockMutationAuthority clientAuthority = BlockMutationAuthority.CreateClientProxy(
                    world,
                    registry,
                    localClientId: 7);
                controller.Configure(world, registry, null, null, null, authority: clientAuthority);

                Assert.That(controller.UndoLast(), Is.False);
                Assert.That(controller.LastMutationResult.RejectionReason, Is.EqualTo(BlockMutationRejectionReason.ClientCannotCommitAuthoritativeState));
                Assert.That(world.GetBlock(position), Is.EqualTo(BlockRegistry.Air));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void HotbarSelectionUpdatesSelectedBlockLabel()
        {
            var hotbarObject = new GameObject("Hotbar");
            var labelObject = new GameObject("Selected Label");

            try
            {
                Text label = labelObject.AddComponent<Text>();
                CreativeHotbar hotbar = hotbarObject.AddComponent<CreativeHotbar>();
                hotbar.Configure(BlockRegistry.CreateDefault(), new[] { BlockRegistry.Loam, BlockRegistry.Clearstone }, label);

                hotbar.SelectNext();

                Assert.That(hotbar.SelectedBlockId, Is.EqualTo(BlockRegistry.Clearstone));
                Assert.That(label.text, Does.Contain("Clearstone"));
            }
            finally
            {
                Object.DestroyImmediate(labelObject);
                Object.DestroyImmediate(hotbarObject);
            }
        }

        [Test]
        public void PlacementPreviewCanShowAndHideGhostBlock()
        {
            var previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                PlacementPreview preview = previewObject.AddComponent<PlacementPreview>();
                preview.Configure(previewObject.GetComponent<MeshRenderer>());

                preview.ShowAt(new BlockPosition(2, 3, 4), canPlace: true);

                Assert.That(preview.IsVisible, Is.True);
                Assert.That(preview.CurrentPosition, Is.EqualTo(new BlockPosition(2, 3, 4)));
                Assert.That(previewObject.transform.position, Is.EqualTo(new Vector3(2.5f, 3.5f, 4.5f)));

                preview.Hide();

                Assert.That(preview.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(previewObject);
            }
        }

        [Test]
        public void PlacementPreviewUsesPropertyBlockWithoutInstantiatingMaterials()
        {
            var previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material previewMaterial = null;

            try
            {
                previewMaterial = new Material(Shader.Find("Sprites/Default"));
                MeshRenderer renderer = previewObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = previewMaterial;
                Material originalSharedMaterial = renderer.sharedMaterial;
                PlacementPreview preview = previewObject.AddComponent<PlacementPreview>();
                preview.Configure(renderer);

                preview.ShowAt(new BlockPosition(1, 1, 1), canPlace: false);

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);

                Assert.That(renderer.sharedMaterial, Is.SameAs(originalSharedMaterial));
                Assert.That(properties.GetColor("_Color").r, Is.EqualTo(0.95f).Within(0.001f));
                Assert.That(properties.GetColor("_Color").g, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(properties.GetColor("_Color").b, Is.EqualTo(0.20f).Within(0.001f));
                Assert.That(properties.GetColor("_Color").a, Is.EqualTo(0.42f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(previewObject);
                Object.DestroyImmediate(previewMaterial);
            }
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
    }
}
