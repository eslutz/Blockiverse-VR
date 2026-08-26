using System;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public sealed class WorldGenerationSettings
    {
        // Intentionally public for deterministic flat-world validation fixtures. Runtime
        // new-world flow chooses the ruleset presets explicitly instead of this tiny fixture.
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
            return CreateSurvivalTerrain(
                width: 128,
                height: WorldConstants.WorldMaxY + 1,
                depth: 128,
                chunkSize: WorldConstants.ChunkSize,
                seed: seed);
        }

        public static WorldGenerationSettings CreateSurvivalTerrain(
            int width,
            int height,
            int depth,
            int chunkSize,
            int seed,
            BlockPosition? spawnPosition = null)
        {
            BlockPosition resolvedSpawn = spawnPosition ?? SurvivalSpawnResolver.Resolve(seed, width, height, depth);
            return new WorldGenerationSettings(
                width,
                height,
                depth,
                chunkSize,
                seed,
                WorldConstants.SeaLevel,
                resolvedSpawn);
        }

        public static WorldGenerationSettings CreateDefaultSurvivalLite(int seed = 6401)
        {
            return CreateDefaultSurvivalTerrain(seed);
        }

        public WorldGenerationSettings(int width, int height, int depth, int chunkSize, int seed, int groundHeight, BlockPosition? spawnPosition = null)
        {
            if (groundHeight < 0 || groundHeight >= height)
                throw new ArgumentOutOfRangeException(nameof(groundHeight), "Ground height must leave air above the surface.");
            if (!spawnPosition.HasValue && groundHeight + 1 >= height)
                throw new ArgumentOutOfRangeException(nameof(groundHeight), "Ground height must leave air above the surface.");

            Bounds = new WorldBounds(width, height, depth);
            ChunkSize = chunkSize;
            Seed = seed;
            GroundHeight = groundHeight;
            // The default spawn is the world center; callers may override it as long as the
            // position stays inside the bounds.
            SpawnPosition = spawnPosition ?? new BlockPosition(width / 2, groundHeight + 1, depth / 2);

            if (!Bounds.Contains(SpawnPosition))
                throw new ArgumentOutOfRangeException(nameof(spawnPosition), "Spawn position must be inside the world bounds.");
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

}
