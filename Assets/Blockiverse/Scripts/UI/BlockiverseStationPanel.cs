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
    // SmeltingStationModel and routes deposit/collect actions through the host-authoritative
    // survival sync. The panel never ticks the model — the host's MultiplayerSurvivalSync owns
    // station ticking; client mirrors update from snapshots and the progress bar extrapolates
    // between them. The panel is shown/hidden by BlockiverseMenuController.
    public sealed class BlockiverseStationPanel : MonoBehaviour
    {
        TMP_Text titleLabel;
        TMP_Text[] inputSlotLabels;
        TMP_Text fuelLabel;
        TMP_Text outputLabel;
        TMP_Text statusLabel;
        Slider progressSlider;
        Button closeButton;
        Button depositInputButton;
        Button depositFuelButton;
        Button collectOutputButton;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        SmeltingStationModel station;
        BlockPosition stationPosition;
        float displayProgressTicks;
        int lastModelProgressTicks;

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
            closeButton?.onClick.AddListener(() => CloseRequested?.Invoke());
        }

        public void ConfigureTransferControls(
            Button targetDepositInputButton,
            Button targetDepositFuelButton,
            Button targetCollectOutputButton)
        {
            depositInputButton = targetDepositInputButton;
            depositFuelButton = targetDepositFuelButton;
            collectOutputButton = targetCollectOutputButton;
            WireTransferButtons();
        }

        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void Open(SmeltingStationModel model, BlockPosition position, string displayTitle = null)
        {
            station = model;
            stationPosition = position;
            displayProgressTicks = model?.ProgressTicks ?? 0;
            lastModelProgressTicks = model?.ProgressTicks ?? 0;
            if (titleLabel != null && model != null)
                titleLabel.text = displayTitle ?? model.StationType.ToString();
            if (model != null)
                SetStatus(model.IsActive ? "Active" : "Idle");
            RefreshDisplay();
        }

        public void Close() => station = null;

        void Awake()
        {
            WireTransferButtons();
        }

        void Update()
        {
            if (station == null)
                return;

            // The model is authoritative (host) or snapshot-fed (client mirror); the panel only
            // extrapolates the progress bar between model updates.
            if (station.ProgressTicks != lastModelProgressTicks)
            {
                lastModelProgressTicks = station.ProgressTicks;
                displayProgressTicks = station.ProgressTicks;
            }
            else if (station.IsActive)
            {
                displayProgressTicks = Mathf.Min(
                    displayProgressTicks + Time.deltaTime * SmeltingModel.TicksPerSecond,
                    station.RequiredTicks);
            }
            else
            {
                displayProgressTicks = 0.0f;
            }

            RefreshDisplay();
        }

        void WireTransferButtons()
        {
            Wire(depositInputButton, OnDepositInput);
            Wire(depositFuelButton, OnDepositFuel);
            Wire(collectOutputButton, OnCollectOutput);
        }

        static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // Deposits one of the held hotbar item into a station input slot (host-validated).
        void OnDepositInput() => SubmitHeldItemTransfer(isFuel: false);

        // Deposits one of the held hotbar item as fuel (host-validated; non-fuels are rejected).
        void OnDepositFuel() => SubmitHeldItemTransfer(isFuel: true);

        void SubmitHeldItemTransfer(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus("Hold an item to deposit");
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationDepositFuel(stationPosition, held.ItemId, 1, out sentToHost)
                : survivalSync.TrySubmitStationDepositInput(stationPosition, held.ItemId, 1, out sentToHost);

            SetStatus(result.Accepted
                ? isFuel ? "Fuel added" : "Input added"
                : sentToHost
                    ? "Sending…"
                    : $"Cannot deposit: {result.FailureReason}");
            RefreshDisplay();
        }

        void OnCollectOutput()
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            SurvivalCommandResult result = survivalSync.TrySubmitStationCollect(stationPosition, out bool sentToHost);
            SetStatus(result.Accepted
                ? $"Collected {result.Item.ItemId.ToString().ToLower()} ×{result.Item.Count}"
                : sentToHost
                    ? "Sending…"
                    : $"Cannot collect: {result.FailureReason}");
            RefreshDisplay();
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

        void RefreshDisplay()
        {
            if (station == null)
                return;

            for (int i = 0; i < inputSlotLabels.Length; i++)
            {
                if (inputSlotLabels[i] == null) continue;
                ItemStack input = i < station.InputSlotCount ? station.GetInput(i) : ItemStack.Empty;
                inputSlotLabels[i].text = input.IsEmpty ? "—" : $"{input.ItemId.ToString().ToLower()} ×{input.Count}";
            }

            if (fuelLabel != null)
            {
                fuelLabel.text = station.Fuel.IsEmpty
                    ? "No fuel"
                    : $"{station.Fuel.ItemId.ToString().ToLower()} ×{station.Fuel.Count}";
            }

            if (outputLabel != null)
            {
                outputLabel.text = station.Output.IsEmpty
                    ? "—"
                    : $"{station.Output.ItemId.ToString().ToLower()} ×{station.Output.Count}";
            }

            if (statusLabel != null && station.IsActive)
                statusLabel.text = "Active";

            if (progressSlider != null)
            {
                progressSlider.maxValue = Mathf.Max(1, station.RequiredTicks);
                progressSlider.value = station.IsActive ? displayProgressTicks : 0.0f;
            }
        }
    }
}
