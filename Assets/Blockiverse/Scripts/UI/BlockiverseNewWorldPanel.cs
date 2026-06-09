using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // View layer for the New World config screen (voxel_survival_menus §6.3). Wraps NewWorldConfig;
    // cycle buttons step each selector forward or back. ActionRequested fires NewWorldCreate or
    // NewWorldCancel so BlockiverseMenuController can route the result.
    public sealed class BlockiverseNewWorldPanel : MonoBehaviour
    {
        TMP_InputField nameInput;
        TMP_InputField seedInput;
        Button[] cycleBackButtons;
        Button[] cycleNextButtons;
        TMP_Text[] cycleValueLabels;
        Button createButton;
        Button cancelButton;
        TMP_Text errorLabel;

        static readonly Action<NewWorldConfig>[] ForwardActions =
        {
            c => c.CycleGameMode(),
            c => c.CycleDifficulty(),
            c => c.CycleWorldSize(),
            c => c.CycleWorldPreset(),
            c => c.CycleStartingBiome(),
        };

        static readonly Action<NewWorldConfig>[] BackActions =
        {
            c => c.CycleGameMode(false),
            c => c.CycleDifficulty(false),
            c => c.CycleWorldSize(false),
            c => c.CycleWorldPreset(false),
            c => c.CycleStartingBiome(false),
        };

        static readonly Func<NewWorldConfig, string>[] ValueGetters =
        {
            c => c.GameMode,
            c => c.Difficulty,
            c => c.WorldSize,
            c => c.WorldPreset,
            c => c.StartingBiome,
        };

        public NewWorldConfig Config { get; private set; }
        public event Action<string> ActionRequested;

        public void Configure(
            TMP_InputField nameInput,
            TMP_InputField seedInput,
            Button[] cycleBackButtons,
            Button[] cycleNextButtons,
            TMP_Text[] cycleValueLabels,
            Button createButton,
            Button cancelButton,
            TMP_Text errorLabel)
        {
            this.nameInput = nameInput;
            this.seedInput = seedInput;
            this.cycleBackButtons = cycleBackButtons ?? Array.Empty<Button>();
            this.cycleNextButtons = cycleNextButtons ?? Array.Empty<Button>();
            this.cycleValueLabels = cycleValueLabels ?? Array.Empty<TMP_Text>();
            this.createButton = createButton;
            this.cancelButton = cancelButton;
            this.errorLabel = errorLabel;
            WireControls();
        }

        public void ResetForNewWorld()
        {
            Config = new NewWorldConfig();
            Config.SetName(NewWorldConfig.DefaultName);
            Config.RandomizeSeed(null);
            if (nameInput != null) nameInput.SetTextWithoutNotify(Config.Name);
            if (seedInput != null) seedInput.SetTextWithoutNotify(Config.SeedText);
            if (errorLabel != null) errorLabel.text = string.Empty;
            RefreshAllCycleLabels();
        }

        void WireControls()
        {
            nameInput?.onValueChanged.AddListener(v => Config?.SetName(v));
            seedInput?.onValueChanged.AddListener(v => Config?.SetSeed(v));

            for (int i = 0; i < cycleBackButtons.Length; i++)
            {
                int idx = i;
                cycleBackButtons[idx]?.onClick.AddListener(() => OnCycle(idx, forward: false));
            }

            for (int i = 0; i < cycleNextButtons.Length; i++)
            {
                int idx = i;
                cycleNextButtons[idx]?.onClick.AddListener(() => OnCycle(idx, forward: true));
            }

            createButton?.onClick.AddListener(OnCreate);
            cancelButton?.onClick.AddListener(() => ActionRequested?.Invoke(MenuActions.NewWorldCancel));
        }

        void OnCycle(int idx, bool forward)
        {
            if (Config == null || idx >= ForwardActions.Length) return;
            (forward ? ForwardActions : BackActions)[idx](Config);
            RefreshCycleLabel(idx);
        }

        void OnCreate()
        {
            if (Config == null) return;
            if (!Config.IsValid(out string error))
            {
                if (errorLabel != null) errorLabel.text = error;
                return;
            }
            if (errorLabel != null) errorLabel.text = string.Empty;
            ActionRequested?.Invoke(MenuActions.NewWorldCreate);
        }

        void RefreshCycleLabel(int idx)
        {
            if (Config == null || idx >= ValueGetters.Length) return;
            if (cycleValueLabels != null && idx < cycleValueLabels.Length && cycleValueLabels[idx] != null)
                cycleValueLabels[idx].text = ValueGetters[idx](Config);
        }

        void RefreshAllCycleLabels()
        {
            for (int i = 0; i < ValueGetters.Length; i++)
                RefreshCycleLabel(i);
        }
    }
}
