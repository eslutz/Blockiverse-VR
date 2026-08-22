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

        // The provider's real sample geometry: feet at capsule base + 0.10 m, body at base + 0.55 x
        // height, head at the head transform. For a 1.8 m capsule the feet and body samples are
        // 0.89 m apart, so they land in DIFFERENT cells for all but about one percent of vertical
        // positions. Modelling them as integer offsets from the feet cell — as this file's other
        // helper does — collapses that separation to zero and quietly tests only that one percent.
        const float FeetSampleHeightMeters = 0.10f;
        const float BodySampleCapsuleFraction = 0.55f;

        static SwimState ResolveAtCapsuleBase(int waterCells, float capsuleHeight, float baseWorldY)
        {
            VoxelWorld world = WorldWithWaterColumn(waterCells);

            var feet = new BlockPosition(8, FloorToCell(baseWorldY + FeetSampleHeightMeters), 8);
            var body = new BlockPosition(8, FloorToCell(baseWorldY + capsuleHeight * BodySampleCapsuleFraction), 8);
            var head = new BlockPosition(8, FloorToCell(baseWorldY + capsuleHeight), 8);

            FluidSubmersionState submersion = FluidSubmersion.Sample(world, feet, body, head);
            return BlockiverseSwimMotion.ResolveState(submersion, feet.Y);
        }

        static int FloorToCell(float worldY) => (int)System.Math.Floor(worldY);

        // Sweeps the capsule up through the surface cell of a deep column in 0.05 m steps. Every
        // one of these positions has the feet in water with more water beneath them, so none of
        // them is standing on anything and none may hand vertical motion back to gravity.
        [TestCase(4, 1.80f, TestName = "deep water, default capsule")]
        [TestCase(4, 0.90f, TestName = "deep water, crouched")]
        [TestCase(8, 1.80f, TestName = "very deep water, default capsule")]
        [TestCase(3, 1.20f, TestName = "deep water, short real-height player")]
        [TestCase(6, 2.10f, TestName = "deep water, tall real-height player")]
        public void TreadingAnywhereInTheSurfaceCellOfDeepWaterIsNeverWading(int waterCells, float capsuleHeight)
        {
            int surfaceCellY = GroundY + waterCells;

            // Only positions where the feet sample is genuinely still under the water line. The
            // feet sit 0.10 m above the capsule base and the line is at surfaceCellY + 1, so the
            // feet leave the water once the base reaches surfaceCellY + 0.90; beyond that the
            // player is out and Dry is the right answer, not a regression. Stepped by integers
            // rather than accumulating a float, so the bound cannot drift past it.
            for (int step = -1; step <= 17; step++)
            {
                float offset = step * 0.05f;
                float baseWorldY = surfaceCellY + offset;
                SwimState state = ResolveAtCapsuleBase(waterCells, capsuleHeight, baseWorldY);

                Assert.That(state, Is.Not.EqualTo(SwimState.Wading),
                    $"capsule base {baseWorldY:0.00} ({capsuleHeight:0.00} m player, {waterCells} blocks of water) "
                    + "is afloat over more water, so gravity must stay with the swim provider");
                Assert.That(BlockiverseSwimMotion.OwnsVerticalMotion(state), Is.True,
                    $"capsule base {baseWorldY:0.00} released vertical motion mid-swim");
            }
        }

        static SwimState ResolveForFloaterAtSurface(int waterCells, float capsuleHeight)
        {
            int surfaceCellY = GroundY + waterCells;
            return ResolveAtCapsuleBase(waterCells, capsuleHeight, surfaceCellY);
        }

        [TestCase(4, 1.80f, TestName = "deep water, default capsule")]
        [TestCase(4, 0.90f, TestName = "deep water, crouched")]
        [TestCase(8, 1.80f, TestName = "very deep water, default capsule")]
        [TestCase(3, 1.20f, TestName = "deep water, short real-height player")]
        public void RisingToTheSurfaceOfDeepWaterIsNotWading(int waterCells, float capsuleHeight)
        {
            // Feet in the topmost fluid cell puts the surface cell AT the feet cell, exactly as
            // standing in one block of water does. Reading only the surface therefore called this
            // Wading, which hands vertical motion back to gravity mid-swim: the player sinks,
            // re-enters Surfaced, is buoyed back up, and oscillates at the water line. What
            // separates the two is the cell BELOW the feet — ground in a puddle, more water here.
            Assert.That(ResolveForFloaterAtSurface(waterCells, capsuleHeight),
                Is.Not.EqualTo(SwimState.Wading),
                $"a {capsuleHeight:0.00} m player at the top of {waterCells} blocks of water is afloat, not standing");
        }

        [Test]
        public void TheCellBelowTheFeetIsWhatDistinguishesAPuddleFromASurface()
        {
            // Pinning the discriminator itself, so a future change cannot go back to inferring
            // depth from the surface cell and still pass everything above.
            VoxelWorld puddle = WorldWithWaterColumn(1);
            FluidSubmersionState standing = FluidSubmersion.Sample(
                puddle,
                new BlockPosition(8, GroundY + 1, 8),
                new BlockPosition(8, GroundY + 1, 8),
                new BlockPosition(8, GroundY + 2, 8));

            VoxelWorld deep = WorldWithWaterColumn(4);
            FluidSubmersionState afloat = FluidSubmersion.Sample(
                deep,
                new BlockPosition(8, GroundY + 4, 8),
                new BlockPosition(8, GroundY + 4, 8),
                new BlockPosition(8, GroundY + 5, 8));

            Assert.That(standing.SurfaceCellY, Is.EqualTo(afloat.SurfaceCellY - 3),
                "sanity: these are different columns");
            Assert.That(standing.FluidBelowFeet, Is.False, "a puddle has ground under it");
            Assert.That(afloat.FluidBelowFeet, Is.True, "the top of a deep column has more water under it");
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
