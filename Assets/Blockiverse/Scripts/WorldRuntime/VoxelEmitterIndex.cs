using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.Gameplay
{
    // Chunk-bucketed index of every light-emitting block in the world, kept current from block
    // changes. The mesh builder asks it for the emitters within reach of a chunk so the per-face
    // line-of-sight bake only traces toward lights that could actually touch the chunk, instead
    // of scanning a padded 48^3 volume on every rebuild. Engine-free, like VoxelSkyLightMap.
    public sealed class VoxelEmitterIndex
    {
        readonly VoxelWorld world;
        readonly BlockRegistry registry;
        readonly HashSet<BlockId> emissiveBlocks = new();
        readonly Dictionary<ChunkCoordinate, List<BlockPosition>> buckets = new();
        readonly List<BlockPosition> scanScratch = new();

        public VoxelEmitterIndex(VoxelWorld world, BlockRegistry registry)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

            foreach (BlockDefinition definition in registry.All)
            {
                if (definition.EmissiveLight > 0)
                    emissiveBlocks.Add(definition.Id);
            }

            Rebuild();
        }

        public int Count { get; private set; }

        public void Rebuild()
        {
            buckets.Clear();
            Count = 0;

            scanScratch.Clear();
            // One flat pass over the block array — far cheaper than a nested x/y/z loop with a
            // registry lookup per cell.
            world.CollectBlockPositions(emissiveBlocks, scanScratch);

            foreach (BlockPosition position in scanScratch)
                Add(position);

            scanScratch.Clear();
        }

        // Returns true when the set of emitters changed.
        public bool ApplyChange(BlockChange change)
        {
            bool wasEmitter = emissiveBlocks.Contains(change.PreviousBlock);
            bool isEmitter = emissiveBlocks.Contains(change.NewBlock);

            if (wasEmitter == isEmitter)
                return false;

            if (isEmitter)
                Add(change.Position);
            else
                Remove(change.Position);

            return true;
        }

        public bool Contains(BlockPosition position)
        {
            return world.Bounds.Contains(position) &&
                   buckets.TryGetValue(world.GetChunkCoordinate(position), out List<BlockPosition> bucket) &&
                   bucket.Contains(position);
        }

        // Appends every emitter whose block position lies inside [min, max] (inclusive).
        public void CollectInRange(BlockPosition min, BlockPosition max, List<BlockPosition> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            if (buckets.Count == 0)
                return;

            int chunkSize = world.ChunkSize;
            ChunkCoordinate minChunk = ChunkCoordinate.FromBlockPosition(min, chunkSize);
            ChunkCoordinate maxChunk = ChunkCoordinate.FromBlockPosition(max, chunkSize);

            for (int cy = minChunk.Y; cy <= maxChunk.Y; cy++)
            {
                for (int cz = minChunk.Z; cz <= maxChunk.Z; cz++)
                {
                    for (int cx = minChunk.X; cx <= maxChunk.X; cx++)
                    {
                        if (!buckets.TryGetValue(new ChunkCoordinate(cx, cy, cz), out List<BlockPosition> bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            BlockPosition position = bucket[i];
                            if (position.X >= min.X && position.X <= max.X &&
                                position.Y >= min.Y && position.Y <= max.Y &&
                                position.Z >= min.Z && position.Z <= max.Z)
                            {
                                results.Add(position);
                            }
                        }
                    }
                }
            }
        }

        void Add(BlockPosition position)
        {
            ChunkCoordinate chunk = world.GetChunkCoordinate(position);
            if (!buckets.TryGetValue(chunk, out List<BlockPosition> bucket))
            {
                bucket = new List<BlockPosition>();
                buckets[chunk] = bucket;
            }

            if (bucket.Contains(position))
                return;

            bucket.Add(position);
            Count++;
        }

        void Remove(BlockPosition position)
        {
            ChunkCoordinate chunk = world.GetChunkCoordinate(position);
            if (!buckets.TryGetValue(chunk, out List<BlockPosition> bucket))
                return;

            if (!bucket.Remove(position))
                return;

            Count--;
            if (bucket.Count == 0)
                buckets.Remove(chunk);
        }
    }
}
