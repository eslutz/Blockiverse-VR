using System.IO;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WorldSaveEditModeTests
    {
        [Test]
        public void SaveThenLoadReproducesMetadataAndChangedBlockDeltas()
        {
            string path = CreateTempSavePath();
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(width: 16, height: 8, depth: 16, chunkSize: 16, seed: 2202, groundHeight: 2);
            var preset = new FlatCreativeWorldPreset(registry, settings);
            VoxelWorld world = preset.Generate();
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Clearstone);
            world.SetBlock(new BlockPosition(3, 2, 2), BlockRegistry.Torchbud);

            try
            {
                var service = new WorldSaveService(new WorldSaveMigrationRegistry());
                service.Save(path, "editmode-test", world);

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.WorldName, Is.EqualTo("editmode-test"));
                Assert.That(result.Data.Seed, Is.EqualTo(2202));
                Assert.That(result.Data.ChangedBlocks, Has.Length.EqualTo(2));

                VoxelWorld loadedWorld = preset.Generate();
                result.ApplyTo(loadedWorld);

                Assert.That(loadedWorld.GetBlock(new BlockPosition(2, 2, 2)), Is.EqualTo(BlockRegistry.Clearstone));
                Assert.That(loadedWorld.GetBlock(new BlockPosition(3, 2, 2)), Is.EqualTo(BlockRegistry.Torchbud));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveLogsSanitizedWorldSummary()
        {
            string path = CreateTempSavePath();
            var sink = new CapturingLogSink();
            VoxelWorld world = CreateDefaultWorld();
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 4));
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Clearstone);

            try
            {
                BlockiverseLog.SetSinkForTesting(sink);
                BlockiverseLog.DevelopmentInfoEnabled = true;

                new WorldSaveService(new WorldSaveMigrationRegistry()).Save(path, "summary-test", world, inventory);

                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Persistence &&
                    log.Level == LogType.Log &&
                    log.Message.Contains("Saved world"));
                Assert.That(entry.Message, Does.Contain("world=summary-test"));
                Assert.That(entry.Message, Does.Contain("schema=2"));
                Assert.That(entry.Message, Does.Contain("dimensions=32x16x32"));
                Assert.That(entry.Message, Does.Contain("changedBlocks=1"));
                Assert.That(entry.Message, Does.Contain("inventorySlots=24"));
                Assert.That(entry.Message, Does.Contain("occupiedInventorySlots=1"));
                Assert.That(entry.Message, Does.Contain(Path.GetFileName(path)));
                Assert.That(entry.Message, Does.Not.Contain(Path.GetDirectoryName(path)));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ApplyingLoadedDeltasDoesNotEmitBlockChangeEvents()
        {
            var data = new WorldSaveData
            {
                SchemaVersion = WorldSaveService.CurrentSchemaVersion,
                WorldName = "loaded",
                Width = 4,
                Height = 4,
                Depth = 4,
                ChunkSize = 16,
                Seed = 1,
                ChangedBlocks = new[]
                {
                    new SavedBlockDelta { X = 1, Y = 1, Z = 1, BlockId = BlockRegistry.Slate.Value }
                },
                PlayerInventory = new SavedPlayerInventory
                {
                    SlotCount = Inventory.DefaultSlotCount,
                    HotbarSlotCount = Inventory.DefaultHotbarSlotCount,
                    SelectedHotbarSlotIndex = 0,
                    Slots = new SavedInventorySlot[0]
                }
            };
            var world = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 16, seed: 1);
            int eventCount = 0;
            world.BlockChanged += _ => eventCount++;

            WorldLoadResult.Loaded(data).ApplyTo(world);

            Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Slate));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(eventCount, Is.Zero);
        }

        [Test]
        public void SaveThenLoadReproducesPlayerInventory()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 12));
            inventory.SetSlot(5, new ItemStack(ItemId.Pick, 1));
            inventory.SetSlot(8, new ItemStack(ItemId.RecoveryWrap, 2));

            try
            {
                var service = new WorldSaveService(new WorldSaveMigrationRegistry());
                service.Save(path, "inventory-test", world, inventory, selectedHotbarSlotIndex: 5);

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.PlayerInventory, Is.Not.Null);
                Assert.That(result.Data.PlayerInventory.SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
                Assert.That(result.Data.PlayerInventory.HotbarSlotCount, Is.EqualTo(Inventory.DefaultHotbarSlotCount));
                Assert.That(result.Data.PlayerInventory.SelectedHotbarSlotIndex, Is.EqualTo(5));

                Inventory loadedInventory = result.CreateInventory(itemRegistry);

                Assert.That(loadedInventory.SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
                Assert.That(loadedInventory.HotbarSlotCount, Is.EqualTo(Inventory.DefaultHotbarSlotCount));
                Assert.That(loadedInventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Timber, 12)));
                Assert.That(loadedInventory.GetSlot(5), Is.EqualTo(new ItemStack(ItemId.Pick, 1)));
                Assert.That(loadedInventory.GetSlot(8), Is.EqualTo(new ItemStack(ItemId.RecoveryWrap, 2)));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void WorldOnlySaveWritesEmptyDefaultPlayerInventory()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                var service = new WorldSaveService(new WorldSaveMigrationRegistry());
                service.Save(path, "world-only", world);

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.PlayerInventory, Is.Not.Null);
                Assert.That(result.Data.PlayerInventory.SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
                Assert.That(result.Data.PlayerInventory.HotbarSlotCount, Is.EqualTo(Inventory.DefaultHotbarSlotCount));
                Assert.That(result.Data.PlayerInventory.SelectedHotbarSlotIndex, Is.Zero);
                Assert.That(result.Data.PlayerInventory.Slots, Is.Empty);
                Assert.That(result.CreateInventory(ItemRegistry.CreateDefault()).GetSlot(0).IsEmpty, Is.True);
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveUsesTemporaryFileAndLeavesNoTempFileAfterReplacement()
        {
            string path = CreateTempSavePath();
            BlockRegistry registry = BlockRegistry.CreateDefault();
            VoxelWorld firstWorld = new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
            VoxelWorld secondWorld = new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
            secondWorld.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Clearstone);

            try
            {
                var service = new WorldSaveService(new WorldSaveMigrationRegistry());
                service.Save(path, "first", firstWorld);
                service.Save(path, "second", secondWorld);

                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".tmp"), Is.False);
                Assert.That(service.Load(path).Data.WorldName, Is.EqualTo("second"));
            }
            finally
            {
                DeleteIfExists(path);
                DeleteIfExists(path + ".tmp");
            }
        }

        [Test]
        public void VersionOneSaveMigratesToEmptyPlayerInventory()
        {
            string path = CreateTempSavePath();

            try
            {
                var versionOneData = new WorldSaveData
                {
                    SchemaVersion = 1,
                    WorldName = "v1",
                    Width = 4,
                    Height = 4,
                    Depth = 4,
                    ChunkSize = 16,
                    Seed = 99,
                    ChangedBlocks = new SavedBlockDelta[0]
                };
                File.WriteAllText(path, JsonUtility.ToJson(versionOneData, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.PlayerInventory, Is.Not.Null);
                Assert.That(result.Data.PlayerInventory.Slots, Is.Empty);
                Assert.That(result.CreateInventory(ItemRegistry.CreateDefault()).SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void InvalidInventorySlotReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                var data = new WorldSaveData
                {
                    SchemaVersion = WorldSaveService.CurrentSchemaVersion,
                    WorldName = "bad-inventory",
                    Width = 4,
                    Height = 4,
                    Depth = 4,
                    ChunkSize = 16,
                    Seed = 1,
                    ChangedBlocks = new SavedBlockDelta[0],
                    PlayerInventory = new SavedPlayerInventory
                    {
                        SlotCount = 1,
                        HotbarSlotCount = 1,
                        SelectedHotbarSlotIndex = 0,
                        Slots = new[]
                        {
                            new SavedInventorySlot { SlotIndex = 0, ItemId = (int)ItemId.Pick, Count = 2 }
                        }
                    }
                };
                File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("inventory"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void OversizedInventorySlotCountReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                var data = new WorldSaveData
                {
                    SchemaVersion = WorldSaveService.CurrentSchemaVersion,
                    WorldName = "oversized-inventory",
                    Width = 4,
                    Height = 4,
                    Depth = 4,
                    ChunkSize = 16,
                    Seed = 1,
                    ChangedBlocks = new SavedBlockDelta[0],
                    PlayerInventory = new SavedPlayerInventory
                    {
                        SlotCount = 1_000_000,
                        HotbarSlotCount = 1,
                        SelectedHotbarSlotIndex = 0,
                        Slots = new SavedInventorySlot[0]
                    }
                };
                File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("slot count"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void OldSchemaMigratesToCurrentSchema()
        {
            string path = CreateTempSavePath();

            try
            {
                var oldData = new WorldSaveData
                {
                    SchemaVersion = 0,
                    WorldName = "old",
                    Width = 4,
                    Height = 4,
                    Depth = 4,
                    ChunkSize = 16,
                    Seed = 99,
                    ChangedBlocks = new SavedBlockDelta[0]
                };
                File.WriteAllText(path, JsonUtility.ToJson(oldData, prettyPrint: true));

                var migrations = new WorldSaveMigrationRegistry();
                migrations.Register(0, data =>
                {
                    data.SchemaVersion = WorldSaveService.CurrentSchemaVersion;
                    data.WorldName = "migrated";
                    return data;
                });

                WorldLoadResult result = new WorldSaveService(migrations).Load(path);

                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.WorldName, Is.EqualTo("migrated"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void CorruptedSaveReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                File.WriteAllText(path, "{ definitely not valid json");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("corrupt"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void CorruptedSaveLogsSanitizedFailure()
        {
            string path = CreateTempSavePath();
            var sink = new CapturingLogSink();

            try
            {
                BlockiverseLog.SetSinkForTesting(sink);
                BlockiverseLog.DevelopmentInfoEnabled = true;
                File.WriteAllText(path, "{ definitely not valid json");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Persistence &&
                    log.Level == LogType.Warning &&
                    log.Message.Contains("Failed to load world save"));
                Assert.That(entry.Message, Does.Contain(Path.GetFileName(path)));
                Assert.That(entry.Message, Does.Not.Contain(Path.GetDirectoryName(path)));
                Assert.That(entry.Message, Does.Contain("corrupt or incomplete"));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                DeleteIfExists(path);
            }
        }

        [Test]
        public void PartiallyWrittenSaveEndingInNestedObjectReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                File.WriteAllText(
                    path,
                    "{\"SchemaVersion\":2,\"WorldName\":\"partial\",\"Width\":4,\"Height\":4,\"Depth\":4,\"ChunkSize\":16,\"Seed\":1,\"ChangedBlocks\":[],\"PlayerInventory\":{\"SlotCount\":24,\"HotbarSlotCount\":6,\"SelectedHotbarSlotIndex\":0,\"Slots\":[]}");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("incomplete"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void OutOfBoundsSaveDeltaReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                var data = new WorldSaveData
                {
                    SchemaVersion = WorldSaveService.CurrentSchemaVersion,
                    WorldName = "bad-delta",
                    Width = 4,
                    Height = 4,
                    Depth = 4,
                    ChunkSize = 16,
                    Seed = 1,
                    ChangedBlocks = new[]
                    {
                        new SavedBlockDelta { X = 8, Y = 1, Z = 1, BlockId = BlockRegistry.Loam.Value }
                    }
                };
                File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("corrupt"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        static string CreateTempSavePath()
        {
            return Path.Combine(Path.GetTempPath(), $"blockiverse-save-{System.Guid.NewGuid():N}.json");
        }

        static VoxelWorld CreateDefaultWorld()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            return new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        sealed class CapturingLogSink : IBlockiverseLogSink
        {
            public readonly System.Collections.Generic.List<BlockiverseLogEntry> Entries = new();

            public void Log(BlockiverseLogEntry entry)
            {
                Entries.Add(entry);
            }
        }
    }
}
