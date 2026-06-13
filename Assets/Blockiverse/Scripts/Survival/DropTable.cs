using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    // A single probabilistic drop entry: rolls count in [Min, Max] when Chance passes.
    public readonly struct DropTableEntry
    {
        public readonly ItemId  ItemId;
        public readonly int     Min;
        public readonly int     Max;
        public readonly float   Chance; // 0–1; 1 = always drops

        public DropTableEntry(ItemId itemId, int min, int max, float chance = 1f)
        {
            ItemId = itemId;
            Min    = min;
            Max    = max;
            Chance = chance;
        }

        // Returns Empty if the chance roll fails; otherwise a stack with count in [Min, Max].
        public ItemStack Roll(ref uint rng)
        {
            if (Chance < 1f)
            {
                Advance(ref rng);
                if (rng % 1000 >= (uint)(Chance * 1000)) return ItemStack.Empty;
            }
            Advance(ref rng);
            int count = Min + (Max > Min ? (int)(rng % (uint)(Max - Min + 1)) : 0);
            return new ItemStack(ItemId, count);
        }

        static void Advance(ref uint rng)
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
        }
    }

    // Rolls one or more DropTableEntry instances; each entry is independent.
    // Entry [0] is the primary drop; remaining entries are secondary (bonus) drops.
    public sealed class DropTable
    {
        readonly DropTableEntry[] entries;

        public DropTable(params DropTableEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
                throw new ArgumentException("DropTable requires at least one entry.", nameof(entries));
            this.entries = entries;
        }

        // Maximum count of the primary (first) entry — used for inventory-capacity pre-checks.
        public ItemId PrimaryItemId => entries[0].ItemId;
        public int PrimaryMaxCount => entries[0].Max;
        public bool CanRollNoDrops
        {
            get
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Chance >= 1f)
                        return false;
                }

                return true;
            }
        }

        public ItemStack[] MaxStacks
        {
            get
            {
                var stacks = new ItemStack[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                    stacks[i] = new ItemStack(entries[i].ItemId, entries[i].Max);
                return stacks;
            }
        }

        // Roll all entries and return every non-empty result. Primary drop is [0] when present.
        public ItemStack[] Roll(ref uint rng)
        {
            var results = new List<ItemStack>(entries.Length);
            foreach (var entry in entries)
            {
                ItemStack stack = entry.Roll(ref rng);
                if (!stack.IsEmpty) results.Add(stack);
            }
            return results.ToArray();
        }
    }
}
