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
            var settings = new WorldGenerationSettings(width: 16, height: 32, depth: 16, chunkSize: 16, seed: 2202, groundHeight: 2);
            var preset = new FlatCreativeWorldPreset(registry, settings);
            VoxelWorld world = preset.Generate();
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.LumenQuartzCluster);
            world.SetBlock(new BlockPosition(3, 2, 2), BlockRegistry.Glowwick);

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

                Assert.That(loadedWorld.GetBlock(new BlockPosition(2, 2, 2)), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
                Assert.That(loadedWorld.GetBlock(new BlockPosition(3, 2, 2)), Is.EqualTo(BlockRegistry.Glowwick));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveProducesCanonicalDirectoryLayout()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Graystone);

            try
            {
                new WorldSaveService(new WorldSaveMigrationRegistry()).Save(path, "layout-test", world);

                Assert.That(Directory.Exists(path), Is.True, "Expected .vxlworld directory.");
                Assert.That(File.Exists(Path.Combine(path, "manifest.json")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(path, "dimensions", "main")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "dimension.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "environment.json")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(path, "dimensions", "main", "regions")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "players", "local_player.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "registries", "registry-manifest.json")), Is.True);

                // Verify at least one region file exists (changed block was placed)
                string[] regionFiles = Directory.GetFiles(Path.Combine(path, "dimensions", "main", "regions"), "r.*.*.vxlr");
                Assert.That(regionFiles, Is.Not.Empty, "Expected at least one region file for changed blocks.");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void RegistryHashIsStoredInManifestAndMatchesCurrentRegistry()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService(new WorldSaveMigrationRegistry()).Save(path, "hash-test", world);

                string manifestJson = File.ReadAllText(Path.Combine(path, "manifest.json"));
                VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(manifestJson);

                BlockRegistry blockRegistry = BlockRegistry.CreateDefault();
                ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
                string expectedBlockHash = WorldSaveService.ComputeBlockRegistryHash(blockRegistry);
                string expectedItemHash = WorldSaveService.ComputeItemRegistryHash(itemRegistry);

                Assert.That(manifest.BlockRegistryHash, Is.EqualTo(expectedBlockHash));
                Assert.That(manifest.ItemRegistryHash, Is.EqualTo(expectedItemHash));
                Assert.That(manifest.BlockRegistryHash, Is.Not.Empty);
                Assert.That(manifest.ItemRegistryHash, Is.Not.Empty);
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ShouldAutoSaveReturnsTrueAfterIntervalAndFalseBeforeIt()
        {
            var service = new WorldSaveService(new WorldSaveMigrationRegistry());

            Assert.That(service.ShouldAutoSave(0f), Is.False);
            Assert.That(service.ShouldAutoSave(WorldSaveService.AutoSaveIntervalSeconds - 1f), Is.False);
            Assert.That(service.ShouldAutoSave(WorldSaveService.AutoSaveIntervalSeconds), Is.True);
            Assert.That(service.ShouldAutoSave(WorldSaveService.AutoSaveIntervalSeconds + 60f), Is.True);
        }

        [Test]
        public void SaveLogsSanitizedWorldSummary()
        {
            string path = CreateTempSavePath();
            var sink = new CapturingLogSink();
            VoxelWorld world = CreateDefaultWorld();
            var inventory = new Inventory(ItemRegistry.CreateDefault());
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 4));
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.LumenQuartzCluster);

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
                Assert.That(entry.Message, Does.Contain($"schema={WorldSaveService.CurrentSchemaVersion}"));
                Assert.That(entry.Message, Does.Contain("dimensions=32x16x32"));
                Assert.That(entry.Message, Does.Contain("changedBlocks=1"));
                Assert.That(entry.Message, Does.Contain("inventorySlots=44"));
                Assert.That(entry.Message, Does.Contain("occupiedInventorySlots=1"));
                Assert.That(entry.Message, Does.Contain(new DirectoryInfo(path).Name));
                Assert.That(entry.Message, Does.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ApplyingLoadedDeltasDoesNotRecordPersistenceDeltasButEmitsRenderEvents()
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
                    new SavedBlockDelta { X = 1, Y = 1, Z = 1, CanonicalId = "graystone" }
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

            Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(world.GetChangedBlocks(), Is.Empty);
            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void SaveThenLoadReproducesPlayerInventory()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            inventory.SetSlot(5, new ItemStack(ItemId.ReedwoodDelver, 1));
            inventory.SetSlot(8, new ItemStack(ItemId.FieldBandage, 2));

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
                Assert.That(loadedInventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 12)));
                Assert.That(loadedInventory.GetSlot(5), Is.EqualTo(new ItemStack(ItemId.ReedwoodDelver, 1)));
                Assert.That(loadedInventory.GetSlot(8), Is.EqualTo(new ItemStack(ItemId.FieldBandage, 2)));
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
        public void SaveOverwritesExistingDirectoryAndPreservesCreatedAt()
        {
            string path = CreateTempSavePath();
            BlockRegistry registry = BlockRegistry.CreateDefault();
            VoxelWorld firstWorld = new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
            VoxelWorld secondWorld = new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
            secondWorld.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.LumenQuartzCluster);

            try
            {
                var service = new WorldSaveService(new WorldSaveMigrationRegistry());
                service.Save(path, "first", firstWorld);

                string createdAt = JsonUtility.FromJson<VxlwManifest>(
                    File.ReadAllText(Path.Combine(path, "manifest.json"))).CreatedAtUtc;

                service.Save(path, "second", secondWorld);

                Assert.That(Directory.Exists(path), Is.True);
                Assert.That(service.Load(path).Data.WorldName, Is.EqualTo("second"));

                string createdAtAfter = JsonUtility.FromJson<VxlwManifest>(
                    File.ReadAllText(Path.Combine(path, "manifest.json"))).CreatedAtUtc;
                Assert.That(createdAtAfter, Is.EqualTo(createdAt), "CreatedAtUtc should be preserved across re-saves.");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void NoTmpFilesRemainInsideDirectoryAfterSave()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService(new WorldSaveMigrationRegistry()).Save(path, "no-tmp-test", world);

                string[] tmpFiles = Directory.GetFiles(path, "*.tmp", SearchOption.AllDirectories);
                Assert.That(tmpFiles, Is.Empty, "Expected no .tmp files inside the save directory after save.");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void LegacyVersionOneFlatJsonMigratesOnLoad()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-legacy-{System.Guid.NewGuid():N}.json");

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
                File.WriteAllText(flatPath, JsonUtility.ToJson(versionOneData, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.PlayerInventory, Is.Not.Null);
                Assert.That(result.Data.PlayerInventory.Slots, Is.Empty);
                Assert.That(result.CreateInventory(ItemRegistry.CreateDefault()).SlotCount, Is.EqualTo(Inventory.DefaultSlotCount));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void LegacyFlatJsonMigratesWithCustomRegistry()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-legacy-{System.Guid.NewGuid():N}.json");

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
                File.WriteAllText(flatPath, JsonUtility.ToJson(oldData, prettyPrint: true));

                var migrations = new WorldSaveMigrationRegistry();
                migrations.Register(0, data =>
                {
                    data.SchemaVersion = WorldSaveService.CurrentSchemaVersion;
                    data.WorldName = "migrated";
                    return data;
                });

                WorldLoadResult result = new WorldSaveService(migrations).Load(flatPath);

                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.SchemaVersion, Is.EqualTo(WorldSaveService.CurrentSchemaVersion));
                Assert.That(result.Data.WorldName, Is.EqualTo("migrated"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void InvalidInventorySlotReturnsControlledFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-bad-{System.Guid.NewGuid():N}.json");

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
                            new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.ReedwoodDelver.Value, Count = 2 }
                        }
                    }
                };
                File.WriteAllText(flatPath, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("inventory"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void OversizedInventorySlotCountReturnsControlledFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-bad-{System.Guid.NewGuid():N}.json");

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
                File.WriteAllText(flatPath, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("slot count"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void CorruptedSaveReturnsControlledFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-corrupt-{System.Guid.NewGuid():N}.json");

            try
            {
                File.WriteAllText(flatPath, "{ definitely not valid json");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("corrupt"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void CorruptedSaveLogsSanitizedFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-corrupt-{System.Guid.NewGuid():N}.json");
            var sink = new CapturingLogSink();

            try
            {
                BlockiverseLog.SetSinkForTesting(sink);
                BlockiverseLog.DevelopmentInfoEnabled = true;
                File.WriteAllText(flatPath, "{ definitely not valid json");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Persistence &&
                    log.Level == LogType.Warning &&
                    log.Message.Contains("Failed to load world save"));
                Assert.That(entry.Message, Does.Contain(Path.GetFileName(flatPath)));
                Assert.That(entry.Message, Does.Not.Contain(Path.GetDirectoryName(flatPath)));
                Assert.That(entry.Message, Does.Contain("corrupt or incomplete"));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void PartiallyWrittenFlatJsonReturnsControlledFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-partial-{System.Guid.NewGuid():N}.json");

            try
            {
                File.WriteAllText(
                    flatPath,
                    "{\"SchemaVersion\":2,\"WorldName\":\"partial\",\"Width\":4,\"Height\":4,\"Depth\":4,\"ChunkSize\":16,\"Seed\":1,\"ChangedBlocks\":[],\"PlayerInventory\":{\"SlotCount\":24,\"HotbarSlotCount\":6,\"SelectedHotbarSlotIndex\":0,\"Slots\":[]}");

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("incomplete"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void MissingManifestReturnsControlledFailure()
        {
            string path = CreateTempSavePath();

            try
            {
                Directory.CreateDirectory(path);
                // No manifest.json written — directory exists but is incomplete

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("manifest"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void OutOfBoundsSaveDeltaInLegacyFlatJsonReturnsControlledFailure()
        {
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-bad-{System.Guid.NewGuid():N}.json");

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
                        new SavedBlockDelta { X = 8, Y = 1, Z = 1, BlockId = BlockRegistry.LooseLoam.Value }
                    }
                };
                File.WriteAllText(flatPath, JsonUtility.ToJson(data, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("corrupt"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        static string CreateTempSavePath()
        {
            return Path.Combine(Path.GetTempPath(), $"blockiverse-save-{System.Guid.NewGuid():N}.vxlworld");
        }

        static VoxelWorld CreateDefaultWorld()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            return new FlatCreativeWorldPreset(registry, WorldGenerationSettings.CreateDefaultCreative()).Generate();
        }

        static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
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
