using System;
using Blockiverse.Voxel;
using Unity.Profiling;

namespace Blockiverse.WorldGen
{
    enum TerrainBiome { Meadow, Pinewild, Wetland, Drybrush, Dunes, Tundra, Highlands }

    public sealed class SurvivalTerrainPreset
    {
        static readonly ProfilerMarker GenerateMarker = new("Blockiverse.SurvivalTerrainPreset.Generate");

        const int SpawnClearanceRadius = 3;
        const int SpawnHeadroom = 3;
        const int SpawnProtectedRadius = 4;

        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;
        readonly SurvivalResourceTuning resourceTuning;

        public SurvivalTerrainPreset(BlockRegistry registry, WorldGenerationSettings settings, SurvivalResourceTuning resourceTuning = null)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.resourceTuning = resourceTuning ?? SurvivalResourceTuning.CreateDefault();
        }

        public VoxelWorld Generate()
        {
            using ProfilerMarker.AutoScope scope = GenerateMarker.Auto();

            ValidateSettings();
            ValidateRegistry();

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            TerrainBiome[] biomeMap = BuildBiomeMap();
            int[] surfaceHeights = BuildSurfaceHeights(biomeMap);

            FillTerrain(world, surfaceHeights, biomeMap);
            CarveCaves(world, surfaceHeights);
            PlaceResourceVeins(world, surfaceHeights);
            PlaceSparseVegetation(world, surfaceHeights, biomeMap);
            ApplySpawnSafety(world);

            return world;
        }

        void ValidateSettings()
        {
            if (settings.Bounds.Height < 128)
                throw new InvalidOperationException("Survival terrain generation requires a world height of at least 128 blocks.");

            if (!settings.Bounds.Contains(settings.SpawnPosition))
                throw new InvalidOperationException("Survival terrain generation requires a spawn position inside the world bounds.");

            if (settings.SpawnPosition.Y + SpawnHeadroom >= settings.Bounds.Height)
                throw new InvalidOperationException("Survival terrain generation requires enough headroom above the spawn position.");
        }

        void ValidateRegistry()
        {
            registry.Get(BlockRegistry.Air);
            registry.Get(BlockRegistry.MeadowTurf);
            registry.Get(BlockRegistry.LooseLoam);
            registry.Get(BlockRegistry.Graystone);
            registry.Get(BlockRegistry.Worldroot);
            registry.Get(BlockRegistry.Deepmantle);

            foreach (ResourceVeinTuning vein in resourceTuning.ResourceVeins)
                registry.Get(vein.ResourceBlock);
        }

        TerrainBiome[] BuildBiomeMap()
        {
            WorldBounds bounds = settings.Bounds;
            var biomeMap = new TerrainBiome[bounds.Width * bounds.Depth];

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    int surfaceY = CalculateSurfaceHeight(x, z);
                    biomeMap[SurfaceIndex(x, z)] = ClassifyBiome(x, z, surfaceY);
                }
            }

            return biomeMap;
        }

        TerrainBiome ClassifyBiome(int x, int z, int surfaceY)
        {
            if (surfaceY >= 130)
                return TerrainBiome.Highlands;

            double temperature = ValueNoise2D(x, z, scale: 250, settings.Seed + 11, salt: 511);
            double moisture = ValueNoise2D(x, z, scale: 250, settings.Seed + 23, salt: 737);
            temperature -= Math.Max(0, surfaceY - 120) * 0.006;

            if (temperature < 0.25)
                return TerrainBiome.Tundra;

            if (temperature > 0.72 && moisture < 0.28)
                return TerrainBiome.Dunes;

            if (temperature > 0.58 && moisture < 0.45)
                return TerrainBiome.Drybrush;

            if (moisture > 0.65)
                return TerrainBiome.Wetland;

            if (temperature < 0.45 && moisture > 0.52)
                return TerrainBiome.Pinewild;

            return TerrainBiome.Meadow;
        }

        int[] BuildSurfaceHeights(TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = settings.Bounds;
            int[] surfaceHeights = new int[bounds.Width * bounds.Depth];

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                    surfaceHeights[SurfaceIndex(x, z)] = CalculateSurfaceHeight(x, z);
            }

            FlattenSpawnSurface(surfaceHeights);
            return surfaceHeights;
        }

        int CalculateSurfaceHeight(int x, int z)
        {
            double continent = (ValueNoise2D(x, z, scale: 500, settings.Seed, salt: 101) - 0.5) * 2.0;
            double hills = (ValueNoise2D(x, z, scale: 67, settings.Seed, salt: 211) - 0.5) * 2.0;
            double detail = (ValueNoise2D(x, z, scale: 17, settings.Seed, salt: 323) - 0.5) * 2.0;

            int height = (int)Math.Round(WorldConstants.SeaLevel + continent * 42 + hills * 18 + detail * 5);
            return Clamp(height, 40, 190);
        }

        void FlattenSpawnSurface(int[] surfaceHeights)
        {
            BlockPosition spawn = settings.SpawnPosition;
            int floorY = spawn.Y - 1;
            int flattenRadius = SpawnClearanceRadius + 1;

            for (int dx = -flattenRadius; dx <= flattenRadius; dx++)
            {
                for (int dz = -flattenRadius; dz <= flattenRadius; dz++)
                {
                    if (dx * dx + dz * dz > flattenRadius * flattenRadius)
                        continue;

                    int x = spawn.X + dx;
                    int z = spawn.Z + dz;
                    if (!IsColumnInBounds(x, z))
                        continue;

                    surfaceHeights[SurfaceIndex(x, z)] = floorY;
                }
            }
        }

        void FillTerrain(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = world.Bounds;

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    TerrainBiome biome = biomeMap[SurfaceIndex(x, z)];
                    int subsoilDepth = 3 + (int)(Hash(settings.Seed, x, 0, z, salt: 999) % 4u);

                    for (int y = 0; y <= surfaceY; y++)
                    {
                        BlockId block = SelectTerrainBlock(y, surfaceY, subsoilDepth, biome, x, z);
                        world.SetBlock(new BlockPosition(x, y, z), block, trackChange: false);
                    }

                    BlockId surfaceBlock = SelectSurfaceBlock(biome, x, z);
                    world.SetBlock(new BlockPosition(x, surfaceY, z), surfaceBlock, trackChange: false);
                }
            }
        }

        BlockId SelectTerrainBlock(int y, int surfaceY, int subsoilDepth, TerrainBiome biome, int x, int z)
        {
            if (y <= WorldConstants.BedrockTopY)
                return BlockRegistry.Worldroot;

            if (y <= 24)
                return BlockRegistry.Deepmantle;

            if (y >= surfaceY - subsoilDepth)
                return SelectSubsoilBlock(biome);

            return SelectStoneBlock(y, biome, x, z);
        }

        static BlockId SelectStoneBlock(int y, TerrainBiome biome, int x, int z)
        {
            if (y < 35)
            {
                uint h = Hash(x, 0, y, z, salt: 37);
                return h % 8u == 0 ? BlockRegistry.BlackBasalt : BlockRegistry.Deepmantle;
            }

            if (y < 70)
            {
                uint h = Hash(x, 0, y, z, salt: 43);
                if (h % 20u == 0)
                    return BlockRegistry.BlackBasalt;

                return h % 3u == 0 ? BlockRegistry.Graystone : BlockRegistry.DarkSlate;
            }

            if (y < 130)
            {
                uint h = Hash(x, 0, y, z, salt: 53);
                return h % 5u == 0 ? BlockRegistry.WhiteLimestone : BlockRegistry.Graystone;
            }

            return biome == TerrainBiome.Highlands ? BlockRegistry.WarmGranite : BlockRegistry.Graystone;
        }

        static BlockId SelectSubsoilBlock(TerrainBiome biome)
        {
            return biome switch
            {
                TerrainBiome.Pinewild => BlockRegistry.Rootsoil,
                TerrainBiome.Wetland => BlockRegistry.Claybed,
                TerrainBiome.Dunes => BlockRegistry.PaleSand,
                TerrainBiome.Highlands => BlockRegistry.WarmGranite,
                _ => BlockRegistry.LooseLoam,
            };
        }

        static BlockId SelectSurfaceBlock(TerrainBiome biome, int x, int z)
        {
            return biome switch
            {
                TerrainBiome.Pinewild => BlockRegistry.Rootsoil,
                TerrainBiome.Wetland => BlockRegistry.RiverSilt,
                TerrainBiome.Drybrush => BlockRegistry.DryTurf,
                TerrainBiome.Dunes => BlockRegistry.PaleSand,
                TerrainBiome.Tundra => BlockRegistry.SnowcapTurf,
                TerrainBiome.Highlands => BlockRegistry.WarmGranite,
                _ => BlockRegistry.MeadowTurf,
            };
        }

        void CarveCaves(VoxelWorld world, int[] surfaceHeights)
        {
            WorldBounds bounds = world.Bounds;
            const int horizontalCellSize = 16;
            const int verticalCellSize = 8;

            for (int cellX = 0; cellX < bounds.Width; cellX += horizontalCellSize)
            {
                for (int cellZ = 0; cellZ < bounds.Depth; cellZ += horizontalCellSize)
                {
                    for (int cellY = 8; cellY < bounds.Height - 8; cellY += verticalCellSize)
                    {
                        int gridX = cellX / horizontalCellSize;
                        int gridY = cellY / verticalCellSize;
                        int gridZ = cellZ / horizontalCellSize;
                        uint hash = Hash(settings.Seed, gridX, gridY, gridZ, salt: 503);

                        if (hash % 1000u >= 340u)
                            continue;

                        int centerX = cellX + Range(hash, 0, horizontalCellSize);
                        int centerY = cellY + Range(hash, 8, verticalCellSize);
                        int centerZ = cellZ + Range(hash, 16, horizontalCellSize);

                        if (!CanCarveAt(centerX, centerY, centerZ, surfaceHeights))
                            continue;

                        int radiusX = 3 + Range(hash, 24, 4);
                        int radiusY = 2 + Range(hash, 28, 2);
                        int radiusZ = 3 + Range(hash, 32, 4);

                        CarveEllipsoid(world, surfaceHeights, centerX, centerY, centerZ, radiusX, radiusY, radiusZ);

                        int endX = Clamp(centerX - 6 + Range(hash, 40, 13), 1, bounds.Width - 2);
                        int endY = Clamp(centerY - 2 + Range(hash, 48, 5), 3, bounds.Height - 4);
                        int endZ = Clamp(centerZ - 6 + Range(hash, 56, 13), 1, bounds.Depth - 2);
                        CarveTunnel(world, surfaceHeights, centerX, centerY, centerZ, endX, endY, endZ);
                    }
                }
            }
        }

        void CarveTunnel(VoxelWorld world, int[] surfaceHeights, int startX, int startY, int startZ, int endX, int endY, int endZ)
        {
            int steps = Max(Abs(endX - startX), Abs(endY - startY), Abs(endZ - startZ));
            if (steps == 0)
                return;

            for (int step = 0; step <= steps; step++)
            {
                int x = startX + (endX - startX) * step / steps;
                int y = startY + (endY - startY) * step / steps;
                int z = startZ + (endZ - startZ) * step / steps;
                CarveEllipsoid(world, surfaceHeights, x, y, z, radiusX: 2, radiusY: 1, radiusZ: 2);
            }
        }

        void CarveEllipsoid(VoxelWorld world, int[] surfaceHeights, int centerX, int centerY, int centerZ, int radiusX, int radiusY, int radiusZ)
        {
            for (int dx = -radiusX; dx <= radiusX; dx++)
            {
                for (int dy = -radiusY; dy <= radiusY; dy++)
                {
                    for (int dz = -radiusZ; dz <= radiusZ; dz++)
                    {
                        double normalized =
                            dx * dx / (double)(radiusX * radiusX) +
                            dy * dy / (double)(radiusY * radiusY) +
                            dz * dz / (double)(radiusZ * radiusZ);

                        if (normalized > 1d)
                            continue;

                        TryCarveCaveBlock(world, surfaceHeights, centerX + dx, centerY + dy, centerZ + dz);
                    }
                }
            }
        }

        bool CanCarveAt(int x, int y, int z, int[] surfaceHeights)
        {
            if (!settings.Bounds.Contains(new BlockPosition(x, y, z)))
                return false;

            return y <= surfaceHeights[SurfaceIndex(x, z)] - 5 && y >= 4 && !IsInsideSpawnProtectedColumn(x, z);
        }

        void TryCarveCaveBlock(VoxelWorld world, int[] surfaceHeights, int x, int y, int z)
        {
            var position = new BlockPosition(x, y, z);
            if (!world.Bounds.Contains(position))
                return;

            if (y <= WorldConstants.BedrockTopY || y > surfaceHeights[SurfaceIndex(x, z)] - 3)
                return;

            if (IsInsideSpawnProtectedColumn(x, z))
                return;

            if (world.GetBlock(position) != BlockRegistry.Air)
                world.SetBlock(position, BlockRegistry.Air, trackChange: false);
        }

        void PlaceResourceVeins(VoxelWorld world, int[] surfaceHeights)
        {
            foreach (ResourceVeinTuning vein in resourceTuning.ResourceVeins)
            {
                PlaceResourceVeins(
                    world,
                    surfaceHeights,
                    vein.ResourceBlock,
                    vein.Salt,
                    vein.MinY,
                    vein.MaxY,
                    vein.ChancePermille,
                    vein.Radius,
                    vein.VerticalRadius);
            }
        }

        void PlaceResourceVeins(
            VoxelWorld world,
            int[] surfaceHeights,
            BlockId resource,
            int salt,
            int minY,
            int maxY,
            int chancePermille,
            int radius,
            int verticalRadius)
        {
            WorldBounds bounds = world.Bounds;
            const int cellSize = 8;

            maxY = Clamp(maxY, minY, bounds.Height - 4);

            for (int cellX = 0; cellX < bounds.Width; cellX += cellSize)
            {
                for (int cellZ = 0; cellZ < bounds.Depth; cellZ += cellSize)
                {
                    for (int cellY = minY - minY % cellSize; cellY <= maxY; cellY += cellSize)
                    {
                        int gridX = cellX / cellSize;
                        int gridY = cellY / cellSize;
                        int gridZ = cellZ / cellSize;
                        uint hash = Hash(settings.Seed, gridX, gridY, gridZ, salt);

                        if (hash % 1000u >= chancePermille)
                            continue;

                        int centerX = cellX + Range(hash, 0, cellSize);
                        int centerY = cellY + Range(hash, 8, cellSize);
                        int centerZ = cellZ + Range(hash, 16, cellSize);

                        if (centerY < minY || centerY > maxY)
                            continue;

                        CarveResourceVein(world, surfaceHeights, resource, centerX, centerY, centerZ, radius, verticalRadius, minY, maxY);
                    }
                }
            }
        }

        void CarveResourceVein(
            VoxelWorld world,
            int[] surfaceHeights,
            BlockId resource,
            int centerX,
            int centerY,
            int centerZ,
            int radius,
            int verticalRadius,
            int minY,
            int maxY)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -verticalRadius; dy <= verticalRadius; dy++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        double normalized =
                            dx * dx / (double)(radius * radius) +
                            dy * dy / (double)(verticalRadius * verticalRadius) +
                            dz * dz / (double)(radius * radius);

                        if (normalized > 1d)
                            continue;

                        TryPlaceResourceBlock(world, surfaceHeights, resource, centerX + dx, centerY + dy, centerZ + dz, minY, maxY);
                    }
                }
            }
        }

        void TryPlaceResourceBlock(VoxelWorld world, int[] surfaceHeights, BlockId resource, int x, int y, int z, int minY, int maxY)
        {
            var position = new BlockPosition(x, y, z);
            if (!world.Bounds.Contains(position))
                return;

            if (y < minY || y > maxY || y > surfaceHeights[SurfaceIndex(x, z)] - 3)
                return;

            if (IsInsideSpawnProtectedColumn(x, z))
                return;

            if (world.GetBlock(position) == BlockRegistry.Air)
                return;

            world.SetBlock(position, resource, trackChange: false);
        }

        void PlaceSparseVegetation(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = world.Bounds;

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    if (IsInsideSpawnProtectedColumn(x, z))
                        continue;

                    TerrainBiome biome = biomeMap[SurfaceIndex(x, z)];
                    int treeDensityThreshold = BiomeTreeDensityThreshold(biome);

                    if (treeDensityThreshold == 0)
                        continue;

                    uint hash = Hash(settings.Seed, x, 0, z, salt: 1301);
                    if (hash % 100u >= (uint)treeDensityThreshold)
                        continue;

                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    int trunkHeight = 3 + (int)(hash % 3u);
                    PlaceTree(world, x, surfaceY + 1, z, trunkHeight);
                }
            }
        }

        void PlaceTree(VoxelWorld world, int baseX, int baseY, int baseZ, int trunkHeight)
        {
            WorldBounds bounds = world.Bounds;

            for (int dy = 0; dy < trunkHeight; dy++)
            {
                var trunkPos = new BlockPosition(baseX, baseY + dy, baseZ);
                if (!bounds.Contains(trunkPos))
                    return;

                world.SetBlock(trunkPos, BlockRegistry.BranchwoodLog, trackChange: false);
            }

            int canopyY = baseY + trunkHeight;
            for (int layer = 0; layer <= 1; layer++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var leafPos = new BlockPosition(baseX + dx, canopyY + layer, baseZ + dz);
                        if (!bounds.Contains(leafPos))
                            continue;

                        if (world.GetBlock(leafPos) == BlockRegistry.Air)
                            world.SetBlock(leafPos, BlockRegistry.Leafmoss, trackChange: false);
                    }
                }
            }
        }

        static int BiomeTreeDensityThreshold(TerrainBiome biome)
        {
            return biome switch
            {
                TerrainBiome.Pinewild  => 55,
                TerrainBiome.Meadow    => 25,
                TerrainBiome.Wetland   => 20,
                TerrainBiome.Tundra    => 10,
                TerrainBiome.Highlands => 10,
                TerrainBiome.Drybrush  => 8,
                TerrainBiome.Dunes     => 3,
                _                      => 0,
            };
        }

        void ApplySpawnSafety(VoxelWorld world)
        {
            BlockPosition spawn = settings.SpawnPosition;
            int floorY = spawn.Y - 1;

            for (int dx = -SpawnClearanceRadius; dx <= SpawnClearanceRadius; dx++)
            {
                for (int dz = -SpawnClearanceRadius; dz <= SpawnClearanceRadius; dz++)
                {
                    if (dx * dx + dz * dz > SpawnClearanceRadius * SpawnClearanceRadius)
                        continue;

                    int x = spawn.X + dx;
                    int z = spawn.Z + dz;
                    if (!IsColumnInBounds(x, z))
                        continue;

                    for (int y = 0; y < floorY; y++)
                    {
                        BlockId support = y <= WorldConstants.BedrockTopY
                            ? BlockRegistry.Worldroot
                            : y <= 24 ? BlockRegistry.Deepmantle
                            : y >= floorY - 3 ? BlockRegistry.LooseLoam
                            : BlockRegistry.Graystone;
                        world.SetBlock(new BlockPosition(x, y, z), support, trackChange: false);
                    }

                    world.SetBlock(new BlockPosition(x, floorY, z), BlockRegistry.MeadowTurf, trackChange: false);

                    for (int y = spawn.Y; y <= spawn.Y + SpawnHeadroom; y++)
                    {
                        if (y < world.Bounds.Height)
                            world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Air, trackChange: false);
                    }
                }
            }
        }

        bool IsInsideSpawnProtectedColumn(int x, int z)
        {
            BlockPosition spawn = settings.SpawnPosition;
            int dx = x - spawn.X;
            int dz = z - spawn.Z;
            return dx * dx + dz * dz <= SpawnProtectedRadius * SpawnProtectedRadius;
        }

        bool IsColumnInBounds(int x, int z)
        {
            return x >= 0 && x < settings.Bounds.Width && z >= 0 && z < settings.Bounds.Depth;
        }

        int SurfaceIndex(int x, int z)
        {
            return x + settings.Bounds.Width * z;
        }

        static double ValueNoise2D(int x, int z, int scale, int seed, int salt)
        {
            int cellX = x / scale;
            int cellZ = z / scale;
            double fractionX = (x - cellX * scale) / (double)scale;
            double fractionZ = (z - cellZ * scale) / (double)scale;
            double smoothX = SmoothStep(fractionX);
            double smoothZ = SmoothStep(fractionZ);

            double a = HashUnit(seed, cellX, 0, cellZ, salt);
            double b = HashUnit(seed, cellX + 1, 0, cellZ, salt);
            double c = HashUnit(seed, cellX, 0, cellZ + 1, salt);
            double d = HashUnit(seed, cellX + 1, 0, cellZ + 1, salt);

            return Lerp(Lerp(a, b, smoothX), Lerp(c, d, smoothX), smoothZ);
        }

        static uint Hash(int seed, int x, int y, int z, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, (uint)seed);
                hash = Mix(hash, (uint)x);
                hash = Mix(hash, (uint)y);
                hash = Mix(hash, (uint)z);
                hash = Mix(hash, (uint)salt);
                hash ^= hash >> 16;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                hash *= 3266489917u;
                hash ^= hash >> 16;
                return hash;
            }
        }

        static uint Mix(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value + 0x9e3779b9u + (hash << 6) + (hash >> 2);
                hash *= 16777619u;
                return hash;
            }
        }

        static double HashUnit(int seed, int x, int y, int z, int salt)
        {
            return (Hash(seed, x, y, z, salt) & 0x00ffffffu) / 16777215d;
        }

        static int Range(uint hash, int shift, int count)
        {
            return (int)((hash >> shift) % (uint)count);
        }

        static double SmoothStep(double value)
        {
            return value * value * (3d - 2d * value);
        }

        static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }

        static int Max(int first, int second, int third)
        {
            if (first >= second && first >= third)
                return first;

            return second >= third ? second : third;
        }
    }
}
