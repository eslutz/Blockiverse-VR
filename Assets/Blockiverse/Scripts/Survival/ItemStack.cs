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
            ContentId = ItemId.None;
        }

        public ItemStack(ItemId itemId, int count, ItemId contentId)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Item stack count must be positive. Use ItemStack.Empty for empty slots.");

            if (itemId.IsNone)
                throw new ArgumentException("Empty item IDs cannot have a positive count.", nameof(itemId));

            ItemId = itemId;
            Count = count;
            Durability = 0;
            ContentId = contentId;
        }

        ItemStack(ItemId itemId, int count, int durability, ItemId contentId)
        {
            ItemId = itemId;
            Count = count;
            Durability = durability;
            ContentId = contentId;
        }

        public static ItemStack Empty => default;

        public ItemId ItemId { get; }
        public int Count { get; }
        public int Durability { get; }

        /// <summary>
        /// Discriminator for container contents (e.g. fluid type in a bucket, loot roll in a crate).
        /// Stacks only merge if their ContentId is identical.
        /// </summary>
        public ItemId ContentId { get; }

        public bool IsEmpty => Count == 0;

        public bool CanStackWith(ItemStack other)
        {
            return !IsEmpty && !other.IsEmpty && ItemId == other.ItemId
                && Durability == 0 && other.Durability == 0
                && ContentId == other.ContentId;
        }

        public ItemStack WithCount(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Item stack count cannot be negative.");

            if (count == 0)
                return Empty;

            return new ItemStack(ItemId, count, Durability, ContentId);
        }

        public ItemStack WithDurability(int durability)
        {
            if (IsEmpty)
                return Empty;

            return new ItemStack(ItemId, Count, durability, ContentId);
        }

        public ItemStack WithContent(ItemId contentId)
        {
            if (IsEmpty)
                return Empty;

            return new ItemStack(ItemId, Count, Durability, contentId);
        }

        public bool Equals(ItemStack other)
        {
            return ItemId == other.ItemId && Count == other.Count && Durability == other.Durability && ContentId == other.ContentId;
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
                hash = (hash * 397) ^ Durability;
                return (hash * 397) ^ ContentId.GetHashCode();
            }
        }

        public override string ToString()
        {
            if (IsEmpty) return "Empty";
            string desc = Durability > 0 ? $"{ItemId} x{Count} [{Durability}dur]" : $"{ItemId} x{Count}";
            if (!ContentId.IsNone) desc += $" ({ContentId})";
            return desc;
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
