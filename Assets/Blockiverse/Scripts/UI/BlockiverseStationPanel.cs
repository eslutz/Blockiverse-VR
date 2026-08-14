using System;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // World-space panel for smelting stations (Clay Kiln / Bellows Forge). Displays the bound
    // SmeltingStationModel and routes deposit/withdraw/collect actions through the host-authoritative
    // survival sync. The panel never ticks the model — the host's MultiplayerSurvivalSync owns
    // station ticking from WorldTimeClock; client mirrors update from snapshots. The panel is
    // shown/hidden by BlockiverseMenuController.
    public sealed class BlockiverseStationPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] TMP_Text[] inputSlotLabels = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text fuelLabel;
        [SerializeField] TMP_Text outputLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Slider progressSlider;
        [SerializeField] Button closeButton;
        [SerializeField] Button depositInputButton;
        [SerializeField] Button depositFuelButton;
        [SerializeField] Button collectOutputButton;
        [SerializeField] Button withdrawInputButton;
        [SerializeField] Button withdrawFuelButton;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        ItemRegistry itemRegistry;
        SmeltingStationModel station;
        BlockPosition stationPosition;
        float displayProgressTicks;
        int lastModelProgressTicks;
        int lastContentVersion = -1;

        public event Action CloseRequested;

        public bool IsOpen => station != null;
        public BlockPosition OpenPosition => stationPosition;

        public void Configure(
            TMP_Text titleLabel,
            TMP_Text[] inputSlotLabels,
            TMP_Text fuelLabel,
            TMP_Text outputLabel,
            TMP_Text statusLabel,
            Slider progressSlider,
            Button closeButton)
        {
            this.titleLabel = titleLabel;
            this.inputSlotLabels = inputSlotLabels ?? Array.Empty<TMP_Text>();
            this.fuelLabel = fuelLabel;
            this.outputLabel = outputLabel;
            this.statusLabel = statusLabel;
            this.progressSlider = progressSlider;
            this.closeButton = closeButton;
            Wire(closeButton, () => CloseRequested?.Invoke());
        }

        public void ConfigureTransferControls(
            Button targetDepositInputButton,
            Button targetDepositFuelButton,
            Button targetCollectOutputButton,
            Button targetWithdrawInputButton = null,
            Button targetWithdrawFuelButton = null)
        {
            depositInputButton = targetDepositInputButton;
            depositFuelButton = targetDepositFuelButton;
            collectOutputButton = targetCollectOutputButton;
            withdrawInputButton = targetWithdrawInputButton;
            withdrawFuelButton = targetWithdrawFuelButton;
            WireTransferButtons();
        }

        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void ConfigureItemRegistry(ItemRegistry registry) => itemRegistry = registry;

        public void ResolveRuntimeReferences()
        {
            Transform root = transform.Find("Panel") ?? transform;

            AssignIfMissing(ref titleLabel, FindChildComponent<TMP_Text>(root, "Title"));
            if (NeedsRefresh(inputSlotLabels, SmeltingStationModel.MaxInputSlots))
                inputSlotLabels = FindInputSlotLabels(root);
            AssignIfMissing(ref fuelLabel, FindChildComponent<TMP_Text>(root, "Fuel Slot"));
            AssignIfMissing(ref outputLabel, FindChildComponent<TMP_Text>(root, "Output Slot"));
            AssignIfMissing(ref statusLabel, FindChildComponent<TMP_Text>(root, "Status"));
            AssignIfMissing(ref progressSlider, FindChildComponent<Slider>(root, "Progress"));
            AssignIfMissing(ref closeButton, FindChildComponent<Button>(root, "Close Button"));
            AssignIfMissing(ref depositInputButton, FindChildComponent<Button>(root, "Deposit Input Button"));
            AssignIfMissing(ref depositFuelButton, FindChildComponent<Button>(root, "Deposit Fuel Button"));
            AssignIfMissing(ref collectOutputButton, FindChildComponent<Button>(root, "Collect Output Button"));
            AssignIfMissing(ref withdrawInputButton, FindChildComponent<Button>(root, "Withdraw Input Button"));
            AssignIfMissing(ref withdrawFuelButton, FindChildComponent<Button>(root, "Withdraw Fuel Button"));

            Wire(closeButton, () => CloseRequested?.Invoke());
            WireTransferButtons();
            RefreshDisplay();
        }

        public void Open(SmeltingStationModel model, BlockPosition position, string displayTitle = null)
        {
            ResolveRuntimeReferences();
            station = model;
            stationPosition = position;
            displayProgressTicks = model?.ProgressTicks ?? 0;
            lastModelProgressTicks = model?.ProgressTicks ?? 0;
            if (titleLabel != null && model != null)
                titleLabel.text = displayTitle ?? BlockiverseLocalization.DisplayName(model.StationType);
            if (model != null)
                SetStatus(model.IsActive
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonActive)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationIdle));
            RefreshDisplay();
        }

        public void Close() => station = null;

        void Awake()
        {
            ResolveRuntimeReferences();
        }

        void Update()
        {
            if (station == null)
                return;

            // The model is authoritative (host) or snapshot-fed (client mirror); the panel mirrors
            // its progress instead of running a separate UI-side station clock.
            if (station.ProgressTicks != lastModelProgressTicks)
            {
                lastModelProgressTicks = station.ProgressTicks;
                displayProgressTicks = station.ProgressTicks;
            }
            else
            {
                displayProgressTicks = station.IsActive ? station.ProgressTicks : 0.0f;
            }

            // Slot/fuel/output labels only change when the model's contents change (TMP label
            // rebuilds and the string formatting are too costly per frame in VR).
            if (station.ContentVersion != lastContentVersion)
                RefreshLabels();
            RefreshProgress();
        }

        void WireTransferButtons()
        {
            Wire(depositInputButton, OnDepositInput);
            Wire(depositFuelButton, OnDepositFuel);
            Wire(collectOutputButton, OnCollectOutput);
            Wire(withdrawInputButton, OnWithdrawInput);
            Wire(withdrawFuelButton, OnWithdrawFuel);
        }

        static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        static bool AssignIfMissing<T>(ref T target, T value) where T : Component
        {
            if (target != null || value == null)
                return false;

            target = value;
            return true;
        }

        static bool NeedsRefresh<T>(T[] values, int expectedLength) where T : Component
        {
            if (values == null || values.Length < expectedLength)
                return true;

            for (int i = 0; i < expectedLength; i++)
            {
                if (values[i] == null)
                    return true;
            }

            return false;
        }

        static TMP_Text[] FindInputSlotLabels(Transform root)
        {
            var labels = new TMP_Text[SmeltingStationModel.MaxInputSlots];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = FindChildComponent<TMP_Text>(root, $"Input Slot {i + 1}");
            return labels;
        }

        static T FindChildComponent<T>(Transform root, string name) where T : Component
        {
            Transform child = FindChildRecursive(root, name);
            return child != null ? child.GetComponent<T>() : null;
        }

        static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name)
                    return child;

                Transform nested = FindChildRecursive(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        // Deposits one of the held hotbar item into a station input slot (host-validated).
        void OnDepositInput() => SubmitHeldItemTransfer(isFuel: false);

        // Deposits one of the held hotbar item as fuel (host-validated; non-fuels are rejected).
        void OnDepositFuel() => SubmitHeldItemTransfer(isFuel: true);

        void OnWithdrawInput() => SubmitStationWithdrawal(isFuel: false);

        void OnWithdrawFuel() => SubmitStationWithdrawal(isFuel: true);

        void SubmitHeldItemTransfer(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationHoldItem));
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationDepositFuel(stationPosition, held.ItemId, 1, out sentToHost)
                : survivalSync.TrySubmitStationDepositInput(stationPosition, held.ItemId, 1, out sentToHost);

            SetStatus(result.Accepted
                ? isFuel
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationFuelAdded)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationInputAdded)
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotDeposit,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
            RefreshDisplay();
        }

        void SubmitStationWithdrawal(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack target = isFuel ? station.Fuel : FirstInputStack();
            if (target.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.StationCannotWithdraw,
                    BlockiverseLocalization.DisplayName(SurvivalCommandFailureReason.StationRejected)));
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationWithdrawFuel(stationPosition, target.ItemId, target.Count, out sentToHost)
                : survivalSync.TrySubmitStationWithdrawInput(stationPosition, target.ItemId, target.Count, out sentToHost);

            SetStatus(result.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StationWithdrew, FormatStack(result.Item))
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotWithdraw,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
            RefreshDisplay();
        }

        void OnCollectOutput()
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            SurvivalCommandResult result = survivalSync.TrySubmitStationCollect(stationPosition, out bool sentToHost);
            SetStatus(result.Accepted
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StationCollected, FormatStack(result.Item))
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonSending)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.StationCannotCollect,
                        BlockiverseLocalization.DisplayName(result.FailureReason)));
            RefreshDisplay();
        }

        ItemStack FirstInputStack()
        {
            if (station == null)
                return ItemStack.Empty;

            for (int i = 0; i < station.InputSlotCount; i++)
            {
                ItemStack input = station.GetInput(i);
                if (!input.IsEmpty)
                    return input;
            }

            return ItemStack.Empty;
        }

        bool DiscoverSurvivalSync()
        {
            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>();

            return survivalSync != null;
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        // Player-facing labels use registry display names ("Iron Ingot"), never raw canonical
        // ids ("iron_ingot"), matching SurvivalCraftingPanel.FormatStack. Falls back to the
        // default registry when no shared instance was injected via ConfigureItemRegistry.
        string FormatStack(ItemStack stack)
        {
            itemRegistry ??= ItemRegistry.Default;
            return BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.StationStack,
                itemRegistry.Get(stack.ItemId).Name,
                stack.Count);
        }

        void RefreshDisplay()
        {
            if (station == null)
                return;

            RefreshLabels();
            RefreshProgress();
        }

        void RefreshLabels()
        {
            lastContentVersion = station.ContentVersion;

            for (int i = 0; i < inputSlotLabels.Length; i++)
            {
                if (inputSlotLabels[i] == null) continue;
                ItemStack input = i < station.InputSlotCount ? station.GetInput(i) : ItemStack.Empty;
                inputSlotLabels[i].text = input.IsEmpty ? "—" : FormatStack(input);
            }

            if (fuelLabel != null)
            {
                fuelLabel.text = station.Fuel.IsEmpty
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StationNoFuel)
                    : FormatStack(station.Fuel);
            }

            if (outputLabel != null)
            {
                outputLabel.text = station.Output.IsEmpty
                    ? "—"
                    : FormatStack(station.Output);
            }

            if (statusLabel != null && station.IsActive)
                statusLabel.text = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonActive);

            if (progressSlider != null)
                progressSlider.maxValue = Mathf.Max(1, station.RequiredTicks);
        }

        void RefreshProgress()
        {
            if (progressSlider != null)
                progressSlider.value = station.IsActive ? displayProgressTicks : 0.0f;
        }
    }
}
