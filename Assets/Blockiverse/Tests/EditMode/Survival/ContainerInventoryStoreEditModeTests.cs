using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class ContainerInventoryStoreEditModeTests
    {
        static readonly BlockPosition Pos = new(5, 6, 7);

        [Test]
        public void PopulateCreatesContainerWithGivenItems()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            store.Populate(Pos, new[] { ("reed_fiber", 5), ("stout_pole", 2) });

            Assert.That(store.Contains(Pos), Is.True);
            Inventory inv = store.GetOrNull(Pos);
            Assert.That(inv, Is.Not.Null);
            Assert.That(inv.CountOf(ItemId.ReedFiber), Is.EqualTo(5));
            Assert.That(inv.CountOf(ItemId.StoutPole), Is.EqualTo(2));
        }

        [Test]
        public void PopulateSkipsUnknownAndNonPositiveItems()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            store.Populate(Pos, new[] { ("totally_not_an_item", 4), ("reed_fiber", 0), ("reed_fiber", 3) });

            Inventory inv = store.GetOrNull(Pos);
            Assert.That(inv.CountOf(ItemId.ReedFiber), Is.EqualTo(3));
        }

        [Test]
        public void TransferAllIntoMovesContentsAndEmptiesContainer()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            store.Populate(Pos, new[] { ("reed_fiber", 6) });
            var target = new Inventory(ItemRegistry.CreateDefault(), slotCount: 10, hotbarSlotCount: 1);

            bool emptied = store.TransferAllInto(Pos, target);

            Assert.That(emptied, Is.True);
            Assert.That(target.CountOf(ItemId.ReedFiber), Is.EqualTo(6));
            Assert.That(store.Contains(Pos), Is.False, "A fully drained container is removed from the store.");
        }

        [Test]
        public void TransferAllIntoLeavesRemainderWhenTargetIsFull()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            store.Populate(Pos, new[] { ("reed_fiber", 8) });

            // Single-slot target whose only slot is occupied by a different item, leaving no room.
            var target = new Inventory(ItemRegistry.CreateDefault(), slotCount: 1, hotbarSlotCount: 1);
            target.SetSlot(0, new ItemStack(ItemId.StoutPole, 1));
            Assume.That(target.GetAvailableCapacity(ItemId.ReedFiber), Is.EqualTo(0));

            bool emptied = store.TransferAllInto(Pos, target);

            Assert.That(emptied, Is.False, "Container should not be emptied when the target cannot hold anything.");
            Assert.That(store.Contains(Pos), Is.True, "Undrained container remains in the store.");
            Assert.That(target.CountOf(ItemId.ReedFiber), Is.EqualTo(0));
            Assert.That(store.GetOrNull(Pos).CountOf(ItemId.ReedFiber), Is.EqualTo(8));
        }

        [Test]
        public void RemoveDeletesContainerEntry()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            store.Populate(Pos, new[] { ("reed_fiber", 1) });

            Assert.That(store.Remove(Pos), Is.True);
            Assert.That(store.Contains(Pos), Is.False);
            Assert.That(store.Count, Is.EqualTo(0));
        }

        [Test]
        public void TransferAllIntoNonexistentContainerIsNoOp()
        {
            var store = new ContainerInventoryStore(ItemRegistry.CreateDefault());
            var target = new Inventory(ItemRegistry.CreateDefault(), slotCount: 4, hotbarSlotCount: 1);
            Assert.That(store.TransferAllInto(Pos, target), Is.True);
        }
    }
}
