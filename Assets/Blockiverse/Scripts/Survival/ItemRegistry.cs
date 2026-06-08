using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public sealed class ItemRegistry
    {
        readonly Dictionary<ItemId, ItemDefinition> definitionsById = new();
        readonly Dictionary<BlockId, ItemDefinition> definitionsByBlock = new();
        readonly Dictionary<string, ItemDefinition> definitionsByName = new(StringComparer.OrdinalIgnoreCase);

        public const int BlockStackSize = 99;
        public const int OreStackSize = 50;
        public const int CrystalStackSize = 30;
        public const int FoodStackSize = 20;
        public const int ToolStackSize = 1;
        public const int ConsumableStackSize = 20;
        public const int FieldBandageStackSize = 20;

        // Kept for backward compat with existing callers.
        public const int ResourceStackSize = BlockStackSize;
        public const int RecoveryWrapStackSize = FieldBandageStackSize;

        public IReadOnlyCollection<ItemDefinition> All => definitionsById.Values;

        public static ItemRegistry CreateDefault()
        {
            var registry = new ItemRegistry();
            registry.Register(new ItemDefinition(ItemId.None, "None", ItemKind.None, maxStackSize: 0));

            // ── Block items (terrain) ─────────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.MeadowTurf, "Meadow Turf", ItemKind.Resource, BlockStackSize, BlockRegistry.MeadowTurf));
            registry.Register(new ItemDefinition(ItemId.DryTurf, "Dry Turf", ItemKind.Resource, BlockStackSize, BlockRegistry.DryTurf));
            registry.Register(new ItemDefinition(ItemId.SnowcapTurf, "Snowcap Turf", ItemKind.Resource, BlockStackSize, BlockRegistry.SnowcapTurf));
            registry.Register(new ItemDefinition(ItemId.LooseLoam, "Loose Loam", ItemKind.Resource, BlockStackSize, BlockRegistry.LooseLoam));
            registry.Register(new ItemDefinition(ItemId.Rootsoil, "Rootsoil", ItemKind.Resource, BlockStackSize, BlockRegistry.Rootsoil));
            registry.Register(new ItemDefinition(ItemId.Claybed, "Claybed", ItemKind.Resource, BlockStackSize, BlockRegistry.Claybed));
            registry.Register(new ItemDefinition(ItemId.RiverSilt, "River Silt", ItemKind.Resource, BlockStackSize, BlockRegistry.RiverSilt));
            registry.Register(new ItemDefinition(ItemId.PaleSand, "Pale Sand", ItemKind.Resource, BlockStackSize, BlockRegistry.PaleSand));
            registry.Register(new ItemDefinition(ItemId.ShingleGravel, "Shingle Gravel", ItemKind.Resource, BlockStackSize, BlockRegistry.ShingleGravel));
            registry.Register(new ItemDefinition(ItemId.Graystone, "Graystone", ItemKind.Resource, BlockStackSize, BlockRegistry.Graystone));
            registry.Register(new ItemDefinition(ItemId.DarkSlate, "Dark Slate", ItemKind.Resource, BlockStackSize, BlockRegistry.DarkSlate));
            registry.Register(new ItemDefinition(ItemId.WarmGranite, "Warm Granite", ItemKind.Resource, BlockStackSize, BlockRegistry.WarmGranite));
            registry.Register(new ItemDefinition(ItemId.WhiteLimestone, "White Limestone", ItemKind.Resource, BlockStackSize, BlockRegistry.WhiteLimestone));
            registry.Register(new ItemDefinition(ItemId.BlackBasalt, "Black Basalt", ItemKind.Resource, BlockStackSize, BlockRegistry.BlackBasalt));

            // ── Block items (vegetation) ──────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.BranchwoodLog, "Branchwood Log", ItemKind.Resource, BlockStackSize, BlockRegistry.BranchwoodLog));
            registry.Register(new ItemDefinition(ItemId.Leafmoss, "Leafmoss", ItemKind.Resource, BlockStackSize, BlockRegistry.Leafmoss));
            registry.Register(new ItemDefinition(ItemId.Thornbrush, "Thornbrush", ItemKind.Resource, BlockStackSize, BlockRegistry.Thornbrush));
            registry.Register(new ItemDefinition(ItemId.Reedgrass, "Reedgrass", ItemKind.Resource, BlockStackSize, BlockRegistry.Reedgrass));

            // ── Block items (crafted) ─────────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.WorkPlank, "Work Plank", ItemKind.Resource, BlockStackSize, BlockRegistry.WorkPlank));
            registry.Register(new ItemDefinition(ItemId.CutstoneBlock, "Cutstone Block", ItemKind.Resource, BlockStackSize, BlockRegistry.CutstoneBlock));
            // Fired Brick is a kiln-smelted intermediate item (not placeable); the placeable
            // building block is Fired Brick Block, crafted from fired bricks at the Build Table (§9.2/§9.3).
            registry.Register(new ItemDefinition(ItemId.FiredBrick, "Fired Brick", ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.FiredBrickBlock, "Fired Brick Block", ItemKind.Resource, BlockStackSize, BlockRegistry.FiredBrickBlock));
            registry.Register(new ItemDefinition(ItemId.ClearpaneGlass, "Clearpane Glass", ItemKind.Resource, BlockStackSize, BlockRegistry.ClearpaneGlass));

            // ── Block items (placeable stations/lights) ───────────────────────
            registry.Register(new ItemDefinition(ItemId.BuildTable, "Build Table", ItemKind.Placeable, BlockStackSize, BlockRegistry.BuildTable));
            registry.Register(new ItemDefinition(ItemId.Glowwick, "Glowwick", ItemKind.Placeable, BlockStackSize, BlockRegistry.Glowwick));
            registry.Register(new ItemDefinition(ItemId.LumenLamp, "Lumen Lamp", ItemKind.Placeable, BlockStackSize, BlockRegistry.LumenLamp));
            registry.Register(new ItemDefinition(ItemId.SparkFlare, "Spark Flare", ItemKind.Placeable, BlockStackSize, BlockRegistry.SparkFlare));
            registry.Register(new ItemDefinition(ItemId.StorageCrate, "Storage Crate", ItemKind.Placeable, BlockStackSize, BlockRegistry.StorageCrate));
            registry.Register(new ItemDefinition(ItemId.Campfire, "Campfire", ItemKind.Placeable, BlockStackSize, BlockRegistry.Campfire));
            registry.Register(new ItemDefinition(ItemId.ClayKiln, "Clay Kiln", ItemKind.Placeable, BlockStackSize, BlockRegistry.ClayKiln));
            registry.Register(new ItemDefinition(ItemId.BellowsForge, "Bellows Forge", ItemKind.Placeable, BlockStackSize, BlockRegistry.BellowsForge));
            registry.Register(new ItemDefinition(ItemId.PrepBoard, "Prep Board", ItemKind.Placeable, BlockStackSize, BlockRegistry.PrepBoard));
            registry.Register(new ItemDefinition(ItemId.MendBench, "Mend Bench", ItemKind.Placeable, BlockStackSize, BlockRegistry.MendBench));

            // ── Raw resource items (block mapping used for harvest drop lookup until M6 drop tables) ──
            registry.Register(new ItemDefinition(ItemId.SurfacePebbles, "Surface Pebbles", ItemKind.Resource, BlockStackSize,  BlockRegistry.SurfacePebbles));
            registry.Register(new ItemDefinition(ItemId.FlintyShingle,  "Flinty Shingle",  ItemKind.Resource, BlockStackSize,  BlockRegistry.FlintyShingle));
            registry.Register(new ItemDefinition(ItemId.Embercoal,      "Embercoal",        ItemKind.Resource, OreStackSize,    BlockRegistry.EmbercoalSeam));
            registry.Register(new ItemDefinition(ItemId.RawRosycopper,  "Raw Rosycopper",   ItemKind.Resource, OreStackSize,    BlockRegistry.RosycopperBloom));
            registry.Register(new ItemDefinition(ItemId.RawPaletin,     "Raw Paletin",      ItemKind.Resource, OreStackSize,    BlockRegistry.PaletinThread));
            registry.Register(new ItemDefinition(ItemId.RawRustcore,    "Raw Rustcore",     ItemKind.Resource, OreStackSize,    BlockRegistry.RustcoreOre));
            registry.Register(new ItemDefinition(ItemId.RawSunmetal,    "Raw Sunmetal",     ItemKind.Resource, OreStackSize,    BlockRegistry.SunmetalFleck));
            registry.Register(new ItemDefinition(ItemId.LumenCrystal,   "Lumen Crystal",    ItemKind.Resource, CrystalStackSize, BlockRegistry.LumenQuartzCluster));
            registry.Register(new ItemDefinition(ItemId.SparkNiter,     "Spark Niter",      ItemKind.Resource, OreStackSize,    BlockRegistry.NiterstonePocket));
            registry.Register(new ItemDefinition(ItemId.Brightsalt,     "Brightsalt",       ItemKind.Resource, OreStackSize,    BlockRegistry.BrightsaltCrust));
            registry.Register(new ItemDefinition(ItemId.Shellgrit,      "Shellgrit",        ItemKind.Resource, OreStackSize,    BlockRegistry.ShellgritBed));
            registry.Register(new ItemDefinition(ItemId.ResinKnot,      "Resin Knot",       ItemKind.Resource, BlockStackSize,  BlockRegistry.ResinKnot));
            registry.Register(new ItemDefinition(ItemId.Berrybush,      "Berrybush",        ItemKind.Resource, FoodStackSize,   BlockRegistry.Berrybush));
            registry.Register(new ItemDefinition(ItemId.GrainStalk,     "Grain Stalk",      ItemKind.Resource, FoodStackSize,   BlockRegistry.GrainStalk));

            // Grown crop stage blocks share the same drop as their base block
            registry.RegisterDropAlias(BlockRegistry.GrainStalk_S1, ItemId.GrainStalk);
            registry.RegisterDropAlias(BlockRegistry.GrainStalk_S2, ItemId.GrainStalk);
            registry.RegisterDropAlias(BlockRegistry.Berrybush_S1,  ItemId.Berrybush);
            registry.RegisterDropAlias(BlockRegistry.Berrybush_S2,  ItemId.Berrybush);
            registry.RegisterDropAlias(BlockRegistry.Reedgrass_S1,  ItemId.Reedgrass);
            registry.Register(new ItemDefinition(ItemId.RawUmbralite,   "Raw Umbralite",    ItemKind.Resource, CrystalStackSize, BlockRegistry.UmbraliteNode));
            registry.Register(new ItemDefinition(ItemId.StaropalShard,  "Staropal Shard",   ItemKind.Resource, CrystalStackSize, BlockRegistry.StaropalGeode));

            // ── Crafted intermediates: work parts, smelted bars/dust (§9) ─────
            registry.Register(new ItemDefinition(ItemId.StoutPole,      "Stout Pole",       ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.FiberCord,      "Fiber Cord",       ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedFiber,      "Reed Fiber",       ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.StoneRubble,    "Stone Rubble",     ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.ClayLump,       "Clay Lump",        ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.GlassShard,     "Glass Shard",      ItemKind.Resource, BlockStackSize));
            registry.Register(new ItemDefinition(ItemId.LumenDust,      "Lumen Dust",       ItemKind.Resource, CrystalStackSize));
            registry.Register(new ItemDefinition(ItemId.EmbercoalBlock, "Embercoal Block",  ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.RosycopperBar,  "Rosycopper Bar",   ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.PaletinBar,     "Paletin Bar",      ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.BronzeBar,      "Bronze Bar",       ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.IronrootBar,    "Ironroot Bar",     ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.SunmetalBar,    "Sunmetal Bar",     ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.DeepsteelBar,   "Deepsteel Bar",    ItemKind.Resource, OreStackSize));
            registry.Register(new ItemDefinition(ItemId.StarforgedCore, "Starforged Core",  ItemKind.Resource, CrystalStackSize));

            // ── Tier-1 (Reedwood) tools — base 48, scaled by class multiplier ─
            registry.Register(new ItemDefinition(ItemId.ReedwoodDelver, "Reedwood Delver", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Delver, toolTier: 1, maxDurability: 48));
            registry.Register(new ItemDefinition(ItemId.ReedwoodSpade,  "Reedwood Spade",  ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Spade,  toolTier: 1, maxDurability: 38));
            registry.Register(new ItemDefinition(ItemId.ReedwoodFeller, "Reedwood Feller", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Feller, toolTier: 1, maxDurability: 43));
            registry.Register(new ItemDefinition(ItemId.ReedwoodSickle, "Reedwood Sickle", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Sickle, toolTier: 1, maxDurability: 34));
            registry.Register(new ItemDefinition(ItemId.ReedwoodMallet, "Reedwood Mallet", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Mallet, toolTier: 1, maxDurability: 58));
            registry.Register(new ItemDefinition(ItemId.ReedwoodCarver, "Reedwood Carver", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Carver, toolTier: 1, maxDurability: 29));
            registry.Register(new ItemDefinition(ItemId.ReedwoodTiller, "Reedwood Tiller", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Tiller, toolTier: 1, maxDurability: 38));

            // ── Tier-2 (Flint) tools — base 90, scaled by class multiplier ────
            registry.Register(new ItemDefinition(ItemId.FlintDelver, "Flint Delver", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Delver, toolTier: 2, maxDurability: 90));
            registry.Register(new ItemDefinition(ItemId.FlintSpade,  "Flint Spade",  ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Spade,  toolTier: 2, maxDurability: 72));
            registry.Register(new ItemDefinition(ItemId.FlintFeller, "Flint Feller", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Feller, toolTier: 2, maxDurability: 81));
            registry.Register(new ItemDefinition(ItemId.FlintSickle, "Flint Sickle", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Sickle, toolTier: 2, maxDurability: 63));
            registry.Register(new ItemDefinition(ItemId.FlintMallet, "Flint Mallet", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Mallet, toolTier: 2, maxDurability: 108));
            registry.Register(new ItemDefinition(ItemId.FlintCarver, "Flint Carver", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Carver, toolTier: 2, maxDurability: 54));
            registry.Register(new ItemDefinition(ItemId.FlintTiller, "Flint Tiller", ItemKind.Tool, ToolStackSize, toolClass: HarvestToolKind.Tiller, toolTier: 2, maxDurability: 72));

            // ── Tier-3..7 metal tools (§7.1/§7.2): durability = material base × class multiplier ─
            RegisterToolTier(registry, "rosycopper", "Rosycopper", tier: 3, baseDurability: 160);
            RegisterToolTier(registry, "bronze",     "Bronze",     tier: 4, baseDurability: 300);
            RegisterToolTier(registry, "ironroot",   "Ironroot",   tier: 5, baseDurability: 550);
            RegisterToolTier(registry, "deepsteel",  "Deepsteel",  tier: 6, baseDurability: 1000);
            RegisterToolTier(registry, "starforged", "Starforged", tier: 7, baseDurability: 1800);

            // ── Consumables ───────────────────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.FieldBandage, "Field Bandage", ItemKind.Consumable, FieldBandageStackSize));

            return registry;
        }

        // Canonical tool classes with their durability multipliers (§7.2).
        static readonly (HarvestToolKind kind, string idSuffix, string nameSuffix, float durabilityMultiplier)[] ToolClasses =
        {
            (HarvestToolKind.Delver, "delver", "Delver", 1.00f),
            (HarvestToolKind.Spade,  "spade",  "Spade",  0.80f),
            (HarvestToolKind.Feller, "feller", "Feller", 0.90f),
            (HarvestToolKind.Sickle, "sickle", "Sickle", 0.70f),
            (HarvestToolKind.Mallet, "mallet", "Mallet", 1.20f),
            (HarvestToolKind.Carver, "carver", "Carver", 0.60f),
            (HarvestToolKind.Tiller, "tiller", "Tiller", 0.80f),
        };

        // Registers all seven tool classes for one material tier.
        static void RegisterToolTier(ItemRegistry registry, string materialId, string materialName, int tier, int baseDurability)
        {
            foreach ((HarvestToolKind kind, string idSuffix, string nameSuffix, float durabilityMultiplier) in ToolClasses)
            {
                registry.Register(new ItemDefinition(
                    new ItemId($"{materialId}_{idSuffix}"),
                    $"{materialName} {nameSuffix}",
                    ItemKind.Tool,
                    ToolStackSize,
                    toolClass: kind,
                    toolTier: tier,
                    maxDurability: (int)Math.Round(baseDurability * durabilityMultiplier)));
            }
        }

        // Maps an additional block (e.g. a grown crop stage) to an already-registered item's drop,
        // without creating a new ItemDefinition or ItemId.
        public void RegisterDropAlias(BlockId blockId, ItemId itemId)
        {
            if (!definitionsById.TryGetValue(itemId, out ItemDefinition definition))
                throw new KeyNotFoundException($"Item ID is not registered: {itemId}");
            if (definitionsByBlock.ContainsKey(blockId))
                throw new InvalidOperationException($"Block ID already has an item mapping: {blockId}");
            definitionsByBlock.Add(blockId, definition);
        }

        public void Register(ItemDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (definitionsById.ContainsKey(definition.Id))
                throw new InvalidOperationException($"Item ID is already registered: {definition.Id}");

            if (definitionsByName.ContainsKey(definition.Name))
                throw new InvalidOperationException($"Item name is already registered: {definition.Name}");

            if (definition.BlockId.HasValue && definitionsByBlock.ContainsKey(definition.BlockId.Value))
                throw new InvalidOperationException($"Block ID already has an item mapping: {definition.BlockId.Value}");

            definitionsById.Add(definition.Id, definition);
            definitionsByName.Add(definition.Name, definition);

            if (definition.BlockId.HasValue)
                definitionsByBlock.Add(definition.BlockId.Value, definition);
        }

        public ItemDefinition Get(ItemId id)
        {
            if (!definitionsById.TryGetValue(id, out ItemDefinition definition))
                throw new KeyNotFoundException($"Item ID is not registered: {id}");

            return definition;
        }

        public bool TryGet(ItemId id, out ItemDefinition definition)
        {
            return definitionsById.TryGetValue(id, out definition);
        }

        public bool TryGetItemForBlock(BlockId blockId, out ItemDefinition definition)
        {
            return definitionsByBlock.TryGetValue(blockId, out definition);
        }

        public ItemStack CreateDropForBlock(BlockId blockId, int count = 1)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Drop count must be positive.");

            return TryGetItemForBlock(blockId, out ItemDefinition definition)
                ? new ItemStack(definition.Id, count)
                : ItemStack.Empty;
        }

        public ItemStack CreateItemStack(ItemId id, int count = 1)
        {
            ItemDefinition def = Get(id);
            ItemStack stack = new ItemStack(id, count);
            return def.MaxDurability > 0 ? stack.WithDurability(def.MaxDurability) : stack;
        }
    }
}
