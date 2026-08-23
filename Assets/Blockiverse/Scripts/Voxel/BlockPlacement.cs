namespace Blockiverse.Voxel
{
    // What a placed block is allowed to overwrite.
    //
    // Air is the obvious case. Fluids are the one that was missing: the creative and survival
    // placement paths both hardcoded "target must be Air", which meant a lake read as solid to the
    // builder -- you could only stack blocks on top of the water surface, never build a pier
    // footing, dam a channel, or fill in a pool. That is a straight divergence from the canonical
    // creative ruleset, which specifies replace-placement over fluids
    // (docs/rulesets/voxel_creative_ruleset.md section 8.3: "Allow replace fluid with block | True").
    //
    // Lives in Voxel rather than beside either caller because BOTH the local creative path
    // (Gameplay) and the host-authoritative survival path (Networking) have to agree; two copies of
    // this predicate would drift, and a client that thought a cell was placeable while the host did
    // not would produce a rejected mutation and a visible rubber-band.
    public static class BlockPlacement
    {
        // Whether a newly placed block may take this cell. Deliberately does NOT consider player
        // occupancy -- that is a separate, position-and-posture-dependent check the callers already
        // run, and folding it in here would make this impure.
        //
        // The registry overload is preferred. Passable vegetation is replaceable for the same
        // reason fluids are (vegetation ruleset §4a.4: a passable block does not obstruct, so it
        // must not obstruct building either) -- without it, every grass tile reads as solid to the
        // builder and you cannot place a block on a meadow at all.
        public static bool IsReplaceable(BlockId existing, BlockRegistry registry)
        {
            if (IsReplaceable(existing))
                return true;

            if (registry == null)
                return false;

            BlockDefinition definition = registry.Get(existing);
            return definition != null && definition.IsPassable;
        }

        // Registry-free overload, kept for call sites that have no registry to hand. It cannot see
        // passability, so prefer the overload above wherever a registry is available.
        public static bool IsReplaceable(BlockId existing) =>
            existing == BlockRegistry.Air || FluidBlocks.IsFluid(existing);
    }
}
