using System;
using System.Collections.Generic;

namespace Blockiverse.Voxel
{
    public readonly struct BlockChange
    {
        public BlockChange(BlockPosition position, BlockId previousBlock, BlockId newBlock)
        {
            Position = position;
            PreviousBlock = previousBlock;
            NewBlock = newBlock;
        }

        public BlockPosition Position { get; }
        public BlockId PreviousBlock { get; }
        public BlockId NewBlock { get; }
    }

    public sealed class VoxelWorld
    {
        readonly BlockId[] blocks;
        readonly Dictionary<BlockPosition, BlockChange> changedBlocks = new();

        public VoxelWorld(WorldBounds bounds, int chunkSize, int seed)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize));

            Bounds = bounds;
            ChunkSize = chunkSize;
            Seed = seed;
            blocks = new BlockId[bounds.Width * bounds.Height * bounds.Depth];
        }

        public WorldBounds Bounds { get; }
        public int ChunkSize { get; }
        public int Seed { get; }

        public event Action<BlockChange> BlockChanged;

        public BlockId GetBlock(BlockPosition position)
        {
            EnsureInBounds(position);
            return blocks[ToIndex(position)];
        }

        public void SetBlock(BlockPosition position, BlockId block)
        {
            SetBlock(position, block, trackChange: true);
        }

        public void SetBlock(BlockPosition position, BlockId block, bool trackChange)
        {
            EnsureInBounds(position);

            int index = ToIndex(position);
            BlockId previous = blocks[index];

            if (previous == block)
                return;

            blocks[index] = block;
            var change = new BlockChange(position, previous, block);

            if (trackChange)
                changedBlocks[position] = change;

            BlockChanged?.Invoke(change);
        }

        public ChunkCoordinate GetChunkCoordinate(BlockPosition position)
        {
            EnsureInBounds(position);
            return ChunkCoordinate.FromBlockPosition(position, ChunkSize);
        }

        // Linear scan over the backing array — decodes a BlockPosition only for matches. Full-world
        // sweeps (e.g. leaf-decay seeding) must use this instead of per-position GetBlock, which pays
        // a bounds check and index computation on every block.
        public void CollectBlockPositions(BlockId block, ICollection<BlockPosition> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            int width = Bounds.Width;
            int layer = width * Bounds.Depth;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] != block)
                    continue;

                int y = i / layer;
                int remainder = i - y * layer;
                results.Add(new BlockPosition(remainder % width, y, remainder / width));
            }
        }

        public IReadOnlyCollection<BlockChange> GetChangedBlocks()
        {
            return changedBlocks.Values;
        }

        public void ClearChangedBlocks()
        {
            changedBlocks.Clear();
        }

        public void TrackChangedBlock(BlockChange change)
        {
            EnsureInBounds(change.Position);
            changedBlocks[change.Position] = change;
        }

        int ToIndex(BlockPosition position)
        {
            return position.X + Bounds.Width * (position.Z + Bounds.Depth * position.Y);
        }

        void EnsureInBounds(BlockPosition position)
        {
            if (!Bounds.Contains(position))
                throw new ArgumentOutOfRangeException(nameof(position), $"Block position is outside world bounds: {position}");
        }
    }

    public sealed class SetBlockCommand
    {
        BlockChange? appliedChange;

        public SetBlockCommand(BlockPosition position, BlockId newBlock)
        {
            Position = position;
            NewBlock = newBlock;
        }

        public BlockPosition Position { get; }
        public BlockId NewBlock { get; }
        public bool HasAppliedChange => appliedChange.HasValue;
        public BlockChange AppliedChange => appliedChange ?? throw new InvalidOperationException("Block command has not executed.");

        public BlockChange Execute(VoxelWorld world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            BlockId previous = world.GetBlock(Position);
            world.SetBlock(Position, NewBlock);
            appliedChange = new BlockChange(Position, previous, NewBlock);
            return appliedChange.Value;
        }

        public void Undo(VoxelWorld world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (!appliedChange.HasValue)
                throw new InvalidOperationException("Cannot undo a block command before it has executed.");

            world.SetBlock(Position, appliedChange.Value.PreviousBlock);
        }
    }
}
