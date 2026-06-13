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

        // Leaf decay works off a candidate set instead of a full-world scan per interval:
        // seeded once from the world, then maintained incrementally as logs are removed and
        // Leafmoss is placed. Supported leaves drop out of the set after a check and are
        // re-marked when a nearby log disappears.
        readonly HashSet<BlockPosition> leafDecayCandidates = new();
        readonly List<BlockPosition> leafDecayScratch = new();
        bool leafDecayCandidatesSeeded;

        // Scratch lists reused across TickSapling calls to avoid per-tick allocations.
        readonly List<BlockPosition> saplingRemoveScratch = new();
        readonly List<(BlockPosition pos, int accumulated)> saplingAdvanceScratch = new();
        readonly List<(BlockPosition pos, int value)> saplingUpdateScratch = new();

        // Scratch list for ScanAndTrackSaplings' flat-array sweep (world init / late join).
        readonly List<BlockPosition> saplingScanScratch = new();

        Func<int, int, int> biomeResolver; // (x, z) → TerrainBiome as int; null = default Meadow

        public void Configure(Func<int, int, int> biomeAt)
        {
            biomeResolver = biomeAt;
        }

        public static bool IsLeafSupportBlock(BlockId block) =>
            block == BlockRegistry.BranchwoodLog || block == BlockRegistry.SmoothBranchwood;

        // ── 7 tree variants ─────────────────────────────────────────────────

        public void PlaceStandardTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 4)) return;
            PlaceTrunk(world, basePos, trunkHeight: 4, trackChange: trackChange);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 4, basePos.Z), radius: 2, layers: 2, trackChange: trackChange);
        }

        public void PlaceTallTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 7)) return;
            PlaceTrunk(world, basePos, trunkHeight: 7, trackChange: trackChange);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 7, basePos.Z), radius: 1, layers: 2, trackChange: trackChange);
        }

        public void PlaceConicalTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 6)) return;
            PlaceTrunk(world, basePos, trunkHeight: 6, trackChange: trackChange);
            // Layered conical canopy: each layer one block smaller
            for (int layer = 0; layer < 4; layer++)
            {
                int r = 3 - layer;
                var center = new BlockPosition(basePos.X, basePos.Y + 3 + layer, basePos.Z);
                PlaceCanopySquare(world, center, radius: r, trackChange: trackChange);
            }
        }

        public void PlaceShrubTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 2)) return;
            PlaceTrunk(world, basePos, trunkHeight: 2, trackChange: trackChange);
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 2, basePos.Z), radius: 3, layers: 1, trackChange: trackChange);
        }

        public void PlaceWillowTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 5)) return;
            PlaceTrunk(world, basePos, trunkHeight: 5, trackChange: trackChange);
            var canopyBase = new BlockPosition(basePos.X, basePos.Y + 5, basePos.Z);
            PlaceCanopyRound(world, canopyBase, radius: 3, layers: 2, trackChange: trackChange);
            // Drooping leaves one below canopy edge
            PlaceCanopyRound(world, new BlockPosition(basePos.X, basePos.Y + 4, basePos.Z), radius: 2, layers: 1, trackChange: trackChange);
        }

        public void PlaceSparseTree(VoxelWorld world, BlockPosition basePos, bool trackChange = false)
        {
            if (!TrunkClear(world, basePos, trunkHeight: 8)) return;
            PlaceTrunk(world, basePos, trunkHeight: 8, trackChange: trackChange);
            PlaceCanopySquare(world, new BlockPosition(basePos.X, basePos.Y + 8, basePos.Z), radius: 1, trackChange: trackChange);
            PlaceCanopySquare(world, new BlockPosition(basePos.X, basePos.Y + 9, basePos.Z), radius: 1, trackChange: trackChange);
        }

        // ── Biome-aware tree dispatch ────────────────────────────────────────

        void PlaceBiomeTree(VoxelWorld world, BlockPosition pos, TerrainBiome biome, bool trackChange = false)
        {
            switch (biome)
            {
                case TerrainBiome.Pinewild:   PlaceConicalTree(world, pos, trackChange);  break;
                case TerrainBiome.Wetland:    PlaceWillowTree(world, pos, trackChange);   break;
                case TerrainBiome.Drybrush:   PlaceShrubTree(world, pos, trackChange);    break;
                case TerrainBiome.Tundra:     PlaceSparseTree(world, pos, trackChange);   break;
                case TerrainBiome.Highlands:  PlaceTallTree(world, pos, trackChange);     break;
                case TerrainBiome.Dunes:      PlaceShrubTree(world, pos, trackChange);    break;
                default:                      PlaceStandardTree(world, pos, trackChange); break;
            }
        }

        // ── Sapling growth ───────────────────────────────────────────────────

        public void ScanAndTrackSaplings(VoxelWorld world)
        {
            // Prune entries for positions no longer holding sapling blocks.
            var toRemove = new List<BlockPosition>();
            foreach (BlockPosition pos in saplingTicks.Keys)
            {
                BlockId b = world.GetBlock(pos);
                if (b != BlockRegistry.Sapling && b != BlockRegistry.Sapling_S1 && b != BlockRegistry.Sapling_S2)
                    toRemove.Add(pos);
            }
            foreach (BlockPosition pos in toRemove)
                saplingTicks.Remove(pos);

            // Add newly found saplings without resetting ticks for already-tracked ones.
            // Flat-array sweeps (see VoxelWorld.CollectBlockPositions) — a per-position GetBlock
            // scan over a full-size world (4M+ blocks) stalls the main thread on world init,
            // including the late-join client finalize path.
            saplingScanScratch.Clear();
            world.CollectBlockPositions(BlockRegistry.Sapling, saplingScanScratch);
            world.CollectBlockPositions(BlockRegistry.Sapling_S1, saplingScanScratch);
            world.CollectBlockPositions(BlockRegistry.Sapling_S2, saplingScanScratch);
            foreach (BlockPosition pos in saplingScanScratch)
            {
                if (!saplingTicks.ContainsKey(pos))
                    saplingTicks[pos] = 0;
            }
            saplingScanScratch.Clear();
        }

        public void TrackSapling(BlockPosition position)
        {
            if (!saplingTicks.ContainsKey(position))
                saplingTicks[position] = 0;
        }

        public void TickSapling(VoxelWorld world, int ticks)
        {
            if (ticks <= 0) return;

            // Reused scratch lists — this runs on every world tick batch, so per-call
            // allocations would add steady GC pressure.
            List<BlockPosition> toRemove = saplingRemoveScratch;
            List<(BlockPosition pos, int accumulated)> toAdvance = saplingAdvanceScratch;
            List<(BlockPosition pos, int value)> toUpdate = saplingUpdateScratch;
            toRemove.Clear();
            toAdvance.Clear();
            toUpdate.Clear();

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
                    toAdvance.Add((kv.Key, accumulated));
                else
                    toUpdate.Add((kv.Key, accumulated));
            }

            foreach (var pos in toRemove)         saplingTicks.Remove(pos);
            foreach (var (pos, val) in toUpdate)  saplingTicks[pos] = val;
            foreach (var (pos, accumulated) in toAdvance) AdvanceSapling(world, pos, accumulated - SaplingGrowthIntervalTicks);
        }

        void AdvanceSapling(VoxelWorld world, BlockPosition pos, int remainder)
        {
            while (true)
            {
                BlockId current = world.GetBlock(pos);
                if (current == BlockRegistry.Sapling)
                {
                    world.SetBlock(pos, BlockRegistry.Sapling_S1);
                }
                else if (current == BlockRegistry.Sapling_S1)
                {
                    world.SetBlock(pos, BlockRegistry.Sapling_S2);
                }
                else if (current == BlockRegistry.Sapling_S2)
                {
                    world.SetBlock(pos, BlockRegistry.Air);
                    int biomeIndex = biomeResolver != null ? biomeResolver(pos.X, pos.Z) : 0;
                    PlaceBiomeTree(world, pos, (TerrainBiome)biomeIndex, trackChange: true);

                    // Each biome tree checks its own trunk clearance (4–8 blocks) and silently
                    // no-ops when blocked; in that case the trunk base is still Air — restore the
                    // mature sapling and retry next interval instead of destroying it.
                    if (world.GetBlock(pos) == BlockRegistry.Air)
                    {
                        world.SetBlock(pos, BlockRegistry.Sapling_S2);
                        saplingTicks[pos] = remainder;
                        return;
                    }

                    saplingTicks.Remove(pos);
                    return;
                }
                else
                {
                    saplingTicks[pos] = remainder;
                    return;
                }

                if (remainder < SaplingGrowthIntervalTicks)
                {
                    saplingTicks[pos] = remainder;
                    return;
                }
                remainder -= SaplingGrowthIntervalTicks;
            }
        }

        // ── Wild regrowth queue ──────────────────────────────────────────────

        public readonly struct WildRegrowthMarker
        {
            public readonly BlockId BlockId;
            public readonly BlockPosition Position;
            public readonly long RegrowAfterTick;  // absolute world tick when regrowth may happen
            public readonly int AttemptsLeft;

            public WildRegrowthMarker(BlockId blockId, BlockPosition position, long regrowAfterTick, int attemptsLeft = 5)
            {
                BlockId = blockId;
                Position = position;
                RegrowAfterTick = regrowAfterTick;
                AttemptsLeft = attemptsLeft;
            }
        }

        const long WildRegrowthRetryDelayTicks = SimulationTime.TicksPerDay;

        readonly List<WildRegrowthMarker> wildRegrowthQueue = new();

        // Call when a wild plant is harvested. currentTick is the absolute world time in ticks
        // (long, matching WorldTimeClock.TotalElapsedTicks).
        public void MarkWildHarvest(BlockId blockId, BlockPosition position, long currentTick)
        {
            int delay = WildRegrowthDelayTicks(blockId);
            if (delay <= 0) return;
            wildRegrowthQueue.Add(new WildRegrowthMarker(blockId, position, currentTick + delay));
        }

        // Call every game tick with the current absolute world time. Restores harvested wild plants.
        public void TickWildRegrowth(VoxelWorld world, long currentTick)
        {
            for (int i = wildRegrowthQueue.Count - 1; i >= 0; i--)
            {
                WildRegrowthMarker marker = wildRegrowthQueue[i];
                if (currentTick < marker.RegrowAfterTick) continue;

                BlockId current = world.GetBlock(marker.Position);
                if (current != BlockRegistry.Air)
                {
                    // Position was filled by something else — remove marker.
                    wildRegrowthQueue.RemoveAt(i);
                    continue;
                }

                BlockPosition below = new BlockPosition(marker.Position.X, marker.Position.Y - 1, marker.Position.Z);
                bool hasGround = world.Bounds.Contains(below) && world.GetBlock(below) != BlockRegistry.Air;

                if (hasGround)
                {
                    world.SetBlock(marker.Position, marker.BlockId);
                    wildRegrowthQueue.RemoveAt(i);
                }
                else if (marker.AttemptsLeft > 1)
                {
                    // Spot is unsupported (no ground below) — delay and retry.
                    wildRegrowthQueue[i] = new WildRegrowthMarker(
                        marker.BlockId, marker.Position,
                        currentTick + WildRegrowthRetryDelayTicks,
                        marker.AttemptsLeft - 1);
                }
                else
                {
                    wildRegrowthQueue.RemoveAt(i);
                }
            }
        }

        // Exposes current regrowth queue count for tests and save/load.
        public int WildRegrowthQueueCount => wildRegrowthQueue.Count;

        // ── Save/load (world persistence) ────────────────────────────────────

        // Snapshot of per-sapling growth progress (accumulated ticks toward the next stage).
        public IReadOnlyList<(BlockPosition position, int accumulatedTicks)> ExportSaplingProgress()
        {
            var result = new List<(BlockPosition, int)>(saplingTicks.Count);
            foreach (KeyValuePair<BlockPosition, int> entry in saplingTicks)
                result.Add((entry.Key, entry.Value));
            return result;
        }

        // Replaces the sapling tracker with saved progress. Entries are validated against the
        // world on the next tick (TickSapling prunes positions that no longer hold saplings).
        public void RestoreSaplingProgress(IEnumerable<(BlockPosition position, int accumulatedTicks)> entries)
        {
            saplingTicks.Clear();
            if (entries == null)
                return;

            foreach ((BlockPosition position, int accumulatedTicks) in entries)
                saplingTicks[position] = Math.Max(0, accumulatedTicks);
        }

        // Snapshot of the pending wild-regrowth markers (absolute-tick deadlines).
        public IReadOnlyList<WildRegrowthMarker> ExportWildRegrowth()
        {
            return wildRegrowthQueue.ToArray();
        }

        // Replaces the wild-regrowth queue with saved markers.
        public void RestoreWildRegrowth(IEnumerable<WildRegrowthMarker> markers)
        {
            wildRegrowthQueue.Clear();
            if (markers == null)
                return;

            wildRegrowthQueue.AddRange(markers);
        }

        static int WildRegrowthDelayTicks(BlockId blockId)
        {
            if (blockId == BlockRegistry.Berrybush)   return 2 * SimulationTime.TicksPerDay;
            if (blockId == BlockRegistry.GrainStalk)  return 3 * SimulationTime.TicksPerDay;
            if (blockId == BlockRegistry.Reedgrass)   return SimulationTime.TicksPerDay;
            if (blockId == BlockRegistry.Thornbrush)  return 4 * SimulationTime.TicksPerDay;
            return 2 * SimulationTime.TicksPerDay; // default 2 days for any other small plant
        }

        // ── Leaf decay ───────────────────────────────────────────────────────

        // Marks a single Leafmoss position for the next decay check (call when Leafmoss is placed).
        public void MarkLeafDecayCandidate(BlockPosition position)
        {
            leafDecayCandidates.Add(position);
        }

        // Marks all Leafmoss within decay range of a removed log for re-checking — those leaves
        // may have just lost their last supporting log.
        public void MarkLeafDecayCandidates(VoxelWorld world, BlockPosition removedLogPosition)
        {
            int x0 = Math.Max(0, removedLogPosition.X - LeafDecayMaxDistance);
            int x1 = Math.Min(world.Bounds.Width - 1, removedLogPosition.X + LeafDecayMaxDistance);
            int y0 = Math.Max(0, removedLogPosition.Y - LeafDecayMaxDistance);
            int y1 = Math.Min(world.Bounds.Height - 1, removedLogPosition.Y + LeafDecayMaxDistance);
            int z0 = Math.Max(0, removedLogPosition.Z - LeafDecayMaxDistance);
            int z1 = Math.Min(world.Bounds.Depth - 1, removedLogPosition.Z + LeafDecayMaxDistance);

            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var pos = new BlockPosition(x, y, z);
                if (world.GetBlock(pos) == BlockRegistry.Leafmoss)
                    leafDecayCandidates.Add(pos);
            }
        }

        public void TickLeafDecay(VoxelWorld world, int ticks)
        {
            if (ticks <= 0) return;

            leafDecayAccumulator += ticks;
            if (leafDecayAccumulator < LeafDecayIntervalTicks)
                return;

            // Consume all elapsed intervals up front: decay is idempotent within a call (removing
            // an orphan leaf cannot orphan another — support comes from logs, not leaves), so one
            // candidate pass covers any number of elapsed intervals.
            leafDecayAccumulator %= LeafDecayIntervalTicks;

            // First sweep seeds the candidate set with every Leafmoss in the world (covers
            // worldgen canopies and loaded saves); afterwards the set is maintained
            // incrementally by the Mark* calls.
            if (!leafDecayCandidatesSeeded)
            {
                leafDecayCandidatesSeeded = true;
                SeedLeafDecayCandidates(world);
            }

            if (leafDecayCandidates.Count == 0)
                return;

            leafDecayScratch.Clear();
            leafDecayScratch.AddRange(leafDecayCandidates);
            leafDecayCandidates.Clear();

            foreach (BlockPosition pos in leafDecayScratch)
            {
                if (world.GetBlock(pos) != BlockRegistry.Leafmoss)
                    continue;

                if (!HasNearbyLog(world, pos, LeafDecayMaxDistance))
                    world.SetBlock(pos, BlockRegistry.Air);
                // Supported leaves leave the candidate set; MarkLeafDecayCandidates re-adds
                // them when a nearby log is removed.
            }
        }

        void SeedLeafDecayCandidates(VoxelWorld world)
        {
            // Single flat-array pass; per-position GetBlock over a full-size world (4M+ blocks)
            // pays a bounds check and index computation per block and stalls the first decay tick.
            world.CollectBlockPositions(BlockRegistry.Leafmoss, leafDecayCandidates);
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
                if (IsLeafSupportBlock(world.GetBlock(new BlockPosition(x, y, z))))
                    return true;
            }

            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static void PlaceTrunk(VoxelWorld world, BlockPosition basePos, int trunkHeight, bool trackChange)
        {
            for (int dy = 0; dy < trunkHeight; dy++)
                TrySetBlock(world, new BlockPosition(basePos.X, basePos.Y + dy, basePos.Z), BlockRegistry.BranchwoodLog, trackChange);
        }

        static void PlaceCanopyRound(VoxelWorld world, BlockPosition center, int radius, int layers, bool trackChange)
        {
            for (int layer = 0; layer < layers; layer++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz <= radius * radius + 1)
                    TrySetBlock(world, new BlockPosition(center.X + dx, center.Y + layer, center.Z + dz), BlockRegistry.Leafmoss, trackChange);
            }
        }

        static void PlaceCanopySquare(VoxelWorld world, BlockPosition center, int radius, bool trackChange)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
                TrySetBlock(world, new BlockPosition(center.X + dx, center.Y, center.Z + dz), BlockRegistry.Leafmoss, trackChange);
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

        static void TrySetBlock(VoxelWorld world, BlockPosition pos, BlockId block, bool trackChange)
        {
            if (world.Bounds.Contains(pos) && world.GetBlock(pos) == BlockRegistry.Air)
                world.SetBlock(pos, block, trackChange);
        }
    }
}
