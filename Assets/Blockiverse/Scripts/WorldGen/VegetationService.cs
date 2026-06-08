using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public sealed class VegetationService
    {
        const int SaplingGrowthIntervalTicks = 1200;
        const int LeafDecayMaxDistance = 5;
        const int LeafDecayIntervalTicks = 120;

        readonly Dictionary<BlockPosition, int> saplingTicks = new();
        int leafDecayAccumulator;

        // ── 7 tree variants ─────────────────────────────────────────────────

        public void PlaceStandardTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 4)) return;
            PlaceTrunk(world, basePos, trunkHeight: 4);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 4, basePos.Z), radius: 2, layers: 2);
        }

        public void PlaceTallTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 7)) return;
            PlaceTrunk(world, basePos, trunkHeight: 7);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 7, basePos.Z), radius: 1, layers: 2);
        }

        public void PlaceConicalTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 6)) return;
            PlaceTrunk(world, basePos, trunkHeight: 6);
            // Layered conical canopy: each layer one block smaller
            for (int layer = 0; layer < 4; layer++)
            {
                int r = 3 - layer;
                var center = new BlockPosition(basePos.X, basePos.Y + 3 + layer, basePos.Z);
                PlaceCanopySquare(world, center, radius: r);
            }
        }

        public void PlaceShrubTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 2)) return;
            PlaceTrunk(world, basePos, trunkHeight: 2);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 2, basePos.Z), radius: 3, layers: 1);
        }

        public void PlaceWillowTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 5)) return;
            PlaceTrunk(world, basePos, trunkHeight: 5);
            var canopyBase = new BlockPosition(basePos.X, basePos.Y + 5, basePos.Z);
            PlaceCanopyRound(world, canopyBase, radius: 3, layers: 2);
            // Drooping leaves one below canopy edge
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 4, basePos.Z), radius: 2, layers: 1);
        }

        public void PlaceSparseTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 8)) return;
            PlaceTrunk(world, basePos, trunkHeight: 8);
            PlaceCanopySquare(world, new BlockPosition(basePos.X, basePos.Y + 8, basePos.Z), radius: 1);
            PlaceCanopySquare(world, new BlockPosition(basePos.X, basePos.Y + 9, basePos.Z), radius: 1);
        }

        public void PlaceMassiveTree(VoxelWorld world, BlockPosition basePos)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 6)) return;
            // 2×2 trunk
            for (int dy = 0; dy < 6; dy++)
            for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++)
                TrySetBlock(world, new BlockPosition(basePos.X + dx, basePos.Y + dy, basePos.Z + dz), BlockRegistry.BranchwoodLog);

            var canopyBase = new BlockPosition(basePos.X, basePos.Y + 6, basePos.Z);
            PlaceCanopyRound(world, canopyBase, radius: 4, layers: 3);
        }

        // ── Sapling growth ───────────────────────────────────────────────────

        public void TrackSapling(BlockPosition position)
        {
            saplingTicks[position] = 0;
        }

        public void TickSapling(VoxelWorld world, int ticks)
        {
            if (ticks <= 0) return;

            var toRemove  = new List<BlockPosition>();
            var toAdvance = new List<BlockPosition>();
            var toUpdate  = new List<(BlockPosition pos, int value)>();

            foreach (var kv in saplingTicks)
            {
                BlockId block = world.GetBlock(kv.Key);
                if (block != BlockRegistry.Sapling && block != BlockRegistry.Sapling_S1 && block != BlockRegistry.Sapling_S2)
                {
                    toRemove.Add(kv.Key);
                    continue;
                }

                int accumulated = kv.Value + ticks;
                if (accumulated >= SaplingGrowthIntervalTicks)
                    toAdvance.Add(kv.Key);
                else
                    toUpdate.Add((kv.Key, accumulated));
            }

            foreach (var pos in toRemove)         saplingTicks.Remove(pos);
            foreach (var (pos, val) in toUpdate)  saplingTicks[pos] = val;
            foreach (var pos in toAdvance)         AdvanceSapling(world, pos);
        }

        void AdvanceSapling(VoxelWorld world, BlockPosition pos)
        {
            BlockId current = world.GetBlock(pos);
            if (current == BlockRegistry.Sapling)
            {
                world.SetBlock(pos, BlockRegistry.Sapling_S1);
                saplingTicks[pos] = 0;
            }
            else if (current == BlockRegistry.Sapling_S1)
            {
                world.SetBlock(pos, BlockRegistry.Sapling_S2);
                saplingTicks[pos] = 0;
            }
            else if (current == BlockRegistry.Sapling_S2)
            {
                world.SetBlock(pos, BlockRegistry.Air);
                saplingTicks.Remove(pos);
                PlaceStandardTree(world, pos);
            }
        }

        // ── Leaf decay ───────────────────────────────────────────────────────

        public void TickLeafDecay(VoxelWorld world, int ticks)
        {
            if (ticks <= 0) return;

            leafDecayAccumulator += ticks;
            if (leafDecayAccumulator < LeafDecayIntervalTicks)
                return;

            leafDecayAccumulator -= LeafDecayIntervalTicks;

            // O(W×H×D) scan per decay interval. Acceptable for current world sizes; use a
            // dirty-block set if worlds grow significantly larger.
            WorldBounds bounds = world.Bounds;
            for (int y = 0; y < bounds.Height; y++)
            for (int z = 0; z < bounds.Depth; z++)
            for (int x = 0; x < bounds.Width; x++)
            {
                var pos = new BlockPosition(x, y, z);
                if (world.GetBlock(pos) != BlockRegistry.Leafmoss)
                    continue;

                if (!HasNearbyLog(world, pos, LeafDecayMaxDistance))
                    world.SetBlock(pos, BlockRegistry.Air);
            }
        }

        // Searches a cube of half-extent maxDist, not a sphere. Corner-diagonal leaves
        // (up to maxDist * √3 actual distance) are preserved — intentional, consistent behaviour.
        static bool HasNearbyLog(VoxelWorld world, BlockPosition pos, int maxDist)
        {
            int x0 = Math.Max(0, pos.X - maxDist);
            int x1 = Math.Min(world.Bounds.Width - 1, pos.X + maxDist);
            int y0 = Math.Max(0, pos.Y - maxDist);
            int y1 = Math.Min(world.Bounds.Height - 1, pos.Y + maxDist);
            int z0 = Math.Max(0, pos.Z - maxDist);
            int z1 = Math.Min(world.Bounds.Depth - 1, pos.Z + maxDist);

            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (world.GetBlock(new BlockPosition(x, y, z)) == BlockRegistry.BranchwoodLog)
                    return true;
            }

            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static void PlaceTrunk(VoxelWorld world, BlockPosition basePos, int trunkHeight)
        {
            for (int dy = 0; dy < trunkHeight; dy++)
                TrySetBlock(world, new BlockPosition(basePos.X, basePos.Y + dy, basePos.Z), BlockRegistry.BranchwoodLog);
        }

        static void PlaceCanopyRound(VoxelWorld world, BlockPosition center, int radius, int layers)
        {
            for (int layer = 0; layer < layers; layer++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz <= radius * radius + 1)
                    TrySetBlock(world, new BlockPosition(center.X + dx, center.Y + layer, center.Z + dz), BlockRegistry.Leafmoss);
            }
        }

        static void PlaceCanopySquare(VoxelWorld world, BlockPosition center, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
                TrySetBlock(world, new BlockPosition(center.X + dx, center.Y, center.Z + dz), BlockRegistry.Leafmoss);
        }

        static bool TrunkClear(VoxelWorld world, BlockPosition basePos, int trunkHeight)
        {
            for (int dy = 0; dy < trunkHeight; dy++)
            {
                var pos = new BlockPosition(basePos.X, basePos.Y + dy, basePos.Z);
                if (!world.Bounds.Contains(pos) || world.GetBlock(pos) != BlockRegistry.Air)
                    return false;
            }
            return true;
        }

        static void TrySetBlock(VoxelWorld world, BlockPosition pos, BlockId block)
        {
            if (world.Bounds.Contains(pos) && world.GetBlock(pos) == BlockRegistry.Air)
                world.SetBlock(pos, block, trackChange: false);
        }
    }
}
