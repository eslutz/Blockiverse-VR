using System;
using System.Collections.Generic;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Canonical texture NAME to atlas tile index.
    ///
    /// This is the vocabulary a user-supplied texture pack is written against: a pack ships
    /// <c>blocks/meadow_turf.png</c>, not <c>blocks/0.png</c>. Keying on names rather than indices
    /// is what lets the atlas be reflowed -- as it was, from 12 columns to 16 -- without
    /// invalidating a single pack.
    ///
    /// THIS IS THE THIRD HAND-MAINTAINED MIRROR of the same data. The other two are the BLOCKS
    /// list in scripts/art/generate-art-assets.py and BlockVisualAtlas.TileIndexByBlockId, whose
    /// own comment has long noted that they "drift silently". Rather than add a third way to be
    /// quietly wrong, BlockAtlasTileNameTableEditModeTests parses the Python list and asserts all
    /// three agree -- so a drift in ANY of them now fails loudly instead of shipping a block with
    /// somebody else's texture.
    ///
    /// Indices are hex because the atlas is 16 columns wide: index = row &lt;&lt; 4 | column, so
    /// 0x63 is row 6, column 3.
    /// </summary>
    public static class BlockAtlasTileNames
    {
        // The 97 tiles the art generator composes into the atlas, in its own order.
        static readonly Dictionary<string, int> TileIndexByTextureNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "meadow_turf", 0x00 },
            { "loose_loam", 0x01 },
            { "graystone", 0x02 },
            { "branchwood_log", 0x03 },
            { "leafmoss", 0x04 },
            { "lumen_quartz_cluster", 0x05 },
            { "embercoal_seam", 0x06 },
            { "rosycopper_bloom", 0x07 },
            { "rustcore_ore", 0x08 },
            { "build_table", 0x09 },
            { "glowwick", 0x0A },
            { "storage_crate", 0x0B },
            { "worldroot", 0x0C },
            { "deepmantle", 0x0D },
            { "dark_slate", 0x0E },
            { "warm_granite", 0x0F },
            { "white_limestone", 0x10 },
            { "black_basalt", 0x11 },
            { "dry_turf", 0x12 },
            { "snowcap_turf", 0x13 },
            { "rootsoil", 0x14 },
            { "claybed", 0x15 },
            { "river_silt", 0x16 },
            { "pale_sand", 0x17 },
            { "shingle_gravel", 0x18 },
            { "snowpack", 0x19 },
            { "frostglass", 0x1A },
            { "thornbrush", 0x1B },
            { "reedgrass", 0x1C },
            { "work_plank", 0x1D },
            { "cutstone_block", 0x1E },
            { "fired_brick_block", 0x1F },
            { "clearpane_glass", 0x20 },
            { "mirror_pane", 0x4D },
            { "surface_pebbles", 0x21 },
            { "flinty_shingle", 0x22 },
            { "paletin_thread", 0x23 },
            { "sunmetal_fleck", 0x24 },
            { "niterstone_pocket", 0x25 },
            { "brightsalt_crust", 0x26 },
            { "shellgrit_bed", 0x27 },
            { "resin_knot", 0x28 },
            { "berrybush", 0x29 },
            { "grain_stalk", 0x2A },
            { "umbralite_node", 0x2B },
            { "staropal_geode", 0x2C },
            { "campfire", 0x2D },
            { "clay_kiln", 0x2E },
            { "bellows_forge", 0x2F },
            { "prep_board", 0x30 },
            { "mend_bench", 0x31 },
            { "lumen_lamp", 0x32 },
            { "spark_flare", 0x33 },
            { "tended_soil", 0x34 },
            { "grain_stalk_s1", 0x35 },
            { "grain_stalk_s2", 0x36 },
            { "berrybush_s1", 0x37 },
            { "berrybush_s2", 0x38 },
            { "reedgrass_s1", 0x39 },
            { "sapling", 0x3A },
            { "sapling_s1", 0x3B },
            { "sapling_s2", 0x3C },
            { "grain_stalk_s3", 0x3D },
            { "grain_stalk_s4", 0x3E },
            { "berrybush_s3", 0x3F },
            { "berrybush_s4", 0x40 },
            { "berrybush_s5", 0x41 },
            { "reedgrass_s2", 0x42 },
            { "reedgrass_s3", 0x43 },
            { "smooth_branchwood", 0x44 },
            { "reed_basket", 0x45 },
            { "tool_rack", 0x46 },
            { "pantry_jar", 0x47 },
            { "deep_locker", 0x48 },
            { "freshwater", 0x49 },
            { "brine", 0x4A },
            { "emberflow", 0x4B },
            { "bedroll", 0x4C },
            { "drygrass_tuft", 0x4E },
            { "meadow_tuft", 0x4F },
            { "wildflower_cluster", 0x50 },
            { "dune_sage", 0x51 },
            { "salt_reed", 0x52 },
            { "frost_fern", 0x53 },
            { "windroot_shrub", 0x54 },
            { "hanging_reed", 0x55 },
            { "moss_carpet", 0x56 },
            { "snow_lichen", 0x57 },
            { "fallen_leaves", 0x58 },
            { "charred_log", 0x59 },
            { "snow_block", 0x5A },
            { "meadow_turf_side", 0x5B },
            { "dry_turf_side", 0x5C },
            { "snowcap_turf_side", 0x5D },
            { "rootsoil_side", 0x5E },
            { "branchwood_log_end", 0x5F },
            { "smooth_branchwood_end", 0x60 },

            // Flow variants. These have NO atlas slot of their own -- a flowing cell renders with
            // its family's source tile (see BlockVisualAtlas.TileIndexByBlockId) -- so they are
            // mapped here only to be RECOGNISED. A pack that ships one gets a specific message
            // rather than being told the filename is unknown.
            { "freshwater_flow", 0x49 },
            { "brine_flow", 0x4A },
            { "emberflow_flow", 0x4B },
        };

        /// <summary>Every name a pack may supply, including the recognised-but-unused flow aliases.</summary>
        public static IReadOnlyCollection<string> AllTextureNames => TileIndexByTextureNameMap.Keys;

        /// <summary>Number of names that occupy a real atlas slot.</summary>
        public const int AtlasTileCount = 97;

        /// <summary>
        /// The atlas slot a texture name composites into. False for an unknown name, and false for
        /// a flow alias -- callers want those distinguished, which is what
        /// <see cref="IsRecognisedButUnused"/> is for.
        /// </summary>
        public static bool TryGetTileIndex(string textureName, out int tileIndex)
        {
            tileIndex = -1;

            if (string.IsNullOrWhiteSpace(textureName) || IsRecognisedButUnused(textureName))
                return false;

            return TileIndexByTextureNameMap.TryGetValue(textureName.Trim(), out tileIndex);
        }

        /// <summary>
        /// True for a name that is a real Blockiverse texture but has no atlas slot: the three
        /// fluid flow variants. Supplying one is not an error, it just has no effect.
        /// </summary>
        public static bool IsRecognisedButUnused(string textureName)
        {
            if (string.IsNullOrWhiteSpace(textureName))
                return false;

            string trimmed = textureName.Trim();
            foreach (string alias in FlowAliasNames)
                if (string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }

        /// <summary>True when the name is neither an atlas tile nor a known alias.</summary>
        public static bool IsUnknown(string textureName) =>
            !TryGetTileIndex(textureName, out _) && !IsRecognisedButUnused(textureName);

        public static readonly string[] FlowAliasNames =
        {
            "freshwater_flow",
            "brine_flow",
            "emberflow_flow",
        };
    }
}
