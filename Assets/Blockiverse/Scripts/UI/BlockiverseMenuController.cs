using System;
using System.Collections.Generic;
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
        BlockiverseInputRig inputRig;

        BlockiverseActionMenu titleMenu;
        BlockiverseActionMenu pauseMenu;
        BlockiverseActionMenu deathMenu;
        BlockiverseActionMenu confirmMenu;
        BlockiverseActionMenu settingsMenu;
        BlockiverseNewWorldPanel newWorldPanel;
        BlockiverseLoadWorldPanel loadWorldPanel;
        [SerializeField] BlockiverseStationPanel stationPanel;
        MultiplayerSurvivalSync survivalSync;
        SurvivalVitalsRuntime vitalsRuntime;

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
            titleMenu?.SetMenu("Blockiverse", MenuActions.Title(hasLatestSave, hasAnySave, CanQuit()));
        }

        public void Configure(
            BlockiverseInputRig rig,
            BlockiverseActionMenu title,
            BlockiverseActionMenu pause,
            BlockiverseActionMenu death,
            BlockiverseActionMenu confirm,
            BlockiverseNewWorldPanel newWorld,
            BlockiverseLoadWorldPanel loadWorld,
            BlockiverseActionMenu settings = null)
        {
            inputRig = rig;
            titleMenu = title;
            pauseMenu = pause;
            deathMenu = death;
            confirmMenu = confirm;
            settingsMenu = settings;
            newWorldPanel = newWorld;
            loadWorldPanel = loadWorld;
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
            BlockiverseWorldSpacePanelPresenter lanMultiplayer = null)
        {
            screenPresenters.Clear();
            AddPresenter(MenuActions.TitleScreen, title);
            AddPresenter(MenuActions.PauseScreen, pause);
            AddPresenter(MenuActions.DeathScreen, death);
            AddPresenter(MenuActions.ConfirmModal, confirm);
            AddPresenter(MenuActions.NewWorldScreen, newWorld);
            AddPresenter(MenuActions.LoadWorldScreen, loadWorld);
            AddPresenter(MenuActions.SettingsScreen, settings);
            if (station != null)
                AddPresenter(MenuActions.StationMenuScreen, station);
            if (lanMultiplayer != null)
                AddPresenter(MenuActions.LanMultiplayerScreen, lanMultiplayer);
        }

        // Wires the smelting-station panel so a "use" on a kiln/forge block opens it (§8.4).
        public void ConfigureStationPanel(BlockiverseStationPanel panel)
        {
            stationPanel = panel;
        }

        // Called by the bootstrapper after building the XR rig; also subscribed at runtime via Start.
        public void OnMenuPressed()
        {
            if (router == null) return;
            string active = router.ActiveScreen.ScreenId;
            if (active == MenuActions.GameplayHudScreen)
                router.PushScreen(new ScreenRoute(MenuActions.PauseScreen, pauseGame: true));
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

        // Shows the death screen, updating the respawn options.
        public void ShowDeathScreen(bool hasBedrollSpawn)
        {
            deathMenu?.SetMenu("You Died", MenuActions.Death(hasBedrollSpawn));
            router?.PushScreen(new ScreenRoute(MenuActions.DeathScreen, pauseGame: true));
        }

        // Pushes a confirm modal over the current screen; callback receives true on accept, false on cancel.
        public void RequestConfirm(string prompt, string confirmLabel, string cancelLabel, Action<bool> callback)
        {
            confirmMenu?.SetMenu(prompt, MenuActions.Confirm(confirmLabel, cancelLabel));
            confirmCallback = callback;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        // Feeds the load-world panel when saves are enumerated by the world manager.
        public void SetSaveList(IEnumerable<WorldSaveSummary> saves) => loadWorldPanel?.SetSaves(saves);

        // Exposes the pending new-world config so the world manager can read it on NewWorldCreate.
        public NewWorldConfig PendingNewWorldConfig => newWorldPanel?.Config;

        // Exposes the selected save so the world manager can read it on LoadWorldLoad.
        public WorldSaveSummary? PendingLoadSave => loadWorldPanel?.SelectedSave;

        void Start()
        {
            router = new UiScreenRouter(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
            router.Changed += ApplyRouterState;

            if (inputRig != null)
                inputRig.MenuPressed.AddListener(OnMenuPressed);

            WireMenus();
            WireStationPanel();
            WireVitalsRuntime();

            RefreshTitleMenu();
            pauseMenu?.SetMenu("Paused", MenuActions.Pause);
            deathMenu?.SetMenu("You Died", MenuActions.Death(false));
            confirmMenu?.SetMenu("Confirm?", MenuActions.Confirm());

            ApplyRouterState();
        }

        // Close hook for the LAN multiplayer panel's button (wired by the bootstrapper).
        public void CloseLanMultiplayerScreen()
        {
            HandleAction(MenuActions.LanMultiplayerClose);
        }

        // Status line on the title menu — used by the session coordinator to surface
        // save/load failures without leaving the title screen.
        public void SetTitleStatus(string message)
        {
            titleMenu?.SetStatus(message ?? string.Empty);
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;
            inputRig?.MenuPressed.RemoveListener(OnMenuPressed);
            UnwireMenus();
            UnwireStationPanel();
            UnwireVitalsRuntime();
        }

        // ── Player vitals: death and respawn (§6.21, §13) ───────────────────────────────────────

        void WireVitalsRuntime()
        {
            if (vitalsRuntime == null)
                vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

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

            // Dying with the station panel open closes it first so the death screen sits over the HUD.
            if (router.ActiveScreen.ScreenId == MenuActions.StationMenuScreen && !router.HasModal)
                HandleStationCloseRequested();

            if (router.ActiveScreen.ScreenId == MenuActions.GameplayHudScreen)
                ShowDeathScreen(vitalsRuntime != null && vitalsRuntime.HasBedrollSpawn);
        }

        // ── Smelting-station panel (§8.4) ───────────────────────────────────────────────────────

        void WireStationPanel()
        {
            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (survivalSync != null)
                survivalSync.StationOpenRequested += HandleStationOpenRequested;

            if (stationPanel != null)
            {
                stationPanel.ConfigureSurvivalSync(survivalSync);
                stationPanel.CloseRequested += HandleStationCloseRequested;
            }
        }

        void UnwireStationPanel()
        {
            if (survivalSync != null)
                survivalSync.StationOpenRequested -= HandleStationOpenRequested;

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

        BlockiverseAudioCuePlayer stationCuePlayer;

        void PlayStationCue(BlockiverseAudioCue cue)
        {
            if (!Application.isPlaying)
                return;

            if (stationCuePlayer == null)
                stationCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            stationCuePlayer?.PlayCue(cue);
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
                    router.PushScreen(new ScreenRoute(MenuActions.LanMultiplayerScreen, pauseGame: true));
                    break;
                case MenuActions.LanMultiplayerClose:
                    if (router.ActiveScreen.ScreenId == MenuActions.LanMultiplayerScreen)
                        router.PopScreen();
                    break;
                case MenuActions.TitleSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.TitleQuit:
                    // Give the session coordinator a chance to save before the process exits.
                    ActionRequested?.Invoke(actionId);
                    Application.Quit();
                    break;

                case MenuActions.PauseResume:
                    router.PopScreen();
                    break;
                case MenuActions.PauseSaveGame:
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.PauseToggleMode:
                    FindFirstObjectByType<BlockiverseCreativeInputBridge>(FindObjectsInactive.Include)
                        ?.ToggleSurvivalCreativeMode();
                    ActionRequested?.Invoke(actionId);
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
                    ActionRequested?.Invoke(MenuActions.PauseSaveGame);
                    Application.Quit();
                    break;

                case MenuActions.DeathRespawnBedroll:
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

                case MenuActions.SettingsClose:
                    router.PopScreen();
                    break;
            }
        }

        void ApplyRouterState()
        {
            if (router == null) return;

            string activeId = router.ActiveScreen.ScreenId;
            string inputTarget = router.InputTarget;

            foreach (var (screenId, presenter) in screenPresenters)
            {
                bool isModal = screenId == MenuActions.ConfirmModal;
                bool visible = isModal
                    ? router.HasModal && inputTarget == screenId
                    : screenId == activeId;

                if (visible && !presenter.IsVisible)
                    presenter.Show();
                else if (!visible && presenter.IsVisible)
                    presenter.Hide();
            }
        }

        void AddPresenter(string screenId, BlockiverseWorldSpacePanelPresenter presenter)
        {
            if (presenter != null)
                screenPresenters.Add((screenId, presenter));
        }

        void WireMenus()
        {
            if (titleMenu != null) titleMenu.ActionInvoked += HandleAction;
            if (pauseMenu != null) pauseMenu.ActionInvoked += HandleAction;
            if (deathMenu != null) deathMenu.ActionInvoked += HandleAction;
            if (confirmMenu != null) confirmMenu.ActionInvoked += HandleAction;
            if (settingsMenu != null) settingsMenu.ActionInvoked += HandleAction;
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
            if (newWorldPanel != null) newWorldPanel.ActionRequested -= HandleAction;
            if (loadWorldPanel != null) loadWorldPanel.ActionRequested -= HandleAction;
        }

        static bool CanQuit() =>
#if UNITY_EDITOR
            true;
#else
            false; // Quest apps exit via the system Home button, not an in-app quit action.
#endif
    }
}
