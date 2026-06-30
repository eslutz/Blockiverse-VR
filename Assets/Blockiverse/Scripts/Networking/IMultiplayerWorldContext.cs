using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.Survival;
using Blockiverse.Persistence;

namespace Blockiverse.Networking
{
    public enum WorldGameMode
    {
        Creative,
        Survival,
    }

    public enum CreativeWorldGenerationPreset
    {
        SurvivalLite,
        FlatCreative,
        VoidBuilder
    }

    public readonly struct WeatherSyncState
    {
        public readonly WeatherState State;
        public readonly int Ticks;
        public readonly uint RngState;

        public WeatherSyncState(WeatherState state, int ticks, uint rngState)
        {
            State = state;
            Ticks = ticks;
            RngState = rngState;
        }
    }

    public readonly struct GeneratedCreativeWorld
    {
        public GeneratedCreativeWorld(
            BlockRegistry registry,
            WorldGenerationSettings settings,
            VoxelWorld world,
            CreativeWorldGenerationPreset generationPreset,
            IReadOnlyList<StructureContainerLoot> containerLoot = null)
        {
            Registry = registry;
            Settings = settings;
            World = world;
            GenerationPreset = generationPreset;
            ContainerLoot = containerLoot;
        }

        public BlockRegistry Registry { get; }
        public WorldGenerationSettings Settings { get; }
        public VoxelWorld World { get; }
        public CreativeWorldGenerationPreset GenerationPreset { get; }
        // Container loot rolled during generation (null when the preset places none).
        public IReadOnlyList<StructureContainerLoot> ContainerLoot { get; }
    }

    public interface ISurvivalVitalsContext
    {
        void RestorePlayerSaveState(SavedPlayerState playerState);
        void ConfigureDifficulty(string difficulty);
        void ResetVitalsToFull();
        SavedPlayerState BuildPlayerSaveState();
    }

    public interface IVoxelWorldRenderer
    {
        void RebuildDirty();
        void RebuildAll();
        void RebuildSpawnRegion(BlockPosition spawn, int radiusChunks = 1);
    }

    public interface IMultiplayerWorldContext
    {
        VoxelWorld World { get; }
        BlockRegistry Registry { get; }
        WorldGenerationSettings Settings { get; }
        WorldTimeClock WorldTimeClock { get; }
        WorldGameMode GameMode { get; }
        string GameModeString { get; }
        void SetGameMode(WorldGameMode mode);
        ContainerInventoryStore ContainerStore { get; }
        bool SuppressContainerAutoLoot { get; set; }
        string CurrentWeatherState { get; }
        CreativeWorldGenerationPreset GenerationPreset { get; }
        void ConfigureAuthoritySync(MultiplayerChunkAuthoritySync sync);
        void InitializeGeneratedWorld(BlockRegistry registry, WorldGenerationSettings settings, VoxelWorld world, CreativeWorldGenerationPreset generationPreset, IReadOnlyList<StructureContainerLoot> containerLoot = null);
        void InitializeDefaultWorld();
        void RestoreWeatherSyncState(WeatherSyncState sync, bool preserveForNextWorldInitialization = false);
        WeatherSyncState GetWeatherSyncState();
        IVoxelWorldRenderer Renderer { get; }
        ContainerInventoryStore GetOrCreateContainerStore();
        void NotifyContainerLooted(BlockPosition position);
        void FillSaveExtras(WorldSaveExtras extras);
        void RestoreContainerStore(IEnumerable<(BlockPosition position, IEnumerable<(string itemId, int count, int durability)> items)> savedContainers);
        void RestoreSimulationState(WorldSaveData data);
        void RestoreWorldTimeTicks(long totalElapsedTicks);
        void SetTextureSet(string textureSetId);
    }
}