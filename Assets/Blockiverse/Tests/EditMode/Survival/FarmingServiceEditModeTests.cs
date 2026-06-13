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

        // Guaranteed-outcome growth conditions: AlwaysGrow pushes the stage chance above 1 via the
        // biome modifier so every deterministic roll passes; NeverGrow zeroes the chance.
        static readonly CropGrowthConditions AlwaysGrow = new(lightLevel: 15, soilMoist: true, biomeModifier: 10_000f);
        static readonly CropGrowthConditions NeverGrow = new(lightLevel: 15, soilMoist: true, biomeModifier: 0f);

        VoxelWorld world;
        FarmingService farming;
        long currentTick;

        [SetUp]
        public void SetUp()
        {
            world = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            world.SetBlock(SoilPos, BlockRegistry.LooseLoam, trackChange: false);
            farming = new FarmingService();
            farming.ConfigureDeterministicGrowth(worldSeed: 1234);
            currentTick = 0;
        }

        // Advances the world clock by the given ticks and processes deterministic growth. The
        // first call anchors freshly planted crops at interval 0, so growth begins at the first
        // interval boundary — mirroring the runtime, which ticks crops from the moment they exist.
        void Grow(int ticks) => Grow(ticks, AlwaysGrow);

        void Grow(int ticks, CropGrowthConditions conditions)
        {
            if (currentTick == 0)
                farming.TickGrowth(world, 0L, _ => conditions);
            currentTick += ticks;
            farming.TickGrowth(world, currentTick, _ => conditions);
        }

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

        [Test]
        public void TillWithoutNearbyWaterConsumesCleanWaterFlask()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            // Flasks stack to 1 (§14), so a spare flask occupies its own slot.
            inventory.SetSlot(0, new ItemStack(ItemId.CleanWaterFlask, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.CleanWaterFlask, 1));

            FarmingResult result = farming.Till(world, SoilPos, inventory, hasFreshwaterNearby: (_, _) => false);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.TendedSoil));
            Assert.That(inventory.CountOf(ItemId.CleanWaterFlask), Is.EqualTo(1));
            // The emptied flask comes back (§731 container-return).
            Assert.That(inventory.CountOf(ItemId.WaterFlask), Is.EqualTo(1));
        }

        [Test]
        public void TillWithoutWaterOrFlaskRequiresWater()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);

            FarmingResult result = farming.Till(world, SoilPos, inventory, hasFreshwaterNearby: (_, _) => false);

            Assert.That(result, Is.EqualTo(FarmingResult.RequiresWater));
            Assert.That(world.GetBlock(SoilPos), Is.EqualTo(BlockRegistry.LooseLoam));
        }

        [Test]
        public void TillWithNearbyWaterDoesNotConsumeFlask()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.CleanWaterFlask, 1));

            FarmingResult result = farming.Till(world, SoilPos, inventory, hasFreshwaterNearby: (_, _) => true);

            Assert.That(result, Is.EqualTo(FarmingResult.Success));
            Assert.That(inventory.CountOf(ItemId.CleanWaterFlask), Is.EqualTo(1));
        }

        [Test]
        public void BerrybushRegrowthExportRestoreRoundTripsAccumulatedTicks()
        {
            var harvested = new BlockPosition(3, 3, 3);
            farming.OnBlockHarvested(BlockRegistry.Berrybush_S5, harvested);
            farming.TickRegrowth(world, FarmingService.BerrybushRegrowTicks - 100); // almost due

            var restored = new FarmingService();
            restored.RestoreBerrybushRegrowth(farming.ExportBerrybushRegrowth());
            Assert.That(restored.HasPendingRegrowth(harvested), Is.True);

            // 100 remaining ticks after restore: just before stays pending, at the boundary the
            // bush replants.
            restored.TickRegrowth(world, 99);
            Assert.That(world.GetBlock(harvested), Is.EqualTo(BlockRegistry.Air));
            restored.TickRegrowth(world, 1);
            Assert.That(world.GetBlock(harvested), Is.EqualTo(BlockRegistry.Berrybush));
        }

        [Test]
        public void BerrybushRegrowthQueuesOnlyAfterMatureStageHarvest()
        {
            var immature = new BlockPosition(3, 3, 3);
            var mature = new BlockPosition(4, 3, 3);

            farming.OnBlockHarvested(BlockRegistry.Berrybush_S4, immature);
            farming.OnBlockHarvested(BlockRegistry.Berrybush_S5, mature);

            Assert.That(farming.HasPendingRegrowth(immature), Is.False);
            Assert.That(farming.HasPendingRegrowth(mature), Is.True);
            Assert.That(FarmingService.IsMatureBerrybushStage(BlockRegistry.Berrybush_S5), Is.True);
            Assert.That(FarmingService.IsMatureBerrybushStage(BlockRegistry.Berrybush_S4), Is.False);
        }

        [Test]
        public void HasFreshwaterNearbyFindsWaterWithinTheReachBox()
        {
            // dx=4, dy=+1: the far corner of the §11.1 reach box.
            world.SetBlock(new BlockPosition(6, 3, 2), BlockRegistry.Freshwater, trackChange: false);

            Assert.That(FarmingService.HasFreshwaterNearby(world, SoilPos), Is.True);
        }

        [Test]
        public void HasFreshwaterNearbyIgnoresWaterOutsideTheReachBox()
        {
            // dx=5 and dy=+2 both sit one cell past the reach limits.
            world.SetBlock(new BlockPosition(7, 2, 2), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(2, 4, 2), BlockRegistry.Freshwater, trackChange: false);

            Assert.That(FarmingService.HasFreshwaterNearby(world, SoilPos), Is.False);
        }

        [Test]
        public void HasFreshwaterNearbyIgnoresBrine()
        {
            // Salt water does not irrigate (§5.4/§11.1).
            world.SetBlock(new BlockPosition(3, 2, 2), BlockRegistry.Brine, trackChange: false);

            Assert.That(FarmingService.HasFreshwaterNearby(world, SoilPos), Is.False);
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
            // Grain base chance 0.35; unfavorable is 0.25× = 0.0875. Find a seed whose
            // interval-1 roll for the crop cell lands between the two, so the same roll passes
            // the favorable check but fails the unfavorable one.
            int seed = -1;
            for (int candidate = 0; candidate < 1024; candidate++)
            {
                double roll = DeterministicHash.UnitRoll(
                    candidate, CropPos.X, CropPos.Y, CropPos.Z, BlockRegistry.GrainStalk.Value, 1L);
                if (roll >= 0.0875 && roll < 0.35)
                {
                    seed = candidate;
                    break;
                }
            }
            Assert.That(seed, Is.GreaterThanOrEqualTo(0), "Expected a seed with a discriminating first-interval roll.");

            Assert.That(GrowOneInterval(seed, CropGrowthConditions.Favorable),
                Is.EqualTo(BlockRegistry.GrainStalk_S1), "Favorable conditions should grow at base chance.");
            Assert.That(GrowOneInterval(seed, new CropGrowthConditions(lightLevel: 0, soilMoist: false)),
                Is.EqualTo(BlockRegistry.GrainStalk), "Unfavorable conditions cut growth chance to 25% of base.");
        }

        BlockId GrowOneInterval(int seed, CropGrowthConditions conditions)
        {
            var growthWorld = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            growthWorld.SetBlock(SoilPos, BlockRegistry.LooseLoam, trackChange: false);
            var service = new FarmingService();
            service.ConfigureDeterministicGrowth(seed);
            service.Till(growthWorld, SoilPos);
            service.PlantCrop(growthWorld, SoilPos, BlockRegistry.GrainStalk);

            service.TickGrowth(growthWorld, 0L, _ => conditions); // anchor at interval 0
            service.TickGrowth(growthWorld, FarmingService.GrowthIntervalTicks, _ => conditions);
            return growthWorld.GetBlock(CropPos);
        }

        [Test]
        public void FailedRollsNeverAdvanceCrop()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Grow(FarmingService.GrowthIntervalTicks * 10, NeverGrow);
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

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, new ItemStack(ItemId.ReedwoodSickle, 1));

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop.ItemId, Is.EqualTo(ItemId.GrainBundle));
            Assert.That(result.Drops, Does.Contain(new ItemStack(ItemId.MeadowSeed, 1)));
            Assert.That(inventory.CountOf(ItemId.GrainBundle), Is.InRange(1, 3));
            Assert.That(inventory.CountOf(ItemId.MeadowSeed), Is.EqualTo(1));
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

        [Test]
        public void MatureBerrybushDropsBerriesAndSeed()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Berrybush);
            Grow(FarmingService.GrowthIntervalTicks * 5);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush_S5));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry),
                rngSeed: 12);

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, new ItemStack(ItemId.ReedwoodSickle, 1));

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop.ItemId, Is.EqualTo(ItemId.BerryCluster));
            Assert.That(result.Drops, Does.Contain(new ItemStack(ItemId.BerrySeed, 1)));
            Assert.That(inventory.CountOf(ItemId.BerryCluster), Is.InRange(2, 4));
            Assert.That(inventory.CountOf(ItemId.BerrySeed), Is.EqualTo(1));
        }

        [Test]
        public void MatureReedgrassDropsFiberAndCutting()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.Reedgrass);
            Grow(FarmingService.GrowthIntervalTicks * 3);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Reedgrass_S3));

            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry),
                rngSeed: 9);

            BlockHarvestResult result = service.TryHarvest(world, inventory, CropPos, new ItemStack(ItemId.ReedwoodSickle, 1));

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop.ItemId, Is.EqualTo(ItemId.ReedFiber));
            Assert.That(result.Drops, Does.Contain(new ItemStack(ItemId.ReedCutting, 1)));
            Assert.That(inventory.CountOf(ItemId.ReedFiber), Is.InRange(1, 3));
            Assert.That(inventory.CountOf(ItemId.ReedCutting), Is.EqualTo(1));
        }

        // ── Berrybush regrowth (§3) ──────────────────────────────────────────

        [Test]
        public void HarvestedBerrybushRegrowsAfterTwoGameDays()
        {
            world.SetBlock(CropPos, BlockRegistry.Air, trackChange: false);
            farming.OnBlockHarvested(BlockRegistry.Berrybush_S5, CropPos);
            Assert.That(farming.HasPendingRegrowth(CropPos), Is.True);

            farming.TickRegrowth(world, FarmingService.BerrybushRegrowTicks - 1);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Air), "Berrybush must not regrow before two game days.");

            farming.TickRegrowth(world, 1);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Berrybush), "Berrybush regrows to stage 0 after two game days.");
            Assert.That(farming.HasPendingRegrowth(CropPos), Is.False);
        }

        [Test]
        public void NonBerrybushHarvestDoesNotQueueRegrowth()
        {
            farming.OnBlockHarvested(BlockRegistry.GrainStalk_S4, CropPos);
            Assert.That(farming.HasPendingRegrowth(CropPos), Is.False);
        }

        [Test]
        public void RegrowthIsSkippedWhenPositionIsOccupied()
        {
            farming.OnBlockHarvested(BlockRegistry.Berrybush, CropPos);
            world.SetBlock(CropPos, BlockRegistry.Graystone, trackChange: false);

            farming.TickRegrowth(world, FarmingService.BerrybushRegrowTicks);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(farming.HasPendingRegrowth(CropPos), Is.False);
        }

        // ── Deterministic interval-indexed growth (multiplayer lockstep) ─────

        [Test]
        public void DeterministicGrowthRequiresConfiguration()
        {
            var unconfigured = new FarmingService();
            unconfigured.Till(world, SoilPos);
            unconfigured.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            Assert.Throws<InvalidOperationException>(() => unconfigured.TickGrowth(world, 0L));
        }

        [Test]
        public void DeterministicGrowthDoesNotAdvanceWithinTheAnchorInterval()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);

            farming.TickGrowth(world, 0L); // anchors the crop at interval 0
            farming.TickGrowth(world, FarmingService.GrowthIntervalTicks - 1L);

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                "No interval boundary has been crossed since the crop was anchored.");
        }

        [Test]
        public void DeterministicGrowthIsIndependentOfTickBatching()
        {
            const int seed = 98765;
            const int intervals = 64;

            // Host: ticks growth at every interval boundary. Late joiner: receives the same crop
            // via the chunk snapshot and processes all elapsed intervals in one batch. Both must
            // land on the same stage because every roll is a pure function of
            // (seed, position, stage, interval index).
            BlockId stepped = RunDeterministicGrowth(seed, intervals, stepPerInterval: true);
            BlockId batched = RunDeterministicGrowth(seed, intervals, stepPerInterval: false);

            Assert.That(batched, Is.EqualTo(stepped),
                "Batched catch-up growth must match per-interval host growth exactly.");
            Assert.That(stepped, Is.Not.EqualTo(BlockRegistry.GrainStalk),
                "64 favorable intervals at 35% base chance must advance the crop.");
        }

        [Test]
        public void DeterministicGrowthDivergesAcrossDifferentSeeds()
        {
            // Sanity check that the roll actually keys off the world seed: scanning many seeds,
            // the first-interval outcome must differ between some pair of seeds.
            bool sawGrowth = false, sawNoGrowth = false;
            for (int seed = 0; seed < 64 && !(sawGrowth && sawNoGrowth); seed++)
            {
                BlockId result = RunDeterministicGrowth(seed, intervals: 1, stepPerInterval: true);
                if (result == BlockRegistry.GrainStalk) sawNoGrowth = true;
                else sawGrowth = true;
            }

            Assert.That(sawGrowth && sawNoGrowth, Is.True,
                "First-interval growth outcomes must vary by world seed.");
        }

        BlockId RunDeterministicGrowth(int seed, int intervals, bool stepPerInterval)
        {
            var growthWorld = new VoxelWorld(new WorldBounds(8, 8, 8), chunkSize: 8, seed: 1);
            growthWorld.SetBlock(SoilPos, BlockRegistry.LooseLoam, trackChange: false);
            var service = new FarmingService();
            service.ConfigureDeterministicGrowth(seed);
            service.Till(growthWorld, SoilPos);
            service.PlantCrop(growthWorld, SoilPos, BlockRegistry.GrainStalk);

            service.TickGrowth(growthWorld, 0L); // anchor at interval 0

            if (stepPerInterval)
            {
                for (int i = 1; i <= intervals; i++)
                    service.TickGrowth(growthWorld, (long)i * FarmingService.GrowthIntervalTicks);
            }
            else
            {
                service.TickGrowth(growthWorld, (long)intervals * FarmingService.GrowthIntervalTicks);
            }

            return growthWorld.GetBlock(CropPos);
        }

        // ── Crop tracking bookkeeping ────────────────────────────────────────

        [Test]
        public void ScanAndTrackCropsPreservesGrowthAnchorsForTrackedCrops()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            Grow(FarmingService.GrowthIntervalTicks - 100); // anchored at interval 0

            farming.ScanAndTrackCrops(world);
            Grow(100); // crosses the first interval boundary

            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1),
                "ScanAndTrackCrops must not re-anchor already-tracked crops.");
        }

        [Test]
        public void TrackCropReanchorsAReplacedCropAtTheSamePosition()
        {
            farming.Till(world, SoilPos);
            farming.PlantCrop(world, SoilPos, BlockRegistry.GrainStalk);
            Grow(FarmingService.GrowthIntervalTicks - 100); // anchored at interval 0

            world.SetBlock(CropPos, BlockRegistry.Air, trackChange: false);
            farming.TrackCrop(CropPos);
            world.SetBlock(CropPos, BlockRegistry.GrainStalk, trackChange: false);

            // The boundary at interval 1 is crossed here, but the re-planted crop is re-anchored
            // at that interval, so it must not inherit the old crop's progress.
            Grow(100);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk),
                "TrackCrop must re-anchor so a re-planted crop starts fresh.");

            Grow(FarmingService.GrowthIntervalTicks);
            Assert.That(world.GetBlock(CropPos), Is.EqualTo(BlockRegistry.GrainStalk_S1));
        }
    }
}
