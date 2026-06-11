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
                var service = new WorldSaveService();
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
        public void SaveThenLoadPreservesDifficultyAndWorldPreset()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                var service = new WorldSaveService();
                service.Save(path, "settings-test", world, difficulty: "hard", worldPreset: "flat_builder");

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.Difficulty, Is.EqualTo("hard"));
                Assert.That(result.Data.WorldPreset, Is.EqualTo("flat_builder"));
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
                new WorldSaveService().Save(path, "layout-test", world);

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
                new WorldSaveService().Save(path, "hash-test", world);

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
            var service = new WorldSaveService();

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

                new WorldSaveService().Save(path, "summary-test", world, inventory);

                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Persistence &&
                    log.Level == LogType.Log &&
                    log.Message.Contains("Saved world"));
                Assert.That(entry.Message, Does.Contain("world=summary-test"));
                Assert.That(entry.Message, Does.Contain($"schema={WorldSaveService.CurrentSchemaVersion}"));
                Assert.That(entry.Message, Does.Contain($"dimensions={world.Bounds.Width}x{world.Bounds.Height}x{world.Bounds.Depth}"));
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
        public void UnresolvedCanonicalIdDeltaIsSkippedOnApply()
        {
            // No legacy aliases or integer fallbacks exist (the app is unreleased); a delta whose
            // canonical id no longer resolves is skipped rather than remapped.
            var data = new WorldSaveData
            {
                SchemaVersion = WorldSaveService.CurrentSchemaVersion,
                WorldName = "unknown-block",
                Width = 4,
                Height = 4,
                Depth = 4,
                ChunkSize = 16,
                Seed = 1,
                ChangedBlocks = new[]
                {
                    new SavedBlockDelta { X = 2, Y = 1, Z = 2, CanonicalId = "no_such_block" },
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

            WorldLoadResult.Loaded(data).ApplyTo(world);

            Assert.That(world.GetBlock(new BlockPosition(2, 1, 2)), Is.EqualTo(BlockRegistry.Air));
            Assert.That(world.GetBlock(new BlockPosition(1, 1, 1)), Is.EqualTo(BlockRegistry.Graystone));
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
                var service = new WorldSaveService();
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
        public void SurvivalInventorySnapshotRoundTripsThroughSave()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            var snapshot = new Inventory(itemRegistry);
            snapshot.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            snapshot.SetSlot(3, new ItemStack(ItemId.FieldBandage, 2));

            try
            {
                var service = new WorldSaveService();
                service.Save(path, "snapshot-roundtrip", world, inventory, survivalSnapshot: snapshot);

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.PlayerInventory.SurvivalInventorySnapshot, Is.Not.Null);
                Assert.That(result.Data.PlayerInventory.SurvivalInventorySnapshot, Has.Length.EqualTo(2));

                SavedInventorySlot logs = result.Data.PlayerInventory.SurvivalInventorySnapshot.First(s => s.SlotIndex == 0);
                Assert.That(logs.CanonicalId, Is.EqualTo(ItemId.BranchwoodLog.Value));
                Assert.That(logs.Count, Is.EqualTo(12));

                SavedInventorySlot bandages = result.Data.PlayerInventory.SurvivalInventorySnapshot.First(s => s.SlotIndex == 3);
                Assert.That(bandages.CanonicalId, Is.EqualTo(ItemId.FieldBandage.Value));
                Assert.That(bandages.Count, Is.EqualTo(2));
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
                var service = new WorldSaveService();
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
                var service = new WorldSaveService();
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
                new WorldSaveService().Save(path, "no-tmp-test", world);

                string[] tmpFiles = Directory.GetFiles(path, "*.tmp", SearchOption.AllDirectories);
                Assert.That(tmpFiles, Is.Empty, "Expected no .tmp files inside the save directory after save.");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void FlatJsonFilePathReturnsControlledFailure()
        {
            // The legacy flat-JSON format (schema v1-v3) is unsupported: the app is unreleased,
            // so loading a flat file fails fast instead of migrating.
            string flatPath = Path.Combine(Path.GetTempPath(), $"blockiverse-legacy-{System.Guid.NewGuid():N}.json");

            try
            {
                var flatData = new WorldSaveData
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
                File.WriteAllText(flatPath, JsonUtility.ToJson(flatData, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService().Load(flatPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("unsupported"));
            }
            finally
            {
                if (File.Exists(flatPath)) File.Delete(flatPath);
            }
        }

        [Test]
        public void UnsupportedManifestSchemaVersionReturnsControlledFailure()
        {
            // No migrations exist (the app is unreleased): any schema other than the current one
            // is rejected with a clear failure instead of being migrated.
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "old-schema", world);

                string manifestPath = Path.Combine(path, "manifest.json");
                VxlwManifest manifest = JsonUtility.FromJson<VxlwManifest>(File.ReadAllText(manifestPath));
                manifest.SchemaVersion = WorldSaveService.CurrentSchemaVersion - 1;
                File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("unsupported"));
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
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "bad-inventory", world);

                // A tool stack above its max stack size must fail inventory validation on load.
                var playerSave = new VxlwPlayerSave
                {
                    SlotCount = 1,
                    HotbarSlotCount = 1,
                    SelectedHotbarSlotIndex = 0,
                    Slots = new[]
                    {
                        new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.ReedwoodDelver.Value, Count = 2 }
                    }
                };
                File.WriteAllText(
                    Path.Combine(path, "players", "local_player.json"),
                    JsonUtility.ToJson(playerSave, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService().Load(path);

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
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "oversized-inventory", world);

                var playerSave = new VxlwPlayerSave
                {
                    SlotCount = 1_000_000,
                    HotbarSlotCount = 1,
                    SelectedHotbarSlotIndex = 0,
                    Slots = new SavedInventorySlot[0]
                };
                File.WriteAllText(
                    Path.Combine(path, "players", "local_player.json"),
                    JsonUtility.ToJson(playerSave, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("slot count"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        // The "missing survival snapshot slot" (null element) branch is not reachable through
        // Load: JsonUtility never deserializes null array elements, so only the remaining
        // snapshot rejection reasons are covered here.
        [Test]
        public void SurvivalSnapshotSlotIndexOutOfBoundsReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-oob", world);
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = Inventory.DefaultSlotCount, CanonicalId = ItemId.BranchwoodLog.Value, Count = 1 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot slot index is outside inventory bounds"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SurvivalSnapshotDuplicateSlotIndexReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-duplicate", world);
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.BranchwoodLog.Value, Count = 1 },
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.FieldBandage.Value, Count = 1 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot has duplicate slot indexes"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SurvivalSnapshotNonPositiveCountReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-zero-count", world);
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.BranchwoodLog.Value, Count = 0 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot stack count is invalid"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SurvivalSnapshotMissingItemIdReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-missing-id", world);
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = string.Empty, Count = 1 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot slot is missing canonical item id"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SurvivalSnapshotUnregisteredItemReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-unregistered", world);
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = "no_such_item", Count = 1 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot item id is not registered"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SurvivalSnapshotOverstackedToolReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "snapshot-overstack", world);
                // A tool stack above its max stack size must fail snapshot validation on load.
                WritePlayerSaveWithSurvivalSnapshot(path, new[]
                {
                    new SavedInventorySlot { SlotIndex = 0, CanonicalId = ItemId.ReedwoodDelver.Value, Count = 2 }
                });

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("survival snapshot stack count exceeds item max stack size"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void CorruptedManifestReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "corrupt-manifest", world);
                File.WriteAllText(Path.Combine(path, "manifest.json"), "{ definitely not valid json");

                WorldLoadResult result = new WorldSaveService().Load(path);

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
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "corrupt-log", world);
                File.WriteAllText(Path.Combine(path, "manifest.json"), "{ definitely not valid json");

                BlockiverseLog.SetSinkForTesting(sink);
                BlockiverseLog.DevelopmentInfoEnabled = true;

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                BlockiverseLogEntry entry = sink.Entries.Single(log =>
                    log.Category == BlockiverseLogCategory.Persistence &&
                    log.Level == LogType.Warning &&
                    log.Message.Contains("Failed to load world save"));
                Assert.That(entry.Message, Does.Contain(new DirectoryInfo(path).Name));
                Assert.That(entry.Message, Does.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
                Assert.That(entry.Message, Does.Contain("corrupt or incomplete"));
            }
            finally
            {
                BlockiverseLog.ResetSinkForTesting();
                DeleteIfExists(path);
            }
        }

        [Test]
        public void PartiallyWrittenManifestReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                new WorldSaveService().Save(path, "partial-manifest", world);

                string manifestJson = File.ReadAllText(Path.Combine(path, "manifest.json"));
                File.WriteAllText(
                    Path.Combine(path, "manifest.json"),
                    manifestJson.Substring(0, manifestJson.Length - 2));

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("incomplete"));
            }
            finally
            {
                DeleteIfExists(path);
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

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("manifest"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ContainerContentsRoundTripThroughSave()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            var containers = new[]
            {
                new SavedContainer
                {
                    X = 2, Y = 3, Z = 4,
                    Slots = new[]
                    {
                        new SavedContainerSlot { CanonicalId = "reed_fiber", Count = 6 },
                        new SavedContainerSlot { CanonicalId = "stout_pole", Count = 2 },
                    }
                },
                new SavedContainer { X = 5, Y = 6, Z = 7, Slots = System.Array.Empty<SavedContainerSlot>() },
            };

            try
            {
                var service = new WorldSaveService();
                service.Save(path, "container-test", world, containers: containers);

                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "containers.json")), Is.True,
                    "Containers file should be written when containers are supplied.");

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.Containers, Is.Not.Null);
                Assert.That(result.Data.Containers.Length, Is.EqualTo(2));

                SavedContainer full = result.Data.Containers.First(c => c.X == 2 && c.Y == 3 && c.Z == 4);
                Assert.That(full.Slots.Length, Is.EqualTo(2));
                Assert.That(full.Slots.First(s => s.CanonicalId == "reed_fiber").Count, Is.EqualTo(6));

                SavedContainer emptied = result.Data.Containers.First(c => c.X == 5);
                Assert.That(emptied.Slots, Is.Empty, "An emptied container persists as a zero-slot entry.");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveWithoutContainersOmitsContainerFile()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            try
            {
                new WorldSaveService().Save(path, "no-containers", world);

                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "containers.json")), Is.False,
                    "No containers file should be written when none are supplied (backward compatible).");

                WorldLoadResult result = new WorldSaveService().Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.Containers, Is.Null);
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void OutOfBoundsRegionDeltaReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);
                new WorldSaveService().Save(path, "oob-delta", world);

                // Doctor the region file so its chunk index lands the delta far outside the
                // manifest bounds; the load must fail controlled instead of letting the delta
                // throw later in WorldLoadResult.ApplyTo.
                string regionsDir = Path.Combine(path, "dimensions", "main", "regions");
                string regionPath = Directory.GetFiles(regionsDir, "r.*.vxlr")[0];
                VxlwRegionFile region = JsonUtility.FromJson<VxlwRegionFile>(File.ReadAllText(regionPath));
                region.Chunks[0].ChunkX = 1000;
                File.WriteAllText(regionPath, JsonUtility.ToJson(region, prettyPrint: true));

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("outside world bounds"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void UnreadableRegionFileReturnsControlledFailure()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            try
            {
                world.SetBlock(new BlockPosition(1, 1, 1), BlockRegistry.Graystone);
                new WorldSaveService().Save(path, "bad-region", world);

                string regionsDir = Path.Combine(path, "dimensions", "main", "regions");
                string regionPath = Directory.GetFiles(regionsDir, "r.*.vxlr")[0];
                File.WriteAllText(regionPath, "{ definitely not a region file");

                WorldLoadResult result = new WorldSaveService().Load(path);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("corrupt"));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveThenLoadReproducesSimulationStationAndPlayerExtras()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            var extras = new WorldSaveExtras
            {
                WeatherTicksInState = 432,
                WeatherRngState = 48879u,
                PlayerState = new SavedPlayerState
                {
                    PositionX = 12.5f, PositionY = 33f, PositionZ = 64.25f, YawDegrees = 90f,
                    Health = 73, Hunger = 41, Thirst = 28, Stamina = 66
                },
                Saplings = new[] { new VxlwSaplingProgress { X = 1, Y = 2, Z = 3, AccumulatedTicks = 600 } },
                WildRegrowth = new[] { new VxlwWildRegrowthMarker { CanonicalId = "thornbrush", X = 4, Y = 5, Z = 6, RegrowAfterTick = 48000, AttemptsLeft = 3 } },
                BerrybushRegrowth = new[] { new VxlwBerrybushRegrowth { X = 7, Y = 8, Z = 9, AccumulatedTicks = 24000 } },
                Stations = new[]
                {
                    new VxlwStation
                    {
                        X = 10, Y = 11, Z = 12,
                        StationType = "ClayKiln",
                        Inputs = new[] { new SavedContainerSlot { CanonicalId = "clay_lump", Count = 4 } },
                        Fuel = new SavedContainerSlot { CanonicalId = "embercoal", Count = 2 },
                        Output = new SavedContainerSlot { CanonicalId = "fired_brick", Count = 1 },
                        ActiveRecipeOutputId = "fired_brick",
                        ProgressTicks = 60
                    }
                }
            };

            try
            {
                new WorldSaveService().Save(path, "extras-test", world, extras: extras);

                WorldLoadResult result = new WorldSaveService().Load(path);
                Assert.That(result.Success, Is.True, result.Error);

                WorldSaveData data = result.Data;
                Assert.That(data.WeatherTicksInState, Is.EqualTo(432));
                Assert.That(data.WeatherRngState, Is.EqualTo(48879u));

                Assert.That(data.PlayerState, Is.Not.Null, "Player state must round-trip.");
                Assert.That(data.PlayerState.PositionX, Is.EqualTo(12.5f));
                Assert.That(data.PlayerState.PositionY, Is.EqualTo(33f));
                Assert.That(data.PlayerState.PositionZ, Is.EqualTo(64.25f));
                Assert.That(data.PlayerState.YawDegrees, Is.EqualTo(90f));
                Assert.That(data.PlayerState.Health, Is.EqualTo(73));
                Assert.That(data.PlayerState.Hunger, Is.EqualTo(41));
                Assert.That(data.PlayerState.Thirst, Is.EqualTo(28));
                Assert.That(data.PlayerState.Stamina, Is.EqualTo(66));

                Assert.That(data.Saplings, Has.Length.EqualTo(1));
                Assert.That(data.Saplings[0].X, Is.EqualTo(1));
                Assert.That(data.Saplings[0].AccumulatedTicks, Is.EqualTo(600));

                Assert.That(data.WildRegrowth, Has.Length.EqualTo(1));
                Assert.That(data.WildRegrowth[0].CanonicalId, Is.EqualTo("thornbrush"));
                Assert.That(data.WildRegrowth[0].RegrowAfterTick, Is.EqualTo(48000L));
                Assert.That(data.WildRegrowth[0].AttemptsLeft, Is.EqualTo(3));

                Assert.That(data.BerrybushRegrowth, Has.Length.EqualTo(1));
                Assert.That(data.BerrybushRegrowth[0].Z, Is.EqualTo(9));
                Assert.That(data.BerrybushRegrowth[0].AccumulatedTicks, Is.EqualTo(24000));

                Assert.That(data.Stations, Has.Length.EqualTo(1));
                Assert.That(data.Stations[0].StationType, Is.EqualTo("ClayKiln"));
                Assert.That(data.Stations[0].Inputs, Has.Length.EqualTo(1));
                Assert.That(data.Stations[0].Inputs[0].CanonicalId, Is.EqualTo("clay_lump"));
                Assert.That(data.Stations[0].Inputs[0].Count, Is.EqualTo(4));
                Assert.That(data.Stations[0].Fuel.CanonicalId, Is.EqualTo("embercoal"));
                Assert.That(data.Stations[0].Output.CanonicalId, Is.EqualTo("fired_brick"));
                Assert.That(data.Stations[0].ActiveRecipeOutputId, Is.EqualTo("fired_brick"));
                Assert.That(data.Stations[0].ProgressTicks, Is.EqualTo(60));
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void ResavingWithoutSimulationOrStationsRemovesTheStaleFiles()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();

            var extras = new WorldSaveExtras
            {
                Saplings = new[] { new VxlwSaplingProgress { X = 1, Y = 2, Z = 3, AccumulatedTicks = 600 } },
                Stations = new[]
                {
                    new VxlwStation { X = 1, Y = 2, Z = 3, StationType = "ClayKiln", Inputs = new SavedContainerSlot[0] }
                }
            };

            try
            {
                var service = new WorldSaveService();
                service.Save(path, "stale-test", world, extras: extras);
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "simulation.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "stations.json")), Is.True);

                // The queues drained and the stations were removed: a re-save without them must
                // not leave the old files to resurrect dead state on the next load.
                service.Save(path, "stale-test", world, extras: new WorldSaveExtras());
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "simulation.json")), Is.False);
                Assert.That(File.Exists(Path.Combine(path, "dimensions", "main", "stations.json")), Is.False);

                WorldLoadResult result = service.Load(path);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.Data.Saplings, Is.Null);
                Assert.That(result.Data.Stations, Is.Null);
                Assert.That(result.Data.PlayerState, Is.Null);
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        [Test]
        public void SaveDiscardsOrphanedRegionsTmpDirectoryFromCrashedPriorSave()
        {
            string path = CreateTempSavePath();
            VoxelWorld world = CreateDefaultWorld();
            world.SetBlock(new BlockPosition(2, 2, 2), BlockRegistry.Graystone);

            try
            {
                // A save that crashed mid-write leaves a populated regions.tmp behind; its
                // region files must not be swapped into the live regions directory.
                string regionsDirTmp = Path.Combine(path, "dimensions", "main", "regions.tmp");
                Directory.CreateDirectory(regionsDirTmp);
                File.WriteAllText(Path.Combine(regionsDirTmp, "r.99.99.vxlr"), "{}");

                new WorldSaveService().Save(path, "orphan-tmp-test", world);

                string regionsDir = Path.Combine(path, "dimensions", "main", "regions");
                Assert.That(File.Exists(Path.Combine(regionsDir, "r.99.99.vxlr")), Is.False,
                    "Stale region file from an orphaned regions.tmp must not survive the swap.");
                Assert.That(Directory.GetFiles(regionsDir, "r.*.*.vxlr"), Is.Not.Empty,
                    "Expected the current save's own region file.");
                Assert.That(Directory.Exists(regionsDirTmp), Is.False,
                    "regions.tmp must not linger after a successful save.");
            }
            finally
            {
                DeleteIfExists(path);
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

        // Overwrites the saved player file with a valid base inventory plus the supplied
        // survival snapshot slots, so each test isolates one snapshot rejection reason.
        static void WritePlayerSaveWithSurvivalSnapshot(string path, SavedInventorySlot[] snapshotSlots)
        {
            var playerSave = new VxlwPlayerSave
            {
                SlotCount = Inventory.DefaultSlotCount,
                HotbarSlotCount = Inventory.DefaultHotbarSlotCount,
                SelectedHotbarSlotIndex = 0,
                Slots = new SavedInventorySlot[0],
                SurvivalInventorySnapshot = snapshotSlots
            };
            File.WriteAllText(
                Path.Combine(path, "players", "local_player.json"),
                JsonUtility.ToJson(playerSave, prettyPrint: true));
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
