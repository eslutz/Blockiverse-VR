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

        public static BlockHarvestResult Success(BlockHarvestRule rule, ItemStack rolledDrop, HarvestToolKind usedTool, int toolTier = 1)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            return new BlockHarvestResult(
                true,
                BlockHarvestFailureReason.None,
                rule.BlockId,
                rolledDrop.IsEmpty ? rule.Drop : rolledDrop,
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
        uint rngState;

        public ResourceHarvestService(
            BlockRegistry blockRegistry = null,
            ItemRegistry itemRegistry = null,
            BlockHarvestRuleSet harvestRules = null,
            uint rngSeed = 0)
        {
            this.itemRegistry  = itemRegistry  ?? ItemRegistry.CreateDefault();
            this.blockRegistry = blockRegistry ?? BlockRegistry.CreateDefault();
            this.harvestRules  = harvestRules  ?? BlockHarvestRuleSet.CreateDefault(this.itemRegistry, this.blockRegistry);
            // Drop-table rolls are deterministic only when an explicit rngSeed is supplied (tests and
            // any future replay/save use). The default falls back to a wall-clock seed; this is safe
            // because harvest rolls are host-authoritative (only the host rolls, then broadcasts an
            // inventory snapshot), so clients never need to reproduce the host's roll locally.
            rngState = rngSeed == 0 ? (uint)Math.Abs(Environment.TickCount) | 1u : rngSeed;
        }

        public BlockHarvestResult TryHarvest(VoxelWorld world, Inventory inventory, BlockPosition position, ItemStack equippedItem, int equippedSlotIndex = -1)
        {
            BlockHarvestResult result = TryPreviewHarvest(world, inventory, position, equippedItem);

            if (!result.Succeeded)
                return result;

            // Apply variable drop table when the rule has one (Sickle double-roll, Carver full yield).
            if (harvestRules.TryGet(result.BlockId, out BlockHarvestRule rule) && rule.Table != null)
            {
                HarvestToolKind usedTool = BlockHarvestRuleSet.GetToolKind(equippedItem, itemRegistry);
                ItemStack rolledDrop = RollDrop(rule, usedTool);
                result = BlockHarvestResult.Success(rule, rolledDrop, usedTool, GetToolTier(equippedItem));
            }

            return ApplyHarvestToInventory(result, world, inventory, position, equippedItem, equippedSlotIndex);
        }

        // Rolls the final drop for an already-validated harvest, applying the rule's drop table
        // (Sickle double-roll / Carver full yield) when present, else the fixed base drop. Used by the
        // authoritative survival harvest path so tool-action bonuses apply there too — not only in the
        // local TryHarvest helper. Capacity for the maximum was already checked during preview.
        public ItemStack RollHarvestDrop(BlockId blockId, HarvestToolKind usedTool)
        {
            if (!harvestRules.TryGet(blockId, out BlockHarvestRule rule))
                return ItemStack.Empty;
            return rule.Table != null ? RollDrop(rule, usedTool) : rule.Drop;
        }

        BlockHarvestResult ApplyHarvestToInventory(
            BlockHarvestResult result, VoxelWorld world, Inventory inventory, BlockPosition position, ItemStack equippedItem, int equippedSlotIndex)
        {

            // Defensive: the preview already verified capacity for the maximum possible drop, so this
            // add should always succeed. If it somehow cannot, do not clear the block — that would
            // silently destroy the resource.
            if (!inventory.TryAddAll(result.Drop))
            {
                return BlockHarvestResult.Failure(
                    BlockHarvestFailureReason.InventoryFull,
                    result.BlockId,
                    result.Drop,
                    result.UsedTool,
                    result.EffectiveTool,
                    result.WorkRequired);
            }

            world.SetBlock(position, BlockRegistry.Air);

            if (equippedSlotIndex >= 0 && equippedItem.Durability > 0)
                ApplyDurabilityCost(inventory, equippedSlotIndex, equippedItem, ComputeDurabilityCost(result, equippedItem));

            return result;
        }

        // Maximum drop count the equipped tool can actually produce for this rule.
        // Only the matching bonus tool (Sickle/Carver) can reach the table's full range; every other
        // tool yields the fixed minimum, so the capacity pre-check stays tight and avoids spurious
        // InventoryFull failures (e.g. hand-harvesting a 1–2 ResinKnot with room for exactly one).
        static int MaxDropCountForTool(BlockHarvestRule rule, HarvestToolKind usedTool)
        {
            if (rule.Table == null)
                return rule.Drop.Count;

            bool toolGetsBonus =
                (usedTool == HarvestToolKind.Sickle && rule.EffectiveTool == HarvestToolKind.Sickle) ||
                (usedTool == HarvestToolKind.Carver && rule.EffectiveTool == HarvestToolKind.Carver);

            return toolGetsBonus ? rule.MaxDropCount : rule.Drop.Count;
        }

        ItemStack RollDrop(BlockHarvestRule rule, HarvestToolKind usedTool)
        {
            if (usedTool == HarvestToolKind.Sickle && rule.EffectiveTool == HarvestToolKind.Sickle)
            {
                // Double-roll: roll twice, keep the primary drop with the higher count.
                ItemStack[] r1 = rule.Table.Roll(ref rngState);
                ItemStack[] r2 = rule.Table.Roll(ref rngState);
                int c1 = r1.Length > 0 ? r1[0].Count : 0;
                int c2 = r2.Length > 0 ? r2[0].Count : 0;
                return c1 >= c2 ? (r1.Length > 0 ? r1[0] : rule.Drop) : (r2.Length > 0 ? r2[0] : rule.Drop);
            }
            if (usedTool == HarvestToolKind.Carver && rule.EffectiveTool == HarvestToolKind.Carver)
            {
                // Full yield: single table roll.
                ItemStack[] drops = rule.Table.Roll(ref rngState);
                return drops.Length > 0 ? drops[0] : rule.Drop;
            }
            // Other tools: fixed minimum (rule.Drop) — full yield requires the correct tool.
            return rule.Drop;
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
            if (inventory.GetAvailableCapacity(rule.Drop.ItemId) < MaxDropCountForTool(rule, usedTool))
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
