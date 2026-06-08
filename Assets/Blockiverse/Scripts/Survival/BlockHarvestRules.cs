using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum HarvestToolKind
    {
        Hand,
        Delver,
        Spade,
        Feller,
        Sickle,
        Mallet,
        Carver,
        Tiller
    }

    public sealed class BlockHarvestRule
    {
        static readonly int[] BaseWorkByClass   = { 4, 8, 16, 32 };
        static readonly float[] SpeedByClass    = { 2f, 2f, 2.5f, 4f };

        public BlockHarvestRule(
            BlockId blockId,
            ItemStack drop,
            HarvestToolKind effectiveTool,
            BlockHardnessClass hardnessClass,
            int harvestTierMin)
        {
            if (drop.IsEmpty)
                throw new ArgumentException("Harvest rules must produce a drop.", nameof(drop));

            BlockId = blockId;
            Drop = drop;
            EffectiveTool = effectiveTool;
            HardnessClass = hardnessClass;
            HarvestTierMin = harvestTierMin;
        }

        public BlockId BlockId { get; }
        public ItemStack Drop { get; }
        public HarvestToolKind EffectiveTool { get; }
        public BlockHardnessClass HardnessClass { get; }
        public int HarvestTierMin { get; }

        public int HandWork => BaseWorkByClass[(int)HardnessClass];

        public int GetWorkRequired(HarvestToolKind toolKind) => GetWorkRequired(toolKind, toolTier: 1);

        public int GetWorkRequired(HarvestToolKind toolKind, int toolTier)
        {
            int baseWork = BaseWorkByClass[(int)HardnessClass];
            if (toolKind != EffectiveTool || toolTier < HarvestTierMin)
                return baseWork;

            float tierMult  = 1f + (toolTier - HarvestTierMin);
            float classBonus = SpeedByClass[(int)HardnessClass];
            return Math.Max(1, (int)(baseWork / (tierMult * classBonus)));
        }
    }

    public sealed class BlockHarvestRuleSet
    {
        readonly Dictionary<BlockId, BlockHarvestRule> rulesByBlock = new();
        readonly ItemRegistry itemRegistry;
        readonly BlockRegistry blockRegistry;

        public BlockHarvestRuleSet(ItemRegistry itemRegistry = null, BlockRegistry blockRegistry = null)
        {
            this.itemRegistry  = itemRegistry  ?? ItemRegistry.CreateDefault();
            this.blockRegistry = blockRegistry ?? BlockRegistry.CreateDefault();
        }

        public IReadOnlyCollection<BlockHarvestRule> All => rulesByBlock.Values;

        public static BlockHarvestRuleSet CreateDefault(ItemRegistry itemRegistry = null, BlockRegistry blockRegistry = null)
        {
            itemRegistry  ??= ItemRegistry.CreateDefault();
            blockRegistry ??= BlockRegistry.CreateDefault();
            var rules = new BlockHarvestRuleSet(itemRegistry, blockRegistry);

            rules.RegisterForBlock(BlockRegistry.MeadowTurf,        HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.LooseLoam,          HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.Graystone,          HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BranchwoodLog,      HarvestToolKind.Feller);
            rules.RegisterForBlock(BlockRegistry.Leafmoss,           HarvestToolKind.Feller);
            rules.RegisterForBlock(BlockRegistry.LumenQuartzCluster, HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.EmbercoalSeam,      HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.RosycopperBloom,    HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.RustcoreOre,        HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BuildTable,         HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.Glowwick,           HarvestToolKind.Hand);
            rules.RegisterForBlock(BlockRegistry.StorageCrate,       HarvestToolKind.Mallet);

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

        public HarvestToolKind GetToolKind(ItemStack equippedItem)
        {
            if (equippedItem.IsEmpty)
                return HarvestToolKind.Hand;

            if (itemRegistry.TryGet(equippedItem.ItemId, out ItemDefinition def) && def.ToolClass != HarvestToolKind.Hand)
                return def.ToolClass;

            return HarvestToolKind.Hand;
        }

        public static HarvestToolKind GetToolKind(ItemStack equippedItem, ItemRegistry itemRegistry = null)
        {
            if (equippedItem.IsEmpty)
                return HarvestToolKind.Hand;

            if (itemRegistry != null && itemRegistry.TryGet(equippedItem.ItemId, out ItemDefinition def) && def.ToolClass != HarvestToolKind.Hand)
                return def.ToolClass;

            ItemId id = equippedItem.ItemId;

            if (id == ItemId.ReedwoodDelver || id == ItemId.FlintDelver) return HarvestToolKind.Delver;
            if (id == ItemId.ReedwoodSpade  || id == ItemId.FlintSpade)  return HarvestToolKind.Spade;
            if (id == ItemId.ReedwoodFeller || id == ItemId.FlintFeller) return HarvestToolKind.Feller;
            if (id == ItemId.ReedwoodSickle || id == ItemId.FlintSickle) return HarvestToolKind.Sickle;
            if (id == ItemId.ReedwoodMallet || id == ItemId.FlintMallet) return HarvestToolKind.Mallet;
            if (id == ItemId.ReedwoodCarver || id == ItemId.FlintCarver) return HarvestToolKind.Carver;
            if (id == ItemId.ReedwoodTiller || id == ItemId.FlintTiller) return HarvestToolKind.Tiller;

            return HarvestToolKind.Hand;
        }

        void RegisterForBlock(BlockId blockId, HarvestToolKind effectiveTool)
        {
            ItemStack drop = itemRegistry.CreateDropForBlock(blockId);
            blockRegistry.TryGet(blockId, out BlockDefinition def);
            Register(new BlockHarvestRule(
                blockId, drop, effectiveTool,
                def?.HardnessClass  ?? BlockHardnessClass.Soft,
                def?.HarvestTierMin ?? 0));
        }
    }
}
