using System;
using System.Collections.Generic;
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
            ItemStack[] drops,
            HarvestToolKind usedTool,
            HarvestToolKind effectiveTool,
            int workRequired)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            BlockId = blockId;
            this.drops = NormalizeDrops(drop, drops);
            Drop = this.drops.Length > 0 ? this.drops[0] : drop;
            UsedTool = usedTool;
            EffectiveTool = effectiveTool;
            WorkRequired = workRequired;
        }

        readonly ItemStack[] drops;

        public bool Succeeded { get; }
        public BlockHarvestFailureReason FailureReason { get; }
        public BlockId BlockId { get; }
        public ItemStack Drop { get; }
        public IReadOnlyList<ItemStack> Drops => drops ?? Array.Empty<ItemStack>();
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
                null,
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
                rolledDrop.IsEmpty ? null : new[] { rolledDrop },
                usedTool,
                rule.EffectiveTool,
                rule.GetMineTicks(usedTool, toolTier));
        }

        public static BlockHarvestResult Success(BlockHarvestRule rule, ItemStack[] rolledDrops, HarvestToolKind usedTool, int toolTier = 1)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            return new BlockHarvestResult(
                true,
                BlockHarvestFailureReason.None,
                rule.BlockId,
                rule.Drop,
                rolledDrops,
                usedTool,
                rule.EffectiveTool,
                rule.GetMineTicks(usedTool, toolTier));
        }

        public static BlockHarvestResult SuccessNoDrops(BlockHarvestRule rule, HarvestToolKind usedTool, int toolTier = 1)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            return new BlockHarvestResult(
                true,
                BlockHarvestFailureReason.None,
                rule.BlockId,
                ItemStack.Empty,
                Array.Empty<ItemStack>(),
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

            return new BlockHarvestResult(false, failureReason, blockId, drop, null, usedTool, effectiveTool, workRequired);
        }

        static ItemStack[] NormalizeDrops(ItemStack fallbackDrop, ItemStack[] candidateDrops)
        {
            if (candidateDrops != null && candidateDrops.Length > 0)
            {
                int nonEmptyCount = 0;
                for (int i = 0; i < candidateDrops.Length; i++)
                {
                    if (!candidateDrops[i].IsEmpty)
                        nonEmptyCount++;
                }

                if (nonEmptyCount > 0)
                {
                    var normalized = new ItemStack[nonEmptyCount];
                    int next = 0;
                    for (int i = 0; i < candidateDrops.Length; i++)
                    {
                        if (!candidateDrops[i].IsEmpty)
                            normalized[next++] = candidateDrops[i];
                    }

                    return normalized;
                }
            }

            return fallbackDrop.IsEmpty ? Array.Empty<ItemStack>() : new[] { fallbackDrop };
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
            this.itemRegistry  = itemRegistry  ?? ItemRegistry.Default;
            this.blockRegistry = blockRegistry ?? BlockRegistry.Default;
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
            if (result.Drops.Count > 0 &&
                harvestRules.TryGet(result.BlockId, out BlockHarvestRule rule) &&
                rule.Table != null)
            {
                HarvestToolKind usedTool = BlockHarvestRuleSet.GetToolKind(equippedItem, itemRegistry);
                ItemStack[] rolledDrops = RollDrops(rule, usedTool);
                result = BlockHarvestResult.Success(rule, rolledDrops, usedTool, GetToolTier(equippedItem));
            }

            return ApplyHarvestToInventory(result, world, inventory, position, equippedItem, equippedSlotIndex);
        }

        public BlockHarvestResult TryHarvestToGround(
            VoxelWorld world,
            Inventory inventory,
            BlockPosition position,
            ItemStack equippedItem,
            GroundItemStore groundItems,
            float dropX,
            float dropY,
            float dropZ,
            long worldTick,
            string droppedByPlayerId = null,
            int equippedSlotIndex = -1)
        {
            if (groundItems == null)
                throw new ArgumentNullException(nameof(groundItems));

            BlockHarvestResult result = TryPreviewHarvest(world, inventory, position, equippedItem, allowGroundDrops: true);

            if (!result.Succeeded)
                return result;

            if (result.Drops.Count > 0 &&
                harvestRules.TryGet(result.BlockId, out BlockHarvestRule rule) &&
                rule.Table != null)
            {
                HarvestToolKind usedTool = BlockHarvestRuleSet.GetToolKind(equippedItem, itemRegistry);
                ItemStack[] rolledDrops = RollDrops(rule, usedTool);
                result = BlockHarvestResult.Success(rule, rolledDrops, usedTool, GetToolTier(equippedItem));
            }

            return ApplyHarvestToInventoryOrGround(
                result,
                world,
                inventory,
                position,
                equippedItem,
                equippedSlotIndex,
                groundItems,
                dropX,
                dropY,
                dropZ,
                worldTick,
                droppedByPlayerId);
        }

        // Rolls the final drop for an already-validated harvest, applying the rule's drop table
        // (Sickle double-roll / Carver full yield) when present, else the fixed base drop. Used by the
        // authoritative survival harvest path so tool-action bonuses apply there too — not only in the
        // local TryHarvest helper. Capacity for the maximum was already checked during preview.
        public ItemStack RollHarvestDrop(BlockId blockId, HarvestToolKind usedTool)
        {
            ItemStack[] drops = RollHarvestDrops(blockId, usedTool);
            return drops.Length > 0 ? drops[0] : ItemStack.Empty;
        }

        public ItemStack[] RollHarvestDrops(BlockId blockId, HarvestToolKind usedTool)
        {
            if (!harvestRules.TryGet(blockId, out BlockHarvestRule rule))
                return Array.Empty<ItemStack>();
            return rule.Table != null ? RollDrops(rule, usedTool) : new[] { rule.Drop };
        }

        public ItemStack[] RollHarvestDrops(BlockHarvestResult result)
        {
            if (!result.Succeeded || result.Drops.Count == 0)
                return Array.Empty<ItemStack>();

            if (!harvestRules.TryGet(result.BlockId, out BlockHarvestRule rule))
                return Array.Empty<ItemStack>();

            return rule.Table != null ? RollDrops(rule, result.UsedTool) : new[] { rule.Drop };
        }

        BlockHarvestResult ApplyHarvestToInventory(
            BlockHarvestResult result, VoxelWorld world, Inventory inventory, BlockPosition position, ItemStack equippedItem, int equippedSlotIndex)
        {
            // Defensive: the preview already verified capacity for the maximum possible drop, so this
            // add should always succeed. If it somehow cannot, do not clear the block — that would
            // silently destroy the resource.
            if (!CanAddStacks(inventory, result.Drops))
            {
                return BlockHarvestResult.Failure(
                    BlockHarvestFailureReason.InventoryFull,
                    result.BlockId,
                    result.Drop,
                    result.UsedTool,
                    result.EffectiveTool,
                    result.WorkRequired);
            }

            foreach (ItemStack drop in result.Drops)
                inventory.TryAddAll(drop);

            world.SetBlock(position, BlockRegistry.Air);

            if (equippedSlotIndex >= 0 && equippedItem.Durability > 0)
                ApplyDurabilityCost(inventory, equippedSlotIndex, equippedItem, ComputeDurabilityCost(result, equippedItem));

            return result;
        }

        BlockHarvestResult ApplyHarvestToInventoryOrGround(
            BlockHarvestResult result,
            VoxelWorld world,
            Inventory inventory,
            BlockPosition position,
            ItemStack equippedItem,
            int equippedSlotIndex,
            GroundItemStore groundItems,
            float dropX,
            float dropY,
            float dropZ,
            long worldTick,
            string droppedByPlayerId)
        {
            world.SetBlock(position, BlockRegistry.Air);

            foreach (ItemStack drop in result.Drops)
            {
                if (drop.IsEmpty)
                    continue;

                if (!inventory.TryAddAll(drop))
                    groundItems.Spawn(drop, dropX, dropY, dropZ, worldTick, droppedByPlayerId);
            }

            if (equippedSlotIndex >= 0 && equippedItem.Durability > 0)
                ApplyDurabilityCost(inventory, equippedSlotIndex, equippedItem, ComputeDurabilityCost(result, equippedItem));

            return result;
        }

        // Maximum primary drop count the equipped tool can actually produce for this rule.
        // Only the matching effective tool can reach the table's full range; every other tool yields
        // the fixed minimum, so the capacity pre-check stays tight and avoids spurious InventoryFull
        // failures (e.g. hand-harvesting a 1–2 ResinKnot with room for exactly one).
        static ItemStack[] MaxDropsForTool(BlockHarvestRule rule, HarvestToolKind usedTool)
        {
            if (!UsesDropTable(rule, usedTool))
                return new[] { rule.Drop };

            ItemStack[] maxStacks = rule.Table.MaxStacks;
            if (!rule.Table.CanRollNoDrops || rule.Drop.IsEmpty)
                return maxStacks;

            var stacks = new ItemStack[maxStacks.Length + 1];
            Array.Copy(maxStacks, stacks, maxStacks.Length);
            stacks[^1] = rule.Drop;
            return stacks;
        }

        bool AllowsResourceDrops(BlockHarvestRule rule, HarvestToolKind usedTool, int toolTier)
        {
            if (!blockRegistry.TryGet(rule.BlockId, out BlockDefinition definition) ||
                definition.Category != BlockCategory.Resource)
            {
                return true;
            }

            return usedTool == rule.EffectiveTool && toolTier >= rule.HarvestTierMin;
        }

        bool CanAddStacks(Inventory inventory, IReadOnlyList<ItemStack> stacks)
        {
            var required = new Dictionary<ItemId, int>();
            for (int i = 0; i < stacks.Count; i++)
            {
                ItemStack stack = stacks[i];
                if (stack.IsEmpty)
                    continue;

                required.TryGetValue(stack.ItemId, out int existing);
                required[stack.ItemId] = existing + stack.Count;
            }

            if (required.Count == 0)
                return true;

            int emptySlots = 0;
            for (int slot = 0; slot < inventory.SlotCount; slot++)
            {
                ItemStack existing = inventory.GetSlot(slot);
                if (existing.IsEmpty)
                {
                    emptySlots++;
                    continue;
                }

                if (!required.TryGetValue(existing.ItemId, out int remaining) || remaining <= 0)
                    continue;

                int available = itemRegistry.Get(existing.ItemId).MaxStackSize - existing.Count;
                if (available <= 0)
                    continue;

                remaining = Math.Max(0, remaining - available);
                required[existing.ItemId] = remaining;
            }

            int neededEmptySlots = 0;
            foreach (KeyValuePair<ItemId, int> entry in required)
            {
                int remaining = entry.Value;
                if (remaining <= 0)
                    continue;

                int maxStackSize = itemRegistry.Get(entry.Key).MaxStackSize;
                neededEmptySlots += (remaining + maxStackSize - 1) / maxStackSize;
            }

            return neededEmptySlots <= emptySlots;
        }

        ItemStack[] RollDrops(BlockHarvestRule rule, HarvestToolKind usedTool)
        {
            if (usedTool == HarvestToolKind.Sickle && rule.EffectiveTool == HarvestToolKind.Sickle)
            {
                // Double-roll: roll twice, keep the primary drop with the higher count.
                ItemStack[] r1 = rule.Table.Roll(ref rngState);
                ItemStack[] r2 = rule.Table.Roll(ref rngState);
                int c1 = r1.Length > 0 ? r1[0].Count : 0;
                int c2 = r2.Length > 0 ? r2[0].Count : 0;
                return NormalizeRolledDrops(rule, c1 >= c2 ? r1 : r2);
            }
            if (usedTool == HarvestToolKind.Carver && rule.EffectiveTool == HarvestToolKind.Carver)
            {
                // Full yield: single table roll.
                ItemStack[] drops = rule.Table.Roll(ref rngState);
                return NormalizeRolledDrops(rule, drops);
            }

            if (UsesDropTable(rule, usedTool))
            {
                ItemStack[] drops = rule.Table.Roll(ref rngState);
                return NormalizeRolledDrops(rule, drops);
            }

            // Other tools: fixed minimum (rule.Drop) — full yield requires the correct tool.
            return new[] { rule.Drop };
        }

        static ItemStack[] NormalizeRolledDrops(BlockHarvestRule rule, ItemStack[] drops) =>
            drops != null && drops.Length > 0 ? drops : new[] { rule.Drop };

        static bool UsesDropTable(BlockHarvestRule rule, HarvestToolKind usedTool) =>
            rule.Table != null && usedTool == rule.EffectiveTool;

        // Durability cost the authoritative host path applies for a completed harvest (§6.3) — kept
        // public so MultiplayerSurvivalSync charges the same formula cost as the local TryHarvest.
        public int GetHarvestDurabilityCost(BlockHarvestResult result, ItemStack equippedItem) =>
            ComputeDurabilityCost(result, equippedItem);

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

        public BlockHarvestResult TryPreviewHarvest(
            VoxelWorld world,
            Inventory inventory,
            BlockPosition position,
            ItemStack equippedItem,
            bool allowGroundDrops = false)
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

            int workRequired = rule.GetMineTicks(usedTool, toolTier);
            bool allowDrops = AllowsResourceDrops(rule, usedTool, toolTier);
            ItemStack[] previewDrops = allowDrops ? MaxDropsForTool(rule, usedTool) : Array.Empty<ItemStack>();
            if (!allowGroundDrops && !CanAddStacks(inventory, previewDrops))
            {
                return BlockHarvestResult.Failure(
                    BlockHarvestFailureReason.InventoryFull,
                    blockId,
                    rule.Drop,
                    usedTool,
                    rule.EffectiveTool,
                    workRequired);
            }

            if (!allowDrops)
                return BlockHarvestResult.SuccessNoDrops(rule, usedTool, toolTier);

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
