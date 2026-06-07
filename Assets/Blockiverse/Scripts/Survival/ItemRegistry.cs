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
            registry.Register(new ItemDefinition(ItemId.FiredBrick, "Fired Brick", ItemKind.Resource, BlockStackSize, BlockRegistry.FiredBrick));
            registry.Register(new ItemDefinition(ItemId.ClearpaneGlass, "Clearpane Glass", ItemKind.Resource, BlockStackSize, BlockRegistry.ClearpaneGlass));

            // ── Block items (placeable stations/lights) ───────────────────────
            registry.Register(new ItemDefinition(ItemId.BuildTable, "Build Table", ItemKind.Placeable, BlockStackSize, BlockRegistry.BuildTable));
            registry.Register(new ItemDefinition(ItemId.Glowwick, "Glowwick", ItemKind.Placeable, BlockStackSize, BlockRegistry.Glowwick));
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
            registry.Register(new ItemDefinition(ItemId.PaletinThread,  "Paletin Thread",   ItemKind.Resource, OreStackSize,    BlockRegistry.PaletinThread));
            registry.Register(new ItemDefinition(ItemId.RawRustcore,    "Raw Rustcore",     ItemKind.Resource, OreStackSize,    BlockRegistry.RustcoreOre));
            registry.Register(new ItemDefinition(ItemId.SunmetalFleck,  "Sunmetal Fleck",   ItemKind.Resource, OreStackSize,    BlockRegistry.SunmetalFleck));
            registry.Register(new ItemDefinition(ItemId.LumenCrystal,   "Lumen Crystal",    ItemKind.Resource, CrystalStackSize, BlockRegistry.LumenQuartzCluster));
            registry.Register(new ItemDefinition(ItemId.Niterstone,     "Niterstone",       ItemKind.Resource, OreStackSize,    BlockRegistry.NiterstonePocket));
            registry.Register(new ItemDefinition(ItemId.Brightsalt,     "Brightsalt",       ItemKind.Resource, OreStackSize,    BlockRegistry.BrightsaltCrust));
            registry.Register(new ItemDefinition(ItemId.Shellgrit,      "Shellgrit",        ItemKind.Resource, OreStackSize,    BlockRegistry.ShellgritBed));
            registry.Register(new ItemDefinition(ItemId.ResinKnot,      "Resin Knot",       ItemKind.Resource, BlockStackSize,  BlockRegistry.ResinKnot));
            registry.Register(new ItemDefinition(ItemId.Berrybush,      "Berrybush",        ItemKind.Resource, FoodStackSize,   BlockRegistry.Berrybush));
            registry.Register(new ItemDefinition(ItemId.GrainStalk,     "Grain Stalk",      ItemKind.Resource, FoodStackSize,   BlockRegistry.GrainStalk));
            registry.Register(new ItemDefinition(ItemId.UmbraliteNode,  "Umbralite Node",   ItemKind.Resource, CrystalStackSize, BlockRegistry.UmbraliteNode));
            registry.Register(new ItemDefinition(ItemId.StaropalGeode,  "Staropal Geode",   ItemKind.Resource, CrystalStackSize, BlockRegistry.StaropalGeode));

            // ── Tier-1 (Reedwood) tools ───────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.ReedwoodDelver, "Reedwood Delver", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodSpade, "Reedwood Spade", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodFeller, "Reedwood Feller", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodSickle, "Reedwood Sickle", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodMallet, "Reedwood Mallet", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodCarver, "Reedwood Carver", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.ReedwoodTiller, "Reedwood Tiller", ItemKind.Tool, ToolStackSize));

            // ── Tier-2 (Flint) tools ──────────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.FlintDelver, "Flint Delver", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintSpade, "Flint Spade", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintFeller, "Flint Feller", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintSickle, "Flint Sickle", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintMallet, "Flint Mallet", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintCarver, "Flint Carver", ItemKind.Tool, ToolStackSize));
            registry.Register(new ItemDefinition(ItemId.FlintTiller, "Flint Tiller", ItemKind.Tool, ToolStackSize));

            // ── Consumables ───────────────────────────────────────────────────
            registry.Register(new ItemDefinition(ItemId.FieldBandage, "Field Bandage", ItemKind.Consumable, FieldBandageStackSize));

            return registry;
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
    }
}
