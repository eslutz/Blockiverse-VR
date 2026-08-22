namespace Blockiverse.Voxel
{
    // Whether a swimmer at the edge of a body of water has a bank they can climb onto.
    //
    // Without this a player who swims to shore gets stuck. The swim state ends the moment the BODY
    // sample goes dry, and that sample sits roughly a metre above the capsule base -- so it clears
    // the waterline while the player's feet are still about a metre below the bank. Gravity resumes
    // there and pulls them straight back in. None of the usual rescues apply: the character
    // controller's step offset is far short of a metre, its step assist requires being grounded and
    // a treading player never is, and the jump provider is disabled while swimming.
    //
    // A voxel query rather than a physics cast, deliberately: deterministic, engine-free, no
    // collider bake to wait on, and it answers the same kind of question FluidSubmersion already
    // answers the same way.
    public static class FluidLedge
    {
        // How far ahead of the swimmer to look for a bank, in blocks. One cell: this is a "you are
        // at the edge" assist, not a reach.
        public const int ForwardReachBlocks = 1;

        // How far above the swimmer's foot cell a bank's TOP SURFACE may sit and still be
        // climbable. One covers exactly the two cases asked for: a bank level with the water
        // surface (surface at the foot cell, stand one above it) and a bank one block above the
        // water (surface one up, stand two above).
        //
        // Bounded on the surface, and the LANDING is one higher again -- so the largest lift this
        // can ever apply is two blocks. Setting this to 2 quietly allows three, which is a
        // markedly bigger vection event than anyone agreed to.
        public const int MaximumRiseBlocks = 1;

        // Cells of clear space a climbed-onto ledge needs above it. The player capsule is two
        // blocks tall standing, so landing on a ledge with a ceiling one block up would wedge them.
        public const int RequiredHeadroomCells = 2;

        // Finds the lowest climbable surface in the column one step ahead, if there is one.
        //
        // `feet` is the swimmer's foot cell. `forwardX`/`forwardZ` must be a unit step on one axis
        // -- the caller quantises the move stick, because a diagonal would need both neighbouring
        // columns checked to avoid pulling the player through a corner.
        public static bool TryResolveClimbOut(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition feet,
            int forwardX,
            int forwardZ,
            out BlockPosition landing)
        {
            landing = default;

            if (world == null || registry == null)
                return false;

            if (forwardX == 0 && forwardZ == 0)
                return false;

            BlockDefinition[] defs = registry.CachedDefinitions;
            int x = feet.X + (forwardX * ForwardReachBlocks);
            int z = feet.Z + (forwardZ * ForwardReachBlocks);

            // Lowest first, so a bank level with the water wins over one a block higher and the
            // player is lifted the smallest distance that gets them out.
            for (int rise = 0; rise <= MaximumRiseBlocks; rise++)
            {
                int surfaceY = feet.Y + rise;
                var surface = new BlockPosition(x, surfaceY, z);

                if (!world.Bounds.Contains(surface))
                    continue;

                if (!IsSolidGround(world, defs, surface))
                    continue;

                var candidate = new BlockPosition(x, surfaceY + 1, z);

                if (!HasHeadroom(world, defs, candidate))
                    continue;

                landing = candidate;
                return true;
            }

            return false;
        }

        // Fluids are not solid (their definitions carry isSolid: false), so water can never be
        // mistaken for a bank -- which would let a swimmer "climb out" onto the surface of the lake
        // they are in.
        static bool IsSolidGround(VoxelWorld world, BlockDefinition[] defs, BlockPosition position)
        {
            BlockId id = world.GetBlock(position);

            if (id == BlockRegistry.Air)
                return false;

            int index = id.Value;

            if (index < 0 || index >= defs.Length || defs[index] == null)
                return false;

            return defs[index].IsSolid;
        }

        static bool HasHeadroom(VoxelWorld world, BlockDefinition[] defs, BlockPosition landing)
        {
            for (int offset = 0; offset < RequiredHeadroomCells; offset++)
            {
                var cell = new BlockPosition(landing.X, landing.Y + offset, landing.Z);

                if (!world.Bounds.Contains(cell))
                    return false;

                if (IsSolidGround(world, defs, cell))
                    return false;
            }

            return true;
        }
    }
}
