using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using UnityEngine;

namespace Blockiverse.UI
{
    // Owns the UiScreenRouter and coordinates panel visibility for the first-playable menu set
    // (voxel_survival_menus §2/§8.1). The menu button cycles between gameplay and the pause screen;
    // all other transitions are triggered by action ids dispatched from BlockiverseActionMenu.
    public sealed class BlockiverseMenuController : MonoBehaviour
    {
        const string TitleMenuName = "Title Menu";
        const string PauseMenuName = "Pause Menu";
        const string DeathScreenName = "Death Screen";
        const string ConfirmDialogName = "Confirm Dialog";
        const string NewWorldPanelName = "New World Panel";
        const string LoadWorldPanelName = "Load World Panel";
        const string SettingsPanelName = "Settings Panel";
        const string ComfortSettingsPanelName = "Comfort Settings Menu";
        const string AudioSettingsPanelName = "Audio Settings Panel";
        const string ControlsPanelName = "Controls Panel";
        const string WorldDetailsPanelName = "World Details Panel";
        const string CreativeToolsPanelName = "Creative Tools Panel";
        const string StationPanelName = "Station Panel";
        const string LanMultiplayerPanelName = "LAN Multiplayer Panel";
        const string ControllerMappingPopupName = "Controller Mapping Popup";
        const string StartupLoadingOverlayName = "Startup Loading Overlay";
        const string SurvivalHudName = "Survival HUD";
        const float GameplayHudScale = 0.00105f;

        [SerializeField] BlockiverseInputRig inputRig;

        [SerializeField] BlockiverseActionMenu titleMenu;
        [SerializeField] BlockiverseActionMenu pauseMenu;
        [SerializeField] BlockiverseActionMenu deathMenu;
        [SerializeField] BlockiverseActionMenu confirmMenu;
        [SerializeField] BlockiverseActionMenu settingsMenu;
        [SerializeField] BlockiverseNewWorldPanel newWorldPanel;
        [SerializeField] BlockiverseLoadWorldPanel loadWorldPanel;
        [SerializeField] BlockiverseWorldDetailsPanel worldDetailsPanel;
        [SerializeField] BlockiverseActionMenu worldDetailsMenu;
        [SerializeField] BlockiverseComfortMenu comfortMenu;
        [SerializeField] BlockiverseStationPanel stationPanel;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] BlockiverseWorldSpacePanelPresenter controllerMappingPresenter;
        [SerializeField] BlockiverseWorldSpacePanelPresenter worldLoadingPresenter;

        readonly List<(string screenId, BlockiverseWorldSpacePanelPresenter presenter)> screenPresenters = new();
        Action<bool> confirmCallback;
        UiScreenRouter router;
        bool hasLatestSave;
        bool hasAnySave;

        public UiScreenRouter Router => router;

        // Fires when the controller needs the world session to act (save, load, respawn, etc.).
        public event Action<string> ActionRequested;

        // Updates the title menu's save-dependent entries (Continue / Load World). Called by the
        // world session coordinator whenever the set of saves changes.
        public void SetSaveAvailability(bool latestSaveExists, bool anySaveExists)
        {
            hasLatestSave = latestSaveExists;
            hasAnySave = anySaveExists;
            RefreshTitleMenu();
        }

        void RefreshTitleMenu()
        {
            if (titleMenu == null)
                ResolveRuntimeReferences();
            titleMenu?.SetMenu(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleBlockiverse),
                MenuActions.Title(hasLatestSave, hasAnySave, CanQuit()));
        }

        void RefreshPauseMenu()
        {
            if (pauseMenu == null || survivalSync == null)
                ResolveRuntimeReferences();

            bool canToggleMode = survivalSync != null && survivalSync.CanToggleMode;
            bool canOpenCreativeTools = survivalSync != null && survivalSync.CanUseCreativeMode;
            pauseMenu?.SetMenu(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused),
                MenuActions.PauseMenu(canToggleMode, canOpenCreativeTools, CanQuit()));
        }

        public void Configure(
            BlockiverseInputRig rig,
            BlockiverseActionMenu title,
            BlockiverseActionMenu pause,
            BlockiverseActionMenu death,
            BlockiverseActionMenu confirm,
            BlockiverseNewWorldPanel newWorld,
            BlockiverseLoadWorldPanel loadWorld,
            BlockiverseActionMenu settings = null,
            BlockiverseWorldDetailsPanel worldDetails = null,
            BlockiverseActionMenu worldDetailsActions = null)
        {
            inputRig = rig;
            titleMenu = title;
            pauseMenu = pause;
            deathMenu = death;
            confirmMenu = confirm;
            settingsMenu = settings;
            newWorldPanel = newWorld;
            loadWorldPanel = loadWorld;
            worldDetailsPanel = worldDetails;
            worldDetailsMenu = worldDetailsActions;
        }

        public void ConfigurePresenters(
            BlockiverseWorldSpacePanelPresenter title,
            BlockiverseWorldSpacePanelPresenter pause,
            BlockiverseWorldSpacePanelPresenter death,
            BlockiverseWorldSpacePanelPresenter confirm,
            BlockiverseWorldSpacePanelPresenter newWorld,
            BlockiverseWorldSpacePanelPresenter loadWorld,
            BlockiverseWorldSpacePanelPresenter settings,
            BlockiverseWorldSpacePanelPresenter station = null,
            BlockiverseWorldSpacePanelPresenter lanMultiplayer = null,
            BlockiverseWorldSpacePanelPresenter audioSettings = null,
            BlockiverseWorldSpacePanelPresenter controls = null,
            BlockiverseWorldSpacePanelPresenter worldDetails = null,
            BlockiverseWorldSpacePanelPresenter creativeTools = null,
            BlockiverseWorldSpacePanelPresenter gameplayHud = null,
            BlockiverseWorldSpacePanelPresenter comfortSettings = null,
            BlockiverseWorldSpacePanelPresenter controllerMapping = null,
            BlockiverseWorldSpacePanelPresenter worldLoading = null)
        {
            controllerMappingPresenter = controllerMapping;
            worldLoadingPresenter = worldLoading;
            screenPresenters.Clear();
            AddPresenter(MenuActions.ControllerMappingScreen, controllerMappingPresenter);
            AddPresenter(MenuActions.WorldLoadingScreen, worldLoadingPresenter);
            AddPresenter(MenuActions.GameplayHudScreen, gameplayHud);
            AddPresenter(MenuActions.TitleScreen, title);
            AddPresenter(MenuActions.PauseScreen, pause);
            AddPresenter(MenuActions.DeathScreen, death);
            AddPresenter(MenuActions.ConfirmModal, confirm);
            AddPresenter(MenuActions.NewWorldScreen, newWorld);
            AddPresenter(MenuActions.LoadWorldScreen, loadWorld);
            AddPresenter(MenuActions.SettingsScreen, settings);
            if (comfortSettings != null)
                AddPresenter(MenuActions.ComfortSettingsScreen, comfortSettings);
            if (station != null)
                AddPresenter(MenuActions.StationMenuScreen, station);
            if (lanMultiplayer != null)
                AddPresenter(MenuActions.LanMultiplayerScreen, lanMultiplayer);
            if (audioSettings != null)
                AddPresenter(MenuActions.AudioSettingsScreen, audioSettings);
            if (controls != null)
                AddPresenter(MenuActions.ControlsScreen, controls);
            if (worldDetails != null)
                AddPresenter(MenuActions.WorldDetailsScreen, worldDetails);
            if (creativeTools != null)
                AddPresenter(MenuActions.CreativeToolsScreen, creativeTools);
        }

        // Wires the smelting-station panel so a "use" on a kiln/forge block opens it (§8.4).
        public void ConfigureStationPanel(BlockiverseStationPanel panel)
        {
            stationPanel = panel;
        }

        public void ResolveRuntimeReferences()
        {
            if (inputRig == null)
                inputRig = GetComponent<BlockiverseInputRig>()
                    ?? GetComponentInChildren<BlockiverseInputRig>(true)
                    ?? BlockiverseSceneLookup.Find<BlockiverseInputRig>(FindObjectsInactive.Include);

            titleMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(TitleMenuName);
            pauseMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(PauseMenuName);
            deathMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(DeathScreenName);
            confirmMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(ConfirmDialogName);
            settingsMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(SettingsPanelName);
            comfortMenu ??= FindGeneratedComponent<BlockiverseComfortMenu>(ComfortSettingsPanelName);
            newWorldPanel ??= FindGeneratedComponent<BlockiverseNewWorldPanel>(NewWorldPanelName);
            loadWorldPanel ??= FindGeneratedComponent<BlockiverseLoadWorldPanel>(LoadWorldPanelName);
            worldDetailsPanel ??= FindGeneratedComponent<BlockiverseWorldDetailsPanel>(WorldDetailsPanelName);
            worldDetailsMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(WorldDetailsPanelName);
            stationPanel ??= FindGeneratedComponent<BlockiverseStationPanel>(StationPanelName);
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            titleMenu?.ResolveRuntimeReferences();
            pauseMenu?.ResolveRuntimeReferences();
            deathMenu?.ResolveRuntimeReferences();
            confirmMenu?.ResolveRuntimeReferences();
            settingsMenu?.ResolveRuntimeReferences();
            worldDetailsMenu?.ResolveRuntimeReferences();
            newWorldPanel?.ResolveRuntimeReferences();
            loadWorldPanel?.ResolveRuntimeReferences();
            stationPanel?.ResolveRuntimeReferences();

            BlockiverseWorldSpacePanelPresenter titlePresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(TitleMenuName);
            BlockiverseWorldSpacePanelPresenter pausePresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(PauseMenuName);
            BlockiverseWorldSpacePanelPresenter deathPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(DeathScreenName);
            BlockiverseWorldSpacePanelPresenter confirmPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ConfirmDialogName);
            BlockiverseWorldSpacePanelPresenter newWorldPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(NewWorldPanelName);
            BlockiverseWorldSpacePanelPresenter loadWorldPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(LoadWorldPanelName);
            BlockiverseWorldSpacePanelPresenter settingsPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(SettingsPanelName);
            BlockiverseWorldSpacePanelPresenter comfortSettingsPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ComfortSettingsPanelName);
            BlockiverseWorldSpacePanelPresenter stationPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(StationPanelName);
            BlockiverseWorldSpacePanelPresenter lanPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(LanMultiplayerPanelName);
            BlockiverseWorldSpacePanelPresenter audioPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(AudioSettingsPanelName);
            BlockiverseWorldSpacePanelPresenter controlsPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ControlsPanelName);
            BlockiverseWorldSpacePanelPresenter worldDetailsPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(WorldDetailsPanelName);
            BlockiverseWorldSpacePanelPresenter creativeToolsPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(CreativeToolsPanelName);
            BlockiverseWorldSpacePanelPresenter gameplayHudPresenter =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(SurvivalHudName)
                ?? EnsureGameplayHudPresenter();
            BlockiverseWorldSpacePanelPresenter controllerMapping =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ControllerMappingPopupName);
            BlockiverseWorldSpacePanelPresenter worldLoading =
                FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(StartupLoadingOverlayName);

            if (screenPresenters.Count == 0 ||
                titlePresenter != null || pausePresenter != null || deathPresenter != null ||
                confirmPresenter != null || newWorldPresenter != null || loadWorldPresenter != null ||
                settingsPresenter != null || comfortSettingsPresenter != null || stationPresenter != null || lanPresenter != null ||
                audioPresenter != null || controlsPresenter != null || worldDetailsPresenter != null ||
                creativeToolsPresenter != null || gameplayHudPresenter != null || controllerMapping != null ||
                worldLoading != null)
            {
                ConfigurePresenters(
                    titlePresenter,
                    pausePresenter,
                    deathPresenter,
                    confirmPresenter,
                    newWorldPresenter,
                    loadWorldPresenter,
                    settingsPresenter,
                    stationPresenter,
                    lanPresenter,
                    audioPresenter,
                    controlsPresenter,
                    worldDetailsPresenter,
                    creativeToolsPresenter,
                    gameplayHudPresenter,
                    comfortSettingsPresenter,
                    controllerMapping,
                    worldLoading);
            }
        }

        // Called by the bootstrapper after building the XR rig; also subscribed at runtime via Start.
        public void OnMenuPressed()
        {
            if (router == null) return;
            string active = router.ActiveScreen.ScreenId;
            if (active == MenuActions.GameplayHudScreen)
            {
                RefreshPauseMenu();
                router.PushScreen(new ScreenRoute(MenuActions.PauseScreen, pauseGame: true));
            }
            else if (active == MenuActions.ControllerMappingScreen && !router.HasModal)
                CloseControllerMappingScreen();
            else if (active == MenuActions.StationMenuScreen && !router.HasModal)
                HandleStationCloseRequested();
            else if (active != MenuActions.TitleScreen && active != MenuActions.DeathScreen && !router.HasModal)
                // Generic escape hatch: the menu button pops any other screen (pause included),
                // so a screen without its own close action can never trap the player. The death
                // screen is excluded — dismissing it here would skip Respawn() and leave the
                // player walking around dead; its own actions handle respawn before popping.
                router.PopScreen();
        }

        // Called by the world manager after a world is created or loaded.
        public void EnterGameplay()
        {
            router?.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
        }

        public void ShowWorldLoadingScreen()
        {
            router?.ClearToRoot(new ScreenRoute(MenuActions.WorldLoadingScreen, pauseGame: true));
        }

        public void ShowTitleScreen()
        {
            router?.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
            RefreshTitleMenu();
        }

        // Shows the death screen, updating the respawn options.
        public void ShowDeathScreen(bool hasBedrollSpawn)
        {
            if (deathMenu == null)
                ResolveRuntimeReferences();

            deathMenu?.SetMenu(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleDeath),
                MenuActions.Death(hasBedrollSpawn));
            if (router == null)
                return;

            router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
            router.PushScreen(new ScreenRoute(MenuActions.DeathScreen, pauseGame: true));
        }

        // Pushes a confirm modal over the current screen; callback receives true on accept, false on cancel.
        public void RequestConfirm(string prompt, string confirmLabel, string cancelLabel, Action<bool> callback)
        {
            confirmMenu?.SetMenu(prompt, MenuActions.Confirm(confirmLabel, cancelLabel));
            confirmCallback = callback;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        // Feeds the load-world panel when saves are enumerated by the world manager.
        public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
        {
            if (loadWorldPanel == null)
                ResolveRuntimeReferences();
            loadWorldPanel?.SetSaves(saves);
        }

        public void SetLoadWorldStatus(string message)
        {
            if (loadWorldPanel == null)
                ResolveRuntimeReferences();
            loadWorldPanel?.SetStatus(message);
        }

        public bool IsActiveScreen(string screenId)
        {
            return router != null && router.ActiveScreen.ScreenId == screenId;
        }

        // Exposes the pending new-world config so the world manager can read it on NewWorldCreate.
        public NewWorldConfig PendingNewWorldConfig
        {
            get
            {
                if (newWorldPanel == null)
                    ResolveRuntimeReferences();
                return newWorldPanel?.Config;
            }
        }

        // Exposes the selected save so the world manager can read it on LoadWorldLoad.
        public WorldSaveSummary? PendingLoadSave
        {
            get
            {
                if (loadWorldPanel == null)
                    ResolveRuntimeReferences();
                return loadWorldPanel?.SelectedSave;
            }
        }

        // The save shown on the World Details screen (§6.5) and its pending rename text.
        public WorldSaveSummary? PendingDetailsSave
        {
            get
            {
                if (worldDetailsPanel == null)
                    ResolveRuntimeReferences();
                return worldDetailsPanel?.CurrentSave;
            }
        }

        public string PendingDetailsRenameText
        {
            get
            {
                if (worldDetailsPanel == null)
                    ResolveRuntimeReferences();
                return worldDetailsPanel?.PendingRenameText ?? string.Empty;
            }
        }

        // Re-shows the details screen content for an updated save (after rename/duplicate).
        public void ShowWorldDetails(WorldSaveSummary save)
        {
            if (worldDetailsPanel == null)
                ResolveRuntimeReferences();
            worldDetailsPanel?.ShowSave(save);
        }

        // Pops the World Details screen (after a delete removed the shown save).
        public void CloseWorldDetails()
        {
            if (router != null && router.ActiveScreen.ScreenId == MenuActions.WorldDetailsScreen && !router.HasModal)
                router.PopScreen();
        }

        // Generic error dialog (§6.22 adapted): a single-OK modal over the current screen.
        public void ShowError(string message)
        {
            confirmMenu?.SetMenu(
                message,
                new[]
                {
                    new MenuAction(
                        MenuActions.ConfirmAccept,
                        BlockiverseLocalization.Keys.ConfirmOk,
                        "OK"),
                });
            confirmCallback = null;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        void Start()
        {
            ResolveRuntimeReferences();

            router = new UiScreenRouter(ResolveInitialRoute());
            router.Changed += ApplyRouterState;

            if (inputRig != null)
                inputRig.MenuPressed.AddListener(OnMenuPressed);

            WireMenus();
            WireStationPanel();
            WireVitalsRuntime();

            RefreshTitleMenu();
            RefreshPauseMenu();
            deathMenu?.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleDeath), MenuActions.Death(false));
            confirmMenu?.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleConfirm), MenuActions.Confirm());
            settingsMenu?.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleSettings), MenuActions.Settings);
            worldDetailsMenu?.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleWorldDetails), MenuActions.WorldDetails);

            if (comfortMenu == null)
                comfortMenu = BlockiverseSceneLookup.Find<BlockiverseComfortMenu>(FindObjectsInactive.Include);

            ApplyRouterState();
        }

        ScreenRoute ResolveInitialRoute()
        {
            return controllerMappingPresenter != null && ShouldShowControllerMappingOnStart()
                ? new ScreenRoute(MenuActions.ControllerMappingScreen, pauseGame: true)
                : new ScreenRoute(MenuActions.TitleScreen, pauseGame: true);
        }

        static bool ShouldShowControllerMappingOnStart() =>
            PlayerPrefs.GetInt(BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey, 0) == 0;

        public void CloseControllerMappingScreen()
        {
            PlayerPrefs.SetInt(BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey, 1);
            PlayerPrefs.Save();

            if (router != null && router.ActiveScreen.ScreenId == MenuActions.ControllerMappingScreen)
            {
                router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                RefreshTitleMenu();
                return;
            }

            controllerMappingPresenter?.Hide();
        }

        // Close hook for the LAN multiplayer panel's button (wired by the bootstrapper).
        public void CloseLanMultiplayerScreen()
        {
            HandleAction(MenuActions.LanMultiplayerClose);
        }

        public void CloseComfortSettingsScreen()
        {
            HandleAction(MenuActions.ComfortSettingsClose);
        }

        public bool ShowLanMultiplayerScreen()
        {
            if (router == null)
                return false;

            if (router.ActiveScreen.ScreenId != MenuActions.LanMultiplayerScreen)
                router.PushScreen(new ScreenRoute(MenuActions.LanMultiplayerScreen, pauseGame: true));

            return true;
        }

        // Status line on the title menu — used by the session coordinator to surface
        // save/load failures without leaving the title screen.
        public void SetTitleStatus(string message)
        {
            titleMenu?.SetStatus(message ?? string.Empty);
        }

        // Status line on the pause menu — used by save/autosave flows while gameplay is paused.
        public void SetPauseStatus(string message)
        {
            pauseMenu?.SetStatus(message ?? string.Empty);
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;
            BlockiverseRuntimeState.Reset();
            inputRig?.MenuPressed.RemoveListener(OnMenuPressed);
            UnwireMenus();
            UnwireStationPanel();
            UnwireVitalsRuntime();
        }

        // ── Player vitals: death and respawn (§6.21, §13) ───────────────────────────────────────

        void WireVitalsRuntime()
        {
            if (vitalsRuntime == null)
                vitalsRuntime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

            if (vitalsRuntime != null)
                vitalsRuntime.LocalPlayerDied += HandleLocalPlayerDied;
        }

        void UnwireVitalsRuntime()
        {
            if (vitalsRuntime != null)
                vitalsRuntime.LocalPlayerDied -= HandleLocalPlayerDied;
        }

        void HandleLocalPlayerDied()
        {
            if (router == null)
                return;

            if (stationPanel != null && stationPanel.IsOpen)
                stationPanel.Close();

            ShowDeathScreen(vitalsRuntime != null && vitalsRuntime.HasBedrollSpawn);
        }

        // ── Smelting-station panel (§8.4) ───────────────────────────────────────────────────────

        void WireStationPanel()
        {
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (survivalSync != null)
            {
                survivalSync.StationOpenRequested += HandleStationOpenRequested;
                survivalSync.StationRemoved += HandleStationRemoved;
            }

            if (stationPanel != null)
            {
                stationPanel.ConfigureSurvivalSync(survivalSync);
                stationPanel.CloseRequested += HandleStationCloseRequested;
            }
        }

        void UnwireStationPanel()
        {
            if (survivalSync != null)
            {
                survivalSync.StationOpenRequested -= HandleStationOpenRequested;
                survivalSync.StationRemoved -= HandleStationRemoved;
            }

            if (stationPanel != null)
                stationPanel.CloseRequested -= HandleStationCloseRequested;
        }

        void HandleStationOpenRequested(BlockPosition position, CraftingStation stationType)
        {
            if (router == null || stationPanel == null || survivalSync == null)
                return;

            if (router.ActiveScreen.ScreenId != MenuActions.GameplayHudScreen)
                return;

            SmeltingStationModel model = survivalSync.GetOrCreateStationModel(position, stationType);
            stationPanel.Open(model, position);
            // Pulls the authoritative state onto remote-client mirrors; a no-op validation on the host.
            survivalSync.TrySubmitStationOpen(position, out _);
            router.PushScreen(new ScreenRoute(MenuActions.StationMenuScreen));
            PlayStationCue(BlockiverseAudioCue.ContainerOpen);
        }

        void HandleStationCloseRequested()
        {
            bool wasOpen = stationPanel != null && stationPanel.IsOpen;
            stationPanel?.Close();

            if (router != null && router.ActiveScreen.ScreenId == MenuActions.StationMenuScreen)
                router.PopScreen();

            if (wasOpen)
                PlayStationCue(BlockiverseAudioCue.ContainerClose);
        }

        void HandleStationRemoved(BlockPosition position)
        {
            if (stationPanel == null || !stationPanel.IsOpen || stationPanel.OpenPosition != position)
                return;

            HandleStationCloseRequested();
        }

        BlockiverseAudioCuePlayer stationCuePlayer;

        void PlayStationCue(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.PlayAudio(ref stationCuePlayer, cue);
        }

        void HandleAction(string actionId)
        {
            if (router == null || string.IsNullOrEmpty(actionId)) return;

            switch (actionId)
            {
                case MenuActions.TitleContinue:
                    // The session coordinator loads the save and calls EnterGameplay() on
                    // success — a failed load must leave the player on the title screen.
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.TitleNewWorld:
                    newWorldPanel?.ResetForNewWorld();
                    router.PushScreen(new ScreenRoute(MenuActions.NewWorldScreen, pauseGame: true));
                    break;
                case MenuActions.TitleLoadWorld:
                    ActionRequested?.Invoke(actionId);
                    router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));
                    break;
                case MenuActions.TitleMultiplayer:
                    ShowLanMultiplayerScreen();
                    break;
                case MenuActions.LanMultiplayerClose:
                    if (router.ActiveScreen.ScreenId == MenuActions.LanMultiplayerScreen)
                        router.PopScreen();
                    break;
                case MenuActions.TitleSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.TitleQuit:
                    RequestQuitConfirmation(actionId, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleQuit));
                    break;

                case MenuActions.PauseResume:
                    router.PopScreen();
                    break;
                case MenuActions.PauseSaveGame:
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.PauseToggleMode:
                    if (BlockiverseSceneLookup.Find<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include)
                        ?.ToggleSurvivalCreativeMode() == true)
                    {
                        ActionRequested?.Invoke(actionId);
                        RefreshPauseMenu();
                        router.PopScreen();
                    }
                    break;
                case MenuActions.PauseCreativeTools:
                    if (survivalSync == null || !survivalSync.CanUseCreativeMode)
                    {
                        RefreshPauseMenu();
                        break;
                    }

                    // Pushed without pausing: corner selection aims the live interaction ray at
                    // world blocks while the panel is open (same posture as the station screen).
                    router.PushScreen(new ScreenRoute(MenuActions.CreativeToolsScreen));
                    break;
                case MenuActions.CreativeToolsClose:
                    if (router.ActiveScreen.ScreenId == MenuActions.CreativeToolsScreen)
                        router.PopScreen();
                    break;
                case MenuActions.PauseSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.PauseReturnToTitle:
                    ActionRequested?.Invoke(actionId);
                    router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                    RefreshTitleMenu();
                    break;
                case MenuActions.PauseQuit:
                    RequestQuitConfirmation(MenuActions.PauseSaveGame, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseQuit));
                    break;

                case MenuActions.DeathRespawnBedroll:
                    vitalsRuntime?.RespawnAtBedroll();
                    ActionRequested?.Invoke(actionId);
                    router.PopScreen();
                    break;
                case MenuActions.DeathRespawnWorldSpawn:
                    vitalsRuntime?.Respawn();
                    ActionRequested?.Invoke(actionId);
                    router.PopScreen();
                    break;
                case MenuActions.DeathReturnToTitle:
                    // Restore vitals so re-entering gameplay does not start dead.
                    vitalsRuntime?.Respawn();
                    ActionRequested?.Invoke(actionId);
                    router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                    break;

                case MenuActions.ConfirmAccept:
                {
                    var cb = confirmCallback;
                    confirmCallback = null;
                    router.PopModal();
                    cb?.Invoke(true);
                    break;
                }
                case MenuActions.ConfirmCancel:
                    confirmCallback = null;
                    router.PopModal();
                    break;

                case MenuActions.NewWorldCreate:
                    // Routed to gameplay by the session coordinator (EnterGameplay) only after
                    // generation succeeds.
                    if (newWorldPanel?.Config?.IsValid(out _) == true)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.NewWorldCancel:
                    router.PopScreen();
                    break;

                case MenuActions.LoadWorldLoad:
                    if (loadWorldPanel?.SelectedSave.HasValue == true)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.LoadWorldCancel:
                    router.PopScreen();
                    break;
                case MenuActions.LoadWorldDetails:
                    if (loadWorldPanel?.SelectedSave.HasValue == true && worldDetailsPanel != null)
                    {
                        worldDetailsPanel.ShowSave(loadWorldPanel.SelectedSave.Value);
                        router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
                    }
                    break;

                // ── World Details (§6.5): file operations live in the session controller ──
                case MenuActions.WorldDetailsPlay:
                case MenuActions.WorldDetailsRename:
                case MenuActions.WorldDetailsDuplicate:
                    if (worldDetailsPanel?.CurrentSave.HasValue == true)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.WorldDetailsDeleteRequested:
                    if (worldDetailsPanel?.CurrentSave.HasValue == true)
                    {
                        string worldName = worldDetailsPanel.CurrentSave.Value.Name;
                        RequestConfirm(
                            BlockiverseLocalization.Format(BlockiverseLocalization.Keys.WorldDetailsDeletePrompt, worldName),
                            BlockiverseLocalization.Text(BlockiverseLocalization.Keys.WorldDetailsDelete),
                            BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmCancel),
                            accepted =>
                            {
                                if (accepted)
                                    ActionRequested?.Invoke(actionId);
                            });
                    }
                    break;
                case MenuActions.WorldDetailsBack:
                    router.PopScreen();
                    break;

                case MenuActions.SettingsOpenComfort:
                    router.PushScreen(new ScreenRoute(MenuActions.ComfortSettingsScreen, pauseGame: true));
                    break;
                case MenuActions.SettingsOpenAudio:
                    router.PushScreen(new ScreenRoute(MenuActions.AudioSettingsScreen, pauseGame: true));
                    break;
                case MenuActions.SettingsOpenControls:
                    router.PushScreen(new ScreenRoute(MenuActions.ControlsScreen, pauseGame: true));
                    break;
                case MenuActions.SettingsClose:
                case MenuActions.ComfortSettingsClose:
                case MenuActions.AudioSettingsClose:
                case MenuActions.ControlsClose:
                    router.PopScreen();
                    break;
            }
        }

        void ApplyRouterState()
        {
            if (router == null) return;

            BlockiverseRuntimeState.SetRouterState(router.IsGamePaused, router.AllowWorldInput);
            string activeId = router.ActiveScreen.ScreenId;
            string inputTarget = router.InputTarget;
            if (!HasConfirmModalOpen())
                confirmCallback = null;

            foreach (var (screenId, presenter) in screenPresenters)
            {
                bool isModal = screenId == MenuActions.ConfirmModal;
                bool visible = isModal
                    ? router.HasModal && inputTarget == screenId
                    : screenId == activeId;

                if (string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal))
                    presenter.GetComponent<BlockiverseStartupOverlay>()?.SetAutomaticHide(!visible);

                if (visible && !presenter.IsVisible)
                    presenter.Show();
                else if (!visible && presenter.IsVisible)
                    presenter.Hide();

                bool acceptsInput = visible &&
                    !string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal) &&
                    string.Equals(screenId, inputTarget, StringComparison.Ordinal);
                SetPresenterInputEnabled(presenter, acceptsInput);
            }
        }

        bool HasConfirmModalOpen()
        {
            if (router == null)
                return false;

            foreach (string modalId in router.ModalStack)
                if (string.Equals(modalId, MenuActions.ConfirmModal, StringComparison.Ordinal))
                    return true;

            return false;
        }

        void AddPresenter(string screenId, BlockiverseWorldSpacePanelPresenter presenter)
        {
            if (presenter != null)
                screenPresenters.Add((screenId, presenter));
        }

        BlockiverseWorldSpacePanelPresenter EnsureGameplayHudPresenter()
        {
            Canvas hudCanvas = FindGeneratedComponent<Canvas>(SurvivalHudName);
            if (hudCanvas == null)
                return null;

            hudCanvas.enabled = false;
            BlockiverseWorldSpacePanelPresenter presenter =
                hudCanvas.GetComponent<BlockiverseWorldSpacePanelPresenter>() ??
                hudCanvas.gameObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                hudCanvas,
                Camera.main != null ? Camera.main.transform : null,
                distance: 1.15f,
                horizontalOffset: 0f,
                verticalOffset: -0.30f,
                pitch: 12f,
                scale: GameplayHudScale,
                recenterWhenShown: false);
            return presenter;
        }

        static void SetPresenterInputEnabled(BlockiverseWorldSpacePanelPresenter presenter, bool acceptsInput)
        {
            if (presenter == null)
                return;

            CanvasGroup group = presenter.GetComponent<CanvasGroup>();
            if (group == null)
                group = presenter.gameObject.AddComponent<CanvasGroup>();

            group.interactable = acceptsInput;
            group.blocksRaycasts = acceptsInput;
        }

        void WireMenus()
        {
            if (titleMenu != null) titleMenu.ActionInvoked += HandleAction;
            if (pauseMenu != null) pauseMenu.ActionInvoked += HandleAction;
            if (deathMenu != null) deathMenu.ActionInvoked += HandleAction;
            if (confirmMenu != null) confirmMenu.ActionInvoked += HandleAction;
            if (settingsMenu != null) settingsMenu.ActionInvoked += HandleAction;
            if (worldDetailsMenu != null) worldDetailsMenu.ActionInvoked += HandleAction;
            if (newWorldPanel != null) newWorldPanel.ActionRequested += HandleAction;
            if (loadWorldPanel != null) loadWorldPanel.ActionRequested += HandleAction;
        }

        void UnwireMenus()
        {
            if (titleMenu != null) titleMenu.ActionInvoked -= HandleAction;
            if (pauseMenu != null) pauseMenu.ActionInvoked -= HandleAction;
            if (deathMenu != null) deathMenu.ActionInvoked -= HandleAction;
            if (confirmMenu != null) confirmMenu.ActionInvoked -= HandleAction;
            if (settingsMenu != null) settingsMenu.ActionInvoked -= HandleAction;
            if (worldDetailsMenu != null) worldDetailsMenu.ActionInvoked -= HandleAction;
            if (newWorldPanel != null) newWorldPanel.ActionRequested -= HandleAction;
            if (loadWorldPanel != null) loadWorldPanel.ActionRequested -= HandleAction;
        }

        T FindGeneratedComponent<T>(string objectName) where T : Component
        {
            foreach (T component in GetComponentsInChildren<T>(true))
            {
                if (component != null && component.gameObject.name == objectName)
                    return component;
            }

            return null;
        }

        // Close hooks for panels whose Close buttons are wired as persistent listeners by the
        // bootstrapper (same pattern as CloseLanMultiplayerScreen).
        public void CloseAudioSettingsScreen() => HandleAction(MenuActions.AudioSettingsClose);
        public void CloseControlsScreen() => HandleAction(MenuActions.ControlsClose);
        public void CloseCreativeToolsScreen() => HandleAction(MenuActions.CreativeToolsClose);

        void RequestQuitConfirmation(string saveActionId, string confirmLabel)
        {
            RequestConfirm(
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame),
                confirmLabel,
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmCancel),
                accepted =>
                {
                    if (!accepted)
                        return;

                    ActionRequested?.Invoke(saveActionId);
                    Application.Quit();
                });
        }

        static bool CanQuit() =>
#if UNITY_EDITOR
            true;
#else
            false; // Quest apps exit via the system Home button, not an in-app quit action.
#endif
    }
}
