using System;
using System.Collections.Generic;

namespace Blockiverse.Voxel
{
    public readonly struct BlockId : IEquatable<BlockId>
    {
        public BlockId(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Block IDs must be non-negative.");

            Value = value;
        }

        public int Value { get; }

        public bool Equals(BlockId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(BlockId left, BlockId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BlockId left, BlockId right)
        {
            return !left.Equals(right);
        }
    }

    public enum BlockCategory
    {
        Air,
        Terrain,
        Organic,
        Crafted,
        Resource,
        Station,
        Fluid
    }

    public enum BlockHardnessClass
    {
        Soft     = 0,
        Medium   = 1,
        Hard     = 2,
        VeryHard = 3
    }

    public sealed class BlockDefinition
    {
        public BlockDefinition(
            BlockId id,
            string canonicalId,
            string name,
            BlockCategory category,
            bool isSolid,
            bool isRenderable,
            int emissiveLight = 0,
            BlockHardnessClass hardnessClass = BlockHardnessClass.Soft,
            int harvestTierMin = 0,
            float hardness = -1f)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
                throw new ArgumentException("Block canonical IDs must be non-empty.", nameof(canonicalId));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Block names must be non-empty.", nameof(name));

            if (emissiveLight < 0 || emissiveLight > 15)
                throw new ArgumentOutOfRangeException(nameof(emissiveLight), "Emissive light must be 0–15.");

            if (harvestTierMin < 0)
                throw new ArgumentOutOfRangeException(nameof(harvestTierMin), "Harvest tier min must be non-negative.");

            Id = id;
            CanonicalId = canonicalId;
            Name = name;
            Category = category;
            IsSolid = isSolid;
            IsRenderable = isRenderable;
            EmissiveLight = emissiveLight;
            HardnessClass = hardnessClass;
            HarvestTierMin = harvestTierMin;
            // Canonical mining hardness (voxel_survival_ruleset §2/§3). When not specified
            // explicitly, derive a representative value from the hardness class.
            Hardness = hardness >= 0f ? hardness : HardnessFromClass(hardnessClass);
        }

        static float HardnessFromClass(BlockHardnessClass hardnessClass)
        {
            return hardnessClass switch
            {
                BlockHardnessClass.Soft     => 0.5f,
                BlockHardnessClass.Medium   => 2.0f,
                BlockHardnessClass.Hard     => 3.5f,
                BlockHardnessClass.VeryHard => 6.0f,
                _                           => 1.0f,
            };
        }

        public BlockId Id { get; }
        public string CanonicalId { get; }
        public string Name { get; }
        public BlockCategory Category { get; }
        public bool IsSolid { get; }
        public bool IsRenderable { get; }
        public int EmissiveLight { get; }
        public BlockHardnessClass HardnessClass { get; }
        public int HarvestTierMin { get; }
        public float Hardness { get; }
    }

    public sealed class BlockRegistry
    {
        readonly Dictionary<BlockId, BlockDefinition> definitionsById = new();
        readonly Dictionary<string, BlockDefinition> definitionsByName = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, BlockDefinition> definitionsByCanonicalId = new(StringComparer.OrdinalIgnoreCase);

        public static readonly BlockId Air                 = new(0);
        public static readonly BlockId MeadowTurf          = new(1);
        public static readonly BlockId LooseLoam            = new(2);
        public static readonly BlockId Graystone            = new(3);
        public static readonly BlockId BranchwoodLog        = new(4);
        public static readonly BlockId Leafmoss             = new(5);
        public static readonly BlockId LumenQuartzCluster   = new(6);
        public static readonly BlockId EmbercoalSeam        = new(7);
        public static readonly BlockId RosycopperBloom      = new(8);
        public static readonly BlockId RustcoreOre          = new(9);
        public static readonly BlockId BuildTable           = new(10);
        public static readonly BlockId Glowwick             = new(11);
        public static readonly BlockId StorageCrate         = new(12);

        // ── Canonical terrain blocks ─────────────────────────────────────────
        public static readonly BlockId Worldroot            = new(13);
        public static readonly BlockId Deepmantle           = new(14);
        public static readonly BlockId DarkSlate            = new(15);
        public static readonly BlockId WarmGranite          = new(16);
        public static readonly BlockId WhiteLimestone       = new(17);
        public static readonly BlockId BlackBasalt          = new(18);

        // ── Canonical soil/surface blocks ────────────────────────────────────
        public static readonly BlockId DryTurf              = new(19);
        public static readonly BlockId SnowcapTurf          = new(20);
        public static readonly BlockId Rootsoil             = new(21);
        public static readonly BlockId Claybed              = new(22);
        public static readonly BlockId RiverSilt            = new(23);
        public static readonly BlockId PaleSand             = new(24);
        public static readonly BlockId ShingleGravel        = new(25);
        public static readonly BlockId Snowpack             = new(26);
        public static readonly BlockId Frostglass           = new(27);

        // ── Canonical vegetation blocks ──────────────────────────────────────
        public static readonly BlockId Thornbrush           = new(28);
        public static readonly BlockId Reedgrass            = new(29);

        // ── Canonical crafted blocks ─────────────────────────────────────────
        public static readonly BlockId WorkPlank            = new(30);
        public static readonly BlockId CutstoneBlock        = new(31);
        public static readonly BlockId FiredBrickBlock      = new(32);
        public static readonly BlockId ClearpaneGlass       = new(33);

        // ── Canonical resource nodes ─────────────────────────────────────────
        public static readonly BlockId SurfacePebbles       = new(34);
        public static readonly BlockId FlintyShingle        = new(35);
        public static readonly BlockId PaletinThread        = new(36);
        public static readonly BlockId SunmetalFleck        = new(37);
        public static readonly BlockId NiterstonePocket     = new(38);
        public static readonly BlockId BrightsaltCrust      = new(39);
        public static readonly BlockId ShellgritBed         = new(40);
        public static readonly BlockId ResinKnot            = new(41);
        public static readonly BlockId Berrybush            = new(42);
        public static readonly BlockId GrainStalk           = new(43);
        public static readonly BlockId UmbraliteNode        = new(44);
        public static readonly BlockId StaropalGeode        = new(45);

        // ── Canonical station blocks ─────────────────────────────────────────
        public static readonly BlockId Campfire             = new(46);
        public static readonly BlockId ClayKiln             = new(47);
        public static readonly BlockId BellowsForge         = new(48);
        public static readonly BlockId PrepBoard            = new(49);
        public static readonly BlockId MendBench            = new(50);

        // ── Light-emitting crafted blocks ────────────────────────────────────
        public static readonly BlockId LumenLamp            = new(51);
        public static readonly BlockId SparkFlare           = new(52);

        // ── Farming / tilling blocks ─────────────────────────────────────────
        public static readonly BlockId TendedSoil            = new(53);

        // ── Crop growth stages (S0 = existing GrainStalk/Berrybush/Reedgrass)
        public static readonly BlockId GrainStalk_S1        = new(54);
        public static readonly BlockId GrainStalk_S2        = new(55);
        public static readonly BlockId Berrybush_S1         = new(56);
        public static readonly BlockId Berrybush_S2         = new(57);
        public static readonly BlockId Reedgrass_S1         = new(58);

        // ── Sapling growth stages (S2 triggers full-tree placement) ──────────
        public static readonly BlockId Sapling              = new(59);
        public static readonly BlockId Sapling_S1           = new(60);
        public static readonly BlockId Sapling_S2           = new(61);

        // ── Additional crop growth stages (full canonical stage counts, §11.2) ─
        public static readonly BlockId GrainStalk_S3        = new(62);
        public static readonly BlockId GrainStalk_S4        = new(63);
        public static readonly BlockId Berrybush_S3         = new(64);
        public static readonly BlockId Berrybush_S4         = new(65);
        public static readonly BlockId Berrybush_S5         = new(66);
        public static readonly BlockId Reedgrass_S2         = new(67);
        public static readonly BlockId Reedgrass_S3         = new(68);

        // ── Crafted/decorative blocks needed by M6-F containers and feller strip-log output ─
        public static readonly BlockId SmoothBranchwood     = new(69);
        public static readonly BlockId ReedBasket           = new(70);
        public static readonly BlockId ToolRack             = new(71);
        public static readonly BlockId PantryJar            = new(72);
        public static readonly BlockId DeepLocker           = new(73);

        // ── Fluids (§5.4): still sources for now; flow simulation is a follow-up ─
        public static readonly BlockId Freshwater           = new(74);
        public static readonly BlockId Brine                = new(75);

        public IReadOnlyCollection<BlockDefinition> All => definitionsById.Values;

        public static BlockRegistry CreateDefault()
        {
            var registry = new BlockRegistry();

            // ── Preserved blocks (atlas tiles exist, IsRenderable: true) ─────
            registry.Register(new BlockDefinition(Air,                "air",                "Air",                  BlockCategory.Air,      isSolid: false, isRenderable: false));
            registry.Register(new BlockDefinition(MeadowTurf,         "meadow_turf",        "Meadow Turf",          BlockCategory.Terrain,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(LooseLoam,           "loose_loam",         "Loose Loam",           BlockCategory.Terrain,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Graystone,           "graystone",          "Graystone",            BlockCategory.Terrain,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium, harvestTierMin: 1));
            registry.Register(new BlockDefinition(BranchwoodLog,       "branchwood_log",     "Branchwood Log",       BlockCategory.Organic,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(SmoothBranchwood,    "smooth_branchwood",  "Smooth Branchwood",    BlockCategory.Organic,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(Leafmoss,            "leafmoss",           "Leafmoss",             BlockCategory.Organic,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(LumenQuartzCluster,  "lumen_quartz_cluster","Lumen Quartz Cluster", BlockCategory.Resource, isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Hard,   harvestTierMin: 3));
            registry.Register(new BlockDefinition(EmbercoalSeam,       "embercoal_seam",     "Embercoal Seam",       BlockCategory.Resource, isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Hard,   harvestTierMin: 2));
            registry.Register(new BlockDefinition(RosycopperBloom,     "rosycopper_bloom",   "Rosycopper Bloom",     BlockCategory.Resource, isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Hard,   harvestTierMin: 2));
            registry.Register(new BlockDefinition(RustcoreOre,         "rustcore_ore",       "Rustcore Ore",         BlockCategory.Resource, isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Hard,   harvestTierMin: 3));
            registry.Register(new BlockDefinition(BuildTable,          "build_table",        "Build Table",          BlockCategory.Station,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(Glowwick,            "glowwick",           "Glowwick",             BlockCategory.Crafted,  isSolid: false, isRenderable: true,  emissiveLight: 9));
            registry.Register(new BlockDefinition(StorageCrate,        "storage_crate",      "Storage Crate",        BlockCategory.Crafted,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(ReedBasket,          "reed_basket",        "Reed Basket",          BlockCategory.Crafted,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(ToolRack,            "tool_rack",          "Tool Rack",            BlockCategory.Crafted,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(PantryJar,           "pantry_jar",         "Pantry Jar",           BlockCategory.Crafted,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(DeepLocker,          "deep_locker",        "Deep Locker",          BlockCategory.Crafted,  isSolid: true,  isRenderable: true,  hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 5));

            // ── Additional canonical terrain (atlas tiles generated) ─────────
            registry.Register(new BlockDefinition(Worldroot,    "worldroot",     "Worldroot",     BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.VeryHard, harvestTierMin: 3));
            registry.Register(new BlockDefinition(Deepmantle,   "deepmantle",    "Deepmantle",    BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.VeryHard, harvestTierMin: 5));
            registry.Register(new BlockDefinition(DarkSlate,    "dark_slate",    "Dark Slate",    BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Medium,   harvestTierMin: 1));
            registry.Register(new BlockDefinition(WarmGranite,  "warm_granite",  "Warm Granite",  BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Medium,   harvestTierMin: 2));
            registry.Register(new BlockDefinition(WhiteLimestone,"white_limestone","White Limestone",BlockCategory.Terrain, isSolid: true, isRenderable: true, hardnessClass: BlockHardnessClass.Medium,  harvestTierMin: 1));
            registry.Register(new BlockDefinition(BlackBasalt,  "black_basalt",  "Black Basalt",  BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Hard,     harvestTierMin: 3));

            // ── Additional canonical soil/surface (atlas tiles generated) ────
            registry.Register(new BlockDefinition(DryTurf,       "dry_turf",       "Dry Turf",       BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(SnowcapTurf,   "snowcap_turf",   "Snowcap Turf",   BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Rootsoil,      "rootsoil",       "Rootsoil",       BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Claybed,       "claybed",        "Claybed",        BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(RiverSilt,     "river_silt",     "River Silt",     BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(PaleSand,      "pale_sand",      "Pale Sand",      BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(ShingleGravel, "shingle_gravel", "Shingle Gravel", BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Snowpack,      "snowpack",       "Snowpack",       BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Frostglass,    "frostglass",     "Frostglass",     BlockCategory.Terrain, isSolid: true, isRenderable: true,  hardnessClass: BlockHardnessClass.Soft));

            // ── Additional canonical vegetation (atlas tiles generated) ──────
            registry.Register(new BlockDefinition(Thornbrush, "thornbrush", "Thornbrush", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Reedgrass,  "reedgrass",  "Reedgrass",  BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));

            // ── Additional canonical crafted (atlas tiles generated) ─────────
            registry.Register(new BlockDefinition(WorkPlank,     "work_plank",     "Work Plank",     BlockCategory.Crafted, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(CutstoneBlock, "cutstone_block", "Cutstone Block", BlockCategory.Crafted, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(FiredBrickBlock, "fired_brick_block", "Fired Brick Block", BlockCategory.Crafted, isSolid: true, isRenderable: true, hardnessClass: BlockHardnessClass.Medium, harvestTierMin: 1, hardness: 2.6f));
            registry.Register(new BlockDefinition(ClearpaneGlass,"clearpane_glass","Clearpane Glass",BlockCategory.Crafted, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft,  harvestTierMin: 1));

            // ── Additional canonical resource nodes (atlas tiles generated) ──
            registry.Register(new BlockDefinition(SurfacePebbles,   "surface_pebbles",   "Surface Pebbles",   BlockCategory.Resource, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(FlintyShingle,    "flinty_shingle",    "Flinty Shingle",    BlockCategory.Resource, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(PaletinThread,    "paletin_thread",    "Paletin Thread",    BlockCategory.Resource, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 2));
            registry.Register(new BlockDefinition(SunmetalFleck,    "sunmetal_fleck",    "Sunmetal Fleck",    BlockCategory.Resource, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 4));
            registry.Register(new BlockDefinition(NiterstonePocket, "niterstone_pocket", "Niterstone Pocket", BlockCategory.Resource, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 2));
            registry.Register(new BlockDefinition(BrightsaltCrust,  "brightsalt_crust",  "Brightsalt Crust",  BlockCategory.Resource, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(ShellgritBed,     "shellgrit_bed",     "Shellgrit Bed",     BlockCategory.Resource, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(ResinKnot,        "resin_knot",        "Resin Knot",        BlockCategory.Resource, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush,        "berrybush",         "Berrybush",         BlockCategory.Organic,  isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(GrainStalk,       "grain_stalk",       "Grain Stalk",       BlockCategory.Organic,  isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(UmbraliteNode,    "umbralite_node",    "Umbralite Node",    BlockCategory.Resource, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 5));
            registry.Register(new BlockDefinition(StaropalGeode,    "staropal_geode",    "Staropal Geode",    BlockCategory.Resource, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Hard, harvestTierMin: 6));

            // ── Additional canonical stations (atlas tiles generated) ────────
            registry.Register(new BlockDefinition(Campfire,     "campfire",      "Campfire",      BlockCategory.Station, isSolid: false, isRenderable: true, emissiveLight: 12));
            registry.Register(new BlockDefinition(ClayKiln,     "clay_kiln",     "Clay Kiln",     BlockCategory.Station, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(BellowsForge, "bellows_forge", "Bellows Forge", BlockCategory.Station, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(PrepBoard,    "prep_board",    "Prep Board",    BlockCategory.Station, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));
            registry.Register(new BlockDefinition(MendBench,    "mend_bench",    "Mend Bench",    BlockCategory.Station, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Medium));

            // ── Light-emitting crafted blocks ────────────────────────────────
            registry.Register(new BlockDefinition(LumenLamp,  "lumen_lamp",   "Lumen Lamp",  BlockCategory.Crafted, isSolid: true,  isRenderable: true, emissiveLight: 14));
            registry.Register(new BlockDefinition(SparkFlare, "spark_flare",  "Spark Flare", BlockCategory.Crafted, isSolid: false, isRenderable: true, emissiveLight: 15));

            // ── Farming / tilling blocks ─────────────────────────────────────
            registry.Register(new BlockDefinition(TendedSoil,     "tended_soil",    "Tended Soil",    BlockCategory.Terrain, isSolid: true,  isRenderable: true, hardnessClass: BlockHardnessClass.Soft));

            // ── Crop growth stages (stage 0 = existing GrainStalk/Berrybush/Reedgrass) ─
            registry.Register(new BlockDefinition(GrainStalk_S1, "grain_stalk_s1", "Grain Stalk S1", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(GrainStalk_S2, "grain_stalk_s2", "Grain Stalk S2", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(GrainStalk_S3, "grain_stalk_s3", "Grain Stalk S3", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(GrainStalk_S4, "grain_stalk_s4", "Grain Stalk S4", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush_S1,  "berrybush_s1",   "Berrybush S1",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush_S2,  "berrybush_s2",   "Berrybush S2",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush_S3,  "berrybush_s3",   "Berrybush S3",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush_S4,  "berrybush_s4",   "Berrybush S4",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Berrybush_S5,  "berrybush_s5",   "Berrybush S5",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Reedgrass_S1,  "reedgrass_s1",   "Reedgrass S1",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Reedgrass_S2,  "reedgrass_s2",   "Reedgrass S2",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Reedgrass_S3,  "reedgrass_s3",   "Reedgrass S3",   BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));

            // ── Sapling growth stages ─────────────────────────────────────────
            registry.Register(new BlockDefinition(Sapling,    "sapling",    "Sapling",    BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Sapling_S1, "sapling_s1", "Sapling S1", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));
            registry.Register(new BlockDefinition(Sapling_S2, "sapling_s2", "Sapling S2", BlockCategory.Organic, isSolid: false, isRenderable: true, hardnessClass: BlockHardnessClass.Soft));

            // ── Fluids (§5.4): non-solid still sources, light-passable, not harvestable ─
            registry.Register(new BlockDefinition(Freshwater, "freshwater", "Freshwater", BlockCategory.Fluid, isSolid: false, isRenderable: true));
            registry.Register(new BlockDefinition(Brine,      "brine",      "Brine",      BlockCategory.Fluid, isSolid: false, isRenderable: true));

            return registry;
        }

        public void Register(BlockDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (definitionsById.ContainsKey(definition.Id))
                throw new InvalidOperationException($"Block ID is already registered: {definition.Id}");

            if (definitionsByName.ContainsKey(definition.Name))
                throw new InvalidOperationException($"Block name is already registered: {definition.Name}");

            if (definitionsByCanonicalId.ContainsKey(definition.CanonicalId))
                throw new InvalidOperationException($"Block canonical ID is already registered: {definition.CanonicalId}");

            definitionsById.Add(definition.Id, definition);
            definitionsByName.Add(definition.Name, definition);
            definitionsByCanonicalId.Add(definition.CanonicalId, definition);
        }

        public BlockDefinition Get(BlockId id)
        {
            if (!definitionsById.TryGetValue(id, out BlockDefinition definition))
                throw new KeyNotFoundException($"Block ID is not registered: {id}");

            return definition;
        }

        public bool TryGet(BlockId id, out BlockDefinition definition)
        {
            return definitionsById.TryGetValue(id, out definition);
        }

        public bool TryGetByCanonicalId(string canonicalId, out BlockDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
            {
                definition = null;
                return false;
            }

            return definitionsByCanonicalId.TryGetValue(canonicalId, out definition);
        }
    }

    public readonly struct BlockPosition : IEquatable<BlockPosition>
    {
        public BlockPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public bool Equals(BlockPosition other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        public static BlockPosition operator +(BlockPosition left, BlockPosition right)
        {
            return new BlockPosition(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static bool operator ==(BlockPosition left, BlockPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BlockPosition left, BlockPosition right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct ChunkCoordinate : IEquatable<ChunkCoordinate>
    {
        public ChunkCoordinate(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public static ChunkCoordinate FromBlockPosition(BlockPosition position, int chunkSize)
        {
            ValidateChunkSize(chunkSize);
            return new ChunkCoordinate(
                FloorDiv(position.X, chunkSize),
                FloorDiv(position.Y, chunkSize),
                FloorDiv(position.Z, chunkSize));
        }

        public static BlockPosition LocalPositionFromBlockPosition(BlockPosition position, int chunkSize)
        {
            ValidateChunkSize(chunkSize);
            return new BlockPosition(
                FloorMod(position.X, chunkSize),
                FloorMod(position.Y, chunkSize),
                FloorMod(position.Z, chunkSize));
        }

        public bool Equals(ChunkCoordinate other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        public static bool operator ==(ChunkCoordinate left, ChunkCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChunkCoordinate left, ChunkCoordinate right)
        {
            return !left.Equals(right);
        }

        static void ValidateChunkSize(int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        static int FloorMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }

    public readonly struct WorldBounds : IEquatable<WorldBounds>
    {
        public WorldBounds(int width, int height, int depth)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth));

            Width = width;
            Height = height;
            Depth = depth;
        }

        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }

        public bool Contains(BlockPosition position)
        {
            return position.X >= 0 && position.X < Width &&
                   position.Y >= 0 && position.Y < Height &&
                   position.Z >= 0 && position.Z < Depth;
        }

        public bool Equals(WorldBounds other)
        {
            return Width == other.Width && Height == other.Height && Depth == other.Depth;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldBounds other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Width;
                hash = (hash * 397) ^ Height;
                hash = (hash * 397) ^ Depth;
                return hash;
            }
        }

        public static bool operator ==(WorldBounds left, WorldBounds right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(WorldBounds left, WorldBounds right)
        {
            return !left.Equals(right);
        }
    }
}
