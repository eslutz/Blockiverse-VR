using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    // Per-block container contents (e.g. the StorageCrate placed inside a structure ruin).
    //
    // Keyed by world block position. The store is the runtime home for "block entity" style data
    // that the flat voxel grid cannot hold. Worldgen rolls loot deterministically and the manager
    // populates this store; harvesting/opening a container reads and mutates its inventory here.
    public sealed class ContainerInventoryStore
    {
        public const int DefaultContainerSlotCount = 12;

        readonly Dictionary<BlockPosition, Inventory> containers = new();
        readonly ItemRegistry itemRegistry;
        readonly int slotCount;

        public ContainerInventoryStore(ItemRegistry itemRegistry = null, int slotCount = DefaultContainerSlotCount)
        {
            this.itemRegistry = itemRegistry ?? ItemRegistry.CreateDefault();
            this.slotCount = slotCount > 0 ? slotCount : DefaultContainerSlotCount;
        }

        public int Count => containers.Count;
        public IEnumerable<BlockPosition> Positions => containers.Keys;

        public bool Contains(BlockPosition position) => containers.ContainsKey(position);

        public bool TryGet(BlockPosition position, out Inventory inventory) =>
            containers.TryGetValue(position, out inventory);

        public Inventory GetOrNull(BlockPosition position) =>
            containers.TryGetValue(position, out Inventory inv) ? inv : null;

        // Returns the container at a position, creating an empty one if absent.
        public Inventory GetOrCreate(BlockPosition position)
        {
            if (!containers.TryGetValue(position, out Inventory inv))
            {
                inv = new Inventory(itemRegistry, slotCount, hotbarSlotCount: 0);
                containers[position] = inv;
            }
            return inv;
        }

        public void Set(BlockPosition position, Inventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            containers[position] = inventory;
        }

        public bool Remove(BlockPosition position) => containers.Remove(position);

        public void Clear() => containers.Clear();

        // Creates (or replaces) a container at a position and fills it with the given stacks.
        // Unknown item ids and non-positive counts are skipped; overflow beyond capacity is dropped.
        public Inventory Populate(BlockPosition position, IEnumerable<(string itemId, int count)> items)
        {
            var inventory = new Inventory(itemRegistry, slotCount, hotbarSlotCount: 0);
            if (items != null)
            {
                foreach ((string itemId, int count) in items)
                {
                    if (string.IsNullOrEmpty(itemId) || count <= 0)
                        continue;
                    var id = new ItemId(itemId);
                    if (!itemRegistry.TryGet(id, out _))
                        continue;
                    inventory.TryAddAll(new ItemStack(id, count));
                }
            }
            containers[position] = inventory;
            return inventory;
        }

        // Moves every stack from the container at a position into the target inventory. Returns true if
        // the container was fully emptied (and removed); false if some items remained for lack of room.
        public bool TransferAllInto(BlockPosition position, Inventory target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!containers.TryGetValue(position, out Inventory source))
                return true; // nothing to transfer

            bool fullyEmptied = true;
            for (int i = 0; i < source.SlotCount; i++)
            {
                ItemStack stack = source.GetSlot(i);
                if (stack.IsEmpty)
                    continue;

                int canAccept = target.GetAvailableCapacity(stack.ItemId);
                if (canAccept >= stack.Count)
                {
                    target.TryAddAll(stack);
                    source.SetSlot(i, ItemStack.Empty);
                }
                else if (canAccept > 0)
                {
                    target.TryAddAll(new ItemStack(stack.ItemId, canAccept).WithDurability(stack.Durability));
                    source.SetSlot(i, new ItemStack(stack.ItemId, stack.Count - canAccept).WithDurability(stack.Durability));
                    fullyEmptied = false;
                }
                else
                {
                    fullyEmptied = false;
                }
            }

            if (fullyEmptied)
                containers.Remove(position);

            return fullyEmptied;
        }
    }
}
