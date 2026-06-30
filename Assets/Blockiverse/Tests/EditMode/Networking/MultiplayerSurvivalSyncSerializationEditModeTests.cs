using System.Collections;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class MultiplayerSurvivalSyncSerializationEditModeTests
    {
        [Test]
        public void BoundedNetworkStringReadsValidNgoString()
        {
            var writer = new FastBufferWriter(64, Allocator.Temp);
            try
            {
                writer.WriteValueSafe("clay_lump");
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    bool read = SurvivalSyncWireCodec.TryReadBoundedNetworkString(
                        ref reader,
                        maxChars: 32,
                        out string value);

                    Assert.That(read, Is.True);
                    Assert.That(value, Is.EqualTo("clay_lump"));
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void BoundedNetworkStringRejectsOversizedLengthBeforeAllocatingString()
        {
            var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            try
            {
                writer.WriteValueSafe(4096);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    bool read = SurvivalSyncWireCodec.TryReadBoundedNetworkString(
                        ref reader,
                        maxChars: 64,
                        out string value);

                    Assert.That(read, Is.False);
                    Assert.That(value, Is.Empty);
                    Assert.That(reader.Position, Is.EqualTo(reader.Length));
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void CommandResultRoundTripsThroughWireCodec()
        {
            SurvivalCommandResult original = SurvivalCommandResult.Reject(
                SurvivalCommandKind.HarvestResource,
                SurvivalCommandFailureReason.HarvestRejected,
                requestId: 42,
                item: new ItemStack(new ItemId("clay_lump"), 3).WithDurability(7),
                harvestFailureReason: BlockHarvestFailureReason.InventoryFull);

            var writer = new FastBufferWriter(128, Allocator.Temp);
            try
            {
                SurvivalSyncWireCodec.WriteCommandResult(ref writer, original);
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    SurvivalCommandResult decoded = SurvivalSyncWireCodec.ReadCommandResult(ref reader);

                    Assert.That(decoded.Accepted, Is.EqualTo(original.Accepted));
                    Assert.That(decoded.CommandKind, Is.EqualTo(original.CommandKind));
                    Assert.That(decoded.FailureReason, Is.EqualTo(original.FailureReason));
                    Assert.That(decoded.RequestId, Is.EqualTo(original.RequestId));
                    Assert.That(decoded.Item, Is.EqualTo(original.Item));
                    Assert.That(decoded.HarvestFailureReason, Is.EqualTo(original.HarvestFailureReason));
                }
                finally
                {
                    reader.Dispose();
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void HostDuplicateRequestWindowRejectsReplayWithoutFeedback()
        {
            var syncObject = new GameObject("Duplicate Request Survival Sync");
            syncObject.SetActive(false);

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                MethodInfo rejectDuplicate = typeof(MultiplayerSurvivalSync).GetMethod(
                    "TryRejectDuplicate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(rejectDuplicate, Is.Not.Null, "The host duplicate guard should remain present.");

                int feedbackCount = 0;
                sync.CommandFeedback += (_, _) => feedbackCount++;

                object[] firstArgs = { 42ul, 101u, SurvivalCommandKind.PlaceBlock, false, null };
                Assert.That(rejectDuplicate.Invoke(sync, firstArgs), Is.False);

                object[] replayArgs = { 42ul, 101u, SurvivalCommandKind.PlaceBlock, false, null };
                Assert.That(rejectDuplicate.Invoke(sync, replayArgs), Is.True);
                var replayResult = (SurvivalCommandResult)replayArgs[4];

                Assert.That(replayResult.IsDuplicate, Is.True);
                Assert.That(replayResult.CommandKind, Is.EqualTo(SurvivalCommandKind.PlaceBlock));
                Assert.That(replayResult.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.DuplicateRequest));
                Assert.That(replayResult.RequestId, Is.EqualTo(101u));
                Assert.That(feedbackCount, Is.EqualTo(0), "Duplicate replays should not raise local command feedback.");

                object[] zeroArgs = { 42ul, 0u, SurvivalCommandKind.PlaceBlock, false, null };
                Assert.That(rejectDuplicate.Invoke(sync, zeroArgs), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void ClearSessionStateDropsReconnectIdentityAndStashes()
        {
            var syncObject = new GameObject("Reconnect State Survival Sync");
            syncObject.SetActive(false);

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, null);

                IDictionary identityByClient = (IDictionary)typeof(MultiplayerSurvivalSync)
                    .GetField("playerIdentityKeysByClientId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(sync);
                IDictionary stashes = (IDictionary)typeof(MultiplayerSurvivalSync)
                    .GetField("stashedInventoriesByIdentityKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(sync);
                identityByClient.Add(42ul, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
                stashes.Add("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", new Inventory(ItemRegistry.CreateDefault()));

                MethodInfo clearSessionState = typeof(MultiplayerSurvivalSync).GetMethod(
                    "ClearSessionState",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                clearSessionState.Invoke(sync, null);

                Assert.That(identityByClient.Count, Is.EqualTo(0));
                Assert.That(stashes.Count, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void PlayerIdentityKeyRequiresValidGuidAndSecret()
        {
            MethodInfo buildIdentity = typeof(MultiplayerSurvivalSync).GetMethod(
                "TryBuildPlayerIdentityKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buildIdentity, Is.Not.Null);

            object[] validArgs =
            {
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                string.Empty
            };
            Assert.That(buildIdentity.Invoke(null, validArgs), Is.True);
            Assert.That(
                validArgs[2],
                Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));

            object[] missingSecret =
            {
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                string.Empty,
                "unchanged"
            };
            Assert.That(buildIdentity.Invoke(null, missingSecret), Is.False);
            Assert.That(missingSecret[2], Is.EqualTo(string.Empty));

            object[] malformedGuid =
            {
                "client_guid",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "unchanged"
            };
            Assert.That(buildIdentity.Invoke(null, malformedGuid), Is.False);
            Assert.That(malformedGuid[2], Is.EqualTo(string.Empty));
        }

        [Test]
        public void PlayerIdentityGuardRejectsDuplicateActiveClientClaims()
        {
            var syncObject = new GameObject("Reconnect Duplicate Survival Sync");
            syncObject.SetActive(false);

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, null);

                const string identityKey = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
                IDictionary identityByClient = (IDictionary)typeof(MultiplayerSurvivalSync)
                    .GetField("playerIdentityKeysByClientId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(sync);
                identityByClient.Add(42ul, identityKey);

                MethodInfo isDuplicateIdentity = typeof(MultiplayerSurvivalSync).GetMethod(
                    "IsPlayerIdentityBoundToDifferentClient",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(isDuplicateIdentity.Invoke(sync, new object[] { 42ul, identityKey }), Is.False);
                Assert.That(isDuplicateIdentity.Invoke(sync, new object[] { 43ul, identityKey }), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void UseOnPlacedContainerRaisesOpenEventWithoutPlacingHeldItem()
        {
            var worldObject = new GameObject("Container World Manager");
            var syncObject = new GameObject("Container Survival Sync");
            var target = new BlockPosition(2, 1, 2);
            var placement = new BlockPosition(2, 2, 2);

            try
            {
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                world.SetBlock(target, BlockRegistry.StorageCrate, trackChange: false);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);

                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, worldManager);
                sync.LocalInventory.SetSlot(0, new ItemStack(ItemId.StorageCrate, 1));

                bool opened = false;
                BlockPosition openedPosition = default;
                Inventory openedInventory = null;
                sync.ContainerOpenRequested += (position, inventory) =>
                {
                    opened = true;
                    openedPosition = position;
                    openedInventory = inventory;
                };

                SurvivalCommandResult result = sync.TrySubmitUse(target, placement, out bool requestSentToHost);

                Assert.That(result.Accepted, Is.True);
                Assert.That(result.CommandKind, Is.EqualTo(SurvivalCommandKind.ContainerOpen));
                Assert.That(requestSentToHost, Is.False);
                Assert.That(opened, Is.True);
                Assert.That(openedPosition, Is.EqualTo(target));
                Assert.That(openedInventory, Is.Not.Null);
                Assert.That(openedInventory.SlotCount, Is.EqualTo(ContainerInventoryStore.DefaultContainerSlotCount));
                Assert.That(worldManager.ContainerStore.Contains(target), Is.True);
                Assert.That(world.GetBlock(placement), Is.EqualTo(BlockRegistry.Air),
                    "Opening a placed container must not fall through to placing the held block.");
                Assert.That(sync.LocalInventory.CountOf(ItemId.StorageCrate), Is.EqualTo(1),
                    "Opening a placed container must not consume the held block.");
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void HostPlaceRejectsEquippedSlotOutsideHotbar()
        {
            var worldObject = new GameObject("Hotbar Slot World Manager");
            var syncObject = new GameObject("Hotbar Slot Survival Sync");
            var target = new BlockPosition(2, 1, 2);

            try
            {
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);

                MultiplayerChunkAuthoritySync chunkSync = syncObject.AddComponent<MultiplayerChunkAuthoritySync>();
                chunkSync.Configure(null, worldManager);
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, chunkSync, worldManager);
                int backpackSlot = sync.LocalInventory.HotbarSlotCount;
                sync.LocalInventory.SetSlot(backpackSlot, new ItemStack(ItemId.StorageCrate, 1));

                MethodInfo processPlace = typeof(MultiplayerSurvivalSync).GetMethod(
                    "ProcessHostPlace",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(processPlace, Is.Not.Null);

                var result = (SurvivalCommandResult)processPlace.Invoke(
                    sync,
                    new object[] { NetworkManager.ServerClientId, 41u, target, backpackSlot, false, false });

                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.NotPlaceable));
                Assert.That(world.GetBlock(target), Is.EqualTo(BlockRegistry.Air));
                Assert.That(sync.LocalInventory.GetSlot(backpackSlot), Is.EqualTo(new ItemStack(ItemId.StorageCrate, 1)));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void HostRepairRejectsToolSlotOutsideHotbar()
        {
            var syncObject = new GameObject("Repair Hotbar Slot Survival Sync");

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, null);
                int backpackSlot = sync.LocalInventory.HotbarSlotCount;
                ItemStack wornTool = new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(1);
                sync.LocalInventory.SetSlot(backpackSlot, wornTool);
                sync.LocalInventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 1));

                MethodInfo processRepair = typeof(MultiplayerSurvivalSync).GetMethod(
                    "ProcessHostRepair",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(processRepair, Is.Not.Null);

                var result = (SurvivalCommandResult)processRepair.Invoke(
                    sync,
                    new object[] { NetworkManager.ServerClientId, 42u, backpackSlot, false });

                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.RepairRejected));
                Assert.That(sync.LocalInventory.GetSlot(backpackSlot), Is.EqualTo(wornTool));
                Assert.That(sync.LocalInventory.CountOf(ItemId.WorkPlank), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void RemoteSurvivalCommandsAreRejectedInCreativeWorlds()
        {
            var worldObject = new GameObject("Creative Mode World Manager");
            var syncObject = new GameObject("Creative Mode Survival Sync");

            try
            {
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                worldManager.SetGameMode(WorldGameMode.Creative);
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, worldManager);

                MethodInfo processCraft = typeof(MultiplayerSurvivalSync).GetMethod(
                    "ProcessHostCraft",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(processCraft, Is.Not.Null);

                var result = (SurvivalCommandResult)processCraft.Invoke(
                    sync,
                    new object[]
                    {
                        42ul,
                        41u,
                        ItemId.WorkPlank,
                        CraftingStation.None,
                        true
                    });

                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.GameModeRejected));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void DiagnosticsExposeSurvivalSyncCountersAsGroupedSnapshot()
        {
            var syncObject = new GameObject("Diagnostics Survival Sync");
            syncObject.SetActive(false);

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, null);
                MethodInfo processCraft = typeof(MultiplayerSurvivalSync).GetMethod(
                    "ProcessHostCraft",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(processCraft, Is.Not.Null);

                processCraft.Invoke(
                    sync,
                    new object[]
                    {
                        NetworkManager.ServerClientId,
                        0u,
                        ItemId.None,
                        CraftingStation.None,
                        false
                    });

                SurvivalSyncDiagnostics diagnostics = sync.Diagnostics;
                Assert.That(diagnostics.ReceivedCraftRequestCount, Is.EqualTo(1));
                Assert.That(diagnostics.AcceptedCraftCount, Is.Zero);
                Assert.That(diagnostics.RejectedCommandCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void HarvestingContainerDropsCrateWhenContainerLootConsumesLastInventorySlot()
        {
            var worldObject = new GameObject("Container Harvest World");
            var syncObject = new GameObject("Container Harvest Survival Sync");
            var target = new BlockPosition(2, 1, 2);

            try
            {
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                world.SetBlock(target, BlockRegistry.StorageCrate, trackChange: false);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);
                worldManager.GetOrCreateContainerStore()
                    .GetOrCreate(target)
                    .SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));

                MultiplayerChunkAuthoritySync chunkSync = syncObject.AddComponent<MultiplayerChunkAuthoritySync>();
                chunkSync.Configure(null, worldManager);
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, chunkSync, worldManager);

                for (int slot = 0; slot < sync.LocalInventory.SlotCount; slot++)
                    sync.LocalInventory.SetSlot(slot, new ItemStack(ItemId.LooseLoam, ItemRegistry.BlockStackSize));
                sync.LocalInventory.SetSlot(0, ItemStack.Empty);

                SurvivalCommandResult result = sync.TrySubmitHarvest(
                    target,
                    new ItemStack(ItemId.FlintMallet, 1).WithDurability(108),
                    out bool requestSentToHost);

                Assert.That(result.Accepted, Is.True);
                Assert.That(requestSentToHost, Is.False);
                Assert.That(world.GetBlock(target), Is.EqualTo(BlockRegistry.Air));
                Assert.That(worldManager.ContainerStore.Contains(target), Is.False);
                Assert.That(sync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(1));
                Assert.That(sync.LocalInventory.CountOf(ItemId.StorageCrate), Is.Zero);
                Assert.That(sync.GroundItems.Count, Is.EqualTo(1));
                Assert.That(sync.GroundItems.Items[0].Stack, Is.EqualTo(new ItemStack(ItemId.StorageCrate, 1)));
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void RemoteHarvestRejectsOverflowBeforeSpawningUnsyncedGroundDrop()
        {
            var worldObject = new GameObject("Remote Overflow Harvest World");
            var syncObject = new GameObject("Remote Overflow Harvest Survival Sync");
            var target = new BlockPosition(2, 1, 2);
            const ulong RemoteClientId = 42;

            try
            {
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                world.SetBlock(target, BlockRegistry.BranchwoodLog, trackChange: false);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);

                MultiplayerChunkAuthoritySync chunkSync = syncObject.AddComponent<MultiplayerChunkAuthoritySync>();
                chunkSync.Configure(null, worldManager);
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, chunkSync, worldManager);

                Inventory remoteInventory = sync.GetInventory(RemoteClientId);
                for (int slot = 0; slot < remoteInventory.SlotCount; slot++)
                    remoteInventory.SetSlot(slot, new ItemStack(ItemId.LooseLoam, ItemRegistry.BlockStackSize));

                SurvivalCommandResult result = InvokeRemoteHostHarvest(
                    sync,
                    RemoteClientId,
                    requestId: 1,
                    target);

                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.InventoryFull));
                Assert.That(result.HarvestFailureReason, Is.EqualTo(BlockHarvestFailureReason.InventoryFull));
                Assert.That(world.GetBlock(target), Is.EqualTo(BlockRegistry.BranchwoodLog));
                Assert.That(sync.GroundItems.Count, Is.Zero);
                Assert.That(remoteInventory.CountOf(ItemId.BranchwoodLog), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        [Test]
        public void StationPersistentStateExportRestoreRoundTripsAndClears()
        {
            var syncObject = new GameObject("Station Persistence Survival Sync");
            syncObject.SetActive(false);

            try
            {
                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, null);

                var position = new BlockPosition(4, 5, 6);
                var restoredState = new MultiplayerSurvivalSync.StationPersistentState(
                    position,
                    CraftingStation.ClayKiln,
                    new[] { new ItemStack(ItemId.ClayLump, 4) },
                    new ItemStack(ItemId.Embercoal, 1),
                    new ItemStack(ItemId.FiredBrick, 1),
                    ItemId.FiredBrick,
                    progressTicks: 60);

                sync.RestoreStationStates(new[] { restoredState });
                var exported = sync.ExportStationStates();

                Assert.That(exported, Has.Count.EqualTo(1));
                MultiplayerSurvivalSync.StationPersistentState roundTripped = exported[0];
                Assert.That(roundTripped.Position, Is.EqualTo(position));
                Assert.That(roundTripped.StationType, Is.EqualTo(CraftingStation.ClayKiln));
                Assert.That(roundTripped.Inputs, Has.Length.EqualTo(1));
                Assert.That(roundTripped.Inputs[0], Is.EqualTo(new ItemStack(ItemId.ClayLump, 4)));
                Assert.That(roundTripped.Fuel, Is.EqualTo(new ItemStack(ItemId.Embercoal, 1)));
                Assert.That(roundTripped.Output, Is.EqualTo(new ItemStack(ItemId.FiredBrick, 1)));
                Assert.That(roundTripped.ActiveRecipeOutput, Is.EqualTo(ItemId.FiredBrick));
                Assert.That(roundTripped.ProgressTicks, Is.EqualTo(60));

                sync.RestoreStationStates(null);
                Assert.That(sync.ExportStationStates(), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
            }
        }

        [Test]
        public void HostStationCraftsAdvanceFromWorldTimeClockTicks()
        {
            var worldObject = new GameObject("Station Clock World");
            var syncObject = new GameObject("Station Clock Survival Sync");
            var stationPosition = new BlockPosition(2, 1, 2);

            try
            {
                BlockiverseRuntimeState.Reset();
                WorldTimeClock clock = worldObject.AddComponent<WorldTimeClock>();
                clock.Configure(WorldTimeClock.DefaultDayLengthSeconds, startNormalizedTime: 0.25f, timeScale: 1.0f);
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                world.SetBlock(stationPosition, BlockRegistry.ClayKiln, trackChange: false);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);

                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, worldManager);
                SmeltingStationModel station = sync.GetOrCreateStationModel(stationPosition, CraftingStation.ClayKiln);
                Assert.That(station.TryDepositInput(new ItemStack(ItemId.ClayLump, 2)), Is.True);
                Assert.That(station.TryDepositFuel(new ItemStack(ItemId.Embercoal, 1)), Is.True);

                clock.AdvanceRuntime(1.0f);

                Assert.That(station.IsActive, Is.True);
                Assert.That(station.ProgressTicks, Is.EqualTo(SmeltingModel.TicksPerSecond));
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void HostPrunesStaleStationModelsAndRaisesRemovalEvent()
        {
            var worldObject = new GameObject("Stale Station World");
            var syncObject = new GameObject("Stale Station Survival Sync");
            var stationPosition = new BlockPosition(2, 1, 2);

            try
            {
                BlockiverseRuntimeState.Reset();
                CreativeWorldManager worldManager = worldObject.AddComponent<CreativeWorldManager>();
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 6,
                    height: 4,
                    depth: 6,
                    chunkSize: 2,
                    seed: 47,
                    groundHeight: 1);
                var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
                world.SetBlock(stationPosition, BlockRegistry.ClayKiln, trackChange: false);
                worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
                worldManager.SetGameMode(WorldGameMode.Survival);

                MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                sync.Configure(null, null, worldManager);
                sync.GetOrCreateStationModel(stationPosition, CraftingStation.ClayKiln);

                int removalEvents = 0;
                BlockPosition removedPosition = default;
                sync.StationRemoved += removed =>
                {
                    removalEvents++;
                    removedPosition = removed;
                };

                world.SetBlock(stationPosition, BlockRegistry.Air, trackChange: false);
                sync.TickStations(1);

                Assert.That(sync.ExportStationStates(), Is.Empty);
                Assert.That(removalEvents, Is.EqualTo(1));
                Assert.That(removedPosition, Is.EqualTo(stationPosition));
            }
            finally
            {
                BlockiverseRuntimeState.Reset();
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void InventorySnapshotsUseFragmentedReliableDelivery()
        {
            FieldInfo field = typeof(MultiplayerSurvivalSync).GetField(
                "InventorySnapshotDelivery",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, "Inventory snapshot delivery contract field is missing.");
            Assert.That(field.GetValue(null), Is.EqualTo(NetworkDelivery.ReliableFragmentedSequenced));
        }

        static SurvivalCommandResult InvokeRemoteHostHarvest(
            MultiplayerSurvivalSync sync,
            ulong clientId,
            uint requestId,
            BlockPosition position)
        {
            MethodInfo method = typeof(MultiplayerSurvivalSync).GetMethod(
                "ProcessHostHarvest",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            return (SurvivalCommandResult)method.Invoke(
                sync,
                new object[] { clientId, requestId, position, ItemStack.Empty, true, -1 });
        }
    }
}
