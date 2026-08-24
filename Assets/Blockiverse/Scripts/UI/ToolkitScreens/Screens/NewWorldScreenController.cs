using System;
using Blockiverse.UI.Toolkit;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseNewWorldPanel (voxel_survival_menus §6.3). Wraps the same
    // pure NewWorldConfig model; the six selector rows cycle its canonical option lists in the
    // uGUI panel's fixed row order. Create validates through the model and dispatches
    // new_world.create; Cancel dispatches new_world.cancel. Like the uGUI panel, this screen
    // plays no feedback cues of its own — show/hide cues come from the host, and the config
    // itself has no per-interaction cue.
    [UiToolkitScreen(MenuActions.NewWorldScreen, "Assets/Blockiverse/UI/Documents/NewWorldScreen.uxml",
        900, 1234, UiToolkitPlacementProfile.Menu)]
    public sealed class NewWorldScreenController : UiToolkitScreenController, IUiToolkitNewWorldScreen
    {
        // Row order is the uGUI panel's CycleRowNames order; the three tables below and the
        // UXML rows are indexed together and must never be reordered independently.
        static readonly string[] SelectorElementPrefixes =
        {
            "bv-game-mode",
            "bv-difficulty",
            "bv-world-size",
            "bv-world-preset",
            "bv-starting-biome",
            "bv-texture-set",
        };

        static readonly Action<NewWorldConfig, bool>[] CycleActions =
        {
            (config, forward) => config.CycleGameMode(forward),
            (config, forward) => config.CycleDifficulty(forward),
            (config, forward) => config.CycleWorldSize(forward),
            (config, forward) => config.CycleWorldPreset(forward),
            (config, forward) => config.CycleStartingBiome(forward),
            (config, forward) => config.CycleTextureSet(forward),
        };

        static readonly Func<NewWorldConfig, string>[] ValueGetters =
        {
            config => config.GameMode,
            config => config.Difficulty,
            config => config.WorldSize,
            config => config.WorldPreset,
            config => config.StartingBiome,
            config => config.TextureSet,
        };

        // Canonical option ids are lowercase snake_case, so prefix + id is exactly what
        // BlockiverseLocalization.NormalizeKey would produce — no normalization pass needed.
        const string CanonicalValueKeyPrefix = "ui.value.canonical.";
        const string StatusRejectedClassName = "hs-status--rejected";

        TextField nameField;
        TextField seedField;
        readonly Button[] backButtons = new Button[SelectorElementPrefixes.Length];
        readonly Button[] nextButtons = new Button[SelectorElementPrefixes.Length];
        readonly Label[] valueLabels = new Label[SelectorElementPrefixes.Length];
        Button createButton;
        Button cancelButton;
        Label statusLabel;

        EventCallback<ChangeEvent<string>> nameChangedCallback;
        EventCallback<ChangeEvent<string>> seedChangedCallback;
        EventCallback<ClickEvent>[] backClickCallbacks;
        EventCallback<ClickEvent>[] nextClickCallbacks;
        EventCallback<ClickEvent> createClickCallback;
        EventCallback<ClickEvent> cancelClickCallback;

        NewWorldConfig config;

        public override string ScreenId => MenuActions.NewWorldScreen;

        // Pending-state read for the session controller (host → PendingNewWorldConfig). Null
        // until the first ResetForNewWorld, exactly like the uGUI panel's Config. Text is
        // pulled from the fields at read time: the value-changed callbacks keep the config
        // current at runtime, but a detached panel (EditMode tests, tree rebuilds) never
        // fires ChangeEvents, and the config must still reflect what the fields show.
        public NewWorldConfig Config
        {
            get
            {
                SyncFieldsIntoConfig();
                return config;
            }
        }

        public void ResetForNewWorld()
        {
            config = new NewWorldConfig();
            config.SetName(NewWorldConfig.DefaultName);
            config.RandomizeSeed(null);
            ApplyConfigToElements();
            SetStatus(string.Empty);
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            nameField = Require<TextField>(root, "bv-name-field", ref allFound);
            seedField = Require<TextField>(root, "bv-seed-field", ref allFound);

            for (int i = 0; i < SelectorElementPrefixes.Length; i++)
            {
                string prefix = SelectorElementPrefixes[i];
                backButtons[i] = Require<Button>(root, prefix + "-back", ref allFound);
                valueLabels[i] = Require<Label>(root, prefix + "-value", ref allFound);
                nextButtons[i] = Require<Button>(root, prefix + "-next", ref allFound);
            }

            createButton = Require<Button>(root, "bv-create", ref allFound);
            cancelButton = Require<Button>(root, "bv-cancel", ref allFound);
            statusLabel = Require<Label>(root, "bv-status", ref allFound);

            // A rebuild mid-session gets brand-new blank elements; re-render the pending
            // config into them so the screen does not come back empty.
            if (config != null)
                ApplyConfigToElements();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            nameChangedCallback = evt => config?.SetName(evt.newValue);
            seedChangedCallback = evt => config?.SetSeed(evt.newValue);
            nameField?.RegisterValueChangedCallback(nameChangedCallback);
            seedField?.RegisterValueChangedCallback(seedChangedCallback);

            backClickCallbacks = new EventCallback<ClickEvent>[SelectorElementPrefixes.Length];
            nextClickCallbacks = new EventCallback<ClickEvent>[SelectorElementPrefixes.Length];

            for (int i = 0; i < SelectorElementPrefixes.Length; i++)
            {
                int index = i;
                backClickCallbacks[i] = _ => CycleSelector(index, forward: false);
                nextClickCallbacks[i] = _ => CycleSelector(index, forward: true);
                backButtons[i]?.RegisterCallback(backClickCallbacks[i]);
                nextButtons[i]?.RegisterCallback(nextClickCallbacks[i]);
            }

            createClickCallback = _ => SubmitCreate();
            cancelClickCallback = _ => SubmitCancel();
            createButton?.RegisterCallback(createClickCallback);
            cancelButton?.RegisterCallback(cancelClickCallback);

            // The six value labels are dynamic text (UiText); static labels update through
            // their own bindings (matrix §4: locale change).
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            nameField?.UnregisterValueChangedCallback(nameChangedCallback);
            seedField?.UnregisterValueChangedCallback(seedChangedCallback);
            nameChangedCallback = null;
            seedChangedCallback = null;

            for (int i = 0; i < SelectorElementPrefixes.Length; i++)
            {
                if (backClickCallbacks != null && backClickCallbacks[i] != null)
                    backButtons[i]?.UnregisterCallback(backClickCallbacks[i]);
                if (nextClickCallbacks != null && nextClickCallbacks[i] != null)
                    nextButtons[i]?.UnregisterCallback(nextClickCallbacks[i]);
            }

            backClickCallbacks = null;
            nextClickCallbacks = null;

            if (createClickCallback != null)
                createButton?.UnregisterCallback(createClickCallback);
            if (cancelClickCallback != null)
                cancelButton?.UnregisterCallback(cancelClickCallback);
            createClickCallback = null;
            cancelClickCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            nameField = null;
            seedField = null;

            for (int i = 0; i < SelectorElementPrefixes.Length; i++)
            {
                backButtons[i] = null;
                nextButtons[i] = null;
                valueLabels[i] = null;
            }

            createButton = null;
            cancelButton = null;
            statusLabel = null;
        }

        // Public handler seams (also the click targets): mirror the uGUI panel's OnCycle /
        // OnCreate / cancel-click semantics, including its silent no-op while Config is null
        // (the host resets the screen before routing to it, so null means "never opened").
        public void CycleSelector(int selectorIndex, bool forward)
        {
            if (config == null || selectorIndex < 0 || selectorIndex >= CycleActions.Length)
                return;

            CycleActions[selectorIndex](config, forward);
            RefreshSelectorValueLabel(selectorIndex);
        }

        public void SubmitCreate()
        {
            if (config == null)
                return;

            SyncFieldsIntoConfig();

            if (!config.IsValid(out string error))
            {
                SetStatus(error);
                return;
            }

            SetStatus(string.Empty);
            DispatchAction(MenuActions.NewWorldCreate);
        }

        public void SubmitCancel()
        {
            DispatchAction(MenuActions.NewWorldCancel);
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            RefreshAllSelectorValueLabels();
        }

        void SyncFieldsIntoConfig()
        {
            if (config == null)
                return;

            if (nameField != null)
                config.SetName(nameField.value);
            if (seedField != null)
                config.SetSeed(seedField.value);
        }

        void ApplyConfigToElements()
        {
            nameField?.SetValueWithoutNotify(config.Name);
            seedField?.SetValueWithoutNotify(config.SeedText);
            RefreshAllSelectorValueLabels();
        }

        void RefreshAllSelectorValueLabels()
        {
            for (int i = 0; i < ValueGetters.Length; i++)
                RefreshSelectorValueLabel(i);
        }

        void RefreshSelectorValueLabel(int selectorIndex)
        {
            if (config == null)
                return;

            Label label = valueLabels[selectorIndex];
            if (label != null)
                label.text = UiText.Get(CanonicalValueKeyPrefix + ValueGetters[selectorIndex](config));
        }

        void SetStatus(string message)
        {
            if (statusLabel == null)
                return;

            statusLabel.text = message;
            // Validation failures are rejections ("it will not work as configured"); colour
            // never carries the signal alone — the message text is the word.
            statusLabel.EnableInClassList(StatusRejectedClassName, !string.IsNullOrEmpty(message));
        }
    }
}
