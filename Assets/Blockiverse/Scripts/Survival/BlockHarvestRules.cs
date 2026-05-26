using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum HarvestToolKind
    {
        Hand,
        Chipper,
        Mallet,
        Pick
    }

    public sealed class BlockHarvestRule
    {
        public BlockHarvestRule(
            BlockId blockId,
            ItemStack drop,
            HarvestToolKind effectiveTool,
            int handWork,
            int effectiveToolWork)
        {
            if (drop.IsEmpty)
                throw new ArgumentException("Harvest rules must produce a drop.", nameof(drop));

            if (handWork <= 0)
                throw new ArgumentOutOfRangeException(nameof(handWork), "Harvest work must be positive.");

            if (effectiveToolWork <= 0 || effectiveToolWork > handWork)
                throw new ArgumentOutOfRangeException(nameof(effectiveToolWork), "Effective tool work must be positive and no slower than hand work.");

            BlockId = blockId;
            Drop = drop;
            EffectiveTool = effectiveTool;
            HandWork = handWork;
            EffectiveToolWork = effectiveToolWork;
        }

        public BlockId BlockId { get; }
        public ItemStack Drop { get; }
        public HarvestToolKind EffectiveTool { get; }
        public int HandWork { get; }
        public int EffectiveToolWork { get; }

        public int GetWorkRequired(HarvestToolKind toolKind)
        {
            return toolKind == EffectiveTool ? EffectiveToolWork : HandWork;
        }
    }

    public sealed class BlockHarvestRuleSet
    {
        readonly Dictionary<BlockId, BlockHarvestRule> rulesByBlock = new();
        readonly ItemRegistry itemRegistry;

        public BlockHarvestRuleSet(ItemRegistry itemRegistry = null)
        {
            this.itemRegistry = itemRegistry ?? ItemRegistry.CreateDefault();
        }

        public IReadOnlyCollection<BlockHarvestRule> All => rulesByBlock.Values;

        public static BlockHarvestRuleSet CreateDefault(ItemRegistry itemRegistry = null)
        {
            itemRegistry ??= ItemRegistry.CreateDefault();
            var rules = new BlockHarvestRuleSet(itemRegistry);

            rules.RegisterForBlock(BlockRegistry.MeadowTurf, HarvestToolKind.Mallet, handWork: 4, effectiveToolWork: 2);
            rules.RegisterForBlock(BlockRegistry.Loam, HarvestToolKind.Mallet, handWork: 4, effectiveToolWork: 2);
            rules.RegisterForBlock(BlockRegistry.Slate, HarvestToolKind.Pick, handWork: 8, effectiveToolWork: 3);
            rules.RegisterForBlock(BlockRegistry.Timber, HarvestToolKind.Chipper, handWork: 6, effectiveToolWork: 2);
            rules.RegisterForBlock(BlockRegistry.Leafmass, HarvestToolKind.Chipper, handWork: 3, effectiveToolWork: 1);
            rules.RegisterForBlock(BlockRegistry.Clearstone, HarvestToolKind.Pick, handWork: 8, effectiveToolWork: 3);
            rules.RegisterForBlock(BlockRegistry.Coalstone, HarvestToolKind.Pick, handWork: 10, effectiveToolWork: 4);
            rules.RegisterForBlock(BlockRegistry.Copperstone, HarvestToolKind.Pick, handWork: 12, effectiveToolWork: 4);
            rules.RegisterForBlock(BlockRegistry.Ironstone, HarvestToolKind.Pick, handWork: 14, effectiveToolWork: 5);
            rules.RegisterForBlock(BlockRegistry.Workbench, HarvestToolKind.Mallet, handWork: 6, effectiveToolWork: 2);
            rules.RegisterForBlock(BlockRegistry.Torchbud, HarvestToolKind.Hand, handWork: 2, effectiveToolWork: 2);
            rules.RegisterForBlock(BlockRegistry.StorageCrate, HarvestToolKind.Mallet, handWork: 6, effectiveToolWork: 2);

            return rules;
        }

        public void Register(BlockHarvestRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            if (rulesByBlock.ContainsKey(rule.BlockId))
                throw new InvalidOperationException($"A harvest rule is already registered for block: {rule.BlockId}");

            itemRegistry.Get(rule.Drop.ItemId);
            rulesByBlock.Add(rule.BlockId, rule);
        }

        public BlockHarvestRule Get(BlockId blockId)
        {
            if (!rulesByBlock.TryGetValue(blockId, out BlockHarvestRule rule))
                throw new KeyNotFoundException($"No harvest rule is registered for block: {blockId}");

            return rule;
        }

        public bool TryGet(BlockId blockId, out BlockHarvestRule rule)
        {
            return rulesByBlock.TryGetValue(blockId, out rule);
        }

        public static HarvestToolKind GetToolKind(ItemStack equippedItem)
        {
            if (equippedItem.IsEmpty)
                return HarvestToolKind.Hand;

            return equippedItem.ItemId switch
            {
                ItemId.Chipper => HarvestToolKind.Chipper,
                ItemId.Mallet => HarvestToolKind.Mallet,
                ItemId.Pick => HarvestToolKind.Pick,
                _ => HarvestToolKind.Hand
            };
        }

        void RegisterForBlock(BlockId blockId, HarvestToolKind effectiveTool, int handWork, int effectiveToolWork)
        {
            ItemStack drop = itemRegistry.CreateDropForBlock(blockId);
            Register(new BlockHarvestRule(blockId, drop, effectiveTool, handWork, effectiveToolWork));
        }
    }
}
