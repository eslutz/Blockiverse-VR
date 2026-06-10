using System;

namespace Blockiverse.Survival
{
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public ItemStack(ItemId itemId, int count)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Item stack count must be positive. Use ItemStack.Empty for empty slots.");

            if (itemId.IsNone)
                throw new ArgumentException("Empty item IDs cannot have a positive count.", nameof(itemId));

            ItemId = itemId;
            Count = count;
            Durability = 0;
        }

        ItemStack(ItemId itemId, int count, int durability)
        {
            ItemId = itemId;
            Count = count;
            Durability = durability;
        }

        public static ItemStack Empty => default;

        public ItemId ItemId { get; }
        public int Count { get; }
        public int Durability { get; }
        public bool IsEmpty => Count == 0;

        public bool CanStackWith(ItemStack other)
        {
            return !IsEmpty && !other.IsEmpty && ItemId == other.ItemId
                && Durability == 0 && other.Durability == 0;
        }

        public ItemStack WithCount(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Item stack count cannot be negative.");

            if (count == 0)
                return Empty;

            return new ItemStack(ItemId, count, Durability);
        }

        public ItemStack WithDurability(int durability)
        {
            if (IsEmpty)
                return Empty;

            return new ItemStack(ItemId, Count, durability);
        }

        public bool Equals(ItemStack other)
        {
            return ItemId == other.ItemId && Count == other.Count && Durability == other.Durability;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemStack other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (ItemId.GetHashCode() * 397) ^ Count;
                return (hash * 397) ^ Durability;
            }
        }

        public override string ToString()
        {
            if (IsEmpty) return "Empty";
            return Durability > 0 ? $"{ItemId} x{Count} [{Durability}dur]" : $"{ItemId} x{Count}";
        }

        public static bool operator ==(ItemStack left, ItemStack right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ItemStack left, ItemStack right)
        {
            return !left.Equals(right);
        }
    }
}
