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
        // Topmost block that stops light DEAD (LightTransmission == 0). Leaves are excluded, which
        // is what lets a canopy shade without blacking out.
        readonly int[] highestOpaqueY;
        // Product of the transmissions of every partially-transmitting block above highestOpaqueY.
        readonly float[] canopyTransmittance;

        public VoxelSkyLightMap(VoxelWorld world, BlockRegistry registry)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            int columns = world.Bounds.Width * world.Bounds.Depth;
            highestBlockerY = new int[columns];
            highestOpaqueY = new int[columns];
            canopyTransmittance = new float[columns];
            Rebuild();
        }

        public void Rebuild()
        {
            WorldBounds bounds = world.Bounds;
            for (int z = 0; z < bounds.Depth; z++)
            {
                for (int x = 0; x < bounds.Width; x++)
                    ScanColumn(x, z);
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

            // Rescans the whole column rather than nudging the top incrementally. The column is at
            // most WorldMaxY tall and edits happen at player rate, so the cost is irrelevant, and
            // the incremental form now has THREE values to keep consistent (top, opaque top and
            // the transmittance product) where a missed case is a lighting artefact nobody traces
            // back to a block placement.
            ScanColumn(change.Position.X, change.Position.Z);

            newTop = highestBlockerY[index];
            return newTop != previousTop;
        }

        /// <summary>How much skylight reaches a cell, 0..1. 1 is open sky.
        ///
        /// Below the topmost opaque blocker this is 0 — a cell under a roof is genuinely dark. Above
        /// it, the value is the product of the transmissions of the canopy layers overhead, so a
        /// forest floor is shaded but not black.</summary>
        public float SkyTransmittance(BlockPosition position)
        {
            int index = Index(position.X, position.Z);

            if (position.Y < highestOpaqueY[index])
                return 0.0f;

            return canopyTransmittance[index];
        }

        // One pass per column, computing all three values together so they cannot disagree.
        void ScanColumn(int x, int z)
        {
            int index = Index(x, z);
            int top = -1;
            int opaqueTop = -1;
            float transmittance = 1.0f;

            for (int y = world.Bounds.Height - 1; y >= 0; y--)
            {
                BlockDefinition definition = registry.Get(world.GetBlock(new BlockPosition(x, y, z)));

                if (!definition.IsRenderable || !definition.BlocksLight)
                    continue;

                if (top < 0)
                    top = y;

                if (definition.LightTransmission <= 0.0f)
                {
                    // Opaque: everything below is fully shadowed, and canopy above it is what the
                    // accumulated transmittance already describes. Nothing lower can matter.
                    opaqueTop = y;
                    break;
                }

                transmittance *= definition.LightTransmission;
            }

            highestBlockerY[index] = top;
            highestOpaqueY[index] = opaqueTop;
            canopyTransmittance[index] = transmittance;
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
            return definition.IsRenderable && definition.BlocksLight;
        }

        int Index(int x, int z) => x + world.Bounds.Width * z;
    }
}
