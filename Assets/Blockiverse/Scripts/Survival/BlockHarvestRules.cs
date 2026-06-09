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
        public BlockHarvestRule(
            BlockId blockId,
            ItemStack drop,
            HarvestToolKind effectiveTool,
            BlockHardnessClass hardnessClass,
            int harvestTierMin,
            float hardness,
            DropTable table = null)
        {
            if (drop.IsEmpty)
                throw new ArgumentException("Harvest rules must produce a drop.", nameof(drop));

            BlockId = blockId;
            Drop = drop;
            EffectiveTool = effectiveTool;
            HardnessClass = hardnessClass;
            HarvestTierMin = harvestTierMin;
            Hardness = hardness;
            Table = table;
        }

        public BlockId BlockId { get; }
        public ItemStack Drop { get; }
        public DropTable Table { get; }
        // Max possible primary drop count — used for inventory-capacity checks when a DropTable is present.
        public int MaxDropCount => Table?.PrimaryMaxCount ?? Drop.Count;
        public HarvestToolKind EffectiveTool { get; }
        public BlockHardnessClass HardnessClass { get; }
        public int HarvestTierMin { get; }
        public float Hardness { get; }

        // Mining time in ticks with bare hands.
        public int HandMineTicks => GetMineTicks(HarvestToolKind.Hand, toolTier: 0);

        public int GetMineTicks(HarvestToolKind toolKind) => GetMineTicks(toolKind, toolTier: 1);

        // Mining time in ticks for the given tool (voxel_survival_ruleset §6.1).
        public int GetMineTicks(HarvestToolKind toolKind, int toolTier) =>
            MiningFormula.MineTicks(Hardness, EffectiveTool, HarvestTierMin, toolKind, toolTier);
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

            rules.RegisterForBlock(BlockRegistry.MeadowTurf,        HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.LooseLoam,          HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.Graystone,          HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BranchwoodLog,      HarvestToolKind.Feller);
            rules.RegisterForBlock(BlockRegistry.SmoothBranchwood,   HarvestToolKind.Feller);
            rules.RegisterForBlock(BlockRegistry.Leafmoss,           HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.LumenQuartzCluster, HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.EmbercoalSeam,      HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.RosycopperBloom,    HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.RustcoreOre,        HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BuildTable,         HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.Glowwick,           HarvestToolKind.Hand);
            rules.RegisterForBlock(BlockRegistry.LumenLamp,          HarvestToolKind.Hand);
            rules.RegisterForBlock(BlockRegistry.SparkFlare,         HarvestToolKind.Hand);
            rules.RegisterForBlock(BlockRegistry.StorageCrate,       HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.ReedBasket,         HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.ToolRack,           HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.PantryJar,          HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.DeepLocker,         HarvestToolKind.Mallet);

            // ── Additional canonical terrain ─────────────────────────────────
            rules.RegisterForBlock(BlockRegistry.DryTurf,            HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.SnowcapTurf,        HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.Rootsoil,           HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.Claybed,            HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.RiverSilt,          HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.PaleSand,           HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.ShingleGravel,      HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.DarkSlate,          HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.WarmGranite,        HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.WhiteLimestone,     HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BlackBasalt,        HarvestToolKind.Delver);

            // ── Additional canonical vegetation ──────────────────────────────
            rules.RegisterForBlock(BlockRegistry.Thornbrush,         HarvestToolKind.Sickle);
            // Reedgrass: variable fiber yield (1–3); Sickle double-roll applies.
            var reedFiberTable = new DropTable(new DropTableEntry(ItemId.ReedFiber, 1, 3));
            rules.RegisterForBlock(BlockRegistry.Reedgrass,   HarvestToolKind.Sickle, reedFiberTable);
            rules.RegisterForBlock(BlockRegistry.Reedgrass_S1, HarvestToolKind.Sickle, reedFiberTable);
            rules.RegisterForBlock(BlockRegistry.Reedgrass_S2, HarvestToolKind.Sickle, reedFiberTable);
            rules.RegisterForBlock(BlockRegistry.Reedgrass_S3, HarvestToolKind.Sickle, reedFiberTable);

            // ── Additional canonical crafted blocks ──────────────────────────
            rules.RegisterForBlock(BlockRegistry.WorkPlank,          HarvestToolKind.Feller);
            rules.RegisterForBlock(BlockRegistry.CutstoneBlock,      HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.FiredBrickBlock,    HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.ClearpaneGlass,     HarvestToolKind.Mallet);

            // ── Crafting stations ────────────────────────────────────────────
            rules.RegisterForBlock(BlockRegistry.Campfire,           HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.ClayKiln,           HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.BellowsForge,       HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.PrepBoard,          HarvestToolKind.Mallet);
            rules.RegisterForBlock(BlockRegistry.MendBench,          HarvestToolKind.Mallet);

            // ── Additional canonical resource nodes ──────────────────────────
            rules.RegisterForBlock(BlockRegistry.SurfacePebbles,     HarvestToolKind.Hand);
            rules.RegisterForBlock(BlockRegistry.FlintyShingle,      HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.PaletinThread,      HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.SunmetalFleck,      HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.NiterstonePocket,   HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.BrightsaltCrust,    HarvestToolKind.Spade);
            rules.RegisterForBlock(BlockRegistry.ShellgritBed,       HarvestToolKind.Spade);
            // ResinKnot: Carver gives full yield (1–2); without Carver, only 1 drops (fixed Drop).
            rules.RegisterForBlock(BlockRegistry.ResinKnot, HarvestToolKind.Carver,
                new DropTable(new DropTableEntry(ItemId.ResinKnot, 1, 2)));
            rules.RegisterForBlock(BlockRegistry.Berrybush,          HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.Berrybush_S1,       HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.Berrybush_S2,       HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.Berrybush_S3,       HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.Berrybush_S4,       HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.Berrybush_S5,       HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.GrainStalk,         HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.GrainStalk_S1,      HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.GrainStalk_S2,      HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.GrainStalk_S3,      HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.GrainStalk_S4,      HarvestToolKind.Sickle);
            rules.RegisterForBlock(BlockRegistry.UmbraliteNode,      HarvestToolKind.Delver);
            rules.RegisterForBlock(BlockRegistry.StaropalGeode,      HarvestToolKind.Delver);

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

            return HarvestToolKind.Hand;
        }

        void RegisterForBlock(BlockId blockId, HarvestToolKind effectiveTool, DropTable table = null)
        {
            ItemStack drop = itemRegistry.CreateDropForBlock(blockId);
            if (drop.IsEmpty)
                throw new InvalidOperationException($"No item mapping found for block '{blockId}' — add an ItemDefinition with blockId set before registering a harvest rule.");
            blockRegistry.TryGet(blockId, out BlockDefinition def);
            Register(new BlockHarvestRule(
                blockId, drop, effectiveTool,
                def?.HardnessClass  ?? BlockHardnessClass.Soft,
                def?.HarvestTierMin ?? 0,
                def != null ? def.Hardness : 1.0f,
                table));
        }
    }
}
