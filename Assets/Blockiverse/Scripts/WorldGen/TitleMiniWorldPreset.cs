using System;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    /// <summary>
    /// A fixed, non-persistent survival-terrain exhibit used only behind the title menu.
    /// It deliberately does not share the random survival preset: every visit must expose the
    /// same compact set of biomes and representative content.
    /// </summary>
    public sealed class TitleMiniWorldPreset
    {
        public const int Size = 128;
        const int SurfaceY = WorldConstants.SeaLevel;

        readonly BlockRegistry registry;
        readonly WorldGenerationSettings settings;

        public TitleMiniWorldPreset(BlockRegistry registry, WorldGenerationSettings settings)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public VoxelWorld Generate()
        {
            if (settings.Bounds.Width != Size || settings.Bounds.Depth != Size || settings.Bounds.Height != Size)
                throw new ArgumentException("The title mini-world requires exact 128x128x128 bounds.", nameof(settings));

            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            for (int x = 0; x < Size; x++)
            for (int z = 0; z < Size; z++)
                FillColumn(world, x, z, BiomeAt(x, z));

            CarveBrineOcean(world);
            CarveFreshwaterRiver(world);
            CarveEmberflowGrotto(world);
            CarveGrottoApproach(world);
            PlaceShowcaseVegetation(world);
            PlaceShowcaseResources(world);
            PlaceShowcaseStructures(world);
            // Re-assert this reserved marker after structure placement; it is intentionally outside
            // every structure footprint and guarantees the complete plant catalog remains visible.
            PlaceSurface(world, 64, 50, BlockRegistry.MeadowTuft);
            PlaceSurface(world, 66, 50, BlockRegistry.WildflowerCluster);
            PrepareSpawnCourtyard(world);
            return world;
        }

        public static TerrainBiome BiomeAt(int x, int z)
        {
            int dx = x - Size / 2;
            int dz = z - Size / 2;
            if (dx * dx + dz * dz < 24 * 24)
                return TerrainBiome.Meadow;
            if (z < 30)
                return TerrainBiome.Highlands;
            if (x > 92 && z < 64)
                return TerrainBiome.Tundra;
            if (x > 92)
                return TerrainBiome.Pinewild;
            if (z > 94 && x > 64)
                return TerrainBiome.Wetland;
            if (z > 94)
                return TerrainBiome.Dunes;
            return TerrainBiome.Drybrush;
        }

        void FillColumn(VoxelWorld world, int x, int z, TerrainBiome biome)
        {
            int surface = SurfaceY + HeightOffset(biome, x, z);
            BlockId top = SurfaceFor(biome);
            for (int y = 0; y <= surface; y++)
            {
                BlockId block = y == surface ? top : y >= surface - 3 ? BlockRegistry.LooseLoam : BlockRegistry.Graystone;
                world.SetBlock(new BlockPosition(x, y, z), block, trackChange: false);
            }
        }

        static int HeightOffset(TerrainBiome biome, int x, int z)
        {
            int ripple = ((x * 13 + z * 7) & 3) - 1;
            return biome == TerrainBiome.Highlands ? 10 + ripple :
                biome == TerrainBiome.Tundra ? 5 + ripple :
                biome == TerrainBiome.Dunes ? 2 + ripple : ripple;
        }

        static BlockId SurfaceFor(TerrainBiome biome)
        {
            switch (biome)
            {
                case TerrainBiome.Pinewild: return BlockRegistry.Rootsoil;
                case TerrainBiome.Wetland: return BlockRegistry.RiverSilt;
                case TerrainBiome.Drybrush: return BlockRegistry.DryTurf;
                case TerrainBiome.Dunes: return BlockRegistry.PaleSand;
                case TerrainBiome.Tundra: return BlockRegistry.SnowcapTurf;
                case TerrainBiome.Highlands: return BlockRegistry.WarmGranite;
                default: return BlockRegistry.MeadowTurf;
            }
        }

        static void CarveBrineOcean(VoxelWorld world)
        {
            for (int x = 0; x < Size; x++)
            for (int z = 108; z < Size; z++)
            {
                int surface = FindSurfaceY(world, x, z);
                for (int y = SurfaceY - 3; y <= surface; y++)
                    world.SetBlock(new BlockPosition(x, y, z), y <= SurfaceY ? BlockRegistry.Brine : BlockRegistry.Air, trackChange: false);
            }
        }

        static void CarveFreshwaterRiver(VoxelWorld world)
        {
            for (int z = 24; z < 109; z++)
            {
                int center = 64 + ((z / 12) % 2 == 0 ? -3 : 3);
                for (int x = center - 2; x <= center + 2; x++)
                {
                    int surface = FindSurfaceY(world, x, z);
                    for (int y = SurfaceY - 2; y <= surface; y++)
                        world.SetBlock(new BlockPosition(x, y, z), y <= SurfaceY ? BlockRegistry.Freshwater : BlockRegistry.Air, trackChange: false);
                }
            }
        }

        static void CarveEmberflowGrotto(VoxelWorld world)
        {
            for (int x = 23; x <= 33; x++)
            for (int y = 20; y <= 29; y++)
            for (int z = 23; z <= 33; z++)
                world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Air, trackChange: false);

            for (int x = 26; x <= 30; x++)
            for (int z = 26; z <= 30; z++)
                world.SetBlock(new BlockPosition(x, 20, z), BlockRegistry.Emberflow, trackChange: false);
        }

        static void CarveGrottoApproach(VoxelWorld world)
        {
            // A gentle, one-block-per-step descent joins the surface to the grotto. The title
            // world has no editing, so every underground exhibit must be reachable on foot.
            for (int x = 28; x <= 71; x++)
            {
                int floorY = 20 + x - 28;
                for (int z = 31; z <= 33; z++)
                {
                    world.SetBlock(new BlockPosition(x, floorY, z), BlockRegistry.Graystone, trackChange: false);
                    for (int y = floorY + 1; y <= floorY + 3 && y < world.Bounds.Height; y++)
                        world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Air, trackChange: false);
                }
            }
        }

        void PlaceShowcaseVegetation(VoxelWorld world)
        {
            PlaceSurface(world, 56, 55, BlockRegistry.Berrybush);
            PlaceSurface(world, 93, 94, BlockRegistry.Reedgrass);
            PlaceSurface(world, 35, 78, BlockRegistry.Thornbrush);
            PlaceSurface(world, 35, 22, BlockRegistry.GrainStalk);
            PlaceSurface(world, 60, 52, BlockRegistry.MeadowTuft);
            PlaceSurface(world, 105, 80, BlockRegistry.MossCarpet);
            PlaceSurface(world, 75, 105, BlockRegistry.SaltReed);
            PlaceSurface(world, 42, 82, BlockRegistry.DrygrassTuft);
            PlaceSurface(world, 42, 101, BlockRegistry.DuneSage);
            PlaceSurface(world, 106, 42, BlockRegistry.SnowLichen);
            PlaceSurface(world, 111, 47, BlockRegistry.FrostFern);
            PlaceSurface(world, 60, 20, BlockRegistry.WindrootShrub);

            var vegetation = new VegetationService();
            vegetation.Configure((x, z) => (int)BiomeAt(x, z), settings.Seed);
            PlaceTree(world, vegetation, 52, 45, TerrainBiome.Meadow);
            PlaceTree(world, vegetation, 106, 75, TerrainBiome.Pinewild);
            PlaceTree(world, vegetation, 95, 90, TerrainBiome.Wetland);
            PlaceTree(world, vegetation, 38, 80, TerrainBiome.Drybrush);
            PlaceTree(world, vegetation, 40, 98, TerrainBiome.Dunes);
            PlaceTree(world, vegetation, 105, 45, TerrainBiome.Tundra);
            PlaceTree(world, vegetation, 58, 24, TerrainBiome.Highlands);
        }

        static void PlaceSurface(VoxelWorld world, int x, int z, BlockId block)
        {
            int surface = FindSurfaceY(world, x, z);
            if (surface >= 0 && !FluidBlocks.IsFluid(world.GetBlock(new BlockPosition(x, surface, z))))
                Place(world, x, surface + 1, z, block);
        }

        static void PlaceTree(VoxelWorld world, VegetationService vegetation, int x, int z, TerrainBiome biome)
        {
            int surface = FindSurfaceY(world, x, z);
            if (surface >= 0 && !FluidBlocks.IsFluid(world.GetBlock(new BlockPosition(x, surface, z))))
                vegetation.PlaceBiomeTree(world, new BlockPosition(x, surface + 1, z), biome);
        }

        static int FindSurfaceY(VoxelWorld world, int x, int z)
        {
            for (int y = world.Bounds.Height - 1; y >= 0; y--)
                if (world.GetBlock(new BlockPosition(x, y, z)) != BlockRegistry.Air)
                    return y;
            return -1;
        }

        static void PlaceShowcaseResources(VoxelWorld world)
        {
            BlockId[] resources =
            {
                BlockRegistry.EmbercoalSeam, BlockRegistry.RosycopperBloom, BlockRegistry.PaletinThread,
                BlockRegistry.NiterstonePocket, BlockRegistry.RustcoreOre, BlockRegistry.SunmetalFleck,
                BlockRegistry.LumenQuartzCluster, BlockRegistry.UmbraliteNode, BlockRegistry.StaropalGeode
            };
            for (int i = 0; i < resources.Length; i++)
                Place(world, 24 + i, 24 + (i % 3), 24, resources[i]);
        }

        void PlaceShowcaseStructures(VoxelWorld world)
        {
            // One intentionally chosen, pristine instance of every catalog entry.  The exhibit
            // favors discoverability over survival-world rarity and never rolls a second variant.
            (string id, int x, int z)[] placements =
            {
                ("pathmark_stones", 18, 50), ("old_wayflag", 28, 56), ("fallen_branchwood", 100, 76),
                ("saltmarker_cairn", 24, 94), ("frostmarker_cairn", 102, 32), ("forager_lean_to", 48, 76),
                ("resin_tap_grove", 104, 86), ("wetland_stilt_cache", 94, 101), ("drybrush_niter_pit", 20, 70),
                ("frost_shelter", 112, 52), ("bridge_segment", 64, 72), ("weathered_watchpost", 46, 22),
                ("ruined_kiln_yard", 28, 104), ("mossroot_hut_cluster", 101, 70), ("sunmetal_survey_tower", 15, 105),
                ("frost_beacon_ruin", 110, 20), ("cave_shrine", 21, 39), ("stoneburrow_cellar", 42, 39),
                ("lumen_hollow", 34, 39), ("ember_vent_outpost", 28, 39), ("deep_locker_room", 50, 39),
                ("staropal_pocket_shrine", 56, 39)
            };
            for (int i = 0; i < placements.Length; i++)
            {
                var placement = placements[i];
                int surfaceY = StructureService.FindSurfaceY(world, placement.x, placement.z);
                StructureService.TryPlaceStructureAt(world, placement.id, placement.x, surfaceY, placement.z, settings.Seed);
            }
        }

        static void PrepareSpawnCourtyard(VoxelWorld world)
        {
            // The player and fixed title menu occupy this small central courtyard. Terrain ripple
            // and the river are allowed everywhere else, but a raised column directly along +Z
            // puts the headset inside a dirt wall before the player can see or use the menu.
            for (int x = 62; x <= 66; x++)
            for (int z = 62; z <= 68; z++)
            {
                world.SetBlock(new BlockPosition(x, SurfaceY, z), BlockRegistry.MeadowTurf, trackChange: false);
                for (int y = SurfaceY + 1; y < world.Bounds.Height; y++)
                    world.SetBlock(new BlockPosition(x, y, z), BlockRegistry.Air, trackChange: false);
            }
        }

        static void Place(VoxelWorld world, int x, int y, int z, BlockId block) =>
            world.SetBlock(new BlockPosition(x, y, z), block, trackChange: false);
    }
}
