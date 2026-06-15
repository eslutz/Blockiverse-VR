using System;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;

namespace Blockiverse.Gameplay
{
    public static class WorldSaveGeneration
    {
        public const int BuilderWorldHeight = 64;
        public const int FlatBuilderGroundHeight = 8;
        public const int VoidBuilderGroundHeight = 32;
        // Survival worlds always generate at the canonical full height; derived from
        // WorldConstants.WorldMaxY because WorldConstants has no height constant of its own.
        public const int SurvivalWorldHeight = WorldConstants.WorldMaxY + 1;

        public static GeneratedCreativeWorld GenerateDefaultWorld(int seed = 6401)
        {
            BlockRegistry registry = BlockRegistry.Default;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        public static GeneratedCreativeWorld GenerateNewWorld(
            string worldPreset,
            ulong menuSeed,
            string worldSize,
            string startingBiome)
        {
            int seed = FoldSeed(menuSeed);
            (int width, int depth) = SizeFor(worldSize);
            return GenerateNewWorld(worldPreset, seed, width, depth, startingBiome);
        }

        public static GeneratedCreativeWorld GenerateNewWorld(
            string worldPreset,
            int seed,
            int width,
            int depth,
            string startingBiome)
        {
            BlockRegistry registry = BlockRegistry.Default;

            switch (GenerationPresetForId(worldPreset))
            {
                case CreativeWorldGenerationPreset.FlatCreative:
                    var flatSettings = new WorldGenerationSettings(
                        width, BuilderWorldHeight, depth, WorldConstants.ChunkSize, seed, FlatBuilderGroundHeight);
                    return GenerateWorld(CreativeWorldGenerationPreset.FlatCreative, registry, flatSettings);
                case CreativeWorldGenerationPreset.VoidBuilder:
                    var voidSettings = new WorldGenerationSettings(
                        width, BuilderWorldHeight, depth, WorldConstants.ChunkSize, seed, VoidBuilderGroundHeight);
                    return GenerateWorld(CreativeWorldGenerationPreset.VoidBuilder, registry, voidSettings);
            }

            BlockPosition? spawn = FindSpawnForBiome(
                seed,
                width,
                SurvivalWorldHeight,
                depth,
                WorldConstants.SeaLevel,
                startingBiome);
            var settings = new WorldGenerationSettings(
                width, SurvivalWorldHeight, depth, WorldConstants.ChunkSize, seed, WorldConstants.SeaLevel, spawn);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        public static CreativeWorldGenerationPreset GenerationPresetForId(string presetId)
        {
            string normalized = WorldPresetIds.Normalize(presetId);
            if (string.Equals(normalized, WorldPresetIds.FlatBuilder, StringComparison.OrdinalIgnoreCase))
                return CreativeWorldGenerationPreset.FlatCreative;
            if (string.Equals(normalized, WorldPresetIds.VoidBuilder, StringComparison.OrdinalIgnoreCase))
                return CreativeWorldGenerationPreset.VoidBuilder;
            return CreativeWorldGenerationPreset.SurvivalLite;
        }

        public static GeneratedCreativeWorld GenerateWorld(
            CreativeWorldGenerationPreset preset,
            BlockRegistry registry,
            WorldGenerationSettings settings)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            switch (preset)
            {
                case CreativeWorldGenerationPreset.FlatCreative:
                    return new GeneratedCreativeWorld(
                        registry,
                        settings,
                        new FlatBuilderPreset(registry, settings).Generate(),
                        CreativeWorldGenerationPreset.FlatCreative);
                case CreativeWorldGenerationPreset.VoidBuilder:
                    return new GeneratedCreativeWorld(
                        registry,
                        settings,
                        new VoidBuilderPreset(registry, settings).Generate(),
                        CreativeWorldGenerationPreset.VoidBuilder);
                default:
                    var survivalPreset = new SurvivalTerrainPreset(registry, settings);
                    VoxelWorld world = survivalPreset.Generate();
                    return new GeneratedCreativeWorld(
                        registry,
                        settings,
                        world,
                        CreativeWorldGenerationPreset.SurvivalLite,
                        survivalPreset.ContainerLoot);
            }
        }

        // Maps the menu's world-size selector to bounded dimensions. The ruleset keeps an
        // "infinite" canonical value for future region streaming, but the current UI presents it
        // as a 256x256 preview so it is not mistaken for an unbounded world.
        public static (int width, int depth) SizeFor(string worldSize)
        {
            switch (worldSize)
            {
                case "medium": return (192, 192);
                case "large":
                case "infinite": return (256, 256);
                default: return (128, 128);
            }
        }

        // Folds the 64-bit menu seed into the generator's int seed deterministically.
        public static int FoldSeed(ulong seed) => unchecked((int)(seed ^ (seed >> 32)));

        public static GeneratedCreativeWorld Regenerate(WorldSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            BlockRegistry registry = BlockRegistry.Default;
            BlockPosition? spawnPosition = ResolveSavedSpawnPosition(data);

            switch (GenerationPresetForId(data.WorldPreset))
            {
                case CreativeWorldGenerationPreset.FlatCreative:
                    var flatSettings = new WorldGenerationSettings(
                        data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed,
                        groundHeight: Math.Min(FlatBuilderGroundHeight, data.Height - 2),
                        spawnPosition: spawnPosition);
                    return GenerateWorld(CreativeWorldGenerationPreset.FlatCreative, registry, flatSettings);
                case CreativeWorldGenerationPreset.VoidBuilder:
                    var voidSettings = new WorldGenerationSettings(
                        data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed,
                        groundHeight: Math.Min(VoidBuilderGroundHeight, data.Height - 2),
                        spawnPosition: spawnPosition);
                    return GenerateWorld(CreativeWorldGenerationPreset.VoidBuilder, registry, voidSettings);
            }

            int survivalGroundHeight = Math.Min(WorldConstants.SeaLevel, data.Height - 2);
            var settings = new WorldGenerationSettings(
                data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed, survivalGroundHeight, spawnPosition);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        static BlockPosition? ResolveSavedSpawnPosition(WorldSaveData data)
        {
            return data.HasSpawnPosition
                ? new BlockPosition(data.SpawnX, data.SpawnY, data.SpawnZ)
                : null;
        }

        // Searches outward from the world center for a dry-land column of the requested starting
        // biome ("balanced" accepts any biome). Columns below sea level flood with fluid (§5.4),
        // so the spawn prefers terrain at or above it rather than terraforming an island into a
        // lake. Pure seed math via SurvivalBiomeResolver, so the search agrees with what the
        // generator will build.
        static BlockPosition? FindSpawnForBiome(
            int seed,
            int width,
            int height,
            int depth,
            int groundHeight,
            string startingBiome)
        {
            int target = SurvivalBiomeResolver.BiomeIndexForCanonicalId(startingBiome);

            var resolver = new SurvivalBiomeResolver(seed, height);
            int centerX = width / 2;
            int centerZ = depth / 2;
            const int edgeMargin = 8;
            const int step = 4;

            bool Matches(int x, int z) =>
                (target < 0 || resolver.BiomeIndexAt(x, z) == target) &&
                resolver.SurfaceHeight(x, z) >= groundHeight;

            if (Matches(centerX, centerZ))
                return null; // center already matches — keep the default spawn

            int maxRadius = Math.Max(width, depth);
            for (int radius = step; radius < maxRadius; radius += step)
            {
                for (int dx = -radius; dx <= radius; dx += step)
                {
                    for (int dz = -radius; dz <= radius; dz += step)
                    {
                        // Ring only — interior radii were already covered.
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                            continue;

                        int x = centerX + dx;
                        int z = centerZ + dz;
                        if (x < edgeMargin || x >= width - edgeMargin || z < edgeMargin || z >= depth - edgeMargin)
                            continue;

                        if (Matches(x, z))
                            return new BlockPosition(x, groundHeight + 1, z);
                    }
                }
            }

            return null; // no dry match in this seed — fall back to the center spawn
        }
    }
}
