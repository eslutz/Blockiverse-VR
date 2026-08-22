using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    // Acoustic material families (voxel_audio_vfx_ruleset.md §13). These are deliberately NOT
    // BlockCategory: the registry's category is a gameplay classification, so soil and stone are
    // both Terrain and wood and leaves are both Organic — distinctions that matter enormously to
    // the ear and not at all to the simulation. Keeping the table here in Gameplay also keeps
    // BlockDefinition (and the engine-free Voxel assembly) free of presentation concerns.
    public enum BlockiverseMaterialFamily
    {
        Soil,
        Stone,
        GravelSand,
        Wood,
        Leaf,
        Glass,
        Crystal,
        OreMetal,
        Snow
    }

    // What the player is standing on. Coarser than the material family because a footstep does not
    // need to distinguish quartz from granite — both read as "hard stone" underfoot. Water is a
    // surface here but never a material family, since fluids are not broken or placed.
    public enum BlockiverseSurfaceFamily
    {
        Soil,
        Stone,
        GravelSand,
        Wood,
        Leaf,
        Snow,
        Water
    }

    public static class BlockiverseBlockFeedbackCues
    {
        // Canonical-ID → family. Canonical IDs are the persistence and wire vocabulary, so keying
        // on them (rather than BlockId ints, which are in-memory only) keeps this table stable
        // across registry reordering and readable against the ruleset's §13 table.
        static readonly Dictionary<string, BlockiverseMaterialFamily> FamilyByCanonicalId = new()
        {
            // Soil — crumbling, damp, low-frequency
            ["meadow_turf"] = BlockiverseMaterialFamily.Soil,
            ["loose_loam"] = BlockiverseMaterialFamily.Soil,
            ["rootsoil"] = BlockiverseMaterialFamily.Soil,
            ["tended_soil"] = BlockiverseMaterialFamily.Soil,
            ["dry_turf"] = BlockiverseMaterialFamily.Soil,
            ["claybed"] = BlockiverseMaterialFamily.Soil,
            ["river_silt"] = BlockiverseMaterialFamily.Soil,

            // Stone — dense grit and crack
            ["graystone"] = BlockiverseMaterialFamily.Stone,
            ["dark_slate"] = BlockiverseMaterialFamily.Stone,
            ["black_basalt"] = BlockiverseMaterialFamily.Stone,
            ["deepmantle"] = BlockiverseMaterialFamily.Stone,
            ["warm_granite"] = BlockiverseMaterialFamily.Stone,
            ["white_limestone"] = BlockiverseMaterialFamily.Stone,
            ["cutstone_block"] = BlockiverseMaterialFamily.Stone,
            ["fired_brick_block"] = BlockiverseMaterialFamily.Stone,
            // The world's bottom layer reads as the heaviest stone in the set.
            ["worldroot"] = BlockiverseMaterialFamily.Stone,
            // Stations built of stone and fired clay.
            ["clay_kiln"] = BlockiverseMaterialFamily.Stone,
            ["bellows_forge"] = BlockiverseMaterialFamily.Stone,

            // Gravel and sand — loose, granular, no single impact transient
            ["pale_sand"] = BlockiverseMaterialFamily.GravelSand,
            ["shingle_gravel"] = BlockiverseMaterialFamily.GravelSand,
            ["surface_pebbles"] = BlockiverseMaterialFamily.GravelSand,
            ["shellgrit_bed"] = BlockiverseMaterialFamily.GravelSand,
            ["flinty_shingle"] = BlockiverseMaterialFamily.GravelSand,

            // Wood — hollow knock and splinter
            ["branchwood_log"] = BlockiverseMaterialFamily.Wood,
            ["smooth_branchwood"] = BlockiverseMaterialFamily.Wood,
            ["work_plank"] = BlockiverseMaterialFamily.Wood,
            ["resin_knot"] = BlockiverseMaterialFamily.Wood,
            ["build_table"] = BlockiverseMaterialFamily.Wood,
            ["storage_crate"] = BlockiverseMaterialFamily.Wood,
            ["prep_board"] = BlockiverseMaterialFamily.Wood,
            ["tool_rack"] = BlockiverseMaterialFamily.Wood,
            ["mend_bench"] = BlockiverseMaterialFamily.Wood,
            ["deep_locker"] = BlockiverseMaterialFamily.Wood,
            ["campfire"] = BlockiverseMaterialFamily.Wood,

            // Leaf and soft plant matter — rustle, no impact
            ["leafmoss"] = BlockiverseMaterialFamily.Leaf,
            ["reedgrass"] = BlockiverseMaterialFamily.Leaf,
            ["grain_stalk"] = BlockiverseMaterialFamily.Leaf,
            ["thornbrush"] = BlockiverseMaterialFamily.Leaf,
            ["sapling"] = BlockiverseMaterialFamily.Leaf,
            ["berrybush"] = BlockiverseMaterialFamily.Leaf,
            ["glowwick"] = BlockiverseMaterialFamily.Leaf,
            // Woven and stuffed crafted goods sit with the soft organics, not with planks.
            ["reed_basket"] = BlockiverseMaterialFamily.Leaf,
            ["bedroll"] = BlockiverseMaterialFamily.Leaf,

            // Glass and fired ceramic — bright shatter
            ["clearpane_glass"] = BlockiverseMaterialFamily.Glass,
            ["frostglass"] = BlockiverseMaterialFamily.Glass,
            ["pantry_jar"] = BlockiverseMaterialFamily.Glass,

            // Crystal — ringing chip, longer tail than glass
            ["lumen_quartz_cluster"] = BlockiverseMaterialFamily.Crystal,
            ["staropal_geode"] = BlockiverseMaterialFamily.Crystal,
            ["brightsalt_crust"] = BlockiverseMaterialFamily.Crystal,
            ["niterstone_pocket"] = BlockiverseMaterialFamily.Crystal,
            ["lumen_lamp"] = BlockiverseMaterialFamily.Crystal,
            ["spark_flare"] = BlockiverseMaterialFamily.Crystal,

            // Ore and metal — dense grit with a metallic ring
            ["embercoal_seam"] = BlockiverseMaterialFamily.OreMetal,
            ["rosycopper_bloom"] = BlockiverseMaterialFamily.OreMetal,
            ["rustcore_ore"] = BlockiverseMaterialFamily.OreMetal,
            ["sunmetal_fleck"] = BlockiverseMaterialFamily.OreMetal,
            ["paletin_thread"] = BlockiverseMaterialFamily.OreMetal,
            ["umbralite_node"] = BlockiverseMaterialFamily.OreMetal,

            // Snow — soft compaction, no ring
            ["snowpack"] = BlockiverseMaterialFamily.Snow,
            ["snowcap_turf"] = BlockiverseMaterialFamily.Snow,
        };

        /// <summary>
        /// Acoustic family for a block. Unmapped blocks fall back to a category/hardness guess
        /// rather than throwing, so a newly registered block is merely generic, never silent.
        /// </summary>
        public static BlockiverseMaterialFamily FamilyForBlock(BlockRegistry registry, BlockId block)
        {
            if (registry == null || !registry.TryGet(block, out BlockDefinition definition))
                return BlockiverseMaterialFamily.Stone;

            if (definition.CanonicalId != null &&
                FamilyByCanonicalId.TryGetValue(definition.CanonicalId, out BlockiverseMaterialFamily family))
                return family;

            return FallbackFamily(definition);
        }

        // Best guess for a block that predates or postdates the table above.
        static BlockiverseMaterialFamily FallbackFamily(BlockDefinition definition)
        {
            return definition.Category switch
            {
                BlockCategory.Organic => BlockiverseMaterialFamily.Leaf,
                BlockCategory.Resource => BlockiverseMaterialFamily.OreMetal,
                BlockCategory.Crafted => BlockiverseMaterialFamily.Wood,
                BlockCategory.Station => BlockiverseMaterialFamily.Wood,
                BlockCategory.Terrain => definition.HardnessClass >= BlockHardnessClass.Medium
                    ? BlockiverseMaterialFamily.Stone
                    : BlockiverseMaterialFamily.Soil,
                _ => BlockiverseMaterialFamily.Stone,
            };
        }

        /// <summary>
        /// What this block sounds like underfoot. Fluids resolve to <see cref="BlockiverseSurfaceFamily.Water"/>;
        /// hard non-porous materials collapse onto Stone because a boot cannot tell them apart.
        /// </summary>
        public static BlockiverseSurfaceFamily SurfaceForBlock(BlockRegistry registry, BlockId block)
        {
            if (FluidBlocks.IsFluid(block))
                return BlockiverseSurfaceFamily.Water;

            return FamilyForBlock(registry, block) switch
            {
                BlockiverseMaterialFamily.Soil => BlockiverseSurfaceFamily.Soil,
                BlockiverseMaterialFamily.GravelSand => BlockiverseSurfaceFamily.GravelSand,
                BlockiverseMaterialFamily.Wood => BlockiverseSurfaceFamily.Wood,
                BlockiverseMaterialFamily.Leaf => BlockiverseSurfaceFamily.Leaf,
                BlockiverseMaterialFamily.Snow => BlockiverseSurfaceFamily.Snow,
                // Stone, Glass, Crystal, and OreMetal are all hard underfoot.
                _ => BlockiverseSurfaceFamily.Stone,
            };
        }

        public static BlockiverseAudioCue ToolHitForBlock(BlockRegistry registry, BlockId block)
        {
            if (registry == null || !registry.TryGet(block, out BlockDefinition definition))
                return BlockiverseAudioCue.ToolHitSoft;

            if (definition.Category == BlockCategory.Organic)
                return BlockiverseAudioCue.ToolHitSoft;

            return definition.Category == BlockCategory.Terrain ||
                   definition.Category == BlockCategory.Resource ||
                   definition.HardnessClass >= BlockHardnessClass.Medium
                ? BlockiverseAudioCue.ToolHitStone
                : BlockiverseAudioCue.ToolHitSoft;
        }
    }
}
