using System;

namespace Blockiverse.Survival
{
    public sealed class Inventory
    {
        public const int DefaultSlotCount = 44;
        public const int DefaultHotbarSlotCount = 10;
        public const int MaxSlotCount = 128;

        readonly ItemRegistry registry;
        readonly ItemStack[] slots;

        public Inventory(ItemRegistry registry = null, int slotCount = DefaultSlotCount, int hotbarSlotCount = DefaultHotbarSlotCount)
        {
            if (slotCount <= 0 || slotCount > MaxSlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotCount), $"Inventory must have between 1 and {MaxSlotCount} slots.");

            if (hotbarSlotCount < 0 || hotbarSlotCount > slotCount)
                throw new ArgumentOutOfRangeException(nameof(hotbarSlotCount), "Hotbar slots must fit inside the inventory.");

            this.registry = registry ?? ItemRegistry.CreateDefault();
            slots = new ItemStack[slotCount];
            HotbarSlotCount = hotbarSlotCount;
        }

        public int SlotCount => slots.Length;
        public int HotbarSlotCount { get; }

        public ItemStack GetSlot(int slotIndex)
        {
            ValidateSlotIndex(slotIndex);
            return slots[slotIndex];
        }

        public void SetSlot(int slotIndex, ItemStack stack)
        {
            ValidateSlotIndex(slotIndex);
            ValidateSlotStack(stack);
            slots[slotIndex] = stack;
        }

        public void ClearSlot(int slotIndex)
        {
            ValidateSlotIndex(slotIndex);
            slots[slotIndex] = ItemStack.Empty;
        }

        public ItemStack Add(ItemStack stack)
        {
            if (stack.IsEmpty)
                return ItemStack.Empty;

            ItemDefinition definition = registry.Get(stack.ItemId);
            int remaining = stack.Count;

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                ItemStack existing = slots[i];
                if (existing.IsEmpty || existing.ItemId != stack.ItemId)
                    continue;

                int available = definition.MaxStackSize - existing.Count;
                if (available <= 0)
                    continue;

                int moved = Math.Min(available, remaining);
                slots[i] = existing.WithCount(existing.Count + moved);
                remaining -= moved;
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty)
                    continue;

                int moved = Math.Min(definition.MaxStackSize, remaining);
                slots[i] = stack.WithCount(moved);
                remaining -= moved;
            }

            return remaining > 0 ? stack.WithCount(remaining) : ItemStack.Empty;
        }

        public bool TryAddAll(ItemStack stack)
        {
            if (stack.IsEmpty)
                return true;

            ValidateNonEmptyKnownItem(stack.ItemId);

            if (GetAvailableCapacity(stack.ItemId) < stack.Count)
                return false;

            Add(stack);
            return true;
        }

        public ItemStack SplitSlot(int slotIndex, int count)
        {
            ValidateSlotIndex(slotIndex);
            ValidatePositiveCount(count, nameof(count));

            ItemStack existing = slots[slotIndex];
            if (existing.IsEmpty)
                throw new InvalidOperationException("Cannot split an empty inventory slot.");

            if (count > existing.Count)
                throw new InvalidOperationException("Cannot split more items than the slot contains.");

            var split = existing.WithCount(count);
            slots[slotIndex] = existing.Count == count ? ItemStack.Empty : existing.WithCount(existing.Count - count);
            return split;
        }

        public bool Remove(ItemId itemId, int count)
        {
            ValidatePositiveCount(count, nameof(count));
            ValidateNonEmptyKnownItem(itemId);

            if (CountOf(itemId) < count)
                return false;

            int remaining = count;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                ItemStack existing = slots[i];
                if (existing.IsEmpty || existing.ItemId != itemId)
                    continue;

                int removed = Math.Min(existing.Count, remaining);
                slots[i] = existing.Count == removed ? ItemStack.Empty : existing.WithCount(existing.Count - removed);
                remaining -= removed;
            }

            return true;
        }

        public int CountOf(ItemId itemId)
        {
            ValidateNonEmptyKnownItem(itemId);

            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i];
                if (!stack.IsEmpty && stack.ItemId == itemId)
                    count += stack.Count;
            }

            return count;
        }

        public int GetAvailableCapacity(ItemId itemId)
        {
            ItemDefinition definition = ValidateNonEmptyKnownItem(itemId);
            int capacity = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack stack = slots[i];
                if (stack.IsEmpty)
                {
                    capacity += definition.MaxStackSize;
                }
                else if (stack.ItemId == itemId && stack.Count < definition.MaxStackSize)
                {
                    capacity += definition.MaxStackSize - stack.Count;
                }
            }

            return capacity;
        }

        void ValidateSlotIndex(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                throw new ArgumentOutOfRangeException(nameof(slotIndex), "Inventory slot index is out of range.");
        }

        void ValidateSlotStack(ItemStack stack)
        {
            if (stack.IsEmpty)
                return;

            ItemDefinition definition = registry.Get(stack.ItemId);
            if (stack.Count > definition.MaxStackSize)
                throw new InvalidOperationException($"Slot stack count {stack.Count} exceeds max stack size {definition.MaxStackSize} for {stack.ItemId}.");
        }

        ItemDefinition ValidateNonEmptyKnownItem(ItemId itemId)
        {
            if (itemId.IsNone)
                throw new ArgumentException("Empty item IDs are not valid inventory items.", nameof(itemId));

            return registry.Get(itemId);
        }

        static void ValidatePositiveCount(int count, string parameterName)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "Count must be positive.");
        }
    }
}
