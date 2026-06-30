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
        [SerializeField] TMP_InputField nameInput;
        [SerializeField] TMP_InputField seedInput;
        [SerializeField] Button[] cycleBackButtons;
        [SerializeField] Button[] cycleNextButtons;
        [SerializeField] TMP_Text[] cycleValueLabels;
        [SerializeField] Button createButton;
        [SerializeField] Button cancelButton;
        [SerializeField] TMP_Text errorLabel;

        static readonly Action<NewWorldConfig>[] ForwardActions =
        {
            c => c.CycleGameMode(),
            c => c.CycleDifficulty(),
            c => c.CycleWorldSize(),
            c => c.CycleWorldPreset(),
            c => c.CycleStartingBiome(),
            c => c.CycleTextureSet(),
        };

        static readonly Action<NewWorldConfig>[] BackActions =
        {
            c => c.CycleGameMode(false),
            c => c.CycleDifficulty(false),
            c => c.CycleWorldSize(false),
            c => c.CycleWorldPreset(false),
            c => c.CycleStartingBiome(false),
            c => c.CycleTextureSet(false),
        };

        static readonly Func<NewWorldConfig, string>[] ValueGetters =
        {
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.GameMode),
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.Difficulty),
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.WorldSize),
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.WorldPreset),
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.StartingBiome),
            c => BlockiverseLocalization.DisplayNameForCanonicalId(c.TextureSet),
        };

        static readonly string[] CycleRowNames =
        {
            "Game Mode",
            "Difficulty",
            "World Size",
            "World Preset",
            "Starting Biome",
            "Texture Set",
        };

        bool controlsWired;

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
            controlsWired = false;
            ConfigureNameInput();
            ConfigureSeedInput();
            ConfigureCycleButtonHitAreas();
            WireControls();
        }

        public void ResolveRuntimeReferences()
        {
            Transform root = transform.Find("Panel") ?? transform;
            bool changed = false;

            changed |= AssignIfMissing(ref nameInput, FindChildComponent<TMP_InputField>(root, "Name Input"));
            changed |= AssignIfMissing(ref seedInput, FindChildComponent<TMP_InputField>(root, "Seed Input"));

            if (NeedsRefresh(cycleBackButtons, CycleRowNames.Length))
            {
                cycleBackButtons = FindCycleButtons(root, "Back");
                changed |= cycleBackButtons.Length > 0;
            }

            if (NeedsRefresh(cycleNextButtons, CycleRowNames.Length))
            {
                cycleNextButtons = FindCycleButtons(root, "Next");
                changed |= cycleNextButtons.Length > 0;
            }

            if (NeedsRefresh(cycleValueLabels, CycleRowNames.Length))
            {
                cycleValueLabels = FindCycleLabels(root);
                changed |= cycleValueLabels.Length > 0;
            }

            changed |= AssignIfMissing(ref createButton, FindChildComponent<Button>(root, "Create Button"));
            changed |= AssignIfMissing(ref cancelButton, FindChildComponent<Button>(root, "Cancel Button"));
            changed |= AssignIfMissing(ref errorLabel, FindChildComponent<TMP_Text>(root, "Error"));

            ConfigureNameInput();
            ConfigureSeedInput();
            ConfigureCycleButtonHitAreas();

            if (changed)
                controlsWired = false;

            WireControls();
        }

        public void ResetForNewWorld()
        {
            ResolveRuntimeReferences();
            Config = new NewWorldConfig();
            Config.SetName(NewWorldConfig.DefaultName);
            Config.RandomizeSeed(null);
            if (nameInput != null) nameInput.SetTextWithoutNotify(Config.Name);
            if (seedInput != null) seedInput.SetTextWithoutNotify(Config.SeedText);
            if (errorLabel != null) errorLabel.text = string.Empty;
            RefreshAllCycleLabels();
        }

        void Awake()
        {
            ResolveRuntimeReferences();
        }

        void WireControls()
        {
            if (controlsWired)
                return;

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
            controlsWired = true;
        }

        void ConfigureNameInput()
        {
            ConfigureEditableTextInput(nameInput);
        }

        void ConfigureSeedInput()
        {
            ConfigureEditableTextInput(seedInput);
        }

        static void ConfigureEditableTextInput(TMP_InputField input)
        {
            if (input == null)
                return;

            input.interactable = true;
            input.readOnly = false;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.keyboardType = TouchScreenKeyboardType.Default;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            input.inputType = TMP_InputField.InputType.Standard;

            if (input.targetGraphic == null && input.TryGetComponent(out Graphic inputGraphic))
                input.targetGraphic = inputGraphic;

            if (input.targetGraphic != null)
                input.targetGraphic.raycastTarget = true;

            if (input.textComponent != null)
                input.textComponent.raycastTarget = false;

            if (input.placeholder is Graphic placeholderGraphic)
                placeholderGraphic.raycastTarget = false;

            Blockiverse.Gameplay.IBlockiverseSystemKeyboardField keyboardField = input.GetComponent<Blockiverse.Gameplay.IBlockiverseSystemKeyboardField>();
            keyboardField?.Configure(input, TouchScreenKeyboardType.Default);
        }

        void ConfigureCycleButtonHitAreas()
        {
            ConfigureButtonHitAreas(cycleBackButtons);
            ConfigureButtonHitAreas(cycleNextButtons);
        }

        static void ConfigureButtonHitAreas(Button[] buttons)
        {
            if (buttons == null)
                return;

            for (int i = 0; i < buttons.Length; i++)
                ConfigureButtonHitArea(buttons[i]);
        }

        static void ConfigureButtonHitArea(Button button)
        {
            if (button == null)
                return;

            if (button.targetGraphic == null && button.TryGetComponent(out Graphic rootGraphic))
                button.targetGraphic = rootGraphic;

            if (button.targetGraphic != null)
                button.targetGraphic.raycastTarget = true;

            foreach (Graphic childGraphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if (button.targetGraphic != null && childGraphic == button.targetGraphic)
                    continue;
                childGraphic.raycastTarget = false;
            }
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

        static Button[] FindCycleButtons(Transform root, string buttonName)
        {
            var buttons = new Button[CycleRowNames.Length];
            for (int i = 0; i < CycleRowNames.Length; i++)
                buttons[i] = FindChildComponent<Button>(root, $"Row {CycleRowNames[i]}/{buttonName}");
            return buttons;
        }

        static TMP_Text[] FindCycleLabels(Transform root)
        {
            var labels = new TMP_Text[CycleRowNames.Length];
            for (int i = 0; i < CycleRowNames.Length; i++)
                labels[i] = FindChildComponent<TMP_Text>(root, $"Row {CycleRowNames[i]}/Value");
            return labels;
        }

        static T FindChildComponent<T>(Transform root, string path) where T : Component
        {
            Transform child = root != null ? root.Find(path) : null;
            return child != null ? child.GetComponent<T>() : null;
        }

        static bool AssignIfMissing<T>(ref T target, T value) where T : Component
        {
            if (target != null || value == null)
                return false;

            target = value;
            return true;
        }

        static bool NeedsRefresh<T>(T[] values, int expectedLength) where T : class
        {
            if (values == null || values.Length != expectedLength || values.Length == 0)
                return true;

            for (int i = 0; i < values.Length; i++)
                if (values[i] == null)
                    return true;
            return false;
        }
    }
}
