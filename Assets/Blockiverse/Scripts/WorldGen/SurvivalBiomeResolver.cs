using System;
using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    // Deterministic biome + surface-height resolver derived purely from the world seed.
    //
    // Shared by SurvivalTerrainPreset (generation time) and the runtime (sapling growth
    // dispatch, structure placement) so biome classification is byte-identical everywhere
    // without storing or transmitting the full biome map. Because every result is a pure
    // function of (seed, worldHeight, x, z), a client that knows the seed reconstructs the
    // exact same biomes the host generated.
    //
    // BiomeIndexAt returns the TerrainBiome value as an int (0–6) so callers in other
    // assemblies can consume it without the internal TerrainBiome enum.
    public sealed class SurvivalBiomeResolver
    {
        readonly int seed;
        readonly int worldHeight;

        public SurvivalBiomeResolver(int seed, int worldHeight)
        {
            if (worldHeight < 1)
                throw new ArgumentOutOfRangeException(nameof(worldHeight), "World height must be positive.");

            this.seed = seed;
            this.worldHeight = worldHeight;
        }

        // Raw terrain surface height at the given column (pre spawn-flatten), matching
        // SurvivalTerrainPreset.CalculateSurfaceHeight exactly.
        public int SurfaceHeight(int x, int z)
        {
            double continent = (ValueNoise2D(x, z, scale: 500, seed, salt: 101) - 0.5) * 2.0;
            double hills      = (ValueNoise2D(x, z, scale: 67,  seed, salt: 211) - 0.5) * 2.0;
            double detail     = (ValueNoise2D(x, z, scale: 17,  seed, salt: 323) - 0.5) * 2.0;

            int height = (int)Math.Round(WorldConstants.SeaLevel + continent * 42 + hills * 18 + detail * 5);
            return Clamp(height, 40, worldHeight - 1);
        }

        // TerrainBiome value (0–6) at the given column.
        public int BiomeIndexAt(int x, int z) => (int)BiomeAt(x, z);

        internal TerrainBiome BiomeAt(int x, int z) => Classify(x, z, SurfaceHeight(x, z));

        internal TerrainBiome Classify(int x, int z, int surfaceY)
        {
            if (surfaceY >= 130)
                return TerrainBiome.Highlands;

            double temperature = ValueNoise2D(x, z, scale: 250, seed + 11, salt: 511);
            double moisture    = ValueNoise2D(x, z, scale: 250, seed + 23, salt: 737);
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

        // ── Shared deterministic noise/hash primitives ───────────────────────

        internal static double ValueNoise2D(int x, int z, int scale, int seed, int salt)
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

        internal static uint Hash(int seed, int x, int y, int z, int salt)
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

        static double SmoothStep(double value) => value * value * (3d - 2d * value);

        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
