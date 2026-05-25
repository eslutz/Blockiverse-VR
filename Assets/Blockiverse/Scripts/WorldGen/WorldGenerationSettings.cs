using System;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public sealed class WorldGenerationSettings
    {
        public static WorldGenerationSettings CreateDefaultCreative()
        {
            return new WorldGenerationSettings(
                width: 32,
                height: 16,
                depth: 32,
                chunkSize: 16,
                seed: 1001,
                groundHeight: 2);
        }

        public static WorldGenerationSettings CreateDefaultSurvivalLite(int seed = 6401)
        {
            return new WorldGenerationSettings(
                width: 128,
                height: 64,
                depth: 128,
                chunkSize: 16,
                seed: seed,
                groundHeight: 32);
        }

        public WorldGenerationSettings(int width, int height, int depth, int chunkSize, int seed, int groundHeight)
        {
            if (groundHeight < 1 || groundHeight >= height)
                throw new ArgumentOutOfRangeException(nameof(groundHeight), "Ground height must leave air above the surface.");

            Bounds = new WorldBounds(width, height, depth);
            ChunkSize = chunkSize;
            Seed = seed;
            GroundHeight = groundHeight;
            SpawnPosition = new BlockPosition(width / 2, groundHeight + 1, depth / 2);
        }

        public WorldBounds Bounds { get; }
        public int ChunkSize { get; }
        public int Seed { get; }
        public int GroundHeight { get; }
        public BlockPosition SpawnPosition { get; }
    }

    public sealed class FlatCreativeWorldPreset
    {
        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;

        public FlatCreativeWorldPreset(BlockRegistry registry, WorldGenerationSettings settings)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public VoxelWorld Generate()
        {
            registry.Get(BlockRegistry.Air);
            registry.Get(BlockRegistry.MeadowTurf);
            registry.Get(BlockRegistry.Loam);

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int x = 0; x < settings.Bounds.Width; x++)
            {
                for (int z = 0; z < settings.Bounds.Depth; z++)
                {
                    for (int y = 0; y < settings.GroundHeight; y++)
                    {
                        BlockId block = y == settings.GroundHeight - 1
                            ? BlockRegistry.MeadowTurf
                            : BlockRegistry.Loam;
                        world.SetBlock(new BlockPosition(x, y, z), block, trackChange: false);
                    }
                }
            }

            return world;
        }
    }
}
