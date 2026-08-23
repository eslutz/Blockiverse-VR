using System;
using System.Collections.Generic;
using System.Text;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseStationPanel (matrix row 20) — the Clay Kiln / Bellows
    // Forge panel. Displays the bound SmeltingStationModel and routes deposit/withdraw/collect
    // actions through the host-authoritative survival sync. The screen never ticks the model —
    // the host's MultiplayerSurvivalSync owns station ticking from WorldTimeClock; client
    // mirrors update from snapshots. Slot/fuel/output labels rebuild only when the model's
    // ContentVersion changes (per-frame text rebuilds are the cost this gate exists to avoid);
    // the progress slider mirrors ProgressTicks every frame with no UI-side station clock.
    //
    // Lifecycle parity with the uGUI panel: Open(model, position) binds, Close() just drops the
    // model. IUiToolkitStationScreen (IsOpenAt/CloseView) is the frontend seam
    // BlockiverseMenuController uses to force-close the screen when the backing station block
    // is removed. Nothing routes TO station_menu yet (the uGUI stationPanel.Open has no runtime
    // caller either); the open surface is ported faithfully for the interaction wire-up to call.
    [UiToolkitScreen(MenuActions.StationMenuScreen, "Assets/Blockiverse/UI/Documents/StationScreen.uxml",
        1000, 760, UiToolkitPlacementProfile.Menu)]
    public sealed class StationScreenController : UiToolkitScreenController, IUiToolkitStationScreen
    {
        const string StationTitleKey = "ui.generated.station.title";
        const string IdleKey = "ui.generated.station.idle";
        const string ActiveKey = "ui.common.active";
        const string SendingKey = "ui.common.sending";
        const string HoldItemKey = "ui.status.station.hold_item";
        const string FuelAddedKey = "ui.status.station.fuel_added";
        const string InputAddedKey = "ui.status.station.input_added";
        const string CannotDepositKey = "ui.status.station.cannot_deposit";
        const string WithdrewKey = "ui.status.station.withdrew";
        const string CannotWithdrawKey = "ui.status.station.cannot_withdraw";
        const string CollectedKey = "ui.status.station.collected";
        const string CannotCollectKey = "ui.status.station.cannot_collect";
        const string NoFuelKey = "ui.status.station.no_fuel";
        const string StationStackKey = "ui.status.station.stack";

        const string CraftingStationValueKeyPrefix = "ui.value.crafting_station.";
        const string SurvivalCommandFailureValueKeyPrefix = "ui.value.survival_command_failure.";

        const string StatusConfirmedClassName = "hs-status--confirmed";
        const string StatusRefusedClassName = "hs-status--refused";

        // The empty-slot glyph, byte-identical to the uGUI panel's rendering.
        const string EmptySlotText = "—";

        enum StatusTone
        {
            Neutral,
            Confirmed,
            Refused
        }

        Label titleLabel;
        readonly Label[] inputSlotLabels = new Label[SmeltingStationModel.MaxInputSlots];
        Label fuelLabel;
        Label outputLabel;
        Label statusLabel;
        Slider progressSlider;
        Button closeButton;
        Button depositInputButton;
        Button depositFuelButton;
        Button collectOutputButton;
        Button withdrawInputButton;
        Button withdrawFuelButton;

        EventCallback<ClickEvent> closeClickCallback;
        EventCallback<ClickEvent> depositInputClickCallback;
        EventCallback<ClickEvent> depositFuelClickCallback;
        EventCallback<ClickEvent> collectClickCallback;
        EventCallback<ClickEvent> withdrawInputClickCallback;
        EventCallback<ClickEvent> withdrawFuelClickCallback;

        MultiplayerSurvivalSync survivalSync;
        ItemRegistry itemRegistry;
        SmeltingStationModel station;
        BlockPosition stationPosition;
        string customTitle;
        float displayProgressTicks;
        int lastModelProgressTicks;
        int lastContentVersion = -1;

        string statusText = string.Empty;
        StatusTone statusTone = StatusTone.Neutral;

        public event Action CloseRequested;

        public bool IsOpen => station != null;
        public BlockPosition OpenPosition => stationPosition;

        public override string ScreenId => MenuActions.StationMenuScreen;

        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync) => survivalSync = sync;

        public void ConfigureItemRegistry(ItemRegistry registry) => itemRegistry = registry;

        // ---- IUiToolkitStationScreen (the close-on-station-removed seam) ----

        public bool IsOpenAt(BlockPosition position) => station != null && stationPosition.Equals(position);

        public void CloseView() => Close();

        public void Open(SmeltingStationModel model, BlockPosition position, string displayTitle = null)
        {
            station = model;
            stationPosition = position;
            customTitle = model != null ? displayTitle : null;
            displayProgressTicks = model?.ProgressTicks ?? 0;
            lastModelProgressTicks = model?.ProgressTicks ?? 0;
            if (model != null)
            {
                RenderTitle();
                SetStatus(
                    model.IsActive ? UiText.Get(ActiveKey) : UiText.Get(IdleKey),
                    StatusTone.Neutral);
            }

            RefreshDisplay();
        }

        public void Close() => station = null;

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            titleLabel = Require<Label>(root, "bv-station-title", ref allFound);
            for (int i = 0; i < SmeltingStationModel.MaxInputSlots; i++)
                inputSlotLabels[i] = Require<Label>(root, "bv-station-input-" + (i + 1), ref allFound);
            fuelLabel = Require<Label>(root, "bv-station-fuel", ref allFound);
            outputLabel = Require<Label>(root, "bv-station-output", ref allFound);
            statusLabel = Require<Label>(root, "bv-station-status", ref allFound);
            progressSlider = Require<Slider>(root, "bv-station-progress", ref allFound);
            closeButton = Require<Button>(root, "bv-station-close", ref allFound);
            depositInputButton = Require<Button>(root, "bv-station-add-input", ref allFound);
            depositFuelButton = Require<Button>(root, "bv-station-add-fuel", ref allFound);
            collectOutputButton = Require<Button>(root, "bv-station-collect", ref allFound);
            withdrawInputButton = Require<Button>(root, "bv-station-withdraw-input", ref allFound);
            withdrawFuelButton = Require<Button>(root, "bv-station-withdraw-fuel", ref allFound);

            // Display-only progress: picking-mode does not cascade, so every internal part of
            // the slider (tracker, dragger) must be silenced individually or the dragger still
            // takes rays meant for the world.
            if (progressSlider != null)
            {
                progressSlider.focusable = false;
                progressSlider.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            }

            // Brand-new elements know nothing: re-render the pending state into them.
            RenderTitle();
            ApplyStatusToLabel();
            RefreshDisplay();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            closeClickCallback = _ => CloseRequested?.Invoke();
            depositInputClickCallback = _ => SubmitDepositInput();
            depositFuelClickCallback = _ => SubmitDepositFuel();
            collectClickCallback = _ => SubmitCollect();
            withdrawInputClickCallback = _ => SubmitWithdrawInput();
            withdrawFuelClickCallback = _ => SubmitWithdrawFuel();

            closeButton?.RegisterCallback(closeClickCallback);
            depositInputButton?.RegisterCallback(depositInputClickCallback);
            depositFuelButton?.RegisterCallback(depositFuelClickCallback);
            collectOutputButton?.RegisterCallback(collectClickCallback);
            withdrawInputButton?.RegisterCallback(withdrawInputClickCallback);
            withdrawFuelButton?.RegisterCallback(withdrawFuelClickCallback);

            // The slot labels are ContentVersion-gated dynamic text and the title is a
            // runtime-resolved display name, so a live language switch must re-render both
            // (matrix §4) — static bindings cover only the captions and command buttons.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (closeClickCallback != null)
                closeButton?.UnregisterCallback(closeClickCallback);
            if (depositInputClickCallback != null)
                depositInputButton?.UnregisterCallback(depositInputClickCallback);
            if (depositFuelClickCallback != null)
                depositFuelButton?.UnregisterCallback(depositFuelClickCallback);
            if (collectClickCallback != null)
                collectOutputButton?.UnregisterCallback(collectClickCallback);
            if (withdrawInputClickCallback != null)
                withdrawInputButton?.UnregisterCallback(withdrawInputClickCallback);
            if (withdrawFuelClickCallback != null)
                withdrawFuelButton?.UnregisterCallback(withdrawFuelClickCallback);

            closeClickCallback = null;
            depositInputClickCallback = null;
            depositFuelClickCallback = null;
            collectClickCallback = null;
            withdrawInputClickCallback = null;
            withdrawFuelClickCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            titleLabel = null;
            for (int i = 0; i < inputSlotLabels.Length; i++)
                inputSlotLabels[i] = null;
            fuelLabel = null;
            outputLabel = null;
            statusLabel = null;
            progressSlider = null;
            closeButton = null;
            depositInputButton = null;
            depositFuelButton = null;
            collectOutputButton = null;
            withdrawInputButton = null;
            withdrawFuelButton = null;
        }

        void Update()
        {
            if (station == null)
                return;

            // The model is authoritative (host) or snapshot-fed (client mirror); the screen
            // mirrors its progress instead of running a separate UI-side station clock.
            if (station.ProgressTicks != lastModelProgressTicks)
            {
                lastModelProgressTicks = station.ProgressTicks;
                displayProgressTicks = station.ProgressTicks;
            }
            else
            {
                displayProgressTicks = station.IsActive ? station.ProgressTicks : 0.0f;
            }

            if (station.ContentVersion != lastContentVersion)
                RefreshLabels();
            RefreshProgress();
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            RenderTitle();
            if (station != null)
                RefreshLabels();
        }

        // Test/click seams for the transfer commands (EditMode cannot deliver a ClickEvent
        // without a runtime panel).

        // Deposits one of the held hotbar item into a station input slot (host-validated).
        public void SubmitDepositInput() => SubmitHeldItemTransfer(isFuel: false);

        // Deposits one of the held hotbar item as fuel (host-validated; non-fuels are rejected).
        public void SubmitDepositFuel() => SubmitHeldItemTransfer(isFuel: true);

        public void SubmitWithdrawInput() => SubmitStationWithdrawal(isFuel: false);

        public void SubmitWithdrawFuel() => SubmitStationWithdrawal(isFuel: true);

        public void SubmitCollect()
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            SurvivalCommandResult result = survivalSync.TrySubmitStationCollect(stationPosition, out bool sentToHost);
            if (result.Accepted)
                SetStatus(UiText.Format(CollectedKey, FormatStack(result.Item)), StatusTone.Confirmed);
            else if (sentToHost)
                SetStatus(UiText.Get(SendingKey), StatusTone.Neutral);
            else
                SetStatus(
                    UiText.Format(
                        CannotCollectKey,
                        EnumDisplayName(SurvivalCommandFailureValueKeyPrefix, result.FailureReason.ToString())),
                    StatusTone.Refused);
            RefreshDisplay();
        }

        void SubmitHeldItemTransfer(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus(UiText.Get(HoldItemKey), StatusTone.Refused);
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationDepositFuel(stationPosition, held.ItemId, 1, out sentToHost)
                : survivalSync.TrySubmitStationDepositInput(stationPosition, held.ItemId, 1, out sentToHost);

            if (result.Accepted)
                SetStatus(UiText.Get(isFuel ? FuelAddedKey : InputAddedKey), StatusTone.Confirmed);
            else if (sentToHost)
                SetStatus(UiText.Get(SendingKey), StatusTone.Neutral);
            else
                SetStatus(
                    UiText.Format(
                        CannotDepositKey,
                        EnumDisplayName(SurvivalCommandFailureValueKeyPrefix, result.FailureReason.ToString())),
                    StatusTone.Refused);
            RefreshDisplay();
        }

        void SubmitStationWithdrawal(bool isFuel)
        {
            if (station == null || !DiscoverSurvivalSync())
                return;

            ItemStack target = isFuel ? station.Fuel : FirstInputStack();
            if (target.IsEmpty)
            {
                SetStatus(
                    UiText.Format(
                        CannotWithdrawKey,
                        EnumDisplayName(
                            SurvivalCommandFailureValueKeyPrefix,
                            SurvivalCommandFailureReason.StationRejected.ToString())),
                    StatusTone.Refused);
                return;
            }

            bool sentToHost;
            SurvivalCommandResult result = isFuel
                ? survivalSync.TrySubmitStationWithdrawFuel(stationPosition, target.ItemId, target.Count, out sentToHost)
                : survivalSync.TrySubmitStationWithdrawInput(stationPosition, target.ItemId, target.Count, out sentToHost);

            if (result.Accepted)
                SetStatus(UiText.Format(WithdrewKey, FormatStack(result.Item)), StatusTone.Confirmed);
            else if (sentToHost)
                SetStatus(UiText.Get(SendingKey), StatusTone.Neutral);
            else
                SetStatus(
                    UiText.Format(
                        CannotWithdrawKey,
                        EnumDisplayName(SurvivalCommandFailureValueKeyPrefix, result.FailureReason.ToString())),
                    StatusTone.Refused);
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

        void RenderTitle()
        {
            if (titleLabel == null)
                return;

            titleLabel.text = station != null
                ? customTitle ?? EnumDisplayName(CraftingStationValueKeyPrefix, station.StationType.ToString())
                : UiText.Get(StationTitleKey);
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
                if (inputSlotLabels[i] == null)
                    continue;

                ItemStack input = i < station.InputSlotCount ? station.GetInput(i) : ItemStack.Empty;
                inputSlotLabels[i].text = input.IsEmpty ? EmptySlotText : FormatStack(input);
            }

            if (fuelLabel != null)
            {
                fuelLabel.text = station.Fuel.IsEmpty
                    ? UiText.Get(NoFuelKey)
                    : FormatStack(station.Fuel);
            }

            if (outputLabel != null)
            {
                outputLabel.text = station.Output.IsEmpty
                    ? EmptySlotText
                    : FormatStack(station.Output);
            }

            if (station.IsActive)
                SetStatus(UiText.Get(ActiveKey), StatusTone.Neutral);

            if (progressSlider != null)
                progressSlider.highValue = Mathf.Max(1, station.RequiredTicks);
        }

        void RefreshProgress()
        {
            if (progressSlider != null && station != null)
                progressSlider.SetValueWithoutNotify(station.IsActive ? displayProgressTicks : 0.0f);
        }

        // Player-facing labels use registry display names ("Iron Ingot"), never raw canonical
        // ids ("iron_ingot"). Falls back to the default registry when no shared instance was
        // injected via ConfigureItemRegistry.
        string FormatStack(ItemStack stack)
        {
            itemRegistry ??= ItemRegistry.Default;
            return UiText.Format(StationStackKey, itemRegistry.Get(stack.ItemId).Name, stack.Count);
        }

        void SetStatus(string message, StatusTone tone)
        {
            statusText = message ?? string.Empty;
            statusTone = tone;
            ApplyStatusToLabel();
        }

        void ApplyStatusToLabel()
        {
            if (statusLabel == null)
                return;

            statusLabel.text = statusText;
            statusLabel.EnableInClassList(StatusConfirmedClassName, statusTone == StatusTone.Confirmed);
            statusLabel.EnableInClassList(StatusRefusedClassName, statusTone == StatusTone.Refused);
        }

        // ui.value.* resolution with the same humanize fallback BlockiverseLocalization uses;
        // duplicated from CraftingScreenController deliberately — the two screens are the only
        // consumers and a shared helper file is outside this migration slice's file set.
        static string EnumDisplayName(string keyPrefix, string enumValueName)
        {
            string key = keyPrefix + ToSnakeCase(enumValueName);
            string resolved = UiText.Get(key);
            return string.Equals(resolved, key, StringComparison.Ordinal)
                ? HumanizeEnumName(enumValueName)
                : resolved;
        }

        static string ToSnakeCase(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (builder.Length > 0 &&
                    char.IsUpper(character) &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
                previous = character;
            }

            return builder.ToString();
        }

        static string HumanizeEnumName(string value)
        {
            var words = new List<string>();
            var builder = new StringBuilder(value.Length);
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (builder.Length > 0 &&
                    char.IsUpper(character) &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                {
                    words.Add(builder.ToString());
                    builder.Clear();
                }

                builder.Append(character);
                previous = character;
            }

            if (builder.Length > 0)
                words.Add(builder.ToString());

            for (int i = 0; i < words.Count; i++)
            {
                string lower = words[i].ToLowerInvariant();
                bool lowerMinorWord = i > 0 && (lower == "a" || lower == "an" || lower == "the" || lower == "of" || lower == "to");
                words[i] = lowerMinorWord ? lower : char.ToUpperInvariant(lower[0]) + lower.Substring(1);
            }

            return string.Join(" ", words);
        }
    }
}
