using System;

namespace Blockiverse.Survival
{
    public readonly struct ItemId : IEquatable<ItemId>
    {
        readonly string value;

        public ItemId(string canonicalId)
        {
            if (string.IsNullOrWhiteSpace(canonicalId))
                throw new ArgumentException("Item canonical IDs must be non-empty.", nameof(canonicalId));

            value = canonicalId;
        }

        public string Value => value ?? string.Empty;
        public bool IsNone => string.IsNullOrEmpty(value);

        public static readonly ItemId None = default;

        // ── Block items ──────────────────────────────────────────────────────
        public static readonly ItemId MeadowTurf          = new("meadow_turf");
        public static readonly ItemId DryTurf              = new("dry_turf");
        public static readonly ItemId SnowcapTurf          = new("snowcap_turf");
        public static readonly ItemId LooseLoam            = new("loose_loam");
        public static readonly ItemId Rootsoil             = new("rootsoil");
        public static readonly ItemId Claybed              = new("claybed");
        public static readonly ItemId RiverSilt            = new("river_silt");
        public static readonly ItemId PaleSand             = new("pale_sand");
        public static readonly ItemId ShingleGravel        = new("shingle_gravel");
        public static readonly ItemId Snowpack             = new("snowpack");
        public static readonly ItemId Frostglass           = new("frostglass");
        public static readonly ItemId Graystone            = new("graystone");
        public static readonly ItemId DarkSlate            = new("dark_slate");
        public static readonly ItemId WarmGranite          = new("warm_granite");
        public static readonly ItemId WhiteLimestone       = new("white_limestone");
        public static readonly ItemId BlackBasalt          = new("black_basalt");
        public static readonly ItemId Worldroot            = new("worldroot");
        public static readonly ItemId Deepmantle           = new("deepmantle");
        public static readonly ItemId BranchwoodLog        = new("branchwood_log");
        public static readonly ItemId SmoothBranchwood     = new("smooth_branchwood");
        public static readonly ItemId Leafmoss             = new("leafmoss");
        public static readonly ItemId Thornbrush           = new("thornbrush");
        public static readonly ItemId WorkPlank            = new("work_plank");
        public static readonly ItemId CutstoneBlock        = new("cutstone_block");
        public static readonly ItemId FiredBrick           = new("fired_brick");
        public static readonly ItemId FiredBrickBlock      = new("fired_brick_block");
        public static readonly ItemId ClearpaneGlass       = new("clearpane_glass");
        public static readonly ItemId BuildTable           = new("build_table");
        public static readonly ItemId Glowwick             = new("glowwick");
        public static readonly ItemId LumenLamp            = new("lumen_lamp");
        public static readonly ItemId SparkFlare           = new("spark_flare");
        public static readonly ItemId StorageCrate         = new("storage_crate");
        public static readonly ItemId ReedBasket           = new("reed_basket");
        public static readonly ItemId ToolRack             = new("tool_rack");
        public static readonly ItemId PantryJar            = new("pantry_jar");
        public static readonly ItemId DeepLocker           = new("deep_locker");
        public static readonly ItemId Campfire             = new("campfire");
        public static readonly ItemId ClayKiln             = new("clay_kiln");
        public static readonly ItemId BellowsForge         = new("bellows_forge");
        public static readonly ItemId PrepBoard            = new("prep_board");
        public static readonly ItemId MendBench            = new("mend_bench");

        // ── Raw resource drops (node block → raw item, voxel_survival_ruleset §3) ─
        public static readonly ItemId SurfacePebbles       = new("surface_pebbles");
        public static readonly ItemId FlintyShingle        = new("flinty_shingle");
        public static readonly ItemId Embercoal            = new("embercoal");
        public static readonly ItemId RawRosycopper        = new("raw_rosycopper");
        public static readonly ItemId RawPaletin           = new("raw_paletin");
        public static readonly ItemId RawRustcore          = new("raw_rustcore");
        public static readonly ItemId RawSunmetal          = new("raw_sunmetal");
        public static readonly ItemId LumenCrystal         = new("lumen_crystal");
        public static readonly ItemId SparkNiter           = new("spark_niter");
        public static readonly ItemId Brightsalt           = new("brightsalt");
        public static readonly ItemId Shellgrit            = new("shellgrit");
        public static readonly ItemId ResinKnot            = new("resin_knot");
        public static readonly ItemId RawUmbralite         = new("raw_umbralite");
        public static readonly ItemId StaropalShard        = new("staropal_shard");

        // ── Farming drops and seeds (§3, §11.2) ───────────────────────────────
        public static readonly ItemId GrainBundle          = new("grain_bundle");
        public static readonly ItemId BerryCluster         = new("berry_cluster");
        public static readonly ItemId MeadowSeed           = new("meadow_seed");
        public static readonly ItemId DrygrassSeed         = new("drygrass_seed");
        public static readonly ItemId ReedCutting          = new("reed_cutting");
        public static readonly ItemId BerrySeed            = new("berry_seed");
        public static readonly ItemId CleanWaterFlask      = new("clean_water_flask");

        // ── Crafted intermediates (work parts, smelted bars, §9) ──────────────
        public static readonly ItemId StoutPole            = new("stout_pole");
        public static readonly ItemId FiberCord            = new("fiber_cord");
        public static readonly ItemId ReedFiber            = new("reed_fiber");
        public static readonly ItemId StoneRubble          = new("stone_rubble");
        public static readonly ItemId ClayLump             = new("clay_lump");
        public static readonly ItemId GlassShard           = new("glass_shard");
        public static readonly ItemId LumenDust            = new("lumen_dust");
        public static readonly ItemId EmbercoalBlock       = new("embercoal_block");
        public static readonly ItemId RosycopperBar        = new("rosycopper_bar");
        public static readonly ItemId PaletinBar           = new("paletin_bar");
        public static readonly ItemId BronzeBar            = new("bronze_bar");
        public static readonly ItemId IronrootBar          = new("ironroot_bar");
        public static readonly ItemId SunmetalBar          = new("sunmetal_bar");
        public static readonly ItemId DeepsteelBar         = new("deepsteel_bar");
        public static readonly ItemId StarforgedCore       = new("starforged_core");

        // ── Tier-1 (Reedwood) tools ──────────────────────────────────────────
        public static readonly ItemId ReedwoodDelver       = new("reedwood_delver");
        public static readonly ItemId ReedwoodSpade        = new("reedwood_spade");
        public static readonly ItemId ReedwoodFeller       = new("reedwood_feller");
        public static readonly ItemId ReedwoodSickle       = new("reedwood_sickle");
        public static readonly ItemId ReedwoodMallet       = new("reedwood_mallet");
        public static readonly ItemId ReedwoodCarver       = new("reedwood_carver");
        public static readonly ItemId ReedwoodTiller       = new("reedwood_tiller");

        // ── Tier-2 (Flint) tools ─────────────────────────────────────────────
        public static readonly ItemId FlintDelver          = new("flint_delver");
        public static readonly ItemId FlintSpade           = new("flint_spade");
        public static readonly ItemId FlintFeller          = new("flint_feller");
        public static readonly ItemId FlintSickle          = new("flint_sickle");
        public static readonly ItemId FlintMallet          = new("flint_mallet");
        public static readonly ItemId FlintCarver          = new("flint_carver");
        public static readonly ItemId FlintTiller          = new("flint_tiller");

        // ── Consumables ───────────────────────────────────────────────────────
        public static readonly ItemId FieldBandage         = new("field_bandage");

        public bool Equals(ItemId other)
        {
            return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is ItemId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
        }

        public override string ToString()
        {
            return IsNone ? "none" : Value;
        }

        public static bool operator ==(ItemId left, ItemId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ItemId left, ItemId right)
        {
            return !left.Equals(right);
        }
    }
}
