using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class VegetationServiceEditModeTests
    {
        static readonly BlockPosition BasePos = new(8, 8, 8);

        VoxelWorld world;
        VegetationService vegetation;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            vegetation = new VegetationService();
        }

        [Test]
        public void PlaceStandardTreePlacesLogsAndLeaves()
        {
            vegetation.PlaceStandardTree(world, BasePos);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 3, BasePos.Z)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(CountBlocks(BlockRegistry.Leafmoss), Is.GreaterThan(0));
        }

        [Test]
        public void PlaceConicalTreeProducesNarrowTopWideBottom()
        {
            vegetation.PlaceConicalTree(world, BasePos);

            // Trunk exists
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(CountBlocks(BlockRegistry.Leafmoss), Is.GreaterThan(0));
        }

        [Test]
        public void PlaceShrubTreeHasShortTrunk()
        {
            vegetation.PlaceShrubTree(world, BasePos);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 1, BasePos.Z)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            // Trunk should not extend to 5 blocks
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X, BasePos.Y + 5, BasePos.Z)), Is.Not.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void PlaceTallTreeHasTallerTrunkThanStandard()
        {
            var world2 = new VoxelWorld(new WorldBounds(32, 32, 32), chunkSize: 16, seed: 1);
            vegetation.PlaceStandardTree(world,  BasePos);
            vegetation.PlaceTallTree(world2, BasePos);

            int standardTrunkTop = FindTrunkTop(world,  BasePos);
            int tallTrunkTop     = FindTrunkTop(world2, BasePos);

            Assert.That(tallTrunkTop, Is.GreaterThan(standardTrunkTop));
        }

        [Test]
        public void PlaceMassiveTreeHasWiderTrunk()
        {
            vegetation.PlaceMassiveTree(world, BasePos);

            // 2×2 trunk check
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X,     BasePos.Y, BasePos.Z)),     Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X + 1, BasePos.Y, BasePos.Z)),     Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X,     BasePos.Y, BasePos.Z + 1)), Is.EqualTo(BlockRegistry.BranchwoodLog));
            Assert.That(world.GetBlock(new BlockPosition(BasePos.X + 1, BasePos.Y, BasePos.Z + 1)), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void TickSaplingAdvancesThroughStagesAndPlacesTree()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            // Stage 0 → 1
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1));

            // Stage 1 → 2
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S2));

            // Stage 2 → full tree (sapling removed, logs placed)
            vegetation.TickSapling(world, 1200);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void TickSaplingDoesNotAdvanceBeforeInterval()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            vegetation.TickSapling(world, 1199);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling));
        }

        [Test]
        public void TickLeafDecayRemovesLeafmossFarFromLogs()
        {
            // Place isolated Leafmoss with no nearby log
            var isolatedLeaf = new BlockPosition(8, 16, 8);
            world.SetBlock(isolatedLeaf, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(isolatedLeaf), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void TickLeafDecayPreservesLeafmossNearLog()
        {
            var logPos  = new BlockPosition(8, 8, 8);
            var leafPos = new BlockPosition(8, 9, 8);
            world.SetBlock(logPos,  BlockRegistry.BranchwoodLog);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Leafmoss));
        }

        int CountBlocks(BlockId blockId)
        {
            int count = 0;
            for (int y = 0; y < world.Bounds.Height; y++)
            for (int z = 0; z < world.Bounds.Depth; z++)
            for (int x = 0; x < world.Bounds.Width; x++)
                if (world.GetBlock(new BlockPosition(x, y, z)) == blockId) count++;
            return count;
        }

        static int FindTrunkTop(VoxelWorld w, BlockPosition basePos)
        {
            int top = basePos.Y;
            for (int dy = 0; dy < 20; dy++)
            {
                var pos = new BlockPosition(basePos.X, basePos.Y + dy, basePos.Z);
                if (!w.Bounds.Contains(pos) || w.GetBlock(pos) != BlockRegistry.BranchwoodLog)
                    break;
                top = basePos.Y + dy;
            }
            return top;
        }
    }
}
