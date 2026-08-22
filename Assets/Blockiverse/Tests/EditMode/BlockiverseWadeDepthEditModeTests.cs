using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    /// <summary>
    /// Wading resolved from water DEPTH rather than from where a capsule fraction lands.
    ///
    /// This is the layer that had no coverage. `ResolveState(bool, bool, bool)` was correct and
    /// tested, but nothing exercised the sampling that feeds it, and the sampling never produced
    /// the Wading input for one-block water: the body sample sits at 0.55 x capsule height, which
    /// is 0.99 m at the default 1.8 m capsule and 0.50 m crouched — both inside a one-block water
    /// cell. So every player waded only if their capsule exceeded ~1.82 m, and everyone else swam
    /// in ankle-deep water with gravity locked off. Right function, wrong inputs, green suite.
    /// </summary>
    public sealed class BlockiverseWadeDepthEditModeTests
    {
        const int GroundY = 8;

        // Builds a column: solid up to GroundY, then `waterCells` of freshwater on top.
        static VoxelWorld WorldWithWaterColumn(int waterCells)
        {
            var world = new VoxelWorld(new WorldBounds(16, 32, 16), chunkSize: 16, seed: 1);

            for (int y = 0; y <= GroundY; y++)
                world.SetBlock(new BlockPosition(8, y, 8), BlockRegistry.Graystone);

            for (int i = 0; i < waterCells; i++)
                world.SetBlock(new BlockPosition(8, GroundY + 1 + i, 8), BlockRegistry.Freshwater);

            return world;
        }

        // Samples as the provider does: feet just above the capsule base, body at a fraction of
        // capsule height, head at the top. Heights in metres; cells are 1 m.
        static SwimState ResolveForPlayer(int waterCells, float capsuleHeight)
        {
            VoxelWorld world = WorldWithWaterColumn(waterCells);
            int feetCellY = GroundY + 1;

            var feet = new BlockPosition(8, feetCellY, 8);
            var body = new BlockPosition(8, feetCellY + CellOffset(capsuleHeight * 0.55f), 8);
            var head = new BlockPosition(8, feetCellY + CellOffset(capsuleHeight), 8);

            FluidSubmersionState submersion = FluidSubmersion.Sample(world, feet, body, head);
            return BlockiverseSwimMotion.ResolveState(submersion, feetCellY);
        }

        // Cell offset for a height above the capsule base, matching ToBlockPosition's floor.
        static int CellOffset(float metres) => (int)System.Math.Floor(metres);

        [TestCase(1.80f, TestName = "standing, default capsule")]
        [TestCase(0.90f, TestName = "crouched, default capsule")]
        [TestCase(1.20f, TestName = "short real-height player")]
        [TestCase(2.10f, TestName = "tall real-height player")]
        [TestCase(0.60f, TestName = "crouched short real-height player")]
        public void OneBlockOfWaterIsWadingForEveryHeight(float capsuleHeight)
        {
            // The whole point of the depth rule: how deep the water is cannot depend on how tall
            // the player is. Before it, only capsules above ~1.82 m waded here.
            Assert.That(ResolveForPlayer(waterCells: 1, capsuleHeight),
                Is.EqualTo(SwimState.Wading),
                $"a {capsuleHeight:0.00} m player in one block of water is standing on the bottom");
        }

        [TestCase(1.80f)]
        [TestCase(0.90f)]
        [TestCase(1.20f)]
        public void TwoBlocksOfWaterIsNotWading(float capsuleHeight)
        {
            // Two blocks is over the head of most of these, and out of standing depth for all of
            // them: the swim provider should own vertical motion.
            Assert.That(ResolveForPlayer(waterCells: 2, capsuleHeight),
                Is.Not.EqualTo(SwimState.Wading));
            Assert.That(ResolveForPlayer(waterCells: 2, capsuleHeight),
                Is.Not.EqualTo(SwimState.Dry));
        }

        [Test]
        public void DryGroundIsDry()
        {
            Assert.That(ResolveForPlayer(waterCells: 0, capsuleHeight: 1.8f), Is.EqualTo(SwimState.Dry));
        }

        [Test]
        public void WadingKeepsGravityOn()
        {
            // The consequence that made this a gameplay bug rather than a cosmetic one: Surfaced
            // pauses gravity, so misreading a puddle let the player hold jump and float out of it.
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Wading), Is.False);
            Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(SwimState.Surfaced), Is.True);
        }

        [Test]
        public void DeepWaterStillSwimsWhenTheHeadGoesUnder()
        {
            Assert.That(ResolveForPlayer(waterCells: 4, capsuleHeight: 1.8f),
                Is.EqualTo(SwimState.Swimming));
        }

        [Test]
        public void OutOfFluidResolvesDryWhateverTheFeetCell()
        {
            FluidSubmersionState none = default;
            Assert.That(BlockiverseSwimMotion.ResolveState(none, feetCellY: GroundY + 1),
                Is.EqualTo(SwimState.Dry));
        }
    }
}
