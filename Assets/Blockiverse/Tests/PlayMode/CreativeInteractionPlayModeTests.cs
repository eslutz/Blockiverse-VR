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
    }
}
