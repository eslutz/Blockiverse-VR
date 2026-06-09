using System;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class FarmingServiceEditModeTests
    {
        static readonly BlockPosition SoilPos = new(2, 2, 2);
        static readonly BlockPosition CropPos = new(2, 3, 2);

        // Deterministic growth rolls: AlwaysGrow always passes the chance check; NeverGrow never does.
        static readonly Func<double> AlwaysGrow = () => 0.0;
        static readonly Func<double> NeverGrow = () => 1.0;

        VoxelWorld world;
        FarmingService farming;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            world.SetBlock(SoilPos, BlockRegistry.LooseLoam, trackChange: false);
            farming = new FarmingService();
        }

        void Grow(int ticks) => farming.TickGrowth(world, ticks, conditions: null, random: AlwaysGrow);

        // ── Tilling ──────────────────────────────────────────────────────────

        [Test]
        public void TillConvertsLooseLoamToTendedSoil()
        {
            Assert.That(farming.Till(world, SoilPos), Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
        }

        [Test]
        public void TillRejectsNonTillableBlock()
        {
            world.SetBlock(SoilPos, BlockRegistry.Graystone, trackChange: false);
            Assert.That(farming.Till(world, SoilPos), Is.EqualTo(FarmingResult.NotTillableBlock));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.Graystone));
        }

        [Test]
        public void TillConvertsRootsoilAndRiverSilt()
        {
            world.SetBlock(SoilPos, BlockRegistry.Rootsoil, trackChange: false);
            Assert.That(farming.Till(world, SoilPos), Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));

            world.SetBlock(SoilPos, BlockRegistry.RiverSilt, trackChange: false);
            Assert.That(farming.Till(world, SoilPos), Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
        }

        // ── Planting ─────────────────────────────────────────────────────────

        [Test]
        public void PlantCropPlacesCropAboveTendedSoil()
        {
            farming.Till(world, SoilPos);
            Assert.That(farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk), Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk));
        }

        [Test]
        public void PlantCropRejectsNonTendedSoilBelow()
        {
            Assert.That(farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk), Is.EqualTo(FarmingResult.NotTendedSoil));
        }

        [Test]
        public void PlantCropRejectsBlockedSpaceAbove()
        {
            farming.Till(world, SoilPos);
            world.SetBlock(CropPos, BlockRegistry.Graystone, trackChange: false);
            Assert.That(farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk), Is.EqualTo(FarmingResult.BlockAboveNotAir));
        }

        [Test]
        public void PlantCropRejectsUnknownCropKind()
        {
            farming.Till(world, SoilPos);
            Assert.That(farming.PlantCrop(world, SoilPos, BlockRegistry.Graystone), Is.EqualTo(FarmingResult.UnknownCrop));
        }

        [Test]
        public void PlantSeedPlantsCropForSeedItem()
        {
            farming.Till(world, SoilPos);
            Assert.That(farming.PlantSeed(world, SoilPos, ItemId.BerrySeed), Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush));
            Assert.That(FarmingService.IsSeedItem(ItemId.MeadowSeed), Is.True);
            Assert.That(FarmingService.IsSeedItem(ItemId.Embercoal), Is.False);
        }

        // ── Growth chains (deterministic via AlwaysGrow) ─────────────────────

        [Test]
        public void GrowthAdvancesOneStagePerInterval()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Grow(FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1));
        }

        [Test]
        public void GrowthDoesNotAdvanceBeforeIntervalCompletes()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Grow(FarmingService.GrowthIntervalTicks - 1);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk));
        }

        [Test]
        public void GrainReachesFullFiveStages()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Grow(FarmingService.GrowthIntervalTicks * 4);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S4));

            // Mature grain does not advance further.
            Grow(FarmingService.GrowthIntervalTicks * 5);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S4));
        }

        [Test]
        public void BerryReachesFullSixStages()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Berrybush);

            Grow(FarmingService.GrowthIntervalTicks * 5);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S5));
        }

        [Test]
        public void ReedReachesFullFourStages()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Reedgrass);

            Grow(FarmingService.GrowthIntervalTicks * 3);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Reedgrass_S3));

            Grow(FarmingService.GrowthIntervalTicks * 5);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Reedgrass_S3));
        }

        // ── Probabilistic light/moisture gating (§11.2) ──────────────────────

        [Test]
        public void FavorableConditionsGrowWhereUnfavorableConditionsDoNot()
        {
            // Grain base chance 0.35; unfavorable is 0.25× = 0.0875. A roll of 0.2 passes the
            // favorable check but not the unfavorable one.
            Func<double> roll = () => 0.2;

            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks,
                conditions: _ => CropGrowthConditions.Favorable, random: roll);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1), "Favorable conditions should grow at base chance.");

            // Reset to a fresh crop and apply unfavorable conditions (dark, dry).
            world.SetBlock(CropPos, BlockRegistry.Air, trackChange: false);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks,
                conditions: _ => new CropGrowthConditions(lightLevel: 0, soilMoist: false), random: roll);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk), "Unfavorable conditions cut growth chance to 25% of base.");
        }

        [Test]
        public void FailedRollsNeverAdvanceCrop()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks * 10, conditions: null, random: NeverGrow);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk));
        }

        // ── Harvest drops ────────────────────────────────────────────────────

        [Test]
        public void MatureGrainDropsCanonicalGrainBundle()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            Grow(FarmingService.GrowthIntervalTicks * 4);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S4));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(BlockRegistry.CreateDefault(), itemRegistry, BlockHarvestRuleSet.CreateDefault(itemRegistry));

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.GrainBundle), Is.EqualTo(1));
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void IntermediateBerryStageDropsCanonicalBerryCluster()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Berrybush);
            Grow(FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S1));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(BlockRegistry.CreateDefault(), itemRegistry, BlockHarvestRuleSet.CreateDefault(itemRegistry));

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.BerryCluster), Is.EqualTo(1));
        }

        // ── Tick accumulator bookkeeping ─────────────────────────────────────

        [Test]
        public void ScanAndTrackCropsPreservesExistingTickAccumulators()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            Grow(FarmingService.GrowthIntervalTicks - 100);

            farming.ScanAndTrackCrops(world);
            Grow(100);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1),
                "ScanAndTrackCrops must not reset accumulated growth ticks for already-tracked crops.");
        }

        [Test]
        public void TrackCropResetsAccumulatorWhenCropIsReplacedAtSamePosition()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            Grow(FarmingService.GrowthIntervalTicks - 100);

            world.SetBlock(CropPos, BlockRegistry.Air, trackChange: false);
            farming.TrackCrop(CropPos);
            world.SetBlock(CropPos, BlockRegistry.GrainStalk, trackChange: false);

            Grow(100);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                "TrackCrop must reset ticks to 0 so a re-planted crop starts fresh.");

            Grow(FarmingService.GrowthIntervalTicks - 100);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1));
        }
    }
}
