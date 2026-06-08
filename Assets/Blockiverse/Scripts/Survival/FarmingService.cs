using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public enum FarmingResult
    {
        Success,
        OutOfBounds,
        NotTillableBlock,
        NotTendedSoil,
        BlockAboveNotAir,
        UnknownCrop,
    }

    public sealed class FarmingService
    {
        public const int GrowthIntervalTicks = 1200;

        static readonly Dictionary<BlockId, BlockId> NextGrowthStage = new()
        {
            { BlockRegistry.GrainStalk,   BlockRegistry.GrainStalk_S1 },
            { BlockRegistry.GrainStalk_S1, BlockRegistry.GrainStalk_S2 },
            { BlockRegistry.Berrybush,    BlockRegistry.Berrybush_S1 },
            { BlockRegistry.Berrybush_S1,  BlockRegistry.Berrybush_S2 },
            { BlockRegistry.Reedgrass,    BlockRegistry.Reedgrass_S1 },
        };

        static readonly HashSet<BlockId> CropBlocks = new(NextGrowthStage.Keys);

        readonly Dictionary<BlockPosition, int> tickAccumulator = new();

        public FarmingResult Till(VoxelWorld world, BlockPosition position)
        {
            if (!world.Bounds.Contains(position))
                return FarmingResult.OutOfBounds;

            if (world.GetBlock(position) != BlockRegistry.LooseLoam)
                return FarmingResult.NotTillableBlock;

            world.SetBlock(position, BlockRegistry.TendedSoil);
            return FarmingResult.Success;
        }

        public FarmingResult PlantCrop(VoxelWorld world, BlockPosition soilPosition, BlockId cropKind)
        {
            if (!world.Bounds.Contains(soilPosition))
                return FarmingResult.OutOfBounds;

            if (world.GetBlock(soilPosition) != BlockRegistry.TendedSoil)
                return FarmingResult.NotTendedSoil;

            if (!CropBlocks.Contains(cropKind))
                return FarmingResult.UnknownCrop;

            var cropPosition = new BlockPosition(soilPosition.X, soilPosition.Y + 1, soilPosition.Z);

            if (!world.Bounds.Contains(cropPosition) || world.GetBlock(cropPosition) != BlockRegistry.Air)
                return FarmingResult.BlockAboveNotAir;

            world.SetBlock(cropPosition, cropKind);
            tickAccumulator[cropPosition] = 0;
            return FarmingResult.Success;
        }

        public void TickGrowth(VoxelWorld world, int ticks)
        {
            if (ticks <= 0)
                return;

            var toRemove  = new List<BlockPosition>();
            var toAdvance = new List<(BlockPosition pos, int remainder)>();
            var toUpdate  = new List<(BlockPosition pos, int value)>();

            foreach (var kv in tickAccumulator)
            {
                BlockPosition pos = kv.Key;

                if (!CropBlocks.Contains(world.GetBlock(pos)))
                {
                    toRemove.Add(pos);
                    continue;
                }

                int accumulated = kv.Value + ticks;
                if (accumulated >= GrowthIntervalTicks)
                    toAdvance.Add((pos, accumulated - GrowthIntervalTicks));
                else
                    toUpdate.Add((pos, accumulated));
            }

            foreach (BlockPosition pos in toRemove)
                tickAccumulator.Remove(pos);

            foreach (var (pos, val) in toUpdate)
                tickAccumulator[pos] = val;

            foreach (var (pos, remainder) in toAdvance)
            {
                BlockId current = world.GetBlock(pos);
                if (NextGrowthStage.TryGetValue(current, out BlockId next))
                {
                    world.SetBlock(pos, next);
                    tickAccumulator[pos] = remainder;
                }
                else
                {
                    // Fully grown — stop tracking
                    tickAccumulator.Remove(pos);
                }
            }
        }
    }
}
