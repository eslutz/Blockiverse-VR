using System;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    // Per-column map of the highest light-blocking block, kept current from block changes.
    // Turns the renderer's sky-access checks (previously a full column walk per probe step,
    // millions of reads per rebuilt chunk) into O(1) lookups, and tells the rebuild queue
    // whether an edit changed the column's sky profile at all — only then do the cells below
    // need a lighting rebuild; underground edits invalidate just the probe halo around them.
    public sealed class VoxelSkyLightMap
    {
        readonly VoxelWorld world;
        readonly BlockRegistry registry;
        readonly int[] highestBlockerY; // -1 = column fully light-passable

        public VoxelSkyLightMap(VoxelWorld world, BlockRegistry registry)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            highestBlockerY = new int[world.Bounds.Width * world.Bounds.Depth];
            Rebuild();
        }

        public void Rebuild()
        {
            WorldBounds bounds = world.Bounds;
            for (int z = 0; z < bounds.Depth; z++)
            {
                for (int x = 0; x < bounds.Width; x++)
                    highestBlockerY[Index(x, z)] = ScanDown(x, z, bounds.Height - 1);
            }
        }

        // True when no light-blocking block sits strictly above the cell (matches the legacy
        // column walk in VoxelLightSampler.HasSkyAccess).
        public bool HasSkyAccess(BlockPosition position) =>
            highestBlockerY[Index(position.X, position.Z)] <= position.Y;

        // Applies a block change to the map; returns true when the column's highest blocker
        // moved, i.e. the edit changed which cells below can see the sky.
        public bool ApplyChange(BlockChange change, out int previousTop, out int newTop)
        {
            int index = Index(change.Position.X, change.Position.Z);
            previousTop = highestBlockerY[index];

            bool blocksLight = IsLightBlocking(change.NewBlock);
            if (blocksLight && change.Position.Y > previousTop)
                highestBlockerY[index] = change.Position.Y;
            else if (!blocksLight && change.Position.Y == previousTop)
                highestBlockerY[index] = ScanDown(change.Position.X, change.Position.Z, change.Position.Y - 1);

            newTop = highestBlockerY[index];
            return newTop != previousTop;
        }

        int ScanDown(int x, int z, int fromY)
        {
            for (int y = fromY; y >= 0; y--)
            {
                if (IsLightBlocking(world.GetBlock(new BlockPosition(x, y, z))))
                    return y;
            }

            return -1;
        }

        // Inverse of VoxelLightSampler.IsLightPassable: only rendered solid blocks shade.
        bool IsLightBlocking(BlockId block)
        {
            BlockDefinition definition = registry.Get(block);
            return definition.IsRenderable && definition.IsSolid;
        }

        int Index(int x, int z) => x + world.Bounds.Width * z;
    }
}
