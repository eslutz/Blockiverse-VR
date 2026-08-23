using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public enum StructureDegradation { Intact, Weathered, Ruined, Crumbled }
    enum StructurePlacementKind { Surface, Underground, CaveFeature }

    public readonly struct StructureCatalogEntry
    {
        public readonly string Id;
        public readonly bool IsUnderground;
        public readonly int MinDistanceFromSpawn;
        public readonly string LootTableId;

        public StructureCatalogEntry(string id, bool isUnderground, int minDistanceFromSpawn, string lootTableId)
        {
            Id = id;
            IsUnderground = isUnderground;
            MinDistanceFromSpawn = minDistanceFromSpawn;
            LootTableId = lootTableId;
        }
    }

    // One stack of rolled container loot (canonical item id + count). Kept string-based so the
    // WorldGen assembly need not depend on the Survival item registry.
    public readonly struct ContainerLootItem
    {
        public readonly string ItemId;
        public readonly int Count;

        public ContainerLootItem(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    // The rolled contents of a single container placed by a structure, at a world block position.
    public sealed class StructureContainerLoot
    {
        public BlockPosition Position { get; }
        public IReadOnlyList<ContainerLootItem> Items { get; }

        public StructureContainerLoot(BlockPosition position, IReadOnlyList<ContainerLootItem> items)
        {
            Position = position;
            Items = items;
        }
    }

    // Minimal structure definition for catalog dispatch
    sealed class StructureDefinition
    {
        public readonly string Id;
        public readonly TerrainBiome[] AllowedBiomes;
        public readonly int RegionChancePercent; // 0-100
        public readonly int MinDistanceFromSpawn;
        public readonly StructureDegradation MaxDegradation;
        public readonly bool HasLoot;
        public readonly bool HasStation;
        public readonly string LootTableId; // which StructureLootTable fills this structure's crate
        public readonly StructurePlacementKind PlacementKind;
        public readonly BlockId PrimaryBlock;
        public readonly BlockId StationBlock;
        public readonly int MinPlacementY;
        public readonly int MaxPlacementY;

        public StructureDefinition(
            string id,
            TerrainBiome[] allowedBiomes,
            int regionChancePercent,
            int minDistanceFromSpawn = 80,
            StructureDegradation maxDegradation = StructureDegradation.Ruined,
            bool hasLoot = false,
            bool hasStation = false,
            string lootTableId = null,
            StructurePlacementKind placementKind = StructurePlacementKind.Surface,
            BlockId? primaryBlock = null,
            BlockId? stationBlock = null,
            int minPlacementY = 0,
            int maxPlacementY = int.MaxValue)
        {
            Id = id;
            AllowedBiomes = allowedBiomes;
            RegionChancePercent = regionChancePercent;
            MinDistanceFromSpawn = minDistanceFromSpawn;
            MaxDegradation = maxDegradation;
            HasLoot = hasLoot;
            HasStation = hasStation;
            LootTableId = lootTableId ?? StructureLootTable.CommonSupply.Id;
            PlacementKind = placementKind;
            PrimaryBlock = primaryBlock ?? BlockRegistry.Graystone;
            StationBlock = stationBlock ?? BlockRegistry.Campfire;
            MinPlacementY = minPlacementY;
            MaxPlacementY = maxPlacementY;
        }
    }

    public sealed class StructureService
    {
        // One candidate per 32×32 chunk region; must be ≥ 48 blocks from any other.
        const int RegionSize      = 32;
        const int MinSpacing      = 48;
        // ~30% of regions hold a structure (matches the original pre-catalog density).
        const uint RegionSpawnChancePercent = 30;
        // Number of TerrainBiome enum values; biome indices are wrapped into this range.
        const int BiomeCount = 7;

        static readonly TerrainBiome[] AllBiomes =
        {
            TerrainBiome.Meadow,
            TerrainBiome.Pinewild,
            TerrainBiome.Wetland,
            TerrainBiome.Drybrush,
            TerrainBiome.Dunes,
            TerrainBiome.Tundra,
            TerrainBiome.Highlands,
        };

        static readonly TerrainBiome[] AllExceptDunes =
        {
            TerrainBiome.Meadow,
            TerrainBiome.Pinewild,
            TerrainBiome.Wetland,
            TerrainBiome.Drybrush,
            TerrainBiome.Tundra,
            TerrainBiome.Highlands,
        };

        static readonly StructureDefinition[] Catalog =
        {
            new("pathmark_stones",       AllExceptDunes, 95, minDistanceFromSpawn: 20, maxDegradation: StructureDegradation.Weathered),
            new("old_wayflag",           new[]{ TerrainBiome.Meadow, TerrainBiome.Drybrush, TerrainBiome.Highlands }, 45, minDistanceFromSpawn: 24, maxDegradation: StructureDegradation.Weathered, hasLoot: true, lootTableId: StructureLootTable.EmptyRuinId, primaryBlock: BlockRegistry.CutstoneBlock),
            new("fallen_branchwood",     new[]{ TerrainBiome.Meadow, TerrainBiome.Pinewild, TerrainBiome.Tundra }, 70, minDistanceFromSpawn: 24, maxDegradation: StructureDegradation.Weathered, primaryBlock: BlockRegistry.SmoothBranchwood),
            new("saltmarker_cairn",      new[]{ TerrainBiome.Dunes, TerrainBiome.Drybrush }, 45, minDistanceFromSpawn: 24, maxDegradation: StructureDegradation.Weathered, hasLoot: true, lootTableId: StructureLootTable.MinerCacheId, primaryBlock: BlockRegistry.WarmGranite),
            new("frostmarker_cairn",     new[]{ TerrainBiome.Tundra, TerrainBiome.Highlands }, 45, minDistanceFromSpawn: 24, maxDegradation: StructureDegradation.Weathered, hasLoot: true, lootTableId: StructureLootTable.CommonSupplyId, primaryBlock: BlockRegistry.DarkSlate),

            new("forager_lean_to",       new[]{ TerrainBiome.Meadow, TerrainBiome.Pinewild, TerrainBiome.Drybrush }, 55, hasLoot: true, lootTableId: StructureLootTable.ForagerFoodId, primaryBlock: BlockRegistry.BranchwoodLog),
            new("resin_tap_grove",       new[]{ TerrainBiome.Pinewild, TerrainBiome.Meadow }, 35, hasLoot: true, lootTableId: StructureLootTable.BuilderCacheId, primaryBlock: BlockRegistry.BranchwoodLog),
            new("wetland_stilt_cache",   new[]{ TerrainBiome.Wetland }, 35, minDistanceFromSpawn: 64, hasLoot: true, lootTableId: StructureLootTable.ForagerFoodId, primaryBlock: BlockRegistry.WorkPlank),
            new("drybrush_niter_pit",    new[]{ TerrainBiome.Drybrush, TerrainBiome.Dunes }, 30, hasLoot: true, lootTableId: StructureLootTable.MinerCacheId, primaryBlock: BlockRegistry.ShingleGravel),
            new("frost_shelter",         new[]{ TerrainBiome.Tundra, TerrainBiome.Highlands }, 30, hasLoot: true, hasStation: true, lootTableId: StructureLootTable.ForagerFoodId, primaryBlock: BlockRegistry.DarkSlate),
            new("bridge_segment",        new[]{ TerrainBiome.Meadow, TerrainBiome.Wetland, TerrainBiome.Pinewild }, 35, minDistanceFromSpawn: 28, maxDegradation: StructureDegradation.Weathered),

            new("weathered_watchpost",   new[]{ TerrainBiome.Meadow, TerrainBiome.Drybrush, TerrainBiome.Highlands }, 20, minDistanceFromSpawn: 56, hasLoot: true, hasStation: true, lootTableId: StructureLootTable.BuilderCacheId, primaryBlock: BlockRegistry.CutstoneBlock, stationBlock: BlockRegistry.MendBench),
            new("ruined_kiln_yard",      new[]{ TerrainBiome.Meadow, TerrainBiome.Drybrush, TerrainBiome.Dunes }, 18, hasLoot: true, hasStation: true, lootTableId: StructureLootTable.BuilderCacheId, primaryBlock: BlockRegistry.FiredBrickBlock, stationBlock: BlockRegistry.ClayKiln),
            new("mossroot_hut_cluster",  new[]{ TerrainBiome.Pinewild, TerrainBiome.Wetland }, 12, hasLoot: true, lootTableId: StructureLootTable.ForagerFoodId, primaryBlock: BlockRegistry.Rootsoil),
            new("sunmetal_survey_tower", new[]{ TerrainBiome.Dunes, TerrainBiome.Highlands }, 6, hasLoot: true, lootTableId: StructureLootTable.MetalCacheId, primaryBlock: BlockRegistry.WarmGranite),
            new("frost_beacon_ruin",     new[]{ TerrainBiome.Tundra, TerrainBiome.Highlands }, 6, hasLoot: true, lootTableId: StructureLootTable.MetalCacheId, primaryBlock: BlockRegistry.DarkSlate),

            // Underground/cave entries use a compact procedural room until authored templates
            // exist. The cave shrine keeps its existing cave-pocket fallback behavior.
            new("cave_shrine",           AllBiomes, 10, minDistanceFromSpawn: 28, maxDegradation: StructureDegradation.Weathered, hasLoot: true, lootTableId: StructureLootTable.CommonSupplyId, placementKind: StructurePlacementKind.CaveFeature),
            new("stoneburrow_cellar",    AllBiomes, 18, hasLoot: true, lootTableId: StructureLootTable.MinerCacheId, placementKind: StructurePlacementKind.Underground, primaryBlock: BlockRegistry.CutstoneBlock, minPlacementY: 45, maxPlacementY: 110),
            new("lumen_hollow",          AllBiomes, 22, hasLoot: true, lootTableId: StructureLootTable.MinerCacheId, placementKind: StructurePlacementKind.CaveFeature, primaryBlock: BlockRegistry.WhiteLimestone, minPlacementY: 20, maxPlacementY: 100),
            new("ember_vent_outpost",    AllBiomes, 12, hasLoot: true, hasStation: true, lootTableId: StructureLootTable.MetalCacheId, placementKind: StructurePlacementKind.CaveFeature, primaryBlock: BlockRegistry.BlackBasalt, stationBlock: BlockRegistry.BellowsForge, minPlacementY: 5, maxPlacementY: 35),
            new("deep_locker_room",      AllBiomes, 6, hasLoot: true, lootTableId: StructureLootTable.DeepCacheId, placementKind: StructurePlacementKind.Underground, primaryBlock: BlockRegistry.Deepmantle, minPlacementY: 8, maxPlacementY: 28),
            new("staropal_pocket_shrine",AllBiomes, 3, hasLoot: true, lootTableId: StructureLootTable.DeepCacheId, placementKind: StructurePlacementKind.CaveFeature, primaryBlock: BlockRegistry.Deepmantle, minPlacementY: 2, maxPlacementY: 22),
        };

        static readonly StructureCatalogEntry[] CatalogSnapshot = BuildCatalogSnapshot();

        public static IReadOnlyList<StructureCatalogEntry> CatalogEntries => CatalogSnapshot;

        public static void PlaceStructures(
            VoxelWorld world,
            BlockRegistry registry,
            WorldGenerationSettings settings,
            int seed,
            Func<int, int, int> biomeAt = null,    // (x, z) → TerrainBiome as int; null = Meadow everywhere
            List<StructureContainerLoot> lootSink = null) // receives rolled loot for each placed crate
        {
            if (world == null)    throw new ArgumentNullException(nameof(world));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            WorldBounds bounds = world.Bounds;
            int regionsX = Math.Max(1, bounds.Width  / RegionSize);
            int regionsZ = Math.Max(1, bounds.Depth  / RegionSize);

            var accepted = new List<(int x, int z)>();

            for (int rx = 0; rx < regionsX; rx++)
            {
                for (int rz = 0; rz < regionsZ; rz++)
                {
                    uint regionHash = Hash(seed, rx, 0, rz, salt: 7919);

                    // Per-region spawn gate: only ~RegionSpawnChancePercent of regions hold a
                    // structure, preserving the original world density independent of how many
                    // catalog entries happen to be valid for the region's biome.
                    if (regionHash % 100u >= RegionSpawnChancePercent) continue;

                    int localX = (int)(Hash(seed, rx, 1, rz, salt: 3571) % (uint)RegionSize);
                    int localZ = (int)(Hash(seed, rx, 2, rz, salt: 5381) % (uint)RegionSize);
                    int worldX = rx * RegionSize + localX;
                    int worldZ = rz * RegionSize + localZ;

                    TerrainBiome biome = biomeAt != null
                        ? (TerrainBiome)(((biomeAt(worldX, worldZ) % BiomeCount) + BiomeCount) % BiomeCount)
                        : TerrainBiome.Meadow;

                    StructureDefinition def = PickStructureForBiome(biome, seed, rx, rz);
                    if (def == null) continue;

                    if (IsTooCloseToSpawn(worldX, worldZ, settings, def.MinDistanceFromSpawn)) continue;
                    if (IsTooCloseToAccepted(worldX, worldZ, accepted)) continue;

                    int surfaceY = FindSurfaceY(world, worldX, worldZ);
                    if (surfaceY < 0) continue;

                    // Fluids are placed before structures, so the found "surface" over a lake is
                    // the fluid top — ruins don't float on water or emberflow (§5.4).
                    if (FluidBlocks.IsFluid(world.GetBlock(new BlockPosition(worldX, surfaceY, worldZ)))) continue;

                    var degradation = RollDegradation(regionHash, def.MaxDegradation);
                    bool placed;

                    if (def.Id == "cave_shrine")
                    {
                        PlaceCaveShrine(world, worldX, surfaceY, worldZ, seed, def, degradation, lootSink);
                        placed = true;
                    }
                    else if (def.Id == "bridge_segment")
                    {
                        PlaceBridgeSegment(world, worldX, surfaceY + 1, worldZ, degradation, seed);
                        placed = true;
                    }
                    else if (def.PlacementKind != StructurePlacementKind.Surface)
                    {
                        placed = PlaceUndergroundStructure(world, worldX, surfaceY, worldZ, degradation, seed, def, lootSink);
                    }
                    else
                    {
                        PlaceRuin(world, worldX, surfaceY + 1, worldZ, degradation, seed, def, lootSink);
                        placed = true;
                    }

                    if (placed)
                        accepted.Add((worldX, worldZ));
                }
            }
        }

        // Creative spawner: places one default ruin with its base at the given position (no
        // degradation, no loot roll). Offline/host creative tools only.
        public static void PlaceStructureAt(VoxelWorld world, int baseX, int baseY, int baseZ, int seed = 0, bool trackChange = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            PlaceRuin(world, baseX, baseY, baseZ, StructureDegradation.Intact, seed, trackChange: trackChange);
        }

        public static bool TryPlaceStructureAt(
            VoxelWorld world,
            string structureId,
            int anchorX,
            int surfaceY,
            int anchorZ,
            int seed = 0,
            List<StructureContainerLoot> lootSink = null,
            bool trackChange = false,
            // Explicit placements default to pristine. Overridable so callers (and tests) can
            // exercise a specific age state, which is otherwise chosen by the region hash.
            StructureDegradation degradation = StructureDegradation.Intact)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(structureId)) return false;

            StructureDefinition def = FindDefinitionById(structureId);
            if (def == null)
                return false;

            if (def.Id == "cave_shrine")
            {
                PlaceCaveShrine(world, anchorX, surfaceY, anchorZ, seed, def, degradation, lootSink);
                return true;
            }

            if (def.Id == "bridge_segment")
            {
                PlaceBridgeSegment(world, anchorX, surfaceY + 1, anchorZ, degradation, seed);
                return true;
            }

            if (def.PlacementKind != StructurePlacementKind.Surface)
                return PlaceUndergroundStructure(world, anchorX, surfaceY, anchorZ, degradation, seed, def, lootSink);

            PlaceRuin(world, anchorX, surfaceY + 1, anchorZ, degradation, seed, def, lootSink, trackChange);
            return true;
        }

        public static int FindSurfaceY(VoxelWorld world, int x, int z)
        {
            if (x < 0 || x >= world.Bounds.Width || z < 0 || z >= world.Bounds.Depth)
                return -1;

            for (int y = world.Bounds.Height - 1; y >= 0; y--)
            {
                if (world.GetBlock(new BlockPosition(x, y, z)) != BlockRegistry.Air)
                    return y;
            }

            return -1;
        }

        static void PlaceRuin(
            VoxelWorld world,
            int baseX,
            int baseY,
            int baseZ,
            StructureDegradation degradation,
            int seed,
            StructureDefinition def = null,
            List<StructureContainerLoot> lootSink = null,
            bool trackChange = false)
        {
            // 5×5 footprint, 3-block-high walls. Each wall block rolls its own degradation skip
            // (hashed over its actual position + wall index) so weathering holes are not
            // mirrored identically onto all four walls.
            const int wallH = 3;
            BlockId wallBlock = def?.PrimaryBlock ?? BlockRegistry.Graystone;

            for (int dy = 0; dy < wallH; dy++)
            {
                for (int side = 0; side < 5; side++)
                {
                    TryPlaceWallBlock(world, new BlockPosition(baseX + side, baseY + dy, baseZ), degradation, seed, wall: 0, solidBlock: wallBlock, trackChange: trackChange);
                    TryPlaceWallBlock(world, new BlockPosition(baseX + side, baseY + dy, baseZ + 4), degradation, seed, wall: 1, solidBlock: wallBlock, trackChange: trackChange);
                    TryPlaceWallBlock(world, new BlockPosition(baseX, baseY + dy, baseZ + side), degradation, seed, wall: 2, solidBlock: wallBlock, trackChange: trackChange);
                    TryPlaceWallBlock(world, new BlockPosition(baseX + 4, baseY + dy, baseZ + side), degradation, seed, wall: 3, solidBlock: wallBlock, trackChange: trackChange);
                }
            }

            // Place interior items (loot crate at center floor, station near a wall)
            if (def != null && def.HasLoot)
            {
                var lootPos = new BlockPosition(baseX + 2, baseY, baseZ + 2);
                if (world.Bounds.Contains(lootPos) && world.GetBlock(lootPos) == BlockRegistry.Air)
                    PlaceLootCrate(world, lootPos, BlockRegistry.StorageCrate, seed, baseX, baseY, baseZ, def, degradation, lootSink, trackChange);
            }

            if (def != null && def.HasStation)
            {
                var stationPos = new BlockPosition(baseX + 1, baseY, baseZ + 1);
                if (world.Bounds.Contains(stationPos) && world.GetBlock(stationPos) == BlockRegistry.Air)
                    world.SetBlock(stationPos, def.StationBlock, trackChange);
            }

            // Place a glowwick inside intact or weathered structures
            if (def != null && degradation <= StructureDegradation.Weathered)
            {
                var lightPos = new BlockPosition(baseX + 2, baseY + 1, baseZ + 2);
                if (world.Bounds.Contains(lightPos) && world.GetBlock(lightPos) == BlockRegistry.Air)
                    world.SetBlock(lightPos, BlockRegistry.Glowwick, trackChange);
            }
        }

        // Small shrine: 3×3 cutstone base with four corner pillars and a lumen lamp, set into the
        // first cave pocket beneath the column (the surface when the column has no cave). The
        // loot crate sits beside the lamp.
        static void PlaceCaveShrine(VoxelWorld world, int centerX, int surfaceY, int centerZ, int seed, StructureDefinition def, StructureDegradation degradation, List<StructureContainerLoot> lootSink)
        {
            int baseY = FindCaveFloorY(world, centerX, centerZ, surfaceY) ?? surfaceY + 1;

            // 3×3 cutstone floor, with the interior cell above it cleared so the shrine always has
            // headroom (a cave pocket may have rock where the lamp/crate go).
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var floor = new BlockPosition(centerX + dx, baseY - 1, centerZ + dz);
                    if (world.Bounds.Contains(floor))
                        world.SetBlock(floor, BlockRegistry.CutstoneBlock, trackChange: false);

                    bool isCorner = dx != 0 && dz != 0;
                    var interior = new BlockPosition(centerX + dx, baseY, centerZ + dz);
                    if (!isCorner && world.Bounds.Contains(interior))
                        world.SetBlock(interior, BlockRegistry.Air, trackChange: false);
                }
            }

            foreach ((int dx, int dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    var pillar = new BlockPosition(centerX + dx, baseY + dy, centerZ + dz);
                    if (world.Bounds.Contains(pillar))
                        world.SetBlock(pillar, BlockRegistry.CutstoneBlock, trackChange: false);
                }
            }

            var lamp = new BlockPosition(centerX, baseY, centerZ);
            if (world.Bounds.Contains(lamp))
                world.SetBlock(lamp, BlockRegistry.LumenLamp, trackChange: false);

            // The crate sits beside the lamp on the cleared interior, so a shrine always yields
            // loot (it is the reliable all-biome loot source now that ruins skip flooded columns).
            if (def.HasLoot)
            {
                var lootPos = new BlockPosition(centerX + 1, baseY, centerZ);
                if (world.Bounds.Contains(lootPos))
                    PlaceLootCrate(world, lootPos, BlockRegistry.StorageCrate, seed, centerX, baseY, centerZ, def, degradation, lootSink);
            }
}

        static void PlaceLootCrate(
            VoxelWorld world,
            BlockPosition lootPos,
            BlockId containerBlock,
            int seed,
            int anchorX,
            int anchorY,
            int anchorZ,
            StructureDefinition def,
            StructureDegradation degradation,
            List<StructureContainerLoot> lootSink,
            bool trackChange = false)
        {
            if (!world.Bounds.Contains(lootPos))
                return;

            // Worldgen placements are not player changes — keep them out of change tracking so
            // they don't pollute save deltas. Creative/admin structure placement may opt in.
            world.SetBlock(lootPos, containerBlock, trackChange);

            // Roll this structure's loot table deterministically from the anchor position so the
            // host and any client that regenerates the world produce identical contents.
            if (lootSink != null)
            {
                uint lootSeed = Hash(seed, anchorX, anchorY, anchorZ, salt: 6151);
                StructureLootTable table = StructureLootTable.GetById(def.LootTableId);
                List<ContainerLootItem> items = ScaleLootForAge(table.Roll(lootSeed), degradation);
                if (items.Count > 0)
                    lootSink.Add(new StructureContainerLoot(lootPos, items));
            }
        }

        // Applies §14's per-age loot modifier to rolled stack counts. A stack that was rolled at
        // all keeps at least one item — the modifier reduces quantity, it does not delete entries,
        // so a Crumbled structure still yields what its table promised, just less of it.
        static List<ContainerLootItem> ScaleLootForAge(List<ContainerLootItem> items, StructureDegradation degradation)
        {
            float modifier = LootQuantityModifier(degradation);
            if (items == null || items.Count == 0 || modifier >= 1.0f)
                return items;

            var scaled = new List<ContainerLootItem>(items.Count);
            foreach (ContainerLootItem item in items)
            {
                int count = Math.Max(1, (int)Math.Round(item.Count * modifier, MidpointRounding.AwayFromZero));
                scaled.Add(new ContainerLootItem(item.ItemId, count));
            }

            return scaled;
        }

        // First walkable cave pocket beneath the column: ≥2 cells of air over a solid floor,
        // comfortably below the surface so the shrine never opens the column to the sky.
        static int? FindCaveFloorY(VoxelWorld world, int x, int z, int surfaceY)
        {
            for (int y = surfaceY - 8; y >= 8; y--)
            {
                var cell = new BlockPosition(x, y, z);
                var above = new BlockPosition(x, y + 1, z);
                var below = new BlockPosition(x, y - 1, z);

                if (world.Bounds.Contains(cell) && world.Bounds.Contains(above) && world.Bounds.Contains(below) &&
                    world.GetBlock(cell) == BlockRegistry.Air &&
                    world.GetBlock(above) == BlockRegistry.Air &&
                    world.GetBlock(below) != BlockRegistry.Air)
                {
                    return y;
                }
            }

            return null;
        }

        // Weathered plank crossing: a 3-wide × 9-long work-plank deck with stout corner posts,
        // each deck plank rolling its own degradation skip like ruin walls.
        static void PlaceBridgeSegment(VoxelWorld world, int baseX, int baseY, int baseZ, StructureDegradation degradation, int seed)
        {
            const int length = 9;
            const int width = 3;

            for (int dx = 0; dx < length; dx++)
            {
                for (int dz = 0; dz < width; dz++)
                {
                    var deck = new BlockPosition(baseX + dx, baseY, baseZ + dz);
                    int skipChance = (int)degradation * 20;
                    if (skipChance > 0 && Hash(seed, deck.X, deck.Y, deck.Z, salt: 4099) % 100u < (uint)skipChance)
                        continue;

                    if (world.Bounds.Contains(deck))
                        world.SetBlock(deck, BlockRegistry.WorkPlank, trackChange: false);
                }
            }

            foreach ((int dx, int dz) in new[] { (0, 0), (0, width - 1), (length - 1, 0), (length - 1, width - 1) })
            {
                var post = new BlockPosition(baseX + dx, baseY + 1, baseZ + dz);
                if (world.Bounds.Contains(post) && world.GetBlock(post) == BlockRegistry.Air)
                    world.SetBlock(post, BlockRegistry.SmoothBranchwood, trackChange: false);
            }
        }

        // Compact underground/cave room templates (Dormitory, Laboratory, Storage) replacing 
        // the previous procedural fallback logic (technical audit §D4). It cuts a reachable 
        // shaft from the surface column, builds a themed chamber, and rolls authored loot.
        static bool PlaceUndergroundStructure(
            VoxelWorld world,
            int centerX,
            int surfaceY,
            int centerZ,
            StructureDegradation degradation,
            int seed,
            StructureDefinition def,
            List<StructureContainerLoot> lootSink)
        {
            int maxY = Math.Min(def.MaxPlacementY, surfaceY - 6);
            int minY = Math.Max(2, def.MinPlacementY);
            if (maxY < minY)
                return false;

            int baseY = minY + (int)(Hash(seed, centerX, surfaceY, centerZ, salt: 6211) % (uint)(maxY - minY + 1));
            const int radius = 2;
            const int roomHeight = 3;

            // 1. Clear shaft to surface
            for (int y = baseY; y <= surfaceY; y++)
            {
                var shaft = new BlockPosition(centerX, y, centerZ);
                if (world.Bounds.Contains(shaft))
                    world.SetBlock(shaft, BlockRegistry.Air, trackChange: false);
            }

            // 2. Build the shell
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    var floor = new BlockPosition(centerX + dx, baseY - 1, centerZ + dz);
                    var ceiling = new BlockPosition(centerX + dx, baseY + roomHeight, centerZ + dz);
                    if (world.Bounds.Contains(floor))
                        world.SetBlock(floor, def.PrimaryBlock, trackChange: false);
                    if (world.Bounds.Contains(ceiling))
                        world.SetBlock(ceiling, def.PrimaryBlock, trackChange: false);

                    bool wall = Math.Abs(dx) == radius || Math.Abs(dz) == radius;
                    for (int dy = 0; dy < roomHeight; dy++)
                    {
                        var cell = new BlockPosition(centerX + dx, baseY + dy, centerZ + dz);
                        if (!world.Bounds.Contains(cell))
                            continue;

                        if (wall)
                        {
                            int skipChance = MissingBlockChancePercent(degradation);
                            if (skipChance > 0 && Hash(seed, cell.X, cell.Y, cell.Z, salt: 6221) % 100u < (uint)skipChance)
                                world.SetBlock(cell, BlockRegistry.Air, trackChange: false);
                            else
                                world.SetBlock(cell, def.PrimaryBlock, trackChange: false);
                        }
                        else
                        {
                            world.SetBlock(cell, BlockRegistry.Air, trackChange: false);
                        }
                    }
                }
            }

            // 3. Place Authored Interior
            uint roomType = Hash(seed, centerX, baseY, centerZ, salt: 7711) % 3u;
            
            // Common light source
            var lamp = new BlockPosition(centerX, baseY + roomHeight - 1, centerZ);
            if (world.Bounds.Contains(lamp))
                world.SetBlock(lamp, BlockRegistry.LumenLamp, trackChange: false);

            if (roomType == 0) // Dormitory
            {
                var bed = new BlockPosition(centerX - 1, baseY, centerZ + 1);
                if (world.Bounds.Contains(bed)) world.SetBlock(bed, BlockRegistry.Bedroll, trackChange: false);
                
                if (def.HasLoot)
                    PlaceLootCrate(world, new BlockPosition(centerX + 1, baseY, centerZ - 1), BlockRegistry.ReedBasket, seed, centerX, baseY, centerZ, def, degradation, lootSink);
            }
            else if (roomType == 1) // Laboratory
            {
                var station = new BlockPosition(centerX - 1, baseY, centerZ + 1);
                if (world.Bounds.Contains(station)) world.SetBlock(station, def.StationBlock, trackChange: false);
                
                if (def.HasLoot)
                    PlaceLootCrate(world, new BlockPosition(centerX + 1, baseY, centerZ - 1), BlockRegistry.ToolRack, seed, centerX, baseY, centerZ, def, degradation, lootSink);
            }
            else // Storage
            {
                if (def.HasLoot)
                {
                    PlaceLootCrate(world, new BlockPosition(centerX - 1, baseY, centerZ + 1), BlockRegistry.StorageCrate, seed, centerX, baseY, centerZ, def, degradation, lootSink);
                    PlaceLootCrate(world, new BlockPosition(centerX + 1, baseY, centerZ - 1), BlockRegistry.PantryJar, seed, centerX, baseY, centerZ, def, degradation, lootSink);
                }
            }

            return true;
        }

        // The three §14 tables below are public so tests can assert them directly. They are pure
        // functions of the age state; exposing them is what stops the ruleset's numbers drifting
        // out of the generator unnoticed, which is exactly how the previous uniform roll survived.

        // §14 age distribution: intact 20%, weathered 45%, ruined 27%, collapsed 8%. A uniform
        // roll (the previous `hash % 4`) gives each state ~25%, which over-produces both intact
        // and collapsed structures relative to the ruleset.
        public static StructureDegradation RollDegradation(uint hash, StructureDegradation cap)
        {
            uint roll = hash % 100u;
            StructureDegradation state =
                roll < 20u ? StructureDegradation.Intact    :
                roll < 65u ? StructureDegradation.Weathered :
                roll < 92u ? StructureDegradation.Ruined    :
                             StructureDegradation.Crumbled;

            return (StructureDegradation)Math.Min((int)cap, (int)state);
        }

        // §14 "Missing Block Chance" per age state.
        public static int MissingBlockChancePercent(StructureDegradation degradation) => degradation switch
        {
            StructureDegradation.Intact    => 0,
            StructureDegradation.Weathered => 4,
            StructureDegradation.Ruined    => 12,
            _                              => 24,
        };

        // §14 "Loot Modifier" per age state — older structures yield slightly less.
        public static float LootQuantityModifier(StructureDegradation degradation) => degradation switch
        {
            StructureDegradation.Intact    => 1.00f,
            StructureDegradation.Weathered => 1.00f,
            StructureDegradation.Ruined    => 0.85f,
            _                              => 0.70f,
        };

        static void TryPlaceWallBlock(
            VoxelWorld world,
            BlockPosition pos,
            StructureDegradation degradation,
            int seed,
            int wall,
            BlockId solidBlock,
            bool trackChange = false)
        {
            int skipChance = MissingBlockChancePercent(degradation);
            if (skipChance > 0 && Hash(seed, pos.X, pos.Y, pos.Z, salt: 4093 + wall) % 100u < (uint)skipChance)
                return;

            TrySetSolid(world, pos, solidBlock, trackChange);
        }

        static void TrySetSolid(VoxelWorld world, BlockPosition pos, BlockId solidBlock, bool trackChange = false)
        {
            if (world.Bounds.Contains(pos))
                world.SetBlock(pos, solidBlock, trackChange);
        }

        static StructureDefinition PickStructureForBiome(TerrainBiome biome, int seed, int rx, int rz)
        {
            uint roll = Hash(seed, rx, 99, rz, salt: 8191);
            // Collect valid structures for this biome
            int totalWeight = 0;
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (IsAllowedBiome(Catalog[i], biome))
                    totalWeight += Catalog[i].RegionChancePercent;
            }
            if (totalWeight == 0) return null;

            int pick = (int)(roll % (uint)totalWeight);
            int accumulated = 0;
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (!IsAllowedBiome(Catalog[i], biome)) continue;
                accumulated += Catalog[i].RegionChancePercent;
                if (pick < accumulated) return Catalog[i];
            }
            return null;
        }

        static StructureDefinition FindDefinitionById(string id)
        {
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (Catalog[i].Id == id)
                    return Catalog[i];
            }

            return null;
        }

        static StructureCatalogEntry[] BuildCatalogSnapshot()
        {
            var result = new StructureCatalogEntry[Catalog.Length];
            for (int i = 0; i < Catalog.Length; i++)
            {
                StructureDefinition def = Catalog[i];
                result[i] = new StructureCatalogEntry(
                    def.Id,
                    def.PlacementKind != StructurePlacementKind.Surface,
                    def.MinDistanceFromSpawn,
                    def.LootTableId);
            }

            return result;
        }

        static bool IsAllowedBiome(StructureDefinition def, TerrainBiome biome)
        {
            foreach (TerrainBiome b in def.AllowedBiomes)
                if (b == biome) return true;
            return false;
        }

        static bool IsTooCloseToSpawn(int x, int z, WorldGenerationSettings settings, int minDistance)
        {
            int dx = x - settings.SpawnPosition.X;
            int dz = z - settings.SpawnPosition.Z;
            return dx * dx + dz * dz < minDistance * minDistance;
        }

        static bool IsTooCloseToAccepted(int x, int z, List<(int x, int z)> accepted)
        {
            foreach (var (ax, az) in accepted)
            {
                int dx = x - ax;
                int dz = z - az;
                if (dx * dx + dz * dz < MinSpacing * MinSpacing)
                    return true;
            }
            return false;
        }

        static uint Hash(int seed, int x, int y, int z, int salt)
        {
            return DeterministicHash.Hash(seed, x, y, z, salt);
        }
    }

    public readonly struct StructureLootEntry
    {
        public readonly string ItemId;
        public readonly int MinCount;
        public readonly int MaxCount;
        public readonly int Weight;

        public StructureLootEntry(string itemId, int minCount, int maxCount, int weight)
        {
            ItemId = itemId; MinCount = minCount; MaxCount = maxCount; Weight = weight;
        }
    }

    public sealed class StructureLootTable
    {
        public const string CommonSupplyId = "loot_common_supply";
        public const string ForagerFoodId = "loot_forager_food";
        public const string BuilderCacheId = "loot_builder_cache";
        public const string MinerCacheId = "loot_miner_cache";
        public const string MetalCacheId = "loot_metal_cache";
        public const string DeepCacheId = "loot_deep_cache";
        public const string EmptyRuinId = "loot_empty_ruin";

        public readonly string Id;
        public readonly int MinRolls;
        public readonly int MaxRolls;
        public readonly StructureLootEntry[] Entries;

        public StructureLootTable(string id, int minRolls, int maxRolls, StructureLootEntry[] entries)
        {
            // Fail fast: Pick/Roll index into Entries unconditionally, so an empty table would
            // otherwise surface as an IndexOutOfRangeException at loot time instead of here.
            if (entries == null || entries.Length == 0)
                throw new ArgumentException("Structure loot table requires at least one entry.", nameof(entries));

            Id = id; MinRolls = minRolls; MaxRolls = maxRolls; Entries = entries;
        }

        // Pick a random entry using a seed
        public StructureLootEntry Pick(uint rng)
        {
            int total = 0;
            foreach (StructureLootEntry e in Entries) total += e.Weight;
            if (total == 0) return Entries[0];
            int roll = (int)(rng % (uint)total);
            int accumulated = 0;
            foreach (StructureLootEntry e in Entries)
            {
                accumulated += e.Weight;
                if (roll < accumulated) return e;
            }
            return Entries[Entries.Length - 1];
        }

        // Deterministically roll this table MinRolls..MaxRolls times from a single seed and return the
        // aggregated (itemId, count) results. Same seed → same loot, so host and a client that
        // regenerates the world from the world seed produce identical container contents.
        public List<ContainerLootItem> Roll(uint seed)
        {
            uint rng = seed == 0 ? 1u : seed;
            int span = Math.Max(1, MaxRolls - MinRolls + 1);
            int rolls = MinRolls + (int)(NextRng(ref rng) % (uint)span);

            var aggregate = new Dictionary<string, int>();
            for (int i = 0; i < rolls; i++)
            {
                StructureLootEntry entry = Pick(NextRng(ref rng));
                int countSpan = Math.Max(1, entry.MaxCount - entry.MinCount + 1);
                int count = entry.MinCount + (int)(NextRng(ref rng) % (uint)countSpan);
                aggregate.TryGetValue(entry.ItemId, out int existing);
                aggregate[entry.ItemId] = existing + count;
            }

            var result = new List<ContainerLootItem>(aggregate.Count);
            foreach (KeyValuePair<string, int> kv in aggregate)
                result.Add(new ContainerLootItem(kv.Key, kv.Value));
            return result;
        }

        static uint NextRng(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public static StructureLootTable GetById(string id)
        {
            if (id == ForagerFood.Id) return ForagerFood;
            if (id == BuilderCache.Id) return BuilderCache;
            if (id == MinerCache.Id) return MinerCache;
            if (id == MetalCache.Id) return MetalCache;
            if (id == DeepCache.Id) return DeepCache;
            if (id == EmptyRuin.Id) return EmptyRuin;
            return CommonSupply;
        }

        // Canonical common supply loot table
        public static readonly StructureLootTable CommonSupply = new(CommonSupplyId, 2, 4,
            new[]
            {
                new StructureLootEntry("reed_fiber",     2, 8, 16),
                new StructureLootEntry("fiber_cord",     1, 4, 12),
                new StructureLootEntry("stout_pole",     1, 5, 12),
                new StructureLootEntry("surface_pebbles", 2, 8, 10),
                new StructureLootEntry("flinty_shingle", 1, 4,  8),
                new StructureLootEntry("glowwick",       1, 3,  6),
                new StructureLootEntry("berry_cluster",  1, 4,  5),
            });

        // Canonical forager food loot table
        public static readonly StructureLootTable ForagerFood = new(ForagerFoodId, 2, 5,
            new[]
            {
                new StructureLootEntry("berry_cluster",    2, 8, 14),
                new StructureLootEntry("grain_bundle",     2, 6, 12),
                new StructureLootEntry("trail_ration",     1, 3,  7),
                new StructureLootEntry("clean_water_flask",1, 2,  5),
                new StructureLootEntry("brightsalt",       1, 4,  4),
                new StructureLootEntry("field_bandage",    1, 2,  3),
            });

        public static readonly StructureLootTable BuilderCache = new(BuilderCacheId, 3, 6,
            new[]
            {
                new StructureLootEntry("work_plank",        4, 16, 12),
                new StructureLootEntry("branchwood_log",    2,  8, 10),
                new StructureLootEntry("stone_rubble",      6, 18, 10),
                new StructureLootEntry("clay_lump",         3, 12,  8),
                new StructureLootEntry("fired_brick",       2, 10,  7),
                new StructureLootEntry("glass_shard",       1,  6,  5),
                new StructureLootEntry("resin_knot",        1,  5,  5),
            });

        // §15.1 also lists `small_blast_charge` (1, weight 1) here. That item is not registered
        // in ItemId.cs, so the entry is omitted rather than rolling an id no registry can resolve.
        // Restore it when the item exists.
        public static readonly StructureLootTable MinerCache = new(MinerCacheId, 2, 5,
            new[]
            {
                new StructureLootEntry("embercoal",         2, 8, 12),
                new StructureLootEntry("spark_niter",       1, 5,  8),
                new StructureLootEntry("raw_rosycopper",    1, 4,  6),
                new StructureLootEntry("raw_paletin",       1, 3,  4),
                new StructureLootEntry("raw_rustcore",      1, 2,  2),
            });

        public static readonly StructureLootTable MetalCache = new(MetalCacheId, 1, 4,
            new[]
            {
                new StructureLootEntry("rosycopper_bar",    1, 3, 10),
                new StructureLootEntry("paletin_bar",       1, 2,  6),
                new StructureLootEntry("bronze_bar",        1, 2,  4),
                new StructureLootEntry("ironroot_bar",      1, 1,  2),
                new StructureLootEntry("lumen_crystal",     1, 2,  2),
                new StructureLootEntry("sunmetal_bar",      1, 1,  1),
            });

        public static readonly StructureLootTable DeepCache = new(DeepCacheId, 2, 4,
            new[]
            {
                new StructureLootEntry("raw_umbralite",     1, 2,  6),
                new StructureLootEntry("lumen_crystal",     1, 3,  6),
                new StructureLootEntry("lumen_dust",        2, 5,  5),
                new StructureLootEntry("field_bandage",     1, 3,  4),
                new StructureLootEntry("deepsteel_bar",     1, 1,  2),
                new StructureLootEntry("staropal_shard",    1, 1,  1),
            });

        public static readonly StructureLootTable EmptyRuin = new(EmptyRuinId, 0, 1,
            new[]
            {
                new StructureLootEntry("reed_fiber",        1, 2,  8),
                new StructureLootEntry("surface_pebbles",   1, 3,  7),
                new StructureLootEntry("brightsalt",        1, 1,  3),
                new StructureLootEntry("frostglass",        1, 1,  2),
            });

        static readonly StructureLootTable[] AllTables =
        {
            CommonSupply,
            ForagerFood,
            BuilderCache,
            MinerCache,
            MetalCache,
            DeepCache,
            EmptyRuin,
        };

        public static IReadOnlyList<StructureLootTable> All => AllTables;
    }
}
