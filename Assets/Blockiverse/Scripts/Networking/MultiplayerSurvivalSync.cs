using System;
using System.Collections.Generic;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Networking
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
        ContainerOpen,
        StationDepositInput,
        StationDepositFuel,
        StationCollectOutput,
        StationWithdrawInput,
        StationWithdrawFuel,
        UseConsumable,
        FillBucket,
        PourBucket,
        DeathDropInventory
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
        TillRequiresWater,
        NotPlantable,
        PlantRejected,
        RepairRejected,
        NotAStation,
        StationRejected,
        NotConsumable,
        NotBucketUse,
        BucketRejected,
        GameModeRejected,
        OutOfReach
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

    public readonly struct SurvivalSyncDiagnostics
    {
        public SurvivalSyncDiagnostics(MultiplayerSurvivalSync sync)
        {
            ReceivedHarvestRequestCount = sync.ReceivedHarvestRequestCount;
            ReceivedPlaceRequestCount = sync.ReceivedPlaceRequestCount;
            AcceptedPlaceCount = sync.AcceptedPlaceCount;
            ReceivedStripLogRequestCount = sync.ReceivedStripLogRequestCount;
            AcceptedStripLogCount = sync.AcceptedStripLogCount;
            ReceivedTillRequestCount = sync.ReceivedTillRequestCount;
            AcceptedTillCount = sync.AcceptedTillCount;
            ReceivedPlantRequestCount = sync.ReceivedPlantRequestCount;
            AcceptedPlantCount = sync.AcceptedPlantCount;
            ReceivedRepairRequestCount = sync.ReceivedRepairRequestCount;
            AcceptedRepairCount = sync.AcceptedRepairCount;
            ReceivedConsumableRequestCount = sync.ReceivedConsumableRequestCount;
            AcceptedConsumableCount = sync.AcceptedConsumableCount;
            ReceivedBucketRequestCount = sync.ReceivedBucketRequestCount;
            AcceptedBucketCount = sync.AcceptedBucketCount;
            ReceivedStationCommandRequestCount = sync.ReceivedStationCommandRequestCount;
            AcceptedStationCommandCount = sync.AcceptedStationCommandCount;
            ReceivedStationSnapshotCount = sync.ReceivedStationSnapshotCount;
            ReceivedCraftRequestCount = sync.ReceivedCraftRequestCount;
            ReceivedCrateTransferRequestCount = sync.ReceivedCrateTransferRequestCount;
            AcceptedHarvestCount = sync.AcceptedHarvestCount;
            AcceptedCraftCount = sync.AcceptedCraftCount;
            AcceptedCrateTransferCount = sync.AcceptedCrateTransferCount;
            ReceivedDeathDropRequestCount = sync.ReceivedDeathDropRequestCount;
            AcceptedDeathDropCount = sync.AcceptedDeathDropCount;
            RejectedCommandCount = sync.RejectedCommandCount;
            RateLimitedCommandRequestCount = sync.RateLimitedCommandRequestCount;
            ReceivedInventorySnapshotCount = sync.ReceivedInventorySnapshotCount;
            ReceivedSharedCrateSnapshotCount = sync.ReceivedSharedCrateSnapshotCount;
            LastSentCommandRequestId = sync.LastSentCommandRequestId;
            LastCompletedCommandRequestId = sync.LastCompletedCommandRequestId;
        }

        public int ReceivedHarvestRequestCount { get; }
        public int ReceivedPlaceRequestCount { get; }
        public int AcceptedPlaceCount { get; }
        public int ReceivedStripLogRequestCount { get; }
        public int AcceptedStripLogCount { get; }
        public int ReceivedTillRequestCount { get; }
        public int AcceptedTillCount { get; }
        public int ReceivedPlantRequestCount { get; }
        public int AcceptedPlantCount { get; }
        public int ReceivedRepairRequestCount { get; }
        public int AcceptedRepairCount { get; }
        public int ReceivedConsumableRequestCount { get; }
        public int AcceptedConsumableCount { get; }
        public int ReceivedBucketRequestCount { get; }
        public int AcceptedBucketCount { get; }
        public int ReceivedStationCommandRequestCount { get; }
        public int AcceptedStationCommandCount { get; }
        public int ReceivedStationSnapshotCount { get; }
        public int ReceivedCraftRequestCount { get; }
        public int ReceivedCrateTransferRequestCount { get; }
        public int AcceptedHarvestCount { get; }
        public int AcceptedCraftCount { get; }
        public int AcceptedCrateTransferCount { get; }
        public int ReceivedDeathDropRequestCount { get; }
        public int AcceptedDeathDropCount { get; }
        public int RejectedCommandCount { get; }
        public int RateLimitedCommandRequestCount { get; }
        public int ReceivedInventorySnapshotCount { get; }
        public int ReceivedSharedCrateSnapshotCount { get; }
        public uint LastSentCommandRequestId { get; }
        public uint LastCompletedCommandRequestId { get; }
    }

    [DisallowMultipleComponent]
    public sealed class MultiplayerSurvivalSync : MonoBehaviour
    {
        const string CommandRequestMessage = "Blockiverse.Survival.CommandRequest";
        const string StationSnapshotMessage = "Blockiverse.Survival.StationSnapshot";
        const string CommandResultMessage = "Blockiverse.Survival.CommandResult";
        const string InventorySnapshotMessage = "Blockiverse.Survival.InventorySnapshot";
        const string SharedCrateSnapshotMessage = "Blockiverse.Survival.SharedCrateSnapshot";
        const string PlayerHelloMessage = "Blockiverse.Survival.PlayerHello";
        const string PlayerCrouchStateMessage = "Blockiverse.Survival.PlayerCrouchState";
        const string PlayerGuidPrefKey = "Blockiverse.PlayerGuid";
        const string PlayerSecretPrefKey = "Blockiverse.PlayerSecret";
        const int CommandRequestMessageBytes = 128;
        const int CommandResultMessageBytes = 128;
        const int StationSnapshotMessageBytes = 512;
        const int StationRemovedSnapshotType = -1;
        const int InventorySnapshotMessageBytes = 4096;
        const int PlayerHelloMessageBytes = 192;
        const int PlayerCrouchStateMessageBytes = 1;
        static readonly NetworkDelivery InventorySnapshotDelivery = NetworkDelivery.ReliableFragmentedSequenced;
        const int SharedCrateSlotCount = 12;
        const int MaxNetworkPlayerGuidChars = 64;
        const int MaxNetworkPlayerSecretChars = 64;
        const double HostHarvestRateGraceSeconds = 0.15d;
        const int HostCommandRateLimitMaxRequests = 30;
        const double HostCommandRateLimitWindowSeconds = 1.0d;
        const float CrouchStateHeartbeatSeconds = 0.25f;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        IMultiplayerWorldContext worldManager;

        readonly Dictionary<ulong, Inventory> inventoriesByClientId = new();
        readonly Dictionary<ulong, string> playerIdentityKeysByClientId = new();
        readonly Dictionary<string, Inventory> stashedInventoriesByIdentityKey = new();
        readonly Dictionary<ulong, ProcessedRequestWindow> processedRequestsByClientId = new();
        readonly PerClientRequestRateLimiter hostCommandRateLimiter =
            new(HostCommandRateLimitMaxRequests, HostCommandRateLimitWindowSeconds);
        readonly Dictionary<uint, (SurvivalCommandKind kind, BlockPosition position)> pendingCommandRequests = new();
        readonly Dictionary<ulong, double> lastAcceptedHarvestTimeByClientId = new();
        readonly Dictionary<ulong, bool> lastKnownCrouchStateByClientId = new();

        readonly Dictionary<BlockPosition, SmeltingStationModel> stationModels = new();
        readonly List<BlockPosition> staleStationPositions = new();

        ItemStack[] stationSnapshotInputs = Array.Empty<ItemStack>();

        NetworkManager subscribedNetworkManager;
        WorldTimeClock subscribedStationClock;
        ItemRegistry itemRegistry;
        CraftingRecipeBook recipeBook;
        ResourceHarvestService harvestService;
        GroundItemStore groundItems;
        Inventory localInventory;
        Inventory sharedCrateInventory;
        Func<double> hostCommandTimeProvider;
        Func<bool> localCrouchStateProvider;
        uint nextCommandRequestId = 1;
        bool messagesRegistered;
        bool hasSentLocalCrouchState;
        bool lastSentLocalCrouchState;
        float nextCrouchStateHeartbeatTime;

        public Inventory LocalInventory => GetInventory(ResolveLocalClientId());
        public Inventory SharedCrateInventory => sharedCrateInventory ??= CreateSharedCrateInventory();
        public GroundItemStore GroundItems => groundItems ??= new GroundItemStore(itemRegistry);
        public SurvivalCommandResult LastCommandResult { get; private set; }
        public int PendingCommandRequestCount => pendingCommandRequests.Count;

        public event Action LocalInventoryChanged;
        public event Action SharedCrateChanged;

        public event Action<SurvivalCommandResult, BlockPosition> CommandFeedback;

        void RaiseCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            CommandFeedback?.Invoke(result, position);
        }

        SurvivalCommandResult CompleteLocalCommand(SurvivalCommandResult result, BlockPosition position = default)
        {
            LastCommandResult = result;
            if (!result.IsDuplicate)
                RaiseCommandFeedback(result, position);
            return result;
        }

        double HostCommandTimeSeconds => hostCommandTimeProvider?.Invoke() ?? Time.unscaledTimeAsDouble;

        readonly List<ulong> staleLocalInventoryIds = new();

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

        public void SetSelectedHotbarSlot(int slotIndex) => SelectedHotbarSlotIndex = slotIndex;

        readonly SurvivalCreativeModeSwitch modeSwitch = new();
        public PlayerModeState CurrentMode => modeSwitch.CurrentMode;
        public bool CanUseCreativeMode => worldManager != null &&
            CreativePermissionPolicy.CanUseCreativeMode(worldManager.GameMode, IsActiveClientOnly());
        public bool CanToggleMode => worldManager != null &&
            CreativePermissionPolicy.CanTogglePlayerMode(worldManager.GameMode, modeSwitch.CurrentMode, IsActiveClientOnly());

        public void ConfigureLocalCrouchStateProvider(Func<bool> crouchStateProvider)
        {
            localCrouchStateProvider = crouchStateProvider;
        }

        public bool ToggleMode()
        {
            if (!CanToggleMode)
                return false;

            if (modeSwitch.CurrentMode == PlayerModeState.Survival && !CanUseCreativeMode)
                return false;

            if (modeSwitch.CurrentMode == PlayerModeState.Survival)
                modeSwitch.SwitchToCreative(LocalInventory);
            else
                modeSwitch.SwitchToSurvival(LocalInventory);

            LocalInventoryChanged?.Invoke();
            return true;
        }

        public void SetMode(PlayerModeState mode)
        {
            if (mode == modeSwitch.CurrentMode)
                return;

            ToggleMode();
        }

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

        public IReadOnlyList<WorldSavePlayerInventory> BuildPersistedPlayerInventories()
        {
            var result = new List<WorldSavePlayerInventory>();
            var seenPlayerIds = new HashSet<string>(StringComparer.Ordinal);
            ulong localClientId = ResolveLocalClientId();

            foreach (KeyValuePair<ulong, string> pair in playerIdentityKeysByClientId)
            {
                if (pair.Key == localClientId ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    !seenPlayerIds.Add(pair.Value) ||
                    !inventoriesByClientId.TryGetValue(pair.Key, out Inventory inventory))
                {
                    continue;
                }

                result.Add(new WorldSavePlayerInventory(pair.Value, CloneInventory(inventory)));
            }

            foreach (KeyValuePair<string, Inventory> pair in stashedInventoriesByIdentityKey)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value == null ||
                    !seenPlayerIds.Add(pair.Key))
                {
                    continue;
                }

                result.Add(new WorldSavePlayerInventory(pair.Key, CloneInventory(pair.Value)));
            }

            return result;
        }

        public Inventory BuildPersistedSharedCrateInventory()
        {
            return CloneInventory(SharedCrateInventory);
        }

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

        public void RestoreSharedCrateInventory(SavedPlayerInventory savedInventory)
        {
            if (IsActiveClientOnly())
                return;

            Inventory target = SharedCrateInventory;
            ClearInventorySlots(target);

            if (savedInventory != null)
            {
                Inventory loaded = WorldSaveService.CreateInventoryFromData(
                    savedInventory,
                    itemRegistry ?? ItemRegistry.Default);
                int count = Math.Min(loaded.SlotCount, target.SlotCount);
                for (int index = 0; index < count; index++)
                {
                    ItemStack stack = loaded.GetSlot(index);
                    if (!stack.IsEmpty)
                        target.SetSlot(index, stack);
                }
            }

            SharedCrateChanged?.Invoke();
        }

        public void RestorePersistedRemoteInventories(IEnumerable<SavedMultiplayerPlayerInventory> savedPlayers)
        {
            ulong localClientId = ResolveLocalClientId();
            staleLocalInventoryIds.Clear();
            foreach (KeyValuePair<ulong, Inventory> pair in inventoriesByClientId)
            {
                if (pair.Key != localClientId)
                    staleLocalInventoryIds.Add(pair.Key);
            }

            foreach (ulong staleId in staleLocalInventoryIds)
                inventoriesByClientId.Remove(staleId);

            playerIdentityKeysByClientId.Clear();
            stashedInventoriesByIdentityKey.Clear();

            if (savedPlayers == null)
                return;

            ItemRegistry registry = itemRegistry ?? ItemRegistry.Default;
            foreach (SavedMultiplayerPlayerInventory savedPlayer in savedPlayers)
            {
                if (savedPlayer == null ||
                    string.IsNullOrWhiteSpace(savedPlayer.PlayerId) ||
                    savedPlayer.Inventory == null)
                {
                    continue;
                }

                stashedInventoriesByIdentityKey[savedPlayer.PlayerId] =
                    WorldSaveService.CreateInventoryFromData(savedPlayer.Inventory, registry);
            }
        }

        static Inventory CloneInventory(Inventory source)
        {
            var clone = new Inventory(slotCount: source.SlotCount, hotbarSlotCount: source.HotbarSlotCount);
            for (int index = 0; index < source.SlotCount; index++)
            {
                ItemStack stack = source.GetSlot(index);
                if (!stack.IsEmpty)
                    clone.SetSlot(index, stack);
            }

            return clone;
        }

        public ItemStack EquippedItem
        {
            get
            {
                Inventory inv = LocalInventory;
                return selectedHotbarSlotIndex >= 0 && selectedHotbarSlotIndex < inv.HotbarSlotCount
                    ? inv.GetSlot(selectedHotbarSlotIndex)
                    : ItemStack.Empty;
            }
        }

        internal int ReceivedHarvestRequestCount { get; private set; }
        internal int ReceivedPlaceRequestCount { get; private set; }
        internal int AcceptedPlaceCount { get; private set; }
        internal int ReceivedStripLogRequestCount { get; private set; }
        internal int AcceptedStripLogCount { get; private set; }
        internal int ReceivedTillRequestCount { get; private set; }
        internal int AcceptedTillCount { get; private set; }
        internal int ReceivedPlantRequestCount { get; private set; }
        internal int AcceptedPlantCount { get; private set; }
        internal int ReceivedRepairRequestCount { get; private set; }
        internal int AcceptedRepairCount { get; private set; }
        internal int ReceivedConsumableRequestCount { get; private set; }
        internal int AcceptedConsumableCount { get; private set; }
        internal int ReceivedBucketRequestCount { get; private set; }
        internal int AcceptedBucketCount { get; private set; }
        internal int ReceivedStationCommandRequestCount { get; private set; }
        internal int AcceptedStationCommandCount { get; private set; }
        internal int ReceivedStationSnapshotCount { get; private set; }
        internal int ReceivedCraftRequestCount { get; private set; }
        internal int ReceivedCrateTransferRequestCount { get; private set; }
        internal int AcceptedHarvestCount { get; private set; }
        internal int AcceptedCraftCount { get; private set; }
        internal int AcceptedCrateTransferCount { get; private set; }
        internal int ReceivedDeathDropRequestCount { get; private set; }
        internal int AcceptedDeathDropCount { get; private set; }
        internal int RejectedCommandCount { get; private set; }
        internal int RateLimitedCommandRequestCount { get; private set; }
        internal int ReceivedInventorySnapshotCount { get; private set; }
        internal int ReceivedSharedCrateSnapshotCount { get; private set; }
        internal uint LastSentCommandRequestId { get; private set; }
        internal uint LastCompletedCommandRequestId { get; private set; }
        public SurvivalSyncDiagnostics Diagnostics => new(this);

        public void Configure(
            BlockiverseNetworkSession targetSession,
            MultiplayerChunkAuthoritySync targetChunkAuthoritySync,
            IMultiplayerWorldContext targetWorldManager,
            ItemRegistry targetItemRegistry = null,
            CraftingRecipeBook targetRecipeBook = null)
        {
            inLifecycleResolve = true;
            UnsubscribeNetworkCallbacks();
            session = targetSession;
            chunkAuthoritySync = targetChunkAuthoritySync;
            worldManager = targetWorldManager;
            itemRegistry = targetItemRegistry ?? ItemRegistry.Default;
            recipeBook = targetRecipeBook ?? (ReferenceEquals(itemRegistry, ItemRegistry.Default) ? CraftingRecipeBook.Default : CraftingRecipeBook.CreateDefault(itemRegistry));
            harvestService = new ResourceHarvestService(
                BlockRegistry.Default,
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
            groundItems = new GroundItemStore(itemRegistry);
            inventoriesByClientId.Clear();
            playerIdentityKeysByClientId.Clear();
            stashedInventoriesByIdentityKey.Clear();
            processedRequestsByClientId.Clear();
            hostCommandRateLimiter.Clear();
            pendingCommandRequests.Clear();
            lastAcceptedHarvestTimeByClientId.Clear();
            ClearKnownCrouchState();
            stationModels.Clear();
            nextCommandRequestId = 1;
            localInventory = CreatePlayerInventory();
            sharedCrateInventory = CreateSharedCrateInventory();
            SubscribeNetworkCallbacks();
            RefreshStationClockSubscription();
            RefreshLocalInventoryReference();
            inLifecycleResolve = false;
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
            RefreshStationClockSubscription();
            RefreshLocalInventoryReference();
            inLifecycleResolve = false;
        }

        void OnDisable()
        {
            UnsubscribeNetworkCallbacks();
            UnsubscribeStationClock();
        }

        void Update()
        {
            RefreshStationClockSubscription();
            RefreshCrouchStateReplication();
        }

        void OnDestroy()
        {
            UnsubscribeNetworkCallbacks();
            UnsubscribeStationClock();
        }

        public Inventory GetInventory(ulong clientId)
        {
            if (!inventoriesByClientId.TryGetValue(clientId, out Inventory inventory))
            {
                inventory = clientId == ResolveLocalClientId()
                    ? localInventory ??= CreatePlayerInventory()
                    : CreatePlayerInventory();
                inventoriesByClientId.Add(clientId, inventory);
            }

            return inventory;
        }

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

        public SurvivalCommandResult TrySubmitHarvest(BlockPosition position, out bool requestSentToHost)
            => TrySubmitHarvest(position, EquippedItem, out requestSentToHost, SelectedHotbarSlotIndex);

        public SurvivalCommandResult TrySubmitPlace(BlockPosition position, out bool requestSentToHost)
            => TrySubmitPlace(position, out requestSentToHost, SelectedHotbarSlotIndex);

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
                SendPlaceCommandRequest(requestId, position, equippedSlotIndex, ResolveLocalCrouchActive());
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.PlaceBlock, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostPlace(
                    ResolveLocalClientId(),
                    requestId: 0,
                    position,
                    equippedSlotIndex,
                    sendResponse: false,
                    requesterCrouching: ResolveLocalCrouchActive()),
                position);
        }

        public SurvivalCommandResult TrySubmitDeathDrop(BlockPosition preferredPosition, out bool requestSentToHost)
        {
            requestSentToHost = false;

            if (IsActiveClientOnly())
            {
                if (chunkAuthoritySync != null && !chunkAuthoritySync.HasHostGenerationSnapshotForSession)
                {
                    LastCommandResult = SurvivalCommandResult.Reject(
                        SurvivalCommandKind.DeathDropInventory,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendDeathDropRequest(requestId, preferredPosition);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.DeathDropInventory, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostDeathDrop(ResolveLocalClientId(), requestId: 0, preferredPosition, sendResponse: false),
                preferredPosition);
        }

        public SurvivalCommandResult TrySubmitUse(BlockPosition targetBlock, BlockPosition placement, out bool requestSentToHost)
        {
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

            if (targetWorld != null &&
                targetWorld.Bounds.Contains(targetBlock) &&
                IsPlacedContainerBlock(targetWorld.GetBlock(targetBlock)))
            {
                requestSentToHost = false;
                Inventory container = worldManager.GetOrCreateContainerStore().GetOrCreate(targetBlock);
                ContainerOpenRequested?.Invoke(targetBlock, container);
                LastCommandResult = SurvivalCommandResult.Accept(SurvivalCommandKind.ContainerOpen, requestId: 0);
                return LastCommandResult;
            }

            ItemStack held = EquippedItem;

            if (targetWorld != null &&
                targetWorld.Bounds.Contains(targetBlock) &&
                FluidBlocks.IsFreshwater(targetWorld.GetBlock(targetBlock)) &&
                held.ItemId != ItemId.EmptyBucket)
            {
                requestSentToHost = false;
                WorldDrinkRequested?.Invoke();
                LastCommandResult = SurvivalCommandResult.Accept(SurvivalCommandKind.None, requestId: 0);
                return LastCommandResult;
            }

            if (!held.IsEmpty && itemRegistry.TryGet(held.ItemId, out ItemDefinition def))
            {
                if (def.ToolClass == HarvestToolKind.Feller)
                    return TrySubmitStripLog(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (def.ToolClass == HarvestToolKind.Tiller)
                    return TrySubmitTill(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (FarmingService.IsSeedItem(held.ItemId))
                    return TrySubmitPlantSeed(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (held.ItemId == ItemId.EmptyBucket)
                    return TrySubmitFillBucket(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (held.ItemId == ItemId.FreshwaterBucket || held.ItemId == ItemId.BrineBucket)
                    return TrySubmitPourBucket(placement, out requestSentToHost, SelectedHotbarSlotIndex);

                if (def.Kind == ItemKind.Consumable)
                    return TrySubmitUseConsumable(out requestSentToHost, SelectedHotbarSlotIndex);
            }

            return TrySubmitPlace(placement, out requestSentToHost, SelectedHotbarSlotIndex);
        }

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

        public SurvivalCommandResult TrySubmitFillBucket(
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
                        SurvivalCommandKind.FillBucket,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendBlockCommandRequest(SurvivalCommandKind.FillBucket, requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.FillBucket, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostFillBucket(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false),
                position);
        }

        public SurvivalCommandResult TrySubmitPourBucket(
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
                        SurvivalCommandKind.PourBucket,
                        SurvivalCommandFailureReason.AwaitingHostWorldSnapshot);
                    return LastCommandResult;
                }

                uint requestId = AllocateCommandRequestId();
                SendBlockCommandRequest(SurvivalCommandKind.PourBucket, requestId, position, equippedSlotIndex);
                requestSentToHost = true;
                LastCommandResult = SurvivalCommandResult.RequestSent(SurvivalCommandKind.PourBucket, requestId);
                return LastCommandResult;
            }

            return CompleteLocalCommand(
                ProcessHostPourBucket(ResolveLocalClientId(), requestId: 0, position, equippedSlotIndex, sendResponse: false),
                position);
        }

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

        public bool TryEvaluateHarvestWorkSeconds(BlockPosition position, out float seconds)
        {
            seconds = 0f;

            VoxelWorld world = ResolveWorldOrNull();
            if (world == null)
                return false;

            BlockHarvestResult preview = ResolveHarvestService()
                .TryPreviewHarvest(world, LocalInventory, position, EquippedItem);
            if (!preview.Succeeded)
                return false;

            seconds = preview.WorkRequired / (float)WorldConstants.TicksPerSecond;
            return true;
        }

        public event Action<ItemStack> ConsumableConsumed;
        public event Action WorldDrinkRequested;

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

        public event Action<BlockPosition, CraftingStation> StationOpenRequested;
        public event Action<BlockPosition> StationRemoved;
        public event Action<BlockPosition, Inventory> ContainerOpenRequested;

        static bool IsPlacedContainerBlock(BlockId block) =>
            block == BlockRegistry.StorageCrate ||
            block == BlockRegistry.ReedBasket ||
            block == BlockRegistry.ToolRack ||
            block == BlockRegistry.PantryJar ||
            block == BlockRegistry.DeepLocker;

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

        public readonly struct StationPersistentState
        {
            public readonly BlockPosition Position;
            public readonly CraftingStation StationType;
            public readonly ItemStack[] Inputs;
            public readonly ItemStack Fuel;
            public readonly ItemStack Output;
            public readonly ItemId ActiveRecipeOutput;
            public readonly int ProgressTicks;

            public StationPersistentState(
                BlockPosition position,
                CraftingStation stationType,
                ItemStack[] inputs,
                ItemStack fuel,
                ItemStack output,
                ItemId activeRecipeOutput,
                int progressTicks)
            {
                Position = position;
                StationType = stationType;
                Inputs = inputs;
                Fuel = fuel;
                Output = output;
                ActiveRecipeOutput = activeRecipeOutput;
                ProgressTicks = progressTicks;
            }
        }

        public IReadOnlyList<StationPersistentState> ExportStationStates()
        {
            var result = new List<StationPersistentState>(stationModels.Count);

            foreach (KeyValuePair<BlockPosition, SmeltingStationModel> pair in stationModels)
            {
                SmeltingStationModel model = pair.Value;
                var inputs = new ItemStack[model.InputSlotCount];
                for (int i = 0; i < inputs.Length; i++)
                    inputs[i] = model.GetInput(i);

                result.Add(new StationPersistentState(
                    pair.Key,
                    model.StationType,
                    inputs,
                    model.Fuel,
                    model.Output,
                    model.ActiveRecipe?.Output.ItemId ?? ItemId.None,
                    model.ProgressTicks));
            }

            return result;
        }

        public void RestoreStationStates(IEnumerable<StationPersistentState> states)
        {
            stationModels.Clear();
            if (states == null)
                return;

            CraftingRecipeBook recipeBook = ResolveRecipeBook();
            foreach (StationPersistentState state in states)
            {
                if (!SmeltingStationModel.IsTimedStation(state.StationType))
                    continue;

                CraftingRecipe activeRecipe = null;
                if (!state.ActiveRecipeOutput.IsNone)
                    recipeBook.TryGetByOutput(state.ActiveRecipeOutput, out activeRecipe);

                SmeltingStationModel model = GetOrCreateStationModel(state.Position, state.StationType);
                model.ApplyHostSnapshot(state.Inputs, state.Fuel, state.Output, activeRecipe, state.ProgressTicks);
            }
        }

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
                RemoveStationModel(stale, broadcast: true);
        }

        void RemoveStationModel(BlockPosition position, bool broadcast)
        {
            if (!stationModels.Remove(position))
                return;

            StationRemoved?.Invoke(position);

            if (broadcast)
                BroadcastStationRemoved(position);
        }

        void RefreshStationClockSubscription()
        {
            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (ReferenceEquals(subscribedStationClock, clock))
                return;

            UnsubscribeStationClock();
            subscribedStationClock = clock;
            if (subscribedStationClock != null)
                subscribedStationClock.Ticked += OnWorldClockTicked;
        }

        void UnsubscribeStationClock()
        {
            if (subscribedStationClock == null)
                return;

            subscribedStationClock.Ticked -= OnWorldClockTicked;
            subscribedStationClock = null;
        }

        void OnWorldClockTicked(int ticks)
        {
            TickStations(ticks);
        }

        public SurvivalCommandResult TrySubmitStationOpen(BlockPosition position, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationOpen, position, ItemId.None, 0, out requestSentToHost);

        public SurvivalCommandResult TrySubmitStationDepositInput(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationDepositInput, position, itemId, count, out requestSentToHost);

        public SurvivalCommandResult TrySubmitStationDepositFuel(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationDepositFuel, position, itemId, count, out requestSentToHost);

        public SurvivalCommandResult TrySubmitStationCollect(BlockPosition position, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationCollectOutput, position, ItemId.None, 0, out requestSentToHost);

        public SurvivalCommandResult TrySubmitStationWithdrawInput(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationWithdrawInput, position, itemId, count, out requestSentToHost);

        public SurvivalCommandResult TrySubmitStationWithdrawFuel(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationWithdrawFuel, position, itemId, count, out requestSentToHost);

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.HarvestResource, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.HarvestResource, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack authoritativeItem = ResolveAuthoritativeTool(inventory, equippedItem, equippedSlotIndex, sendResponse);
            BlockHarvestResult harvest = ResolveHarvestService().TryPreviewHarvest(
                ResolveWorld(),
                inventory,
                position,
                authoritativeItem,
                allowGroundDrops: true);

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

            if (TryRejectHarvestCadence(clientId, requestId, harvest, sendResponse, out SurvivalCommandResult cadenceFailure))
                return cadenceFailure;

            bool hasContainerAtPosition = worldManager != null &&
                worldManager.ContainerStore != null &&
                worldManager.ContainerStore.Contains(position);
            List<ItemStack> containerDrops = hasContainerAtPosition
                ? CaptureContainerContents(position)
                : null;
            ItemStack[] drops = ResolveHarvestService().RollHarvestDrops(harvest);
            var pendingDrops = new List<ItemStack>();
            if (containerDrops != null)
                pendingDrops.AddRange(containerDrops);
            pendingDrops.AddRange(drops);

            if (sendResponse && !InventoryCanReceiveAll(inventory, pendingDrops))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.InventoryFull,
                    requestId,
                    harvest.Drop,
                    BlockHarvestFailureReason.InventoryFull);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            bool restoreAutoLoot = false;
            if (hasContainerAtPosition)
            {
                restoreAutoLoot = !worldManager.SuppressContainerAutoLoot;
                worldManager.SuppressContainerAutoLoot = true;
            }

            BlockMutationResult mutation;
            try
            {
                mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                    new BlockMutationRequest(clientId, position, BlockRegistry.Air, harvest.BlockId),
                    out _,
                    out _,
                    BlockMutationSubmissionKind.SurvivalCommand);
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

            bool grantedContainerLoot = false;
            if (containerDrops != null)
            {
                foreach (ItemStack stack in containerDrops)
                {
                    if (stack.IsEmpty)
                        continue;

                    GrantStackToInventoryOrGround(inventory, stack, position, clientId);
                    grantedContainerLoot = true;
                }
            }

            if (grantedContainerLoot)
                worldManager.NotifyContainerLooted(position);

            foreach (ItemStack stack in drops)
            {
                if (stack.IsEmpty)
                    continue;

                GrantStackToInventoryOrGround(inventory, stack, position, clientId);
            }
            ItemStack drop = drops.Length > 0 ? drops[0] : ItemStack.Empty;

            TryLootStationInto(position, inventory);

            if (IsHotbarSlot(inventory, equippedSlotIndex))
            {
                ItemStack serverSlot = inventory.GetSlot(equippedSlotIndex);
                if (!serverSlot.IsEmpty && serverSlot.Durability > 0)
                    ApplyToolDurability(inventory, equippedSlotIndex, ResolveHarvestService().GetHarvestDurabilityCost(harvest, serverSlot));
            }

            AcceptedHarvestCount++;
            RecordAcceptedHarvest(clientId, sendResponse);
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

        List<ItemStack> CaptureContainerContents(BlockPosition position)
        {
            ContainerInventoryStore containerStore = worldManager != null ? worldManager.ContainerStore : null;
            Inventory container = containerStore?.GetOrNull(position);
            var stacks = new List<ItemStack>();
            if (container == null)
                return stacks;

            for (int slot = 0; slot < container.SlotCount; slot++)
            {
                ItemStack stack = container.GetSlot(slot);
                if (!stack.IsEmpty)
                    stacks.Add(stack);
            }

            return stacks;
        }

        static bool InventoryCanReceiveAll(Inventory inventory, IEnumerable<ItemStack> stacks)
        {
            if (inventory == null || stacks == null)
                return true;

            var requiredByItem = new Dictionary<ItemId, int>();
            foreach (ItemStack stack in stacks)
            {
                if (stack.IsEmpty)
                    continue;

                requiredByItem.TryGetValue(stack.ItemId, out int required);
                requiredByItem[stack.ItemId] = required + stack.Count;
            }

            foreach (KeyValuePair<ItemId, int> requirement in requiredByItem)
                if (inventory.GetAvailableCapacity(requirement.Key) < requirement.Value)
                    return false;

            return true;
        }

        void GrantStackToInventoryOrGround(Inventory inventory, ItemStack stack, BlockPosition position, ulong clientId)
        {
            if (stack.IsEmpty)
                return;

            ItemStack remaining = inventory != null ? inventory.Add(stack) : stack;
            if (remaining.IsEmpty)
                return;

            GroundItems.Spawn(
                remaining,
                position.X + 0.5f,
                position.Y + 0.5f,
                position.Z + 0.5f,
                ResolveWorldTick(),
                clientId.ToString());
        }

        SurvivalCommandResult ProcessHostPlace(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse,
            bool requesterCrouching)
        {
            ReceivedPlaceRequestCount++;
            lastKnownCrouchStateByClientId[clientId] = requesterCrouching;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.PlaceBlock, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.PlaceBlock, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.PlaceBlock, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

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

            if (TryRejectPlacementOverlappingPlayer(clientId, requestId, position, requesterCrouching, sendResponse, out SurvivalCommandResult overlapFailure))
                return overlapFailure;

            BlockId block = def.BlockId.Value;
            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, block, BlockRegistry.Air),
                out _,
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

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

        SurvivalCommandResult ProcessHostDeathDrop(
            ulong clientId,
            uint requestId,
            BlockPosition preferredPosition,
            bool sendResponse)
        {
            ReceivedDeathDropRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.DeathDropInventory, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.DeathDropInventory, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.DeathDropInventory, preferredPosition, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            if (!InventoryHasItems(inventory))
            {
                SurvivalCommandResult emptyAccepted = SurvivalCommandResult.Accept(SurvivalCommandKind.DeathDropInventory, requestId);
                SendInventorySnapshot(clientId);
                SendCommandResult(clientId, emptyAccepted, sendResponse);
                LastCommandResult = emptyAccepted;
                return emptyAccepted;
            }

            VoxelWorld world = ResolveWorldOrNull();
            if (world == null || worldManager == null || !TryResolveDeathDropPosition(world, preferredPosition, out BlockPosition dropPosition))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.DeathDropInventory,
                    SurvivalCommandFailureReason.MutationRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, dropPosition, BlockRegistry.StorageCrate, BlockRegistry.Air),
                out _,
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.DeathDropInventory,
                    SurvivalCommandFailureReason.MutationRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            Inventory deathCache = CreateDeathDropInventory(inventory);
            worldManager.GetOrCreateContainerStore().Set(dropPosition, deathCache);
            ClearInventorySlots(inventory);

            AcceptedDeathDropCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(SurvivalCommandKind.DeathDropInventory, requestId);
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        static bool InventoryHasItems(Inventory inventory)
        {
            if (inventory == null)
                return false;

            for (int slot = 0; slot < inventory.SlotCount; slot++)
            {
                if (!inventory.GetSlot(slot).IsEmpty)
                    return true;
            }

            return false;
        }

        Inventory CreateDeathDropInventory(Inventory source)
        {
            int slotCount = Mathf.Clamp(source.SlotCount, 1, Inventory.MaxSlotCount);
            var deathCache = new Inventory(ResolveItemRegistry(), slotCount, hotbarSlotCount: 0);
            for (int slot = 0; slot < source.SlotCount && slot < deathCache.SlotCount; slot++)
            {
                ItemStack stack = source.GetSlot(slot);
                if (!stack.IsEmpty)
                    deathCache.SetSlot(slot, stack);
            }

            return deathCache;
        }

        static bool TryResolveDeathDropPosition(VoxelWorld world, BlockPosition preferredPosition, out BlockPosition dropPosition)
        {
            if (TryAcceptDeathDropCandidate(world, preferredPosition, out dropPosition))
                return true;

            const int searchRadius = 3;
            for (int radius = 1; radius <= searchRadius; radius++)
            {
                for (int y = -1; y <= 2; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        for (int x = -radius; x <= radius; x++)
                        {
                            if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius)
                                continue;

                            var candidate = new BlockPosition(
                                preferredPosition.X + x,
                                preferredPosition.Y + y,
                                preferredPosition.Z + z);
                            if (TryAcceptDeathDropCandidate(world, candidate, out dropPosition))
                                return true;
                        }
                    }
                }
            }

            dropPosition = default;
            return false;
        }

        static bool TryAcceptDeathDropCandidate(VoxelWorld world, BlockPosition candidate, out BlockPosition dropPosition)
        {
            if (world != null &&
                world.Bounds.Contains(candidate) &&
                world.GetBlock(candidate) == BlockRegistry.Air)
            {
                dropPosition = candidate;
                return true;
            }

            dropPosition = default;
            return false;
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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.StripLog, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.StripLog, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

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
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.StripLog,
                    SurvivalCommandFailureReason.StripRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.TillSoil, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.TillSoil, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

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

            bool waterNearby = FarmingService.HasFreshwaterNearby(world, position);
            if (!waterNearby && inventory.CountOf(ItemId.CleanWaterFlask) < 1)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.TillSoil,
                    SurvivalCommandFailureReason.TillRequiresWater,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, BlockRegistry.TendedSoil, world.GetBlock(position)),
                out _,
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.TillSoil,
                    SurvivalCommandFailureReason.TillRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            if (!waterNearby)
            {
                inventory.Remove(ItemId.CleanWaterFlask, 1);
                inventory.TryAddAll(new ItemStack(ItemId.WaterFlask, 1));
            }

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.PlantSeed, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.PlantSeed, soilPosition, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

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
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.RepairTool, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            CraftingStation station = ResolveValidatedStationClaim(clientId, CraftingStation.MendBench, validateProximity: true);

            Inventory inventory = GetInventory(clientId);
            if (!IsHotbarSlot(inventory, toolSlotIndex))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.RepairTool,
                    SurvivalCommandFailureReason.RepairRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.UseConsumable, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

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

            if (stack.ItemId == ItemId.CleanWaterFlask)
                inventory.TryAddAll(new ItemStack(ItemId.WaterFlask, 1));

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

        SurvivalCommandResult ProcessHostFillBucket(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedBucketRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.FillBucket, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.FillBucket, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.FillBucket, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

            VoxelWorld world = ResolveWorld();
            BlockId target = world.Bounds.Contains(position) ? world.GetBlock(position) : BlockRegistry.Air;
            ItemId filledBucket = target == BlockRegistry.Freshwater ? ItemId.FreshwaterBucket
                : target == BlockRegistry.Brine ? ItemId.BrineBucket
                : ItemId.None;

            if (held.IsEmpty || held.ItemId != ItemId.EmptyBucket || filledBucket.IsNone)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.FillBucket,
                    SurvivalCommandFailureReason.NotBucketUse,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, BlockRegistry.Air, target),
                out _,
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.FillBucket,
                    SurvivalCommandFailureReason.BucketRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.SetSlot(equippedSlotIndex, new ItemStack(filledBucket, 1));

            AcceptedBucketCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.FillBucket,
                requestId,
                new ItemStack(filledBucket, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        SurvivalCommandResult ProcessHostPourBucket(
            ulong clientId,
            uint requestId,
            BlockPosition position,
            int equippedSlotIndex,
            bool sendResponse)
        {
            ReceivedBucketRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.PourBucket, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.PourBucket, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.PourBucket, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);
            ItemStack held = GetHotbarSlotOrEmpty(inventory, equippedSlotIndex);

            BlockId fluid = held.ItemId == ItemId.FreshwaterBucket ? BlockRegistry.Freshwater
                : held.ItemId == ItemId.BrineBucket ? BlockRegistry.Brine
                : BlockRegistry.Air;

            VoxelWorld world = ResolveWorld();
            if (fluid == BlockRegistry.Air ||
                !world.Bounds.Contains(position) ||
                world.GetBlock(position) != BlockRegistry.Air)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PourBucket,
                    SurvivalCommandFailureReason.NotBucketUse,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            BlockMutationResult mutation = ResolveChunkAuthoritySync().TrySubmitMutation(
                new BlockMutationRequest(clientId, position, fluid, BlockRegistry.Air),
                out _,
                out _,
                BlockMutationSubmissionKind.SurvivalCommand);

            if (!mutation.Accepted)
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.PourBucket,
                    SurvivalCommandFailureReason.BucketRejected,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            inventory.SetSlot(equippedSlotIndex, new ItemStack(ItemId.EmptyBucket, 1));

            AcceptedBucketCount++;
            SurvivalCommandResult accepted = SurvivalCommandResult.Accept(
                SurvivalCommandKind.PourBucket,
                requestId,
                new ItemStack(held.ItemId, 1));
            SendInventorySnapshot(clientId);
            SendCommandResult(clientId, accepted, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = accepted;
            return accepted;
        }

        void ApplyToolDurability(Inventory inventory, int slotIndex, int cost)
        {
            if (!IsHotbarSlot(inventory, slotIndex))
                return;

            ItemStack slot = inventory.GetSlot(slotIndex);
            if (slot.IsEmpty || slot.Durability <= 0)
                return;

            int remaining = slot.Durability - Math.Max(1, cost);
            inventory.SetSlot(slotIndex, remaining > 0
                ? slot.WithDurability(remaining)
                : ItemStack.Empty);
        }

        static ItemStack ResolveAuthoritativeTool(Inventory inventory, ItemStack equippedItem, int equippedSlotIndex, bool sendResponse)
        {
            bool slotIsValid = IsHotbarSlot(inventory, equippedSlotIndex);
            if (sendResponse)
                return slotIsValid ? inventory.GetSlot(equippedSlotIndex) : ItemStack.Empty;
            return slotIsValid ? inventory.GetSlot(equippedSlotIndex) : equippedItem;
        }

        static ItemStack GetHotbarSlotOrEmpty(Inventory inventory, int slotIndex) =>
            IsHotbarSlot(inventory, slotIndex)
                ? inventory.GetSlot(slotIndex)
                : ItemStack.Empty;

        static bool IsHotbarSlot(Inventory inventory, int slotIndex) =>
            inventory != null &&
            slotIndex >= 0 &&
            slotIndex < inventory.HotbarSlotCount &&
            slotIndex < inventory.SlotCount;

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.CraftRecipe, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (!ResolveRecipeBook().TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                var result = SurvivalCommandResult.Reject(
                    SurvivalCommandKind.CraftRecipe,
                    SurvivalCommandFailureReason.MissingRecipe,
                    requestId);
                SendCommandFailure(clientId, result, sendResponse);
                return result;
            }

            availableStation = ResolveValidatedStationClaim(clientId, availableStation, sendResponse);

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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, commandKind, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

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

            Inventory playerInventory = GetInventory(clientId);
            Inventory crateInventory = SharedCrateInventory;
            SurvivalCommandResult result;

            if (commandKind == SurvivalCommandKind.SharedCrateDeposit)
            {
                if (!TryMoveInventoryItems(playerInventory, crateInventory, itemId, count, out ItemStack movedStack))
                {
                    SurvivalCommandFailureReason reason = playerInventory.CountOf(itemId) < count
                        ? SurvivalCommandFailureReason.InvalidTransfer
                        : SurvivalCommandFailureReason.InventoryFull;
                    result = SurvivalCommandResult.Reject(commandKind, reason, requestId, new ItemStack(itemId, count));
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                result = SurvivalCommandResult.Accept(commandKind, requestId, movedStack);
            }
            else
            {
                if (!TryMoveInventoryItems(crateInventory, playerInventory, itemId, count, out ItemStack movedStack))
                {
                    SurvivalCommandFailureReason reason = crateInventory.CountOf(itemId) < count
                        ? SurvivalCommandFailureReason.SharedCrateEmpty
                        : SurvivalCommandFailureReason.InventoryFull;
                    result = SurvivalCommandResult.Reject(commandKind, reason, requestId, new ItemStack(itemId, count));
                    SendCommandFailure(clientId, result, sendResponse);
                    return result;
                }

                result = SurvivalCommandResult.Accept(commandKind, requestId, movedStack);
            }

            AcceptedCrateTransferCount++;
            SendInventorySnapshot(clientId);
            BroadcastSharedCrateSnapshot();
            SharedCrateChanged?.Invoke();
            SendCommandResult(clientId, result, sendResponse);
            RefreshLocalInventoryReference();
            LastCommandResult = result;
            return result;
        }

        static bool TryMoveInventoryItems(
            Inventory source,
            Inventory destination,
            ItemId itemId,
            int count,
            out ItemStack movedStack)
        {
            movedStack = ItemStack.Empty;
            if (source == null || destination == null || count <= 0)
                return false;

            if (!TryBuildTransferStack(source, itemId, count, out movedStack))
                return false;

            if (destination.GetAvailableCapacity(itemId) < count)
                return false;

            if (!destination.TryAddAll(movedStack))
                return false;

            RemoveTransferStack(source, movedStack);
            return true;
        }

        static bool TryBuildTransferStack(Inventory inventory, ItemId itemId, int count, out ItemStack stack)
        {
            stack = ItemStack.Empty;
            if (inventory == null || itemId.IsNone || count <= 0 || inventory.CountOf(itemId) < count)
                return false;

            for (int slot = 0; slot < inventory.SlotCount; slot++)
            {
                ItemStack candidate = inventory.GetSlot(slot);
                if (candidate.IsEmpty || candidate.ItemId != itemId)
                    continue;

                if (candidate.Count >= count)
                {
                    stack = candidate.WithCount(count);
                    return true;
                }

                if (candidate.Durability > 0)
                    return false;
            }

            stack = new ItemStack(itemId, count);
            return true;
        }

        static void RemoveTransferStack(Inventory inventory, ItemStack stack)
        {
            if (stack.IsEmpty)
                return;

            if (stack.Durability > 0)
            {
                for (int slot = 0; slot < inventory.SlotCount; slot++)
                {
                    ItemStack candidate = inventory.GetSlot(slot);
                    if (!candidate.IsEmpty &&
                        candidate.ItemId == stack.ItemId &&
                        candidate.Durability == stack.Durability &&
                        candidate.Count >= stack.Count)
                    {
                        inventory.SplitSlot(slot, stack.Count);
                        return;
                    }
                }
            }

            inventory.Remove(stack.ItemId, stack.Count);
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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, commandKind, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, commandKind, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

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

            if (ResolveValidatedStationClaim(clientId, stationType, sendResponse) != stationType)
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

                    if (!TryBuildTransferStack(inventory, itemId, count, out ItemStack stack))
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    bool deposited = commandKind == SurvivalCommandKind.StationDepositInput
                        ? station.TryDepositInput(stack)
                        : station.TryDepositFuel(stack);

                    if (!deposited)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.StationRejected, requestId, stack);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    RemoveTransferStack(inventory, stack);
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

                case SurvivalCommandKind.StationWithdrawInput:
                case SurvivalCommandKind.StationWithdrawFuel:
                {
                    if (!IsValidTransferItem(itemId) || count <= 0)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InvalidTransfer, requestId);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    var requestedStack = new ItemStack(itemId, count);
                    ItemRegistry registry = ResolveItemRegistry();
                    if (!InventoryCanReceive(inventory, requestedStack, registry))
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InventoryFull, requestId, requestedStack);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    ItemStack stack;
                    bool withdrew = commandKind == SurvivalCommandKind.StationWithdrawInput
                        ? station.TryWithdrawInput(itemId, count, out stack)
                        : station.TryWithdrawFuel(itemId, count, out stack);

                    if (!withdrew)
                    {
                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.StationRejected, requestId, requestedStack);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    if (!inventory.TryAddAll(stack))
                    {
                        if (commandKind == SurvivalCommandKind.StationWithdrawInput)
                            station.TryDepositInput(stack);
                        else
                            station.TryDepositFuel(stack);

                        result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.InventoryFull, requestId, stack);
                        SendCommandFailure(clientId, result, sendResponse);
                        return result;
                    }

                    result = SurvivalCommandResult.Accept(commandKind, requestId, stack);
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

        static bool InventoryCanReceive(Inventory inventory, ItemStack stack, ItemRegistry registry)
        {
            if (inventory == null || stack.IsEmpty)
                return true;

            ItemDefinition definition = registry.Get(stack.ItemId);
            int capacity = 0;
            for (int i = 0; i < inventory.SlotCount && capacity < stack.Count; i++)
            {
                ItemStack existing = inventory.GetSlot(i);
                if (existing.IsEmpty)
                {
                    capacity += definition.MaxStackSize;
                }
                else if (existing.CanStackWith(stack) && existing.Count < definition.MaxStackSize)
                {
                    capacity += definition.MaxStackSize - existing.Count;
                }
            }

            return capacity >= stack.Count;
        }

        void TryLootStationInto(BlockPosition position, Inventory inventory)
        {
            if (inventory == null || !stationModels.TryGetValue(position, out SmeltingStationModel station))
                return;

            foreach (ItemStack stack in station.DrainContents())
                inventory.Add(stack);

            RemoveStationModel(position, broadcast: true);
        }

        CraftingStation ResolveValidatedStationClaim(ulong clientId, CraftingStation claimed, bool validateProximity)
        {
            if (claimed == CraftingStation.None || !validateProximity)
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

        bool TryRejectHarvestCadence(
            ulong clientId,
            uint requestId,
            BlockHarvestResult harvest,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (!sendResponse || harvest.WorkRequired <= 0)
                return false;

            if (!lastAcceptedHarvestTimeByClientId.TryGetValue(clientId, out double lastAcceptedHarvestTime))
                return false;

            double requiredSeconds = harvest.WorkRequired / (double)WorldConstants.TicksPerSecond;
            double elapsedSeconds = HostCommandTimeSeconds - lastAcceptedHarvestTime;
            if (elapsedSeconds + HostHarvestRateGraceSeconds >= requiredSeconds)
                return false;

            result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.HarvestResource,
                SurvivalCommandFailureReason.HarvestRejected,
                requestId,
                harvest.Drop);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
        }

        void RecordAcceptedHarvest(ulong clientId, bool sendResponse)
        {
            if (sendResponse)
                lastAcceptedHarvestTimeByClientId[clientId] = HostCommandTimeSeconds;
        }

        bool TryRejectOutOfReach(
            ulong clientId,
            uint requestId,
            SurvivalCommandKind commandKind,
            BlockPosition targetPosition,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (!sendResponse)
                return false;

            if (!TryResolveClientWorldPosition(clientId, out Vector3 requesterPosition))
                return false;

            if (IsBlockWithinInteractionReach(requesterPosition, targetPosition))
                return false;

            result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.OutOfReach, requestId);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
        }

        bool TryRejectPlacementOverlappingPlayer(
            ulong clientId,
            uint requestId,
            BlockPosition targetPosition,
            bool requesterCrouching,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (!IsBlockOccupiedByPlayer(targetPosition, clientId, requesterCrouching))
                return false;

            result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.PlaceBlock,
                SurvivalCommandFailureReason.PlacementRejected,
                requestId);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
        }

        bool IsBlockOccupiedByPlayer(BlockPosition targetPosition, ulong fallbackClientId, bool fallbackCrouching)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager != null && networkManager.IsListening)
            {
                foreach (ulong clientId in networkManager.ConnectedClientsIds)
                {
                    bool crouching = clientId == fallbackClientId
                        ? fallbackCrouching
                        : TryResolveKnownClientCrouch(clientId, out bool knownCrouching) && knownCrouching;

                    if (TryResolveClientBlockPosition(clientId, out BlockPosition playerPosition) &&
                        IsPlayerOccupyingBlock(targetPosition, playerPosition, crouching))
                    {
                        return true;
                    }
                }

                return false;
            }

            return TryResolveClientBlockPosition(fallbackClientId, out BlockPosition fallbackPosition) &&
                   IsPlayerOccupyingBlock(targetPosition, fallbackPosition, fallbackCrouching);
        }

        bool TryResolveKnownClientCrouch(ulong clientId, out bool crouching)
        {
            if (clientId == ResolveLocalClientId())
            {
                crouching = ResolveLocalCrouchActive();
                return true;
            }

            return lastKnownCrouchStateByClientId.TryGetValue(clientId, out crouching);
        }

        bool ResolveLocalCrouchActive() => localCrouchStateProvider != null && localCrouchStateProvider();

        void ClearKnownCrouchState()
        {
            lastKnownCrouchStateByClientId.Clear();
            hasSentLocalCrouchState = false;
            lastSentLocalCrouchState = false;
            nextCrouchStateHeartbeatTime = 0.0f;
        }

        void RefreshCrouchStateReplication()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || !networkManager.IsListening)
                return;

            bool crouching = ResolveLocalCrouchActive();
            lastKnownCrouchStateByClientId[ResolveLocalClientId()] = crouching;

            if (!IsActiveClientOnly())
                return;

            if (hasSentLocalCrouchState &&
                lastSentLocalCrouchState == crouching &&
                Time.unscaledTime < nextCrouchStateHeartbeatTime)
            {
                return;
            }

            SendPlayerCrouchState(crouching);
            hasSentLocalCrouchState = true;
            lastSentLocalCrouchState = crouching;
            nextCrouchStateHeartbeatTime = Time.unscaledTime + CrouchStateHeartbeatSeconds;
        }

        bool TryRejectSurvivalCommandForWorldMode(
            ulong clientId,
            uint requestId,
            SurvivalCommandKind commandKind,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (!sendResponse || worldManager == null || worldManager.GameMode != WorldGameMode.Creative)
                return false;

            result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.GameModeRejected, requestId);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
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

        void HandleCommandRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            if (!hostCommandRateLimiter.TryConsume(senderClientId, HostCommandTimeSeconds))
            {
                RateLimitedCommandRequestCount++;
                return;
            }

            reader.ReadValueSafe(out uint requestId);
            reader.ReadValueSafe(out int kindValue);
            var commandKind = (SurvivalCommandKind)kindValue;

            switch (commandKind)
            {
                case SurvivalCommandKind.HarvestResource:
                {
                    BlockPosition position = SurvivalSyncWireCodec.ReadBlockPosition(ref reader);
                    reader.ReadValueSafe(out int equippedSlotIndex);
                    ProcessHostHarvest(senderClientId, requestId, position, ItemStack.Empty, sendResponse: true, equippedSlotIndex);
                    break;
                }
                case SurvivalCommandKind.PlaceBlock:
                case SurvivalCommandKind.StripLog:
                case SurvivalCommandKind.TillSoil:
                case SurvivalCommandKind.PlantSeed:
                case SurvivalCommandKind.FillBucket:
                case SurvivalCommandKind.PourBucket:
                {
                    BlockPosition position = SurvivalSyncWireCodec.ReadBlockPosition(ref reader);
                    reader.ReadValueSafe(out int equippedSlotIndex);

                    if (commandKind == SurvivalCommandKind.PlaceBlock)
                    {
                        reader.ReadValueSafe(out bool requesterCrouching);
                        ProcessHostPlace(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true, requesterCrouching: requesterCrouching);
                    }
                    else if (commandKind == SurvivalCommandKind.StripLog)
                        ProcessHostStripLog(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else if (commandKind == SurvivalCommandKind.TillSoil)
                        ProcessHostTill(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else if (commandKind == SurvivalCommandKind.PlantSeed)
                        ProcessHostPlantSeed(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else if (commandKind == SurvivalCommandKind.FillBucket)
                        ProcessHostFillBucket(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
                    else
                        ProcessHostPourBucket(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
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
                case SurvivalCommandKind.DeathDropInventory:
                {
                    BlockPosition position = SurvivalSyncWireCodec.ReadBlockPosition(ref reader);
                    ProcessHostDeathDrop(senderClientId, requestId, position, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.CraftRecipe:
                {
                    if (!SurvivalSyncWireCodec.TryReadBoundedNetworkString(ref reader, SurvivalSyncWireCodec.MaxNetworkItemIdChars, out string outputItemId))
                        return;
                    reader.ReadValueSafe(out int availableStation);
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
                    if (!SurvivalSyncWireCodec.TryReadItemStack(ref reader, out ItemStack stack))
                        return;
                    ProcessHostCrateTransfer(senderClientId, requestId, commandKind, stack.ItemId, stack.Count, sendResponse: true);
                    break;
                }
                case SurvivalCommandKind.StationOpen:
                case SurvivalCommandKind.StationDepositInput:
                case SurvivalCommandKind.StationDepositFuel:
                case SurvivalCommandKind.StationCollectOutput:
                case SurvivalCommandKind.StationWithdrawInput:
                case SurvivalCommandKind.StationWithdrawFuel:
                {
                    BlockPosition position = SurvivalSyncWireCodec.ReadBlockPosition(ref reader);
                    if (!SurvivalSyncWireCodec.TryReadItemStack(ref reader, out ItemStack stack))
                        return;
                    ProcessHostStationCommand(senderClientId, requestId, commandKind, position, stack.ItemId, stack.Count, sendResponse: true);
                    break;
                }
            }
        }

        delegate void CommandPayloadWriter(ref FastBufferWriter writer);

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

        void SendBlockCommandRequest(SurvivalCommandKind commandKind, uint requestId, BlockPosition position, int slotIndex) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) =>
            {
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(slotIndex);
            }, position);

        void SendPlaceCommandRequest(uint requestId, BlockPosition position, int slotIndex, bool requesterCrouching) =>
            SendCommandRequest(SurvivalCommandKind.PlaceBlock, requestId, (ref FastBufferWriter writer) =>
            {
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(slotIndex);
                writer.WriteValueSafe(requesterCrouching);
            }, position);

        void SendSlotCommandRequest(SurvivalCommandKind commandKind, uint requestId, int slotIndex) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) => writer.WriteValueSafe(slotIndex));

        void HandleCommandResultMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            SurvivalCommandResult result = SurvivalSyncWireCodec.ReadCommandResult(ref reader);
            LastCommandResult = result;
            bool completedPending = TryCompletePendingCommandRequest(
                result.RequestId,
                out (SurvivalCommandKind kind, BlockPosition position) pending);

            if (result.Accepted && result.CommandKind == SurvivalCommandKind.UseConsumable)
                ConsumableConsumed?.Invoke(result.Item);

            if (completedPending && !result.IsDuplicate)
                RaiseCommandFeedback(result, pending.position);
        }

        void HandleInventorySnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            SurvivalSyncWireCodec.ApplyInventorySnapshot(LocalInventory, ref reader);
            ReceivedInventorySnapshotCount++;
            LocalInventoryChanged?.Invoke();
        }

        void HandleSharedCrateSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            SurvivalSyncWireCodec.ApplyInventorySnapshot(SharedCrateInventory, ref reader);
            ReceivedSharedCrateSnapshotCount++;
            SharedCrateChanged?.Invoke();
        }

        void SendHarvestRequest(uint requestId, BlockPosition position, int equippedSlotIndex = -1) =>
            SendCommandRequest(SurvivalCommandKind.HarvestResource, requestId, (ref FastBufferWriter writer) =>
            {
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(equippedSlotIndex);
            }, position);

        void SendDeathDropRequest(uint requestId, BlockPosition position) =>
            SendCommandRequest(SurvivalCommandKind.DeathDropInventory, requestId, (ref FastBufferWriter writer) =>
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position), position);

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
                SurvivalSyncWireCodec.WriteItemStack(ref writer, new ItemStack(itemId, count)));

        void SendStationCommandRequest(
            uint requestId,
            SurvivalCommandKind commandKind,
            BlockPosition position,
            ItemId itemId,
            int count) =>
            SendCommandRequest(commandKind, requestId, (ref FastBufferWriter writer) =>
            {
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                SurvivalSyncWireCodec.WriteItemStack(ref writer, count > 0 ? new ItemStack(itemId, count) : ItemStack.Empty);
            }, position);

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
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe((int)station.StationType);
                writer.WriteValueSafe(station.InputSlotCount);
                for (int slot = 0; slot < station.InputSlotCount; slot++)
                    SurvivalSyncWireCodec.WriteItemStack(ref writer, station.GetInput(slot));
                SurvivalSyncWireCodec.WriteItemStack(ref writer, station.Fuel);
                SurvivalSyncWireCodec.WriteItemStack(ref writer, station.Output);
                writer.WriteValueSafe(station.IsActive);
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

        void BroadcastStationRemoved(BlockPosition position)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();

            if (networkManager == null ||
                !networkManager.IsListening ||
                !networkManager.IsServer)
            {
                return;
            }

            foreach (ulong clientId in networkManager.ConnectedClientsIds)
                SendStationRemovedSnapshot(clientId, position);
        }

        void SendStationRemovedSnapshot(ulong clientId, BlockPosition position)
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
            var writer = new FastBufferWriter(StationSnapshotMessageBytes, Allocator.Temp);

            try
            {
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(StationRemovedSnapshotType);
                networkManager.CustomMessagingManager.SendNamedMessage(StationSnapshotMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void HandleStationSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!IsMessageFromHost(senderClientId))
                return;

            BlockPosition position = SurvivalSyncWireCodec.ReadBlockPosition(ref reader);
            reader.ReadValueSafe(out int stationTypeValue);
            if (stationTypeValue == StationRemovedSnapshotType)
            {
                stationModels.Remove(position);
                StationRemoved?.Invoke(position);
                return;
            }

            reader.ReadValueSafe(out int inputSlotCount);
            int inputCount = Mathf.Max(0, inputSlotCount);
            if (stationSnapshotInputs.Length < inputCount)
                stationSnapshotInputs = new ItemStack[inputCount];
            for (int slot = 0; slot < inputCount; slot++)
            {
                if (!SurvivalSyncWireCodec.TryReadItemStack(ref reader, out stationSnapshotInputs[slot]))
                    return;
            }
            for (int slot = inputCount; slot < stationSnapshotInputs.Length; slot++)
                stationSnapshotInputs[slot] = ItemStack.Empty;
            if (!SurvivalSyncWireCodec.TryReadItemStack(ref reader, out ItemStack fuel))
                return;
            if (!SurvivalSyncWireCodec.TryReadItemStack(ref reader, out ItemStack output))
                return;
            reader.ReadValueSafe(out bool isActive);
            if (!SurvivalSyncWireCodec.TryReadBoundedNetworkString(ref reader, SurvivalSyncWireCodec.MaxNetworkItemIdChars, out string activeOutputItemId))
                return;
            reader.ReadValueSafe(out int progressTicks);

            var stationType = (CraftingStation)stationTypeValue;
            if (!SmeltingStationModel.IsTimedStation(stationType))
                return;

            SmeltingStationModel mirror = GetOrCreateStationModel(position, stationType);
            CraftingRecipe activeRecipe = isActive && !string.IsNullOrWhiteSpace(activeOutputItemId)
                ? FindStationRecipeByOutput(stationType, new ItemId(activeOutputItemId))
                : null;
            mirror.ApplyHostSnapshot(stationSnapshotInputs, fuel, output, activeRecipe, progressTicks);
            ReceivedStationSnapshotCount++;
        }

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
                SurvivalSyncWireCodec.WriteCommandResult(ref writer, result);
                networkManager.CustomMessagingManager.SendNamedMessage(CommandResultMessage, clientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendInventorySnapshot(ulong clientId)
        {
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
                SurvivalSyncWireCodec.WriteInventorySnapshot(ref writer, GetInventory(clientId));
                networkManager.CustomMessagingManager.SendNamedMessage(
                    InventorySnapshotMessage,
                    clientId,
                    writer,
                    InventorySnapshotDelivery);
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
                SurvivalSyncWireCodec.WriteInventorySnapshot(ref writer, SharedCrateInventory);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    SharedCrateSnapshotMessage,
                    clientId,
                    writer,
                    InventorySnapshotDelivery);
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
            if (IsActiveClientOnly() && clientId == ResolveLocalClientId())
            {
                SendPlayerHello();
                return;
            }

            if (!CanProcessHostRequests())
                return;

            GetInventory(clientId);
            SendInventorySnapshot(clientId);
            SendSharedCrateSnapshot(clientId);
        }

        void ClearSessionState()
        {
            inventoriesByClientId.Clear();
            playerIdentityKeysByClientId.Clear();
            stashedInventoriesByIdentityKey.Clear();
            processedRequestsByClientId.Clear();
            hostCommandRateLimiter.Clear();
            lastAcceptedHarvestTimeByClientId.Clear();
            ClearKnownCrouchState();
            groundItems = new GroundItemStore(itemRegistry);
            stationModels.Clear();
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

        void HandleClientDisconnected(ulong clientId)
        {
            if (!CanProcessHostRequests() || clientId == ResolveLocalClientId())
                return;

            ClearClientConnectionState(clientId);

            if (playerIdentityKeysByClientId.Remove(clientId, out string identityKey) &&
                inventoriesByClientId.Remove(clientId, out Inventory inventory))
            {
                stashedInventoriesByIdentityKey[identityKey] = inventory;
            }
        }

        void ClearClientConnectionState(ulong clientId)
        {
            processedRequestsByClientId.Remove(clientId);
            hostCommandRateLimiter.RemoveClient(clientId);
            lastAcceptedHarvestTimeByClientId.Remove(clientId);
            lastKnownCrouchStateByClientId.Remove(clientId);
        }

        static string ResolveLocalPlayerGuid()
        {
            return ResolveLocalIdentityPart(PlayerGuidPrefKey);
        }

        static string ResolveLocalPlayerSecret()
        {
            return ResolveLocalIdentityPart(PlayerSecretPrefKey);
        }

        static string ResolveLocalIdentityPart(string prefKey)
        {
            string value = PlayerPrefs.GetString(prefKey, string.Empty);
            if (!IsValidPlayerIdentityPart(value))
            {
                value = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(prefKey, value);
            }

            return value;
        }

        void SendPlayerHello()
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || networkManager.CustomMessagingManager == null)
                return;

            RegisterMessageHandlers();
            var writer = new FastBufferWriter(PlayerHelloMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(ResolveLocalPlayerGuid());
                writer.WriteValueSafe(ResolveLocalPlayerSecret());
                networkManager.CustomMessagingManager.SendNamedMessage(PlayerHelloMessage, NetworkManager.ServerClientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void SendPlayerCrouchState(bool crouching)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null || networkManager.CustomMessagingManager == null)
                return;

            RegisterMessageHandlers();
            var writer = new FastBufferWriter(PlayerCrouchStateMessageBytes, Allocator.Temp);

            try
            {
                writer.WriteValueSafe(crouching);
                networkManager.CustomMessagingManager.SendNamedMessage(PlayerCrouchStateMessage, NetworkManager.ServerClientId, writer);
            }
            finally
            {
                writer.Dispose();
            }
        }

        void HandlePlayerHelloMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            if (!SurvivalSyncWireCodec.TryReadBoundedNetworkString(ref reader, MaxNetworkPlayerGuidChars, out string guid))
                return;
            if (!SurvivalSyncWireCodec.TryReadBoundedNetworkString(ref reader, MaxNetworkPlayerSecretChars, out string secret))
                return;
            if (!TryBuildPlayerIdentityKey(guid, secret, out string identityKey))
                return;

            if (IsPlayerIdentityBoundToDifferentClient(senderClientId, identityKey))
                return;

            playerIdentityKeysByClientId[senderClientId] = identityKey;

            if (stashedInventoriesByIdentityKey.Remove(identityKey, out Inventory stashed))
            {
                inventoriesByClientId[senderClientId] = stashed;
                SendInventorySnapshot(senderClientId);
            }
        }

        void HandlePlayerCrouchStateMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (!CanProcessHostRequests())
                return;

            reader.ReadValueSafe(out bool crouching);
            lastKnownCrouchStateByClientId[senderClientId] = crouching;
        }

        bool IsPlayerIdentityBoundToDifferentClient(ulong clientId, string identityKey)
        {
            foreach (KeyValuePair<ulong, string> pair in playerIdentityKeysByClientId)
            {
                if (pair.Key != clientId && string.Equals(pair.Value, identityKey, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static bool TryBuildPlayerIdentityKey(string guid, string secret, out string identityKey)
        {
            identityKey = string.Empty;
            if (!IsValidPlayerIdentityPart(guid) || !IsValidPlayerIdentityPart(secret))
                return false;

            identityKey = $"{guid}_{secret}";
            return true;
        }

        static bool IsValidPlayerIdentityPart(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Length <= MaxNetworkPlayerGuidChars &&
                   Guid.TryParseExact(value, "N", out _);
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
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlayerHelloMessage, HandlePlayerHelloMessage);
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PlayerCrouchStateMessage, HandlePlayerCrouchStateMessage);
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
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlayerHelloMessage);
            subscribedNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PlayerCrouchStateMessage);
            messagesRegistered = false;
        }

        bool inLifecycleResolve;

        IMultiplayerWorldContext FindWorldManagerFallback()
        {
            if (Application.isPlaying && !inLifecycleResolve)
                Debug.LogWarning("MultiplayerSurvivalSync fell back to a IMultiplayerWorldContext scene scan outside Awake/OnEnable/Configure; wire it via Configure or the inspector.");

            var managers = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mono in managers)
            {
                if (mono is IMultiplayerWorldContext context)
                    return context;
            }
            return null;
        }

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();

            if (worldManager == null)
                worldManager = FindWorldManagerFallback();

            itemRegistry ??= ItemRegistry.Default;
            recipeBook ??= ReferenceEquals(itemRegistry, ItemRegistry.Default)
                ? CraftingRecipeBook.Default
                : CraftingRecipeBook.CreateDefault(itemRegistry);
            harvestService ??= new ResourceHarvestService(
                BlockRegistry.Default,
                itemRegistry,
                BlockHarvestRuleSet.CreateDefault(itemRegistry));
            groundItems ??= new GroundItemStore(itemRegistry);
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
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager == null)
                throw new InvalidOperationException("Multiplayer survival sync requires a network session.");

            return networkManager;
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

            if (chunkAuthoritySync == null)
                throw new InvalidOperationException("Multiplayer survival sync requires chunk authority sync.");

            return chunkAuthoritySync;
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

        long ResolveWorldTick()
        {
            return worldManager != null && worldManager.WorldTimeClock != null
                ? worldManager.WorldTimeClock.TotalElapsedTicks
                : 0L;
        }

        bool TryResolveClientBlockPosition(ulong clientId, out BlockPosition position)
        {
            if (TryResolveClientWorldPosition(clientId, out Vector3 worldPosition))
            {
                position = ToBlockPosition(worldPosition);
                return true;
            }

            position = default;
            return false;
        }

        bool TryResolveClientWorldPosition(ulong clientId, out Vector3 position)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                BlockiverseNetworkAvatarRig avatarRig = client.PlayerObject.GetComponent<BlockiverseNetworkAvatarRig>();
                Transform headTransform = avatarRig != null && avatarRig.HeadAnchor != null
                    ? avatarRig.HeadAnchor
                    : client.PlayerObject.transform;
                position = headTransform.position;
                return true;
            }

            if (clientId == ResolveLocalClientId() && Camera.main != null)
            {
                position = Camera.main.transform.position;
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

        private static BlockPosition ToBlockPosition(Vector3 worldPosition)
        {
            return new BlockPosition(
                Mathf.FloorToInt(worldPosition.x),
                Mathf.FloorToInt(worldPosition.y),
                Mathf.FloorToInt(worldPosition.z));
        }

        private static bool IsBlockWithinInteractionReach(Vector3 requesterPosition, BlockPosition targetPosition)
        {
            Vector3 center = new Vector3(targetPosition.X + 0.5f, targetPosition.Y + 0.5f, targetPosition.Z + 0.5f);
            return Vector3.Distance(requesterPosition, center) <= 6.0f;
        }

        private static bool IsPlayerOccupyingBlock(BlockPosition targetPosition, BlockPosition playerHeadPosition, bool crouching)
        {
            if (targetPosition.X != playerHeadPosition.X || targetPosition.Z != playerHeadPosition.Z)
                return false;

            int feetY = playerHeadPosition.Y - 1;
            return targetPosition.Y == feetY || (!crouching && targetPosition.Y == playerHeadPosition.Y);
        }

        sealed class ProcessedRequestWindow
        {
            const int MaxTrackedRequests = 1024;

            readonly HashSet<uint> ids = new();
            readonly Queue<uint> order = new();

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
    }
}