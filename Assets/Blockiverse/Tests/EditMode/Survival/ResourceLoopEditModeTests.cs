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
        public void MatchingToolsReduceHarvestWorkComparedWithHand()
        {
            BlockHarvestRuleSet rules = BlockHarvestRuleSet.CreateDefault(ItemRegistry.CreateDefault());

            BlockHarvestRule log = rules.Get(BlockRegistry.BranchwoodLog);
            BlockHarvestRule rosycopper = rules.Get(BlockRegistry.RosycopperBloom);
            BlockHarvestRule buildTable = rules.Get(BlockRegistry.BuildTable);

            Assert.That(log.GetWorkRequired(HarvestToolKind.Feller), Is.LessThan(log.GetWorkRequired(HarvestToolKind.Hand)));
            Assert.That(log.GetWorkRequired(HarvestToolKind.Delver), Is.EqualTo(log.GetWorkRequired(HarvestToolKind.Hand)));
            Assert.That(rosycopper.GetWorkRequired(HarvestToolKind.Delver), Is.LessThan(rosycopper.GetWorkRequired(HarvestToolKind.Hand)));
            Assert.That(buildTable.GetWorkRequired(HarvestToolKind.Mallet), Is.LessThan(buildTable.GetWorkRequired(HarvestToolKind.Hand)));
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
            Assert.That(coal.MaxY, Is.GreaterThan(copper.MaxY));
            Assert.That(copper.MaxY, Is.GreaterThan(iron.MaxY));
            Assert.That(coal.MinY, Is.GreaterThanOrEqualTo(iron.MinY));
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
