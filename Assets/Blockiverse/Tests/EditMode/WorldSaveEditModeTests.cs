using System.IO;
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
    }
}
