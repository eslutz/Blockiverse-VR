using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class FluidSubmersionEditModeTests
    {
        [Test]
        public void AColumnOfWaterReportsFeetBodyAndHeadSubmerged()
        {
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Freshwater, fromY: 1, toY: 6);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 2, 4),
                body: new BlockPosition(4, 3, 4),
                head: new BlockPosition(4, 4, 4));

            Assert.That(state.InFluid, Is.True);
            Assert.That(state.FeetSubmerged, Is.True);
            Assert.That(state.BodySubmerged, Is.True);
            Assert.That(state.HeadSubmerged, Is.True);
            Assert.That(state.Immersion, Is.EqualTo(FluidImmersion.Head));
            Assert.That(state.Family, Is.EqualTo(FluidFamily.Freshwater));
        }

        [Test]
        public void FeetInWaterWithADryBodyReportsWadingNotSwimming()
        {
            // The one-block shore step and every puddle depend on this: feet alone must not trip
            // the swim state, or walking into ankle-deep water would lock gravity off.
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Freshwater, fromY: 1, toY: 1);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 1, 4),
                body: new BlockPosition(4, 2, 4),
                head: new BlockPosition(4, 3, 4));

            Assert.That(state.InFluid, Is.True);
            Assert.That(state.FeetSubmerged, Is.True);
            Assert.That(state.BodySubmerged, Is.False);
            Assert.That(state.HeadSubmerged, Is.False);
            Assert.That(state.Immersion, Is.EqualTo(FluidImmersion.Feet));
        }

        [Test]
        public void NoFluidAtAnySamplePointReadsAsDry()
        {
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Freshwater, fromY: 1, toY: 2);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(9, 6, 9),
                body: new BlockPosition(9, 7, 9),
                head: new BlockPosition(9, 8, 9));

            Assert.That(state.InFluid, Is.False);
            Assert.That(state.Immersion, Is.EqualTo(FluidImmersion.None));
        }

        [Test]
        public void ANullWorldReadsAsDry()
        {
            // A real state: the title screen, and the window while a world is being swapped. If it
            // read as anything else, a reload while swimming would leave gravity locked off with
            // nothing to unlock it.
            FluidSubmersionState state = FluidSubmersion.Sample(
                null,
                feet: new BlockPosition(0, 0, 0),
                body: new BlockPosition(0, 1, 0),
                head: new BlockPosition(0, 2, 0));

            Assert.That(state.InFluid, Is.False);
            Assert.That(state.Immersion, Is.EqualTo(FluidImmersion.None));
        }

        [Test]
        public void TheSurfaceIsTheHighestContiguousCellOfTheSameFamily()
        {
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Freshwater, fromY: 1, toY: 4);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 1, 4),
                body: new BlockPosition(4, 2, 4),
                head: new BlockPosition(4, 3, 4));

            Assert.That(state.HasSurface, Is.True);
            Assert.That(state.SurfaceCellY, Is.EqualTo(4),
                "Water fills y1..y4 with air above, so the surface cell is y4.");
            Assert.That(FluidSubmersion.SurfaceWorldY(state.SurfaceCellY), Is.EqualTo(5.0f).Within(0.0001f),
                "The water plane is the top face of the surface cell; the render-side wave only ever dips below it.");
        }

        [Test]
        public void TheSurfaceScanStopsAtADifferentFluidFamily()
        {
            // Freshwater lying over emberflow: the water's surface is its own top, not the top of
            // whatever is underneath it.
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Emberflow, fromY: 1, toY: 2);

            for (int y = 3; y <= 5; y++)
                world.SetBlock(new BlockPosition(4, y, 4), FluidBlocks.SourceOf(FluidFamily.Freshwater), trackChange: false);

            bool found = FluidSubmersion.TryFindSurfaceCellY(
                world,
                new BlockPosition(4, 1, 4),
                FluidFamily.Emberflow,
                FluidSubmersion.DefaultSurfaceScanCells,
                out int surfaceCellY);

            Assert.That(found, Is.True);
            Assert.That(surfaceCellY, Is.EqualTo(2),
                "The emberflow column ends at y2; the freshwater above it belongs to a different surface.");
        }

        [Test]
        public void TheDeepestSampleDecidesTheFamily()
        {
            // Standing in emberflow with freshwater at chest height: the fluid you are standing in
            // is the one that decides how you move and what it does to you.
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Emberflow, fromY: 1, toY: 1);

            for (int y = 2; y <= 4; y++)
                world.SetBlock(new BlockPosition(4, y, 4), FluidBlocks.SourceOf(FluidFamily.Freshwater), trackChange: false);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 1, 4),
                body: new BlockPosition(4, 2, 4),
                head: new BlockPosition(4, 3, 4));

            Assert.That(state.Family, Is.EqualTo(FluidFamily.Emberflow));
        }

        [Test]
        public void FlowingCellsCountAsFluidForTheirFamily()
        {
            // A stream is flow cells, not source cells. If they did not count, a player could stand
            // in a waterfall and the game would think they were dry.
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 3);

            for (int y = 1; y <= 4; y++)
                world.SetBlock(new BlockPosition(4, y, 4), FluidBlocks.FlowOf(FluidFamily.Brine), trackChange: false);

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 1, 4),
                body: new BlockPosition(4, 2, 4),
                head: new BlockPosition(4, 3, 4));

            Assert.That(state.InFluid, Is.True);
            Assert.That(state.Family, Is.EqualTo(FluidFamily.Brine));
            Assert.That(state.SurfaceCellY, Is.EqualTo(4));
        }

        [Test]
        public void OutOfBoundsSamplesAreDryRatherThanAnError()
        {
            // The head cell is above the world ceiling on any ordinary frame near the build limit.
            VoxelWorld world = CreateWorldWithFluidColumn(FluidFamily.Freshwater, fromY: 1, toY: 2);

            Assert.DoesNotThrow(() => FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(4, 1, 4),
                body: new BlockPosition(4, 2, 4),
                head: new BlockPosition(4, 999, 4)));

            FluidSubmersionState state = FluidSubmersion.Sample(
                world,
                feet: new BlockPosition(-5, 1, 4),
                body: new BlockPosition(-5, 2, 4),
                head: new BlockPosition(-5, 3, 4));

            Assert.That(state.InFluid, Is.False);
        }

        [Test]
        public void TheSurfaceScanIsBoundedByItsCellBudget()
        {
            // A deep ocean column must not be walked to the top every frame.
            var world = new VoxelWorld(new WorldBounds(16, 32, 16), chunkSize: 16, seed: 4);

            for (int y = 1; y <= 20; y++)
                world.SetBlock(new BlockPosition(4, y, 4), FluidBlocks.SourceOf(FluidFamily.Freshwater), trackChange: false);

            FluidSubmersion.TryFindSurfaceCellY(
                world,
                new BlockPosition(4, 1, 4),
                FluidFamily.Freshwater,
                maxScanCells: 3,
                out int surfaceCellY);

            Assert.That(surfaceCellY, Is.EqualTo(4),
                "Three cells above the start is as far as the scan may look when it is given a budget of three.");
        }

        static VoxelWorld CreateWorldWithFluidColumn(FluidFamily family, int fromY, int toY)
        {
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 2);

            for (int y = fromY; y <= toY; y++)
                world.SetBlock(new BlockPosition(4, y, 4), FluidBlocks.SourceOf(family), trackChange: false);

            return world;
        }
    }
}
