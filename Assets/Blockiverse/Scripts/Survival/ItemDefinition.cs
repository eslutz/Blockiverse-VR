using System;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum ItemKind
    {
        None,
        Resource,
        Placeable,
        Tool,
        Consumable
    }

    public sealed class ItemDefinition
    {
        public ItemDefinition(ItemId id, string name, ItemKind kind, int maxStackSize, BlockId? blockId = null, HarvestToolKind toolClass = HarvestToolKind.Hand, int toolTier = 0, int maxDurability = 0)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Item names must be non-empty.", nameof(name));

            if (id.IsNone)
            {
                if (kind != ItemKind.None)
                    throw new ArgumentException("The empty item must use ItemKind.None.", nameof(kind));

                if (maxStackSize != 0)
                    throw new ArgumentOutOfRangeException(nameof(maxStackSize), "The empty item must have a stack size of zero.");
            }
            else
            {
                if (kind == ItemKind.None)
                    throw new ArgumentException("Non-empty items must declare a concrete item kind.", nameof(kind));

                if (maxStackSize <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maxStackSize), "Stack size must be positive for non-empty items.");
            }

            Id = id;
            Name = name;
            Kind = kind;
            MaxStackSize = maxStackSize;
            BlockId = blockId;
            ToolClass = toolClass;
            ToolTier = toolTier;
            MaxDurability = maxDurability;
        }

        public ItemId Id { get; }
        public string Name { get; }
        public ItemKind Kind { get; }
        public int MaxStackSize { get; }
        public BlockId? BlockId { get; }
        public HarvestToolKind ToolClass { get; }
        public int ToolTier { get; }
        public int MaxDurability { get; }
        public bool IsStackable => MaxStackSize > 1;
        public bool HasBlockMapping => BlockId.HasValue;
    }
}
