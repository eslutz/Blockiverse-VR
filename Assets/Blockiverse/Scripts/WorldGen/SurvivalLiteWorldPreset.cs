using System;
using System.Collections.Generic;
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
        // §5.5: emberflow pools only occur in deep caves, below this height.
        const int EmberflowMaxY = 18;

        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;
        readonly SurvivalResourceTuning resourceTuning;
        readonly SurvivalBiomeResolver biomeResolver;
        readonly List<StructureContainerLoot> containerLoot = new();

        public SurvivalTerrainPreset(BlockRegistry registry, WorldGenerationSettings settings, SurvivalResourceTuning resourceTuning = null)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.resourceTuning = resourceTuning ?? SurvivalResourceTuning.CreateDefault();
            this.biomeResolver = new SurvivalBiomeResolver(settings.Seed, settings.Bounds.Height);
        }

        // Container loot rolled during the most recent Generate() call (structure crates → contents).
        // Deterministic from the world seed, so callers can build container inventories without
        // transmitting them across the network.
        public IReadOnlyList<StructureContainerLoot> ContainerLoot => containerLoot;

        public VoxelWorld Generate()
        {
            using ProfilerMarker.AutoScope scope = GenerateMarker.Auto();

            ValidateSettings();
            ValidateRegistry();

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            TerrainBiome[] biomeMap = BuildBiomeMap();
            int[] surfaceHeights = BuildSurfaceHeights(biomeMap);

            FillTerrain(world, surfaceHeights, biomeMap);
            PlaceFluids(world, surfaceHeights, biomeMap);
            CarveCaves(world, surfaceHeights, biomeMap);
            PlaceResourceVeins(world, surfaceHeights);
            containerLoot.Clear();
            StructureService.PlaceStructures(world, registry, settings, settings.Seed, biomeResolver.BiomeIndexAt, containerLoot);
            PlaceSparseVegetation(world, surfaceHeights, biomeMap);
            PlaceWildPlants(world, surfaceHeights, biomeMap);
            PlaceSurfaceResourceNodes(world, surfaceHeights, biomeMap);
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

        TerrainBiome ClassifyBiome(int x, int z, int surfaceY) =>
            biomeResolver.Classify(x, z, surfaceY);

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

        int CalculateSurfaceHeight(int x, int z) => biomeResolver.SurfaceHeight(x, z);

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
            // Canonical depth bands: Deepmantle bedrock, a Worldroot mantle above it, then the
            // stone column with low-frequency strata patches.
            if (y <= WorldConstants.BedrockTopY)
                return BlockRegistry.Deepmantle;

            if (y <= WorldConstants.BedrockTopY + 9)
                return BlockRegistry.Worldroot;

            if (y >= surfaceY - subsoilDepth)
                return SelectSubsoilBlock(biome);

            return SelectStoneBlock(y, x, z);
        }

        // Strata patches over the Graystone base, in 8³ cells so veins of slate/limestone/
        // granite/basalt read as coherent pockets rather than noise.
        BlockId SelectStoneBlock(int y, int x, int z)
        {
            uint roll = Hash(settings.Seed, x >> 3, y >> 3, z >> 3, salt: 2657) % 1000u;

            if (roll < 90)
                return y < 64 ? BlockRegistry.DarkSlate : BlockRegistry.WhiteLimestone;

            if (roll >= 910)
                return y < 48 ? BlockRegistry.BlackBasalt : BlockRegistry.WarmGranite;

            return BlockRegistry.Graystone;
        }

        static BlockId SelectSubsoilBlock(TerrainBiome biome)
        {
            return biome switch
            {
                TerrainBiome.Highlands => BlockRegistry.Graystone,
                TerrainBiome.Dunes     => BlockRegistry.PaleSand,
                TerrainBiome.Wetland   => BlockRegistry.Claybed,
                TerrainBiome.Drybrush  => BlockRegistry.Rootsoil,
                TerrainBiome.Pinewild  => BlockRegistry.Rootsoil,
                TerrainBiome.Tundra    => BlockRegistry.Rootsoil,
                _ => BlockRegistry.LooseLoam,
            };
        }

        BlockId SelectSurfaceBlock(TerrainBiome biome, int x, int z)
        {
            switch (biome)
            {
                case TerrainBiome.Highlands:
                {
                    // Scree patches over bare rock.
                    uint roll = Hash(settings.Seed, x, 0, z, salt: 2741) % 100u;
                    return roll < 15 ? BlockRegistry.ShingleGravel : BlockRegistry.Graystone;
                }
                case TerrainBiome.Tundra:
                {
                    // Mostly snow-covered turf with packed-snow drifts and rare frostglass sheets.
                    uint roll = Hash(settings.Seed, x, 0, z, salt: 2837) % 100u;
                    if (roll < 35) return BlockRegistry.Snowpack;
                    if (roll < 41) return BlockRegistry.Frostglass;
                    return BlockRegistry.SnowcapTurf;
                }
                case TerrainBiome.Drybrush:
                    return BlockRegistry.DryTurf;
                case TerrainBiome.Dunes:
                    return BlockRegistry.PaleSand;
                case TerrainBiome.Pinewild:
                    return BlockRegistry.Rootsoil;
                case TerrainBiome.Wetland:
                {
                    // Silt flats threaded through the wet ground.
                    uint roll = Hash(settings.Seed, x, 0, z, salt: 2903) % 100u;
                    return roll < 40 ? BlockRegistry.RiverSilt : BlockRegistry.Claybed;
                }
                default:
                    return BlockRegistry.MeadowTurf;
            }
        }

        // §5.4: after solid terrain, columns whose surface sits below sea level fill with a still
        // fluid — freshwater everywhere, brine in the dunes (salt basins/coastlines). The water
        // top sits at SeaLevel-1 so shorelines keep a one-block step down from dry land.
        void PlaceFluids(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = world.Bounds;

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    if (surfaceY + 1 >= WorldConstants.SeaLevel)
                        continue;

                    // The spawn clearing stays dry, matching the cave/structure protection.
                    if (IsInsideSpawnProtectedColumn(x, z))
                        continue;

                    BlockId fluid = biomeMap[SurfaceIndex(x, z)] == TerrainBiome.Dunes
                        ? BlockRegistry.Brine
                        : BlockRegistry.Freshwater;

                    for (int y = surfaceY + 1; y < WorldConstants.SeaLevel; y++)
                        world.SetBlock(new BlockPosition(x, y, z), fluid, trackChange: false);
                }
            }
        }

        void CarveCaves(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
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

                        // Cave radii come from a dedicated hash: a shift of 32 on a uint aliases to 0
                        // (C# masks shift counts to 5 bits), which correlated radiusZ with the
                        // cave center bits above.
                        uint radiusHash = Hash(settings.Seed, gridX, gridY, gridZ, salt: 504);
                        int radiusX = 3 + Range(radiusHash, 0, 4);
                        int radiusY = 2 + Range(radiusHash, 8, 2);
                        int radiusZ = 3 + Range(radiusHash, 16, 4);

                        // §5.5: deep caves (below y=18) roll a 10% chance to pool with emberflow;
                        // otherwise caves below sea level have a 35% chance to flood with water.
                        // The water follows the surface biome above the cave (brine under the
                        // dunes), like §5.4 lakes.
                        BlockId carveFill = BlockRegistry.Air;
                        uint floodRoll = Hash(settings.Seed, gridX, gridY, gridZ, salt: 521);
                        if (centerY < EmberflowMaxY && floodRoll % 100u < 10u)
                        {
                            carveFill = BlockRegistry.Emberflow;
                        }
                        else if (centerY < WorldConstants.SeaLevel && floodRoll % 100u < 35u)
                        {
                            carveFill = biomeMap[SurfaceIndex(centerX, centerZ)] == TerrainBiome.Dunes
                                ? BlockRegistry.Brine
                                : BlockRegistry.Freshwater;
                        }

                        CarveEllipsoid(world, surfaceHeights, centerX, centerY, centerZ, radiusX, radiusY, radiusZ, carveFill);

                        // Tunnel endpoints come from a dedicated hash: shifts of 40/48/56 on a uint
                        // alias to 8/16/24 (C# masks shift counts to 5 bits), which correlated the
                        // endpoints with the cave center/radius bits above.
                        uint tunnelHash = Hash(settings.Seed, gridX, gridY, gridZ, salt: 509);
                        int endX = Clamp(centerX - 6 + Range(tunnelHash, 0, 13), 1, bounds.Width - 2);
                        int endY = Clamp(centerY - 2 + Range(tunnelHash, 8, 5), 3, bounds.Height - 4);
                        int endZ = Clamp(centerZ - 6 + Range(tunnelHash, 16, 13), 1, bounds.Depth - 2);
                        CarveTunnel(world, surfaceHeights, centerX, centerY, centerZ, endX, endY, endZ, carveFill);
                    }
                }
            }
        }

        void CarveTunnel(VoxelWorld world, int[] surfaceHeights, int startX, int startY, int startZ, int endX, int endY, int endZ, BlockId carveFill)
        {
            int steps = Max(Abs(endX - startX), Abs(endY - startY), Abs(endZ - startZ));
            if (steps == 0)
                return;

            for (int step = 0; step <= steps; step++)
            {
                int x = startX + (endX - startX) * step / steps;
                int y = startY + (endY - startY) * step / steps;
                int z = startZ + (endZ - startZ) * step / steps;
                CarveEllipsoid(world, surfaceHeights, x, y, z, radiusX: 2, radiusY: 1, radiusZ: 2, carveFill);
            }
        }

        void CarveEllipsoid(VoxelWorld world, int[] surfaceHeights, int centerX, int centerY, int centerZ, int radiusX, int radiusY, int radiusZ, BlockId carveFill)
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

                        TryCarveCaveBlock(world, surfaceHeights, centerX + dx, centerY + dy, centerZ + dz, carveFill);
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

        void TryCarveCaveBlock(VoxelWorld world, int[] surfaceHeights, int x, int y, int z, BlockId carveFill)
        {
            var position = new BlockPosition(x, y, z);
            if (!world.Bounds.Contains(position))
                return;

            if (y <= WorldConstants.BedrockTopY || y > surfaceHeights[SurfaceIndex(x, z)] - 3)
                return;

            if (IsInsideSpawnProtectedColumn(x, z))
                return;

            BlockId existing = world.GetBlock(position);

            // Never carve through fluid: cutting a lake or flooded-cave boundary at generation
            // time would dump the neighboring pocket's fluid through the hole.
            if (FluidBlocks.IsFluid(existing))
                return;

            // Flooded caves fill below their fluid's ceiling and stay open air above it: water
            // pools cap at the §5.4 water table, emberflow pools at the §5.5 deep-cave band.
            int fluidCeiling = carveFill == BlockRegistry.Emberflow ? EmberflowMaxY : WorldConstants.SeaLevel;
            BlockId fill = carveFill != BlockRegistry.Air && y < fluidCeiling
                ? carveFill
                : BlockRegistry.Air;

            if (existing != fill)
                world.SetBlock(position, fill, trackChange: false);
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
            var vegetation = new VegetationService();

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    if (IsInsideSpawnProtectedColumn(x, z))
                        continue;

                    TerrainBiome biome = biomeMap[SurfaceIndex(x, z)];
                    int treeDensityPercent = BiomeTreeDensityPercent(biome);

                    if (treeDensityPercent == 0)
                        continue;

                    // Tree density is in percent (0–100) gated against hash % 100u — unlike the
                    // permille (0–1000) densities used by WildPlantForBiome and SurfaceNodeTable.
                    // Changing the modulus or unit here changes generation for existing seeds.
                    uint hash = Hash(settings.Seed, x, 0, z, salt: 1301);
                    if (hash % 100u >= (uint)treeDensityPercent)
                        continue;

                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    var basePos  = new BlockPosition(x, surfaceY + 1, z);

                    // Skip flooded columns (§5.4): trees don't grow under lakes, and planting one
                    // would overwrite the water volume.
                    if (!world.Bounds.Contains(basePos) || world.GetBlock(basePos) != BlockRegistry.Air)
                        continue;

                    PlaceBiomeTree(vegetation, world, basePos, biome);
                }
            }
        }

        static void PlaceBiomeTree(VegetationService vegetation, VoxelWorld world, BlockPosition basePos, TerrainBiome biome)
        {
            switch (biome)
            {
                case TerrainBiome.Pinewild:   vegetation.PlaceConicalTree(world, basePos);  break;
                case TerrainBiome.Wetland:    vegetation.PlaceWillowTree(world, basePos);   break;
                case TerrainBiome.Drybrush:   vegetation.PlaceShrubTree(world, basePos);    break;
                case TerrainBiome.Tundra:     vegetation.PlaceSparseTree(world, basePos);   break;
                case TerrainBiome.Highlands:  vegetation.PlaceTallTree(world, basePos);     break;
                case TerrainBiome.Dunes:      vegetation.PlaceShrubTree(world, basePos);    break;
                default:                      vegetation.PlaceStandardTree(world, basePos); break;
            }
        }

        // Per-biome tree density in percent (0–100), compared against hash % 100u in
        // PlaceSparseVegetation. NOT permille: the rest of this file uses 0–1000 densities.
        static int BiomeTreeDensityPercent(TerrainBiome biome)
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

        // Scatters single-block wild plants (berrybush, reedgrass, thornbrush, grain stalk) on the
        // surface by biome. These feed the harvest → regrowth loop (FarmingService owns berrybush;
        // VegetationService's wild-regrowth queue owns grain/reed/thorn). Placement is deterministic
        // from the seed and only lands on a clear column with a solid surface block below.
        void PlaceWildPlants(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = world.Bounds;

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    if (IsInsideSpawnProtectedColumn(x, z))
                        continue;

                    TerrainBiome biome = biomeMap[SurfaceIndex(x, z)];
                    BlockId plant = WildPlantForBiome(biome, out int densityThreshold);
                    if (densityThreshold == 0)
                        continue;

                    uint hash = Hash(settings.Seed, x, 0, z, salt: 2389);
                    if (hash % 1000u >= (uint)densityThreshold)
                        continue;

                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    var plantPos = new BlockPosition(x, surfaceY + 1, z);
                    if (!world.Bounds.Contains(plantPos))
                        continue;

                    // Only place on an empty column above a solid surface (don't overwrite trees/structures).
                    if (world.GetBlock(plantPos) != BlockRegistry.Air)
                        continue;
                    if (world.GetBlock(new BlockPosition(x, surfaceY, z)) == BlockRegistry.Air)
                        continue;

                    world.SetBlock(plantPos, plant, trackChange: false);
                }
            }
        }

        // Per-biome wild plant choice and density in tenths-of-a-percent (0–1000 → 0–100%).
        static BlockId WildPlantForBiome(TerrainBiome biome, out int densityPermille)
        {
            switch (biome)
            {
                case TerrainBiome.Meadow:   densityPermille = 28; return BlockRegistry.Berrybush;
                case TerrainBiome.Pinewild: densityPermille = 18; return BlockRegistry.Berrybush;
                case TerrainBiome.Wetland:  densityPermille = 45; return BlockRegistry.Reedgrass;
                case TerrainBiome.Drybrush: densityPermille = 30; return BlockRegistry.Thornbrush;
                case TerrainBiome.Dunes:    densityPermille = 12; return BlockRegistry.Thornbrush;
                case TerrainBiome.Highlands:densityPermille = 14; return BlockRegistry.GrainStalk;
                default:                    densityPermille = 0;  return BlockRegistry.Air;
            }
        }

        // Per-biome surface node scatter (block, density in permille, salt). These are the
        // renewable-ish gathering nodes (§3) that feed early recipes: pebbles/flint for tools and
        // campfires, brightsalt/shellgrit/resin for glass, preservation, and bandages.
        // ORDER MATTERS: PlaceSurfaceResourceNodes evaluates entries in declaration order with
        // first-match-wins (it breaks after the first same-biome entry whose roll passes), so
        // later same-biome rows have a slightly reduced effective rate. Reordering rows changes
        // generated terrain for existing seeds — saves store only deltas against seed-regenerated
        // terrain, so a reorder would silently corrupt existing worlds.
        static readonly (TerrainBiome biome, BlockId block, int permille, int salt)[] SurfaceNodeTable =
        {
            (TerrainBiome.Meadow,    BlockRegistry.SurfacePebbles,  24, 3001),
            (TerrainBiome.Meadow,    BlockRegistry.FlintyShingle,   12, 3109),
            (TerrainBiome.Meadow,    BlockRegistry.ResinKnot,       10, 3407),
            (TerrainBiome.Pinewild,  BlockRegistry.SurfacePebbles,  16, 3001),
            (TerrainBiome.Pinewild,  BlockRegistry.ResinKnot,       28, 3407),
            (TerrainBiome.Wetland,   BlockRegistry.SurfacePebbles,  14, 3001),
            (TerrainBiome.Wetland,   BlockRegistry.ShellgritBed,    36, 3301),
            (TerrainBiome.Drybrush,  BlockRegistry.SurfacePebbles,  22, 3001),
            (TerrainBiome.Drybrush,  BlockRegistry.FlintyShingle,   18, 3109),
            (TerrainBiome.Drybrush,  BlockRegistry.BrightsaltCrust, 12, 3203),
            (TerrainBiome.Dunes,     BlockRegistry.BrightsaltCrust, 30, 3203),
            (TerrainBiome.Dunes,     BlockRegistry.FlintyShingle,   10, 3109),
            (TerrainBiome.Tundra,    BlockRegistry.SurfacePebbles,  16, 3001),
            (TerrainBiome.Tundra,    BlockRegistry.FlintyShingle,   10, 3109),
            (TerrainBiome.Highlands, BlockRegistry.SurfacePebbles,  26, 3001),
            (TerrainBiome.Highlands, BlockRegistry.FlintyShingle,   20, 3109),
        };

        // Scatters single-block resource nodes on clear surface columns, after vegetation and
        // wild plants so it never overwrites them (first occupant of the cell wins).
        void PlaceSurfaceResourceNodes(VoxelWorld world, int[] surfaceHeights, TerrainBiome[] biomeMap)
        {
            WorldBounds bounds = world.Bounds;

            for (int x = 0; x < bounds.Width; x++)
            {
                for (int z = 0; z < bounds.Depth; z++)
                {
                    if (IsInsideSpawnProtectedColumn(x, z))
                        continue;

                    TerrainBiome biome = biomeMap[SurfaceIndex(x, z)];
                    int surfaceY = surfaceHeights[SurfaceIndex(x, z)];
                    var nodePos = new BlockPosition(x, surfaceY + 1, z);
                    if (!world.Bounds.Contains(nodePos))
                        continue;

                    if (world.GetBlock(nodePos) != BlockRegistry.Air)
                        continue;
                    if (world.GetBlock(new BlockPosition(x, surfaceY, z)) == BlockRegistry.Air)
                        continue;

                    foreach ((TerrainBiome nodeBiome, BlockId block, int permille, int salt) in SurfaceNodeTable)
                    {
                        if (nodeBiome != biome)
                            continue;

                        if (Hash(settings.Seed, x, 0, z, salt) % 1000u >= (uint)permille)
                            continue;

                        world.SetBlock(nodePos, block, trackChange: false);
                        break;
                    }
                }
            }
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
                        BlockId support = y >= floorY - 3 ? BlockRegistry.LooseLoam : BlockRegistry.Graystone;
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

        static uint Hash(int seed, int x, int y, int z, int salt)
        {
            return DeterministicHash.Hash(seed, x, y, z, salt);
        }

        static int Range(uint hash, int shift, int count)
        {
            return (int)((hash >> shift) % (uint)count);
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
