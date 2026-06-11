using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Persistence
{
    [Serializable]
    public sealed class SavedBlockDelta
    {
        public int X;
        public int Y;
        public int Z;
        public string CanonicalId;  // canonical string block ID
    }

    [Serializable]
    public sealed class SavedInventorySlot
    {
        public int SlotIndex;
        public string CanonicalId;  // canonical string item ID
        public int Count;
        public int Durability;      // 0 = no tracking
    }

    [Serializable]
    public sealed class SavedPlayerInventory
    {
        public int SlotCount;
        public int HotbarSlotCount;
        public int SelectedHotbarSlotIndex;
        public SavedInventorySlot[] Slots;
        public SavedInventorySlot[] SurvivalInventorySnapshot;
    }

    [Serializable]
    public sealed class SavedContainerSlot
    {
        public string CanonicalId;
        public int Count;
    }

    [Serializable]
    public sealed class SavedContainer
    {
        public int X;
        public int Y;
        public int Z;
        public SavedContainerSlot[] Slots;
    }

    [Serializable]
    public sealed class SavedPlayerState
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float YawDegrees;
        public int Health;
        public int Hunger;
        public int Thirst;
        public int Stamina;
    }

    // Optional world-sim state saved alongside the core world: full weather-machine position,
    // player presence/vitals, vegetation/farming simulation queues, and timed-station contents.
    public sealed class WorldSaveExtras
    {
        public int WeatherTicksInState;
        public uint WeatherRngState = 1u;
        public SavedPlayerState PlayerState;            // null = no player presence saved
        public VxlwSaplingProgress[] Saplings;
        public VxlwWildRegrowthMarker[] WildRegrowth;
        public VxlwBerrybushRegrowth[] BerrybushRegrowth;
        public VxlwStation[] Stations;
    }

    [Serializable]
    public sealed class WorldSaveData
    {
        public int SchemaVersion;
        public string WorldName;
        public int Width;
        public int Height;
        public int Depth;
        public int ChunkSize;
        public int Seed;
        public SavedBlockDelta[] ChangedBlocks;
        public SavedPlayerInventory PlayerInventory;
        public string WeatherState;
        public long WorldTimeTicks;
        public string GameMode;
        public string WorldPreset;
        public string Difficulty;
        public SavedContainer[] Containers;
        public int WeatherTicksInState;
        public uint WeatherRngState;
        public SavedPlayerState PlayerState;            // null when the save carries none
        public VxlwSaplingProgress[] Saplings;
        public VxlwWildRegrowthMarker[] WildRegrowth;
        public VxlwBerrybushRegrowth[] BerrybushRegrowth;
        public VxlwStation[] Stations;
    }

    public sealed class WorldLoadResult
    {
        WorldLoadResult(bool success, WorldSaveData data, string error)
        {
            Success = success;
            Data = data;
            Error = error;
        }

        public bool Success { get; }
        public WorldSaveData Data { get; }
        public string Error { get; }

        public static WorldLoadResult Loaded(WorldSaveData data)
        {
            return new WorldLoadResult(true, data, string.Empty);
        }

        public static WorldLoadResult Failed(string error)
        {
            return new WorldLoadResult(false, null, error);
        }

        public void ApplyTo(VoxelWorld world, BlockRegistry blockRegistry = null, bool preserveLoadedBlockChanges = false)
        {
            if (!Success)
                throw new InvalidOperationException("Cannot apply a failed save load result.");
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            blockRegistry ??= BlockRegistry.CreateDefault();

            foreach (SavedBlockDelta delta in Data.ChangedBlocks ?? Array.Empty<SavedBlockDelta>())
            {
                // Deltas whose canonical ID no longer resolves are skipped (the app is unreleased;
                // saves carry no legacy fallbacks).
                if (string.IsNullOrEmpty(delta.CanonicalId) ||
                    !blockRegistry.TryGetByCanonicalId(delta.CanonicalId, out BlockDefinition def))
                {
                    continue;
                }

                world.SetBlock(
                    new BlockPosition(delta.X, delta.Y, delta.Z),
                    def.Id,
                    trackChange: preserveLoadedBlockChanges);
            }

            if (!preserveLoadedBlockChanges)
                world.ClearChangedBlocks();
        }

        public Inventory CreateInventory(ItemRegistry itemRegistry = null)
        {
            if (!Success)
                throw new InvalidOperationException("Cannot create an inventory from a failed save load result.");

            itemRegistry ??= ItemRegistry.CreateDefault();
            SavedPlayerInventory savedInventory = Data.PlayerInventory ?? WorldSaveService.CreateEmptyInventoryData();
            var inventory = new Inventory(itemRegistry, savedInventory.SlotCount, savedInventory.HotbarSlotCount);

            foreach (SavedInventorySlot slot in savedInventory.Slots ?? Array.Empty<SavedInventorySlot>())
            {
                if (string.IsNullOrEmpty(slot.CanonicalId))
                    continue;

                var itemId = new ItemId(slot.CanonicalId);
                if (!itemRegistry.TryGet(itemId, out _))
                    continue;

                ItemStack stack = new ItemStack(itemId, slot.Count);
                if (slot.Durability > 0)
                    stack = stack.WithDurability(slot.Durability);
                inventory.SetSlot(slot.SlotIndex, stack);
            }

            return inventory;
        }
    }

    public sealed class WorldSaveService
    {
        readonly ItemRegistry itemRegistry;

        public const int CurrentSchemaVersion = 4;
        public const string SaveFormatVersion = "1.0.0";
        public const float AutoSaveIntervalSeconds = 300f;

        const int RegionSizeChunks = 32;
        const int SectionSize = 16;

        public WorldSaveService(ItemRegistry items = null)
        {
            itemRegistry = items ?? ItemRegistry.CreateDefault();
        }

        public bool ShouldAutoSave(float elapsedSecondsSinceLastSave)
        {
            return elapsedSecondsSinceLastSave >= AutoSaveIntervalSeconds;
        }

        // ── Save ─────────────────────────────────────────────────────────────

        public void Save(string path, string worldName, VoxelWorld world, string weatherState = null, string gameMode = "survival", long worldTimeTicks = 0, IReadOnlyList<SavedContainer> containers = null, string difficulty = null, string worldPreset = "survival_terrain", WorldSaveExtras extras = null)
        {
            Save(path, worldName, world, new Inventory(itemRegistry), selectedHotbarSlotIndex: 0, weatherState: weatherState, gameMode: gameMode, worldTimeTicks: worldTimeTicks, containers: containers, difficulty: difficulty, worldPreset: worldPreset, extras: extras);
        }

        public void Save(string path, string worldName, VoxelWorld world, Inventory inventory, int selectedHotbarSlotIndex = 0, Inventory survivalSnapshot = null, string weatherState = null, string gameMode = "survival", long worldTimeTicks = 0, IReadOnlyList<SavedContainer> containers = null, string difficulty = null, string worldPreset = "survival_terrain", WorldSaveExtras extras = null)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Save path must be non-empty.", nameof(path));
            // Section cell packing strides by SectionSize; a chunk wider than a section would
            // collide local coordinates and silently corrupt the region files.
            if (world.ChunkSize > SectionSize)
                throw new ArgumentException($"World chunk size {world.ChunkSize} exceeds the region section size {SectionSize}.", nameof(world));

            Directory.CreateDirectory(path);
            string sanitizedPath = SanitizeSavePath(path);

            BlockRegistry blockRegistry = BlockRegistry.CreateDefault();
            string blockHash = ComputeBlockRegistryHash(blockRegistry);
            string itemHash = ComputeItemRegistryHash(itemRegistry);
            string now = DateTime.UtcNow.ToString("o");
            string createdAt = GetExistingCreatedAtUtc(path) ?? now;
            worldName = string.IsNullOrWhiteSpace(worldName) ? "Creative World" : worldName;

            var manifest = new VxlwManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                SaveFormatVersion = SaveFormatVersion,
                WorldName = worldName,
                Seed = world.Seed,
                Width = world.Bounds.Width,
                Height = world.Bounds.Height,
                Depth = world.Bounds.Depth,
                ChunkSize = world.ChunkSize,
                WorldPreset = string.IsNullOrWhiteSpace(worldPreset) ? "survival_terrain" : worldPreset,
                GameMode = gameMode,
                Difficulty = difficulty ?? string.Empty,
                CreatedAtUtc = createdAt,
                ModifiedAtUtc = now,
                BlockRegistryHash = blockHash,
                ItemRegistryHash = itemHash
            };

            var dimension = new VxlwDimension
            {
                DimensionId = "main",
                Seed = world.Seed,
                MinY = 0,
                MaxY = world.Bounds.Height - 1,
                ChunkSize = world.ChunkSize
            };

            var environment = new VxlwEnvironment
            {
                WorldTimeTicks = worldTimeTicks,
                WeatherState = !string.IsNullOrEmpty(weatherState) ? weatherState : "CLEAR",
                WeatherTicksInState = extras?.WeatherTicksInState ?? 0,
                WeatherRngState = extras?.WeatherRngState ?? 1u
            };

            var registryManifest = new VxlwRegistryManifest
            {
                BlockRegistryHash = blockHash,
                ItemRegistryHash = itemHash,
                BlockCount = blockRegistry.All.Count,
                ItemCount = itemRegistry.All.Count
            };

            SavedPlayerState playerState = extras?.PlayerState;
            var playerSave = new VxlwPlayerSave
            {
                PlayerId = "local_player",
                GameMode = gameMode,
                SlotCount = inventory.SlotCount,
                HotbarSlotCount = inventory.HotbarSlotCount,
                SelectedHotbarSlotIndex = selectedHotbarSlotIndex,
                Slots = BuildInventorySlots(inventory, selectedHotbarSlotIndex),
                SurvivalInventorySnapshot = survivalSnapshot != null
                    ? BuildInventorySlots(survivalSnapshot, 0)
                    : null,
                HasPlayerState = playerState != null,
                PositionX = playerState?.PositionX ?? 0f,
                PositionY = playerState?.PositionY ?? 0f,
                PositionZ = playerState?.PositionZ ?? 0f,
                YawDegrees = playerState?.YawDegrees ?? 0f,
                Health = playerState?.Health ?? 0,
                Hunger = playerState?.Hunger ?? 0,
                Thirst = playerState?.Thirst ?? 0,
                Stamina = playerState?.Stamina ?? 0
            };

            // Write all files atomically using .tmp → rename. Only the manifest is
            // pretty-printed (developer-inspectable); machine-read files stay compact.
            WriteJsonAtomic(Path.Combine(path, "manifest.json"), manifest, prettyPrint: true);

            string dimDir = Path.Combine(path, "dimensions", "main");
            Directory.CreateDirectory(dimDir);
            WriteJsonAtomic(Path.Combine(dimDir, "dimension.json"), dimension);
            WriteJsonAtomic(Path.Combine(dimDir, "environment.json"), environment);
            WriteContainerFile(dimDir, containers);
            WriteSimulationFile(dimDir, extras);
            WriteStationFile(dimDir, extras?.Stations);

            WriteRegionFiles(path, world, blockRegistry);

            string playersDir = Path.Combine(path, "players");
            Directory.CreateDirectory(playersDir);
            WriteJsonAtomic(Path.Combine(playersDir, "local_player.json"), playerSave);

            string registriesDir = Path.Combine(path, "registries");
            Directory.CreateDirectory(registriesDir);
            WriteJsonAtomic(Path.Combine(registriesDir, "registry-manifest.json"), registryManifest);

            int changedBlockCount = world.GetChangedBlocks().Count();
            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Saved world save file={sanitizedPath} world={worldName} schema={CurrentSchemaVersion} dimensions={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth} changedBlocks={changedBlockCount} inventorySlots={inventory.SlotCount} occupiedInventorySlots={CountOccupiedSlots(inventory)}");
        }

        // ── Load ─────────────────────────────────────────────────────────────

        public WorldLoadResult Load(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    return LoadDirectory(path);

                // Legacy flat-JSON saves (schema v1–v3) are unsupported: the app is unreleased,
                // so old formats fail fast instead of migrating.
                if (File.Exists(path))
                    return FailedLoad(path, "World save format is unsupported: expected a save directory.");

                return FailedLoad(path, $"World save does not exist: {path}", "World save does not exist.");
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                return FailedLoad(
                    path,
                    $"World save is corrupt or unreadable: {exception.Message}",
                    $"World save is corrupt or unreadable: {exception.GetType().Name}");
            }
        }

        // ── Directory format (schema v4) ──────────────────────────────────────

        WorldLoadResult LoadDirectory(string path)
        {
            string manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath))
                return FailedLoad(path, "World save is corrupt: missing manifest.json.");

            string manifestJson = File.ReadAllText(manifestPath);
            if (!HasCompleteTopLevelJsonObject(manifestJson))
                return FailedLoad(path, "World save is corrupt or incomplete.");

            VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(manifestJson);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.WorldName))
                return FailedLoad(path, "World save is corrupt: manifest is invalid.");

            if (manifest.Width <= 0 || manifest.Height <= 0 || manifest.Depth <= 0 || manifest.ChunkSize <= 0)
                return FailedLoad(path, "World save is corrupt: invalid world dimensions.");

            // No migrations: the app is unreleased, so any other schema fails fast.
            if (manifest.SchemaVersion != CurrentSchemaVersion)
                return FailedLoad(path, $"World save schema {manifest.SchemaVersion} is unsupported (expected {CurrentSchemaVersion}).");

            // Region cell packing strides by SectionSize; wider chunks would have collided at
            // save time, so such a manifest can only come from a corrupt or foreign file.
            if (manifest.ChunkSize > SectionSize)
                return FailedLoad(path, $"World save chunk size {manifest.ChunkSize} is unsupported (expected at most {SectionSize}).");

            BlockRegistry blockRegistry = BlockRegistry.CreateDefault();
            string currentBlockHash = ComputeBlockRegistryHash(blockRegistry);
            if (!string.IsNullOrEmpty(manifest.BlockRegistryHash) &&
                manifest.BlockRegistryHash != currentBlockHash)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"World save registry hash mismatch file={SanitizeSavePath(path)} — block registry has changed since this save was created.");
            }

            List<SavedBlockDelta> changedBlocks = LoadRegionFiles(path, manifest, out string regionError);
            if (regionError != null)
                return FailedLoad(path, $"World save is corrupt: {regionError}");

            SavedPlayerInventory playerInventory = LoadPlayerInventory(path, out SavedPlayerState playerState) ?? CreateEmptyInventoryData();
            EnsurePlayerInventoryDefaults(ref playerInventory);

            VxlwEnvironment environment = LoadEnvironmentState(path);
            SavedContainer[] containers = LoadContainerFile(path);
            VxlwSimulationFile simulation = LoadSimulationFile(path);
            VxlwStation[] stations = LoadStationFile(path);

            var data = new WorldSaveData
            {
                SchemaVersion = CurrentSchemaVersion,
                WorldName = manifest.WorldName,
                Width = manifest.Width,
                Height = manifest.Height,
                Depth = manifest.Depth,
                ChunkSize = manifest.ChunkSize,
                Seed = manifest.Seed,
                ChangedBlocks = changedBlocks.ToArray(),
                PlayerInventory = playerInventory,
                WeatherState = environment?.WeatherState,
                WorldTimeTicks = environment?.WorldTimeTicks ?? 0L,
                WeatherTicksInState = environment?.WeatherTicksInState ?? 0,
                WeatherRngState = environment?.WeatherRngState ?? 1u,
                GameMode = !string.IsNullOrEmpty(manifest.GameMode) ? manifest.GameMode : "survival",
                WorldPreset = !string.IsNullOrEmpty(manifest.WorldPreset) ? manifest.WorldPreset : "survival_terrain",
                Difficulty = manifest.Difficulty ?? string.Empty,
                Containers = containers,
                PlayerState = playerState,
                Saplings = simulation?.Saplings,
                WildRegrowth = simulation?.WildRegrowth,
                BerrybushRegrowth = simulation?.BerrybushRegrowth,
                Stations = stations
            };

            if (!IsValidInventory(data.PlayerInventory, out string inventoryError))
                return FailedLoad(path, $"World save is corrupt: {inventoryError}");

            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Loaded world save file={SanitizeSavePath(path)} world={data.WorldName} schema={data.SchemaVersion} dimensions={data.Width}x{data.Height}x{data.Depth} changedBlocks={data.ChangedBlocks.Length} inventorySlots={data.PlayerInventory.SlotCount} occupiedInventorySlots={data.PlayerInventory.Slots?.Length ?? 0}");

            return WorldLoadResult.Loaded(data);
        }

        void WriteRegionFiles(string savePath, VoxelWorld world, BlockRegistry blockRegistry)
        {
            var regionMap = new Dictionary<(int rx, int rz), Dictionary<(int cx, int cz), Dictionary<int, List<(int pos, string canonicalId)>>>>();

            foreach (BlockChange change in world.GetChangedBlocks())
            {
                int chunkX = change.Position.X / world.ChunkSize;
                int chunkZ = change.Position.Z / world.ChunkSize;
                int regionX = chunkX / RegionSizeChunks;
                int regionZ = chunkZ / RegionSizeChunks;
                int sectionY = change.Position.Y / SectionSize;

                int localX = change.Position.X - chunkX * world.ChunkSize;
                int localZ = change.Position.Z - chunkZ * world.ChunkSize;
                int localSectionY = change.Position.Y - sectionY * SectionSize;
                int pos = localX + localZ * SectionSize + localSectionY * SectionSize * SectionSize;

                string canonicalId = blockRegistry.TryGet(change.NewBlock, out BlockDefinition def)
                    ? def.CanonicalId
                    : "air";

                var rKey = (regionX, regionZ);
                if (!regionMap.TryGetValue(rKey, out var chunkMap))
                    regionMap[rKey] = chunkMap = new Dictionary<(int, int), Dictionary<int, List<(int, string)>>>();

                var cKey = (chunkX, chunkZ);
                if (!chunkMap.TryGetValue(cKey, out var sectionMap))
                    chunkMap[cKey] = sectionMap = new Dictionary<int, List<(int, string)>>();

                if (!sectionMap.TryGetValue(sectionY, out var changes))
                    sectionMap[sectionY] = changes = new List<(int, string)>();

                changes.Add((pos, canonicalId));
            }

            string regionsDir = Path.Combine(savePath, "dimensions", "main", "regions");
            // Replace the entire regions directory atomically so stale region files from a
            // previous save with more/different edits are never resurrected on load.
            string regionsDirTmp = regionsDir + ".tmp";
            string regionsDirBak = regionsDir + ".bak";
            Directory.CreateDirectory(regionsDirTmp);

            foreach (var (rKey, chunkMap) in regionMap)
            {
                var chunkDataList = new List<VxlwChunkData>();

                foreach (var (cKey, sectionMap) in chunkMap)
                {
                    var sectionList = new List<VxlwSectionData>();

                    foreach (var (sectionY, changeList) in sectionMap)
                    {
                        var palette = new List<string>();
                        var paletteIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        var positions = new List<int>();
                        var indices = new List<int>();

                        foreach (var (pos, canonicalId) in changeList)
                        {
                            if (!paletteIndex.TryGetValue(canonicalId, out int idx))
                            {
                                idx = palette.Count;
                                palette.Add(canonicalId);
                                paletteIndex[canonicalId] = idx;
                            }
                            positions.Add(pos);
                            indices.Add(idx);
                        }

                        sectionList.Add(new VxlwSectionData
                        {
                            SectionY = sectionY,
                            BlockPalette = palette.ToArray(),
                            ChangePositions = positions.ToArray(),
                            PaletteIndices = indices.ToArray()
                        });
                    }

                    chunkDataList.Add(new VxlwChunkData
                    {
                        ChunkX = cKey.Item1,
                        ChunkZ = cKey.Item2,
                        Sections = sectionList.ToArray()
                    });
                }

                var regionFile = new VxlwRegionFile
                {
                    Format = "vxlr",
                    SaveFormatVersion = SaveFormatVersion,
                    RegionX = rKey.rx,
                    RegionZ = rKey.rz,
                    Chunks = chunkDataList.ToArray()
                };

                string regionPath = Path.Combine(regionsDirTmp, $"r.{rKey.rx}.{rKey.rz}.vxlr");
                WriteJsonAtomic(regionPath, regionFile);
            }

            // Atomically swap regionsDirTmp → regionsDir, removing any stale previous region files.
            if (Directory.Exists(regionsDirBak))
                Directory.Delete(regionsDirBak, recursive: true);

            if (Directory.Exists(regionsDir))
                Directory.Move(regionsDir, regionsDirBak);

            Directory.Move(regionsDirTmp, regionsDir);

            if (Directory.Exists(regionsDirBak))
                Directory.Delete(regionsDirBak, recursive: true);
        }

        List<SavedBlockDelta> LoadRegionFiles(string savePath, VxlwManifest manifest, out string error)
        {
            error = null;
            var deltas = new List<SavedBlockDelta>();
            string regionsDir = Path.Combine(savePath, "dimensions", "main", "regions");
            string regionsDirBak = regionsDir + ".bak";

            // Crash-window recovery: the atomic regions swap moves the live directory to .bak
            // before renaming the fresh one in; dying between the two leaves only the backup.
            if (!Directory.Exists(regionsDir) && Directory.Exists(regionsDirBak))
                Directory.Move(regionsDirBak, regionsDir);

            if (!Directory.Exists(regionsDir))
                return deltas;

            foreach (string regionPath in Directory.GetFiles(regionsDir, "r.*.*.vxlr"))
            {
                string regionJson = File.ReadAllText(regionPath);
                VxlwRegionFile regionFile = string.IsNullOrWhiteSpace(regionJson)
                    ? null
                    : JsonUtility.FromJson<VxlwRegionFile>(regionJson);

                // A region file that exists but cannot be parsed is silent data loss, not
                // skippable noise — surface a controlled failure instead.
                if (regionFile?.Chunks == null)
                {
                    error = $"unreadable region file '{Path.GetFileName(regionPath)}'";
                    return deltas;
                }

                foreach (VxlwChunkData chunk in regionFile.Chunks)
                {
                    if (chunk?.Sections == null)
                        continue;

                    foreach (VxlwSectionData section in chunk.Sections)
                    {
                        if (section?.BlockPalette == null || section.ChangePositions == null)
                            continue;

                        int count = Math.Min(section.ChangePositions.Length, section.PaletteIndices?.Length ?? 0);
                        for (int i = 0; i < count; i++)
                        {
                            int paletteIdx = section.PaletteIndices[i];
                            if (paletteIdx < 0 || paletteIdx >= section.BlockPalette.Length)
                                continue;

                            string canonicalId = section.BlockPalette[paletteIdx];
                            int pos = section.ChangePositions[i];

                            int localSectionY = pos / (SectionSize * SectionSize);
                            int remainder = pos % (SectionSize * SectionSize);
                            int localZ = remainder / SectionSize;
                            int localX = remainder % SectionSize;

                            int worldX = chunk.ChunkX * manifest.ChunkSize + localX;
                            int worldY = section.SectionY * SectionSize + localSectionY;
                            int worldZ = chunk.ChunkZ * manifest.ChunkSize + localZ;

                            // A delta outside the manifest's world bounds would later throw an
                            // uncontrolled exception in WorldLoadResult.ApplyTo — fail here.
                            if (worldX < 0 || worldX >= manifest.Width ||
                                worldY < 0 || worldY >= manifest.Height ||
                                worldZ < 0 || worldZ >= manifest.Depth)
                            {
                                error = $"block delta is outside world bounds at ({worldX}, {worldY}, {worldZ})";
                                return deltas;
                            }

                            deltas.Add(new SavedBlockDelta
                            {
                                X = worldX,
                                Y = worldY,
                                Z = worldZ,
                                CanonicalId = canonicalId
                            });
                        }
                    }
                }
            }

            return deltas;
        }

        SavedPlayerInventory LoadPlayerInventory(string savePath, out SavedPlayerState playerState)
        {
            playerState = null;

            string playerPath = Path.Combine(savePath, "players", "local_player.json");
            if (!File.Exists(playerPath))
                return null;

            string json = File.ReadAllText(playerPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            VxlwPlayerSave playerSave = JsonUtility.FromJson<VxlwPlayerSave>(json);
            if (playerSave == null)
                return null;

            if (playerSave.HasPlayerState)
            {
                playerState = new SavedPlayerState
                {
                    PositionX = playerSave.PositionX,
                    PositionY = playerSave.PositionY,
                    PositionZ = playerSave.PositionZ,
                    YawDegrees = playerSave.YawDegrees,
                    Health = playerSave.Health,
                    Hunger = playerSave.Hunger,
                    Thirst = playerSave.Thirst,
                    Stamina = playerSave.Stamina
                };
            }

            return new SavedPlayerInventory
            {
                SlotCount = playerSave.SlotCount,
                HotbarSlotCount = playerSave.HotbarSlotCount,
                SelectedHotbarSlotIndex = playerSave.SelectedHotbarSlotIndex,
                Slots = playerSave.Slots ?? Array.Empty<SavedInventorySlot>(),
                SurvivalInventorySnapshot = playerSave.SurvivalInventorySnapshot
            };
        }

        static VxlwEnvironment LoadEnvironmentState(string savePath)
        {
            string envPath = Path.Combine(savePath, "dimensions", "main", "environment.json");
            if (!File.Exists(envPath))
                return null;

            string json = File.ReadAllText(envPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonUtility.FromJson<VxlwEnvironment>(json);
        }

        // Writes the vegetation/farming simulation queues, or deletes the file when there is
        // nothing pending — a stale file from a previous save must not resurrect dead queues.
        static void WriteSimulationFile(string dimDir, WorldSaveExtras extras)
        {
            string path = Path.Combine(dimDir, "simulation.json");
            bool hasContent =
                (extras?.Saplings?.Length ?? 0) > 0 ||
                (extras?.WildRegrowth?.Length ?? 0) > 0 ||
                (extras?.BerrybushRegrowth?.Length ?? 0) > 0;

            if (!hasContent)
            {
                File.Delete(path);
                return;
            }

            WriteJsonAtomic(path, new VxlwSimulationFile
            {
                Format = "blockiverse-simulation",
                SaveFormatVersion = SaveFormatVersion,
                Saplings = extras.Saplings ?? Array.Empty<VxlwSaplingProgress>(),
                WildRegrowth = extras.WildRegrowth ?? Array.Empty<VxlwWildRegrowthMarker>(),
                BerrybushRegrowth = extras.BerrybushRegrowth ?? Array.Empty<VxlwBerrybushRegrowth>()
            });
        }

        static VxlwSimulationFile LoadSimulationFile(string savePath)
        {
            string path = Path.Combine(savePath, "dimensions", "main", "simulation.json");
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonUtility.FromJson<VxlwSimulationFile>(json);
        }

        // Writes the timed-station runtime contents, or deletes the file when no stations exist.
        static void WriteStationFile(string dimDir, VxlwStation[] stations)
        {
            string path = Path.Combine(dimDir, "stations.json");
            if (stations == null || stations.Length == 0)
            {
                File.Delete(path);
                return;
            }

            WriteJsonAtomic(path, new VxlwStationFile
            {
                Format = "blockiverse-stations",
                SaveFormatVersion = SaveFormatVersion,
                Stations = stations
            });
        }

        static VxlwStation[] LoadStationFile(string savePath)
        {
            string path = Path.Combine(savePath, "dimensions", "main", "stations.json");
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            VxlwStationFile file = JsonUtility.FromJson<VxlwStationFile>(json);
            return file?.Stations != null && file.Stations.Length > 0 ? file.Stations : null;
        }

        void WriteContainerFile(string dimDir, IReadOnlyList<SavedContainer> containers)
        {
            // Containers are optional and additive — only write the file when there is content, so old
            // saves and worlds without containers remain byte-identical and validation is unaffected.
            if (containers == null || containers.Count == 0)
                return;

            var file = new VxlwContainerFile
            {
                Format = "blockiverse-containers",
                SaveFormatVersion = SaveFormatVersion,
                Containers = new VxlwContainer[containers.Count]
            };

            for (int i = 0; i < containers.Count; i++)
            {
                SavedContainer c = containers[i];
                SavedContainerSlot[] slots = c.Slots ?? Array.Empty<SavedContainerSlot>();
                var outSlots = new VxlwContainerSlot[slots.Length];
                for (int s = 0; s < slots.Length; s++)
                    outSlots[s] = new VxlwContainerSlot { CanonicalId = slots[s].CanonicalId, Count = slots[s].Count };

                file.Containers[i] = new VxlwContainer { X = c.X, Y = c.Y, Z = c.Z, Slots = outSlots };
            }

            WriteJsonAtomic(Path.Combine(dimDir, "containers.json"), file);
        }

        static SavedContainer[] LoadContainerFile(string savePath)
        {
            string containerPath = Path.Combine(savePath, "dimensions", "main", "containers.json");
            if (!File.Exists(containerPath))
                return null;

            string json = File.ReadAllText(containerPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            VxlwContainerFile file = JsonUtility.FromJson<VxlwContainerFile>(json);
            if (file?.Containers == null || file.Containers.Length == 0)
                return null;

            var result = new SavedContainer[file.Containers.Length];
            for (int i = 0; i < file.Containers.Length; i++)
            {
                VxlwContainer c = file.Containers[i];
                VxlwContainerSlot[] slots = c.Slots ?? Array.Empty<VxlwContainerSlot>();
                var outSlots = new SavedContainerSlot[slots.Length];
                for (int s = 0; s < slots.Length; s++)
                    outSlots[s] = new SavedContainerSlot { CanonicalId = slots[s].CanonicalId, Count = slots[s].Count };

                result[i] = new SavedContainer { X = c.X, Y = c.Y, Z = c.Z, Slots = outSlots };
            }

            return result;
        }

        // ── Save management (World Details §6.5: rename/duplicate/delete) ─────

        // Moves the save directory and rewrites the manifest's world name. Fails (false) when the
        // destination exists or the source is not a loadable save directory.
        public static bool TryRenameSave(string path, string destinationPath, string newWorldName)
        {
            if (string.IsNullOrWhiteSpace(newWorldName) ||
                !Directory.Exists(path) ||
                Directory.Exists(destinationPath) || File.Exists(destinationPath))
            {
                return false;
            }

            try
            {
                Directory.Move(path, destinationPath);
                return TryRewriteManifestWorldName(destinationPath, newWorldName);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to rename save file={SanitizeSavePath(path)} exception={exception.GetType().Name}");
                return false;
            }
        }

        // Copies the save directory recursively and rewrites the copy's world name.
        public static bool TryDuplicateSave(string sourcePath, string destinationPath, string newWorldName)
        {
            if (string.IsNullOrWhiteSpace(newWorldName) ||
                !Directory.Exists(sourcePath) ||
                Directory.Exists(destinationPath) || File.Exists(destinationPath))
            {
                return false;
            }

            try
            {
                CopyDirectoryRecursive(sourcePath, destinationPath);
                return TryRewriteManifestWorldName(destinationPath, newWorldName);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to duplicate save file={SanitizeSavePath(sourcePath)} exception={exception.GetType().Name}");
                return false;
            }
        }

        public static bool TryDeleteSave(string path)
        {
            if (!Directory.Exists(path))
                return false;

            try
            {
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to delete save file={SanitizeSavePath(path)} exception={exception.GetType().Name}");
                return false;
            }
        }

        static bool TryRewriteManifestWorldName(string savePath, string newWorldName)
        {
            string manifestPath = Path.Combine(savePath, "manifest.json");
            if (!File.Exists(manifestPath))
                return false;

            VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
                return false;

            manifest.WorldName = newWorldName;
            manifest.ModifiedAtUtc = DateTime.UtcNow.ToString("o");
            WriteJsonAtomic(manifestPath, manifest);
            return true;
        }

        static void CopyDirectoryRecursive(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectoryRecursive(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        // ── Save enumeration (menu listings) ──────────────────────────────────

        // Lightweight save-slot summary: manifest fields plus the saved world time, read without
        // touching region files. Only saves the current build can actually load are returned.
        public sealed class WorldSaveInfo
        {
            public string Path;
            public VxlwManifest Manifest;
            public long WorldTimeTicks;
        }

        public static IReadOnlyList<WorldSaveInfo> EnumerateSaves(string savesRootPath)
        {
            var saves = new List<WorldSaveInfo>();

            if (string.IsNullOrWhiteSpace(savesRootPath) || !Directory.Exists(savesRootPath))
                return saves;

            foreach (string saveDir in Directory.GetDirectories(savesRootPath))
            {
                WorldSaveInfo info = TryReadSaveInfo(saveDir);
                if (info != null)
                    saves.Add(info);
            }

            return saves;
        }

        public static WorldSaveInfo TryReadSaveInfo(string path)
        {
            try
            {
                string manifestPath = Path.Combine(path, "manifest.json");
                if (!File.Exists(manifestPath))
                    return null;

                string manifestJson = File.ReadAllText(manifestPath);
                if (!HasCompleteTopLevelJsonObject(manifestJson))
                    return null;

                VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(manifestJson);
                if (manifest == null ||
                    string.IsNullOrWhiteSpace(manifest.WorldName) ||
                    manifest.SchemaVersion != CurrentSchemaVersion ||
                    manifest.Width <= 0 || manifest.Height <= 0 || manifest.Depth <= 0 || manifest.ChunkSize <= 0)
                {
                    return null;
                }

                VxlwEnvironment environment = LoadEnvironmentState(path);
                return new WorldSaveInfo { Path = path, Manifest = manifest, WorldTimeTicks = environment?.WorldTimeTicks ?? 0L };
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        // ── Registry hashes ───────────────────────────────────────────────────

        public static string ComputeBlockRegistryHash(BlockRegistry registry)
        {
            string content = string.Join("|", registry.All.Select(d => d.CanonicalId).OrderBy(id => id));
            return ComputeMd5Hex(content);
        }

        public static string ComputeItemRegistryHash(ItemRegistry registry)
        {
            string content = string.Join("|", registry.All.Select(d => d.Id.Value).OrderBy(id => id));
            return ComputeMd5Hex(content);
        }

        static string ComputeMd5Hex(string content)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        SavedInventorySlot[] BuildInventorySlots(Inventory inventory, int selectedHotbarSlotIndex)
        {
            if (!IsValidSelectedHotbarSlotIndex(selectedHotbarSlotIndex, inventory.HotbarSlotCount))
                throw new ArgumentOutOfRangeException(nameof(selectedHotbarSlotIndex), "Selected hotbar slot must fit inside the inventory hotbar.");

            var slots = new List<SavedInventorySlot>();
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                ItemStack stack = inventory.GetSlot(i);
                if (stack.IsEmpty)
                    continue;

                if (!itemRegistry.TryGet(stack.ItemId, out ItemDefinition definition))
                    throw new InvalidOperationException($"Inventory item is not registered: {stack.ItemId}.");

                if (stack.Count > definition.MaxStackSize)
                    throw new InvalidOperationException($"Inventory stack count {stack.Count} exceeds max stack size {definition.MaxStackSize} for {stack.ItemId}.");

                slots.Add(new SavedInventorySlot
                {
                    SlotIndex = i,
                    CanonicalId = stack.ItemId.Value,
                    Count = stack.Count,
                    Durability = stack.Durability
                });
            }
            return slots.ToArray();
        }

        static bool HasCompleteTopLevelJsonObject(string json)
        {
            int index = 0;
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;

            if (index >= json.Length || json[index] != '{')
                return false;

            bool inString = false;
            bool escaped = false;
            bool completedTopLevelObject = false;
            int objectDepth = 0;
            int arrayDepth = 0;

            for (; index < json.Length; index++)
            {
                char character = json[index];

                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (character == '\\')
                        escaped = true;
                    else if (character == '"')
                        inString = false;
                    continue;
                }

                if (character == '"')
                {
                    if (completedTopLevelObject) return false;
                    inString = true;
                    continue;
                }

                switch (character)
                {
                    case '{':
                        if (completedTopLevelObject) return false;
                        objectDepth++;
                        break;
                    case '}':
                        objectDepth--;
                        if (objectDepth < 0) return false;
                        if (objectDepth == 0 && arrayDepth == 0) completedTopLevelObject = true;
                        break;
                    case '[':
                        if (completedTopLevelObject) return false;
                        arrayDepth++;
                        break;
                    case ']':
                        arrayDepth--;
                        if (arrayDepth < 0) return false;
                        break;
                    default:
                        if (completedTopLevelObject && !char.IsWhiteSpace(character)) return false;
                        break;
                }
            }

            return completedTopLevelObject && objectDepth == 0 && arrayDepth == 0 && !inString && !escaped;
        }

        static void EnsurePlayerInventoryDefaults(ref SavedPlayerInventory inventory)
        {
            if (inventory == null || IsMissingInventoryData(inventory))
                inventory = CreateEmptyInventoryData();

            if (inventory.Slots == null)
                inventory.Slots = Array.Empty<SavedInventorySlot>();
        }

        static bool IsMissingInventoryData(SavedPlayerInventory inventory)
        {
            return inventory.SlotCount == 0 &&
                   inventory.HotbarSlotCount == 0 &&
                   inventory.SelectedHotbarSlotIndex == 0 &&
                   (inventory.Slots == null || inventory.Slots.Length == 0);
        }

        internal static SavedPlayerInventory CreateEmptyInventoryData()
        {
            return new SavedPlayerInventory
            {
                SlotCount = Inventory.DefaultSlotCount,
                HotbarSlotCount = Inventory.DefaultHotbarSlotCount,
                SelectedHotbarSlotIndex = 0,
                Slots = Array.Empty<SavedInventorySlot>()
            };
        }

        bool IsValidInventory(SavedPlayerInventory inventory, out string error)
        {
            if (inventory == null) { error = "missing player inventory"; return false; }

            if (inventory.SlotCount <= 0 || inventory.SlotCount > Inventory.MaxSlotCount)
            { error = "player inventory slot count is invalid"; return false; }

            if (inventory.HotbarSlotCount < 0 || inventory.HotbarSlotCount > inventory.SlotCount)
            { error = "player inventory hotbar count is invalid"; return false; }

            if (!IsValidSelectedHotbarSlotIndex(inventory.SelectedHotbarSlotIndex, inventory.HotbarSlotCount))
            { error = "player inventory selected hotbar slot is invalid"; return false; }

            if (inventory.Slots == null)
                inventory.Slots = Array.Empty<SavedInventorySlot>();

            var occupiedSlots = new HashSet<int>();
            foreach (SavedInventorySlot slot in inventory.Slots)
            {
                if (slot == null) { error = "missing player inventory slot"; return false; }
                if (slot.SlotIndex < 0 || slot.SlotIndex >= inventory.SlotCount) { error = "player inventory slot index is outside inventory bounds"; return false; }
                if (!occupiedSlots.Add(slot.SlotIndex)) { error = "player inventory has duplicate slot indexes"; return false; }
                if (slot.Count <= 0) { error = "player inventory stack count is invalid"; return false; }
                if (string.IsNullOrEmpty(slot.CanonicalId)) { error = "player inventory slot is missing canonical item id"; return false; }

                var itemId = new ItemId(slot.CanonicalId);
                if (!itemRegistry.TryGet(itemId, out ItemDefinition definition)) { error = $"player inventory item id is not registered: {slot.CanonicalId}"; return false; }
                if (slot.Count > definition.MaxStackSize) { error = "player inventory stack count exceeds item max stack size"; return false; }
            }

            // The survival snapshot (stashed inventory while in creative mode) feeds the mode
            // switch on load — it must satisfy the same shape rules as the live slots.
            if (inventory.SurvivalInventorySnapshot != null)
            {
                var snapshotSlots = new HashSet<int>();
                foreach (SavedInventorySlot slot in inventory.SurvivalInventorySnapshot)
                {
                    if (slot == null) { error = "missing survival snapshot slot"; return false; }
                    if (slot.SlotIndex < 0 || slot.SlotIndex >= inventory.SlotCount) { error = "survival snapshot slot index is outside inventory bounds"; return false; }
                    if (!snapshotSlots.Add(slot.SlotIndex)) { error = "survival snapshot has duplicate slot indexes"; return false; }
                    if (slot.Count <= 0) { error = "survival snapshot stack count is invalid"; return false; }
                    if (string.IsNullOrEmpty(slot.CanonicalId)) { error = "survival snapshot slot is missing canonical item id"; return false; }

                    var snapshotItemId = new ItemId(slot.CanonicalId);
                    if (!itemRegistry.TryGet(snapshotItemId, out ItemDefinition snapshotDefinition)) { error = $"survival snapshot item id is not registered: {slot.CanonicalId}"; return false; }
                    if (slot.Count > snapshotDefinition.MaxStackSize) { error = "survival snapshot stack count exceeds item max stack size"; return false; }
                }
            }

            error = string.Empty;
            return true;
        }

        static bool IsValidSelectedHotbarSlotIndex(int selectedHotbarSlotIndex, int hotbarSlotCount)
        {
            if (hotbarSlotCount == 0)
                return selectedHotbarSlotIndex == 0;
            return selectedHotbarSlotIndex >= 0 && selectedHotbarSlotIndex < hotbarSlotCount;
        }

        static void WriteJsonAtomic(string path, object data, bool prettyPrint = false)
        {
            string json = JsonUtility.ToJson(data, prettyPrint);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            ReplaceWithTempFile(tempPath, path);
        }

        static void ReplaceWithTempFile(string tempPath, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            string backupPath = path + ".bak";
            try
            {
                File.Replace(tempPath, path, backupPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException || exception is UnauthorizedAccessException)
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(path, backupPath);
                try
                {
                    File.Move(tempPath, path);
                }
                catch
                {
                    // A failed swap must not strand the live file at .bak — put the original back.
                    File.Move(backupPath, path);
                    throw;
                }
                File.Delete(backupPath);
            }
        }

        static string GetExistingCreatedAtUtc(string dirPath)
        {
            string manifestPath = Path.Combine(dirPath, "manifest.json");
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                VxlwManifest existing = JsonUtility.FromJson<VxlwManifest>(File.ReadAllText(manifestPath));
                return string.IsNullOrWhiteSpace(existing?.CreatedAtUtc) ? null : existing.CreatedAtUtc;
            }
            catch
            {
                return null;
            }
        }

        WorldLoadResult FailedLoad(string path, string error, string logReason = null)
        {
            BlockiverseLog.Warning(
                BlockiverseLogCategory.Persistence,
                $"Failed to load world save file={SanitizeSavePath(path)} reason={logReason ?? error}");
            return WorldLoadResult.Failed(error);
        }

        static string SanitizeSavePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "<empty>";

            // For directories, use the directory name; for files, use the file name
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name;
        }

        static int CountOccupiedSlots(Inventory inventory)
        {
            int count = 0;
            for (int i = 0; i < inventory.SlotCount; i++)
                if (!inventory.GetSlot(i).IsEmpty) count++;
            return count;
        }
    }
}
