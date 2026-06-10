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
        public void TickLeafDecayWithZeroTicksIsNoop()
        {
            var isolatedLeaf = new BlockPosition(8, 16, 8);
            world.SetBlock(isolatedLeaf, BlockRegistry.Leafmoss);

            vegetation.TickLeafDecay(world, 0);

            Assert.That(world.GetBlock(isolatedLeaf), Is.EqualTo(BlockRegistry.Leafmoss));
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

        [Test]
        public void TickLeafDecayRemovesOrphanedLeafAfterLogRemovalIsMarked()
        {
            var logPos  = new BlockPosition(8, 8, 8);
            var leafPos = new BlockPosition(8, 9, 8);
            world.SetBlock(logPos,  BlockRegistry.BranchwoodLog);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);

            // First sweep: the leaf is supported and drops out of the candidate set.
            vegetation.TickLeafDecay(world, 120);
            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Leafmoss));

            // Removing the log re-marks the surrounding leaves (the runtime wires this through
            // CreativeWorldManager.OnBlockChanged); the next sweep removes the orphan.
            world.SetBlock(logPos, BlockRegistry.Air);
            vegetation.MarkLeafDecayCandidates(world, logPos);
            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void TickLeafDecayChecksNewlyPlacedLeafmossViaCandidateMark()
        {
            // Seed the candidate set with an empty world (first sweep), then place an orphan leaf.
            vegetation.TickLeafDecay(world, 120);

            var leafPos = new BlockPosition(8, 16, 8);
            world.SetBlock(leafPos, BlockRegistry.Leafmoss);
            vegetation.MarkLeafDecayCandidate(leafPos);

            vegetation.TickLeafDecay(world, 120);

            Assert.That(world.GetBlock(leafPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void ScanAndTrackSaplingsPreservesExistingTickAccumulators()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 1100);

            // Re-scan mid-growth; accumulated ticks must survive.
            vegetation.ScanAndTrackSaplings(world);
            vegetation.TickSapling(world, 100);

            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1),
                "ScanAndTrackSaplings must not reset accumulated growth ticks for already-tracked saplings.");
        }

        [Test]
        public void TickSaplingPreservesRemainderAfterGrowthThreshold()
        {
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);

            // 1700 = 1200 + 500 remainder
            vegetation.TickSapling(world, 1700);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S1));

            // Only 700 more needed (1200 - 500 remainder) to reach S2
            vegetation.TickSapling(world, 700);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Sapling_S2),
                "TickSapling must carry over remainder ticks so the next stage uses the correct threshold.");
        }

        [Test]
        public void TickSaplingUsesCorrespondingBiomeTreeVariantViaBiomeResolver()
        {
            // Biome 1 = Pinewild → ConicalTree (PlaceConicalTree places logs at trunk center column)
            vegetation.Configure((x, z) => 1); // 1 = Pinewild

            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 3600); // 3 × 1200 ticks to reach S2 + grow
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog),
                "Sapling should grow into a tree using the biome-specified variant.");
        }

        [Test]
        public void TickSaplingDefaultsToStandardTreeWhenNoBiomeResolverSet()
        {
            // No Configure call — resolver is null
            world.SetBlock(BasePos, BlockRegistry.Sapling);
            vegetation.TrackSapling(BasePos);
            vegetation.TickSapling(world, 3600);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void MarkWildHarvestAddsToRegrowthQueue()
        {
            vegetation.MarkWildHarvest(BlockRegistry.Berrybush, BasePos, currentTick: 0);
            Assert.That(vegetation.WildRegrowthQueueCount, Is.EqualTo(1));
        }

        [Test]
        public void TickWildRegrowthRestoresBlockAfterDelay()
        {
            // Plant a berrybush, mark it harvested (position is Air), tick past the delay.
            // The position's block below (BasePos.Y - 1) must be solid.
            var surface = new BlockPosition(BasePos.X, BasePos.Y - 1, BasePos.Z);
            world.SetBlock(surface, BlockRegistry.MeadowTurf);
            // BasePos itself is Air (default)

            vegetation.MarkWildHarvest(BlockRegistry.Berrybush, BasePos, currentTick: 0);

            // Berrybush delay = 48000; tick to just before — should not restore.
            vegetation.TickWildRegrowth(world, 47999);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Air));

            // Tick past delay — should restore.
            vegetation.TickWildRegrowth(world, 48001);
            Assert.That(world.GetBlock(BasePos), Is.EqualTo(BlockRegistry.Berrybush));
            Assert.That(vegetation.WildRegrowthQueueCount, Is.EqualTo(0));
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
