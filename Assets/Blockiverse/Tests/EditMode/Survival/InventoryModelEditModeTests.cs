using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class InventoryModelEditModeTests
    {
        [Test]
        public void DefaultRegistryContainsCanonicalItemDefinitionsAndBlockMappings()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();

            Assert.That(ItemId.None.IsNone, Is.True);
            Assert.That(registry.TryGetItemForBlock(BlockRegistry.Air, out _), Is.False);

            AssertBlockMapsToItem(registry, BlockRegistry.MeadowTurf,        ItemId.MeadowTurf);
            AssertBlockMapsToItem(registry, BlockRegistry.LooseLoam,          ItemId.LooseLoam);
            AssertBlockMapsToItem(registry, BlockRegistry.Graystone,          ItemId.Graystone);
            AssertBlockMapsToItem(registry, BlockRegistry.BranchwoodLog,      ItemId.BranchwoodLog);
            AssertBlockMapsToItem(registry, BlockRegistry.Leafmoss,           ItemId.Leafmoss);
            AssertBlockMapsToItem(registry, BlockRegistry.LumenQuartzCluster, ItemId.LumenCrystal);
            AssertBlockMapsToItem(registry, BlockRegistry.EmbercoalSeam,      ItemId.Embercoal);
            AssertBlockMapsToItem(registry, BlockRegistry.RosycopperBloom,    ItemId.RawRosycopper);
            AssertBlockMapsToItem(registry, BlockRegistry.RustcoreOre,        ItemId.RawRustcore);
            AssertBlockMapsToItem(registry, BlockRegistry.BuildTable,         ItemId.BuildTable);
            AssertBlockMapsToItem(registry, BlockRegistry.Glowwick,           ItemId.Glowwick);
            AssertBlockMapsToItem(registry, BlockRegistry.StorageCrate,       ItemId.StorageCrate);
        }

        [Test]
        public void DefaultRegistryUsesRequiredStackSizesPerCategory()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();

            Assert.That(registry.Get(ItemId.MeadowTurf).MaxStackSize,    Is.EqualTo(ItemRegistry.BlockStackSize));
            Assert.That(registry.Get(ItemId.BranchwoodLog).MaxStackSize,  Is.EqualTo(ItemRegistry.BlockStackSize));
            Assert.That(registry.Get(ItemId.Embercoal).MaxStackSize,      Is.EqualTo(ItemRegistry.OreStackSize));
            Assert.That(registry.Get(ItemId.LumenCrystal).MaxStackSize,   Is.EqualTo(ItemRegistry.CrystalStackSize));
            Assert.That(registry.Get(ItemId.BuildTable).MaxStackSize,     Is.EqualTo(ItemRegistry.BlockStackSize));
            Assert.That(registry.Get(ItemId.StorageCrate).MaxStackSize,   Is.EqualTo(ItemRegistry.BlockStackSize));
            Assert.That(registry.Get(ItemId.ReedwoodFeller).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
            Assert.That(registry.Get(ItemId.ReedwoodMallet).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
            Assert.That(registry.Get(ItemId.ReedwoodDelver).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
            Assert.That(registry.Get(ItemId.FieldBandage).MaxStackSize,   Is.EqualTo(ItemRegistry.FieldBandageStackSize));
        }

        [Test]
        public void DefaultInventoryHasTwentyFourSlotsAndSixHotbarSlots()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());

            Assert.That(inventory.SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
            Assert.That(inventory.SlotCount, Is.EqualTo(44));
            Assert.That(inventory.HotbarSlotCount, Is.EqualTo(Inventory.DefaultHotbarSlotCount));
            Assert.That(inventory.HotbarSlotCount, Is.EqualTo(10));
            Assert.That(Enumerable.Range(0, inventory.SlotCount).All(slot => inventory.GetSlot(slot).IsEmpty), Is.True);
        }

        [Test]
        public void AddMergesIntoPartialStacksBeforeUsingEmptySlots()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 3, hotbarSlotCount: 1);
            inventory.SetSlot(1, new ItemStack(ItemId.LooseLoam, 95));

            ItemStack leftover = inventory.Add(new ItemStack(ItemId.LooseLoam, 10));

            Assert.That(leftover.IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.LooseLoam, 99)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.LooseLoam, 6)));
            Assert.That(inventory.GetSlot(2).IsEmpty, Is.True);
        }

        [Test]
        public void AddReturnsLeftoverWhenInventoryCannotFitFullStack()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 2, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.MeadowTurf, 99));
            inventory.SetSlot(1, new ItemStack(ItemId.MeadowTurf, 95));

            ItemStack leftover = inventory.Add(new ItemStack(ItemId.MeadowTurf, 10));

            Assert.That(leftover, Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 6)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 99)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 99)));
        }

        [Test]
        public void TryAddAllReturnsFalseAndLeavesInventoryUnchangedWhenCapacityIsInsufficient()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, 94));

            bool added = inventory.TryAddAll(new ItemStack(ItemId.LooseLoam, 10));

            Assert.That(added, Is.False);
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.LooseLoam, 94)));
        }

        [Test]
        public void SplitSlotRemovesRequestedCountAndLeavesRemainder()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 20));

            ItemStack split = inventory.SplitSlot(0, 7);

            Assert.That(split, Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 7)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 13)));

            ItemStack remainder = inventory.SplitSlot(0, 13);

            Assert.That(remainder, Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 13)));
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void RemoveConsumesExactCountAcrossStacksOrLeavesInventoryUnchanged()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, 5));
            inventory.SetSlot(1, new ItemStack(ItemId.LooseLoam, 10));

            bool removed = inventory.Remove(ItemId.LooseLoam, 12);

            Assert.That(removed, Is.True);
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.LooseLoam, 3)));

            bool removedTooMuch = inventory.Remove(ItemId.LooseLoam, 4);

            Assert.That(removedTooMuch, Is.False);
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.LooseLoam, 3)));
        }

        [Test]
        public void InventoryValidatesInvalidCountsStackSizesAndUnknownItems()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());

            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemStack(ItemId.BranchwoodLog, 0));
            Assert.Throws<ArgumentException>(() => new ItemStack(ItemId.None, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(ItemRegistry.CreateDefault(), Inventory.MaxSlotCount + 1, hotbarSlotCount: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Remove(ItemId.BranchwoodLog, 0));
            Assert.Throws<KeyNotFoundException>(() => inventory.Add(new ItemStack(new ItemId("unknown_item_test_999"), 1)));
            Assert.Throws<InvalidOperationException>(() => inventory.SetSlot(0, new ItemStack(ItemId.ReedwoodDelver, 2)));
        }

        static void AssertBlockMapsToItem(ItemRegistry registry, BlockId blockId, ItemId itemId)
        {
            Assert.That(registry.TryGetItemForBlock(blockId, out ItemDefinition definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo(itemId));
        }
    }
}
