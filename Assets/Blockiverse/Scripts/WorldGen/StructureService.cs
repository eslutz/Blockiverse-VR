using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public enum StructureDegradation { Intact, Weathered, Ruined, Crumbled }
    public enum StructureTerrainFit  { SnapToSurface, Flatten }

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

        public StructureDefinition(
            string id,
            TerrainBiome[] allowedBiomes,
            int regionChancePercent,
            int minDistanceFromSpawn = 80,
            StructureDegradation maxDegradation = StructureDegradation.Ruined,
            bool hasLoot = false,
            bool hasStation = false)
        {
            Id = id;
            AllowedBiomes = allowedBiomes;
            RegionChancePercent = regionChancePercent;
            MinDistanceFromSpawn = minDistanceFromSpawn;
            MaxDegradation = maxDegradation;
            HasLoot = hasLoot;
            HasStation = hasStation;
        }
    }

    public sealed class StructureService
    {
        // One candidate per 32×32 chunk region; must be ≥ 48 blocks from any other.
        const int RegionSize      = 32;
        const int MinSpacing      = 48;
        const int SpawnExclusion  = 40;
        // ~30% of regions hold a structure (matches the original pre-catalog density).
        const uint RegionSpawnChancePercent = 30;
        // Number of TerrainBiome enum values; biome indices are wrapped into this range.
        const int BiomeCount = 7;

        static readonly StructureDefinition[] Catalog =
        {
            new("pathmark_stones",     new[]{ TerrainBiome.Meadow, TerrainBiome.Pinewild, TerrainBiome.Wetland, TerrainBiome.Drybrush, TerrainBiome.Highlands, TerrainBiome.Tundra }, 80, minDistanceFromSpawn: 20, StructureDegradation.Weathered),
            new("forager_lean_to",     new[]{ TerrainBiome.Meadow, TerrainBiome.Pinewild, TerrainBiome.Drybrush }, 55, hasLoot: true),
            new("resin_tap_grove",     new[]{ TerrainBiome.Pinewild, TerrainBiome.Meadow }, 35, hasLoot: true, hasStation: false),
            new("frost_shelter",       new[]{ TerrainBiome.Tundra, TerrainBiome.Highlands }, 30, hasLoot: true, hasStation: true),
            new("drybrush_niter_pit",  new[]{ TerrainBiome.Drybrush, TerrainBiome.Dunes }, 30, hasLoot: true),
            new("weathered_watchpost", new[]{ TerrainBiome.Meadow, TerrainBiome.Drybrush, TerrainBiome.Highlands }, 20, minDistanceFromSpawn: 128, hasLoot: true, hasStation: true),
        };

        public static void PlaceStructures(
            VoxelWorld world,
            BlockRegistry registry,
            WorldGenerationSettings settings,
            int seed,
            Func<int, int, int> biomeAt = null)  // (x, z) → TerrainBiome as int; null = Meadow everywhere
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

                    accepted.Add((worldX, worldZ));
                    var degradation = (StructureDegradation)(Math.Min((int)def.MaxDegradation, (int)(regionHash % 4u)));
                    PlaceRuin(world, worldX, surfaceY + 1, worldZ, degradation, seed, def);
                }
            }
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

        static void PlaceRuin(VoxelWorld world, int baseX, int baseY, int baseZ, StructureDegradation degradation, int seed, StructureDefinition def = null)
        {
            // 5×5 footprint, 3-block-high walls
            const int wallH = 3;

            for (int dy = 0; dy < wallH; dy++)
            {
                for (int side = 0; side < 5; side++)
                {
                    // Skip wall blocks for more degraded states
                    uint skipHash = Hash(seed, baseX + side, baseY + dy, baseZ, salt: 4093);
                    int skipChance = (int)degradation * 20;
                    if (skipChance > 0 && skipHash % 100u < (uint)skipChance)
                        continue;

                    // North wall
                    TrySetSolid(world, new BlockPosition(baseX + side, baseY + dy, baseZ));
                    // South wall
                    TrySetSolid(world, new BlockPosition(baseX + side, baseY + dy, baseZ + 4));
                    // West wall (corners already placed)
                    TrySetSolid(world, new BlockPosition(baseX,     baseY + dy, baseZ + side));
                    // East wall
                    TrySetSolid(world, new BlockPosition(baseX + 4, baseY + dy, baseZ + side));
                }
            }

            // Place interior items (loot crate at center floor, station near a wall)
            if (def != null && def.HasLoot)
            {
                var lootPos = new BlockPosition(baseX + 2, baseY, baseZ + 2);
                if (world.Bounds.Contains(lootPos) && world.GetBlock(lootPos) == BlockRegistry.Air)
                    world.SetBlock(lootPos, BlockRegistry.StorageCrate);
            }

            if (def != null && def.HasStation)
            {
                var stationPos = new BlockPosition(baseX + 1, baseY, baseZ + 1);
                if (world.Bounds.Contains(stationPos) && world.GetBlock(stationPos) == BlockRegistry.Air)
                    world.SetBlock(stationPos, BlockRegistry.Campfire);
            }

            // Place a glowwick inside intact or weathered structures
            if (def != null && degradation <= StructureDegradation.Weathered)
            {
                var lightPos = new BlockPosition(baseX + 2, baseY + 1, baseZ + 2);
                if (world.Bounds.Contains(lightPos) && world.GetBlock(lightPos) == BlockRegistry.Air)
                    world.SetBlock(lightPos, BlockRegistry.Glowwick);
            }
        }

        static void TrySetSolid(VoxelWorld world, BlockPosition pos)
        {
            if (world.Bounds.Contains(pos))
                world.SetBlock(pos, BlockRegistry.Graystone, trackChange: false);
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

        static bool IsTooCloseToSpawn(int x, int z, WorldGenerationSettings settings)
            => IsTooCloseToSpawn(x, z, settings, SpawnExclusion);

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
            unchecked
            {
                uint hash = 2166136261u;
                hash ^= (uint)seed;  hash *= 16777619u;
                hash ^= (uint)x;     hash *= 16777619u;
                hash ^= (uint)y;     hash *= 16777619u;
                hash ^= (uint)z;     hash *= 16777619u;
                hash ^= (uint)salt;  hash *= 16777619u;
                return hash;
            }
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
        public readonly string Id;
        public readonly int MinRolls;
        public readonly int MaxRolls;
        public readonly StructureLootEntry[] Entries;

        public StructureLootTable(string id, int minRolls, int maxRolls, StructureLootEntry[] entries)
        {
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

        // Canonical common supply loot table
        public static readonly StructureLootTable CommonSupply = new("loot_common_supply", 2, 4,
            new[]
            {
                new StructureLootEntry("reed_fiber",   2, 8, 16),
                new StructureLootEntry("fiber_cord",   1, 4, 12),
                new StructureLootEntry("stout_pole",   1, 5, 12),
                new StructureLootEntry("stone_pebble", 2, 8, 10),
                new StructureLootEntry("flint_shard",  1, 4,  8),
                new StructureLootEntry("glowwick",     1, 3,  6),
                new StructureLootEntry("berry_cluster",1, 4,  5),
            });

        // Canonical forager food loot table
        public static readonly StructureLootTable ForagerFood = new("loot_forager_food", 2, 5,
            new[]
            {
                new StructureLootEntry("berry_cluster",    2, 8, 14),
                new StructureLootEntry("grain_bundle",     2, 6, 12),
                new StructureLootEntry("trail_ration",     1, 3,  7),
                new StructureLootEntry("clean_water_flask",1, 2,  5),
                new StructureLootEntry("brightsalt",       1, 4,  4),
                new StructureLootEntry("field_bandage",    1, 2,  3),
            });
    }
}
