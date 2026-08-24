using System.Globalization;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseComfortMenu (matrix row 11): every toggle and slider maps
    // one comfort setting, the Glide/Teleport pair behaves as a radio group that can never be
    // both-off, and push-down uses SetValueWithoutNotify everywhere so mirroring settings into
    // the widgets can never echo back into the settings (matrix §4 items 10 and 14).
    //
    // Deliberate deviation from the uGUI panel: its UnregisterControlCallbacks omitted 8 of
    // the 20 registered controls (toggleToMine, realPlayerHeight, sprint, crouch, and the four
    // swim controls) — a known defect, not a behaviour. This controller unregisters every
    // callback it registers, and RegisteredElementCallbackCount exists so a test can falsify
    // an incomplete unregister (a leftover registration shows up as a count drift across
    // re-attach).
    [UiToolkitScreen(
        MenuActions.ComfortSettingsScreen,
        "Assets/Blockiverse/UI/Documents/ComfortSettingsScreen.uxml",
        1090,
        1346,
        UiToolkitPlacementProfile.Menu)]
    public sealed class ComfortSettingsScreenController : UiToolkitScreenController
    {
        // Requested ui.generated.comfort.* entries (none of the uGUI comfort strings exist in
        // the UI table yet). Until the entries land centrally, UiText.Get falls back to the
        // key string, so the screen stays legible rather than blank.
        const string TitleKey = "ui.generated.comfort.title";
        const string MovementModeKey = "ui.generated.comfort.movement_mode";
        const string GlideMotionKey = "ui.generated.comfort.glide_motion";
        const string TeleportKey = "ui.generated.comfort.teleport";
        const string MoveSpeedKey = "ui.generated.comfort.move_speed";
        const string WalkHeadBobKey = "ui.generated.comfort.walk_head_bob";
        const string TurningKey = "ui.generated.comfort.turning";
        const string SmoothTurnKey = "ui.generated.comfort.smooth_turn";
        const string SnapTurnKey = "ui.generated.comfort.snap_turn";
        const string TurnAroundKey = "ui.generated.comfort.turn_around";
        const string SmoothTurnSpeedKey = "ui.generated.comfort.smooth_turn_speed";
        const string ControlOptionsKey = "ui.generated.comfort.control_options";
        const string LeftHandedKey = "ui.generated.comfort.left_handed";
        const string ToggleToMineKey = "ui.generated.comfort.toggle_to_mine";
        const string SprintToggleKey = "ui.generated.comfort.sprint_toggle";
        const string CrouchToggleKey = "ui.generated.comfort.crouch_toggle";
        const string ViewComfortKey = "ui.generated.comfort.view_comfort";
        const string MotionVignetteKey = "ui.generated.comfort.motion_vignette";
        const string VignetteStrengthKey = "ui.generated.comfort.vignette_strength";
        const string PlayerViewKey = "ui.generated.comfort.player_view";
        const string ResetHeightKey = "ui.generated.comfort.reset_height";
        const string RealPlayerHeightKey = "ui.generated.comfort.real_player_height";
        const string UiScaleKey = "ui.generated.comfort.ui_scale";
        const string SwimSinkKey = "ui.generated.comfort.swim_sink";
        const string SwimVignetteKey = "ui.generated.comfort.swim_vignette";
        const string SwimClimbOutKey = "ui.generated.comfort.swim_climb_out";
        const string SwimSpeedKey = "ui.generated.comfort.swim_speed";

        Label titleLabel;
        Button closeButton;
        Label movementHeading;
        Label turningHeading;
        Label controlOptionsHeading;
        Label viewComfortHeading;
        Label playerViewHeading;

        Toggle glideToggle;
        Toggle teleportToggle;
        Toggle glideBobToggle;
        Toggle smoothTurnToggle;
        Toggle turnAroundToggle;
        Toggle leftHandToggle;
        Toggle toggleToMineToggle;
        Toggle sprintToggleToggle;
        Toggle crouchToggleToggle;
        Toggle vignetteToggle;
        Toggle realPlayerHeightToggle;
        Toggle swimPassiveSinkToggle;
        Toggle swimVignetteToggle;
        Toggle swimClimbOutToggle;

        Slider moveSpeedSlider;
        Slider snapTurnSlider;
        Slider smoothTurnSpeedSlider;
        Slider vignetteStrengthSlider;
        Slider uiScaleSlider;
        Slider swimSpeedSlider;

        Label moveSpeedValueLabel;
        Label snapTurnValueLabel;
        Label smoothTurnSpeedValueLabel;
        Label vignetteStrengthValueLabel;
        Label uiScaleValueLabel;
        Label swimSpeedValueLabel;

        Button heightResetButton;

        BlockiverseComfortSettings settings;
        IBlockiverseHeightReset heightReset;
        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        EventCallback<ChangeEvent<bool>> onGlideChanged;
        EventCallback<ChangeEvent<bool>> onTeleportChanged;
        EventCallback<ChangeEvent<bool>> onToggleChanged;
        EventCallback<ChangeEvent<float>> onSliderChanged;
        EventCallback<ClickEvent> onCloseClicked;
        EventCallback<ClickEvent> onHeightResetClicked;

        public override string ScreenId => MenuActions.ComfortSettingsScreen;

        // Mirrors the actual element Register/Unregister calls one-for-one. An unregister that
        // skips a control (the uGUI defect this port fixes) leaves the count above the
        // registered set size after a re-attach cycle.
        public int RegisteredElementCallbackCount { get; private set; }

        public void ConfigureSettings(BlockiverseComfortSettings comfortSettings) =>
            settings = comfortSettings;

        public void ConfigureHeightReset(IBlockiverseHeightReset targetHeightReset) =>
            heightReset = targetHeightReset;

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            titleLabel = Require<Label>(root, "bv-comfort-title", ref allFound);
            closeButton = Require<Button>(root, "bv-comfort-close", ref allFound);
            movementHeading = Require<Label>(root, "bv-comfort-movement-heading", ref allFound);
            turningHeading = Require<Label>(root, "bv-comfort-turning-heading", ref allFound);
            controlOptionsHeading = Require<Label>(root, "bv-comfort-control-options-heading", ref allFound);
            viewComfortHeading = Require<Label>(root, "bv-comfort-view-comfort-heading", ref allFound);
            playerViewHeading = Require<Label>(root, "bv-comfort-player-view-heading", ref allFound);

            glideToggle = Require<Toggle>(root, "bv-comfort-glide", ref allFound);
            teleportToggle = Require<Toggle>(root, "bv-comfort-teleport", ref allFound);
            glideBobToggle = Require<Toggle>(root, "bv-comfort-glide-bob", ref allFound);
            smoothTurnToggle = Require<Toggle>(root, "bv-comfort-smooth-turn", ref allFound);
            turnAroundToggle = Require<Toggle>(root, "bv-comfort-turn-around", ref allFound);
            leftHandToggle = Require<Toggle>(root, "bv-comfort-left-hand", ref allFound);
            toggleToMineToggle = Require<Toggle>(root, "bv-comfort-toggle-to-mine", ref allFound);
            sprintToggleToggle = Require<Toggle>(root, "bv-comfort-sprint-toggle", ref allFound);
            crouchToggleToggle = Require<Toggle>(root, "bv-comfort-crouch-toggle", ref allFound);
            vignetteToggle = Require<Toggle>(root, "bv-comfort-vignette", ref allFound);
            realPlayerHeightToggle = Require<Toggle>(root, "bv-comfort-real-height", ref allFound);
            swimPassiveSinkToggle = Require<Toggle>(root, "bv-comfort-swim-sink", ref allFound);
            swimVignetteToggle = Require<Toggle>(root, "bv-comfort-swim-vignette", ref allFound);
            swimClimbOutToggle = Require<Toggle>(root, "bv-comfort-swim-climb-out", ref allFound);

            moveSpeedSlider = Require<Slider>(root, "bv-comfort-move-speed", ref allFound);
            snapTurnSlider = Require<Slider>(root, "bv-comfort-snap-turn", ref allFound);
            smoothTurnSpeedSlider = Require<Slider>(root, "bv-comfort-smooth-turn-speed", ref allFound);
            vignetteStrengthSlider = Require<Slider>(root, "bv-comfort-vignette-strength", ref allFound);
            uiScaleSlider = Require<Slider>(root, "bv-comfort-ui-scale", ref allFound);
            swimSpeedSlider = Require<Slider>(root, "bv-comfort-swim-speed", ref allFound);

            moveSpeedValueLabel = Require<Label>(root, "bv-comfort-move-speed-value", ref allFound);
            snapTurnValueLabel = Require<Label>(root, "bv-comfort-snap-turn-value", ref allFound);
            smoothTurnSpeedValueLabel = Require<Label>(root, "bv-comfort-smooth-turn-speed-value", ref allFound);
            vignetteStrengthValueLabel = Require<Label>(root, "bv-comfort-vignette-strength-value", ref allFound);
            uiScaleValueLabel = Require<Label>(root, "bv-comfort-ui-scale-value", ref allFound);
            swimSpeedValueLabel = Require<Label>(root, "bv-comfort-swim-speed-value", ref allFound);

            heightResetButton = Require<Button>(root, "bv-comfort-height-reset", ref allFound);

            if (settings == null)
                settings = BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);

            ApplyStaticLabels();
            RefreshFromSettings();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            onGlideChanged ??= changeEvent => ApplyGlideToggled(changeEvent.newValue);
            onTeleportChanged ??= changeEvent => ApplyTeleportToggled(changeEvent.newValue);
            onToggleChanged ??= _ => ApplyOtherControlsWithFeedback();
            onSliderChanged ??= _ => ApplyOtherControlsWithFeedback();
            onCloseClicked ??= _ => RequestClose();
            onHeightResetClicked ??= _ => ResetPlayerHeight();

            RegisterToggle(glideToggle, onGlideChanged);
            RegisterToggle(teleportToggle, onTeleportChanged);
            RegisterToggle(glideBobToggle, onToggleChanged);
            RegisterToggle(smoothTurnToggle, onToggleChanged);
            RegisterToggle(turnAroundToggle, onToggleChanged);
            RegisterToggle(leftHandToggle, onToggleChanged);
            RegisterToggle(toggleToMineToggle, onToggleChanged);
            RegisterToggle(sprintToggleToggle, onToggleChanged);
            RegisterToggle(crouchToggleToggle, onToggleChanged);
            RegisterToggle(vignetteToggle, onToggleChanged);
            RegisterToggle(realPlayerHeightToggle, onToggleChanged);
            RegisterToggle(swimPassiveSinkToggle, onToggleChanged);
            RegisterToggle(swimVignetteToggle, onToggleChanged);
            RegisterToggle(swimClimbOutToggle, onToggleChanged);

            RegisterSlider(moveSpeedSlider, onSliderChanged);
            RegisterSlider(snapTurnSlider, onSliderChanged);
            RegisterSlider(smoothTurnSpeedSlider, onSliderChanged);
            RegisterSlider(vignetteStrengthSlider, onSliderChanged);
            RegisterSlider(uiScaleSlider, onSliderChanged);
            RegisterSlider(swimSpeedSlider, onSliderChanged);

            RegisterButton(closeButton, onCloseClicked);
            RegisterButton(heightResetButton, onHeightResetClicked);

            // Every visible label on this screen is dynamic UiText (no table entries yet), so
            // a locale change must re-resolve them; the Close button's native binding updates
            // itself.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            UnregisterToggle(glideToggle, onGlideChanged);
            UnregisterToggle(teleportToggle, onTeleportChanged);
            UnregisterToggle(glideBobToggle, onToggleChanged);
            UnregisterToggle(smoothTurnToggle, onToggleChanged);
            UnregisterToggle(turnAroundToggle, onToggleChanged);
            UnregisterToggle(leftHandToggle, onToggleChanged);
            UnregisterToggle(toggleToMineToggle, onToggleChanged);
            UnregisterToggle(sprintToggleToggle, onToggleChanged);
            UnregisterToggle(crouchToggleToggle, onToggleChanged);
            UnregisterToggle(vignetteToggle, onToggleChanged);
            UnregisterToggle(realPlayerHeightToggle, onToggleChanged);
            UnregisterToggle(swimPassiveSinkToggle, onToggleChanged);
            UnregisterToggle(swimVignetteToggle, onToggleChanged);
            UnregisterToggle(swimClimbOutToggle, onToggleChanged);

            UnregisterSlider(moveSpeedSlider, onSliderChanged);
            UnregisterSlider(snapTurnSlider, onSliderChanged);
            UnregisterSlider(smoothTurnSpeedSlider, onSliderChanged);
            UnregisterSlider(vignetteStrengthSlider, onSliderChanged);
            UnregisterSlider(uiScaleSlider, onSliderChanged);
            UnregisterSlider(swimSpeedSlider, onSliderChanged);

            UnregisterButton(closeButton, onCloseClicked);
            UnregisterButton(heightResetButton, onHeightResetClicked);

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            titleLabel = null;
            closeButton = null;
            movementHeading = null;
            turningHeading = null;
            controlOptionsHeading = null;
            viewComfortHeading = null;
            playerViewHeading = null;

            glideToggle = null;
            teleportToggle = null;
            glideBobToggle = null;
            smoothTurnToggle = null;
            turnAroundToggle = null;
            leftHandToggle = null;
            toggleToMineToggle = null;
            sprintToggleToggle = null;
            crouchToggleToggle = null;
            vignetteToggle = null;
            realPlayerHeightToggle = null;
            swimPassiveSinkToggle = null;
            swimVignetteToggle = null;
            swimClimbOutToggle = null;

            moveSpeedSlider = null;
            snapTurnSlider = null;
            smoothTurnSpeedSlider = null;
            vignetteStrengthSlider = null;
            uiScaleSlider = null;
            swimSpeedSlider = null;

            moveSpeedValueLabel = null;
            snapTurnValueLabel = null;
            smoothTurnSpeedValueLabel = null;
            vignetteStrengthValueLabel = null;
            uiScaleValueLabel = null;
            swimSpeedValueLabel = null;

            heightResetButton = null;
        }

        // The uGUI panel synced only at Awake/configure time; re-syncing on every routed show
        // additionally covers settings changed while the screen was hidden (persistence load,
        // another session surface) without any behavioural cost — push-down never notifies.
        protected override void OnShown() => RefreshFromSettings();

        public void RequestClose() => DispatchAction(MenuActions.ComfortSettingsClose);

        public void ResetPlayerHeight()
        {
            if (heightReset == null)
                ResolveHeightReset();

            heightReset?.ResetHeight();
        }

        // Glide and Teleport are a radio pair: selecting one deselects the other, and
        // deselecting one implicitly selects the other so the pair can never be both-off.
        // Public: the EditMode tests drive these seams directly because ChangeEvents do not
        // dispatch on a panel-less tree.
        public void ApplyGlideToggled(bool isOn)
        {
            if (settings == null)
                return;

            if (isOn)
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                teleportToggle?.SetValueWithoutNotify(false);
            }
            else
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                teleportToggle?.SetValueWithoutNotify(true);
            }

            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        public void ApplyTeleportToggled(bool isOn)
        {
            if (settings == null)
                return;

            if (isOn)
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                glideToggle?.SetValueWithoutNotify(false);
            }
            else
            {
                settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
                glideToggle?.SetValueWithoutNotify(true);
            }

            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        // Faithful port of the uGUI shared handler: any non-locomotion change copies every
        // present widget value into settings and plays one UiSelect.
        public void ApplyOtherControlsWithFeedback()
        {
            if (settings == null)
                return;

            if (smoothTurnToggle != null)
                settings.SmoothTurnEnabled = smoothTurnToggle.value;

            // The uGUI snap-turn slider used wholeNumbers; UI Toolkit's Slider has no
            // equivalent, so the whole-degree contract is kept by rounding here.
            if (snapTurnSlider != null)
                settings.SnapTurnDegrees = Mathf.Round(snapTurnSlider.value);

            if (turnAroundToggle != null)
                settings.SnapTurnAroundEnabled = turnAroundToggle.value;

            if (moveSpeedSlider != null)
                settings.ContinuousMoveSpeed = moveSpeedSlider.value;

            if (smoothTurnSpeedSlider != null)
                settings.ContinuousTurnSpeed = smoothTurnSpeedSlider.value;

            if (leftHandToggle != null)
                settings.DominantHand = leftHandToggle.value
                    ? BlockiverseControllerRole.Left
                    : BlockiverseControllerRole.Right;

            if (toggleToMineToggle != null)
                settings.ToggleToMineEnabled = toggleToMineToggle.value;

            if (realPlayerHeightToggle != null)
                settings.RealPlayerHeightEnabled = realPlayerHeightToggle.value;

            if (sprintToggleToggle != null)
                settings.SprintToggleEnabled = sprintToggleToggle.value;

            if (crouchToggleToggle != null)
                settings.CrouchToggleEnabled = crouchToggleToggle.value;

            // The toggle asks "should I sink?", so it maps straight onto the setting rather
            // than being inverted into an accommodation flag (same reasoning as the uGUI menu).
            if (swimPassiveSinkToggle != null)
                settings.SwimPassiveSinkEnabled = swimPassiveSinkToggle.value;

            if (swimSpeedSlider != null)
                settings.SwimSpeedFactor = swimSpeedSlider.value;

            if (swimVignetteToggle != null)
                settings.SwimVignetteBoost = swimVignetteToggle.value;

            if (swimClimbOutToggle != null)
                settings.SwimClimbOutEnabled = swimClimbOutToggle.value;

            if (vignetteToggle != null)
                settings.VignetteEnabled = vignetteToggle.value;

            if (glideBobToggle != null)
                settings.GlideStyle = glideBobToggle.value ? GlideStyle.Bobbing : GlideStyle.Smooth;

            if (vignetteStrengthSlider != null)
                settings.VignetteStrength = vignetteStrengthSlider.value;

            if (uiScaleSlider != null)
                settings.UiScale = uiScaleSlider.value;

            RefreshSliderValueLabels();
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        // Push-down: mirrors current settings into the widgets without triggering callbacks.
        public void RefreshFromSettings()
        {
            if (settings != null)
            {
                bool isGlide = settings.LocomotionMode == BlockiverseLocomotionMode.Glide;
                glideToggle?.SetValueWithoutNotify(isGlide);
                teleportToggle?.SetValueWithoutNotify(!isGlide);

                smoothTurnToggle?.SetValueWithoutNotify(settings.SmoothTurnEnabled);
                turnAroundToggle?.SetValueWithoutNotify(settings.SnapTurnAroundEnabled);
                leftHandToggle?.SetValueWithoutNotify(settings.DominantHand == BlockiverseControllerRole.Left);
                toggleToMineToggle?.SetValueWithoutNotify(settings.ToggleToMineEnabled);
                realPlayerHeightToggle?.SetValueWithoutNotify(settings.RealPlayerHeightEnabled);
                sprintToggleToggle?.SetValueWithoutNotify(settings.SprintToggleEnabled);
                crouchToggleToggle?.SetValueWithoutNotify(settings.CrouchToggleEnabled);
                swimPassiveSinkToggle?.SetValueWithoutNotify(settings.SwimPassiveSinkEnabled);
                swimVignetteToggle?.SetValueWithoutNotify(settings.SwimVignetteBoost);
                swimClimbOutToggle?.SetValueWithoutNotify(settings.SwimClimbOutEnabled);
                vignetteToggle?.SetValueWithoutNotify(settings.VignetteEnabled);
                glideBobToggle?.SetValueWithoutNotify(settings.GlideStyle == GlideStyle.Bobbing);

                moveSpeedSlider?.SetValueWithoutNotify(settings.ContinuousMoveSpeed);
                snapTurnSlider?.SetValueWithoutNotify(settings.SnapTurnDegrees);
                smoothTurnSpeedSlider?.SetValueWithoutNotify(settings.ContinuousTurnSpeed);
                vignetteStrengthSlider?.SetValueWithoutNotify(settings.VignetteStrength);
                uiScaleSlider?.SetValueWithoutNotify(settings.UiScale);
                swimSpeedSlider?.SetValueWithoutNotify(settings.SwimSpeedFactor);
            }

            RefreshSliderValueLabels();
        }

        void ApplyStaticLabels()
        {
            SetText(titleLabel, UiText.Get(TitleKey));
            SetLabel(movementHeading, MovementModeKey);
            SetLabel(turningHeading, TurningKey);
            SetLabel(controlOptionsHeading, ControlOptionsKey);
            SetLabel(viewComfortHeading, ViewComfortKey);
            SetLabel(playerViewHeading, PlayerViewKey);

            SetFieldLabel(glideToggle, GlideMotionKey);
            SetFieldLabel(teleportToggle, TeleportKey);
            SetFieldLabel(glideBobToggle, WalkHeadBobKey);
            SetFieldLabel(smoothTurnToggle, SmoothTurnKey);
            SetFieldLabel(turnAroundToggle, TurnAroundKey);
            SetFieldLabel(leftHandToggle, LeftHandedKey);
            SetFieldLabel(toggleToMineToggle, ToggleToMineKey);
            SetFieldLabel(sprintToggleToggle, SprintToggleKey);
            SetFieldLabel(crouchToggleToggle, CrouchToggleKey);
            SetFieldLabel(vignetteToggle, MotionVignetteKey);
            SetFieldLabel(realPlayerHeightToggle, RealPlayerHeightKey);
            SetFieldLabel(swimPassiveSinkToggle, SwimSinkKey);
            SetFieldLabel(swimVignetteToggle, SwimVignetteKey);
            SetFieldLabel(swimClimbOutToggle, SwimClimbOutKey);

            SetFieldLabel(moveSpeedSlider, MoveSpeedKey);
            SetFieldLabel(snapTurnSlider, SnapTurnKey);
            SetFieldLabel(smoothTurnSpeedSlider, SmoothTurnSpeedKey);
            SetFieldLabel(vignetteStrengthSlider, VignetteStrengthKey);
            SetFieldLabel(uiScaleSlider, UiScaleKey);
            SetFieldLabel(swimSpeedSlider, SwimSpeedKey);

            if (heightResetButton != null)
                heightResetButton.text = UiText.Get(ResetHeightKey);
        }

        void RefreshSliderValueLabels()
        {
            // Identifiers these are not — but the uGUI panel had no value readouts at all, so
            // the format is new presentation: invariant decimals with the precision each range
            // needs, in hs-figures so the digits column-align.
            SetFigure(moveSpeedValueLabel, moveSpeedSlider, "0.0");
            SetFigure(snapTurnValueLabel, snapTurnSlider, "0");
            SetFigure(smoothTurnSpeedValueLabel, smoothTurnSpeedSlider, "0");
            SetFigure(vignetteStrengthValueLabel, vignetteStrengthSlider, "0.00");
            SetFigure(uiScaleValueLabel, uiScaleSlider, "0.00");
            SetFigure(swimSpeedValueLabel, swimSpeedSlider, "0.00");
        }

        static void SetText(Label label, string text)
        {
            if (label != null && label.text != text)
                label.text = text;
        }

        static void SetLabel(Label label, string key) => SetText(label, UiText.Get(key));

        static void SetFieldLabel<T>(BaseField<T> field, string key)
        {
            if (field == null)
                return;

            string text = UiText.Get(key);
            if (field.label != text)
                field.label = text;
        }

        static void SetFigure(Label label, Slider slider, string format)
        {
            if (label == null || slider == null)
                return;

            SetText(label, slider.value.ToString(format, CultureInfo.InvariantCulture));
        }

        void RegisterToggle(Toggle toggle, EventCallback<ChangeEvent<bool>> callback)
        {
            if (toggle == null)
                return;

            toggle.RegisterValueChangedCallback(callback);
            RegisteredElementCallbackCount++;
        }

        void UnregisterToggle(Toggle toggle, EventCallback<ChangeEvent<bool>> callback)
        {
            if (toggle == null)
                return;

            toggle.UnregisterValueChangedCallback(callback);
            RegisteredElementCallbackCount--;
        }

        void RegisterSlider(Slider slider, EventCallback<ChangeEvent<float>> callback)
        {
            if (slider == null)
                return;

            slider.RegisterValueChangedCallback(callback);
            RegisteredElementCallbackCount++;
        }

        void UnregisterSlider(Slider slider, EventCallback<ChangeEvent<float>> callback)
        {
            if (slider == null)
                return;

            slider.UnregisterValueChangedCallback(callback);
            RegisteredElementCallbackCount--;
        }

        void RegisterButton(Button button, EventCallback<ClickEvent> callback)
        {
            if (button == null)
                return;

            button.RegisterCallback(callback);
            RegisteredElementCallbackCount++;
        }

        void UnregisterButton(Button button, EventCallback<ClickEvent> callback)
        {
            if (button == null)
                return;

            button.UnregisterCallback(callback);
            RegisteredElementCallbackCount--;
        }

        void OnSelectedLocaleChanged(Locale locale) => ApplyStaticLabels();

        // The height-reset implementation lives in Blockiverse.VR, which this assembly does
        // not reference; resolve through the Core interface the same way BlockiverseUiFeedback
        // resolves haptics.
        void ResolveHeightReset()
        {
            MonoBehaviour[] behaviours =
                BlockiverseSceneLookup.FindAll<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IBlockiverseHeightReset resolved)
                {
                    heightReset = resolved;
                    return;
                }
            }
        }

        void PlayFeedback(BlockiverseAudioCue cue) =>
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
    }
}
