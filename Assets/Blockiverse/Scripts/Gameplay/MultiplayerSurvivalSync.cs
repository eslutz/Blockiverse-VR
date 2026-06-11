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
        PlaceBlock,
        StripLog,
        TillSoil,
        PlantSeed,
        RepairTool,
        StationOpen,
        StationDepositInput,
        StationDepositFuel,
        StationCollectOutput,
        UseConsumable
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
        PlacementRejected,
        NotStrippable,
        StripRejected,
        NotTillable,
        TillRejected,
        NotPlantable,
        PlantRejected,
        RepairRejected,
        NotAStation,
        StationRejected,
        NotConsumable
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
        // Single client→host command channel: every request shares the wire header
        // [uint requestId][int SurvivalCommandKind]; the host dispatcher switches on the kind.
        const string CommandRequestMessage = "Blockiverse.Survival.CommandRequest";
        const string StationSnapshotMessage = "Blockiverse.Survival.StationSnapshot";
        const string CommandResultMessage = "Blockiverse.Survival.CommandResult";
        const string InventorySnapshotMessage = "Blockiverse.Survival.InventorySnapshot";
        const string SharedCrateSnapshotMessage = "Blockiverse.Survival.SharedCrateSnapshot";
        // Sized for the worst-case command payload, a station deposit at ~66 bytes: the 8-byte
        // [requestId][kind] header + 12-byte block position + an ItemStack whose id string is
        // wire-encoded at 4 + 2 bytes/char (longest canonical id today is 17 chars). 128 keeps
        // ~2x headroom; FastBufferWriter throws at send time on overflow, so grow this alongside
        // any larger future command payload.
        const int CommandRequestMessageBytes = 128;
        const int CommandResultMessageBytes = 128;
        const int StationSnapshotMessageBytes = 512;
        const int InventorySnapshotMessageBytes = 4096;
        const int SharedCrateSlotCount = 12;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] CreativeWorldManager worldManager;

        readonly Dictionary<ulong, Inventory> inventoriesByClientId = new();
        readonly Dictionary<ulong, ProcessedRequestWindow> processedRequestsByClientId = new();
        readonly Dictionary<uint, (SurvivalCommandKind kind, BlockPosition position)> pendingCommandRequests = new();

        // Smelting stations keyed by their block position. On the host these are the authoritative
        // models, ticked from Update; on remote clients they are display mirrors fed by snapshots.
        readonly Dictionary<BlockPosition, SmeltingStationModel> stationModels = new();
        readonly List<BlockPosition> staleStationPositions = new();
        float stationTickRemainder;

        // Scratch input-slot buffer for HandleStationSnapshotMessage, grown to the largest
        // snapshot slot count seen so per-snapshot receipt does not allocate; entries past the
        // current snapshot's count are cleared before use.
        ItemStack[] stationSnapshotInputs = Array.Empty<ItemStack>();

        NetworkManager subscribedNetworkManager;
        ItemRegistry itemRegistry;
        CraftingRecipeBook recipeBook;
        ResourceHarvestService harvestService;
        Inventory localInventory;
        Inventory sharedCrateInventory;
        uint nextCommandRequestId = 1;
        bool messagesRegistered;

        public Inventory LocalInventory => GetInventory(ResolveLocalClientId());
        public Inventory SharedCrateInventory => sharedCrateInventory ??= CreateSharedCrateInventory();
        public SurvivalCommandResult LastCommandResult { get; private set; }
        public int PendingCommandRequestCount => pendingCommandRequests.Count;

        // Raised whenever the local player's inventory contents may have changed (a host-side
        // command mutated it, or a host snapshot was applied on a client) so display code can
        // repaint without polling. SharedCrateChanged is the same signal for the shared crate.
        public event Action LocalInventoryChanged;
        public event Action SharedCrateChanged;

        // Local-player survival command feedback for presentation (audio/VFX/haptics): the
        // resolved result plus the command's block target (default for non-spatial commands).
        // Raised immediately on the host/offline peer and on host confirmation for clients.
        public event Action<SurvivalCommandResult, BlockPosition> CommandFeedback;

        void RaiseCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            CommandFeedback?.Invoke(result, position);
        }

        // Finalizes a host/offline-processed local command: records it and raises presentation
        // feedback (clients get theirs when the host's result message completes the pending entry).
        SurvivalCommandResult CompleteLocalCommand(SurvivalCommandResult result, BlockPosition position = default)
        {
            LastCommandResult = result;
            if (!result.IsDuplicate)
                RaiseCommandFeedback(result, position);
            return result;
        }

        // Scratch for RebindLocalInventoryMapping (the local client id changes across
        // offline/host/client transitions, so stale aliases must be dropped).
        readonly List<ulong> staleLocalInventoryIds = new();

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

        // Flips between survival and creative interaction, snapshotting/restoring the survival
        // inventory. Host/offline only: a remote client's inventory is a host-owned mirror, so a
        // local snapshot+clear would desync from the authoritative copy and be clobbered by the
        // next snapshot. Returns false when the switch is unavailable.
        public bool ToggleMode()
        {
            if (IsActiveClientOnly())
                return false;

            if (modeSwitch.CurrentMode == PlayerModeState.Survival)
                modeSwitch.SwitchToCreative(LocalInventory);
            else
                modeSwitch.SwitchToSurvival(LocalInventory);

            LocalInventoryChanged?.Invoke();
            return true;
        }

        // Sets the interaction mode outright (host/offline only); used when entering a loaded
        // or freshly created world whose manifest dictates the mode.
        public void SetMode(PlayerModeState mode)
        {
            if (mode == modeSwitch.CurrentMode)
                return;

            ToggleMode();
        }

        // The inventory persistence should write: the stashed survival slots while the player is
        // in creative mode, else the live inventory.
        public Inventory BuildPersistedInventory()
        {
            if (modeSwitch.CurrentMode == PlayerModeState.Survival || !modeSwitch.HasSurvivalSnapshot)
                return LocalInventory;

            Inventory inventory = CreatePlayerInventory();
            System.Collections.Generic.IReadOnlyList<ItemStack> slots = modeSwitch.SurvivalSnapshotSlots;
            int count = Math.Min(slots.Count, inventory.SlotCount);
            for (int index = 0; index < count; index++)
            {
                if (!slots[index].IsEmpty)
                    inventory.SetSlot(index, slots[index]);
            }

            return inventory;
        }

        // Replaces the local player's inventory contents in place from a loaded save
        // (host/offline only — clients receive theirs via host snapshots).
        public void RestoreLocalInventory(Inventory loaded, int selectedHotbarSlot)
        {
            if (loaded == null || IsActiveClientOnly())
                return;

            Inventory target = LocalInventory;
            ClearInventorySlots(target);

            int count = Math.Min(loaded.SlotCount, target.SlotCount);
            for (int index = 0; index < count; index++)
            {
                ItemStack stack = loaded.GetSlot(index);
                if (!stack.IsEmpty)
                    target.SetSlot(index, stack);
            }

            SelectedHotbarSlotIndex = selectedHotbarSlot;
            LocalInventoryChanged?.Invoke();
        }

        // The item in the selected hotbar slot (bare hand when empty/out of range).
        public ItemStack EquippedItem
        {
            get
            {
                // Bound by HotbarSlotCount (matching the setter's clamp), not SlotCount — only
                // hotbar slots can be equipped.
                Inventory inv = LocalInventory;
                return selectedHotbarSlotIndex >= 0 && selectedHotbarSlotIndex < inv.HotbarSlotCount
                    ? inv.GetSlot(selectedHotbarSlotIndex)
                    : ItemStack.Empty;
            }
        }

        public int ReceivedHarvestRequestCount { get; private set; }
        public int ReceivedPlaceRequestCount { get; private set; }
        public int AcceptedPlaceCount { get; private set; }
        public int ReceivedStripLogRequestCount { get; private set; }
        public int AcceptedStripLogCount { get; private set; }
        public int ReceivedTillRequestCount { get; private set; }
        public int AcceptedTillCount { get; private set; }
        public int ReceivedPlantRequestCount { get; private set; }
        public int AcceptedPlantCount { get; private set; }
        public int ReceivedRepairRequestCount { get; private set; }
        public int AcceptedRepairCount { get; private set; }
        public int ReceivedConsumableRequestCount { get; private set; }
        public int AcceptedConsumableCount { get; private set; }
        public int ReceivedStationCommandRequestCount { get; private set; }
        public int AcceptedStationCommandCount { get; private set; }
        public int ReceivedStationSnapshotCount { get; private set; }
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
            inLifecycleResolve = true;
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
            stationModels.Clear();
            stationTickRemainder = 0.0f;
            nextCommandRequestId = 1;
            localInventory = CreatePlayerInventory();
            sharedCrateInventory = CreateSharedCrateInventory();
            SubscribeNetworkCallbacks();
            RefreshLocalInventoryReference();
            inLifecycleResolve = false;
            // Configure replaces the inventory instances; consumers holding the old references
            // (HUD panels, auto-loot target) must re-bind.
            LocalInventoryChanged?.Invoke();
            SharedCrateChanged?.Invoke();
        }

        void Awake()
        {
            inLifecycleResolve = true;
            ResolveReferences();
            inLifecycleResolve = false;
        }

        void OnEnable()
        {
            inLifecycleResolve = true;
            ResolveReferences();
            SubscribeNetworkCallbacks();
            RefreshLocalInventoryReference();
            inLifecycleResolve = false;
        }

        void OnDisable()
        {
            UnsubscribeNetworkCallbacks();
        }

        void Update()
        {
            // Authoritative smelting-station ticking (§8/§9.3): the host (or offline peer) advances
            // station crafts in real time; remote clients only mirror snapshots. The empty-model
            // check comes first so the common no-station frame skips network-manager resolution.
            if (stationModels.Count == 0 || IsActiveClientOnly())
                return;

            stationTickRemainder += Time.deltaTime * SmeltingModel.TicksPerSecond;
            int ticks = (int)stationTickRemainder;
            if (ticks <= 0)
                return;

            stationTickRemainder -= ticks;
            TickStations(ticks);
        }

        void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();
        }

        public Inventory GetInventory(ulong clientId)
        {
            if (!inventoriesByClientId.TryGetValue(clientId, out Inventory inventory))
            {
                // The local player's entry IS the stable localInventory instance in every mode
                // (offline, host, client): host-side command processing, the HUD binding, the
                // auto-loot target, and the snapshot mirror all mutate one object, so the local
                // player can never split-brain across two inventories.
                inventory = clientId == ResolveLocalClientId()
                    ? localInventory ??= CreatePlayerInventory()
                    : CreatePlayerInventory();
                inventoriesByClientId.Add(clientId, inventory);
            }

            return inventory;
        }

        // The local client id changes across offline (host id), hosting (host id), and client
        // (assigned id) transitions; drop stale aliases of the local instance and bind the
        // current id so GetInventory(localId) always resolves to localInventory.
        void RebindLocalInventoryMapping()
        {
            if (localInventory == null)
                return;

            staleLocalInventoryIds.Clear();
            foreach (KeyValuePair<ulong, Inventory> pair in inventoriesByClientId)
            {
                if (ReferenceEquals(pair.Value, localInventory))
                    staleLocalInventoryIds.Add(pair.Key);
            }

            foreach (ulong staleId in staleLocalInventoryIds)
                inventoriesByClientId.Remove(staleId);

            inventoriesByClientId[ResolveLocalClientId()] = localInventory;
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
                SendHarvestRequest(requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.HarvestResource, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostHarvest(ResolveLocalClientId(), requestId: 0, position, equippedItem, sendResponse: false, equippedSlotIndex),
                position);
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
                SendBlockCommandRequest(SurvivalCommandKind.PlaceBlock, requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.PlaceBlock, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostPlace(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false),
                position);
        }

        // The survival "use" action with the held item: a Feller strips a targeted branchwood_log
        // into smooth_branchwood (§7.4), a Tiller tills targeted soil into tended_soil (§11.1), a
        // seed plants its crop on targeted tended_soil (§11.2); anything else places the held block
        // into the adjacent cell. The choice lives here (not the VR bridge) because it depends on
        // the held item's tool/block mapping, which is Survival data; the host re-validates.
        public SurvivalCommandResult TrySubmitUse(BlockPosition targetBlock, BlockPosition placement, out bool requestSentToHost)
        {
            // Using a timed smelting station (Clay Kiln / Bellows Forge) opens its panel instead of
            // acting with the held item (§8.4). The UI layer subscribes to StationOpenRequested.
            VoxelWorld targetWorld = ResolveWorldOrNull();
            if (targetWorld != null &&
                targetWorld.Bounds.Contains(targetBlock) &&
                StationProximity.TryGetStationForBlock(targetWorld.GetBlock(targetBlock), out CraftingStation targetStation) &&
                SmeltingStationModel.IsTimedStation(targetStation))
            {
                requestSentToHost = false;
                StationOpenRequested?.Invoke(targetBlock, targetStation);
                LastCommandResult = SurvivalCommandResult.Accept(SurvivalCommandKind.StationOpen, requestId: 0);
                return LastCommandResult;
            }

            ItemStack held = EquippedItem;
            if (!held.IsEmpty && itemRegistry.TryGet(held.ItemId, out ItemDefinition def))
            {
                if (def.ToolClass == HarvestToolKind.Feller)
                    return TrySubmitStripLog(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (def.ToolClass == HarvestToolKind.Tiller)
                    return TrySubmitTill(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (FarmingService.IsSeedItem(held.ItemId))
                    return TrySubmitPlantSeed(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                // Using a held consumable (field bandage, water flask, …) consumes it instead of
                // placing; the vitals effect is applied by SurvivalVitalsRuntime on confirmation.
                if (def.Kind == ItemKind.Consumable)
                    return TrySubmitUseConsumable(out requestSentToHost, SelectedHotbarSlotIndex);
            }

            return TrySubmitPlace(placement, out requestSentToHost, SelectedHotbarSlotIndex);
        }

        // Feller strip-log: turns a targeted branchwood_log into smooth_branchwood in place (no drop),
        // consuming Feller durability. Server-authoritative like harvest/place.
        public SurvivalCommandResult TrySubmitStripLog(
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
                        SurvivalCommandKind.StripLog,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendBlockCommandRequest(SurvivalCommandKind.StripLog, requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.StripLog, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostStripLog(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false),
                position);
        }

        // Tiller till: converts targeted tillable soil to tended_soil in place (§11.1), consuming
        // Tiller durability. Server-authoritative like harvest/place.
        public SurvivalCommandResult TrySubmitTill(
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
                        SurvivalCommandKind.TillSoil,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendBlockCommandRequest(SurvivalCommandKind.TillSoil, requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.TillSoil, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostTill(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false),
                position);
        }

        // Seed planting: plants the held seed's crop above targeted tended_soil (§11.2), consuming
        // one seed. Server-authoritative like harvest/place.
        public SurvivalCommandResult TrySubmitPlantSeed(
            BlockPosition soilPosition,
            out bool requestSentToHost,
            int equippedSlotIndex = -1)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                if (chunkAuthoritySync != null && !chunkAuthoritySync.HasHostGenerationSnapshotForSession)
                {
                    LastCommandResult = SurvivalCommandResult.Reject(
                        SurvivalCommandKind.PlantSeed,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendBlockCommandRequest(SurvivalCommandKind.PlantSeed, requestId, soilPosition, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.PlantSeed, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostPlantSeed(ResolveLocalClientId(), requestId: 0, soilPosition, equippedSlotIndex, sendResponse: false),
                soilPosition);
        }

        // Mend Bench tool repair (§10.7): restores 25% max durability of the tool in the given slot
        // (default: the selected hotbar slot) for one matching head material. Server-authoritative;
        // the host validates Mend Bench proximity and mutates the host-owned inventory.
        public SurvivalCommandResult TrySubmitRepair(out bool requestSentToHost, int toolSlotIndex = -1)
        {
            requestSentToHost = false;

            if (toolSlotIndex < 0)
                toolSlotIndex = SelectedHotbarSlotIndex;

            if (IsActiveClientOnly())
            {
                uint requestId = AllocateCommandRequestId();
                SendSlotCommandRequest(SurvivalCommandKind.RepairTool, requestId, toolSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.RepairTool, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostRepair(ResolveLocalClientId(), requestId: 0, toolSlotIndex, sendResponse: false));
        }

        // Fires on the consuming peer when the host confirms a consumable was used (one stack of
        // the consumed item). SurvivalVitalsRuntime applies the matching vitals effect (§13).
        public event Action<ItemStack> ConsumableConsumed;

        // Consumes one consumable from the given slot (default: the selected hotbar slot). The
        // inventory decrement is server-authoritative; the vitals effect is applied client-side by
        // the ConsumableConsumed subscriber once the host accepts (vitals are local-player state).
        public SurvivalCommandResult TrySubmitUseConsumable(out bool requestSentToHost, int slotIndex = -1)
        {
            requestSentToHost = false;

            if (slotIndex < 0)
                slotIndex = SelectedHotbarSlotIndex;

            if (IsActiveClientOnly())
            {
                uint requestId = AllocateCommandRequestId();
                SendSlotCommandRequest(SurvivalCommandKind.UseConsumable, requestId, slotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.UseConsumable, requestId);
                return LastCommandResult;
            }

            SurvivalCommandResult consumableResult =
                ProcessHostUseConsumable(ResolveLocalClientId(), requestId: 0, slotIndex, sendResponse: false);
            if (consumableResult.Accepted)
                ConsumableConsumed?.Invoke(consumableResult.Item);
            return CompleteLocalCommand(consumableResult);
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

            return CompleteLocalCommand(
                ProcessHostCraft(ResolveLocalClientId(), requestId: 0, outputItemId, availableStation, sendResponse: false));
        }

        // ── Smelting stations (Clay Kiln / Bellows Forge, §8/§9.3/§9.4) ─────────────────────────

        // Fired when the local player uses a timed smelting station block; the menu layer opens the
        // station panel bound to the (host-authoritative or mirrored) model for that position.
        public event Action<BlockPosition, CraftingStation> StationOpenRequested;

        // The station model for a position: authoritative on the host, a display mirror on clients.
        public SmeltingStationModel GetOrCreateStationModel(BlockPosition position, CraftingStation stationType)
        {
            if (!SmeltingStationModel.IsTimedStation(stationType))
                throw new ArgumentException("Only timed stations (kiln/forge) have runtime models.", nameof(stationType));

            if (stationModels.TryGetValue(position, out SmeltingStationModel model) &&
                model.StationType == stationType)
            {
                return model;
            }

            model = new SmeltingStationModel(
                stationType,
                SmeltingStationModel.InputSlotCountFor(stationType),
                ResolveRecipeBook(),
                ResolveItemRegistry());
            stationModels[position] = model;
            return model;
        }

        // Advances all host-owned station models, pruning stations whose block was removed and
        // broadcasting a snapshot whenever a station's externally visible state changes (craft
        // begins/completes, fuel consumed). Exposed for tests; runtime ticking comes from Update.
        public void TickStations(int ticks)
        {
            if (ticks <= 0 || stationModels.Count == 0 || IsActiveClientOnly())
                return;

            VoxelWorld world = ResolveWorldOrNull();
            staleStationPositions.Clear();

            foreach (KeyValuePair<BlockPosition, SmeltingStationModel> pair in stationModels)
            {
                if (world != null &&
                    (!world.Bounds.Contains(pair.Key) ||
                     !StationProximity.TryGetStationForBlock(world.GetBlock(pair.Key), out CraftingStation blockStation) ||
                     blockStation != pair.Value.StationType))
                {
                    staleStationPositions.Add(pair.Key);
                    continue;
                }

                SmeltingStationModel model = pair.Value;
                bool wasActive = model.IsActive;
                int previousOutputCount = model.Output.Count;
                int previousFuelCount = model.Fuel.Count;

                model.Tick(ticks);

                if (model.IsActive != wasActive ||
                    model.Output.Count != previousOutputCount ||
                    model.Fuel.Count != previousFuelCount)
                {
                    BroadcastStationSnapshot(pair.Key);
                }
            }

            foreach (BlockPosition stale in staleStationPositions)
                stationModels.Remove(stale);
        }

        // Requests the current station state from the host so a freshly opened panel mirrors it.
        // On the host/offline this just validates the block still is the station.
        public SurvivalCommandResult TrySubmitStationOpen(BlockPosition position, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationOpen, position, ItemId.None, 0, out requestSentToHost);

        // Deposits items from the requesting player's inventory into a station input slot.
        public SurvivalCommandResult TrySubmitStationDepositInput(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationDepositInput, position, itemId, count, out requestSentToHost);

        // Deposits fuel from the requesting player's inventory into the station fuel slot.
        public SurvivalCommandResult TrySubmitStationDepositFuel(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationDepositFuel, position, itemId, count, out requestSentToHost);

        // Collects the station's accumulated output into the requesting player's inventory.
        public SurvivalCommandResult TrySubmitStationCollect(BlockPosition position, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationCollectOutput, position, ItemId.None, 0, out requestSentToHost);

        SurvivalCommandResult TrySubmitStationCommand(
            SurvivalCommandKind commandKind,
            BlockPosition position,
            ItemId itemId,
            int count,
            out bool requestSentToHost)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                if (chunkAuthoritySync != null && !chunkAuthoritySync.HasHostGenerationSnapshotForSession)
                {
                    LastCommandResult = SurvivalCommandResult.Reject(
                        commandKind,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendStationCommandRequest(requestId, commandKind, position, itemId, count);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(commandKind, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostStationCommand(
                    ResolveLocalClientId(), requestId: 0, commandKind, position, itemId, count, sendResponse: false),
                position);
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

            return CompleteLocalCommand(
                ProcessHostCrateTransfer(
                    ResolveLocalClientId(),
                    requestId: 0,
                    commandKind,
                    itemId,
                    count,
                    sendResponse: false));
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

            // A container's contents go to the player who broke it — host-authoritatively into
            // that client's inventory — not to whichever peer's auto-loot handler fires first.
            // Auto-loot is suppressed around the mutation so the world-change handler does not
            // also grant leftovers to the host's own player. Best effort: contents that do not
            // fit are destroyed with the block, matching the local break-to-loot behavior.
            bool restoreAutoLoot = false;
            if (worldManager != null &&
                worldManager.ContainerStore != null &&
                worldManager.ContainerStore.Contains(position))
            {
                worldManager.TryLootContainerInto(position, inventory);
                restoreAutoLoot = !worldManager.SuppressContainerAutoLoot;
                worldManager.SuppressContainerAutoLoot = true;
            }

            BlockMutationResult mutation;
            try
            {
                mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                    new BlockMutationRequest(clientId, position, BlockRegistry.Air, harvest.BlockId),
                    out _,
                    out _);
            }
            finally
            {
                if (restoreAutoLoot)
                    worldManager.SuppressContainerAutoLoot = false;
            }

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

            // Apply the rule's drop table so tool-action bonuses (Sickle double-roll, Carver full
            // yield) take effect on the authoritative path, not only in the local TryHarvest helper.
            // Capacity for the maximum was already validated by TryPreviewHarvest above.
            ItemStack drop = ResolveHarvestService().RollHarvestDrop(harvest.BlockId, harvest.UsedTool);
            inventory.TryAddAll(drop);

            // §6.3 durability: cost derives from block category/tier and tool correctness — not a
            // flat 1 — so the authoritative path charges the same as the local TryHarvest helper.
            if (equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount)
            {
                ItemStack serverSlot = inventory.GetSlot(equippedSlotIndex);
                if (!serverSlot.IsEmpty && serverSlot.Durability > 0)
                    ApplyToolDurability(inventory, equippedSlotIndex, ResolveHarvestService().GetHarvestDurabilityCost(harvest, serverSlot));
            }

            AcceptedHarvestCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.HarvestResource,
                requestId,
                drop);
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

        SurvivalCommandResult ProcessHostStripLog(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedStripLogRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.StripLog, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            Inventory inventory = GetInventory(clientId);

            // The held tool is read from the host-owned slot, never trusted from the client: a Feller is
            // required, and the target must be a branchwood_log.
            ItemStack held = equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount
                ? inventory.GetSlot(equippedSlotIndex)
                : ItemStack.Empty;

            bool heldIsFeller = !held.IsEmpty &&
                itemRegistry.TryGet(held.ItemId, out ItemDefinition def) &&
                def.ToolClass == HarvestToolKind.Feller;

            if (!heldIsFeller || ResolveWorld().GetBlock(position) != BlockRegistry.BranchwoodLog)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.StripLog,
                    SurvivalCommandFailureReason.NotStrippable,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, BlockRegistry.SmoothBranchwood, BlockRegistry.BranchwoodLog),
                out _,
                out _);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.StripLog,
                    SurvivalCommandFailureReason.StripRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            // Strip-log costs Feller durability (no item drop — the block converts in place).
            // §6.3 formula cost for a correct-tool non-resource action is 1.
            ApplyToolDurability(inventory, equippedSlotIndex, cost: 1);

            AcceptedStripLogCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(SurvivalCommandKind.StripLog, requestId);
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostTill(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedTillRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.TillSoil, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            Inventory inventory = GetInventory(clientId);

            // The held tool is read from the host-owned slot, never trusted from the client: a Tiller
            // is required, and the target must be tillable soil (§11.1).
            ItemStack held = equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount
                ? inventory.GetSlot(equippedSlotIndex)
                : ItemStack.Empty;

            bool heldIsTiller = !held.IsEmpty &&
                itemRegistry.TryGet(held.ItemId, out ItemDefinition def) &&
                def.ToolClass == HarvestToolKind.Tiller;

            VoxelWorld world = ResolveWorld();
            if (!heldIsTiller ||
                !world.Bounds.Contains(position) ||
                !FarmingService.IsTillableBlock(world.GetBlock(position)))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.TillSoil,
                    SurvivalCommandFailureReason.NotTillable,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, BlockRegistry.TendedSoil, world.GetBlock(position)),
                out _,
                out _);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.TillSoil,
                    SurvivalCommandFailureReason.TillRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            // Tilling costs Tiller durability (the soil converts in place, no drop).
            ApplyToolDurability(inventory, equippedSlotIndex, cost: 1);

            AcceptedTillCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(SurvivalCommandKind.TillSoil, requestId);
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostPlantSeed(
            ulong clientId,
            uint requestId,
            BlockPosition soilPosition,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedPlantRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.PlantSeed, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            Inventory inventory = GetInventory(clientId);

            // The planted crop is derived from the host-owned inventory slot, never trusted from the
            // client. Planting requires a seed item, tended soil at the target, and air above it.
            ItemStack held = equippedSlotIndex >= 0 && equippedSlotIndex < inventory.SlotCount
                ? inventory.GetSlot(equippedSlotIndex)
                : ItemStack.Empty;

            VoxelWorld world = ResolveWorld();
            var cropPosition = new BlockPosition(soilPosition.X, soilPosition.Y + 1, soilPosition.Z);

            if (held.IsEmpty ||
                !FarmingService.TryGetCropForSeed(held.ItemId, out BlockId cropKind) ||
                !world.Bounds.Contains(soilPosition) ||
                world.GetBlock(soilPosition) != BlockRegistry.TendedSoil ||
                !world.Bounds.Contains(cropPosition) ||
                world.GetBlock(cropPosition) != BlockRegistry.Air)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PlantSeed,
                    SurvivalCommandFailureReason.NotPlantable,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, cropPosition, cropKind, BlockRegistry.Air),
                out _,
                out _);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PlantSeed,
                    SurvivalCommandFailureReason.PlantRejected,
                    requestId,
                    new ItemStack(held.ItemId, 1));
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.Remove(held.ItemId, 1);

            AcceptedPlantCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.PlantSeed,
                requestId,
                new ItemStack(held.ItemId, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostRepair(
            ulong clientId,
            uint requestId,
            int toolSlotIndex,
            bool sendResponse)
        {
            ReceivedRepairRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.RepairTool, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            // Host-side proximity check (§8): repair requires standing at a Mend Bench; see
            // ResolveValidatedStationClaim for the trust rules.
            CraftingStation station = ResolveValidatedStationClaim(clientId, CraftingStation.MendBench);

            Inventory inventory = GetInventory(clientId);
            RepairResult repair = MendBenchRepair.TryRepair(ResolveItemRegistry(), inventory, toolSlotIndex, station);

            if (!repair.Succeeded)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.RepairTool,
                    SurvivalCommandFailureReason.RepairRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            AcceptedRepairCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.RepairTool,
                requestId,
                new ItemStack(repair.MaterialUsed, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostUseConsumable(
            ulong clientId,
            uint requestId,
            int slotIndex,
            bool sendResponse)
        {
            ReceivedConsumableRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.UseConsumable, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            // Server-authoritative: the consumed item is read from the host-owned inventory slot,
            // never trusted from the client; only ItemKind.Consumable items can be used.
            Inventory inventory = GetInventory(clientId);
            ItemStack stack = slotIndex >= 0 && slotIndex < inventory.SlotCount
                ? inventory.GetSlot(slotIndex)
                : ItemStack.Empty;

            if (stack.IsEmpty ||
                !ResolveItemRegistry().TryGet(stack.ItemId, out ItemDefinition definition) ||
                definition.Kind != ItemKind.Consumable)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.UseConsumable,
                    SurvivalCommandFailureReason.NotConsumable,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.SetSlot(slotIndex, stack.Count > 1 ? stack.WithCount(stack.Count - 1) : ItemStack.Empty);

            AcceptedConsumableCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.UseConsumable,
                requestId,
                new ItemStack(stack.ItemId, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        // Applies a durability cost to the host-owned slot; the tool breaks (slot empties) at 0.
        // No-op for empty slots and durability-less items (stackables report Durability 0).
        void ApplyToolDurability(Inventory inventory, int slotIndex, int cost)
        {
            if (slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return;

            ItemStack slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty || slot.Durability <= 0)
                return;

            int remaining = slot.Durability - Math.Max(1, cost);
            inventory.SetSlot(slotIndex, remaining > 0
                ? slot.WithDurability(remaining)
                : ItemStack.Empty);
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

            // Host-side proximity check (§8): a client cannot claim a crafting station it is not
            // standing near. Invalid claims downgrade to None so CraftingService rejects with
            // MissingStation.
            availableStation = ResolveValidatedStationClaim(clientId, availableStation);

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
            // Broadcast skips the host's own client id; raise the local signal directly
            // (and offline, where there is no session to broadcast through at all).
            SharedCrateChanged?.Invoke();
            SendCommandResult(clientId, result, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = result;
            return result;
        }

        SurvivalCommandResult ProcessHostStationCommand(
            ulong clientId,
            uint requestId,
            SurvivalCommandKind commandKind,
            BlockPosition position,
            ItemId itemId,
            int count,
            bool sendResponse)
        {
            ReceivedStationCommandRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, commandKind, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            // The station is derived from the world block at the requested position, never trusted
            // from the client: only a live kiln/forge block has a runtime station model.
            VoxelWorld world = ResolveWorldOrNull();
            if (world == null ||
                !world.Bounds.Contains(position) ||
                !StationProximity.TryGetStationForBlock(world.GetBlock(position), out CraftingStation stationType) ||
                !SmeltingStationModel.IsTimedStation(stationType))
            {
                var invalidResult = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.NotAStation, requestId);
                SendCommandFailure(clientId, invalidResult, sendResponse);
                return invalidResult;
            }

            // Proximity check: the requester must be near the station block, same trust rules as
            // the crafting/repair paths (§8).
            if (ResolveValidatedStationClaim(clientId, stationType) != stationType)
            {
                var tooFarResult = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.NotAStation, requestId);
                SendCommandFailure(clientId, tooFarResult, sendResponse);
                return tooFarResult;
            }

            SmeltingStationModel station = GetOrCreateStationModel(position, stationType);
            Inventory inventory = GetInventory(clientId);
            SurvivalCommandResult result;

            switch (commandKind)
            {
                case SurvivalCommandKind.StationOpen:
                    result = SurvivalCommandResult.Accept(commandKind, requestId);
                    break;

                case SurvivalCommandKind.StationDepositInput:
                case SurvivalCommandKind.StationDepositFuel:
                {
                    if (!IsValidTransferItem(itemId) || count <= 0 || inventory.CountOf(itemId) < count)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    var stack = new ItemStack(itemId, count);
                    bool deposited = commandKind == SurvivalCommandKind.StationDepositInput
                        ? station.TryDepositInput(stack)
                        : station.TryDepositFuel(stack);

                    if (!deposited)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.StationRejected, requestId, stack);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    inventory.Remove(itemId, count);
                    result = SurvivalCommandResult.Accept(commandKind, requestId, stack);
                    break;
                }

                case SurvivalCommandKind.StationCollectOutput:
                {
                    ItemStack output = station.Output;
                    if (output.IsEmpty)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.StationRejected, requestId);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    if (inventory.GetAvailableCapacity(output.ItemId) < output.Count)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InventoryFull, requestId, output);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    inventory.TryAddAll(station.CollectOutput());
                    result = SurvivalCommandResult.Accept(commandKind, requestId, output);
                    break;
                }

                default:
                {
                    result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }
            }

            AcceptedStationCommandCount++;
            SendInventorySnapshot(clientId);
            // StationOpen is read-only: only the requester's mirror needs refreshing. Mutating
            // commands (deposit/collect) change state every viewing client must see, so broadcast.
            if (commandKind == SurvivalCommandKind.StationOpen)
                SendStationSnapshot(clientId, position);
            else
                BroadcastStationSnapshot(position);
            SendCommandResult(clientId, result, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = result;
            return result;
        }

        bool IsValidTransferItem(ItemId itemId)
        {
            return itemId != ItemId.None && ResolveItemRegistry().TryGet(itemId, out _);
        }

        // Host-side proximity trust policy (§8), shared by the craft/repair/station paths: a
        // claimed station is downgraded to None when the requester's position resolves and no
        // matching station block is within reach. When the position cannot be resolved
        // (offline/tests with no spawned player object), the claim is trusted — the local UI
        // already gates by an actual proximity scan, and remote clients always have a player object.
        CraftingStation ResolveValidatedStationClaim(ulong clientId, CraftingStation claimed)
        {
            if (claimed == CraftingStation.None)
                return claimed;

            VoxelWorld world = ResolveWorldOrNull();
            if (world != null &&
                TryResolveClientBlockPosition(clientId, out BlockPosition requesterPosition) &&
                !StationProximity.ValidateClaim(world, requesterPosition, claimed))
            {
                return CraftingStation.None;
            }

            return claimed;
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

            ProcessedRequestWindow processedRequests = GetProcessedRequests(clientId);
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

        // Single host-side dispatcher for every client→host command. The shared header
        // [requestId][kind] is read here; each kind then reads its own payload and routes to
        // the existing ProcessHost* logic. An unknown kind drops the message, the same posture
        // an unregistered named message had under the old per-command channels.
        void HandleCommandRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out uint requestId);
            reader.ReadValueSafe(out int kindValue);
            var commandKind = (SurvivalCommandKind)kindValue;

            switch (commandKind)
            {
                case SurvivalCommandKind.HarvestResource:
                {
                    // The wire carries only position + slot index: the host resolves the tool
                    // from its own copy of the requester's inventory (ResolveAuthoritativeTool),
                    // so a client-supplied stack would be ignored anyway.
                    BlockPosition position = ReadBlockPosition(ref reader);
                    reader.ReadValueSafe(out int equippedSlotIndex);
                    ProcessHostHarvest(senderClientId, requestId, position, ItemStack.Empty, sendResponse: true, equippedSlotIndex);
                    break;
                }
                case SurvivalCommandKind.PlaceBlock:
                case SurvivalCommandKind.StripLog:
                case SurvivalCommandKind.TillSoil:
                case SurvivalCommandKind.PlantSeed:
                {
                    BlockPosition position = ReadBlockPosition(ref reader);
                    reader.ReadValueSafe(out int equippedSlotIndex);

                    if (commandKind == SurvivalCommandKind.PlaceBlock)
                        ProcessHostPlace(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else if (commandKind == SurvivalCommandKind.StripLog)
                        ProcessHostStripLog(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else if (commandKind == SurvivalCommandKind.TillSoil)
                        ProcessHostTill(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else
                        ProcessHostPlantSeed(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.RepairTool:
                {
                    reader.ReadValueSafe(out int toolSlotIndex);
                    ProcessHostRepair(senderClientId, requestId, toolSlotIndex, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.UseConsumable:
                {
                    reader.ReadValueSafe(out int slotIndex);
                    ProcessHostUseConsumable(senderClientId, requestId, slotIndex, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.CraftRecipe:
                {
                    reader.ReadValueSafe(out string outputItemId);
                    reader.ReadValueSafe(out int availableStation);
                    // An empty output id would throw in the ItemId constructor; route it through
                    // as None so ProcessHostCraft rejects it with MissingRecipe instead.
                    ItemId outputId = string.IsNullOrWhiteSpace(outputItemId)
                        ? ItemId.None
                        : new ItemId(outputItemId);
                    ProcessHostCraft(
                        senderClientId,
                        requestId,
                        outputId,
                        (CraftingStation)availableStation,
                        sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.SharedCrateDeposit:
                case SurvivalCommandKind.SharedCrateWithdraw:
                {
                    ItemStack stack = ReadItemStack(ref reader);
                    ProcessHostCrateTransfer(senderClientId, requestId, commandKind, stack.ItemId, stack.Count, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.StationOpen:
                case SurvivalCommandKind.StationDepositInput:
                case SurvivalCommandKind.StationDepositFuel:
                case SurvivalCommandKind.StationCollectOutput:
                {
                    BlockPosition position = ReadBlockPosition(ref reader);
                    ItemStack stack = ReadItemStack(ref reader);
                    ProcessHostStationCommand(senderClientId, requestId, commandKind, position, stack.ItemId, stack.Count, sendResponse: true);
                    break;
                }
            }
        }

        delegate void CommandPayloadWriter(ref FastBufferWriter writer);

        // Shared client→host command sender: registers the request as pending (with its block
        // target for feedback), frames the message with the shared [requestId][kind] header, and
        // lets the caller append the command-specific payload.
        void SendCommandRequest(SurvivalCommandKind commandKind, uint requestId, CommandPayloadWriter writePayload, BlockPosition position = default)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            RegisterMessageHandlers();
            pendingCommandRequests[requestId] = (commandKind, position);
            LastSentCommandRequestId = requestId;
            var writer = new FastBufferWriter(CommandRequestMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(requestId);
                writer.WriteValueSafe((int)commandKind);
                writePayload(ref writer);
                networkManager.CustomMessagingManager.SendNamedMessage(CommandRequestMessage, NetworkManager.ServerClientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        // Place, strip-log, till, and plant all share the (position, slot index) payload.
        void SendBlockCommandRequest(SurvivalCommandKind commandKind, uint requestId, BlockPosition position, int slotIndex) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) =>
            {
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(slotIndex);
            }, position);

        // Repair and consumable requests carry only a slot index.
        void SendSlotCommandRequest(SurvivalCommandKind commandKind, uint requestId, int slotIndex) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) => writer.WriteValueSafe(slotIndex));

        void HandleCommandResultMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            SurvivalCommandResult result = ReadCommandResult(ref reader);
            LastCommandResult = result;
            bool completedPending = TryCompletePendingCommandRequest(
                result.RequestId,
                out (SurvivalCommandKind kind, BlockPosition position) pending);

            // The consuming peer applies the consumable's vitals effect once the host confirms.
            if (result.Accepted && result.CommandKind == SurvivalCommandKind.UseConsumable)
                ConsumableConsumed?.Invoke(result.Item);

            if (completedPending && !result.IsDuplicate)
                RaiseCommandFeedback(result, pending.position);
        }

        void HandleInventorySnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            ApplyInventorySnapshot(LocalInventory, ref reader);
            ReceivedInventorySnapshotCount++;
            LocalInventoryChanged?.Invoke();
        }

        void HandleSharedCrateSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            ApplyInventorySnapshot(SharedCrateInventory, ref reader);
            ReceivedSharedCrateSnapshotCount++;
            SharedCrateChanged?.Invoke();
        }

        void SendHarvestRequest(uint requestId, BlockPosition position, int equippedSlotIndex = -1) =>
            SendCommandRequest(SurvivalCommandKind.HarvestResource, requestId, (ref FastBufferWriter writer) =>
            {
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(equippedSlotIndex);
            }, position);

        void SendCraftRequest(uint requestId, ItemId outputItemId, CraftingStation availableStation) =>
            SendCommandRequest(SurvivalCommandKind.CraftRecipe, requestId, (ref FastBufferWriter writer) =>
            {
                writer.WriteValueSafe(outputItemId.Value);
                writer.WriteValueSafe((int)availableStation);
            });

        void SendCrateTransferRequest(
            uint requestId,
            SurvivalCommandKind commandKind,
            ItemId itemId,
            int count) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) =>
                WriteItemStack(ref writer, new ItemStack(itemId, count)));

        void SendStationCommandRequest(
            uint requestId,
            SurvivalCommandKind commandKind,
            BlockPosition position,
            ItemId itemId,
            int count) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) =>
            {
                WriteBlockPosition(ref writer, position);
                WriteItemStack(ref writer, count > 0 ? new ItemStack(itemId, count) : ItemStack.Empty);
            }, position);

        // Mirrors a station's authoritative state to one remote client.
        void SendStationSnapshot(ulong clientId, BlockPosition position)
        {
            if (!stationModels.TryGetValue(position, out SmeltingStationModel station))
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer ||
                clientId == networkManager.LocalClientId)
            {
                return;
            }

            RegisterMessageHandlers();
            var writer = new FastBufferWriter(StationSnapshotMessageBytes, Allocator.Temp);

            try
            {
                WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe((int)station.StationType);
                writer.WriteValueSafe(station.InputSlotCount);
                for (int slot = 0; slot < station.InputSlotCount; slot++)
                    WriteItemStack(ref writer, station.GetInput(slot));
                WriteItemStack(ref writer, station.Fuel);
                WriteItemStack(ref writer, station.Output);
                writer.WriteValueSafe(station.IsActive);
                // Intentionally the ItemId string, not a recipe index: every wire payload in this
                // protocol identifies items by canonical ItemId (see WriteItemStack), and the
                // reader resolves it via CraftingRecipeBook.TryGetByOutput (O(1)). A recipe index
                // would be brittle across recipe-book reordering for no measurable size win at
                // snapshot cadence.
                writer.WriteValueSafe(station.IsActive ? station.ActiveRecipe.Output.ItemId.Value : string.Empty);
                writer.WriteValueSafe(station.ProgressTicks);
                networkManager.CustomMessagingManager.SendNamedMessage(StationSnapshotMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void BroadcastStationSnapshot(BlockPosition position)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer)
            {
                return;
            }

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
                SendStationSnapshot(clientId, position);
        }

        void HandleStationSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            BlockPosition position = ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int stationTypeValue);
            reader.ReadValueSafe(out int inputSlotCount);
            int inputCount = Mathf.Max(0, inputSlotCount);
            if (stationSnapshotInputs.Length < inputCount)
                stationSnapshotInputs = new ItemStack[inputCount];
            for (int slot = 0; slot < inputCount; slot++)
                stationSnapshotInputs[slot] = ReadItemStack(ref reader);
            // Stale scratch entries past this snapshot's count must read as empty:
            // ApplyHostSnapshot consumes the array up to the mirror's own slot count.
            for (int slot = inputCount; slot < stationSnapshotInputs.Length; slot++)
                stationSnapshotInputs[slot] = ItemStack.Empty;
            ItemStack fuel = ReadItemStack(ref reader);
            ItemStack output = ReadItemStack(ref reader);
            reader.ReadValueSafe(out bool isActive);
            reader.ReadValueSafe(out string activeOutputItemId);
            reader.ReadValueSafe(out int progressTicks);

            var stationType = (CraftingStation)stationTypeValue;
            if (!SmeltingStationModel.IsTimedStation(stationType))
                return;

            SmeltingStationModel mirror = GetOrCreateStationModel(position, stationType);
            // A malformed snapshot (active with an empty recipe id) must not throw inside the
            // message pump — the ItemId constructor rejects empty ids — so degrade to no active
            // recipe, matching the CraftRecipe request guard.
            CraftingRecipe activeRecipe = isActive && !string.IsNullOrWhiteSpace(activeOutputItemId)
                ? FindStationRecipeByOutput(stationType, new ItemId(activeOutputItemId))
                : null;
            mirror.ApplyHostSnapshot(stationSnapshotInputs, fuel, output, activeRecipe, progressTicks);
            ReceivedStationSnapshotCount++;
        }

        // Resolves the recipe a host snapshot says is in progress; both peers build the same
        // canonical recipe book, so output id + station identify the recipe. Outputs are unique in
        // the book (Register throws on duplicates), so the indexed lookup plus a station/timed
        // check is equivalent to scanning.
        CraftingRecipe FindStationRecipeByOutput(CraftingStation stationType, ItemId outputItemId)
        {
            if (ResolveRecipeBook().TryGetByOutput(outputItemId, out CraftingRecipe recipe) &&
                recipe.RequiredStation == stationType && recipe.TimeTicks > 0)
            {
                return recipe;
            }

            return null;
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
            // The local player's inventory mutates in place (one shared instance) — no wire
            // round-trip; just signal local consumers (HUD) to repaint. Works offline too.
            if (clientId == ResolveLocalClientId())
            {
                LocalInventoryChanged?.Invoke();
                return;
            }

            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer)
            {
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

        bool TryCompletePendingCommandRequest(
            uint requestId,
            out (SurvivalCommandKind kind, BlockPosition position) pending)
        {
            pending = default;

            if (requestId == 0 || !pendingCommandRequests.TryGetValue(requestId, out pending))
                return false;

            pendingCommandRequests.Remove(requestId);
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
                // Clear the stable instances IN PLACE instead of swapping references: the HUD,
                // auto-loot target, and snapshot mirror keep pointing at the same objects, which
                // the host's first snapshots then repopulate.
                ResetPendingCommands();
                ClearInventorySlots(LocalInventory);
                ClearInventorySlots(SharedCrateInventory);
                LocalInventoryChanged?.Invoke();
                SharedCrateChanged?.Invoke();
            }

            RefreshLocalInventoryReference();
        }

        static void ClearInventorySlots(Inventory inventory)
        {
            for (int index = 0; index < inventory.SlotCount; index++)
                inventory.ClearSlot(index);
        }

        void HandleClientConnected(ulong clientId)
        {
            if (!CanProcessHostRequests())
                return;

            GetInventory(clientId);
            SendInventorySnapshot(clientId);
            SendSharedCrateSnapshot(clientId);
        }

        // Mirrors Configure's per-session clear set so a stopped session cannot leak state into
        // a later one started without a fresh Configure: stale station models would tick against
        // the new world, and the old session's inventories/duplicate windows are dead. The stable
        // localInventory instance survives and is re-bound to the current local client id.
        void ClearSessionState()
        {
            inventoriesByClientId.Clear();
            processedRequestsByClientId.Clear();
            stationModels.Clear();
            stationTickRemainder = 0.0f;
            ResetPendingCommands();
            RefreshLocalInventoryReference();
        }

        void HandleServerStopped(bool wasHost)
        {
            ClearSessionState();
            UnregisterMessageHandlers();
        }

        void HandleClientStopped(bool wasHost)
        {
            ClearSessionState();
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
            subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            subscribedNetworkManager.OnServerStopped += HandleServerStopped;
            subscribedNetworkManager.OnClientStopped += HandleClientStopped;
            RegisterMessageHandlers();
        }

        // Caps the reconnect-identity inventory retention below: past this many tracked
        // inventories, disconnected clients' entries are evicted on the next disconnect.
        const int MaxRetainedInventories = 32;

        // Per-connection host state is released when a client leaves. Inventories are
        // intentionally retained for the session (a future reconnect-identity feature can hand a
        // returning player their items back); the duplicate window is purely connection-scoped.
        void HandleClientDisconnected(ulong clientId)
        {
            if (!CanProcessHostRequests() || clientId == ResolveLocalClientId())
                return;

            processedRequestsByClientId.Remove(clientId);

            // The intentional retention must still be bounded across join/leave churn: past the
            // cap, evict inventories of clients no longer connected (arbitrary order — the
            // dictionary keeps no join ordering to prefer).
            if (inventoriesByClientId.Count <= MaxRetainedInventories)
                return;

            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            var evictableClientIds = new List<ulong>();
            foreach (KeyValuePair<ulong, Inventory> pair in inventoriesByClientId)
            {
                bool connected = pair.Key != clientId &&
                    networkManager != null &&
                    networkManager.ConnectedClients.ContainsKey(pair.Key);
                if (!connected && !ReferenceEquals(pair.Value, localInventory))
                    evictableClientIds.Add(pair.Key);
            }

            foreach (ulong evictableId in evictableClientIds)
            {
                if (inventoriesByClientId.Count <= MaxRetainedInventories)
                    break;

                inventoriesByClientId.Remove(evictableId);
            }
        }

        void UnsubscribeNetworkCallbacks()
        {
            UnregisterMessageHandlers();

            if (subscribedNetworkManager == null)
                return;

            subscribedNetworkManager.OnServerStarted -= HandleServerStarted;
            subscribedNetworkManager.OnClientStarted -= HandleClientStarted;
            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
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

            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CommandRequestMessage, HandleCommandRequestMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StationSnapshotMessage, HandleStationSnapshotMessage);
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

            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CommandRequestMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StationSnapshotMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CommandResultMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(InventorySnapshotMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SharedCrateSnapshotMessage);
            messagesRegistered = false;
        }

        // True while Awake/OnEnable/Configure run their expected wiring pass; the scene-scan
        // fallback below is silent there but surfaced anywhere else (e.g. mid-command).
        bool inLifecycleResolve;

        // Scene-scan fallback for a missing worldManager reference. Expected during the
        // Awake/OnEnable/Configure wiring window; firing outside it during play means a command
        // ran before wiring completed, so surface the silent degradation instead of hiding it.
        CreativeWorldManager FindWorldManagerFallback()
        {
            if (Application.isPlaying && !inLifecycleResolve)
                Debug.LogWarning("MultiplayerSurvivalSync fell back to a CreativeWorldManager scene scan outside Awake/OnEnable/Configure; wire it via Configure or the inspector.");

            return FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
        }

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();

            if (worldManager == null)
                worldManager = FindWorldManagerFallback();

            itemRegistry ??= ItemRegistry.CreateDefault();
            recipeBook ??= CraftingRecipeBook.CreateDefault(itemRegistry);
            harvestService ??= new ResourceHarvestService(
                BlockRegistry.CreateDefault(),
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
        }

        void RefreshLocalInventoryReference()
        {
            RebindLocalInventoryMapping();
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
            return ResolveWorldOrNull() ?? throw new InvalidOperationException("Multiplayer survival sync requires a voxel world.");
        }

        VoxelWorld ResolveWorldOrNull()
        {
            if (worldManager == null)
                worldManager = FindWorldManagerFallback();

            return worldManager != null ? worldManager.World : null;
        }

        // Resolves the authoritative world position of a client's player for proximity checks.
        // Prefers the synced head anchor from the avatar rig: XR locomotion moves the headset
        // independently of the NetworkObject root, so the root can lag at spawn while the player
        // has walked to a station. Falls back to the root if no rig/anchor is present, and to the
        // local camera for host-local commands without a network session.
        bool TryResolveClientBlockPosition(ulong clientId, out BlockPosition position)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                BlockiverseNetworkAvatarRig avatarRig = client.PlayerObject.GetComponent<BlockiverseNetworkAvatarRig>();
                Transform headTransform = avatarRig?.HeadAnchor != null ? avatarRig.HeadAnchor : client.PlayerObject.transform;
                position = CreativeInteractionController.ToBlockPosition(headTransform.position);
                return true;
            }

            if (clientId == ResolveLocalClientId() && Camera.main != null)
            {
                position = CreativeInteractionController.ToBlockPosition(Camera.main.transform.position);
                return true;
            }

            position = default;
            return false;
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

        ProcessedRequestWindow GetProcessedRequests(ulong clientId)
        {
            if (!processedRequestsByClientId.TryGetValue(clientId, out ProcessedRequestWindow processedRequests))
            {
                processedRequests = new ProcessedRequestWindow();
                processedRequestsByClientId.Add(clientId, processedRequests);
            }

            return processedRequests;
        }

        // Bounded duplicate-detection window. Request ids are allocated monotonically per client,
        // so retransmits always reference recent ids; the oldest entries can be dropped to keep
        // memory bounded over long sessions instead of growing one HashSet entry per command.
        sealed class ProcessedRequestWindow
        {
            const int MaxTrackedRequests = 1024;

            readonly HashSet<uint> ids = new();
            readonly Queue<uint> order = new();

            // Returns false when the id was already processed (a duplicate).
            public bool Add(uint requestId)
            {
                if (!ids.Add(requestId))
                    return false;

                order.Enqueue(requestId);
                if (order.Count > MaxTrackedRequests)
                    ids.Remove(order.Dequeue());

                return true;
            }
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
            {
                // A malformed or version-mismatched snapshot must not crash the message pump;
                // drop it and keep the local mirror unchanged — the next accepted command's
                // snapshot resynchronizes the inventory.
                Debug.LogWarning(
                    $"Ignoring inventory snapshot with mismatched shape: got {slotCount}/{hotbarSlotCount} slots/hotbar, expected {inventory.SlotCount}/{inventory.HotbarSlotCount}.");
                return;
            }

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
            // A malformed payload (positive count with an empty id) must not throw inside the
            // message pump — the ItemId constructor rejects empty ids, so degrade to Empty.
            if (count <= 0 || string.IsNullOrWhiteSpace(itemId)) return ItemStack.Empty;
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
