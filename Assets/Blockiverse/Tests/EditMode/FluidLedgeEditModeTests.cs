using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Pins the shore-climb query. The failure this exists to prevent is a swimmer reaching a bank
    // and being pulled straight back in, because the swim state ends about a metre before their
    // feet clear the waterline and gravity resumes there.
    public sealed class FluidLedgeEditModeTests
    {
        const int GroundY = 3;

        static (VoxelWorld world, BlockRegistry registry) CreatePool()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 24, 16), chunkSize: 16, seed: 1);

            // Stone floor, water above it up to y = 6, open air over the lot.
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int y = 0; y <= GroundY; y++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Graystone, trackChange: false);

                    for (int y = GroundY + 1; y <= 6; y++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Freshwater, trackChange: false);
                }
            }

            return (world, registry);
        }

        [Test]
        public void OpenWaterOffersNoLedge()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out _),
                Is.False,
                "Mid-lake there is nothing to climb onto -- water is not solid ground.");
        }

        [Test]
        public void ABankLevelWithTheWaterIsClimbable()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            // Raise one column to the water's own surface height.
            for (int y = GroundY + 1; y <= 6; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out BlockPosition landing),
                Is.True);
            Assert.That(landing, Is.EqualTo(new BlockPosition(9, 7, 8)),
                "The player should land on top of the bank, not inside it.");
        }

        [Test]
        public void ABankOneBlockAboveTheWaterIsAlsoClimbable()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            for (int y = GroundY + 1; y <= 7; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out BlockPosition landing),
                Is.True);
            Assert.That(landing, Is.EqualTo(new BlockPosition(9, 8, 8)));
        }

        [Test]
        public void ACliffTooHighToClimbIsRejected()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            // Three above the swimmer's feet -- past the reach, so it stays a wall.
            for (int y = GroundY + 1; y <= 9; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out _),
                Is.False,
                "A high bank must stay a wall; a big automatic lift is a significant vection event.");
        }

        [Test]
        public void ALedgeWithoutHeadroomIsRejected()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            for (int y = GroundY + 1; y <= 6; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            // An overhang one cell above the landing: the two-block capsule would be wedged.
            world.SetBlock(new BlockPosition(9, 8, 8), BlockRegistry.Graystone, trackChange: false);

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out _),
                Is.False);
        }

        [Test]
        public void TheLowestClimbableSurfaceWins()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            // A step at water level with a taller block behind it. The player should be lifted the
            // smallest distance that gets them out, not the largest available.
            for (int y = GroundY + 1; y <= 6; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 1, 0, out BlockPosition landing),
                Is.True);
            Assert.That(landing.Y, Is.EqualTo(7));
        }

        [Test]
        public void NoDirectionMeansNoClimb()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            for (int y = GroundY + 1; y <= 6; y++)
                world.SetBlock(new BlockPosition(9, y, 8), BlockRegistry.Graystone, trackChange: false);

            // The assist only ever fires while the player is actively pushing toward the bank.
            // That is what makes it redirected REQUESTED motion rather than motion nobody asked
            // for -- the distinction the comfort argument rests on.
            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(8, 6, 8), 0, 0, out _),
                Is.False);
        }

        [Test]
        public void OutOfBoundsColumnsAreSafe()
        {
            (VoxelWorld world, BlockRegistry registry) = CreatePool();

            Assert.That(
                FluidLedge.TryResolveClimbOut(world, registry, new BlockPosition(15, 6, 8), 1, 0, out _),
                Is.False,
                "Looking past the world edge must not throw or report a phantom ledge.");
        }
    }
}
