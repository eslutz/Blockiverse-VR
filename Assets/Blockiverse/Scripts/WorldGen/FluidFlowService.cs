using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    // Deterministic fluid flow simulation (voxel_survival_ruleset §5.4 fluid behavior table):
    // sources spread flowing cells downward (fall first, horizontal distance resets) and outward
    // (distance decrements) on each family's tick cadence; flowing cells retract when they lose
    // their support gradient. Freshwater quenches emberflow sources into black basalt; emberflow
    // burns adjacent wood/foliage away.
    //
    // Everything here is a pure function of the synced world state, the synced world clock, and
    // the world seed — the host and every client (including late joiners, who reconstruct the
    // same world) simulate identical flow in lockstep with no flow traffic on the wire, the same
    // pattern as deterministic crop growth.
    public sealed class FluidFlowService
    {
        // Chance per emberflow step that each adjacent flammable block burns away.
        public const int IgnitionChancePercent = 25;

        static readonly BlockPosition Down = new(0, -1, 0);
        static readonly BlockPosition Up = new(0, 1, 0);
        static readonly BlockPosition[] HorizontalOffsets =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1),
        };

        // The six orthogonal neighbors, for the all-direction reaction scans (ignite/quench and
        // neighbor re-activation). Sharing one array avoids a per-call enumerator allocation.
        static readonly BlockPosition[] SixNeighbors =
        {
            new(0, 1, 0),
            new(0, -1, 0),
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1),
        };

        static readonly Comparison<BlockPosition> PositionComparison = ComparePositions;

        // Horizontal flow budget per flowing cell; sources implicitly carry the family maximum.
        readonly Dictionary<BlockPosition, int> flowLevels = new();
        // Cells that may act on their family's next step (spread, retract, ignite, quench),
        // partitioned by family so a step only walks its own family's cells.
        readonly HashSet<BlockPosition>[] active;
        readonly List<BlockPosition> processScratch = new();
        readonly Queue<BlockPosition> levelRefreshQueue = new();

        int worldSeed;
        // The absolute world tick the simulation has advanced to. Families step at absolute-tick
        // multiples of their cadence, so peers configured at different times (late joiners whose
        // clocks restore to the host's position) still step at the same world ticks.
        long lastWorldTick;

        public FluidFlowService()
        {
            active = new HashSet<BlockPosition>[FluidBlocks.FamilyCount];
            for (int i = 0; i < active.Length; i++)
                active[i] = new HashSet<BlockPosition>();
        }

        public int ActiveCellCount
        {
            get
            {
                int total = 0;
                foreach (HashSet<BlockPosition> set in active)
                    total += set.Count;
                return total;
            }
        }

        // Resumes the simulation from an externally restored absolute tick (save load, late-join
        // clock restore) WITHOUT replaying the elapsed ticks: the restored world is already the
        // post-tick state, so the next Tick advances only from here. Without this, a clock that
        // jumps forward after Configure would make the next Tick walk every elapsed tick.
        public void SyncToWorldTick(long worldTick)
        {
            lastWorldTick = worldTick;
        }

        // Full rebuild from the world: recompute flowing-cell levels by a multi-source BFS from
        // every source, then activate each fluid cell that can still act and every orphaned
        // flowing cell (no support path) so it retracts. `worldTick` is the synced clock's
        // current absolute tick (the simulation resumes from there).
        public void Configure(VoxelWorld world, int seed, long worldTick = 0)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            worldSeed = seed;
            lastWorldTick = worldTick;
            flowLevels.Clear();
            foreach (HashSet<BlockPosition> set in active)
                set.Clear();

            var sources = new List<BlockPosition>();
            var flowCells = new List<BlockPosition>();
            var frontier = new Queue<BlockPosition>();

            WorldBounds bounds = world.Bounds;
            for (int y = 0; y < bounds.Height; y++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    for (int x = 0; x < bounds.Width; x++)
                    {
                        var position = new BlockPosition(x, y, z);
                        BlockId block = world.GetBlock(position);

                        if (FluidBlocks.IsSource(block))
                        {
                            sources.Add(position);
                            frontier.Enqueue(position);
                        }
                        else if (FluidBlocks.IsFlow(block))
                        {
                            flowCells.Add(position);
                        }
                    }
                }
            }

            // Multi-source BFS over existing fluid cells: a flowing cell's level is the best
            // budget any source path grants it (falling resets the budget, horizontal steps
            // decrement it).
            while (frontier.Count > 0)
            {
                BlockPosition cell = frontier.Dequeue();
                BlockId block = world.GetBlock(cell);
                if (!FluidBlocks.TryGetFamily(block, out FluidFamily family))
                    continue;

                int level = FluidBlocks.IsSource(block)
                    ? FluidBlocks.FlowDistance(family)
                    : (flowLevels.TryGetValue(cell, out int known) ? known : 0);

                BlockPosition below = cell + Down;
                if (TryImproveLevel(world, family, below, FluidBlocks.FlowDistance(family)))
                    frontier.Enqueue(below);

                if (level >= 2)
                {
                    foreach (BlockPosition offset in HorizontalOffsets)
                    {
                        BlockPosition next = cell + offset;
                        if (TryImproveLevel(world, family, next, level - 1))
                            frontier.Enqueue(next);
                    }
                }
            }

            foreach (BlockPosition cell in flowCells)
            {
                FluidBlocks.TryGetFamily(world.GetBlock(cell), out FluidFamily family);

                // Orphans (no path from any source) retract on their first step.
                if (!flowLevels.ContainsKey(cell))
                    active[(int)family].Add(cell);
                else
                    ActivateIfActionable(world, family, cell);
            }

            // Activate the sources that still have something to do, reusing the source list from
            // the scan above instead of re-walking the whole world.
            foreach (BlockPosition source in sources)
            {
                FluidBlocks.TryGetFamily(world.GetBlock(source), out FluidFamily family);
                ActivateIfActionable(world, family, source);
            }
        }

        bool TryImproveLevel(VoxelWorld world, FluidFamily family, BlockPosition cell, int candidate)
        {
            if (!world.Bounds.Contains(cell) || candidate < 1)
                return false;

            BlockId block = world.GetBlock(cell);
            if (!FluidBlocks.IsFlow(block) ||
                !FluidBlocks.TryGetFamily(block, out FluidFamily cellFamily) ||
                cellFamily != family)
            {
                return false;
            }

            if (flowLevels.TryGetValue(cell, out int existing) && existing >= candidate)
                return false;

            flowLevels[cell] = candidate;
            return true;
        }

        // Activates a fluid cell during Configure if processing it would do anything: fall or
        // spread into open air, emberflow re-rolling ignition on a flammable in contact, or
        // freshwater quenching an emberflow source in contact. This must mirror the conditions
        // ProcessCell uses to KEEP a cell active, or a rebuilt sim (load/late-join) would leave a
        // walled-in emberflow/freshwater cell dormant that the continuous host keeps acting on.
        void ActivateIfActionable(VoxelWorld world, FluidFamily family, BlockPosition cell)
        {
            if (IsActionable(world, family, cell))
                active[(int)family].Add(cell);
        }

        bool IsActionable(VoxelWorld world, FluidFamily family, BlockPosition cell)
        {
            if (HasAirNeighbor(world, cell))
                return true;

            if (family == FluidFamily.Emberflow)
                return HasNeighborMatching(world, cell, IsFlammable);

            if (family == FluidFamily.Freshwater)
                return HasNeighborMatching(world, cell, IsEmberflowSource);

            return false;
        }

        // Air directly below (a fall) or horizontally (a spread). Air strictly above never
        // enables action, matching ProcessCell's fall/spread directions.
        bool HasAirNeighbor(VoxelWorld world, BlockPosition cell)
        {
            BlockPosition below = cell + Down;
            if (world.Bounds.Contains(below) && world.GetBlock(below) == BlockRegistry.Air)
                return true;

            foreach (BlockPosition offset in HorizontalOffsets)
            {
                BlockPosition next = cell + offset;
                if (world.Bounds.Contains(next) && world.GetBlock(next) == BlockRegistry.Air)
                    return true;
            }

            return false;
        }

        bool HasNeighborMatching(VoxelWorld world, BlockPosition cell, Func<BlockId, bool> predicate)
        {
            foreach (BlockPosition offset in SixNeighbors)
            {
                BlockPosition next = cell + offset;
                if (world.Bounds.Contains(next) && predicate(world.GetBlock(next)))
                    return true;
            }

            return false;
        }

        // Keeps the simulation state in step with world edits (player edits, replicated deltas,
        // retraction/spreading from this service itself routes through here too via the world's
        // change event — re-activating a just-written cell is a harmless no-op).
        public void OnBlockChanged(VoxelWorld world, BlockChange change)
        {
            BlockPosition cell = change.Position;

            if (FluidBlocks.TryGetFamily(change.NewBlock, out FluidFamily family))
            {
                if (FluidBlocks.IsSource(change.NewBlock))
                    flowLevels.Remove(cell);

                active[(int)family].Add(cell);
                RefreshFlowLevelsFrom(world, family, cell);
            }
            else
            {
                // The cell stopped being fluid (mined source, quenched, burned, overwritten).
                flowLevels.Remove(cell);
                RemoveActiveAllFamilies(cell);
            }

            // Neighbors may now spread into an opening, fall into it, or retract without it.
            ActivateFluidNeighbors(world, cell);
        }

        void RefreshFlowLevelsFrom(VoxelWorld world, FluidFamily family, BlockPosition origin)
        {
            if (!world.Bounds.Contains(origin) ||
                !FluidBlocks.TryGetFamily(world.GetBlock(origin), out FluidFamily originFamily) ||
                originFamily != family)
            {
                return;
            }

            if (!FluidBlocks.IsSource(world.GetBlock(origin)))
                TryImproveFlowLevelFromExistingSupport(world, family, origin);

            levelRefreshQueue.Clear();
            levelRefreshQueue.Enqueue(origin);

            while (levelRefreshQueue.Count > 0)
            {
                BlockPosition cell = levelRefreshQueue.Dequeue();
                BlockId block = world.GetBlock(cell);
                if (!FluidBlocks.TryGetFamily(block, out FluidFamily cellFamily) || cellFamily != family)
                    continue;

                int level = FluidBlocks.IsSource(block)
                    ? FluidBlocks.FlowDistance(family)
                    : (flowLevels.TryGetValue(cell, out int known) ? known : 0);

                BlockPosition below = cell + Down;
                if (TryImproveLevel(world, family, below, FluidBlocks.FlowDistance(family)))
                {
                    active[(int)family].Add(below);
                    levelRefreshQueue.Enqueue(below);
                }

                if (level < 2)
                    continue;

                foreach (BlockPosition offset in HorizontalOffsets)
                {
                    BlockPosition next = cell + offset;
                    if (TryImproveLevel(world, family, next, level - 1))
                    {
                        active[(int)family].Add(next);
                        levelRefreshQueue.Enqueue(next);
                    }
                }
            }
        }

        void TryImproveFlowLevelFromExistingSupport(VoxelWorld world, FluidFamily family, BlockPosition cell)
        {
            int bestLevel = 0;
            BlockPosition above = cell + Up;
            if (world.Bounds.Contains(above) &&
                FluidBlocks.TryGetFamily(world.GetBlock(above), out FluidFamily aboveFamily) &&
                aboveFamily == family)
            {
                bestLevel = FluidBlocks.FlowDistance(family);
            }

            foreach (BlockPosition offset in HorizontalOffsets)
            {
                BlockPosition next = cell + offset;
                if (!world.Bounds.Contains(next))
                    continue;

                BlockId neighbor = world.GetBlock(next);
                if (!FluidBlocks.TryGetFamily(neighbor, out FluidFamily neighborFamily) ||
                    neighborFamily != family)
                {
                    continue;
                }

                int neighborLevel = FluidBlocks.IsSource(neighbor)
                    ? FluidBlocks.FlowDistance(family)
                    : (flowLevels.TryGetValue(next, out int known) ? known : 0);

                if (neighborLevel >= 2)
                    bestLevel = Math.Max(bestLevel, neighborLevel - 1);
            }

            if (bestLevel > 0 && TryImproveLevel(world, family, cell, bestLevel))
                active[(int)family].Add(cell);
        }

        void ActivateFluidNeighbors(VoxelWorld world, BlockPosition cell)
        {
            foreach (BlockPosition offset in SixNeighbors)
                Activate(world, cell + offset);
        }

        void Activate(VoxelWorld world, BlockPosition cell)
        {
            if (world.Bounds.Contains(cell) &&
                FluidBlocks.TryGetFamily(world.GetBlock(cell), out FluidFamily family))
            {
                active[(int)family].Add(cell);
            }
        }

        void RemoveActiveAllFamilies(BlockPosition cell)
        {
            foreach (HashSet<BlockPosition> set in active)
                set.Remove(cell);
        }

        // Advances the simulation to the synced clock's absolute world tick (same contract as
        // FarmingService.TickGrowth). Ticks are walked one at a time so the family step
        // interleaving is a pure function of the absolute tick — peers that receive the same
        // ticks in different batch sizes (frame hitches, catch-up bursts) still run the exact
        // same step sequence, which keeps cross-family interactions (quench vs ignite)
        // identical everywhere.
        public void Tick(VoxelWorld world, long worldTick)
        {
            if (world == null || worldTick <= lastWorldTick)
                return;

            for (long tick = lastWorldTick + 1; tick <= worldTick; tick++)
            {
                for (int familyIndex = 0; familyIndex < FluidBlocks.FamilyCount; familyIndex++)
                {
                    var family = (FluidFamily)familyIndex;
                    if (tick % FluidBlocks.TickCadence(family) == 0)
                        StepFamily(world, family, tick);
                }
            }

            lastWorldTick = worldTick;
        }

        void StepFamily(VoxelWorld world, FluidFamily family, long tick)
        {
            HashSet<BlockPosition> familyActive = active[(int)family];
            if (familyActive.Count == 0)
                return;

            // Snapshot this family's active cells and sort for deterministic processing order
            // across peers.
            processScratch.Clear();
            processScratch.AddRange(familyActive);
            processScratch.Sort(PositionComparison);

            foreach (BlockPosition cell in processScratch)
                ProcessCell(world, family, cell, tick);
        }

        static int ComparePositions(BlockPosition a, BlockPosition b)
        {
            int compare = a.Y.CompareTo(b.Y);
            if (compare != 0) return compare;
            compare = a.Z.CompareTo(b.Z);
            if (compare != 0) return compare;
            return a.X.CompareTo(b.X);
        }

        void ProcessCell(VoxelWorld world, FluidFamily family, BlockPosition cell, long tick)
        {
            HashSet<BlockPosition> familyActive = active[(int)family];
            BlockId block = world.GetBlock(cell);
            if (!FluidBlocks.TryGetFamily(block, out FluidFamily cellFamily) || cellFamily != family)
            {
                familyActive.Remove(cell);
                return;
            }

            bool isSource = FluidBlocks.IsSource(block);
            int level = isSource
                ? FluidBlocks.FlowDistance(family)
                : (flowLevels.TryGetValue(cell, out int known) ? known : 0);

            // Retraction: a flowing cell needs a support gradient — fluid of its family directly
            // above (falling column) or a neighbor with a larger budget (path back to a source).
            if (!isSource && !HasSupport(world, family, cell, level))
            {
                world.SetBlock(cell, BlockRegistry.Air);
                flowLevels.Remove(cell);
                familyActive.Remove(cell);
                ActivateFluidNeighbors(world, cell);
                return;
            }

            bool acted = false;

            if (family == FluidFamily.Emberflow)
                acted |= TryIgniteNeighbors(world, cell, tick);
            else if (family == FluidFamily.Freshwater)
                acted |= TryQuenchAdjacentEmberflow(world, cell);

            // Fall first: pouring down resets the horizontal budget (§5.4 distances are
            // horizontal); the cell stays active to keep feeding the column.
            BlockPosition belowCell = cell + Down;
            bool belowInBounds = world.Bounds.Contains(belowCell);
            if (belowInBounds && world.GetBlock(belowCell) == BlockRegistry.Air)
            {
                world.SetBlock(belowCell, FluidBlocks.FlowOf(family));
                flowLevels[belowCell] = FluidBlocks.FlowDistance(family);
                active[(int)family].Add(belowCell);
                return;
            }

            // Feeding a column: while same-family fluid sits directly below, keep pouring into
            // it instead of spreading sideways — a midair source makes a single falling column,
            // not a fountain. Horizontal spread happens where the fluid rests on something else.
            if (belowInBounds &&
                FluidBlocks.TryGetFamily(world.GetBlock(belowCell), out FluidFamily belowFamily) &&
                belowFamily == family)
            {
                if (!acted)
                    familyActive.Remove(cell);
                return;
            }

            if (level >= 2)
            {
                foreach (BlockPosition offset in HorizontalOffsets)
                {
                    BlockPosition next = cell + offset;
                    if (!world.Bounds.Contains(next) || world.GetBlock(next) != BlockRegistry.Air)
                        continue;

                    world.SetBlock(next, FluidBlocks.FlowOf(family));
                    flowLevels[next] = level - 1;
                    active[(int)family].Add(next);
                    acted = true;
                }
            }

            if (!acted)
                familyActive.Remove(cell);
        }

        bool HasSupport(VoxelWorld world, FluidFamily family, BlockPosition cell, int level)
        {
            BlockPosition above = cell + Up;
            if (world.Bounds.Contains(above) &&
                FluidBlocks.TryGetFamily(world.GetBlock(above), out FluidFamily aboveFamily) &&
                aboveFamily == family)
            {
                return true;
            }

            foreach (BlockPosition offset in HorizontalOffsets)
            {
                BlockPosition next = cell + offset;
                if (!world.Bounds.Contains(next))
                    continue;

                BlockId neighbor = world.GetBlock(next);
                if (!FluidBlocks.TryGetFamily(neighbor, out FluidFamily neighborFamily) ||
                    neighborFamily != family)
                {
                    continue;
                }

                int neighborLevel = FluidBlocks.IsSource(neighbor)
                    ? FluidBlocks.FlowDistance(family)
                    : (flowLevels.TryGetValue(next, out int known) ? known : 0);

                if (neighborLevel > level)
                    return true;
            }

            return false;
        }

        // Freshwater quenches adjacent emberflow sources into black basalt (§5.4). The dead
        // source's flows lose their support and retract over the following emberflow steps.
        bool TryQuenchAdjacentEmberflow(VoxelWorld world, BlockPosition cell)
        {
            bool quenched = false;

            foreach (BlockPosition offset in SixNeighbors)
            {
                BlockPosition next = cell + offset;
                if (!world.Bounds.Contains(next) || world.GetBlock(next) != BlockRegistry.Emberflow)
                    continue;

                world.SetBlock(next, BlockRegistry.BlackBasalt);
                flowLevels.Remove(next);
                RemoveActiveAllFamilies(next);
                ActivateFluidNeighbors(world, next);
                quenched = true;
            }

            return quenched;
        }

        // Emberflow burns adjacent wood/foliage away (§5.4): each flammable neighbor rolls a
        // deterministic per-step chance seeded by (world seed, position, absolute tick), so
        // peers burn the same blocks at the same world ticks.
        bool TryIgniteNeighbors(VoxelWorld world, BlockPosition cell, long tick)
        {
            bool anyFlammable = false;

            foreach (BlockPosition offset in SixNeighbors)
            {
                BlockPosition next = cell + offset;
                if (!world.Bounds.Contains(next) || !IsFlammable(world.GetBlock(next)))
                    continue;

                anyFlammable = true;

                int salt = unchecked(7333 + (int)(tick & 0x3fffffff));
                if (DeterministicHash.Hash(worldSeed, next.X, next.Y, next.Z, salt) % 100u < IgnitionChancePercent)
                    world.SetBlock(next, BlockRegistry.Air);
            }

            // Stay active while flammables remain in contact so the burn keeps rolling.
            return anyFlammable;
        }

        static bool IsEmberflowSource(BlockId block) => block == BlockRegistry.Emberflow;

        static bool IsFlammable(BlockId block) =>
            block == BlockRegistry.BranchwoodLog ||
            block == BlockRegistry.SmoothBranchwood ||
            block == BlockRegistry.WorkPlank ||
            block == BlockRegistry.Leafmoss ||
            block == BlockRegistry.Thornbrush;
    }
}
