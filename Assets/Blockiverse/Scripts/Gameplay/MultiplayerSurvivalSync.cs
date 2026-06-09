using System;
using System.Collections.Generic;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum SurvivalCommandKind
    {
        None,
        HarvestResource,
        CraftRecipe,
        SharedCrateDeposit,
        SharedCrateWithdraw,
        PlaceBlock
    }

    public enum SurvivalCommandFailureReason
    {
        None,
        AwaitingHostWorldSnapshot,
        HostOnlyAuthorityOperation,
        HarvestRejected,
        MutationRejected,
        MissingRecipe,
        CraftingRejected,
        InvalidTransfer,
        InventoryFull,
        SharedCrateEmpty,
        DuplicateRequest,
        NotPlaceable,
        PlacementRejected
    }

    public readonly struct SurvivalCommandResult
    {
        public SurvivalCommandResult(
            bool accepted,
            bool pendingHostValidation,
            bool duplicate,
            SurvivalCommandKind commandKind,
            SurvivalCommandFailureReason failureReason,
            uint requestId,
            ItemStack item,
            BlockHarvestFailureReason harvestFailureReason,
            CraftingFailureReason craftingFailureReason)
        {
            Accepted = accepted;
            PendingHostValidation = pendingHostValidation;
            IsDuplicate = duplicate;
            CommandKind = commandKind;
            FailureReason = failureReason;
            RequestId = requestId;
            Item = item;
            HarvestFailureReason = harvestFailureReason;
            CraftingFailureReason = craftingFailureReason;
        }

        public bool Accepted { get; }
        public bool PendingHostValidation { get; }
        public bool IsDuplicate { get; }
        public SurvivalCommandKind CommandKind { get; }
        public SurvivalCommandFailureReason FailureReason { get; }
        public uint RequestId { get; }
        public ItemStack Item { get; }
        public BlockHarvestFailureReason HarvestFailureReason { get; }
        public CraftingFailureReason CraftingFailureReason { get; }

        public static SurvivalCommandResult Accept(
            SurvivalCommandKind commandKind,
            uint requestId,
            ItemStack item = default)
        {
            return new SurvivalCommandResult(
                accepted: true,
                pendingHostValidation: false,
                duplicate: false,
                commandKind,
                SurvivalCommandFailureReason.None,
                requestId,
                item,
                BlockHarvestFailureReason.None,
                CraftingFailureReason.None);
        }

        public static SurvivalCommandResult RequestSent(SurvivalCommandKind commandKind, uint requestId)
        {
            return new SurvivalCommandResult(
                accepted: false,
                pendingHostValidation: true,
                duplicate: false,
                commandKind,
                SurvivalCommandFailureReason.None,
                requestId,
                default,
                BlockHarvestFailureReason.None,
                CraftingFailureReason.None);
        }

        public static SurvivalCommandResult DuplicateResult(SurvivalCommandKind commandKind, uint requestId)
        {
            return new SurvivalCommandResult(
                accepted: false,
                pendingHostValidation: false,
                duplicate: true,
                commandKind,
                SurvivalCommandFailureReason.DuplicateRequest,
                requestId,
                default,
                BlockHarvestFailureReason.None,
                CraftingFailureReason.None);
        }

        public static SurvivalCommandResult Reject(
            SurvivalCommandKind commandKind,
            SurvivalCommandFailureReason failureReason,
            uint requestId = 0,
            ItemStack item = default,
            BlockHarvestFailureReason harvestFailureReason = BlockHarvestFailureReason.None,
            CraftingFailureReason craftingFailureReason = CraftingFailureReason.None)
        {
            if (failureReason == SurvivalCommandFailureReason.None)
                throw new ArgumentException("Rejected survival commands must include a concrete reason.", nameof(failureReason));

            return new SurvivalCommandResult(
                accepted: false,
                pendingHostValidation: false,
                duplicate: false,
                commandKind,
                failureReason,
                requestId,
                item,
                harvestFailureReason,
                craftingFailureReason);
        }
    }

    [DisallowMultipleComponent]
    public sealed class MultiplayerSurvivalSync : MonoBehaviour
    {
        const string HarvestRequestMessage = "Blockiverse.Survival.HarvestRequest";
        const string PlaceRequestMessage = "Blockiverse.Survival.PlaceRequest";
        const string CraftRequestMessage = "Blockiverse.Survival.CraftRequest";
        const string CrateTransferRequestMessage = "Blockiverse.Survival.CrateTransferRequest";
        const string CommandResultMessage = "Blockiverse.Survival.CommandResult";
        const string InventorySnapshotMessage = "Blockiverse.Survival.InventorySnapshot";
        const string SharedCrateSnapshotMessage = "Blockiverse.Survival.SharedCrateSnapshot";
        const int CommandRequestMessageBytes = 128;
        const int CommandResultMessageBytes = 128;
        const int InventorySnapshotMessageBytes = 4096;
        const int SharedCrateSlotCount = 12;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] CreativeWorldManager worldManager;

        readonly Dictionary<ulong, Inventory> inventoriesByClientId = new();
        readonly Dictionary<ulong, HashSet<uint>> processedRequestsByClientId = new();
        readonly Dictionary<uint, SurvivalCommandKind> pendingCommandRequests = new();

        NetworkManager subscribedNetworkManager;
        ItemRegistry itemRegistry;
        CraftingRecipeBook recipeBook;
        ResourceHarvestService harvestService;
        Inventory localInventory;
        Inventory sharedCrateInventory;
        uint nextCommandRequestId = 1;
        bool messagesRegistered;

        public Inventory LocalInventory => localInventory ??= CreatePlayerInventory();
        public Inventory SharedCrateInventory => sharedCrateInventory ??= CreateSharedCrateInventory();
        public SurvivalCommandResult LastCommandResult { get; private set; }
        public int PendingCommandRequestCount => pendingCommandRequests.Count;

        // The local player's selected hotbar slot — set by the survival inventory UI, read by the VR
        // interaction bridge so harvest/place use the held tool/block. Lives here (Gameplay) because the
        // VR bridge cannot reference the UI assembly.
        int selectedHotbarSlotIndex;
        public int SelectedHotbarSlotIndex
        {
            get => selectedHotbarSlotIndex;
            set
            {
                int hotbar = LocalInventory.HotbarSlotCount;
                selectedHotbarSlotIndex = hotbar > 0 ? Mathf.Clamp(value, 0, hotbar - 1) : 0;
            }
        }

        // Event-friendly setter for the selected hotbar slot (subscribed to the survival inventory UI).
        public void SetSelectedHotbarSlot(int slotIndex) => SelectedHotbarSlotIndex = slotIndex;

        // Survival/creative interaction mode for the local player, with inventory snapshotting on switch.
        readonly SurvivalCreativeModeSwitch modeSwitch = new();
        public PlayerModeState CurrentMode => modeSwitch.CurrentMode;

        // Flips between survival and creative interaction, snapshotting/restoring the survival inventory.
        public void ToggleMode()
        {
            if (modeSwitch.CurrentMode == PlayerModeState.Survival)
                modeSwitch.SwitchToCreative(LocalInventory);
            else
                modeSwitch.SwitchToSurvival(LocalInventory);
        }

        // The item in the selected hotbar slot (bare hand when empty/out of range).
        public ItemStack EquippedItem
        {
            get
            {
                Inventory inv = LocalInventory;
                return selectedHotbarSlotIndex >= 0 && selectedHotbarSlotIndex < inv.SlotCount
                    ? inv.GetSlot(selectedHotbarSlotIndex)
                    : ItemStack.Empty;
            }
        }

        public int ReceivedHarvestRequestCount { get; private set; }
        public int ReceivedPlaceRequestCount { get; private set; }
        public int AcceptedPlaceCount { get; private set; }
        public int ReceivedCraftRequestCount { get; private set; }
        public int ReceivedCrateTransferRequestCount { get; private set; }
        public int AcceptedHarvestCount { get; private set; }
        public int AcceptedCraftCount { get; private set; }
        public int AcceptedCrateTransferCount { get; private set; }
        public int RejectedCommandCount { get; private set; }
        public int ReceivedInventorySnapshotCount { get; private set; }
        public int ReceivedSharedCrateSnapshotCount { get; private set; }
        public uint LastSentCommandRequestId { get; private set; }
        public uint LastCompletedCommandRequestId { get; private set; }

        public void Configure(
            BlockiverseNetworkSession targetSession,
            MultiplayerChunkAuthoritySync targetChunkAuthoritySync,
            CreativeWorldManager targetWorldManager,
            ItemRegistry targetItemRegistry = null,
            CraftingRecipeBook targetRecipeBook = null)
        {
            UnsubscribeNetworkCallbacks();
            session = targetSession;
            chunkAuthoritySync = targetChunkAuthoritySync;
            worldManager = targetWorldManager;
            itemRegistry = targetItemRegistry ?? ItemRegistry.CreateDefault();
            recipeBook = targetRecipeBook ?? CraftingRecipeBook.CreateDefault(itemRegistry);
            harvestService = new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
            inventoriesByClientId.Clear();
            processedRequestsByClientId.Clear();
            pendingCommandRequests.Clear();
            nextCommandRequestId = 1;
            localInventory = CreatePlayerInventory();
            sharedCrateInventory = CreateSharedCrateInventory();
            SubscribeNetworkCallbacks();
            RefreshLocalInventoryReference();
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            SubscribeNetworkCallbacks();
            RefreshLocalInventoryReference();
        }

        void OnDisable()
        {
            UnsubscribeNetworkCallbacks();
        }

        void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();
        }

        public Inventory GetInventory(ulong clientId)
        {
            if (!inventoriesByClientId.TryGetValue(clientId, out Inventory inventory))
            {
                inventory = CreatePlayerInventory();
                inventoriesByClientId.Add(clientId, inventory);
            }

            return inventory;
        }

        public SurvivalCommandResult TrySubmitHarvest(
            BlockPosition position,
            ItemStack equippedItem,
            out bool requestSentToHost,
            int equippedSlotIndex = -1)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                if (chunkAuthoritySync != null && !chunkAuthoritySync.HasHostGenerationSnapshotForSession)
                {
                    LastCommandResult = SurvivalCommandResult.Reject(
                        SurvivalCommandKind.HarvestResource,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendHarvestRequest(requestId, position, equippedItem, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.HarvestResource, requestId);
                return LastCommandResult;
            }

            LastCommandResult = ProcessHostHarvest(ResolveLocalClientId(), requestId: 0, position, equippedItem, sendResponse: false, equippedSlotIndex);
            return LastCommandResult;
        }

        // Harvest convenience used by the VR interaction bridge: harvests with the currently selected
        // hotbar item, so callers in assemblies that don't reference Survival need not build an ItemStack.
        public SurvivalCommandResult TrySubmitHarvest(BlockPosition position, out bool requestSentToHost)
            => TrySubmitHarvest(position, EquippedItem, out requestSentToHost, SelectedHotbarSlotIndex);

        // Place convenience: places using the currently selected hotbar slot.
        public SurvivalCommandResult TrySubmitPlace(BlockPosition position, out bool requestSentToHost)
            => TrySubmitPlace(position, out requestSentToHost, SelectedHotbarSlotIndex);

        // Places the block mapped to the equipped hotbar item at the target, consuming one item.
        // Authoritative: the host validates the item maps to a placeable block and decrements the
        // host-owned inventory; clients send a request and reconcile from the inventory snapshot.
        public SurvivalCommandResult TrySubmitPlace(
            BlockPosition position,
            out bool requestSentToHost,
            int equippedSlotIndex = -1)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                if (chunkAuthoritySync != null && !chunkAuthoritySync.HasHostGenerationSnapshotForSession)
                {
                    LastCommandResult = SurvivalCommandResult.Reject(
                        SurvivalCommandKind.PlaceBlock,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendPlaceRequest(requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.PlaceBlock, requestId);
                return LastCommandResult;
            }

            LastCommandResult = ProcessHostPlace(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false);
            return LastCommandResult;
        }

        public SurvivalCommandResult TrySubmitCraft(
            ItemId outputItemId,
            CraftingStation availableStation,
            out bool requestSentToHost)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                uint requestId = AllocateCommandRequestId();
                SendCraftRequest(requestId, outputItemId, availableStation);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.CraftRecipe, requestId);
                return LastCommandResult;
            }

            LastCommandResult = ProcessHostCraft(ResolveLocalClientId(), requestId: 0, outputItemId, availableStation, sendResponse: false);
            return LastCommandResult;
        }

        public SurvivalCommandResult TrySubmitCrateDeposit(
            ItemId itemId,
            int count,
            out bool requestSentToHost)
        {
            return TrySubmitCrateTransfer(SurvivalCommandKind.SharedCrateDeposit, itemId, count, out requestSentToHost);
        }

        public SurvivalCommandResult TrySubmitCrateWithdraw(
            ItemId itemId,
            int count,
            out bool requestSentToHost)
        {
            return TrySubmitCrateTransfer(SurvivalCommandKind.SharedCrateWithdraw, itemId, count, out requestSentToHost);
        }

        SurvivalCommandResult TrySubmitCrateTransfer(
            SurvivalCommandKind commandKind,
            ItemId itemId,
            int count,
            out bool requestSentToHost)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                uint requestId = AllocateCommandRequestId();
                SendCrateTransferRequest(requestId, commandKind, itemId, count);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(commandKind, requestId);
                return LastCommandResult;
            }

            LastCommandResult = ProcessHostCrateTransfer(
                ResolveLocalClientId(),
                requestId: 0,
                commandKind,
                itemId,
                count,
                sendResponse: false);
            return LastCommandResult;
        }

        SurvivalCommandResult ProcessHostHarvest(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            ItemStack equippedItem,
            bool sendResponse,
            int equippedSlotIndex = -1)
        {
            ReceivedHarvestRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.HarvestResource, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            Inventory inventory = GetInventory(clientId);
            // Server-authoritative tool resolution (M8). Remote client requests
            // (sendResponse == true) never trust the client-supplied stack: the equipped tool is
            // read from the host's authoritative copy of that client's inventory at the requested
            // slot, so a client cannot fabricate a high-tier tool to bypass harvest tier gates.
            // A request without a valid slot index is treated as empty-handed. Host-local requests
            // (the host's own player) use the local equipped item directly, as it is already
            // authoritative on this peer.
            ItemStack authoritativeItem = ResolveAuthoritativeTool(inventory, equippedItem, equippedSlotIndex, sendResponse);
            BlockHarvestResult harvest = ResolveHarvestService().TryPreviewHarvest(
                ResolveWorld(),
                inventory,
                position,
                authoritativeItem);

            if (!harvest.Succeeded)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.HarvestRejected,
                    requestId,
                    harvest.Drop,
                    harvest.FailureReason);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, BlockRegistry.Air, harvest.BlockId),
                out _,
                out _);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.MutationRejected,
                    requestId,
                    harvest.Drop);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.TryAddAll(harvest.Drop);

            if (equippedSlotIndex >= 0)
            {
                ItemStack serverSlot = inventory.GetSlot(equippedSlotIndex);
                if (!serverSlot.IsEmpty && serverSlot.Durability > 0)
                {
                    int remaining = serverSlot.Durability - 1;
                    inventory.SetSlot(equippedSlotIndex, remaining > 0
                        ? serverSlot.WithDurability(remaining)
                        : ItemStack.Empty);
                }
            }

            AcceptedHarvestCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.HarvestResource,
                requestId,
                harvest.Drop);
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostPlace(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedPlaceRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.PlaceBlock, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            Inventory inventory = GetInventory(clientId);

            // The placed block is derived from the host-owned inventory slot, never trusted from the
            // client. An empty/out-of-range slot or a non-block item cannot place anything.
            ItemStack held = equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount
                ? inventory.GetSlot(equippedSlotIndex)
                : ItemStack.Empty;

            if (held.IsEmpty ||
                !itemRegistry.TryGet(held.ItemId, out ItemDefinition def) ||
                !def.HasBlockMapping)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PlaceBlock,
                    SurvivalCommandFailureReason.NotPlaceable,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockId block = def.BlockId.Value;
            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, block, BlockRegistry.Air),
                out _,
                out _);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PlaceBlock,
                    SurvivalCommandFailureReason.PlacementRejected,
                    requestId,
                    new ItemStack(held.ItemId, 1));
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.Remove(held.ItemId, 1);

            AcceptedPlaceCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.PlaceBlock,
                requestId,
                new ItemStack(held.ItemId, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        // Resolves the tool the host will validate a harvest against.
        // Remote requests are validated against the host-owned inventory slot only; the
        // client-supplied stack is never trusted. Host-local requests use the local stack,
        // falling back to the slot when one is supplied.
        static ItemStack ResolveAuthoritativeTool(Inventory inventory, ItemStack equippedItem, int equippedSlotIndex, bool sendResponse)
        {
            bool slotIsValid = equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount;
            if (sendResponse)
                return slotIsValid ? inventory.GetSlot(equippedSlotIndex) : ItemStack.Empty;
            return slotIsValid ? inventory.GetSlot(equippedSlotIndex) : equippedItem;
        }

        SurvivalCommandResult ProcessHostCraft(
            ulong clientId,
            uint requestId,
            ItemId outputItemId,
            CraftingStation availableStation,
            bool sendResponse)
        {
            ReceivedCraftRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.CraftRecipe, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (!ResolveRecipeBook().TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.CraftRecipe,
                    SurvivalCommandFailureReason.MissingRecipe,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            Inventory inventory = GetInventory(clientId);
            CraftingResult crafting = CraftingService.TryCraft(inventory, recipe, availableStation);

            if (!crafting.Succeeded)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.CraftRecipe,
                    SurvivalCommandFailureReason.CraftingRejected,
                    requestId,
                    recipe.Output,
                    craftingFailureReason: crafting.FailureReason);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            AcceptedCraftCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.CraftRecipe,
                requestId,
                recipe.Output);
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostCrateTransfer(
            ulong clientId,
            uint requestId,
            SurvivalCommandKind commandKind,
            ItemId itemId,
            int count,
            bool sendResponse)
        {
            ReceivedCrateTransferRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, commandKind, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (commandKind != SurvivalCommandKind.SharedCrateDeposit &&
                commandKind != SurvivalCommandKind.SharedCrateWithdraw)
            {
                var invalidResult = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                SendCommandFailure(clientId, invalidResult, sendResponse);
                return invalidResult;
            }

            if (!IsValidTransferItem(itemId) || count <= 0)
            {
                var invalidResult = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                SendCommandFailure(clientId, invalidResult, sendResponse);
                return invalidResult;
            }

            var stack = new ItemStack(itemId, count);
            Inventory playerInventory = GetInventory(clientId);
            Inventory crateInventory = SharedCrateInventory;
            SurvivalCommandResult result;

            if (commandKind == SurvivalCommandKind.SharedCrateDeposit)
            {
                if (playerInventory.CountOf(itemId) < count)
                {
                    result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId, stack);
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                if (crateInventory.GetAvailableCapacity(itemId) < count)
                {
                    result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InventoryFull, requestId, stack);
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                playerInventory.Remove(itemId, count);
                crateInventory.TryAddAll(stack);
            }
            else
            {
                if (crateInventory.CountOf(itemId) < count)
                {
                    result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.SharedCrateEmpty, requestId, stack);
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                if (playerInventory.GetAvailableCapacity(itemId) < count)
                {
                    result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InventoryFull, requestId, stack);
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                crateInventory.Remove(itemId, count);
                playerInventory.TryAddAll(stack);
            }

            AcceptedCrateTransferCount++;
            result = SurvivalCommandResult.Accept(commandKind, requestId, stack);
            SendInventorySnapshot(clientId);
            BroadcastSharedCrateSnapshot();
            SendCommandResult(clientId, result, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = result;
            return result;
        }

        bool IsValidTransferItem(ItemId itemId)
        {
            return itemId != ItemId.None && ResolveItemRegistry().TryGet(itemId, out _);
        }

        bool TryRejectDuplicate(
            ulong clientId,
            uint requestId,
            SurvivalCommandKind commandKind,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (requestId == 0)
                return false;

            HashSet<uint> processedRequests = GetProcessedRequests(clientId);
            if (!processedRequests.Add(requestId))
            {
                result = SurvivalCommandResult.DuplicateResult(commandKind, requestId);
                SendInventorySnapshot(clientId);
                SendSharedCrateSnapshot(clientId);
                SendCommandResult(clientId, result, sendResponse);
                return true;
            }

            return false;
        }

        void SendCommandFailure(ulong clientId, SurvivalCommandResult result, bool sendResponse)
        {
            RejectedCommandCount++;
            SendCommandResult(clientId, result, sendResponse);
            LastCommandResult = result;
        }

        void HandleHarvestRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out uint requestId);
            BlockPosition position = ReadBlockPosition(ref reader);
            ItemStack equippedItem = ReadItemStack(ref reader);
            reader.ReadValueSafe(out int equippedSlotIndex);
            ProcessHostHarvest(senderClientId, requestId, position, equippedItem, sendResponse: true, equippedSlotIndex);
        }

        void SendPlaceRequest(uint requestId, BlockPosition position, int equippedSlotIndex)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingCommandRequests[requestId] = SurvivalCommandKind.PlaceBlock;
            LastSentCommandRequestId = requestId;
            var writer = new FastBufferWriter(CommandRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(equippedSlotIndex);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    PlaceRequestMessage,
                    NetworkManager.ServerClientId,
                    writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void HandlePlaceRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out uint requestId);
            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int equippedSlotIndex);
            ProcessHostPlace(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
        }

        void HandleCraftRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out uint requestId);
            reader.ReadValueSafe(out string outputItemId);
            reader.ReadValueSafe(out int availableStation);
            ProcessHostCraft(
                senderClientId,
                requestId,
                new ItemId(outputItemId),
                (CraftingStation)availableStation,
                sendResponse: true);
        }

        void HandleCrateTransferRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out uint requestId);
            reader.ReadValueSafe(out int commandKind);
            ItemStack stack = ReadItemStack(ref reader);
            ProcessHostCrateTransfer(
                senderClientId,
                requestId,
                (SurvivalCommandKind)commandKind,
                stack.ItemId,
                stack.Count,
                sendResponse: true);
        }

        void HandleCommandResultMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            SurvivalCommandResult result = ReadCommandResult(ref reader);
            LastCommandResult = result;
            TryCompletePendingCommandRequest(result.RequestId);
        }

        void HandleInventorySnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            ApplyInventorySnapshot(LocalInventory, ref reader);
            ReceivedInventorySnapshotCount++;
        }

        void HandleSharedCrateSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            ApplyInventorySnapshot(SharedCrateInventory, ref reader);
            ReceivedSharedCrateSnapshotCount++;
        }

        void SendHarvestRequest(uint requestId, BlockPosition position, ItemStack equippedItem, int equippedSlotIndex = -1)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingCommandRequests[requestId] = SurvivalCommandKind.HarvestResource;
            LastSentCommandRequestId = requestId;
            var writer = new FastBufferWriter(CommandRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                WriteBlockPosition(ref writer, position);
                WriteItemStack(ref writer, equippedItem);
                writer.WriteValueSafe(equippedSlotIndex);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    HarvestRequestMessage,
                    NetworkManager.ServerClientId,
                    writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendCraftRequest(uint requestId, ItemId outputItemId, CraftingStation availableStation)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingCommandRequests[requestId] = SurvivalCommandKind.CraftRecipe;
            LastSentCommandRequestId = requestId;
            var writer = new FastBufferWriter(CommandRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                writer.WriteValueSafe(outputItemId.Value);
                writer.WriteValueSafe((int)availableStation);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    CraftRequestMessage,
                    NetworkManager.ServerClientId,
                    writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendCrateTransferRequest(
            uint requestId,
            SurvivalCommandKind commandKind,
            ItemId itemId,
            int count)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingCommandRequests[requestId] = commandKind;
            LastSentCommandRequestId = requestId;
            var writer = new FastBufferWriter(CommandRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                writer.WriteValueSafe((int)commandKind);
                WriteItemStack(ref writer, new ItemStack(itemId, count));
                networkManager.CustomMessagingManager.SendNamedMessage(
                    CrateTransferRequestMessage,
                    NetworkManager.ServerClientId,
                    writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendCommandResult(ulong clientId, SurvivalCommandResult result, bool sendResponse)
        {
            if (!sendResponse)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId)
            {
                return;
            }

            var writer = new FastBufferWriter(CommandResultMessageBytes, Allocator.Temp);

            try
            {
                WriteCommandResult(ref writer, result);
                networkManager.CustomMessagingManager.SendNamedMessage(CommandResultMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendInventorySnapshot(ulong clientId)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer)
            {
                return;
            }

            if (clientId == networkManager.LocalClientId)
            {
                localInventory = GetInventory(clientId);
                return;
            }

            RegisterMessageHandlers();
            var writer = new FastBufferWriter(InventorySnapshotMessageBytes, Allocator.Temp);

            try
            {
                WriteInventorySnapshot(ref writer, GetInventory(clientId));
                networkManager.CustomMessagingManager.SendNamedMessage(InventorySnapshotMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendSharedCrateSnapshot(ulong clientId)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId)
            {
                return;
            }

            RegisterMessageHandlers();
            var writer = new FastBufferWriter(InventorySnapshotMessageBytes, Allocator.Temp);

            try
            {
                WriteInventorySnapshot(ref writer, SharedCrateInventory);
                networkManager.CustomMessagingManager.SendNamedMessage(SharedCrateSnapshotMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void BroadcastSharedCrateSnapshot()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer)
            {
                return;
            }

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
                SendSharedCrateSnapshot(clientId);
        }

        uint AllocateCommandRequestId()
        {
            uint requestId = nextCommandRequestId++;

            if (nextCommandRequestId == 0)
                nextCommandRequestId = 1;

            return requestId;
        }

        bool TryCompletePendingCommandRequest(uint requestId)
        {
            if (requestId == 0 || !pendingCommandRequests.Remove(requestId))
                return false;

            LastCompletedCommandRequestId = requestId;
            return true;
        }

        void ResetPendingCommands()
        {
            pendingCommandRequests.Clear();
            nextCommandRequestId = 1;
            LastSentCommandRequestId = 0;
            LastCompletedCommandRequestId = 0;
        }

        void HandleServerStarted()
        {
            RegisterMessageHandlers();
            RefreshLocalInventoryReference();
        }

        void HandleClientStarted()
        {
            RegisterMessageHandlers();

            if (IsActiveClientOnly())
            {
                ResetPendingCommands();
                localInventory = CreatePlayerInventory();
                sharedCrateInventory = CreateSharedCrateInventory();
            }
            else
            {
                RefreshLocalInventoryReference();
            }
        }

        void HandleClientConnected(ulong clientId)
        {
            if (!CanProcessHostRequests())
                return;

            GetInventory(clientId);
            SendInventorySnapshot(clientId);
            SendSharedCrateSnapshot(clientId);
        }

        void HandleServerStopped(bool wasHost)
        {
            UnregisterMessageHandlers();
        }

        void HandleClientStopped(bool wasHost)
        {
            ResetPendingCommands();
            UnregisterMessageHandlers();
        }

        void SubscribeNetworkCallbacks()
        {
            ResolveReferences();
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null || subscribedNetworkManager == networkManager)
                return;

            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.OnServerStarted += HandleServerStarted;
            subscribedNetworkManager.OnClientStarted += HandleClientStarted;
            subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
            subscribedNetworkManager.OnServerStopped += HandleServerStopped;
            subscribedNetworkManager.OnClientStopped += HandleClientStopped;
            RegisterMessageHandlers();
        }

        void UnsubscribeNetworkCallbacks()
        {
            UnregisterMessageHandlers();

            if (subscribedNetworkManager == null)
                return;

            subscribedNetworkManager.OnServerStarted -= HandleServerStarted;
            subscribedNetworkManager.OnClientStarted -= HandleClientStarted;
            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnServerStopped -= HandleServerStopped;
            subscribedNetworkManager.OnClientStopped -= HandleClientStopped;
            subscribedNetworkManager = null;
        }

        void RegisterMessageHandlers()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (messagesRegistered ||
                networkManager == null ||
                !networkManager.IsListening ||
                networkManager.CustomMessagingManager == null)
            {
                return;
            }

            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(HarvestRequestMessage, HandleHarvestRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlaceRequestMessage, HandlePlaceRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CraftRequestMessage, HandleCraftRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CrateTransferRequestMessage, HandleCrateTransferRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CommandResultMessage, HandleCommandResultMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(InventorySnapshotMessage, HandleInventorySnapshotMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(SharedCrateSnapshotMessage, HandleSharedCrateSnapshotMessage);
            messagesRegistered = true;
        }

        void UnregisterMessageHandlers()
        {
            if (!messagesRegistered ||
                subscribedNetworkManager == null ||
                subscribedNetworkManager.CustomMessagingManager == null)
            {
                messagesRegistered = false;
                return;
            }

            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(HarvestRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlaceRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CraftRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CrateTransferRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CommandResultMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(InventorySnapshotMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SharedCrateSnapshotMessage);
            messagesRegistered = false;
        }

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            itemRegistry ??= ItemRegistry.CreateDefault();
            recipeBook ??= CraftingRecipeBook.CreateDefault(itemRegistry);
            harvestService ??= new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
        }

        void RefreshLocalInventoryReference()
        {
            if (CanProcessHostRequests())
                localInventory = GetInventory(ResolveLocalClientId());
        }

        bool CanProcessHostRequests()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            return networkManager != null &&
                   networkManager.IsListening &&
                   networkManager.IsServer;
        }

        bool IsActiveClientOnly()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            return networkManager != null &&
                   networkManager.IsListening &&
                   networkManager.IsClient &&
                   !networkManager.IsServer;
        }

        bool IsMessageFromHost(ulong senderClientId)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            return networkManager != null &&
                   networkManager.IsListening &&
                   senderClientId == NetworkManager.ServerClientId &&
                   IsActiveClientOnly();
        }

        ulong ResolveLocalClientId()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            return networkManager != null && networkManager.IsListening
                ? networkManager.LocalClientId
                : NetworkManager.ServerClientId;
        }

        NetworkManager ResolveNetworkManager()
        {
            return ResolveNetworkManagerOrNull() ?? throw new InvalidOperationException("Multiplayer survival sync requires a network session.");
        }

        NetworkManager ResolveNetworkManagerOrNull()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            return session != null ? session.NetworkManager : null;
        }

        MultiplayerChunkAuthoritySync ResolveChunkAuthoritySync()
        {
            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();

            return chunkAuthoritySync ?? throw new InvalidOperationException("Multiplayer survival sync requires chunk authority sync.");
        }

        VoxelWorld ResolveWorld()
        {
            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (worldManager == null || worldManager.World == null)
                throw new InvalidOperationException("Multiplayer survival sync requires a voxel world.");

            return worldManager.World;
        }

        CraftingRecipeBook ResolveRecipeBook()
        {
            ResolveReferences();
            return recipeBook;
        }

        ItemRegistry ResolveItemRegistry()
        {
            ResolveReferences();
            return itemRegistry;
        }

        ResourceHarvestService ResolveHarvestService()
        {
            ResolveReferences();
            return harvestService;
        }

        Inventory CreatePlayerInventory()
        {
            ResolveReferences();
            return new Inventory(itemRegistry);
        }

        Inventory CreateSharedCrateInventory()
        {
            ResolveReferences();
            return new Inventory(itemRegistry, SharedCrateSlotCount, hotbarSlotCount: 0);
        }

        HashSet<uint> GetProcessedRequests(ulong clientId)
        {
            if (!processedRequestsByClientId.TryGetValue(clientId, out HashSet<uint> processedRequests))
            {
                processedRequests = new HashSet<uint>();
                processedRequestsByClientId.Add(clientId, processedRequests);
            }

            return processedRequests;
        }

        static void WriteCommandResult(ref FastBufferWriter writer, SurvivalCommandResult result)
        {
            writer.WriteValueSafe(result.Accepted);
            writer.WriteValueSafe(result.PendingHostValidation);
            writer.WriteValueSafe(result.IsDuplicate);
            writer.WriteValueSafe((int)result.CommandKind);
            writer.WriteValueSafe((int)result.FailureReason);
            writer.WriteValueSafe(result.RequestId);
            WriteItemStack(ref writer, result.Item);
            writer.WriteValueSafe((int)result.HarvestFailureReason);
            writer.WriteValueSafe((int)result.CraftingFailureReason);
        }

        static SurvivalCommandResult ReadCommandResult(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool accepted);
            reader.ReadValueSafe(out bool pendingHostValidation);
            reader.ReadValueSafe(out bool duplicate);
            reader.ReadValueSafe(out int commandKind);
            reader.ReadValueSafe(out int failureReason);
            reader.ReadValueSafe(out uint requestId);
            ItemStack item = ReadItemStack(ref reader);
            reader.ReadValueSafe(out int harvestFailureReason);
            reader.ReadValueSafe(out int craftingFailureReason);

            return new SurvivalCommandResult(
                accepted,
                pendingHostValidation,
                duplicate,
                (SurvivalCommandKind)commandKind,
                (SurvivalCommandFailureReason)failureReason,
                requestId,
                item,
                (BlockHarvestFailureReason)harvestFailureReason,
                (CraftingFailureReason)craftingFailureReason);
        }

        static void WriteInventorySnapshot(ref FastBufferWriter writer, Inventory inventory)
        {
            writer.WriteValueSafe(inventory.SlotCount);
            writer.WriteValueSafe(inventory.HotbarSlotCount);

            for (int index = 0; index < inventory.SlotCount; index++)
                WriteItemStack(ref writer, inventory.GetSlot(index));
        }

        static void ApplyInventorySnapshot(Inventory inventory, ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int slotCount);
            reader.ReadValueSafe(out int hotbarSlotCount);

            if (inventory.SlotCount != slotCount || inventory.HotbarSlotCount != hotbarSlotCount)
                throw new InvalidOperationException("Inventory snapshot shape does not match the receiving inventory.");

            for (int index = 0; index < slotCount; index++)
            {
                ItemStack stack = ReadItemStack(ref reader);
                if (stack.IsEmpty)
                    inventory.ClearSlot(index);
                else
                    inventory.SetSlot(index, stack);
            }
        }

        static void WriteItemStack(ref FastBufferWriter writer, ItemStack stack)
        {
            writer.WriteValueSafe(stack.ItemId.Value);
            writer.WriteValueSafe(stack.Count);
            writer.WriteValueSafe(stack.Durability);
        }

        static ItemStack ReadItemStack(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out string itemId);
            reader.ReadValueSafe(out int count);
            reader.ReadValueSafe(out int durability);
            if (count <= 0) return ItemStack.Empty;
            ItemStack stack = new ItemStack(new ItemId(itemId), count);
            return durability > 0 ? stack.WithDurability(durability) : stack;
        }

        static void WriteBlockPosition(ref FastBufferWriter writer, BlockPosition position)
        {
            writer.WriteValueSafe(position.X);
            writer.WriteValueSafe(position.Y);
            writer.WriteValueSafe(position.Z);
        }

        static BlockPosition ReadBlockPosition(ref FastBufferReader reader)
        {
            reader.ReadValueSafe(out int x);
            reader.ReadValueSafe(out int y);
            reader.ReadValueSafe(out int z);
            return new BlockPosition(x, y, z);
        }
    }
}
