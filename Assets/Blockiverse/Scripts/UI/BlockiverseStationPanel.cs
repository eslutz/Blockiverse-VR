using System;
using Blockiverse.Survival;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // World-space panel for smelting stations (Clay Kiln / Bellows Forge). Displays the current
    // SmeltingStationModel state and drives its tick each Update frame while the panel is visible.
    // The panel is shown/hidden by BlockiverseMenuController when the player opens/closes a station.
    public sealed class BlockiverseStationPanel : MonoBehaviour
    {
        TMP_Text titleLabel;
        TMP_Text[] inputSlotLabels;
        TMP_Text fuelLabel;
        TMP_Text outputLabel;
        TMP_Text statusLabel;
        Slider progressSlider;
        Button closeButton;

        SmeltingStationModel station;

        public event Action CloseRequested;

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

        public void Open(SmeltingStationModel model, string displayTitle = null)
        {
            station = model;
            if (titleLabel != null)
                titleLabel.text = displayTitle ?? model.StationType.ToString();
            RefreshDisplay();
        }

        public void Close() => station = null;

        void Update()
        {
            if (station == null) return;
            int ticks = Mathf.Max(1, (int)(Time.deltaTime * SmeltingModel.TicksPerSecond));
            station.Tick(ticks);
            RefreshDisplay();
        }

        void RefreshDisplay()
        {
            if (station == null) return;

            for (int i = 0; i < inputSlotLabels.Length; i++)
            {
                if (inputSlotLabels[i] == null) continue;
                ItemStack input = station.GetInput(i);
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

            if (statusLabel != null)
                statusLabel.text = station.IsActive ? "Active" : "Idle";

            if (progressSlider != null)
            {
                progressSlider.maxValue = Mathf.Max(1, station.RequiredTicks);
                progressSlider.value = station.ProgressTicks;
            }
        }
    }
}
