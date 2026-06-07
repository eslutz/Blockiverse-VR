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
                chunkSize: WorldConstants.ChunkSize,
                seed: 1001,
                groundHeight: 2);
        }

        public static WorldGenerationSettings CreateDefaultSurvivalTerrain(int seed = 6401)
        {
            return new WorldGenerationSettings(
                width: 128,
                height: 256,
                depth: 128,
                chunkSize: WorldConstants.ChunkSize,
                seed: seed,
                groundHeight: WorldConstants.SeaLevel);
        }

        public static WorldGenerationSettings CreateDefaultSurvivalLite(int seed = 6401)
        {
            return CreateDefaultSurvivalTerrain(seed);
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

    public sealed class FlatBuilderPreset
    {
        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;

        public FlatBuilderPreset(BlockRegistry registry, WorldGenerationSettings settings)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public VoxelWorld Generate()
        {
            registry.Get(BlockRegistry.Air);
            registry.Get(BlockRegistry.MeadowTurf);
            registry.Get(BlockRegistry.LooseLoam);

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            for (int x = 0; x < settings.Bounds.Width; x++)
            {
                for (int z = 0; z < settings.Bounds.Depth; z++)
                {
                    for (int y = 0; y < settings.GroundHeight; y++)
                    {
                        int layersFromSurface = settings.GroundHeight - 1 - y;
                        BlockId block = layersFromSurface == 0
                            ? BlockRegistry.MeadowTurf
                            : layersFromSurface <= 4 ? BlockRegistry.LooseLoam
                            : BlockRegistry.Graystone;
                        world.SetBlock(new BlockPosition(x, y, z), block, trackChange: false);
                    }
                }
            }

            return world;
        }
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
            return new FlatBuilderPreset(registry, settings).Generate();
        }
    }

    public sealed class VoidBuilderPreset
    {
        const int PlatformHalfSize = 2;
        const int PlatformY = 64;

        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;

        public VoidBuilderPreset(BlockRegistry registry, WorldGenerationSettings settings)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public VoxelWorld Generate()
        {
            registry.Get(BlockRegistry.Air);
            registry.Get(BlockRegistry.WhiteLimestone);

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            int centerX = settings.Bounds.Width / 2;
            int centerZ = settings.Bounds.Depth / 2;

            for (int dx = -PlatformHalfSize; dx <= PlatformHalfSize; dx++)
            {
                for (int dz = -PlatformHalfSize; dz <= PlatformHalfSize; dz++)
                {
                    var pos = new BlockPosition(centerX + dx, PlatformY, centerZ + dz);
                    if (world.Bounds.Contains(pos))
                        world.SetBlock(pos, BlockRegistry.WhiteLimestone, trackChange: false);
                }
            }

            return world;
        }
    }
}
