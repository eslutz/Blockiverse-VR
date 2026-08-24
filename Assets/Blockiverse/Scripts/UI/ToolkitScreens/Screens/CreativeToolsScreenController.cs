using System;
using System.Collections.Generic;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of BlockiverseCreativeToolsPanel (matrix row 18; voxel_creative_ruleset
    // §12): region selection (corner A/B from the cached aim) with fill/replace/delete/copy/
    // paste through WorldEditService, region undo/redo, tree/ruin spawners, pick-block, and
    // environment controls (time-of-day, day-cycle speed, weather). Region edits bypass the
    // per-block authority channel, so everything that mutates the world stays hard-gated to
    // offline creative worlds (matrix §4 item 1), and the time/weather controls are host-only
    // during a network session — a refused slider is reverted BEFORE the refusal is reported.
    // Like the uGUI panel, this screen plays no feedback cues of its own.
    [UiToolkitScreen(MenuActions.CreativeToolsScreen, "Assets/Blockiverse/UI/Documents/CreativeToolsScreen.uxml",
        1000, 1250, UiToolkitPlacementProfile.Menu)]
    public sealed class CreativeToolsScreenController : UiToolkitScreenController, IUiToolkitCreativeToolsScreen
    {
        const float DefaultCycleTimeScale = 1.0f;

        static class Keys
        {
            public const string InitialStatus = "ui.generated.creative_tools.initial_status";
            public const string Fill = "ui.generated.creative_tools.fill";
            public const string Replace = "ui.generated.creative_tools.replace";
            public const string Copy = "ui.generated.creative_tools.copy";
            public const string Paste = "ui.generated.creative_tools.paste";
            public const string Undo = "ui.generated.creative_tools.undo";
            public const string Redo = "ui.generated.creative_tools.redo";
            public const string CyclePaused = "ui.generated.creative_tools.cycle_paused";
            public const string CycleResumed = "ui.generated.creative_tools.cycle_resumed";
            public const string CommonDelete = "ui.generated.common.delete";
            public const string ConfirmAccept = "ui.action.confirm.accept";
            public const string ConfirmCancel = "ui.action.confirm.cancel";

            // Pending centrally (reported by this screen's migration task); UiText falls back to
            // the raw key until the entries land, and the section headings render from these.
            public const string RegionOperationsHeading = "ui.generated.creative_tools.region_operations";
            public const string EnvironmentHeading = "ui.generated.creative_tools.environment";

            public const string Weather = "ui.status.creative.weather";
            public const string CornerA = "ui.status.creative.corner_a";
            public const string CornerB = "ui.status.creative.corner_b";
            public const string SetCornerAim = "ui.status.creative.set_corner_aim";
            public const string SetCorner = "ui.status.creative.set_corner";
            public const string Corners = "ui.status.creative.corners";
            public const string AimReplace = "ui.status.creative.aim_replace";
            public const string ChoosePasteOrigin = "ui.status.creative.choose_paste_origin";
            public const string SpawnedTree = "ui.status.creative.spawned_tree";
            public const string SpawnedRuin = "ui.status.creative.spawned_ruin";
            public const string AimPick = "ui.status.creative.aim_pick";
            public const string Picked = "ui.status.creative.picked";
            public const string MissingCatalogBlock = "ui.status.creative.missing_catalog_block";
            public const string WeatherHostOnly = "ui.status.creative.weather_host_only";
            public const string TimeHostOnly = "ui.status.creative.time_host_only";
            public const string SetCornersFirst = "ui.status.creative.set_corners_first";
            public const string NoWorld = "ui.status.creative.no_world";
            public const string CreativeOnly = "ui.status.creative.creative_only";
            public const string LanUnavailable = "ui.status.creative.lan_unavailable";
            public const string AimGround = "ui.status.creative.aim_ground";
            public const string NoRoomAbove = "ui.status.creative.no_room_above";
            public const string OperationDone = "ui.status.creative.operation_done";
            public const string VolumeLimit = "ui.status.creative.volume_limit";
            public const string OutOfBounds = "ui.status.creative.out_of_bounds";
            public const string NoClipboard = "ui.status.creative.no_clipboard";
            public const string NothingToUndo = "ui.status.creative.nothing_to_undo";
            public const string NothingToRedo = "ui.status.creative.nothing_to_redo";
            public const string NothingToReplace = "ui.status.creative.nothing_to_replace";
            public const string OperationFailed = "ui.status.creative.operation_failed";

            public const string WeatherValueKeyPrefix = "ui.value.weather_state.";
        }

        readonly WorldEditService editService = new();
        readonly List<(Button button, EventCallback<ClickEvent> callback)> registeredClicks = new();

        CreativeInteractionController interactionController;
        CreativeWorldManager worldManager;
        CreativeHotbar hotbar;

        BlockPosition? cornerA;
        BlockPosition? cornerB;
        // The last block the interaction ray pointed at: pressing a panel button moves the ray
        // over UI (clearing the live target), so actions use this cached aim instead.
        BlockPosition? lastTarget;
        VoxelWorld trackedWorld;
        Func<bool> networkSessionActiveProvider;
        // Survives tree rebuilds so a re-attach can re-render the last status; null means no
        // status was ever written and the attach seeds the uGUI panel's authored initial line.
        string statusMessage;

        Label cornersLabel;
        Label statusLabel;
        Label weatherLabel;
        Label regionHeadingLabel;
        Label environmentHeadingLabel;
        Button setCornerAButton;
        Button setCornerBButton;
        Button pickButton;
        Button fillButton;
        Button replaceButton;
        Button deleteButton;
        Button copyButton;
        Button pasteButton;
        Button undoButton;
        Button redoButton;
        Button spawnTreeButton;
        Button spawnRuinButton;
        Button toggleCycleButton;
        Button cycleWeatherButton;
        Button closeButton;
        Slider timeOfDaySlider;
        Slider daySpeedSlider;

        EventCallback<ChangeEvent<float>> timeOfDayChangedCallback;
        EventCallback<ChangeEvent<float>> daySpeedChangedCallback;

        public override string ScreenId => MenuActions.CreativeToolsScreen;

        public int WorldEditUndoCount => editService.UndoCount;
        public bool HasWorldEditClipboard => editService.HasClipboard;

        public void ConfigureNetworkSessionActiveProvider(Func<bool> provider)
        {
            networkSessionActiveProvider = provider;
        }

        // Test seam mirroring the uGUI panel's Configure: injects the scene collaborators the
        // production panel resolves from the generated Boot scene.
        public void ConfigureCreativeTools(
            CreativeInteractionController controller,
            CreativeWorldManager manager,
            CreativeHotbar creativeHotbar)
        {
            interactionController = controller;
            worldManager = manager;
            hotbar = creativeHotbar;
            ResetWorldEditStateIfWorldChanged();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            cornersLabel = Require<Label>(root, "bv-corners", ref allFound);
            statusLabel = Require<Label>(root, "bv-status", ref allFound);
            weatherLabel = Require<Label>(root, "bv-weather", ref allFound);
            regionHeadingLabel = Require<Label>(root, "bv-region-heading", ref allFound);
            environmentHeadingLabel = Require<Label>(root, "bv-environment-heading", ref allFound);
            setCornerAButton = Require<Button>(root, "bv-set-a", ref allFound);
            setCornerBButton = Require<Button>(root, "bv-set-b", ref allFound);
            pickButton = Require<Button>(root, "bv-pick", ref allFound);
            fillButton = Require<Button>(root, "bv-fill", ref allFound);
            replaceButton = Require<Button>(root, "bv-replace", ref allFound);
            deleteButton = Require<Button>(root, "bv-delete", ref allFound);
            copyButton = Require<Button>(root, "bv-copy", ref allFound);
            pasteButton = Require<Button>(root, "bv-paste", ref allFound);
            undoButton = Require<Button>(root, "bv-undo", ref allFound);
            redoButton = Require<Button>(root, "bv-redo", ref allFound);
            spawnTreeButton = Require<Button>(root, "bv-spawn-tree", ref allFound);
            spawnRuinButton = Require<Button>(root, "bv-spawn-ruin", ref allFound);
            toggleCycleButton = Require<Button>(root, "bv-toggle-cycle", ref allFound);
            cycleWeatherButton = Require<Button>(root, "bv-cycle-weather", ref allFound);
            closeButton = Require<Button>(root, "bv-close", ref allFound);
            timeOfDaySlider = Require<Slider>(root, "bv-time-of-day", ref allFound);
            daySpeedSlider = Require<Slider>(root, "bv-day-speed", ref allFound);

            RenderHeadings();
            ResolveReferences();
            ResetWorldEditStateIfWorldChanged();

            if (statusLabel != null)
                statusLabel.text = statusMessage ?? UiText.Get(Keys.InitialStatus);
            RefreshCornersLabel();
            RefreshEnvironmentControls();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            RegisterClick(setCornerAButton, SetCornerA);
            RegisterClick(setCornerBButton, SetCornerB);
            // Pick plays no cue here: a successful pick lands in hotbar.SelectBlock, which plays
            // UiSelect itself — the same double-play the Catalog's entry grid had. A failed pick
            // (aimed at air) stays silent, which reads as "nothing happened".
            RegisterClick(pickButton, PickBlock, playCue: false);
            RegisterClick(fillButton, FillRegion);
            RegisterClick(replaceButton, ReplaceRegion);
            RegisterClick(deleteButton, DeleteRegion);
            RegisterClick(copyButton, CopyRegion);
            RegisterClick(pasteButton, PasteRegion);
            RegisterClick(undoButton, UndoEdit);
            RegisterClick(redoButton, RedoEdit);
            RegisterClick(spawnTreeButton, SpawnTree);
            RegisterClick(spawnRuinButton, SpawnRuin);
            RegisterClick(toggleCycleButton, ToggleDayNightCycle);
            RegisterClick(cycleWeatherButton, CycleWeather);
            RegisterClick(closeButton, SubmitClose);

            timeOfDayChangedCallback = evt => ApplyTimeOfDay(evt.newValue);
            daySpeedChangedCallback = evt => ApplyTimeScale(evt.newValue);
            timeOfDaySlider?.RegisterValueChangedCallback(timeOfDayChangedCallback);
            daySpeedSlider?.RegisterValueChangedCallback(daySpeedChangedCallback);

            // The corners/weather/heading lines are cached dynamic text (matrix §4: locale
            // change); the status keeps its last message — formatted statuses cannot be
            // re-derived, exactly like the uGUI panel's TMP label.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            foreach ((Button button, EventCallback<ClickEvent> callback) in registeredClicks)
                button.UnregisterCallback(callback);
            registeredClicks.Clear();

            if (timeOfDayChangedCallback != null)
                timeOfDaySlider?.UnregisterValueChangedCallback(timeOfDayChangedCallback);
            if (daySpeedChangedCallback != null)
                daySpeedSlider?.UnregisterValueChangedCallback(daySpeedChangedCallback);
            timeOfDayChangedCallback = null;
            daySpeedChangedCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            cornersLabel = null;
            statusLabel = null;
            weatherLabel = null;
            regionHeadingLabel = null;
            environmentHeadingLabel = null;
            setCornerAButton = null;
            setCornerBButton = null;
            pickButton = null;
            fillButton = null;
            replaceButton = null;
            deleteButton = null;
            copyButton = null;
            pasteButton = null;
            undoButton = null;
            redoButton = null;
            spawnTreeButton = null;
            spawnRuinButton = null;
            toggleCycleButton = null;
            cycleWeatherButton = null;
            closeButton = null;
            timeOfDaySlider = null;
            daySpeedSlider = null;
        }

        void Update()
        {
            ResetWorldEditStateIfWorldChanged();

            BlockPosition? target = interactionController != null ? interactionController.CurrentTarget : null;
            if (target.HasValue)
                lastTarget = target;
        }

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // Cue rides the click, never the handler: the handlers are the seams tests drive
        // directly, and they must not need an audio rig.
        void RegisterClick(Button button, Action handler, bool playCue = true)
        {
            if (button == null)
                return;

            EventCallback<ClickEvent> callback = playCue
                ? _ =>
                {
                    BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
                    handler();
                }
                : _ => handler();
            button.RegisterCallback(callback);
            registeredClicks.Add((button, callback));
        }

        void ResolveReferences()
        {
            if (interactionController == null)
                interactionController = BlockiverseSceneLookup.Find<CreativeInteractionController>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            if (hotbar == null)
                hotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);
        }

        void RenderHeadings()
        {
            if (regionHeadingLabel != null)
                regionHeadingLabel.text = UiText.Get(Keys.RegionOperationsHeading);
            if (environmentHeadingLabel != null)
                environmentHeadingLabel.text = UiText.Get(Keys.EnvironmentHeading);
        }

        // Pushes the live clock/weather values into the controls without re-firing listeners
        // (SetValueWithoutNotify — matrix §4: refreshing must never echo back into the clock).
        public void RefreshEnvironmentControls()
        {
            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (clock != null)
            {
                timeOfDaySlider?.SetValueWithoutNotify(clock.NormalizedTime);
                daySpeedSlider?.SetValueWithoutNotify(clock.TimeScale);
            }

            if (weatherLabel != null && worldManager != null)
                weatherLabel.text = UiText.Format(
                    Keys.Weather,
                    WeatherDisplayName(worldManager.GetWeatherSyncState().State));
        }

        // ── Region selection ──────────────────────────────────────────────────

        public void SetCornerA()
        {
            cornerA = CaptureAim(UiText.Get(Keys.CornerA));
            RefreshCornersLabel();
        }

        public void SetCornerB()
        {
            cornerB = CaptureAim(UiText.Get(Keys.CornerB));
            RefreshCornersLabel();
        }

        BlockPosition? CaptureAim(string what)
        {
            if (!lastTarget.HasValue)
            {
                SetStatus(UiText.Format(Keys.SetCornerAim, what));
                return null;
            }

            SetStatus(UiText.Format(Keys.SetCorner, what, lastTarget.Value.ToString()));
            return lastTarget;
        }

        void RefreshCornersLabel()
        {
            if (cornersLabel == null)
                return;

            string a = cornerA.HasValue ? cornerA.Value.ToString() : "—";
            string b = cornerB.HasValue ? cornerB.Value.ToString() : "—";
            cornersLabel.text = UiText.Format(Keys.Corners, a, b);
        }

        // ── Region operations (§12.1) ─────────────────────────────────────────

        public void FillRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ConfirmThen(UiText.Get(Keys.Fill), () =>
                ReportEdit(UiText.Get(Keys.Fill), editService.Fill(world, min, max, hotbar.SelectedBlockId)));
        }

        public void DeleteRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ConfirmThen(UiText.Get(Keys.CommonDelete), () =>
                ReportEdit(UiText.Get(Keys.CommonDelete), editService.Delete(world, min, max)));
        }

        // Replaces every block of the aimed-at type inside the region with the hotbar selection.
        public void ReplaceRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            if (!lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(UiText.Get(Keys.AimReplace));
                return;
            }

            BlockId targetType = world.GetBlock(lastTarget.Value);

            ConfirmThen(UiText.Get(Keys.Replace), () =>
                ReportEdit(UiText.Get(Keys.Replace), editService.Replace(world, min, max, targetType, hotbar.SelectedBlockId)));
        }

        public void CopyRegion()
        {
            if (!TryGetRegion(out BlockPosition min, out BlockPosition max) || !CanEdit(out VoxelWorld world))
                return;

            ReportEdit(UiText.Get(Keys.Copy), editService.Copy(world, min, max));
        }

        // Pastes the clipboard with its min corner at corner A (or the current aim).
        public void PasteRegion()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            BlockPosition? origin = cornerA ?? lastTarget;
            if (!origin.HasValue)
            {
                SetStatus(UiText.Get(Keys.ChoosePasteOrigin));
                return;
            }

            ReportEdit(UiText.Get(Keys.Paste), editService.Paste(world, origin.Value));
        }

        public void UndoEdit()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            ReportEdit(UiText.Get(Keys.Undo), editService.Undo(world));
        }

        public void RedoEdit()
        {
            if (!CanEdit(out VoxelWorld world))
                return;

            ReportEdit(UiText.Get(Keys.Redo), editService.Redo(world));
        }

        // ── Spawners / pick block ─────────────────────────────────────────────

        public void SpawnTree()
        {
            if (!CanEdit(out VoxelWorld world) || !TryGetAimAbove(world, out BlockPosition basePos))
                return;

            worldManager.SpawnStandardTree(world, basePos);
            worldManager.Presentation?.RebuildDirty();
            SetStatus(UiText.Format(Keys.SpawnedTree, basePos.ToString()));
        }

        public void SpawnRuin()
        {
            if (!CanEdit(out VoxelWorld world) || !TryGetAimAbove(world, out BlockPosition basePos))
                return;

            worldManager.SpawnStructure(world, basePos);
            worldManager.Presentation?.RebuildDirty();
            SetStatus(UiText.Format(Keys.SpawnedRuin, basePos.ToString()));
        }

        // Puts the aimed-at block into the hotbar selection (block picker).
        public void PickBlock()
        {
            ResetWorldEditStateIfWorldChanged();

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null || hotbar == null || !lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(UiText.Get(Keys.AimPick));
                return;
            }

            BlockId picked = world.GetBlock(lastTarget.Value);
            if (hotbar.SelectBlock(picked))
                SetStatus(UiText.Format(Keys.Picked, picked.ToString()));
            else
                SetStatus(UiText.Get(Keys.MissingCatalogBlock));
        }

        // ── Environment controls ──────────────────────────────────────────────

        // Slider handler seams (also the registered value-changed targets). During a network
        // session time is host-owned: revert the control to the live clock FIRST, then report,
        // so the refused value is never left on screen (matrix §4 item 1).
        public void ApplyTimeOfDay(float value)
        {
            if (NetworkSessionActive())
            {
                RefreshEnvironmentControls();
                SetStatus(UiText.Get(Keys.TimeHostOnly));
                return;
            }

            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (clock != null)
                clock.SetNormalizedTime(value);
        }

        public void ApplyTimeScale(float value)
        {
            if (NetworkSessionActive())
            {
                RefreshEnvironmentControls();
                SetStatus(UiText.Get(Keys.TimeHostOnly));
                return;
            }

            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (clock != null)
                clock.SetTimeScale(value);
        }

        public void ToggleDayNightCycle()
        {
            WorldTimeClock clock = worldManager != null ? worldManager.WorldTimeClock : null;
            if (clock == null)
                return;

            if (NetworkSessionActive())
            {
                RefreshEnvironmentControls();
                SetStatus(UiText.Get(Keys.TimeHostOnly));
                return;
            }

            bool resume = Mathf.Approximately(clock.TimeScale, 0.0f);
            clock.SetTimeScale(resume ? DefaultCycleTimeScale : 0.0f);
            RefreshEnvironmentControls();
            SetStatus(UiText.Get(resume ? Keys.CycleResumed : Keys.CyclePaused));
        }

        // Steps the weather to the next state (wrapping through every WeatherState preset).
        public void CycleWeather()
        {
            if (worldManager == null)
                return;

            if (NetworkSessionActive())
            {
                SetStatus(UiText.Get(Keys.WeatherHostOnly));
                return;
            }

            var states = (WeatherState[])Enum.GetValues(typeof(WeatherState));
            WeatherState current = worldManager.GetWeatherSyncState().State;
            int next = (Array.IndexOf(states, current) + 1) % states.Length;
            worldManager.SetWeather(states[next]);
            RefreshEnvironmentControls();
        }

        public void SubmitClose()
        {
            DispatchAction(MenuActions.CreativeToolsClose);
        }

        // ── Shared gating/reporting ───────────────────────────────────────────

        // Fill/Delete/Replace are destructive and confirm through the modal stack when the menu
        // controller is reachable (the uGUI panel behaved identically, executing directly only
        // in harnesses with no controller). The callback runs the operation only on accept.
        void ConfirmThen(string operationTitle, Action onAccepted)
        {
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
            {
                controller.RequestConfirm(
                    operationTitle,
                    UiText.Get(Keys.ConfirmAccept),
                    UiText.Get(Keys.ConfirmCancel),
                    accepted =>
                    {
                        if (accepted)
                            onAccepted();
                    });
            }
            else
            {
                onAccepted();
            }
        }

        bool TryGetRegion(out BlockPosition min, out BlockPosition max)
        {
            min = default;
            max = default;

            if (!cornerA.HasValue || !cornerB.HasValue)
            {
                SetStatus(UiText.Get(Keys.SetCornersFirst));
                return false;
            }

            BlockPosition a = cornerA.Value;
            BlockPosition b = cornerB.Value;
            min = new BlockPosition(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            max = new BlockPosition(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
            return true;
        }

        // Region edits write directly to the world (no per-block authority round-trip), so they
        // are only legal in offline creative worlds (§12 permission gating).
        bool CanEdit(out VoxelWorld world)
        {
            ResetWorldEditStateIfWorldChanged();
            world = worldManager != null ? worldManager.World : null;

            if (world == null)
            {
                SetStatus(UiText.Get(Keys.NoWorld));
                return false;
            }

            if (worldManager.GameMode != WorldGameMode.Creative)
            {
                SetStatus(UiText.Get(Keys.CreativeOnly));
                return false;
            }

            if (NetworkSessionActive())
            {
                SetStatus(UiText.Get(Keys.LanUnavailable));
                return false;
            }

            return true;
        }

        // Undo history, clipboard, corners and the cached aim never survive a world swap.
        void ResetWorldEditStateIfWorldChanged()
        {
            VoxelWorld currentWorld = worldManager != null ? worldManager.World : null;
            if (ReferenceEquals(currentWorld, trackedWorld))
                return;

            trackedWorld = currentWorld;
            editService.Reset();
            cornerA = null;
            cornerB = null;
            lastTarget = null;
            RefreshCornersLabel();
        }

        bool NetworkSessionActive()
        {
            if (networkSessionActiveProvider != null)
                return networkSessionActiveProvider();

            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }

        bool TryGetAimAbove(VoxelWorld world, out BlockPosition above)
        {
            above = default;

            if (!lastTarget.HasValue || !world.Bounds.Contains(lastTarget.Value))
            {
                SetStatus(UiText.Get(Keys.AimGround));
                return false;
            }

            above = new BlockPosition(lastTarget.Value.X, lastTarget.Value.Y + 1, lastTarget.Value.Z);
            if (!world.Bounds.Contains(above))
            {
                SetStatus(UiText.Get(Keys.NoRoomAbove));
                return false;
            }

            return true;
        }

        void ReportEdit(string operation, WorldEditResult result)
        {
            if (result == WorldEditResult.Success)
            {
                worldManager.Presentation?.RebuildDirty();
                SetStatus(UiText.Format(Keys.OperationDone, operation));
                return;
            }

            SetStatus(result switch
            {
                WorldEditResult.VolumeLimitExceeded => UiText.Format(Keys.VolumeLimit, operation),
                WorldEditResult.OutOfBounds => UiText.Format(Keys.OutOfBounds, operation),
                WorldEditResult.NoClipboard => UiText.Get(Keys.NoClipboard),
                WorldEditResult.NothingToUndo => UiText.Get(Keys.NothingToUndo),
                WorldEditResult.NothingToRedo => UiText.Get(Keys.NothingToRedo),
                WorldEditResult.NothingToReplace => UiText.Get(Keys.NothingToReplace),
                _ => UiText.Format(Keys.OperationFailed, operation)
            });
        }

        void SetStatus(string message)
        {
            statusMessage = message;
            if (statusLabel != null)
                statusLabel.text = message;
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            RenderHeadings();
            RefreshCornersLabel();
            RefreshEnvironmentControls();
        }

        // Weather display names resolve table-first from the same ui.value.weather_state.* keys
        // the uGUI shim's DisplayName uses, with the identical humanized fallback while those
        // entries are still pending centrally. Screen controllers must not call
        // BlockiverseLocalization, so the two tiny transforms are reproduced here; they match
        // NormalizeKey/HumanizeIdentifier byte-for-byte for this closed enum set (single-case
        // names, no digits, no consecutive capitals).
        static string WeatherDisplayName(WeatherState state)
        {
            string enumName = state.ToString();
            string key = Keys.WeatherValueKeyPrefix + ToSnakeCase(enumName);
            string resolved = UiText.Get(key);
            return string.Equals(resolved, key, StringComparison.Ordinal) ? SplitWords(enumName) : resolved;
        }

        static string ToSnakeCase(string enumName)
        {
            var builder = new StringBuilder(enumName.Length + 4);
            for (int i = 0; i < enumName.Length; i++)
            {
                char character = enumName[i];
                if (char.IsUpper(character) && i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        static string SplitWords(string enumName)
        {
            var builder = new StringBuilder(enumName.Length + 4);
            for (int i = 0; i < enumName.Length; i++)
            {
                char character = enumName[i];
                if (char.IsUpper(character) && i > 0)
                    builder.Append(' ');
                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
