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
        public int BlockId;         // legacy (schema v1-v2): integer block ID
        public string CanonicalId;  // canonical (schema v3+): string block ID
    }

    [Serializable]
    public sealed class SavedInventorySlot
    {
        public int SlotIndex;
        public int ItemId;          // legacy (schema v1-v2): integer item ID
        public string CanonicalId;  // canonical (schema v3+): string item ID
        public int Count;
    }

    [Serializable]
    public sealed class SavedPlayerInventory
    {
        public int SlotCount;
        public int HotbarSlotCount;
        public int SelectedHotbarSlotIndex;
        public SavedInventorySlot[] Slots;
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
                BlockId blockId;
                if (!string.IsNullOrEmpty(delta.CanonicalId) &&
                    blockRegistry.TryGetByCanonicalId(delta.CanonicalId, out BlockDefinition def))
                {
                    blockId = def.Id;
                }
                else
                {
                    blockId = new BlockId(delta.BlockId);
                }

                world.SetBlock(
                    new BlockPosition(delta.X, delta.Y, delta.Z),
                    blockId,
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

                string canonicalId = WorldSaveService.LegacyItemCanonicalIdAliases.TryGetValue(slot.CanonicalId, out string aliased)
                    ? aliased
                    : slot.CanonicalId;
                var itemId = new ItemId(canonicalId);
                if (!itemRegistry.TryGet(itemId, out _))
                    continue;

                inventory.SetSlot(slot.SlotIndex, new ItemStack(itemId, slot.Count));
            }

            return inventory;
        }
    }

    public sealed class WorldSaveMigrationRegistry
    {
        readonly Dictionary<int, Func<WorldSaveData, WorldSaveData>> migrations = new();

        public void Register(int fromSchemaVersion, Func<WorldSaveData, WorldSaveData> migration)
        {
            if (migration == null)
                throw new ArgumentNullException(nameof(migration));

            migrations[fromSchemaVersion] = migration;
        }

        public bool TryMigrateToCurrent(WorldSaveData data, int currentSchemaVersion, out WorldSaveData migrated, out string error)
        {
            migrated = data;
            error = string.Empty;
            var visitedSchemaVersions = new HashSet<int>();

            while (migrated.SchemaVersion != currentSchemaVersion)
            {
                if (!visitedSchemaVersions.Add(migrated.SchemaVersion))
                {
                    error = $"World save migration loop detected at schema {migrated.SchemaVersion}.";
                    return false;
                }

                if (!migrations.TryGetValue(migrated.SchemaVersion, out Func<WorldSaveData, WorldSaveData> migration))
                {
                    error = $"No migration registered for world save schema {migrated.SchemaVersion}.";
                    return false;
                }

                migrated = migration(migrated);

                if (migrated == null)
                {
                    error = "World save migration returned no data.";
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class WorldSaveService
    {
        readonly WorldSaveMigrationRegistry migrationRegistry;
        readonly ItemRegistry itemRegistry;

        public const int CurrentSchemaVersion = 4;
        public const string SaveFormatVersion = "1.0.0";
        public const float AutoSaveIntervalSeconds = 300f;

        const int RegionSizeChunks = 32;
        const int SectionSize = 16;

        public WorldSaveService(WorldSaveMigrationRegistry migrations, ItemRegistry items = null)
        {
            migrationRegistry = migrations ?? throw new ArgumentNullException(nameof(migrations));
            itemRegistry = items ?? ItemRegistry.CreateDefault();
        }

        public bool ShouldAutoSave(float elapsedSecondsSinceLastSave)
        {
            return elapsedSecondsSinceLastSave >= AutoSaveIntervalSeconds;
        }

        // ── Save ─────────────────────────────────────────────────────────────

        public void Save(string path, string worldName, VoxelWorld world)
        {
            Save(path, worldName, world, new Inventory(itemRegistry), selectedHotbarSlotIndex: 0);
        }

        public void Save(string path, string worldName, VoxelWorld world, Inventory inventory, int selectedHotbarSlotIndex = 0)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Save path must be non-empty.", nameof(path));

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
                WorldPreset = "survival_terrain",
                GameMode = "creative",
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
                WorldTimeTicks = 0,
                WeatherState = "CLEAR"
            };

            var registryManifest = new VxlwRegistryManifest
            {
                BlockRegistryHash = blockHash,
                ItemRegistryHash = itemHash,
                BlockCount = blockRegistry.All.Count,
                ItemCount = itemRegistry.All.Count
            };

            var playerSave = new VxlwPlayerSave
            {
                PlayerId = "local_player",
                GameMode = "creative",
                SlotCount = inventory.SlotCount,
                HotbarSlotCount = inventory.HotbarSlotCount,
                SelectedHotbarSlotIndex = selectedHotbarSlotIndex,
                Slots = BuildInventorySlots(inventory, selectedHotbarSlotIndex)
            };

            // Write all files atomically using .tmp → rename
            WriteJsonAtomic(Path.Combine(path, "manifest.json"), manifest);

            string dimDir = Path.Combine(path, "dimensions", "main");
            Directory.CreateDirectory(dimDir);
            WriteJsonAtomic(Path.Combine(dimDir, "dimension.json"), dimension);
            WriteJsonAtomic(Path.Combine(dimDir, "environment.json"), environment);

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

                if (File.Exists(path))
                    return LoadFlatJson(path);

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

            BlockRegistry blockRegistry = BlockRegistry.CreateDefault();
            string currentBlockHash = ComputeBlockRegistryHash(blockRegistry);
            if (!string.IsNullOrEmpty(manifest.BlockRegistryHash) &&
                manifest.BlockRegistryHash != currentBlockHash)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"World save registry hash mismatch file={SanitizeSavePath(path)} — block registry has changed since this save was created.");
            }

            List<SavedBlockDelta> changedBlocks = LoadRegionFiles(path, manifest);

            SavedPlayerInventory playerInventory = LoadPlayerInventory(path) ?? CreateEmptyInventoryData();
            EnsurePlayerInventoryDefaults(ref playerInventory);

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
                PlayerInventory = playerInventory
            };

            if (!IsValidInventory(data.PlayerInventory, out string inventoryError))
                return FailedLoad(path, $"World save is corrupt: {inventoryError}");

            int loadedSchemaVersion = manifest.SchemaVersion;
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

        List<SavedBlockDelta> LoadRegionFiles(string savePath, VxlwManifest manifest)
        {
            var deltas = new List<SavedBlockDelta>();
            string regionsDir = Path.Combine(savePath, "dimensions", "main", "regions");

            if (!Directory.Exists(regionsDir))
                return deltas;

            BlockRegistry blockRegistry = BlockRegistry.CreateDefault();

            foreach (string regionPath in Directory.GetFiles(regionsDir, "r.*.*.vxlr"))
            {
                string regionJson = File.ReadAllText(regionPath);
                if (string.IsNullOrWhiteSpace(regionJson))
                    continue;

                VxlwRegionFile regionFile = JsonUtility.FromJson<VxlwRegionFile>(regionJson);
                if (regionFile?.Chunks == null)
                    continue;

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

                            deltas.Add(new SavedBlockDelta
                            {
                                X = worldX,
                                Y = worldY,
                                Z = worldZ,
                                BlockId = 0,
                                CanonicalId = canonicalId
                            });
                        }
                    }
                }
            }

            return deltas;
        }

        SavedPlayerInventory LoadPlayerInventory(string savePath)
        {
            string playerPath = Path.Combine(savePath, "players", "local_player.json");
            if (!File.Exists(playerPath))
                return null;

            string json = File.ReadAllText(playerPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            VxlwPlayerSave playerSave = JsonUtility.FromJson<VxlwPlayerSave>(json);
            if (playerSave == null)
                return null;

            return new SavedPlayerInventory
            {
                SlotCount = playerSave.SlotCount,
                HotbarSlotCount = playerSave.HotbarSlotCount,
                SelectedHotbarSlotIndex = playerSave.SelectedHotbarSlotIndex,
                Slots = playerSave.Slots ?? Array.Empty<SavedInventorySlot>()
            };
        }

        // ── Legacy flat JSON format (schema v1-v3) ────────────────────────────

        WorldLoadResult LoadFlatJson(string path)
        {
            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json) || !HasCompleteTopLevelJsonObject(json))
                return FailedLoad(path, "World save is corrupt or incomplete.");

            WorldSaveData data = JsonUtility.FromJson<WorldSaveData>(json);

            if (!IsValidFlatData(data, out string validationError))
                return FailedLoad(path, $"World save is corrupt: {validationError}");

            int loadedSchemaVersion = data.SchemaVersion;
            data = ApplyBuiltInMigrations(data, SanitizeSavePath(path));

            if (data.SchemaVersion != CurrentSchemaVersion &&
                !migrationRegistry.TryMigrateToCurrent(data, CurrentSchemaVersion, out data, out string migrationError))
            {
                return FailedLoad(path, migrationError);
            }

            if (loadedSchemaVersion != data.SchemaVersion)
            {
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Migrated world save file={SanitizeSavePath(path)} fromSchema={loadedSchemaVersion} toSchema={data.SchemaVersion}");
            }

            EnsurePlayerInventoryDefaults(ref data.PlayerInventory);

            if (!IsValidFlatData(data, out validationError))
                return FailedLoad(path, $"World save is corrupt: {validationError}");

            if (!IsValidInventory(data.PlayerInventory, out validationError))
                return FailedLoad(path, $"World save is corrupt: {validationError}");

            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Loaded world save file={SanitizeSavePath(path)} world={data.WorldName} schema={data.SchemaVersion} dimensions={data.Width}x{data.Height}x{data.Depth} changedBlocks={data.ChangedBlocks?.Length ?? 0} inventorySlots={data.PlayerInventory.SlotCount} occupiedInventorySlots={data.PlayerInventory.Slots?.Length ?? 0}");

            return WorldLoadResult.Loaded(data);
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

        static readonly Dictionary<int, string> LegacyBlockIdToCanonical = new()
        {
            { 0, "air" },
            { 1, "meadow_turf" },
            { 2, "loose_loam" },
            { 3, "graystone" },
            { 4, "branchwood_log" },
            { 5, "leafmoss" },
            { 6, "lumen_quartz_cluster" },
            { 7, "embercoal_seam" },
            { 8, "rosycopper_bloom" },
            { 9, "rustcore_ore" },
            { 10, "build_table" },
            { 11, "glowwick" },
            { 12, "storage_crate" }
        };

        internal static readonly Dictionary<string, string> LegacyItemCanonicalIdAliases = new()
        {
            { "lumen_quartz", "lumen_crystal" },
        };

        static readonly Dictionary<int, string> LegacyItemIdToCanonical = new()
        {
            { 1, "meadow_turf" },
            { 2, "loose_loam" },
            { 3, "graystone" },
            { 4, "branchwood_log" },
            { 5, "leafmoss" },
            { 6, "lumen_crystal" },
            { 7, "embercoal" },
            { 8, "raw_rosycopper" },
            { 9, "raw_rustcore" },
            { 10, "build_table" },
            { 11, "glowwick" },
            { 12, "storage_crate" },
            { 100, "reedwood_feller" },
            { 101, "reedwood_mallet" },
            { 102, "reedwood_delver" },
            { 200, "field_bandage" }
        };

        static WorldSaveData ApplyBuiltInMigrations(WorldSaveData data, string saveName)
        {
            if (data == null)
                return data;

            if (data.SchemaVersion == 1)
            {
                data.SchemaVersion = 2;
                data.PlayerInventory = CreateEmptyInventoryData();
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Applied built-in world save migration file={saveName} fromSchema=1 toSchema=2");
            }

            if (data.SchemaVersion == 2)
            {
                foreach (SavedBlockDelta delta in data.ChangedBlocks ?? Array.Empty<SavedBlockDelta>())
                {
                    if (string.IsNullOrEmpty(delta.CanonicalId) &&
                        LegacyBlockIdToCanonical.TryGetValue(delta.BlockId, out string canonical))
                    {
                        delta.CanonicalId = canonical;
                    }
                }

                if (data.PlayerInventory?.Slots != null)
                {
                    foreach (SavedInventorySlot slot in data.PlayerInventory.Slots)
                    {
                        if (string.IsNullOrEmpty(slot.CanonicalId) &&
                            LegacyItemIdToCanonical.TryGetValue(slot.ItemId, out string canonical))
                        {
                            slot.CanonicalId = canonical;
                        }
                    }
                }

                data.SchemaVersion = 3;
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Applied built-in world save migration file={saveName} fromSchema=2 toSchema=3");
            }

            if (data.SchemaVersion == 3)
            {
                data.SchemaVersion = CurrentSchemaVersion;
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Applied built-in world save migration file={saveName} fromSchema=3 toSchema={CurrentSchemaVersion}");
            }

            return data;
        }

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
                    ItemId = 0,
                    CanonicalId = stack.ItemId.Value,
                    Count = stack.Count
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

        bool IsValidFlatData(WorldSaveData data, out string error)
        {
            if (data == null) { error = "missing root data"; return false; }
            if (data.SchemaVersion < 0) { error = "invalid schema version"; return false; }
            if (data.Width <= 0 || data.Height <= 0 || data.Depth <= 0 || data.ChunkSize <= 0) { error = "invalid world dimensions"; return false; }

            if (data.ChangedBlocks == null)
                data.ChangedBlocks = Array.Empty<SavedBlockDelta>();

            foreach (SavedBlockDelta delta in data.ChangedBlocks)
            {
                if (delta == null) { error = "missing changed block delta"; return false; }

                if (!(delta.X >= 0 && delta.X < data.Width && delta.Y >= 0 && delta.Y < data.Height && delta.Z >= 0 && delta.Z < data.Depth))
                { error = "changed block delta is outside world bounds"; return false; }

                if (delta.BlockId < 0) { error = "changed block delta has an invalid block id"; return false; }
            }

            error = string.Empty;
            return true;
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

                string resolvedId = LegacyItemCanonicalIdAliases.TryGetValue(slot.CanonicalId, out string aliasedId) ? aliasedId : slot.CanonicalId;
                var itemId = new ItemId(resolvedId);
                if (!itemRegistry.TryGet(itemId, out ItemDefinition definition)) { error = $"player inventory item id is not registered: {slot.CanonicalId}"; return false; }
                if (slot.Count > definition.MaxStackSize) { error = "player inventory stack count exceeds item max stack size"; return false; }
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

        static void WriteJsonAtomic(string path, object data)
        {
            string json = JsonUtility.ToJson(data, prettyPrint: true);
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
                File.Move(tempPath, path);
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
