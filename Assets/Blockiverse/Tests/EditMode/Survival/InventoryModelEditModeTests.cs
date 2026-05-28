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
        public void DefaultRegistryContainsWaveOneDefinitionsAndBlockMappings()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();

            ItemId[] expectedItems =
            {
                ItemId.MeadowTurf,
                ItemId.Loam,
                ItemId.Slate,
                ItemId.Timber,
                ItemId.Leafmass,
                ItemId.Clearstone,
                ItemId.Coalstone,
                ItemId.Copperstone,
                ItemId.Ironstone,
                ItemId.Workbench,
                ItemId.Torchbud,
                ItemId.StorageCrate,
                ItemId.Chipper,
                ItemId.Mallet,
                ItemId.Pick,
                ItemId.RecoveryWrap
            };

            CollectionAssert.AreEquivalent(expectedItems, registry.All.Where(item => item.Id != ItemId.None).Select(item => item.Id));
            Assert.That((int)ItemId.None, Is.EqualTo(0));
            Assert.That(registry.TryGetItemForBlock(BlockRegistry.Air, out _), Is.False);

            AssertBlockMapsToItem(registry, BlockRegistry.MeadowTurf, ItemId.MeadowTurf);
            AssertBlockMapsToItem(registry, BlockRegistry.Loam, ItemId.Loam);
            AssertBlockMapsToItem(registry, BlockRegistry.Slate, ItemId.Slate);
            AssertBlockMapsToItem(registry, BlockRegistry.Timber, ItemId.Timber);
            AssertBlockMapsToItem(registry, BlockRegistry.Leafmass, ItemId.Leafmass);
            AssertBlockMapsToItem(registry, BlockRegistry.Clearstone, ItemId.Clearstone);
            AssertBlockMapsToItem(registry, BlockRegistry.Coalstone, ItemId.Coalstone);
            AssertBlockMapsToItem(registry, BlockRegistry.Copperstone, ItemId.Copperstone);
            AssertBlockMapsToItem(registry, BlockRegistry.Ironstone, ItemId.Ironstone);
            AssertBlockMapsToItem(registry, BlockRegistry.Workbench, ItemId.Workbench);
            AssertBlockMapsToItem(registry, BlockRegistry.Torchbud, ItemId.Torchbud);
            AssertBlockMapsToItem(registry, BlockRegistry.StorageCrate, ItemId.StorageCrate);
        }

        [Test]
        public void DefaultRegistryUsesRequiredStackSizesAndStacksPlaceablesToSixtyFour()
        {
            ItemRegistry registry = ItemRegistry.CreateDefault();

            Assert.That(registry.Get(ItemId.MeadowTurf).MaxStackSize, Is.EqualTo(ItemRegistry.ResourceStackSize));
            Assert.That(registry.Get(ItemId.Timber).MaxStackSize, Is.EqualTo(ItemRegistry.ResourceStackSize));
            Assert.That(registry.Get(ItemId.Coalstone).MaxStackSize, Is.EqualTo(ItemRegistry.ResourceStackSize));
            Assert.That(registry.Get(ItemId.Workbench).MaxStackSize, Is.EqualTo(ItemRegistry.ResourceStackSize));
            Assert.That(registry.Get(ItemId.StorageCrate).MaxStackSize, Is.EqualTo(ItemRegistry.ResourceStackSize));
            Assert.That(registry.Get(ItemId.Chipper).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
            Assert.That(registry.Get(ItemId.Mallet).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
            Assert.That(registry.Get(ItemId.Pick).MaxStackSize, Is.EqualTo(ItemRegistry.ToolStackSize));
        }

        [Test]
        public void DefaultInventoryHasTwentyFourSlotsAndSixHotbarSlots()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());

            Assert.That(inventory.SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
            Assert.That(inventory.SlotCount, Is.EqualTo(24));
            Assert.That(inventory.HotbarSlotCount, Is.EqualTo(Inventory.DefaultHotbarSlotCount));
            Assert.That(inventory.HotbarSlotCount, Is.EqualTo(6));
            Assert.That(Enumerable.Range(0, inventory.SlotCount).All(slot => inventory.GetSlot(slot).IsEmpty), Is.True);
        }

        [Test]
        public void AddMergesIntoPartialStacksBeforeUsingEmptySlots()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 3, hotbarSlotCount: 1);
            inventory.SetSlot(1, new ItemStack(ItemId.Slate, 60));

            ItemStack leftover = inventory.Add(new ItemStack(ItemId.Slate, 10));

            Assert.That(leftover.IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Slate, 64)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Slate, 6)));
            Assert.That(inventory.GetSlot(2).IsEmpty, Is.True);
        }

        [Test]
        public void AddReturnsLeftoverWhenInventoryCannotFitFullStack()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 2, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.MeadowTurf, 64));
            inventory.SetSlot(1, new ItemStack(ItemId.MeadowTurf, 60));

            ItemStack leftover = inventory.Add(new ItemStack(ItemId.MeadowTurf, 10));

            Assert.That(leftover, Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 6)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 64)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.MeadowTurf, 64)));
        }

        [Test]
        public void TryAddAllReturnsFalseAndLeavesInventoryUnchangedWhenCapacityIsInsufficient()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault(), slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.Loam, 60));

            bool added = inventory.TryAddAll(new ItemStack(ItemId.Loam, 10));

            Assert.That(added, Is.False);
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Loam, 60)));
        }

        [Test]
        public void SplitSlotRemovesRequestedCountAndLeavesRemainder()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 20));

            ItemStack split = inventory.SplitSlot(0, 7);

            Assert.That(split, Is.EqualTo(new ItemStack(ItemId.Timber, 7)));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Timber, 13)));

            ItemStack remainder = inventory.SplitSlot(0, 13);

            Assert.That(remainder, Is.EqualTo(new ItemStack(ItemId.Timber, 13)));
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
        }

        [Test]
        public void RemoveConsumesExactCountAcrossStacksOrLeavesInventoryUnchanged()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.Loam, 5));
            inventory.SetSlot(1, new ItemStack(ItemId.Loam, 10));

            bool removed = inventory.Remove(ItemId.Loam, 12);

            Assert.That(removed, Is.True);
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Loam, 3)));

            bool removedTooMuch = inventory.Remove(ItemId.Loam, 4);

            Assert.That(removedTooMuch, Is.False);
            Assert.That(inventory.GetSlot(0).IsEmpty, Is.True);
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Loam, 3)));
        }

        [Test]
        public void InventoryValidatesInvalidCountsStackSizesAndUnknownItems()
        {
            var inventory = new Inventory(ItemRegistry.CreateDefault());

            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemStack(ItemId.Timber, 0));
            Assert.Throws<ArgumentException>(() => new ItemStack(ItemId.None, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(ItemRegistry.CreateDefault(), Inventory.MaxSlotCount + 1, hotbarSlotCount: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Remove(ItemId.Timber, 0));
            Assert.Throws<KeyNotFoundException>(() => inventory.Add(new ItemStack((ItemId)999, 1)));
            Assert.Throws<InvalidOperationException>(() => inventory.SetSlot(0, new ItemStack(ItemId.Pick, 2)));
        }

        static void AssertBlockMapsToItem(ItemRegistry registry, BlockId blockId, ItemId itemId)
        {
            Assert.That(registry.TryGetItemForBlock(blockId, out ItemDefinition definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo(itemId));
        }
    }
}
