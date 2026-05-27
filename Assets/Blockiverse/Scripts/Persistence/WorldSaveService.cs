using System;
using System.Collections.Generic;
using System.IO;
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
        public int BlockId;
    }

    [Serializable]
    public sealed class SavedInventorySlot
    {
        public int SlotIndex;
        public int ItemId;
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

        public void ApplyTo(VoxelWorld world)
        {
            if (!Success)
                throw new InvalidOperationException("Cannot apply a failed save load result.");
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            foreach (SavedBlockDelta delta in Data.ChangedBlocks ?? Array.Empty<SavedBlockDelta>())
            {
                world.SetBlock(
                    new BlockPosition(delta.X, delta.Y, delta.Z),
                    new BlockId(delta.BlockId),
                    trackChange: false);
            }

            world.ClearChangedBlocks();
        }

        public Inventory CreateInventory(ItemRegistry itemRegistry = null)
        {
            if (!Success)
                throw new InvalidOperationException("Cannot create an inventory from a failed save load result.");

            SavedPlayerInventory savedInventory = Data.PlayerInventory ?? WorldSaveService.CreateEmptyInventoryData();
            var inventory = new Inventory(itemRegistry, savedInventory.SlotCount, savedInventory.HotbarSlotCount);

            foreach (SavedInventorySlot slot in savedInventory.Slots ?? Array.Empty<SavedInventorySlot>())
                inventory.SetSlot(slot.SlotIndex, new ItemStack((ItemId)slot.ItemId, slot.Count));

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

        public const int CurrentSchemaVersion = 2;

        public WorldSaveService(WorldSaveMigrationRegistry migrations, ItemRegistry items = null)
        {
            migrationRegistry = migrations ?? throw new ArgumentNullException(nameof(migrations));
            itemRegistry = items ?? ItemRegistry.CreateDefault();
        }

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

            WorldSaveData data = CreateSaveData(worldName, world, inventory, selectedHotbarSlotIndex);
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(data, prettyPrint: true));
            ReplaceWithTempFile(tempPath, path);

            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Saved world save file={SanitizeSavePath(path)} world={data.WorldName} schema={data.SchemaVersion} dimensions={data.Width}x{data.Height}x{data.Depth} changedBlocks={ChangedBlockCount(data)} inventorySlots={data.PlayerInventory.SlotCount} occupiedInventorySlots={OccupiedInventorySlotCount(data.PlayerInventory)}");
        }

        public WorldLoadResult Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return FailedLoad(path, $"World save does not exist: {path}", "World save does not exist.");

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json) || !HasCompleteTopLevelJsonObject(json))
                    return FailedLoad(path, "World save is corrupt or incomplete.");

                WorldSaveData data = JsonUtility.FromJson<WorldSaveData>(json);

                if (!IsValid(data, validateInventory: false, out string validationError))
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

                EnsurePlayerInventoryDefaults(data);

                if (!IsValid(data, validateInventory: true, out validationError))
                    return FailedLoad(path, $"World save is corrupt: {validationError}");

                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Loaded world save file={SanitizeSavePath(path)} world={data.WorldName} schema={data.SchemaVersion} dimensions={data.Width}x{data.Height}x{data.Depth} changedBlocks={ChangedBlockCount(data)} inventorySlots={data.PlayerInventory.SlotCount} occupiedInventorySlots={OccupiedInventorySlotCount(data.PlayerInventory)}");
                return WorldLoadResult.Loaded(data);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                return FailedLoad(
                    path,
                    $"World save is corrupt or unreadable: {exception.Message}",
                    $"World save is corrupt or unreadable: {exception.GetType().Name}");
            }
        }

        WorldSaveData CreateSaveData(string worldName, VoxelWorld world, Inventory inventory, int selectedHotbarSlotIndex)
        {
            var deltas = new List<SavedBlockDelta>();

            foreach (BlockChange change in world.GetChangedBlocks())
            {
                deltas.Add(new SavedBlockDelta
                {
                    X = change.Position.X,
                    Y = change.Position.Y,
                    Z = change.Position.Z,
                    BlockId = change.NewBlock.Value
                });
            }

            return new WorldSaveData
            {
                SchemaVersion = CurrentSchemaVersion,
                WorldName = string.IsNullOrWhiteSpace(worldName) ? "Creative World" : worldName,
                Width = world.Bounds.Width,
                Height = world.Bounds.Height,
                Depth = world.Bounds.Depth,
                ChunkSize = world.ChunkSize,
                Seed = world.Seed,
                ChangedBlocks = deltas.ToArray(),
                PlayerInventory = CreateSavedInventory(inventory, selectedHotbarSlotIndex)
            };
        }

        SavedPlayerInventory CreateSavedInventory(Inventory inventory, int selectedHotbarSlotIndex)
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
                    ItemId = (int)stack.ItemId,
                    Count = stack.Count
                });
            }

            return new SavedPlayerInventory
            {
                SlotCount = inventory.SlotCount,
                HotbarSlotCount = inventory.HotbarSlotCount,
                SelectedHotbarSlotIndex = selectedHotbarSlotIndex,
                Slots = slots.ToArray()
            };
        }

        static WorldSaveData ApplyBuiltInMigrations(WorldSaveData data, string saveName)
        {
            if (data != null && data.SchemaVersion == 1)
            {
                data.SchemaVersion = CurrentSchemaVersion;
                data.PlayerInventory = CreateEmptyInventoryData();
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Applied built-in world save migration file={saveName} fromSchema=1 toSchema={CurrentSchemaVersion}");
            }

            return data;
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
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    if (completedTopLevelObject)
                        return false;

                    inString = true;
                    continue;
                }

                switch (character)
                {
                    case '{':
                        if (completedTopLevelObject)
                            return false;

                        objectDepth++;
                        break;

                    case '}':
                        objectDepth--;
                        if (objectDepth < 0)
                            return false;

                        if (objectDepth == 0 && arrayDepth == 0)
                            completedTopLevelObject = true;
                        break;

                    case '[':
                        if (completedTopLevelObject)
                            return false;

                        arrayDepth++;
                        break;

                    case ']':
                        arrayDepth--;
                        if (arrayDepth < 0)
                            return false;
                        break;

                    default:
                        if (completedTopLevelObject && !char.IsWhiteSpace(character))
                            return false;
                        break;
                }
            }

            return completedTopLevelObject &&
                   objectDepth == 0 &&
                   arrayDepth == 0 &&
                   !inString &&
                   !escaped;
        }

        static void EnsurePlayerInventoryDefaults(WorldSaveData data)
        {
            if (data.PlayerInventory == null || IsMissingInventoryData(data.PlayerInventory))
                data.PlayerInventory = CreateEmptyInventoryData();

            if (data.PlayerInventory.Slots == null)
                data.PlayerInventory.Slots = Array.Empty<SavedInventorySlot>();
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

        bool IsValid(WorldSaveData data, bool validateInventory, out string error)
        {
            if (data == null)
            {
                error = "missing root data";
                return false;
            }

            if (data.SchemaVersion < 0)
            {
                error = "invalid schema version";
                return false;
            }

            if (data.Width <= 0 || data.Height <= 0 || data.Depth <= 0 || data.ChunkSize <= 0)
            {
                error = "invalid world dimensions";
                return false;
            }

            if (data.ChangedBlocks == null)
                data.ChangedBlocks = Array.Empty<SavedBlockDelta>();

            foreach (SavedBlockDelta delta in data.ChangedBlocks)
            {
                if (delta == null)
                {
                    error = "missing changed block delta";
                    return false;
                }

                bool deltaInBounds = delta.X >= 0 && delta.X < data.Width &&
                                     delta.Y >= 0 && delta.Y < data.Height &&
                                     delta.Z >= 0 && delta.Z < data.Depth;

                if (!deltaInBounds)
                {
                    error = "changed block delta is outside world bounds";
                    return false;
                }

                if (delta.BlockId < 0)
                {
                    error = "changed block delta has an invalid block id";
                    return false;
                }
            }

            if (validateInventory && !IsValidInventory(data.PlayerInventory, out error))
                return false;

            error = string.Empty;
            return true;
        }

        bool IsValidInventory(SavedPlayerInventory inventory, out string error)
        {
            if (inventory == null)
            {
                error = "missing player inventory";
                return false;
            }

            if (inventory.SlotCount <= 0 || inventory.SlotCount > Inventory.MaxSlotCount)
            {
                error = "player inventory slot count is invalid";
                return false;
            }

            if (inventory.HotbarSlotCount < 0 || inventory.HotbarSlotCount > inventory.SlotCount)
            {
                error = "player inventory hotbar count is invalid";
                return false;
            }

            if (!IsValidSelectedHotbarSlotIndex(inventory.SelectedHotbarSlotIndex, inventory.HotbarSlotCount))
            {
                error = "player inventory selected hotbar slot is invalid";
                return false;
            }

            if (inventory.Slots == null)
                inventory.Slots = Array.Empty<SavedInventorySlot>();

            var occupiedSlots = new HashSet<int>();
            foreach (SavedInventorySlot slot in inventory.Slots)
            {
                if (slot == null)
                {
                    error = "missing player inventory slot";
                    return false;
                }

                if (slot.SlotIndex < 0 || slot.SlotIndex >= inventory.SlotCount)
                {
                    error = "player inventory slot index is outside inventory bounds";
                    return false;
                }

                if (!occupiedSlots.Add(slot.SlotIndex))
                {
                    error = "player inventory has duplicate slot indexes";
                    return false;
                }

                if (slot.Count <= 0)
                {
                    error = "player inventory stack count is invalid";
                    return false;
                }

                ItemId itemId = (ItemId)slot.ItemId;
                if (itemId == ItemId.None || !itemRegistry.TryGet(itemId, out ItemDefinition definition))
                {
                    error = "player inventory item id is invalid";
                    return false;
                }

                if (slot.Count > definition.MaxStackSize)
                {
                    error = "player inventory stack count exceeds item max stack size";
                    return false;
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

                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException || exception is UnauthorizedAccessException)
            {
                File.Delete(path);
                File.Move(tempPath, path);
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

            string fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? "<unnamed>" : fileName;
        }

        static int ChangedBlockCount(WorldSaveData data)
        {
            return data.ChangedBlocks?.Length ?? 0;
        }

        static int OccupiedInventorySlotCount(SavedPlayerInventory inventory)
        {
            return inventory?.Slots?.Length ?? 0;
        }
    }
}
