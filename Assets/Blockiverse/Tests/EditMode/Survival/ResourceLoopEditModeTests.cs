using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class ResourceLoopEditModeTests
    {
        [Test]
        public void HarvestingBlockAddsConfiguredDropAndClearsWorldBlock()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop, Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
            Assert.That(result.WorkRequired, Is.GreaterThan(0));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(1));
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void HarvestingDoesNotRemoveBlockWhenInventoryCannotAcceptDrop()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, ItemRegistry.BlockStackSize));
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BlockHarvestFailureReason.InventoryFull));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.Zero);
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void WorldrootHasNoSurvivalDropOrHarvestRule()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            Assert.That(itemRegistry.TryGet(ItemId.Worldroot, out _), Is.False);
            Assert.That(itemRegistry.TryGetItemForBlock(BlockRegistry.Worldroot, out _), Is.False);

            BlockRegistry blockRegistry = BlockRegistry.CreateDefault();
            BlockDefinition worldroot = blockRegistry.Get(BlockRegistry.Worldroot);
            Assert.That(float.IsPositiveInfinity(worldroot.Hardness), Is.True);

            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.Worldroot);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BlockHarvestFailureReason.NoHarvestRule));
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Worldroot));
        }

        [Test]
        public void MatchingToolsReduceMiningTimeComparedWithHand()
        {
            BlockHarvestRuleSet rules = BlockHarvestRuleSet.CreateDefault(ItemRegistry.CreateDefault());

            BlockHarvestRule log        = rules.Get(BlockRegistry.BranchwoodLog);
            BlockHarvestRule stripped   = rules.Get(BlockRegistry.SmoothBranchwood);
            BlockHarvestRule rosycopper = rules.Get(BlockRegistry.RosycopperBloom);
            BlockHarvestRule buildTable = rules.Get(BlockRegistry.BuildTable);
            BlockHarvestRule locker     = rules.Get(BlockRegistry.DeepLocker);

            // Canonical mining formula (§6.1): correct tool mines faster than bare hands,
            // wrong tool class mines slower than the correct tool, and meeting the tier
            // requirement mines faster than being below tier.
            Assert.That(log.HandMineTicks, Is.GreaterThan(0));
            Assert.That(log.GetMineTicks(HarvestToolKind.Feller, toolTier: 1), Is.LessThan(log.HandMineTicks));
            Assert.That(log.GetMineTicks(HarvestToolKind.Delver, toolTier: 1),
                Is.GreaterThan(log.GetMineTicks(HarvestToolKind.Feller, toolTier: 1)));
            Assert.That(stripped.GetMineTicks(HarvestToolKind.Feller, toolTier: 1), Is.LessThan(stripped.HandMineTicks));
            Assert.That(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 2),
                Is.LessThan(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 1)));
            Assert.That(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 2), Is.LessThan(rosycopper.HandMineTicks));
            Assert.That(buildTable.GetMineTicks(HarvestToolKind.Mallet, toolTier: 1), Is.LessThan(buildTable.HandMineTicks));
            Assert.That(locker.GetMineTicks(HarvestToolKind.Mallet, toolTier: 5), Is.LessThan(locker.HandMineTicks));
        }

        [Test]
        public void HarvestWithTrackedDurabilityToolConsumesDurability()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            ItemStack feller = new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(5);
            inventory.SetSlot(0, feller);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, feller, equippedSlotIndex: 0);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(inventory.GetSlot(0).Durability, Is.EqualTo(4));
        }

        [Test]
        public void HarvestBreaksToolWhenDurabilityReachesZero()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            ItemStack feller = new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(1);
            inventory.SetSlot(0, feller);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, feller, equippedSlotIndex: 0);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void HarvestWithUntrackedDurabilityToolDoesNotConsumeDurability()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            ItemStack feller = new ItemStack(ItemId.ReedwoodFeller, 1); // Durability = 0 (no tracking)
            inventory.SetSlot(0, feller);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            service.TryHarvest(world, inventory, HarvestPosition, feller, equippedSlotIndex: 0);

            Assert.That(inventory.GetSlot(0), Is.EqualTo(feller)); // unchanged
        }

        [Test]
        public void HarvestTierMinResourceBreaksSlowlyButDropsNothingWithWrongToolOrInsufficientTier()
        {
            // RosycopperBloom: EffectiveTool=Delver, HarvestTierMin=2
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.RosycopperBloom);
            var service = CreateService(itemRegistry);

            // Bare hand — wrong tool, no tier
            BlockHarvestResult handResult = service.TryPreviewHarvest(world, inventory, HarvestPosition, ItemStack.Empty);
            Assert.That(handResult.Succeeded, Is.True);
            Assert.That(handResult.Drops, Is.Empty);
            Assert.That(handResult.WorkRequired, Is.GreaterThan(0));

            // Tier-1 delver — right tool, insufficient tier
            ItemStack tier1Delver = new ItemStack(ItemId.ReedwoodDelver, 1).WithDurability(20);
            BlockHarvestResult tier1Result = service.TryPreviewHarvest(world, inventory, HarvestPosition, tier1Delver);
            Assert.That(tier1Result.Succeeded, Is.True);
            Assert.That(tier1Result.Drops, Is.Empty);
            Assert.That(tier1Result.WorkRequired, Is.GreaterThan(0));

            // Tier-2 delver — right tool, meets tier
            ItemStack tier2Delver = new ItemStack(ItemId.FlintDelver, 1).WithDurability(35);
            BlockHarvestResult tier2Result = service.TryPreviewHarvest(world, inventory, HarvestPosition, tier2Delver);
            Assert.That(tier2Result.Succeeded, Is.True);
            Assert.That(tier2Result.Drops, Is.Not.Empty);
        }

        [Test]
        public void DefaultScarcityTuningKeepsResourceOrderingAndDepthBands()
        {
            SurvivalResourceTuning tuning = SurvivalResourceTuning.CreateDefault();

            ResourceVeinTuning coal = tuning.Get(BlockRegistry.EmbercoalSeam);
            ResourceVeinTuning copper = tuning.Get(BlockRegistry.RosycopperBloom);
            ResourceVeinTuning iron = tuning.Get(BlockRegistry.RustcoreOre);

            Assert.That(coal.ChancePermille, Is.GreaterThan(copper.ChancePermille));
            Assert.That(copper.ChancePermille, Is.GreaterThan(iron.ChancePermille));
            Assert.That(copper.MaxY, Is.GreaterThan(coal.MaxY));
            Assert.That(coal.MaxY, Is.GreaterThan(iron.MaxY));
            Assert.That(copper.MaxY, Is.GreaterThan(iron.MaxY));
            Assert.That(coal.MinY, Is.GreaterThanOrEqualTo(iron.MinY));
        }

        [Test]
        public void DurabilityCostFollowsBlockCategoryAndToolMatch()
        {
            // Plants, soil, wood, stone, crafted → 1 (§6.3).
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Organic, 0, correctTool: true, sufficientTier: true), Is.EqualTo(1));
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Terrain, 1, correctTool: true, sufficientTier: true), Is.EqualTo(1));
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Crafted, 1, correctTool: true, sufficientTier: true), Is.EqualTo(1));
            // Common ore (resource, tier < 5) → 2; deep ore (tier ≥ 5) → 3.
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Resource, 2, correctTool: true, sufficientTier: true), Is.EqualTo(2));
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Resource, 5, correctTool: true, sufficientTier: true), Is.EqualTo(3));
            // Wrong tool adds +1; insufficient tier adds +2.
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Terrain, 1, correctTool: false, sufficientTier: true), Is.EqualTo(2));
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Terrain, 1, correctTool: true, sufficientTier: false), Is.EqualTo(3));
            Assert.That(MiningFormula.DurabilityCost(BlockCategory.Resource, 5, correctTool: false, sufficientTier: false), Is.EqualTo(6));
        }

        [Test]
        public void ToolSpeedScalesWithMaterialTierAndClass()
        {
            // Higher material tier mines faster (§7.1).
            Assert.That(MiningFormula.ToolSpeed(HarvestToolKind.Delver, 2),
                Is.GreaterThan(MiningFormula.ToolSpeed(HarvestToolKind.Delver, 1)));
            // Sickle has a higher class multiplier than Mallet at the same tier (§7.2).
            Assert.That(MiningFormula.ToolSpeed(HarvestToolKind.Sickle, 2),
                Is.GreaterThan(MiningFormula.ToolSpeed(HarvestToolKind.Mallet, 2)));
            // Bare hand is the slow baseline.
            Assert.That(MiningFormula.ToolSpeed(HarvestToolKind.Hand, 0), Is.EqualTo(MiningFormula.HandSpeed));
        }

        [Test]
        public void TierThreeToolUnlocksTierThreeOreHarvest()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.RustcoreOre);
            var service = CreateService(itemRegistry);

            // Tier-2 Flint Delver can slowly break iron (Rustcore is Delver/3), but gets no ore.
            ItemStack flint = itemRegistry.CreateItemStack(ItemId.FlintDelver);
            BlockHarvestResult flintPreview = service.TryPreviewHarvest(world, inventory, HarvestPosition, flint);
            Assert.That(flintPreview.Succeeded, Is.True);
            Assert.That(flintPreview.Drops, Is.Empty);

            // Tier-3 Rosycopper Delver (from the new tool ladder) can break and collect ore.
            ItemStack rosycopper = itemRegistry.CreateItemStack(new ItemId("rosycopper_delver"));
            BlockHarvestResult rosycopperPreview = service.TryPreviewHarvest(world, inventory, HarvestPosition, rosycopper);
            Assert.That(rosycopperPreview.Succeeded, Is.True);
            Assert.That(rosycopperPreview.Drops, Is.Not.Empty);
        }

        [Test]
        public void LowTierResourceHarvestBreaksBlockWithoutGrantingOre()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.RustcoreOre);
            var service = CreateService(itemRegistry);
            ItemStack flint = itemRegistry.CreateItemStack(ItemId.FlintDelver);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, flint);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Drops, Is.Empty);
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void MetalToolLadderRegistersAllTiersAndClasses()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();

            // Durability = material base × class multiplier (§7.1/§7.2).
            Assert.That(itemRegistry.Get(new ItemId("rosycopper_delver")).MaxDurability, Is.EqualTo(160));
            Assert.That(itemRegistry.Get(new ItemId("rosycopper_sickle")).MaxDurability, Is.EqualTo(112)); // 160 × 0.70
            Assert.That(itemRegistry.Get(new ItemId("starforged_mallet")).MaxDurability, Is.EqualTo(2160)); // 1800 × 1.20

            // Tiers ascend Rosycopper(3) → Starforged(7).
            Assert.That(itemRegistry.Get(new ItemId("rosycopper_delver")).ToolTier, Is.EqualTo(3));
            Assert.That(itemRegistry.Get(new ItemId("bronze_delver")).ToolTier, Is.EqualTo(4));
            Assert.That(itemRegistry.Get(new ItemId("ironroot_delver")).ToolTier, Is.EqualTo(5));
            Assert.That(itemRegistry.Get(new ItemId("deepsteel_delver")).ToolTier, Is.EqualTo(6));
            Assert.That(itemRegistry.Get(new ItemId("starforged_delver")).ToolTier, Is.EqualTo(7));
        }

        // ── M6-F: Drop table + special tool actions ──────────────────────────

        [Test]
        public void DropTableRollsPrimaryCountWithinBounds()
        {
            var table = new DropTable(new DropTableEntry(ItemId.ReedFiber, 1, 3));
            uint rng = 1u;
            for (int i = 0; i < 100; i++)
            {
                ItemStack[] result = table.Roll(ref rng);
                Assert.That(result.Length, Is.EqualTo(1));
                Assert.That(result[0].Count, Is.InRange(1, 3));
                Assert.That(result[0].ItemId, Is.EqualTo(ItemId.ReedFiber));
            }
        }

        [Test]
        public void DropTableSecondaryEntryRespectsChance()
        {
            // 0% chance entry should never produce a drop.
            var table = new DropTable(
                new DropTableEntry(ItemId.ReedFiber, 1, 1),
                new DropTableEntry(ItemId.Leafmoss,  1, 1, chance: 0f));
            uint rng = 1u;
            for (int i = 0; i < 50; i++)
            {
                ItemStack[] result = table.Roll(ref rng);
                Assert.That(result.Length, Is.EqualTo(1), "Zero-chance secondary entry must never roll a drop.");
            }
        }

        [Test]
        public void ClaybedDropsClayLumpWithinCanonicalRange()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();

            bool sawVariableYield = false;
            for (uint seed = 1; seed <= 40; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: seed);

                ItemStack drop = service.RollHarvestDrop(BlockRegistry.Claybed, HarvestToolKind.Spade);

                Assert.That(drop.ItemId, Is.EqualTo(ItemId.ClayLump));
                Assert.That(drop.Count, Is.InRange(2, 4));
                if (drop.Count > 2)
                    sawVariableYield = true;
            }

            Assert.That(sawVariableYield, Is.True, "Claybed must sometimes drop more than the minimum clay_lump yield.");
        }

        [Test]
        public void CropStageHarvestUsesLowerBaseYieldAndHigherMatureYield()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            bool sawBaseGrainTwo = false;
            bool sawMatureGrainThree = false;
            bool sawMatureBerryFour = false;

            for (uint seed = 1; seed <= 80; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: seed);

                ItemStack baseGrain = service.RollHarvestDrop(BlockRegistry.GrainStalk, HarvestToolKind.Sickle);
                ItemStack matureGrain = service.RollHarvestDrop(BlockRegistry.GrainStalk_S4, HarvestToolKind.Sickle);
                ItemStack immatureBerry = service.RollHarvestDrop(BlockRegistry.Berrybush_S1, HarvestToolKind.Sickle);
                ItemStack matureBerry = service.RollHarvestDrop(BlockRegistry.Berrybush_S5, HarvestToolKind.Sickle);

                Assert.That(baseGrain.ItemId, Is.EqualTo(ItemId.GrainBundle));
                Assert.That(baseGrain.Count, Is.InRange(1, 2));
                Assert.That(matureGrain.ItemId, Is.EqualTo(ItemId.GrainBundle));
                Assert.That(matureGrain.Count, Is.InRange(1, 3));
                Assert.That(immatureBerry.ItemId, Is.EqualTo(ItemId.BerryCluster));
                Assert.That(immatureBerry.Count, Is.EqualTo(1));
                Assert.That(matureBerry.ItemId, Is.EqualTo(ItemId.BerryCluster));
                Assert.That(matureBerry.Count, Is.InRange(2, 4));

                if (baseGrain.Count == 2) sawBaseGrainTwo = true;
                if (matureGrain.Count == 3) sawMatureGrainThree = true;
                if (matureBerry.Count == 4) sawMatureBerryFour = true;
            }

            Assert.That(sawBaseGrainTwo, Is.True, "Base grain should use the lower 1-2 wild grain range.");
            Assert.That(sawMatureGrainThree, Is.True, "Mature grain must reach the canonical 1-3 crop range.");
            Assert.That(sawMatureBerryFour, Is.True, "Mature berrybush must reach the canonical 2-4 crop range.");
        }

        [Test]
        public void HarvestingClaybedAddsClayLumpAndClearsWorldBlock()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: 7);
            var inventory = new Inventory(ir, slotCount: 4, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.Claybed);
            ItemStack spade = ir.CreateItemStack(ItemId.ReedwoodSpade);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, spade);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop.ItemId, Is.EqualTo(ItemId.ClayLump));
            Assert.That(result.Drop.Count, Is.InRange(2, 4));
            Assert.That(inventory.CountOf(ItemId.ClayLump), Is.EqualTo(result.Drop.Count));
            Assert.That(inventory.CountOf(ItemId.Claybed), Is.Zero);
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void RiverSiltCanProduceClayLumpWithSpade()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            bool sawClay = false;

            for (uint seed = 1; seed <= 80 && !sawClay; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: seed);

                ItemStack drop = service.RollHarvestDrop(BlockRegistry.RiverSilt, HarvestToolKind.Spade);
                if (drop.ItemId == ItemId.ClayLump)
                {
                    Assert.That(drop.Count, Is.EqualTo(1));
                    sawClay = true;
                }
            }

            Assert.That(sawClay, Is.True, "River silt must have a reachable clay_lump roll.");
        }

        [Test]
        public void SickleDoubleRollNeverDropsLessThanMinimum()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            ItemStack sickle = new ItemStack(ItemId.ReedwoodSickle, 1);

            for (uint seed = 1; seed <= 30; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir,
                    BlockHarvestRuleSet.CreateDefault(ir),
                    rngSeed: seed);
                var inventory = new Inventory(ir, slotCount: 10, hotbarSlotCount: 1);
                var world = CreateSingleBlockWorld(BlockRegistry.Reedgrass);

                BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, sickle);
                Assert.That(result.Succeeded, Is.True, $"Seed {seed}: harvest should succeed.");
                Assert.That(result.Drop.Count, Is.GreaterThanOrEqualTo(1),
                    $"Seed {seed}: Sickle double-roll must yield at least the minimum count.");
                Assert.That(result.Drop.Count, Is.LessThanOrEqualTo(3),
                    $"Seed {seed}: Sickle double-roll must not exceed the maximum count.");
            }
        }

        [Test]
        public void SickleDoubleRollYieldsHigherAverageThanSingleRoll()
        {
            // Over many harvests, Sickle double-roll should average strictly above the raw minimum (1).
            ItemRegistry ir = ItemRegistry.CreateDefault();
            int totalSickle = 0;

            for (uint seed = 1; seed <= 200; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir,
                    BlockHarvestRuleSet.CreateDefault(ir),
                    rngSeed: seed);
                var inventory = new Inventory(ir, slotCount: 10, hotbarSlotCount: 1);
                var world = CreateSingleBlockWorld(BlockRegistry.Reedgrass);
                ItemStack sickle = new ItemStack(ItemId.ReedwoodSickle, 1);
                BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, sickle);
                if (result.Succeeded) totalSickle += result.Drop.Count;
            }

            // Average should be well above 1 (minimum) given a uniform 1-3 distribution with double-roll.
            float avg = totalSickle / 200f;
            Assert.That(avg, Is.GreaterThan(1.5f),
                "Sickle double-roll average should exceed the minimum drop count.");
        }

        [Test]
        public void CarverOnResinKnotCanDropMoreThanOne()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();

            bool sawTwo = false;
            for (uint seed = 1; seed <= 100; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir,
                    BlockHarvestRuleSet.CreateDefault(ir),
                    rngSeed: seed);
                var inventory = new Inventory(ir, slotCount: 10, hotbarSlotCount: 1);
                var world = CreateSingleBlockWorld(BlockRegistry.ResinKnot);
                ItemStack carver = new ItemStack(ItemId.ReedwoodCarver, 1);

                BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, carver);
                if (result.Succeeded && result.Drop.Count == 2) { sawTwo = true; break; }
            }

            Assert.That(sawTwo, Is.True, "Carver on ResinKnot must be able to yield 2 drops.");
        }

        [Test]
        public void NonCarverOnResinKnotDropsExactlyOne()
        {
            // Without Carver, fixed Drop (1 resin_knot) is returned even though a table is present.
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir,
                BlockHarvestRuleSet.CreateDefault(ir),
                rngSeed: 42);
            var inventory = new Inventory(ir, slotCount: 10, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.ResinKnot);

            // Hand harvest — no effective tool bonus.
            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Drop.Count, Is.EqualTo(1),
                "Without Carver, ResinKnot must drop exactly 1.");
        }

        [Test]
        public void RollHarvestDropAppliesSickleBonusForReedgrass()
        {
            // RollHarvestDrop is the shared roll used by the authoritative survival harvest path.
            ItemRegistry ir = ItemRegistry.CreateDefault();
            bool sawAboveMinimum = false;

            for (uint seed = 1; seed <= 60 && !sawAboveMinimum; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: seed);
                ItemStack drop = service.RollHarvestDrop(BlockRegistry.Reedgrass, HarvestToolKind.Sickle);
                Assert.That(drop.ItemId, Is.EqualTo(ItemId.ReedFiber));
                Assert.That(drop.Count, Is.InRange(1, 3));
                if (drop.Count > 1) sawAboveMinimum = true;
            }

            Assert.That(sawAboveMinimum, Is.True, "Sickle bonus should sometimes yield more than the minimum.");
        }

        [Test]
        public void RollHarvestDropReturnsBaseDropForNonBonusTool()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: 7);

            // Hand on ResinKnot (a Carver block) → fixed base drop of 1, regardless of the table.
            for (int i = 0; i < 20; i++)
            {
                ItemStack drop = service.RollHarvestDrop(BlockRegistry.ResinKnot, HarvestToolKind.Hand);
                Assert.That(drop.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void RollHarvestDropReturnsBaseDropWhenRuleHasNoTable()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: 3);

            // BranchwoodLog has no drop table → always the base 1× log.
            ItemStack drop = service.RollHarvestDrop(BlockRegistry.BranchwoodLog, HarvestToolKind.Feller);
            Assert.That(drop, Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
        }

        [Test]
        public void MultiDropHarvestAddsSecondaryStacksAtomically()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: 6);
            var inventory = new Inventory(ir, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.Reedgrass_S3);

            BlockHarvestResult result = service.TryHarvest(
                world,
                inventory,
                HarvestPosition,
                new ItemStack(ItemId.ReedwoodSickle, 1));

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(result.Drop.ItemId, Is.EqualTo(ItemId.ReedFiber));
            Assert.That(result.Drops, Does.Contain(new ItemStack(ItemId.ReedCutting, 1)));
            Assert.That(inventory.CountOf(ItemId.ReedFiber), Is.InRange(1, 3));
            Assert.That(inventory.CountOf(ItemId.ReedCutting), Is.EqualTo(1));
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void MultiDropHarvestDoesNotRemoveBlockWhenOnlyOneUniqueDropFits()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = new ResourceHarvestService(
                BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: 6);
            var inventory = new Inventory(ir, slotCount: 1, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.Reedgrass_S3);

            BlockHarvestResult result = service.TryHarvest(
                world,
                inventory,
                HarvestPosition,
                new ItemStack(ItemId.ReedwoodSickle, 1));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BlockHarvestFailureReason.InventoryFull));
            Assert.That(inventory.CountOf(ItemId.ReedFiber), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.ReedCutting), Is.Zero);
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Reedgrass_S3));
        }

        [Test]
        public void DryTurfHarvestCanReturnDrygrassSeed()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            bool sawSeed = false;

            for (uint seed = 1; seed <= 120 && !sawSeed; seed++)
            {
                var service = new ResourceHarvestService(
                    BlockRegistry.CreateDefault(), ir, BlockHarvestRuleSet.CreateDefault(ir), rngSeed: seed);
                ItemStack[] drops = service.RollHarvestDrops(BlockRegistry.DryTurf, HarvestToolKind.Spade);
                for (int i = 0; i < drops.Length; i++)
                {
                    if (drops[i].ItemId == ItemId.DrygrassSeed)
                        sawSeed = true;
                }
            }

            Assert.That(sawSeed, Is.True, "Dry turf must have a reachable drygrass_seed roll.");
        }

        [Test]
        public void SaplingStagesHarvestAsPlaceableSaplingItem()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            ItemDefinition sapling = itemRegistry.Get(ItemId.Sapling);
            Assert.That(sapling.Kind, Is.EqualTo(ItemKind.Placeable));
            Assert.That(sapling.BlockId, Is.EqualTo(BlockRegistry.Sapling));

            var service = CreateService(itemRegistry);
            BlockId[] stages = { BlockRegistry.Sapling, BlockRegistry.Sapling_S1, BlockRegistry.Sapling_S2 };
            for (int i = 0; i < stages.Length; i++)
            {
                var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
                VoxelWorld world = CreateSingleBlockWorld(stages[i]);

                BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);

                Assert.That(result.Succeeded, Is.True, $"stage={stages[i]} failure={result.FailureReason}");
                Assert.That(result.Drop, Is.EqualTo(new ItemStack(ItemId.Sapling, 1)));
                Assert.That(inventory.CountOf(ItemId.Sapling), Is.EqualTo(1));
                Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
            }
        }

        [Test]
        public void GroundItemPickupRespectsRadiusAndRecentDropProtection()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var store = new GroundItemStore(ir);
            var inventory = new Inventory(ir, slotCount: 4, hotbarSlotCount: 1);
            long startTick = 100;
            store.Spawn(new ItemStack(ItemId.ReedFiber, 2), 0f, 0f, 0f, startTick, droppedByPlayerId: "player-a");

            bool protectedPickup = store.TryPickupNearest(
                inventory,
                0f,
                0f,
                0f,
                startTick + GroundItemStore.RecentDropProtectionTicks - 1,
                "player-b",
                out _);
            Assert.That(protectedPickup, Is.False, "Recent player drops are protected from other players for 3 seconds.");
            Assert.That(store.Count, Is.EqualTo(1));

            bool outsideRadius = store.TryPickupNearest(
                inventory,
                GroundItemStore.PickupRadiusBlocks + 0.1f,
                0f,
                0f,
                startTick + GroundItemStore.RecentDropProtectionTicks,
                "player-b",
                out _);
            Assert.That(outsideRadius, Is.False, "Items outside the 2.5-block pickup radius must not be collected.");
            Assert.That(store.Count, Is.EqualTo(1));

            bool pickedUp = store.TryPickupNearest(
                inventory,
                GroundItemStore.PickupRadiusBlocks,
                0f,
                0f,
                startTick + GroundItemStore.RecentDropProtectionTicks,
                "player-b",
                out ItemStack stack);
            Assert.That(pickedUp, Is.True);
            Assert.That(stack, Is.EqualTo(new ItemStack(ItemId.ReedFiber, 2)));
            Assert.That(inventory.CountOf(ItemId.ReedFiber), Is.EqualTo(2));
            Assert.That(store.Count, Is.EqualTo(0));
        }

        [Test]
        public void GroundItemDespawnsAfterTenLoadedMinutes()
        {
            var store = new GroundItemStore(ItemRegistry.CreateDefault());
            long startTick = 250;
            store.Spawn(new ItemStack(ItemId.Leafmoss, 1), 1f, 0f, 1f, startTick);

            Assert.That(store.RemoveExpired(startTick + GroundItemStore.DespawnTicks - 1), Is.EqualTo(0));
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.RemoveExpired(startTick + GroundItemStore.DespawnTicks), Is.EqualTo(1));
            Assert.That(store.Count, Is.EqualTo(0));
        }

        [Test]
        public void GroundItemPickupDoesNotRemoveItemWhenInventoryIsFull()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var store = new GroundItemStore(ir);
            var inventory = new Inventory(ir, slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, ItemRegistry.BlockStackSize));
            store.Spawn(new ItemStack(ItemId.ReedFiber, 1), 0f, 0f, 0f, worldTick: 0);

            bool pickedUp = store.TryPickupNearest(
                inventory,
                0f,
                0f,
                0f,
                worldTick: 0,
                playerId: "player-a",
                out ItemStack stack);

            Assert.That(pickedUp, Is.False);
            Assert.That(stack.IsEmpty, Is.True);
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(inventory.CountOf(ItemId.ReedFiber), Is.Zero);
        }

        [Test]
        public void HarvestToGroundBreaksBlockAndSpawnsOverflowDropWhenInventoryIsFull()
        {
            ItemRegistry ir = ItemRegistry.CreateDefault();
            var service = CreateService(ir);
            var inventory = new Inventory(ir, slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, ItemRegistry.BlockStackSize));
            var groundItems = new GroundItemStore(ir);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);

            BlockHarvestResult result = service.TryHarvestToGround(
                world,
                inventory,
                HarvestPosition,
                ItemStack.Empty,
                groundItems,
                dropX: 1.5f,
                dropY: 1.5f,
                dropZ: 1.5f,
                worldTick: 0,
                droppedByPlayerId: "player-a");

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.Air));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.Zero);
            Assert.That(groundItems.Count, Is.EqualTo(1));
            Assert.That(groundItems.Items[0].Stack, Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
        }

        static readonly BlockPosition HarvestPosition = new(1, 1, 1);

        static ResourceHarvestService CreateService(ItemRegistry itemRegistry)
        {
            return new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
        }

        static VoxelWorld CreateSingleBlockWorld(BlockId blockId)
        {
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 4, seed: 123);
            world.SetBlock(HarvestPosition, blockId, trackChange: false);
            return world;
        }
    }
}
