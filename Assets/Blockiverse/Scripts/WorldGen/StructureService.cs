using System;
using System.Collections.Generic;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public enum StructureDegradation { Intact, Weathered, Ruined, Crumbled }
    public enum StructureTerrainFit  { SnapToSurface, Flatten }

    public sealed class StructureService
    {
        // One candidate per 32×32 chunk region; must be ≥ 48 blocks from any other.
        const int RegionSize      = 32;
        const int MinSpacing      = 48;
        const int SpawnExclusion  = 40;

        public static void PlaceStructures(
            VoxelWorld world,
            BlockRegistry registry,
            WorldGenerationSettings settings,
            int seed)
        {
            if (world == null)  throw new ArgumentNullException(nameof(world));
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
                    uint hash = Hash(seed, rx, 0, rz, salt: 7919);

                    // 30 % chance of a structure per region
                    if (hash % 100u >= 30u) continue;

                    int localX = (int)(Hash(seed, rx, 1, rz, salt: 3571) % (uint)RegionSize);
                    int localZ = (int)(Hash(seed, rx, 2, rz, salt: 5381) % (uint)RegionSize);
                    int worldX = rx * RegionSize + localX;
                    int worldZ = rz * RegionSize + localZ;

                    if (IsTooCloseToSpawn(worldX, worldZ, settings))
                        continue;

                    if (IsTooCloseToAccepted(worldX, worldZ, accepted))
                        continue;

                    int surfaceY = FindSurfaceY(world, worldX, worldZ);
                    if (surfaceY < 0) continue;

                    accepted.Add((worldX, worldZ));
                    var degradation = (StructureDegradation)(hash % 4u);
                    PlaceRuin(world, worldX, surfaceY + 1, worldZ, degradation, seed);
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

        static void PlaceRuin(VoxelWorld world, int baseX, int baseY, int baseZ, StructureDegradation degradation, int seed)
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
        }

        static void TrySetSolid(VoxelWorld world, BlockPosition pos)
        {
            if (world.Bounds.Contains(pos))
                world.SetBlock(pos, BlockRegistry.Graystone, trackChange: false);
        }

        static bool IsTooCloseToSpawn(int x, int z, WorldGenerationSettings settings)
        {
            int dx = x - settings.SpawnPosition.X;
            int dz = z - settings.SpawnPosition.Z;
            return dx * dx + dz * dz < SpawnExclusion * SpawnExclusion;
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
}
