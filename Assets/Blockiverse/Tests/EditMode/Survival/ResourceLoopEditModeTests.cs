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
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, ItemRegistry.ResourceStackSize));
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.BranchwoodLog);
            var service = CreateService(itemRegistry);

            BlockHarvestResult result = service.TryHarvest(world, inventory, HarvestPosition, ItemStack.Empty);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(BlockHarvestFailureReason.InventoryFull));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.Zero);
            Assert.That(world.GetBlock(HarvestPosition), Is.EqualTo(BlockRegistry.BranchwoodLog));
        }

        [Test]
        public void MatchingToolsReduceMiningTimeComparedWithHand()
        {
            BlockHarvestRuleSet rules = BlockHarvestRuleSet.CreateDefault(ItemRegistry.CreateDefault());

            BlockHarvestRule log        = rules.Get(BlockRegistry.BranchwoodLog);
            BlockHarvestRule rosycopper = rules.Get(BlockRegistry.RosycopperBloom);
            BlockHarvestRule buildTable = rules.Get(BlockRegistry.BuildTable);

            // Canonical mining formula (§6.1): correct tool mines faster than bare hands,
            // wrong tool class mines slower than the correct tool, and meeting the tier
            // requirement mines faster than being below tier.
            Assert.That(log.HandMineTicks, Is.GreaterThan(0));
            Assert.That(log.GetMineTicks(HarvestToolKind.Feller, toolTier: 1), Is.LessThan(log.HandMineTicks));
            Assert.That(log.GetMineTicks(HarvestToolKind.Delver, toolTier: 1),
                Is.GreaterThan(log.GetMineTicks(HarvestToolKind.Feller, toolTier: 1)));
            Assert.That(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 2),
                Is.LessThan(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 1)));
            Assert.That(rosycopper.GetMineTicks(HarvestToolKind.Delver, toolTier: 2), Is.LessThan(rosycopper.HandMineTicks));
            Assert.That(buildTable.GetMineTicks(HarvestToolKind.Mallet, toolTier: 1), Is.LessThan(buildTable.HandMineTicks));
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
        public void HarvestTierMinBlockFailsWithWrongToolOrInsufficientTier()
        {
            // RosycopperBloom: EffectiveTool=Delver, HarvestTierMin=2
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 2, hotbarSlotCount: 1);
            VoxelWorld world = CreateSingleBlockWorld(BlockRegistry.RosycopperBloom);
            var service = CreateService(itemRegistry);

            // Bare hand — wrong tool, no tier
            BlockHarvestResult handResult = service.TryPreviewHarvest(world, inventory, HarvestPosition, ItemStack.Empty);
            Assert.That(handResult.Succeeded, Is.False);
            Assert.That(handResult.FailureReason, Is.EqualTo(BlockHarvestFailureReason.InsufficientTool));

            // Tier-1 delver — right tool, insufficient tier
            ItemStack tier1Delver = new ItemStack(ItemId.ReedwoodDelver, 1).WithDurability(20);
            BlockHarvestResult tier1Result = service.TryPreviewHarvest(world, inventory, HarvestPosition, tier1Delver);
            Assert.That(tier1Result.Succeeded, Is.False);
            Assert.That(tier1Result.FailureReason, Is.EqualTo(BlockHarvestFailureReason.InsufficientTool));

            // Tier-2 delver — right tool, meets tier
            ItemStack tier2Delver = new ItemStack(ItemId.FlintDelver, 1).WithDurability(35);
            BlockHarvestResult tier2Result = service.TryPreviewHarvest(world, inventory, HarvestPosition, tier2Delver);
            Assert.That(tier2Result.Succeeded, Is.True);
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

            // Tier-2 Flint Delver cannot mine iron (Rustcore is Delver/3).
            ItemStack flint = itemRegistry.CreateItemStack(ItemId.FlintDelver);
            Assert.That(service.TryPreviewHarvest(world, inventory, HarvestPosition, flint).FailureReason,
                Is.EqualTo(BlockHarvestFailureReason.InsufficientTool));

            // Tier-3 Rosycopper Delver (from the new tool ladder) can.
            ItemStack rosycopper = itemRegistry.CreateItemStack(new ItemId("rosycopper_delver"));
            Assert.That(service.TryPreviewHarvest(world, inventory, HarvestPosition, rosycopper).Succeeded, Is.True);
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
