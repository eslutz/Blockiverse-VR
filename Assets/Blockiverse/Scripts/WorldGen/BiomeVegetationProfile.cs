using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    /// <summary>
    /// One plant a biome can scatter, with the surfaces it accepts.
    /// </summary>
    public readonly struct VegetationPlant
    {
        public VegetationPlant(BlockId block, int weight, params BlockId[] validSurfaces)
        {
            Block = block;
            Weight = weight;
            ValidSurfaces = validSurfaces;
        }

        public readonly BlockId Block;

        /// <summary>Relative weight within its own list. Not a probability — the list is
        /// weight-picked once the density roll has already decided that something goes here.</summary>
        public readonly int Weight;

        /// <summary>Surfaces this plant will grow on. Empty means any solid block.</summary>
        public readonly BlockId[] ValidSurfaces;

        public bool AcceptsSurface(BlockId surface)
        {
            if (ValidSurfaces == null || ValidSurfaces.Length == 0)
                return true;

            for (int i = 0; i < ValidSurfaces.Length; i++)
            {
                if (ValidSurfaces[i].Equals(surface))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Per-biome vegetation data (voxel_biome_vegetation_ruleset §7/§8), replacing the two
    /// hardcoded switch statements that previously carried these numbers.
    ///
    /// Three separate scatter passes read this, and they are deliberately distinct:
    ///   TreeDensityPercent  — canopy trees, one roll per column.
    ///   WildPlants          — sparse feature plants (berrybush, reedgrass…) that feed the
    ///                         harvest/regrowth loop. One roll per column.
    ///   GroundCover         — the dense decorative layer (§12), sampled per chunk rather than
    ///                         per column, because it is far denser and a per-column roll at these
    ///                         rates would walk the whole world map for every species.
    /// </summary>
    public sealed class BiomeVegetationProfile
    {
        public BiomeVegetationProfile(
            TerrainBiome biome,
            int treeDensityPercent,
            int wildPlantDensityPermille,
            int groundCoverChancePermille,
            VegetationPlant[] wildPlants,
            VegetationPlant[] groundCover)
        {
            Biome = biome;
            TreeDensityPercent = treeDensityPercent;
            WildPlantDensityPermille = wildPlantDensityPermille;
            GroundCoverChancePermille = groundCoverChancePermille;
            WildPlants = wildPlants ?? System.Array.Empty<VegetationPlant>();
            GroundCover = groundCover ?? System.Array.Empty<VegetationPlant>();
        }

        public readonly TerrainBiome Biome;

        /// <summary>Percent (0-100), rolled against hash % 100u. NOT permille — the rest of the
        /// terrain preset uses 0-1000, and changing the unit here changes existing seeds.</summary>
        public readonly int TreeDensityPercent;

        /// <summary>Permille (0-1000) for the sparse feature plants.</summary>
        public readonly int WildPlantDensityPermille;

        /// <summary>Permille (0-1000), matching §8 exactly (0.08-0.55).
        ///
        /// These were briefly shipped at half the ruleset values as a performance hedge. That was
        /// wrong twice over: it is a silent deviation from the canonical spec that nothing would
        /// have surfaced, and "half" is exactly as unmeasured as "full". The real backstop is
        /// MaxGroundCoverPerChunk, which bounds the worst case regardless of these numbers — at
        /// spec, Pinewild's 0.55 already clamps against it.</summary>
        public readonly int GroundCoverChancePermille;

        public readonly VegetationPlant[] WildPlants;
        public readonly VegetationPlant[] GroundCover;
    }

    /// <summary>
    /// The canonical per-biome profiles (§8 and the §11 placement table).
    /// </summary>
    public static class BiomeVegetationProfiles
    {
        // Surface sets, named once so the tables below read as the ruleset does.
        static readonly BlockId[] Temperate = { BlockRegistry.MeadowTurf, BlockRegistry.Rootsoil, BlockRegistry.LooseLoam };
        static readonly BlockId[] Meadowy = { BlockRegistry.MeadowTurf, BlockRegistry.LooseLoam };
        static readonly BlockId[] Dry = { BlockRegistry.DryTurf, BlockRegistry.PaleSand, BlockRegistry.LooseLoam };
        static readonly BlockId[] DampShade = { BlockRegistry.Rootsoil, BlockRegistry.Graystone, BlockRegistry.WhiteLimestone };
        static readonly BlockId[] Brine = { BlockRegistry.PaleSand, BlockRegistry.RiverSilt, BlockRegistry.Claybed };
        static readonly BlockId[] Cold = { BlockRegistry.SnowcapTurf, BlockRegistry.Graystone, BlockRegistry.WarmGranite };
        static readonly BlockId[] ColdSoil = { BlockRegistry.SnowcapTurf, BlockRegistry.Rootsoil };
        static readonly BlockId[] Exposed = { BlockRegistry.WarmGranite, BlockRegistry.Graystone, BlockRegistry.MeadowTurf };
        static readonly BlockId[] Sandy = { BlockRegistry.PaleSand, BlockRegistry.DryTurf };
        static readonly BlockId[] Forest = { BlockRegistry.Rootsoil, BlockRegistry.MeadowTurf, BlockRegistry.LooseLoam };

        static readonly BiomeVegetationProfile[] Profiles =
        {
            new(TerrainBiome.Meadow, treeDensityPercent: 25, wildPlantDensityPermille: 28,
                groundCoverChancePermille: 300,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Berrybush, 1) },
                groundCover: new[]
                {
                    // Meadow Tuft dominates: §13.1 calls for a continuous layer the player walks
                    // through, not scatter. Wildflowers punctuate it.
                    new VegetationPlant(BlockRegistry.MeadowTuft, 70, Temperate),
                    new VegetationPlant(BlockRegistry.WildflowerCluster, 20, Meadowy),
                    new VegetationPlant(BlockRegistry.FallenLeaves, 10, Forest),
                }),

            new(TerrainBiome.Pinewild, treeDensityPercent: 55, wildPlantDensityPermille: 18,
                groundCoverChancePermille: 550,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Berrybush, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.MossCarpet, 45, DampShade),
                    new VegetationPlant(BlockRegistry.FallenLeaves, 35, Forest),
                    new VegetationPlant(BlockRegistry.MeadowTuft, 20, Temperate),
                }),

            new(TerrainBiome.Wetland, treeDensityPercent: 20, wildPlantDensityPermille: 45,
                groundCoverChancePermille: 420,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Reedgrass, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.MossCarpet, 50, DampShade),
                    new VegetationPlant(BlockRegistry.SaltReed, 25, Brine),
                    new VegetationPlant(BlockRegistry.MeadowTuft, 25, Temperate),
                }),

            new(TerrainBiome.Drybrush, treeDensityPercent: 8, wildPlantDensityPermille: 30,
                groundCoverChancePermille: 250,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Thornbrush, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.DrygrassTuft, 70, Dry),
                    new VegetationPlant(BlockRegistry.DuneSage, 30, Sandy),
                }),

            new(TerrainBiome.Dunes, treeDensityPercent: 3, wildPlantDensityPermille: 12,
                groundCoverChancePermille: 80,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Thornbrush, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.DuneSage, 55, Sandy),
                    new VegetationPlant(BlockRegistry.DrygrassTuft, 30, Dry),
                    new VegetationPlant(BlockRegistry.SaltReed, 15, Brine),
                }),

            // Tundra previously fell through WildPlantForBiome's default and shipped ZERO wild
            // plants, against a ruleset value of 0.20 — a silent gap, because "no case" and
            // "density 0" are indistinguishable in a switch.
            new(TerrainBiome.Tundra, treeDensityPercent: 10, wildPlantDensityPermille: 16,
                groundCoverChancePermille: 200,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.Berrybush, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.SnowLichen, 60, Cold),
                    new VegetationPlant(BlockRegistry.FrostFern, 40, ColdSoil),
                }),

            new(TerrainBiome.Highlands, treeDensityPercent: 10, wildPlantDensityPermille: 14,
                groundCoverChancePermille: 180,
                wildPlants: new[] { new VegetationPlant(BlockRegistry.GrainStalk, 1) },
                groundCover: new[]
                {
                    new VegetationPlant(BlockRegistry.WindrootShrub, 45, Exposed),
                    new VegetationPlant(BlockRegistry.DrygrassTuft, 30, Dry),
                    new VegetationPlant(BlockRegistry.MeadowTuft, 25, Temperate),
                }),
        };

        public static BiomeVegetationProfile For(TerrainBiome biome)
        {
            for (int i = 0; i < Profiles.Length; i++)
            {
                if (Profiles[i].Biome == biome)
                    return Profiles[i];
            }

            return Profiles[0];
        }

        public static System.Collections.Generic.IReadOnlyList<BiomeVegetationProfile> All => Profiles;

        /// <summary>Weight-picks a plant that accepts <paramref name="surface"/>, or returns false
        /// when the biome has nothing that will grow there. Surface filtering happens INSIDE the
        /// pick so a biome whose dominant species rejects this block still gets its runner-up,
        /// rather than the column silently staying bare.</summary>
        public static bool TryPickPlant(VegetationPlant[] table, BlockId surface, uint roll, out BlockId block)
        {
            block = BlockRegistry.Air;
            if (table == null || table.Length == 0)
                return false;

            int total = 0;
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].AcceptsSurface(surface))
                    total += table[i].Weight;
            }

            if (total <= 0)
                return false;

            int target = (int)(roll % (uint)total);
            int accumulated = 0;
            for (int i = 0; i < table.Length; i++)
            {
                if (!table[i].AcceptsSurface(surface))
                    continue;

                accumulated += table[i].Weight;
                if (target < accumulated)
                {
                    block = table[i].Block;
                    return true;
                }
            }

            return false;
        }
    }
}
