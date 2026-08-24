using System;
using System.Collections;
using System.Collections.Generic;

namespace Blockiverse.Voxel
{
    public readonly struct ChunkDelta : IEquatable<ChunkDelta>
    {
        public ChunkDelta(uint sequenceId, ChunkCoordinate chunk, BlockChange change, int newBlockState = BlockState.Default)
        {
            if (sequenceId == 0)
                throw new ArgumentOutOfRangeException(nameof(sequenceId), "Chunk delta sequence IDs must be nonzero.");

            SequenceId = sequenceId;
            Chunk = chunk;
            Change = change;
            NewBlockState = newBlockState;
        }

        public uint SequenceId { get; }
        public ChunkCoordinate Chunk { get; }
        public BlockChange Change { get; }

        /// <summary>Block state at the position AFTER the change (schema v5 / vegetation ruleset §5).
        ///
        /// This rides on the delta because the world simulation runs on EVERY peer in lockstep from
        /// identical inputs — environment mutations are never broadcast — and leaf decay is the
        /// first sim whose input is not derivable from (seed, clock, world). Without it the host
        /// skips a player-placed leaf while the client, seeing Default, deletes it: a permanent
        /// divergence that nothing repairs, because the host made no change and so emits no
        /// delta.</summary>
        public int NewBlockState { get; }

        public bool Equals(ChunkDelta other)
        {
            return SequenceId == other.SequenceId &&
                   Chunk == other.Chunk &&
                   Change.Position == other.Change.Position &&
                   Change.PreviousBlock == other.Change.PreviousBlock &&
                   Change.NewBlock == other.Change.NewBlock &&
                   NewBlockState == other.NewBlockState;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkDelta other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SequenceId, Chunk, Change.Position, Change.PreviousBlock, Change.NewBlock, NewBlockState);
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

        readonly DeltaRingBuffer deltas = new(MaxRetainedDeltas);
        uint nextSequenceId = 1;

        public IReadOnlyList<ChunkDelta> Deltas => deltas;
        public uint LastSequenceId { get; private set; }

        public ChunkDelta Record(BlockChange change, int chunkSize, int newBlockState = BlockState.Default)
        {
            uint sequenceId = AllocateSequenceId();
            var delta = new ChunkDelta(
                sequenceId,
                ChunkCoordinate.FromBlockPosition(change.Position, chunkSize),
                change,
                newBlockState);

            deltas.Add(delta);
            LastSequenceId = sequenceId;
            return delta;
        }

        public void Clear()
        {
            // Sequence IDs are deliberately not reset: they must stay monotonic for the host
            // session lifetime so receivers dedup and order deltas correctly across a
            // clear-and-refill, and so snapshot baselines (LastSequenceId) keep matching the
            // next broadcast delta.
            deltas.Clear();
        }

        public static void Replay(VoxelWorld world, IEnumerable<ChunkDelta> sourceDeltas, bool trackChange = false)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (sourceDeltas == null)
                throw new ArgumentNullException(nameof(sourceDeltas));

            foreach (ChunkDelta delta in sourceDeltas)
            {
                // SetBlock clears state at the position it writes, so the state must be restored
                // after it, never before.
                world.SetBlock(delta.Change.Position, delta.Change.NewBlock, trackChange);
                if (delta.NewBlockState != BlockState.Default)
                    world.SetBlockState(delta.Change.Position, delta.NewBlockState);
            }
        }

        uint AllocateSequenceId()
        {
            uint sequenceId = nextSequenceId++;

            if (nextSequenceId == 0)
                nextSequenceId = 1;

            return sequenceId;
        }

        // Fixed-capacity ring buffer so evicting the oldest delta at the retention cap is
        // O(1); a List-backed log would pay an O(n) RemoveAt(0) on every block edit once
        // the cap is reached.
        sealed class DeltaRingBuffer : IReadOnlyList<ChunkDelta>
        {
            readonly ChunkDelta[] buffer;
            int start;

            public DeltaRingBuffer(int capacity)
            {
                buffer = new ChunkDelta[capacity];
            }

            public int Count { get; private set; }

            public ChunkDelta this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    return buffer[(start + index) % buffer.Length];
                }
            }

            public void Add(ChunkDelta delta)
            {
                if (Count < buffer.Length)
                {
                    buffer[(start + Count) % buffer.Length] = delta;
                    Count++;
                    return;
                }

                buffer[start] = delta;
                start = (start + 1) % buffer.Length;
            }

            public void Clear()
            {
                start = 0;
                Count = 0;
            }

            public IEnumerator<ChunkDelta> GetEnumerator()
            {
                for (int index = 0; index < Count; index++)
                    yield return buffer[(start + index) % buffer.Length];
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
