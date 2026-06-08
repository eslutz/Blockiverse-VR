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
