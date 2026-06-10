namespace Blockiverse.Voxel
{
    // Canonical deterministic hash for all seed-derived world rolls (terrain noise, biome
    // classification, structure placement, farming growth). FNV-1a-seeded mix with a final
    // avalanche so consecutive coordinates decorrelate; every consumer must use this single
    // algorithm so a world seed reproduces identically across systems and peers.
    public static class DeterministicHash
    {
        public static uint Hash(int seed, int x, int y, int z, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, (uint)seed);
                hash = Mix(hash, (uint)x);
                hash = Mix(hash, (uint)y);
                hash = Mix(hash, (uint)z);
                hash = Mix(hash, (uint)salt);
                return Avalanche(hash);
            }
        }

        // Roll in [0, 1) over the standard inputs plus a 64-bit extra (mixed as low then high
        // 32 bits) — used by farming growth, where the extra is the growth-interval index.
        public static double UnitRoll(int seed, int x, int y, int z, int salt, long extra)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, (uint)seed);
                hash = Mix(hash, (uint)x);
                hash = Mix(hash, (uint)y);
                hash = Mix(hash, (uint)z);
                hash = Mix(hash, (uint)salt);
                hash = Mix(hash, (uint)extra);
                hash = Mix(hash, (uint)(extra >> 32));
                return Avalanche(hash) / 4294967296.0;
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

        static uint Avalanche(uint hash)
        {
            unchecked
            {
                hash ^= hash >> 16;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                hash *= 3266489917u;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
