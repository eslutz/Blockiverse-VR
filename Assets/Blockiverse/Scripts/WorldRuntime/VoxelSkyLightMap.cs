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
        // Lowest transmitting block above highestOpaqueY, i.e. where the canopy stops. Cells below
        // it are beneath the WHOLE canopy, so the cached column product is exactly their answer and
        // no walk is needed. int.MinValue when the column has no canopy.
        readonly int[] lowestCanopyY;
        // Product over every transmitting layer in the column. Restored after being removed: it is
        // the O(1) answer for the forest FLOOR, which is most of the calls.
        readonly float[] canopyTransmittance;

        /// <summary>
        /// The share of skylight that reaches a cell no matter how much canopy is above it.
        /// </summary>
        /// <remarks>
        /// The layer product below is Beer–Lambert for a SINGLE VERTICAL RAY, and skylight is not
        /// one ray — it arrives from the whole hemisphere, so light that a straight-down path says
        /// is extinguished still gets in from the side, through gaps two cells over, off the
        /// ground. A pure product therefore over-darkens without limit: leafmoss transmits 0.45, so
        /// four stacked layers give 0.041 and a fifth gives 0.018 — within a few percent of the
        /// zero a sealed room gets, which puts a wood at noon and a cave in the same bucket. That is exactly what "once you start stacking layers of
        /// canopy it still gets pretty dark pretty quick in the woods, even midday on a clear day"
        /// was, and no per-layer transmission value fixes it — halving the extinction only moves
        /// which layer count goes black.
        ///
        /// A floor is the cheap stand-in for that side-scattered term: it saturates, which is what
        /// real canopy shade does. 0.25 keeps a deep wood clearly shaded and clearly readable, and
        /// still leaves the first few layers doing visible work (one layer 0.59, two 0.40, three
        /// 0.32, four 0.28, asymptote 0.25) so a thin crown and a dense one do not look the same.
        ///
        /// THIS IS ONE VALUE, and gameplay reads it too — deliberately (Eric's call, 2026-08-25):
        /// a single number is easier to reason about and to debug than a rendering value and a
        /// gameplay value that can silently drift apart. It does move farming, and the move is
        /// bounded, which is why it is acceptable rather than merely convenient. On the 0-15 scale
        /// FarmingService requires grain 8, berries 7, reeds 5:
        ///
        ///   layers   0      1      2      3      4     ->
        ///   light    15.00  8.81   6.03   4.78   4.21   3.75
        ///   grain    yes    YES*   no     no     no     no
        ///   berry    yes    YES*   no     no     no     no
        ///   reed     yes    yes    YES*   no     no     no
        ///
        /// So a player gains one layer of canopy for grain and berries and two for reeds, and
        /// nothing past that: the asymptote (3.75) sits BELOW the least demanding crop's minimum
        /// (5), so however thick the canopy gets, farming under it still stops. You can plant at
        /// the fringe of a tree, not inside a forest. If that ever needs tightening the lever is
        /// the crop's own minLight in FarmingService, not this constant.
        /// </remarks>
        public const float DiffuseCanopyFloor = 0.25f;

        public VoxelSkyLightMap(VoxelWorld world, BlockRegistry registry)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            int columns = world.Bounds.Width * world.Bounds.Depth;
            highestBlockerY = new int[columns];
            highestOpaqueY = new int[columns];
            lowestCanopyY = new int[columns];
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

        // Applies a block change to the map; returns true when the column's sky PROFILE moved —
        // not just the topmost blocker, but the opaque top or the canopy transmittance beneath it
        // too. An edit below an existing higher leaf (add/remove an opaque block, or another leaf,
        // under a canopy) leaves highestBlockerY untouched while still changing what every cell
        // beneath it sees, so the top alone under-reports which columns need a full rebuild.
        public bool ApplyChange(BlockChange change, out int previousTop, out int newTop)
        {
            int index = Index(change.Position.X, change.Position.Z);
            previousTop = highestBlockerY[index];
            int previousOpaqueTop = highestOpaqueY[index];
            int previousLowestCanopy = lowestCanopyY[index];
            float previousTransmittance = canopyTransmittance[index];

            // Rescans the whole column rather than nudging the top incrementally. The column is at
            // most WorldMaxY tall and edits happen at player rate, so the cost is irrelevant, and
            // the incremental form has values to keep consistent (top, opaque top, canopy profile)
            // where a missed case is a lighting artefact nobody traces back to a block placement.
            ScanColumn(change.Position.X, change.Position.Z);

            newTop = highestBlockerY[index];

            return newTop != previousTop
                || highestOpaqueY[index] != previousOpaqueTop
                || lowestCanopyY[index] != previousLowestCanopy
                || canopyTransmittance[index] != previousTransmittance;
        }

        /// <summary>How much skylight reaches a cell, 0..1. 1 is open sky.
        ///
        /// ONE VALUE, used by the mesher and by gameplay alike (Eric's call, 2026-08-25): a single
        /// number is easier to reason about and to debug than a rendering value and a gameplay
        /// value that can drift apart. What it costs is recorded on
        /// <see cref="DiffuseCanopyFloor"/>, and it is bounded.
        ///
        /// Below the topmost opaque blocker this is 0 — a cell under a roof is genuinely dark.
        ///
        /// COUNTS ONLY WHAT IS ABOVE THE CELL. A single cached product per column is right for a
        /// cell beneath the whole canopy and wrong for every cell inside it: a cell level with the
        /// middle of a crown was charged for the leaves BELOW it as well, so the interior and
        /// underside of every canopy — the faces the mesher bakes and the player stands among —
        /// came out several times darker than the model intended.
        ///
        /// BOTH cases are handled, and that matters for cost. The walk is NOT bounded by canopy
        /// depth on its own: it runs from the topmost blocker down to the queried cell, and a tall
        /// tree over open ground puts those 20-25 blocks apart, on a path the mesher calls per face.
        /// Cells below the canopy — the forest floor, i.e. most calls — take the cached column
        /// product in O(1) instead, and only cells INSIDE the crown walk, where the bound really is
        /// the crown's own extent.</summary>
        public float SkyTransmittance(BlockPosition position)
        {
            int index = Index(position.X, position.Z);

            if (position.Y < highestOpaqueY[index])
                return 0.0f;

            int top = highestBlockerY[index];
            if (position.Y >= top)
                return 1.0f;

            // ONE return for both paths, and that is structural rather than tidy. The first
            // version of this had the cached branch return directly, so the fast path — which is
            // the cell BELOW the whole canopy, i.e. the forest floor, i.e. the exact place the
            // floor exists to fix — came back as the bare product while cells inside the crown
            // came back floored. The ground under a tree was unlit and the tree's own interior was
            // fine, which is precisely backwards. Compute the product, then floor once.
            if (position.Y < lowestCanopyY[index])
                return WithDiffuseFloor(canopyTransmittance[index]);

            float product = 1.0f;

            for (int y = top; y > position.Y; y--)
            {
                BlockDefinition definition = registry.Get(world.GetBlock(new BlockPosition(position.X, y, position.Z)));

                if (!definition.IsRenderable || !definition.BlocksLight)
                    continue;

                // Guard on the invariant, not a case the scan can produce: highestOpaqueY is the
                // TOPMOST opaque block, and we only get here with position.Y at or above it, so
                // nothing in this range is opaque. Kept because the alternative failure is silent
                // — a zero would multiply in and then be lifted back to the floor, i.e. a solid
                // roof quietly transmitting a quarter of the daylight.
                if (definition.LightTransmission <= 0.0f)
                    return 0.0f;

                product *= definition.LightTransmission;
            }

            return WithDiffuseFloor(product);
        }

        /// <summary>Lifts a raw canopy product to the transmittance the game uses.
        ///
        /// Every path out of <see cref="SkyTransmittance"/> that can carry canopy goes through
        /// here, so no future shortcut can return an unfloored value the way the cached path once
        /// did.</summary>
        static float WithDiffuseFloor(float product)
        {
            if (product >= 1.0f)
                return 1.0f;

            if (product <= 0.0f)
                return 0.0f;

            return DiffuseCanopyFloor + (1.0f - DiffuseCanopyFloor) * product;
        }

        // One pass per column, computing every value together so they cannot disagree.
        void ScanColumn(int x, int z)
        {
            int index = Index(x, z);
            int top = -1;
            int opaqueTop = -1;
            int lowestCanopy = int.MaxValue;
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
                    // Opaque: everything below is fully shadowed. Nothing lower can matter.
                    opaqueTop = y;
                    break;
                }

                lowestCanopy = y;
                transmittance *= definition.LightTransmission;
            }

            highestBlockerY[index] = top;
            highestOpaqueY[index] = opaqueTop;
            lowestCanopyY[index] = lowestCanopy == int.MaxValue ? int.MinValue : lowestCanopy;
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
