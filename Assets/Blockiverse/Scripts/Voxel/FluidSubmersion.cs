namespace Blockiverse.Voxel
{
    // How deep in a fluid the player is, in the only terms the game needs: which of the three
    // sample points are inside fluid.
    public enum FluidImmersion
    {
        None = 0,
        Feet = 1,
        Body = 2,
        Head = 3
    }

    public readonly struct FluidSubmersionState
    {
        public FluidSubmersionState(
            bool inFluid,
            FluidFamily family,
            FluidImmersion immersion,
            bool feetSubmerged,
            bool bodySubmerged,
            bool headSubmerged,
            bool hasSurface,
            int surfaceCellY)
        {
            InFluid = inFluid;
            Family = family;
            Immersion = immersion;
            FeetSubmerged = feetSubmerged;
            BodySubmerged = bodySubmerged;
            HeadSubmerged = headSubmerged;
            HasSurface = hasSurface;
            SurfaceCellY = surfaceCellY;
        }

        public bool InFluid { get; }

        // The deepest sample's family wins, so wading out of freshwater into an emberflow pool
        // reports emberflow the moment the feet reach it.
        public FluidFamily Family { get; }

        public FluidImmersion Immersion { get; }

        public bool FeetSubmerged { get; }

        // The swim trigger. Feet alone is wading, which stays walkable.
        public bool BodySubmerged { get; }

        public bool HeadSubmerged { get; }

        public bool HasSurface { get; }

        // Highest contiguous fluid cell of the same family above the sample. Meaningful only when
        // HasSurface is true.
        public int SurfaceCellY { get; }
    }

    // Engine-free submersion query: three voxel reads per frame, no physics. A raycast against the
    // fluid collider would contend with the throttled collider recook queue and would disagree with
    // the GPU wave, which is presentation-only; the voxel grid is the authority for where fluid is.
    public static class FluidSubmersion
    {
        // Deep enough to find the surface above a player who has sunk a little, cheap enough to run
        // every frame while submerged. Beyond this the answer stops mattering: the surface is far
        // out of reach either way.
        public const int DefaultSurfaceScanCells = 6;

        public static FluidSubmersionState Sample(
            VoxelWorld world,
            BlockPosition feet,
            BlockPosition body,
            BlockPosition head,
            int surfaceScanCells = DefaultSurfaceScanCells)
        {
            // A null world is a real state -- the title screen, and the window during a world
            // reload -- and it must read as dry, or a reload while swimming would leave gravity
            // locked off forever.
            if (world == null)
                return default;

            bool feetInFluid = TryGetFluidFamily(world, feet, out FluidFamily feetFamily);
            bool bodyInFluid = TryGetFluidFamily(world, body, out FluidFamily bodyFamily);
            bool headInFluid = TryGetFluidFamily(world, head, out FluidFamily headFamily);

            if (!feetInFluid && !bodyInFluid && !headInFluid)
                return default;

            FluidImmersion immersion = headInFluid
                ? FluidImmersion.Head
                : bodyInFluid
                    ? FluidImmersion.Body
                    : FluidImmersion.Feet;

            // Deepest sample first: the feet are the lowest point, so their fluid is the one the
            // player is standing in even when the body is in something else.
            FluidFamily family = feetInFluid ? feetFamily : bodyInFluid ? bodyFamily : headFamily;
            BlockPosition surfaceScanFrom = headInFluid ? head : bodyInFluid ? body : feet;
            bool hasSurface = TryFindSurfaceCellY(world, surfaceScanFrom, family, surfaceScanCells, out int surfaceCellY);

            return new FluidSubmersionState(
                inFluid: true,
                family: family,
                immersion: immersion,
                feetSubmerged: feetInFluid,
                bodySubmerged: bodyInFluid,
                headSubmerged: headInFluid,
                hasSurface: hasSurface,
                surfaceCellY: surfaceCellY);
        }

        // Walks up from a fluid cell to the highest contiguous cell of the same family. Stops at a
        // different family so a freshwater layer floating over emberflow reports its own surface,
        // and stops at the scan limit rather than walking a whole ocean column.
        public static bool TryFindSurfaceCellY(
            VoxelWorld world,
            BlockPosition from,
            FluidFamily family,
            int maxScanCells,
            out int surfaceCellY)
        {
            surfaceCellY = 0;

            if (world == null)
                return false;

            if (!TryGetFluidFamily(world, from, out FluidFamily startFamily) || startFamily != family)
                return false;

            surfaceCellY = from.Y;

            for (int step = 1; step <= maxScanCells; step++)
            {
                var above = new BlockPosition(from.X, from.Y + step, from.Z);

                if (!TryGetFluidFamily(world, above, out FluidFamily aboveFamily) || aboveFamily != family)
                    break;

                surfaceCellY = above.Y;
            }

            return true;
        }

        // The water plane. World space and voxel space are 1:1 and the surface is never dropped on
        // the CPU -- the render-side wave only ever dips BELOW this plane -- so a cell's top face
        // and the physical water line are the same number.
        public static float SurfaceWorldY(int surfaceCellY) => surfaceCellY + 1.0f;

        static bool TryGetFluidFamily(VoxelWorld world, BlockPosition position, out FluidFamily family)
        {
            family = default;

            // Out of bounds is dry, not an exception: the head cell is above WorldMaxY whenever the
            // player is near the ceiling, and that is an ordinary frame.
            if (!world.Bounds.Contains(position))
                return false;

            return FluidBlocks.TryGetFamily(world.GetBlock(position), out family);
        }
    }
}
