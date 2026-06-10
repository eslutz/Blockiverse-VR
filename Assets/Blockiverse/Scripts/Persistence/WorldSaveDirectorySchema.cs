using System;
using Blockiverse.Survival;

namespace Blockiverse.Persistence
{
    [Serializable]
    public sealed class VxlwManifest
    {
        public int SchemaVersion;
        public string SaveFormatVersion;
        public string WorldName;
        public int Seed;
        public int Width;
        public int Height;
        public int Depth;
        public int ChunkSize;
        public string WorldPreset;
        public string GameMode;
        public string Difficulty;
        public string CreatedAtUtc;
        public string ModifiedAtUtc;
        public string BlockRegistryHash;
        public string ItemRegistryHash;
    }

    [Serializable]
    public sealed class VxlwDimension
    {
        public string DimensionId;
        public int Seed;
        public int MinY;
        public int MaxY;
        public int ChunkSize;
    }

    [Serializable]
    public sealed class VxlwEnvironment
    {
        public long WorldTimeTicks;
        public string WeatherState;
    }

    [Serializable]
    public sealed class VxlwPlayerSave
    {
        public string PlayerId;
        public string GameMode;
        public int SlotCount;
        public int HotbarSlotCount;
        public int SelectedHotbarSlotIndex;
        public SavedInventorySlot[] Slots;
        public SavedInventorySlot[] SurvivalInventorySnapshot;
    }

    [Serializable]
    public sealed class VxlwContainerFile
    {
        public string Format;
        public string SaveFormatVersion;
        public VxlwContainer[] Containers;
    }

    [Serializable]
    public sealed class VxlwContainer
    {
        public int X;
        public int Y;
        public int Z;
        public VxlwContainerSlot[] Slots;
    }

    [Serializable]
    public sealed class VxlwContainerSlot
    {
        public string CanonicalId;
        public int Count;
    }

    [Serializable]
    public sealed class VxlwRegistryManifest
    {
        public string BlockRegistryHash;
        public string ItemRegistryHash;
        public int BlockCount;
        public int ItemCount;
    }

    [Serializable]
    public sealed class VxlwRegionFile
    {
        public string Format;
        public string SaveFormatVersion;
        public int RegionX;
        public int RegionZ;
        public VxlwChunkData[] Chunks;
    }

    [Serializable]
    public sealed class VxlwChunkData
    {
        public int ChunkX;
        public int ChunkZ;
        public VxlwSectionData[] Sections;
    }

    [Serializable]
    public sealed class VxlwSectionData
    {
        public int SectionY;
        public string[] BlockPalette;
        public int[] ChangePositions;
        public int[] PaletteIndices;
    }
}
