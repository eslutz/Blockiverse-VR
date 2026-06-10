using System;
using System.Collections.Generic;

namespace Blockiverse.Voxel
{
    public readonly struct ChunkDelta : IEquatable<ChunkDelta>
    {
        public ChunkDelta(uint sequenceId, ChunkCoordinate chunk, BlockChange change)
        {
            if (sequenceId == 0)
                throw new ArgumentOutOfRangeException(nameof(sequenceId), "Chunk delta sequence IDs must be nonzero.");

            SequenceId = sequenceId;
            Chunk = chunk;
            Change = change;
        }

        public uint SequenceId { get; }
        public ChunkCoordinate Chunk { get; }
        public BlockChange Change { get; }

        public bool Equals(ChunkDelta other)
        {
            return SequenceId == other.SequenceId &&
                   Chunk == other.Chunk &&
                   Change.Position == other.Change.Position &&
                   Change.PreviousBlock == other.Change.PreviousBlock &&
                   Change.NewBlock == other.Change.NewBlock;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkDelta other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SequenceId, Chunk, Change.Position, Change.PreviousBlock, Change.NewBlock);
        }

        public static bool operator ==(ChunkDelta left, ChunkDelta right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChunkDelta left, ChunkDelta right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class ChunkDeltaLog
    {
        // Only the most recent deltas are retained: late-join sync ships the world's changed
        // blocks (not this log), so the log exists for sequencing and diagnostics and must not
        // grow unbounded over a long host session.
        public const int MaxRetainedDeltas = 1024;

        readonly List<ChunkDelta> deltas = new();
        uint nextSequenceId = 1;

        public IReadOnlyList<ChunkDelta> Deltas => deltas;
        public uint LastSequenceId { get; private set; }

        public ChunkDelta Record(BlockChange change, int chunkSize)
        {
            uint sequenceId = AllocateSequenceId();
            var delta = new ChunkDelta(
                sequenceId,
                ChunkCoordinate.FromBlockPosition(change.Position, chunkSize),
                change);

            if (deltas.Count >= MaxRetainedDeltas)
                deltas.RemoveAt(0);

            deltas.Add(delta);
            LastSequenceId = sequenceId;
            return delta;
        }

        public void Clear()
        {
            deltas.Clear();
            nextSequenceId = 1;
            LastSequenceId = 0;
        }

        public static void Replay(VoxelWorld world, IEnumerable<ChunkDelta> sourceDeltas, bool trackChange = false)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (sourceDeltas == null)
                throw new ArgumentNullException(nameof(sourceDeltas));

            foreach (ChunkDelta delta in sourceDeltas)
                world.SetBlock(delta.Change.Position, delta.Change.NewBlock, trackChange);
        }

        uint AllocateSequenceId()
        {
            uint sequenceId = nextSequenceId++;

            if (nextSequenceId == 0)
                nextSequenceId = 1;

            return sequenceId;
        }
    }
}
