using System;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using UnityEngine;

namespace Blockiverse.UI
{
    public sealed class SurvivalHudController : MonoBehaviour
    {
        [SerializeField] BlockiverseHudToolkitSurface hudSurface;
        [SerializeField] int selectedHotbarSlotIndex;
        [SerializeField] float statusMessageSeconds = 2.5f;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        const float StationScanIntervalSeconds = 0.5f;
        const float VitalsRefreshIntervalSeconds = 0.5f;

        public Inventory Inventory { get; private set; }
        public CraftingRecipeBook RecipeBook { get; private set; }
        public PlayerVitals Vitals { get; private set; }
        public SurvivalVitalsRuntime VitalsRuntime => vitalsRuntime;
        public int SelectedHotbarSlotIndex => selectedHotbarSlotIndex;
        public string CurrentStatusText => hudSurface != null ? hudSurface.CurrentStatusText : string.Empty;
        public string CurrentCraftingStatusText => currentCraftingStatusText;
        public string CurrentCrateStatusText => currentCrateStatusText;
        public bool HasSharedCrate => survivalSync != null && survivalSync.SharedCrateInventory != null;

        CreativeWorldManager worldManager;
        SurvivalVitalsRuntime vitalsRuntime;
        MultiplayerSurvivalSync survivalSync;
        BlockiverseCreativeInputBridge inputBridge;
        ItemRegistry itemRegistry;
        float nextStationScanTime;
        float nextVitalsRefreshTime;
        float statusVisibleUntil;
        bool showingMiningProgress;
        CraftingStationSet lastScannedStations;
        string currentCraftingStatusText = string.Empty;
        string currentCrateStatusText = string.Empty;
        int lastHealth = int.MinValue;
        int lastMaxHealth = int.MinValue;
        int lastHunger = int.MinValue;
        int lastThirst = int.MinValue;
        int lastStamina = int.MinValue;
        string lastBaseState;

        public void Configure(
            int targetSelectedHotbarSlotIndex = 0,
            BlockiverseHudToolkitSurface targetHudSurface = null)
        {
            selectedHotbarSlotIndex = targetSelectedHotbarSlotIndex;
            hudSurface = targetHudSurface;
        }

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void BindMenuState(
            Inventory targetInventory,
            CraftingRecipeBook targetRecipeBook,
            ItemRegistry targetItemRegistry,
            CraftingStationSet availableStations = default)
        {
            itemRegistry = targetItemRegistry ?? ItemRegistry.Default;
            Inventory = targetInventory ?? new Inventory(itemRegistry);
            RecipeBook = targetRecipeBook ?? CraftingRecipeBook.CreateDefault(itemRegistry);
            lastScannedStations = availableStations;

            if (worldManager != null)
                worldManager.SetActivePlayerInventory(Inventory);

            ApplySelectedHotbarSlot(selectedHotbarSlotIndex, playFeedback: false);
            RefreshVitalsDisplay(force: true);
        }

        public void BindVitals(PlayerVitals targetVitals)
        {
            Vitals = targetVitals ?? new PlayerVitals();
            RefreshVitalsDisplay(force: true);
        }

        public void SetAvailableStations(CraftingStationSet availableStations)
        {
            lastScannedStations = availableStations;
        }

        void Awake()
        {
            BindValidationState();
        }

        void BindValidationState()
        {
            DiscoverHudReferences();
            itemRegistry = ItemRegistry.Default;
            UnsubscribeTransientFeedback();

            survivalSync = FindAnyObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            Inventory = survivalSync != null ? survivalSync.LocalInventory : new Inventory(itemRegistry);
            RecipeBook = CraftingRecipeBook.Default;

            vitalsRuntime = FindAnyObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            Vitals = vitalsRuntime != null ? vitalsRuntime.Vitals : new PlayerVitals();
            inputBridge = FindAnyObjectByType<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include);

            worldManager = FindAnyObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            if (worldManager != null)
                worldManager.SetActivePlayerInventory(Inventory);

            ApplySelectedHotbarSlot(selectedHotbarSlotIndex, playFeedback: false);

            currentCraftingStatusText = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingReady);
            currentCrateStatusText = survivalSync != null
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateShared)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline);

            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
                survivalSync.SharedCrateChanged += OnSharedCrateChanged;
                survivalSync.CommandFeedback -= OnCommandFeedback;
                survivalSync.CommandFeedback += OnCommandFeedback;
            }

            if (inputBridge != null)
            {
                inputBridge.MiningProgressChanged -= OnMiningProgressChanged;
                inputBridge.MiningProgressChanged += OnMiningProgressChanged;
                inputBridge.MiningProgressCleared -= OnMiningProgressCleared;
                inputBridge.MiningProgressCleared += OnMiningProgressCleared;
            }

            RefreshVitalsDisplay(force: true);
            SetMiningProgressVisible(false);
        }

        void DiscoverHudReferences()
        {
            if (hudSurface == null)
                hudSurface = GetComponent<BlockiverseHudToolkitSurface>()
                    ?? GetComponentInChildren<BlockiverseHudToolkitSurface>(true);
        }

        void OnDestroy()
        {
            UnsubscribeTransientFeedback();

            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
                survivalSync.CommandFeedback -= OnCommandFeedback;
            }
        }

        void OnLocalInventoryChanged()
        {
            if (survivalSync != null && !ReferenceEquals(Inventory, survivalSync.LocalInventory))
            {
                Inventory = survivalSync.LocalInventory;
                if (worldManager != null)
                    worldManager.SetActivePlayerInventory(Inventory);
                ApplySelectedHotbarSlot(selectedHotbarSlotIndex, playFeedback: false);
            }
        }

        void OnSharedCrateChanged()
        {
        }

        void Update()
        {
            ScanNearbyStations();
            RefreshVitalsDisplay();
            ClearExpiredStatus();
        }

        public void HandleSlotSelection(int slotIndex)
        {
            if (Inventory == null || slotIndex < 0 || slotIndex >= Inventory.SlotCount)
                return;

            if (slotIndex < Inventory.HotbarSlotCount)
            {
                ApplySelectedHotbarSlot(slotIndex, playFeedback: true);
                return;
            }

            if (Inventory.HotbarSlotCount == 0)
                return;

            Inventory.SwapSlots(selectedHotbarSlotIndex, slotIndex);
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        void ApplySelectedHotbarSlot(int slotIndex, bool playFeedback)
        {
            if (Inventory != null && !IsValidHotbarSlot(slotIndex, Inventory.HotbarSlotCount))
                slotIndex = 0;

            selectedHotbarSlotIndex = slotIndex;
            if (survivalSync != null)
                survivalSync.SetSelectedHotbarSlot(selectedHotbarSlotIndex);
            if (playFeedback)
                PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        static bool IsValidHotbarSlot(int slotIndex, int hotbarSlotCount)
        {
            if (hotbarSlotCount == 0)
                return slotIndex == 0;

            return slotIndex >= 0 && slotIndex < hotbarSlotCount;
        }

        public CraftingResult TryCraftAtIndex(int index)
        {
            EnsureCraftingBound();

            if (!BlockiverseUiToolkitMenuCatalog.TryGetInstantRecipe(RecipeBook, index, out CraftingRecipe recipe))
            {
                SetCraftingStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRecipeUnavailable));
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient);
            }

            return TryCraft(recipe);
        }

        public CraftingResult TryCraftByOutput(ItemId outputItemId)
        {
            EnsureCraftingBound();

            if (!RecipeBook.TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                SetCraftingStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRecipeUnavailable));
                PlayFeedback(BlockiverseAudioCue.CraftFail);
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, outputItemId);
            }

            return TryCraft(recipe);
        }

        CraftingResult TryCraft(CraftingRecipe recipe)
        {
            if (survivalSync != null)
                return TryCraftAuthoritative(recipe);

            CraftingResult result = CraftingService.TryCraft(Inventory, recipe, EffectiveStationFor(recipe));
            SetCraftingStatus(result.Succeeded
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CraftingCrafted, FormatStack(recipe.Output))
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CraftingCannotCraft,
                    itemRegistry.Get(recipe.Output.ItemId).Name,
                    BlockiverseLocalization.DisplayName(result.FailureReason)));
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);
            return result;
        }

        CraftingResult TryCraftAuthoritative(CraftingRecipe recipe)
        {
            SurvivalCommandResult command = survivalSync.TrySubmitCraft(recipe.Output.ItemId, EffectiveStationFor(recipe), out bool sentToHost);
            bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

            SetCraftingStatus(command.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CraftingCrafted, FormatStack(recipe.Output))
                : sentToHost
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.CraftingPending,
                        itemRegistry.Get(recipe.Output.ItemId).Name)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.CraftingCannotCraft,
                        itemRegistry.Get(recipe.Output.ItemId).Name,
                        BlockiverseLocalization.DisplayName(command.CraftingFailureReason)));
            PlayFeedback(acceptedOrPending ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);

            return acceptedOrPending
                ? CraftingResult.Success()
                : CraftingResult.Failure(command.CraftingFailureReason, recipe.Output.ItemId);
        }

        public bool TryRepairHeldTool(int toolSlotIndex = -1)
        {
            EnsureCraftingBound();

            if (survivalSync != null)
            {
                SurvivalCommandResult command = survivalSync.TrySubmitRepair(out bool sentToHost, toolSlotIndex);
                bool acceptedOrPending = command.Accepted || command.PendingHostValidation || sentToHost;

                SetCraftingStatus(command.Accepted
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingToolRepaired)
                    : sentToHost
                        ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingRepairing)
                        : BlockiverseLocalization.Format(
                            BlockiverseLocalization.Keys.CraftingCannotRepair,
                            BlockiverseLocalization.DisplayName(command.FailureReason)));
                PlayFeedback(acceptedOrPending ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);
                return acceptedOrPending;
            }

            CraftingStation station = lastScannedStations.Contains(CraftingStation.MendBench)
                ? CraftingStation.MendBench
                : CraftingStation.None;
            RepairResult result = MendBenchRepair.TryRepair(itemRegistry, Inventory, Math.Max(0, toolSlotIndex), station);

            SetCraftingStatus(result.Succeeded
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CraftingToolRepaired)
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.CraftingCannotRepair,
                    BlockiverseLocalization.DisplayName(result.FailureReason)));
            PlayFeedback(result.Succeeded ? BlockiverseAudioCue.CraftSuccess : BlockiverseAudioCue.CraftFail);
            return result.Succeeded;
        }

        CraftingStation EffectiveStationFor(CraftingRecipe recipe) =>
            lastScannedStations.Contains(recipe.RequiredStation) ? recipe.RequiredStation : CraftingStation.None;

        public SurvivalCommandResult DepositHeldToCrate()
        {
            if (survivalSync == null)
            {
                SetCrateStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline));
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetCrateStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateNothingHeld));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateDeposit(held.ItemId, held.Count, out bool sentToHost);
            ReportCrateTransfer(
                result,
                sentToHost,
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CrateDeposited, FormatStack(held)));
            return result;
        }

        public SurvivalCommandResult WithdrawCrateSlot(int slotIndex)
        {
            if (survivalSync == null)
            {
                SetCrateStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline));
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);
            }

            Inventory crate = survivalSync.SharedCrateInventory;
            if (slotIndex < 0 || slotIndex >= crate.SlotCount)
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);

            ItemStack stack = crate.GetSlot(slotIndex);
            if (stack.IsEmpty)
            {
                SetCrateStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateEmptySlot));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.SharedCrateEmpty);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateWithdraw(stack.ItemId, stack.Count, out bool sentToHost);
            ReportCrateTransfer(
                result,
                sentToHost,
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CrateWithdrew, FormatStack(stack)));
            return result;
        }

        void ReportCrateTransfer(SurvivalCommandResult result, bool sentToHost, string successText)
        {
            bool ok = result.Accepted || result.PendingHostValidation || sentToHost;
            SetCrateStatus(result.Accepted
                ? successText
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateTransferring)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateTransferRejected));
            PlayFeedback(ok ? BlockiverseAudioCue.UiSelect : BlockiverseAudioCue.UiCancel);
        }

        void RefreshVitalsDisplay(bool force = false)
        {
            if (!force && (vitalsRuntime == null || Time.time < nextVitalsRefreshTime))
                return;

            nextVitalsRefreshTime = Time.time + VitalsRefreshIntervalSeconds;
            if (Vitals == null)
                return;

            if (force || Vitals.CurrentHealth != lastHealth || Vitals.MaxHealth != lastMaxHealth)
            {
                lastHealth = Vitals.CurrentHealth;
                lastMaxHealth = Vitals.MaxHealth;

                ApplyVitalsDisplay();
            }

            string baseState = GetHealthState(Vitals);
            SurvivalVitals survivalVitals = vitalsRuntime != null ? vitalsRuntime.SurvivalVitals : null;
            int hunger = survivalVitals != null ? survivalVitals.Hunger : int.MinValue;
            int thirst = survivalVitals != null ? survivalVitals.Thirst : int.MinValue;
            int stamina = survivalVitals != null ? survivalVitals.Stamina : int.MinValue;

            if (force || baseState != lastBaseState || hunger != lastHunger || thirst != lastThirst || stamina != lastStamina)
            {
                lastBaseState = baseState;
                lastHunger = hunger;
                lastThirst = thirst;
                lastStamina = stamina;

                string state = survivalVitals != null
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.HealthVitals,
                        baseState,
                        hunger,
                        thirst,
                        stamina)
                    : baseState;
                hudSurface?.SetHealth(Vitals.CurrentHealth, Vitals.MaxHealth, state);
            }
        }

        void ApplyVitalsDisplay()
        {
            if (Vitals == null)
                return;

            hudSurface?.SetHealth(Vitals.CurrentHealth, Vitals.MaxHealth, lastBaseState ?? GetHealthState(Vitals));
        }

        static string GetHealthState(PlayerVitals playerVitals)
        {
            if (playerVitals.IsDead)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthDown);

            return playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthCritical)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthStable);
        }

        void ScanNearbyStations()
        {
            if (worldManager == null || worldManager.World == null)
                return;

            if (Time.time < nextStationScanTime)
                return;

            nextStationScanTime = Time.time + StationScanIntervalSeconds;

            Transform origin = Camera.main != null ? Camera.main.transform : transform;
            BlockPosition center = CreativeInteractionController.ToBlockPosition(origin.position);
            lastScannedStations = StationProximity.ScanNearby(worldManager.World, center);
        }

        void OnMiningProgressChanged(BlockPosition position, float elapsedSeconds, float requiredSeconds)
        {
            float progress = requiredSeconds > 0f
                ? Mathf.Clamp01(elapsedSeconds / requiredSeconds)
                : 1.0f;
            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            showingMiningProgress = true;
            statusVisibleUntil = 0f;
            SetStatusText(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.SurvivalHudMiningProgress,
                percent));

            hudSurface?.SetMiningProgress(progress, true);
        }

        void OnMiningProgressCleared()
        {
            showingMiningProgress = false;
            SetMiningProgressVisible(false);

            if (statusVisibleUntil <= 0f)
                SetStatusText(string.Empty);
        }

        void OnCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            if (result.CommandKind != SurvivalCommandKind.HarvestResource ||
                result.Accepted ||
                result.PendingHostValidation ||
                result.IsDuplicate)
            {
                return;
            }

            string message = result.HarvestFailureReason switch
            {
                BlockHarvestFailureReason.InventoryFull => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudInventoryFull),
                BlockHarvestFailureReason.InsufficientTool => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudToolTooWeak),
                _ when result.FailureReason == SurvivalCommandFailureReason.InventoryFull =>
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudInventoryFull),
                _ => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.SurvivalHudHarvestRejected)
            };

            ShowTimedStatus(message);
        }

        void ShowTimedStatus(string message)
        {
            showingMiningProgress = false;
            SetMiningProgressVisible(false);
            SetStatusText(message);
            statusVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, statusMessageSeconds);
        }

        void ClearExpiredStatus()
        {
            if (showingMiningProgress || statusVisibleUntil <= 0f || Time.unscaledTime < statusVisibleUntil)
                return;

            statusVisibleUntil = 0f;
            SetStatusText(string.Empty);
        }

        void SetStatusText(string message)
        {
            hudSurface?.SetStatus(message ?? string.Empty);
        }

        void SetCraftingStatus(string status)
        {
            currentCraftingStatusText = status ?? string.Empty;
        }

        void SetCrateStatus(string status)
        {
            currentCrateStatusText = status ?? string.Empty;
        }

        void SetMiningProgressVisible(bool visible)
        {
            hudSurface?.SetMiningProgress(hudSurface != null ? hudSurface.CurrentMiningProgress : 0.0f, visible);
        }

        void UnsubscribeTransientFeedback()
        {
            if (survivalSync != null)
                survivalSync.CommandFeedback -= OnCommandFeedback;

            if (inputBridge != null)
            {
                inputBridge.MiningProgressChanged -= OnMiningProgressChanged;
                inputBridge.MiningProgressCleared -= OnMiningProgressCleared;
            }
        }

        void EnsureCraftingBound()
        {
            if (RecipeBook == null || Inventory == null)
                throw new InvalidOperationException("Survival HUD menu state has not been bound.");
        }

        string FormatStack(ItemStack stack)
        {
            if (stack.IsEmpty)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty);

            ItemDefinition definition = (itemRegistry ?? ItemRegistry.Default).Get(stack.ItemId);
            return BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStack, definition.Name, stack.Count);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}
