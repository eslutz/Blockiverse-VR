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
            return new WorldGenerationSettings(
                width: 128,
                height: WorldConstants.WorldMaxY + 1,
                depth: 128,
                chunkSize: WorldConstants.ChunkSize,
                seed: seed,
                groundHeight: WorldConstants.SeaLevel);
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
            // The default spawn is the world center; callers may override it (e.g. the new-world
            // flow's starting-biome search) as long as the position stays inside the bounds.
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

    // §11.3 Void Builder: an empty world holding only a 16×16 cutstone starting platform whose
    // walking surface sits at groundHeight-1 — the same surface/spawn relationship as the flat
    // preset, so the default spawn (groundHeight+1 over the spawn column) lands on it. Nothing
    // else generates; weather still runs at the world runtime level.
    public sealed class VoidBuilderPreset
    {
        public const int PlatformSize = 16;

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
            registry.Get(BlockRegistry.CutstoneBlock);

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);

            int platformY = settings.GroundHeight - 1;
            int startX = settings.SpawnPosition.X - PlatformSize / 2;
            int startZ = settings.SpawnPosition.Z - PlatformSize / 2;

            for (int dx = 0; dx < PlatformSize; dx++)
            {
                for (int dz = 0; dz < PlatformSize; dz++)
                {
                    var position = new BlockPosition(startX + dx, platformY, startZ + dz);
                    if (world.Bounds.Contains(position))
                        world.SetBlock(position, BlockRegistry.CutstoneBlock, trackChange: false);
                }
            }

            return world;
        }
    }
}
