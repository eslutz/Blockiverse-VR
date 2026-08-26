using System;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;

namespace Blockiverse.Networking
{
    public static class WorldSaveGeneration
    {
        public const int BuilderWorldHeight = 64;
        public const int FlatBuilderGroundHeight = 8;
        // Survival worlds always generate at the canonical full height; derived from
        // WorldConstants.WorldMaxY because WorldConstants has no height constant of its own.
        public const int SurvivalWorldHeight = WorldConstants.WorldMaxY + 1;

        public static GeneratedCreativeWorld GenerateDefaultWorld(int seed = 6401)
        {
            BlockRegistry registry = BlockRegistry.Default;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefaultSurvivalLite(seed);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        public static GeneratedCreativeWorld GenerateTitleWorld(int seed = 6401)
        {
            BlockRegistry registry = BlockRegistry.Default;
            var settings = new WorldGenerationSettings(
                TitleMiniWorldPreset.Size,
                SurvivalWorldHeight,
                TitleMiniWorldPreset.Size,
                WorldConstants.ChunkSize,
                seed,
                WorldConstants.SeaLevel,
                new BlockPosition(TitleMiniWorldPreset.Size / 2, WorldConstants.SeaLevel + 1, TitleMiniWorldPreset.Size / 2));
            return new GeneratedCreativeWorld(
                registry,
                settings,
                new TitleMiniWorldPreset(registry, settings).Generate(),
                CreativeWorldGenerationPreset.SurvivalLite);
        }

        public static GeneratedCreativeWorld GenerateNewWorld(
            string worldPreset,
            ulong menuSeed,
            string worldSize)
        {
            int seed = FoldSeed(menuSeed);
            (int width, int depth) = SizeFor(worldSize);
            return GenerateNewWorld(worldPreset, seed, width, depth);
        }

        public static GeneratedCreativeWorld GenerateNewWorld(
            string worldPreset,
            int seed,
            int width,
            int depth)
        {
            BlockRegistry registry = BlockRegistry.Default;

            switch (GenerationPresetForId(worldPreset))
            {
                case CreativeWorldGenerationPreset.FlatCreative:
                    var flatSettings = new WorldGenerationSettings(
                        width, BuilderWorldHeight, depth, WorldConstants.ChunkSize, seed, FlatBuilderGroundHeight);
                    return GenerateWorld(CreativeWorldGenerationPreset.FlatCreative, registry, flatSettings);
            }

            WorldGenerationSettings settings = WorldGenerationSettings.CreateSurvivalTerrain(
                width, SurvivalWorldHeight, depth, WorldConstants.ChunkSize, seed);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        public static CreativeWorldGenerationPreset GenerationPresetForId(string presetId)
        {
            string normalized = WorldPresetIds.Normalize(presetId);
            if (string.Equals(normalized, WorldPresetIds.FlatBuilder, StringComparison.OrdinalIgnoreCase))
                return CreativeWorldGenerationPreset.FlatCreative;
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

        // Maps the menu's world-size selector to bounded dimensions. The renderer only keeps the
        // nearby chunks live, but generation and the authoritative VoxelWorld remain full-world.
        public static (int width, int depth) SizeFor(string worldSize)
        {
            if (string.Equals(worldSize, "x_large", StringComparison.OrdinalIgnoreCase))
                return (512, 512);
            if (string.Equals(worldSize, "large", StringComparison.OrdinalIgnoreCase))
                return (384, 384);
            if (string.Equals(worldSize, "medium", StringComparison.OrdinalIgnoreCase))
                return (256, 256);

            return (192, 192);
        }

        // Folds the 64-bit menu seed into the generator's int seed deterministically.
        public static int FoldSeed(ulong seed) => unchecked((int)(seed ^ (seed >> 32)));

        public static GeneratedCreativeWorld Regenerate(WorldSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.SchemaVersion != WorldSaveService.CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Cannot regenerate world with schema version {data.SchemaVersion} (expected {WorldSaveService.CurrentSchemaVersion}). Legacy migration is unsupported.");
            }

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
            }

            WorldGenerationSettings settings = WorldGenerationSettings.CreateSurvivalTerrain(
                data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed, spawnPosition);
            return GenerateWorld(CreativeWorldGenerationPreset.SurvivalLite, registry, settings);
        }

        static BlockPosition? ResolveSavedSpawnPosition(WorldSaveData data)
        {
            return data.HasSpawnPosition
                ? new BlockPosition(data.SpawnX, data.SpawnY, data.SpawnZ)
                : null;
        }

    }
}
