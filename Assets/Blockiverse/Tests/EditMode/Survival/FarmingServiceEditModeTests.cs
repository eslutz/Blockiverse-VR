using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class FarmingServiceEditModeTests
    {
        static readonly BlockPosition SoilPos = new(2, 2, 2);
        static readonly BlockPosition CropPos  = new(2, 3, 2);

        VoxelWorld world;
        FarmingService farming;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            world.SetBlock(SoilPos, BlockRegistry.LooseLoam, trackChange: false);
            farming = new FarmingService();
        }

        [Test]
        public void TillConvertsLooseLoamToTendedSoil()
        {
            FarmingResult result = farming.Till(world, SoilPos);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
        }

        [Test]
        public void TillRejectsNonLooseLoam()
        {
            world.SetBlock(SoilPos, BlockRegistry.Graystone, trackChange: false);

            FarmingResult result = farming.Till(world, SoilPos);

            Assert.That(result, Is.EqualTo(FarmingResult.NotTillableBlock));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.Graystone));
        }

        [Test]
        public void PlantCropPlacesCropAboveTendedSoil()
        {
            farming.Till(world, SoilPos);

            FarmingResult result = farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk));
        }

        [Test]
        public void PlantCropRejectsNonTendedSoilBelow()
        {
            FarmingResult result = farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Assert.That(result, Is.EqualTo(FarmingResult.NotTendedSoil));
        }

        [Test]
        public void PlantCropRejectsBlockedSpaceAbove()
        {
            farming.Till(world, SoilPos);
            world.SetBlock(CropPos, BlockRegistry.Graystone, trackChange: false);

            FarmingResult result = farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Assert.That(result, Is.EqualTo(FarmingResult.BlockAboveNotAir));
        }

        [Test]
        public void PlantCropRejectsUnknownCropKind()
        {
            farming.Till(world, SoilPos);

            FarmingResult result = farming.PlantCrop(world, SoilPos, BlockRegistry.Graystone);

            Assert.That(result, Is.EqualTo(FarmingResult.UnknownCrop));
        }

        [Test]
        public void TickGrowthAdvancesCropAfterGrowthInterval()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1));
        }

        [Test]
        public void TickGrowthDoesNotAdvanceBeforeIntervalCompletes()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks - 1);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk));
        }

        [Test]
        public void TickGrowthAdvancesBerrybushThroughAllStages()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Berrybush);

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S1));

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S2));
        }

        [Test]
        public void TickGrowthStopsTrackingFullyGrownCrop()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Reedgrass);

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Reedgrass_S1));

            // Additional ticks should not change the fully-grown Reedgrass
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks * 5);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Reedgrass_S1));
        }

        [Test]
        public void FullyGrownCropStageIsHarvestableAndDropsBaseItem()
        {
            // Grow GrainStalk through S1 → S2 (fully grown)
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S2));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.GrainStalk), Is.EqualTo(1));
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void IntermediateCropStageIsAlsoHarvestable()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Berrybush);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S1));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.Berrybush), Is.EqualTo(1));
        }

        [Test]
        public void TillConvertsRootsoilToTendedSoil()
        {
            world.SetBlock(SoilPos, BlockRegistry.Rootsoil, trackChange: false);

            FarmingResult result = farming.Till(world, SoilPos);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
        }

        [Test]
        public void TillConvertsRiverSiltToTendedSoil()
        {
            world.SetBlock(SoilPos, BlockRegistry.RiverSilt, trackChange: false);

            FarmingResult result = farming.Till(world, SoilPos);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
        }

        [Test]
        public void ScanAndTrackCropsPreservesExistingTickAccumulators()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks - 100);

            // Re-scan mid-growth; accumulated ticks must survive.
            farming.ScanAndTrackCrops(world);
            farming.TickGrowth(world, 100);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1),
                "ScanAndTrackCrops must not reset accumulated growth ticks for already-tracked crops.");
        }

        [Test]
        public void TrackCropResetsAccumulatorWhenCropIsReplacedAtSamePosition()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            // Simulate partial growth accumulation by ticking almost to threshold.
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks - 100);

            // Player breaks and immediately replants at same position.
            world.SetBlock(CropPos, BlockRegistry.Air, trackChange: false);
            farming.TrackCrop(CropPos);
            world.SetBlock(CropPos, BlockRegistry.GrainStalk, trackChange: false);

            // The new crop should need a full interval, not just 100 more ticks.
            farming.TickGrowth(world, 100);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                "TrackCrop must reset ticks to 0 so a re-planted crop starts fresh.");

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks - 100);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1));
        }
    }
}
