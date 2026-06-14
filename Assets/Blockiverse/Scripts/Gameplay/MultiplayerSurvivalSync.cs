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
        // Single client→host command channel: every request shares the wire header
        // [uint requestId][int SurvivalCommandKind]; the host dispatcher switches on the kind.
        const string CommandRequestMessage = "Blockiverse.Survival.CommandRequest";
        const string StationSnapshotMessage = "Blockiverse.Survival.StationSnapshot";
        const string CommandResultMessage = "Blockiverse.Survival.CommandResult";
        const string InventorySnapshotMessage = "Blockiverse.Survival.InventorySnapshot";
        const string SharedCrateSnapshotMessage = "Blockiverse.Survival.SharedCrateSnapshot";
        const string PlayerHelloMessage = "Blockiverse.Survival.PlayerHello";
        const string PlayerGuidPrefKey = "Blockiverse.PlayerGuid";
        const string PlayerSecretPrefKey = "Blockiverse.PlayerSecret";
        // Sized for the worst-case command payload, a station deposit at ~66 bytes: the 8-byte
        // [requestId][kind] header + 12-byte block position + an ItemStack whose id string is
        // wire-encoded at 4 + 2 bytes/char (longest canonical id today is 17 chars). 128 keeps
        // ~2x headroom; FastBufferWriter throws at send time on overflow, so grow this alongside
        // any larger future command payload.
        const int CommandRequestMessageBytes = 128;
        const int CommandResultMessageBytes = 128;
        const int StationSnapshotMessageBytes = 512;
        const int StationRemovedSnapshotType = -1;
        const int InventorySnapshotMessageBytes = 4096;
        const int PlayerHelloMessageBytes = 192;
        static readonly NetworkDelivery InventorySnapshotDelivery = NetworkDelivery.ReliableFragmentedSequenced;
        const int SharedCrateSlotCount = 12;
        const int MaxNetworkPlayerGuidChars = 64;
        const int MaxNetworkPlayerSecretChars = 64;
        const double HostHarvestRateGraceSeconds = 0.15d;
        const int HostCommandRateLimitMaxRequests = 30;
        const double HostCommandRateLimitWindowSeconds = 1.0d;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] CreativeWorldManager worldManager;

        readonly Dictionary<ulong, Inventory> inventoriesByClientId = new();
        // Reconnect identity: each player carries a persistent GUID plus a private reconnect secret
        // (PlayerPrefs). The host stores only the combined identity key, so a peer cannot reclaim
        // a disconnected player's stash by guessing or replaying the public GUID alone.
        readonly Dictionary<ulong, string> playerIdentityKeysByClientId = new();
        readonly Dictionary<string, Inventory> stashedInventoriesByIdentityKey = new();
        readonly Dictionary<ulong, ProcessedRequestWindow> processedRequestsByClientId = new();
        readonly PerClientRequestRateLimiter hostCommandRateLimiter =
            new(HostCommandRateLimitMaxRequests, HostCommandRateLimitWindowSeconds);
        readonly Dictionary<uint, (SurvivalCommandKind kind, BlockPosition position)> pendingCommandRequests = new();
        readonly Dictionary<ulong, double> lastAcceptedHarvestTimeByClientId = new();

        // Smelting stations keyed by their block position. On the host these are the authoritative
        // models, ticked from WorldTimeClock; on remote clients they are display mirrors fed by snapshots.
        readonly Dictionary<BlockPosition, SmeltingStationModel> stationModels = new();
        readonly List<BlockPosition> staleStationPositions = new();

        // Scratch input-slot buffer for HandleStationSnapshotMessage, grown to the largest
        // snapshot slot count seen so per-snapshot receipt does not allocate; entries past the
        // current snapshot's count are cleared before use.
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
        uint nextCommandRequestId = 1;
        bool messagesRegistered;

        public Inventory LocalInventory => GetInventory(ResolveLocalClientId());
        public Inventory SharedCrateInventory => sharedCrateInventory ??= CreateSharedCrateInventory();
        public GroundItemStore GroundItems => groundItems ??= new GroundItemStore(itemRegistry);
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

        double HostCommandTimeSeconds => hostCommandTimeProvider?.Invoke() ?? Time.unscaledTimeAsDouble;

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
        public bool CanUseCreativeMode => worldManager != null &&
            CreativePermissionPolicy.CanUseCreativeMode(worldManager.GameMode, IsActiveClientOnly());
        public bool CanToggleMode => worldManager != null &&
            CreativePermissionPolicy.CanTogglePlayerMode(worldManager.GameMode, modeSwitch.CurrentMode, IsActiveClientOnly());

        // Flips between survival and creative interaction, snapshotting/restoring the survival
        // inventory. Host/offline only: a remote client's inventory is a host-owned mirror, so a
        // local snapshot+clear would desync from the authoritative copy and be clobbered by the
        // next snapshot. Returns false when the switch is unavailable.
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
            CreativeWorldManager targetWorldManager,
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
            stationModels.Clear();
            nextCommandRequestId = 1;
            localInventory = CreatePlayerInventory();
            sharedCrateInventory = CreateSharedCrateInventory();
            SubscribeNetworkCallbacks();
            RefreshStationClockSubscription();
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
            // World loads and host snapshots can replace the clock after this component was
            // configured, so keep station simulation attached to the current world clock.
            RefreshStationClockSubscription();
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

        // Moves the player's inventory into a persisted death cache near the death position.
        // Host-authoritative: clients send only the preferred drop position; the host picks a
        // valid nearby air cell, places a StorageCrate marker, and clears its authoritative copy
        // of that player's inventory.
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

            // Drinking from targeted freshwater (§6.2; source or flowing): any use that isn't a
            // bucket fill drinks by hand. Vitals are local-player state (SurvivalVitalsRuntime
            // subscribes), so no host round-trip is involved and the world is untouched.
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

                // Buckets (§5.4/§631): an empty bucket scoops the targeted fluid source; a filled
                // one pours its source into the adjacent cell (same cell placement would use).
                if (held.ItemId == ItemId.EmptyBucket)
                    return TrySubmitFillBucket(targetBlock, out requestSentToHost, SelectedHotbarSlotIndex);

                if (held.ItemId == ItemId.FreshwaterBucket || held.ItemId == ItemId.BrineBucket)
                    return TrySubmitPourBucket(placement, out requestSentToHost, SelectedHotbarSlotIndex);

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

        // Bucket fill: scoops the targeted fluid source into the held empty bucket (§631), removing
        // the source block. Server-authoritative like harvest/place.
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

        // Bucket pour: empties the held filled bucket's fluid source into the targeted air cell,
        // returning the bucket empty. Server-authoritative like harvest/place.
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

        // Evaluates how long mining the targeted block takes with the equipped tool (hold-to-mine
        // cadence, §7.3). Local-only preview against the local inventory and replicated world —
        // the host still revalidates the harvest on submit. False when the block cannot be
        // harvested at all (air, no rule, wrong tool, full inventory).
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

        // Fires on the consuming peer when the host confirms a consumable was used (one stack of
        // the consumed item). SurvivalVitalsRuntime applies the matching vitals effect (§13).
        public event Action<ItemStack> ConsumableConsumed;

        // Fires when the local player uses a targeted freshwater source by hand (§6.2).
        // SurvivalVitalsRuntime applies the thirst restore on its own drink cooldown.
        public event Action WorldDrinkRequested;

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
        public event Action<BlockPosition> StationRemoved;

        // Fired when the local player uses a placed container block; UI binds to the per-position
        // inventory so opening a container does not fall through to the held-item place/use action.
        public event Action<BlockPosition, Inventory> ContainerOpenRequested;

        static bool IsPlacedContainerBlock(BlockId block) =>
            block == BlockRegistry.StorageCrate ||
            block == BlockRegistry.ReedBasket ||
            block == BlockRegistry.ToolRack ||
            block == BlockRegistry.PantryJar ||
            block == BlockRegistry.DeepLocker;

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

        // Per-station persistent state for world saves: slot contents plus the in-flight craft
        // (fuel was consumed when the craft began, so recipe+progress restore resumes it exactly).
        public readonly struct StationPersistentState
        {
            public readonly BlockPosition Position;
            public readonly CraftingStation StationType;
            public readonly ItemStack[] Inputs;
            public readonly ItemStack Fuel;
            public readonly ItemStack Output;
            public readonly ItemId ActiveRecipeOutput; // None when idle
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

        // Snapshot of every timed station's runtime state for world persistence (host/offline).
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

        // Rebuilds the station models from saved state (world load, host/offline). Entries whose
        // recipe no longer resolves restore idle with their slots intact; TickStations prunes any
        // whose block no longer matches the station type.
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

        // Advances all host-owned station models, pruning stations whose block was removed and
        // broadcasting a snapshot whenever a station's externally visible state changes (craft
        // begins/completes, fuel consumed). Exposed for tests; runtime ticking comes from WorldTimeClock.
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

        // Withdraws items from a station input slot into the requesting player's inventory.
        public SurvivalCommandResult TrySubmitStationWithdrawInput(
            BlockPosition position, ItemId itemId, int count, out bool requestSentToHost)
            => TrySubmitStationCommand(SurvivalCommandKind.StationWithdrawInput, position, itemId, count, out requestSentToHost);

        // Withdraws fuel from the station fuel slot into the requesting player's inventory.
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

            // A container's contents go to the player who broke it, but only after the block
            // mutation succeeds. Auto-loot is suppressed around the mutation so the world-change
            // handler removes the store entry without granting partially transferred leftovers to
            // the host's active player.
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

            // Apply the rule's drop table so tool-action bonuses (Sickle double-roll, Carver full
            // yield) take effect on the authoritative path, not only in the local TryHarvest helper.
            // Local/offline harvests can spawn protected ground items for overflow. Remote clients
            // do not yet receive or pick up ground-item snapshots, so the preflight above rejects
            // their overflow before the block mutation.
            foreach (ItemStack stack in drops)
            {
                if (stack.IsEmpty)
                    continue;

                GrantStackToInventoryOrGround(inventory, stack, position, clientId);
            }
            ItemStack drop = drops.Length > 0 ? drops[0] : ItemStack.Empty;

            TryLootStationInto(position, inventory);

            // §6.3 durability: cost derives from block category/tier and tool correctness — not a
            // flat 1 — so the authoritative path charges the same as the local TryHarvest helper.
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
            bool sendResponse)
        {
            ReceivedPlaceRequestCount++;

            if (TryRejectDuplicate(clientId, requestId, SurvivalCommandKind.PlaceBlock, sendResponse, out SurvivalCommandResult duplicate))
                return duplicate;

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.PlaceBlock, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.PlaceBlock, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);

            // The placed block is derived from the host-owned inventory slot, never trusted from the
            // client. An empty/out-of-range slot or a non-block item cannot place anything.
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

            if (TryRejectPlacementOverlappingPlayer(clientId, requestId, position, sendResponse, out SurvivalCommandResult overlapFailure))
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

            // The held tool is read from the host-owned slot, never trusted from the client: a Feller is
            // required, and the target must be a branchwood_log.
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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.TillSoil, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.TillSoil, position, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);

            // The held tool is read from the host-owned slot, never trusted from the client: a Tiller
            // is required, and the target must be tillable soil (§11.1).
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

            // §11.1: tilling needs freshwater within reach; without it, one clean water flask is
            // spent instead (§497/§784). Availability is checked before the mutation and the flask
            // removed after it, so a rejected mutation never costs the flask.
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
                // Flasks stack to 1, so the consume freed a slot for the returned empty (§731).
                inventory.TryAddAll(new ItemStack(ItemId.WaterFlask, 1));
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

            if (TryRejectSurvivalCommandForWorldMode(clientId, requestId, SurvivalCommandKind.PlantSeed, sendResponse, out SurvivalCommandResult modeFailure))
                return modeFailure;

            if (TryRejectOutOfReach(clientId, requestId, SurvivalCommandKind.PlantSeed, soilPosition, sendResponse, out SurvivalCommandResult reachFailure))
                return reachFailure;

            Inventory inventory = GetInventory(clientId);

            // The planted crop is derived from the host-owned inventory slot, never trusted from the
            // client. Planting requires a seed item, tended soil at the target, and air above it.
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

            // Host-side proximity check (§8): repair requires standing at a Mend Bench; see
            // ResolveValidatedStationClaim for the trust rules.
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

            // Drinking a filled flask returns the empty flask (§731 container-return). Flasks
            // stack to 1, so the consume above always freed a slot and this add cannot fail.
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

            // The held bucket is read from the host-owned slot, never trusted from the client: an
            // empty bucket is required, and the target must be a fluid source block (§631).
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

            // Filling scoops the source: the block becomes air, the bucket carries it (fluid is
            // conserved between fill and pour).
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

            // Buckets stack to 1, so the held stack is exactly the one bucket being filled.
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

            // The poured fluid is derived from the host-owned inventory slot, never trusted from
            // the client. Pouring requires a filled bucket and an air cell at the target.
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

            // The emptied bucket returns in place (§731); buckets stack to 1 so the swap fits.
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

        // Applies a durability cost to the host-owned slot; the tool breaks (slot empties) at 0.
        // No-op for empty slots and durability-less items (stackables report Durability 0).
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

        // Resolves the tool the host will validate a harvest against.
        // Remote requests are validated against the host-owned inventory slot only; the
        // client-supplied stack is never trusted. Host-local requests use the local stack,
        // falling back to the slot when one is supplied.
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

            // Host-side proximity check (§8): a client cannot claim a crafting station it is not
            // standing near. Invalid claims downgrade to None so CraftingService rejects with
            // MissingStation.
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
            // Broadcast skips the host's own client id; raise the local signal directly
            // (and offline, where there is no session to broadcast through at all).
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
            // StationOpen is read-only: only the requester's mirror needs refreshing. Mutating
            // commands change state every viewing client must see, so broadcast.
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

        // Host-side proximity trust policy (§8), shared by the craft/repair/station paths: a
        // claimed station is downgraded to None when the requester's position resolves and no
        // matching station block is within reach. When the position cannot be resolved
        // (offline/tests with no spawned player object), the claim is trusted — the local UI
        // already gates by an actual proximity scan, and remote clients always have a player object.
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

            // Offline/edit-mode callers may not have a spawned player object or camera. Runtime
            // remote clients do, so unresolved positions are trusted only for those local paths.
            if (!TryResolveClientWorldPosition(clientId, out Vector3 requesterPosition))
                return false;

            if (CreativeInteractionController.IsBlockWithinInteractionReach(requesterPosition, targetPosition))
                return false;

            result = SurvivalCommandResult.Reject(commandKind, SurvivalCommandFailureReason.OutOfReach, requestId);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
        }

        bool TryRejectPlacementOverlappingPlayer(
            ulong clientId,
            uint requestId,
            BlockPosition targetPosition,
            bool sendResponse,
            out SurvivalCommandResult result)
        {
            result = default;

            if (!IsBlockOccupiedByPlayer(targetPosition, clientId))
                return false;

            result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.PlaceBlock,
                SurvivalCommandFailureReason.PlacementRejected,
                requestId);
            SendCommandFailure(clientId, result, sendResponse);
            return true;
        }

        bool IsBlockOccupiedByPlayer(BlockPosition targetPosition, ulong fallbackClientId)
        {
            NetworkManager networkManager = ResolveNetworkManagerOrNull();
            if (networkManager != null && networkManager.IsListening)
            {
                foreach (ulong clientId in networkManager.ConnectedClientsIds)
                {
                    if (TryResolveClientBlockPosition(clientId, out BlockPosition playerPosition) &&
                        IsInPlayerHeadOrFeetColumn(targetPosition, playerPosition))
                    {
                        return true;
                    }
                }

                return false;
            }

            return TryResolveClientBlockPosition(fallbackClientId, out BlockPosition fallbackPosition) &&
                   IsInPlayerHeadOrFeetColumn(targetPosition, fallbackPosition);
        }

        static bool IsInPlayerHeadOrFeetColumn(BlockPosition targetPosition, BlockPosition playerHeadPosition) =>
            targetPosition.X == playerHeadPosition.X &&
            targetPosition.Z == playerHeadPosition.Z &&
            targetPosition.Y >= playerHeadPosition.Y - 1 &&
            targetPosition.Y <= playerHeadPosition.Y;

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

        // Single host-side dispatcher for every client→host command. The shared header
        // [requestId][kind] is read here; each kind then reads its own payload and routes to
        // the existing ProcessHost* logic. An unknown kind drops the message, the same posture
        // an unregistered named message had under the old per-command channels.
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
                    // The wire carries only position + slot index: the host resolves the tool
                    // from its own copy of the requester's inventory (ResolveAuthoritativeTool),
                    // so a client-supplied stack would be ignored anyway.
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
                        ProcessHostPlace(senderClientId, requestId, position, equippedSlotIndex, sendResponse: true);
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
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe(slotIndex);
            }, position);

        // Repair and consumable requests carry only a slot index.
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
                SurvivalSyncWireCodec.WriteBlockPosition(ref writer, position);
                writer.WriteValueSafe((int)station.StationType);
                writer.WriteValueSafe(station.InputSlotCount);
                for (int slot = 0; slot < station.InputSlotCount; slot++)
                    SurvivalSyncWireCodec.WriteItemStack(ref writer, station.GetInput(slot));
                SurvivalSyncWireCodec.WriteItemStack(ref writer, station.Fuel);
                SurvivalSyncWireCodec.WriteItemStack(ref writer, station.Output);
                writer.WriteValueSafe(station.IsActive);
                // Intentionally the ItemId string, not a recipe index: every wire payload in this
                // protocol identifies items by canonical ItemId (see SurvivalSyncWireCodec.WriteItemStack), and the
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
            // Stale scratch entries past this snapshot's count must read as empty:
            // ApplyHostSnapshot consumes the array up to the mirror's own slot count.
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
            // On the connecting client itself: introduce the persistent player identity so the host
            // can hand back a stashed inventory from an earlier disconnect this session.
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

        // Mirrors Configure's per-session clear set so a stopped session cannot leak state into
        // a later one started without a fresh Configure: stale station models would tick against
        // the new world, and the old session's inventories/duplicate windows are dead. The stable
        // localInventory instance survives and is re-bound to the current local client id.
        void ClearSessionState()
        {
            inventoriesByClientId.Clear();
            playerIdentityKeysByClientId.Clear();
            stashedInventoriesByIdentityKey.Clear();
            processedRequestsByClientId.Clear();
            hostCommandRateLimiter.Clear();
            lastAcceptedHarvestTimeByClientId.Clear();
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

        // Per-connection host state is released when a client leaves: the duplicate window is
        // purely connection-scoped, and the departing player's inventory is stashed under their
        // persistent identity key so a reconnect mid-session reclaims it (see HandlePlayerHelloMessage).
        void HandleClientDisconnected(ulong clientId)
        {
            if (!CanProcessHostRequests() || clientId == ResolveLocalClientId())
                return;

            processedRequestsByClientId.Remove(clientId);
            hostCommandRateLimiter.RemoveClient(clientId);
            lastAcceptedHarvestTimeByClientId.Remove(clientId);

            // Reconnect identity: stash the departing player's inventory under their hardened
            // identity key so the same player rejoining this session reclaims it (new client id).
            if (playerIdentityKeysByClientId.Remove(clientId, out string identityKey) &&
                inventoriesByClientId.Remove(clientId, out Inventory inventory))
            {
                stashedInventoriesByIdentityKey[identityKey] = inventory;
            }
        }

        // The local player's persistent identity parts, created once and reused across sessions.
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

        // Client → host introduction carrying the persistent player GUID plus reconnect secret.
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

        // Host: binds the sender to its persistent identity key and hands back any inventory stashed
        // for that key by an earlier disconnect, then pushes the authoritative snapshot.
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

            itemRegistry ??= ItemRegistry.Default;
            recipeBook ??= ReferenceEquals(itemRegistry, ItemRegistry.Default)
                ? CraftingRecipeBook.Default
                : CraftingRecipeBook.CreateDefault(itemRegistry);
            harvestService ??= new ResourceHarvestService(
                BlockRegistry.Default,
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

        // Resolves the authoritative world position of a client's player for proximity checks.
        // Prefers the synced head anchor from the avatar rig: XR locomotion moves the headset
        // independently of the NetworkObject root, so the root can lag at spawn while the player
        // has walked to a station. Falls back to the root if no rig/anchor is present, and to the
        // local camera for host-local commands without a network session.
        bool TryResolveClientBlockPosition(ulong clientId, out BlockPosition position)
        {
            if (TryResolveClientWorldPosition(clientId, out Vector3 worldPosition))
            {
                position = CreativeInteractionController.ToBlockPosition(worldPosition);
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

    }
}
