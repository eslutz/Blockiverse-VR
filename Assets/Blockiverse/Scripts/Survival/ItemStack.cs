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
        }

        public static ItemStack Empty => default;

        public ItemId ItemId { get; }
        public int Count { get; }
        public bool IsEmpty => Count == 0;

        public bool CanStackWith(ItemStack other)
        {
            return !IsEmpty && !other.IsEmpty && ItemId == other.ItemId;
        }

        public ItemStack WithCount(int count)
        {
            if (count == 0)
                return Empty;

            return new ItemStack(ItemId, count);
        }

        public bool Equals(ItemStack other)
        {
            return ItemId == other.ItemId && Count == other.Count;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemStack other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ItemId.GetHashCode() * 397) ^ Count;
            }
        }

        public override string ToString()
        {
            return IsEmpty ? "Empty" : $"{ItemId} x{Count}";
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
