using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class StationProximityEditModeTests
    {
        static readonly BlockPosition Center = new(8, 8, 8);

        VoxelWorld world;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 1);
        }

        [Test]
        public void ScanNearbyFindsStationsWithinRadius()
        {
            world.SetBlock(new BlockPosition(8, 8, 10), BlockRegistry.ClayKiln, trackChange: false);
            world.SetBlock(new BlockPosition(4, 8, 8), BlockRegistry.BuildTable, trackChange: false);

            CraftingStationSet stations = StationProximity.ScanNearby(world, Center);

            Assert.That(stations.Contains(CraftingStation.ClayKiln), Is.True);
            Assert.That(stations.Contains(CraftingStation.BuildTable), Is.True);
            Assert.That(stations.Contains(CraftingStation.BellowsForge), Is.False);
        }

        [Test]
        public void ScanNearbyIgnoresStationsBeyondRadius()
        {
            world.SetBlock(new BlockPosition(8, 8, 13), BlockRegistry.ClayKiln, trackChange: false);

            CraftingStationSet stations = StationProximity.ScanNearby(world, Center);

            Assert.That(stations.Contains(CraftingStation.ClayKiln), Is.False);
            Assert.That(stations.IsEmpty, Is.True);
        }

        [Test]
        public void ScanNearbyClampsToWorldBounds()
        {
            world.SetBlock(new BlockPosition(0, 0, 0), BlockRegistry.MendBench, trackChange: false);

            CraftingStationSet stations = StationProximity.ScanNearby(world, new BlockPosition(1, 1, 1));

            Assert.That(stations.Contains(CraftingStation.MendBench), Is.True);
        }

        [Test]
        public void ContainsNoneIsAlwaysTrue()
        {
            Assert.That(CraftingStationSet.None.Contains(CraftingStation.None), Is.True);
            Assert.That(CraftingStationSet.Of(CraftingStation.ClayKiln).Contains(CraftingStation.None), Is.True);
        }

        [Test]
        public void StationSetEqualityComparesMembership()
        {
            CraftingStationSet a = CraftingStationSet.Of(CraftingStation.ClayKiln).With(CraftingStation.Campfire);
            CraftingStationSet b = CraftingStationSet.Of(CraftingStation.Campfire).With(CraftingStation.ClayKiln);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals(CraftingStationSet.None), Is.False);
        }

        [Test]
        public void ValidateClaimAcceptsNoneAndPresentStationsOnly()
        {
            world.SetBlock(new BlockPosition(8, 8, 10), BlockRegistry.BellowsForge, trackChange: false);

            Assert.That(StationProximity.ValidateClaim(world, Center, CraftingStation.None), Is.True,
                "Stationless recipes never require proximity.");
            Assert.That(StationProximity.ValidateClaim(world, Center, CraftingStation.BellowsForge), Is.True);
            Assert.That(StationProximity.ValidateClaim(world, Center, CraftingStation.ClayKiln), Is.False,
                "A claim for a station that is not nearby must be rejected.");
        }

        [Test]
        public void TryGetStationForBlockMapsEveryStationBlock()
        {
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.BuildTable, out CraftingStation buildTable), Is.True);
            Assert.That(buildTable, Is.EqualTo(CraftingStation.BuildTable));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.ClayKiln, out CraftingStation kiln), Is.True);
            Assert.That(kiln, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.BellowsForge, out CraftingStation forge), Is.True);
            Assert.That(forge, Is.EqualTo(CraftingStation.BellowsForge));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.PrepBoard, out CraftingStation prep), Is.True);
            Assert.That(prep, Is.EqualTo(CraftingStation.PrepBoard));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.MendBench, out CraftingStation mend), Is.True);
            Assert.That(mend, Is.EqualTo(CraftingStation.MendBench));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.Campfire, out CraftingStation campfire), Is.True);
            Assert.That(campfire, Is.EqualTo(CraftingStation.Campfire));
            Assert.That(StationProximity.TryGetStationForBlock(BlockRegistry.Graystone, out _), Is.False);
        }
    }
}
