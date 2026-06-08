using System;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum BlockHarvestFailureReason
    {
        None,
        OutOfBounds,
        AirBlock,
        UnknownBlock,
        NoHarvestRule,
        InsufficientTool,
        InventoryFull
    }

    public readonly struct BlockHarvestResult
    {
        BlockHarvestResult(
            bool succeeded,
            BlockHarvestFailureReason failureReason,
            BlockId blockId,
            ItemStack drop,
            HarvestToolKind usedTool,
            HarvestToolKind effectiveTool,
            int workRequired)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            BlockId = blockId;
            Drop = drop;
            UsedTool = usedTool;
            EffectiveTool = effectiveTool;
            WorkRequired = workRequired;
        }

        public bool Succeeded { get; }
        public BlockHarvestFailureReason FailureReason { get; }
        public BlockId BlockId { get; }
        public ItemStack Drop { get; }
        public HarvestToolKind UsedTool { get; }
        public HarvestToolKind EffectiveTool { get; }
        public int WorkRequired { get; }
        public bool UsedEffectiveTool => UsedTool == EffectiveTool;

        public static BlockHarvestResult Success(BlockHarvestRule rule, HarvestToolKind usedTool, int toolTier = 1)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            return new BlockHarvestResult(
                true,
                BlockHarvestFailureReason.None,
                rule.BlockId,
                rule.Drop,
                usedTool,
                rule.EffectiveTool,
                rule.GetMineTicks(usedTool, toolTier));
        }

        public static BlockHarvestResult Failure(
            BlockHarvestFailureReason failureReason,
            BlockId blockId = default,
            ItemStack drop = default,
            HarvestToolKind usedTool = HarvestToolKind.Hand,
            HarvestToolKind effectiveTool = HarvestToolKind.Hand,
            int workRequired = 0)
        {
            if (failureReason == BlockHarvestFailureReason.None)
                throw new ArgumentException("Failure results must include a concrete reason.", nameof(failureReason));

            return new BlockHarvestResult(false, failureReason, blockId, drop, usedTool, effectiveTool, workRequired);
        }
    }

    public sealed class ResourceHarvestService
    {
        readonly BlockRegistry blockRegistry;
        readonly ItemRegistry itemRegistry;
        readonly BlockHarvestRuleSet harvestRules;

        public ResourceHarvestService(
            BlockRegistry blockRegistry = null,
            ItemRegistry itemRegistry = null,
            BlockHarvestRuleSet harvestRules = null)
        {
            this.itemRegistry  = itemRegistry  ?? ItemRegistry.CreateDefault();
            this.blockRegistry = blockRegistry ?? BlockRegistry.CreateDefault();
            this.harvestRules  = harvestRules  ?? BlockHarvestRuleSet.CreateDefault(this.itemRegistry, this.blockRegistry);
        }

        public BlockHarvestResult TryHarvest(VoxelWorld world, Inventory inventory, BlockPosition position, ItemStack equippedItem, int equippedSlotIndex = -1)
        {
            BlockHarvestResult result = TryPreviewHarvest(world, inventory, position, equippedItem);

            if (!result.Succeeded)
                return result;

            inventory.TryAddAll(result.Drop);
            world.SetBlock(position, BlockRegistry.Air);

            if (equippedSlotIndex >= 0 && equippedItem.Durability > 0)
                ApplyDurabilityCost(inventory, equippedSlotIndex, equippedItem, ComputeDurabilityCost(result, equippedItem));

            return result;
        }

        int ComputeDurabilityCost(BlockHarvestResult result, ItemStack equippedItem)
        {
            BlockCategory category = BlockCategory.Terrain;
            int harvestTierMin = 0;
            if (blockRegistry.TryGet(result.BlockId, out BlockDefinition def))
            {
                category = def.Category;
                harvestTierMin = def.HarvestTierMin;
            }

            bool correctTool = result.UsedTool == result.EffectiveTool;
            bool sufficientTier = GetToolTier(equippedItem) >= harvestTierMin;
            return MiningFormula.DurabilityCost(category, harvestTierMin, correctTool, sufficientTier);
        }

        public BlockHarvestResult TryPreviewHarvest(VoxelWorld world, Inventory inventory, BlockPosition position, ItemStack equippedItem)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            if (!world.Bounds.Contains(position))
                return BlockHarvestResult.Failure(BlockHarvestFailureReason.OutOfBounds);

            BlockId blockId = world.GetBlock(position);
            if (blockId == BlockRegistry.Air)
                return BlockHarvestResult.Failure(BlockHarvestFailureReason.AirBlock, blockId);

            if (!blockRegistry.TryGet(blockId, out _))
                return BlockHarvestResult.Failure(BlockHarvestFailureReason.UnknownBlock, blockId);

            HarvestToolKind usedTool = BlockHarvestRuleSet.GetToolKind(equippedItem, itemRegistry);
            int toolTier = GetToolTier(equippedItem);

            if (!harvestRules.TryGet(blockId, out BlockHarvestRule rule))
                return BlockHarvestResult.Failure(BlockHarvestFailureReason.NoHarvestRule, blockId, usedTool: usedTool);

            if (rule.HarvestTierMin > 0 && (usedTool != rule.EffectiveTool || toolTier < rule.HarvestTierMin))
                return BlockHarvestResult.Failure(BlockHarvestFailureReason.InsufficientTool, blockId, usedTool: usedTool, effectiveTool: rule.EffectiveTool);

            int workRequired = rule.GetMineTicks(usedTool, toolTier);
            if (inventory.GetAvailableCapacity(rule.Drop.ItemId) < rule.Drop.Count)
            {
                return BlockHarvestResult.Failure(
                    BlockHarvestFailureReason.InventoryFull,
                    blockId,
                    rule.Drop,
                    usedTool,
                    rule.EffectiveTool,
                    workRequired);
            }

            return BlockHarvestResult.Success(rule, usedTool, toolTier);
        }

        int GetToolTier(ItemStack equippedItem)
        {
            if (!equippedItem.IsEmpty && itemRegistry.TryGet(equippedItem.ItemId, out ItemDefinition def))
                return def.ToolTier;
            return 0;
        }

        static void ApplyDurabilityCost(Inventory inventory, int slotIndex, ItemStack equippedItem, int cost)
        {
            int remaining = equippedItem.Durability - Math.Max(1, cost);
            inventory.SetSlot(slotIndex, remaining > 0
                ? equippedItem.WithDurability(remaining)
                : ItemStack.Empty);
        }
    }
}
