using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WorldEditServiceEditModeTests
    {
        VoxelWorld world;
        WorldEditService service;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(16, 8, 16), chunkSize: 16, seed: 1);
            service = new WorldEditService();
        }

        [Test]
        public void FillSetsAllBlocksInRegion()
        {
            var min = new BlockPosition(0, 0, 0);
            var max = new BlockPosition(3, 3, 3);

            WorldEditResult result = service.Fill(world, min, max, BlockRegistry.Graystone);

            Assert.That(result, Is.EqualTo(WorldEditResult.Success));

            for (int y = 0; y <= 3; y++)
            for (int z = 0; z <= 3; z++)
            for (int x = 0; x <= 3; x++)
                Assert.That(world.GetBlock(new BlockPosition(x, y, z)), Is.EqualTo(BlockRegistry.Graystone));
        }

        [Test]
        public void FillRejectsVolumeExceedingLimit()
        {
            var largeWorld = new VoxelWorld(new WorldBounds(64, 64, 64), chunkSize: 16, seed: 1);
            var min = new BlockPosition(0, 0, 0);
            var max = new BlockPosition(32, 32, 31);

            WorldEditResult result = service.Fill(largeWorld, min, max, BlockRegistry.Graystone);

            Assert.That(result, Is.EqualTo(WorldEditResult.VolumeLimitExceeded));
        }

        [Test]
        public void FillRejectsOutOfBoundsRegion()
        {
            var min = new BlockPosition(0, 0, 0);
            var max = new BlockPosition(100, 100, 100);

            WorldEditResult result = service.Fill(world, min, max, BlockRegistry.Graystone);

            Assert.That(result, Is.EqualTo(WorldEditResult.OutOfBounds));
        }

        [Test]
        public void ReplaceSwapsOnlyMatchingBlocks()
        {
            world.SetBlock(new BlockPosition(0, 0, 0), BlockRegistry.Graystone);
            world.SetBlock(new BlockPosition(1, 0, 0), BlockRegistry.LooseLoam);
            world.SetBlock(new BlockPosition(2, 0, 0), BlockRegistry.Graystone);

            var min = new BlockPosition(0, 0, 0);
            var max = new BlockPosition(2, 0, 0);

            WorldEditResult result = service.Replace(world, min, max, BlockRegistry.Graystone, BlockRegistry.BranchwoodLog);

            Assert.That(result, Is.EqualTo(WorldEditResult.Success));
            Assert.That(world.GetBlock(new BlockPosition(0, 0, 0)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(1, 0, 0)), Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(world.GetBlock(new BlockPosition(2, 0, 0)), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void ReplaceWithNoMatchingBlocksReturnsNothingToReplace()
        {
            var min = new BlockPosition(0, 0, 0);
            var max = new BlockPosition(2, 0, 0);

            WorldEditResult result = service.Replace(world, min, max, BlockRegistry.Graystone, BlockRegistry.BranchwoodLog);

            Assert.That(result, Is.EqualTo(WorldEditResult.NothingToReplace));
        }

        [Test]
        public void DeleteClearsRegionToAir()
        {
            service.Fill(world, new BlockPosition(0, 0, 0), new BlockPosition(2, 2, 2), BlockRegistry.Graystone);
            service.Delete(world, new BlockPosition(0, 0, 0), new BlockPosition(2, 2, 2));

            for (int y = 0; y <= 2; y++)
            for (int z = 0; z <= 2; z++)
            for (int x = 0; x <= 2; x++)
                Assert.That(world.GetBlock(new BlockPosition(x, y, z)), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void CopyPasteRoundTrip()
        {
            world.SetBlock(new BlockPosition(0, 0, 0), BlockRegistry.Graystone);
            world.SetBlock(new BlockPosition(1, 0, 0), BlockRegistry.BranchwoodLog);
            world.SetBlock(new BlockPosition(0, 1, 0), BlockRegistry.LooseLoam);

            WorldEditResult copyResult = service.Copy(world, new BlockPosition(0, 0, 0), new BlockPosition(1, 1, 0));
            Assert.That(copyResult, Is.EqualTo(WorldEditResult.Success));
            Assert.That(service.HasClipboard, Is.True);

            WorldEditResult pasteResult = service.Paste(world, new BlockPosition(4, 0, 0));
            Assert.That(pasteResult, Is.EqualTo(WorldEditResult.Success));

            Assert.That(world.GetBlock(new BlockPosition(4, 0, 0)), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(world.GetBlock(new BlockPosition(5, 0, 0)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(4, 1, 0)), Is.EqualTo(BlockRegistry.LooseLoam));
        }

        [Test]
        public void UndoRestoresPreviousState()
        {
            var pos = new BlockPosition(0, 0, 0);
            world.SetBlock(pos, BlockRegistry.Graystone);

            service.Fill(world, pos, pos, BlockRegistry.BranchwoodLog);
            Assert.That(world.GetBlock(pos), Is.EqualTo(BlockRegistry.BranchwoodLog));

            WorldEditResult undoResult = service.Undo(world);
            Assert.That(undoResult, Is.EqualTo(WorldEditResult.Success));
            Assert.That(world.GetBlock(pos), Is.EqualTo(BlockRegistry.Graystone));
        }

        [Test]
        public void RedoReappliesAfterUndo()
        {
            var pos = new BlockPosition(0, 0, 0);
            service.Fill(world, pos, pos, BlockRegistry.BranchwoodLog);
            service.Undo(world);

            WorldEditResult redoResult = service.Redo(world);
            Assert.That(redoResult, Is.EqualTo(WorldEditResult.Success));
            Assert.That(world.GetBlock(pos), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void NewActionClearsRedoHistory()
        {
            var pos = new BlockPosition(0, 0, 0);
            service.Fill(world, pos, pos, BlockRegistry.Graystone);
            service.Undo(world);
            Assert.That(service.RedoCount, Is.EqualTo(1));

            service.Fill(world, pos, pos, BlockRegistry.BranchwoodLog);
            Assert.That(service.RedoCount, Is.EqualTo(0));
        }

        [Test]
        public void UndoHistoryCappedAtLimit()
        {
            var pos = new BlockPosition(0, 0, 0);

            for (int i = 0; i <= GameModeConstants.CreativeUndoHistoryLimit; i++)
                service.Fill(world, pos, pos, i % 2 == 0 ? BlockRegistry.Graystone : BlockRegistry.Air);

            Assert.That(service.UndoCount, Is.EqualTo(GameModeConstants.CreativeUndoHistoryLimit));
        }

        [Test]
        public void UndoOnEmptyHistoryReturnsNothingToUndo()
        {
            WorldEditResult result = service.Undo(world);
            Assert.That(result, Is.EqualTo(WorldEditResult.NothingToUndo));
        }

        [Test]
        public void PasteWithoutClipboardReturnsNoClipboard()
        {
            WorldEditResult result = service.Paste(world, new BlockPosition(0, 0, 0));
            Assert.That(result, Is.EqualTo(WorldEditResult.NoClipboard));
        }

        [Test]
        public void CopyRejectsVolumeExceedingLimit()
        {
            var hugeWorld = new VoxelWorld(new WorldBounds(64, 64, 64), chunkSize: 16, seed: 1);
            WorldEditResult result = service.Copy(hugeWorld, new BlockPosition(0, 0, 0), new BlockPosition(40, 40, 40));
            Assert.That(result, Is.EqualTo(WorldEditResult.VolumeLimitExceeded));
        }

        [Test]
        public void PasteDoesNotOverwriteExistingBlocksWithAir()
        {
            // Region (0,0,0)-(1,0,0): Graystone at x=0, Air at x=1
            world.SetBlock(new BlockPosition(0, 0, 0), BlockRegistry.Graystone);

            service.Copy(world, new BlockPosition(0, 0, 0), new BlockPosition(1, 0, 0));

            // Destination: LooseLoam at x=5 (where the clipboard's air slot would land)
            world.SetBlock(new BlockPosition(5, 0, 0), BlockRegistry.LooseLoam);

            service.Paste(world, new BlockPosition(4, 0, 0));

            Assert.That(world.GetBlock(new BlockPosition(4, 0, 0)), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(world.GetBlock(new BlockPosition(5, 0, 0)), Is.EqualTo(BlockRegistry.LooseLoam));
        }
    }
}
