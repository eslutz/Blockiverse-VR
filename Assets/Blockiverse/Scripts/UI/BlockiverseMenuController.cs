using System;
using System.Collections.Generic;
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

        readonly List<(string screenId, BlockiverseWorldSpacePanelPresenter presenter)> screenPresenters = new();
        Action<bool> confirmCallback;
        UiScreenRouter router;

        public UiScreenRouter Router => router;

        // Fires when the controller needs the world manager to act (save, load, respawn, etc.).
        public event Action<string> ActionRequested;

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
            BlockiverseWorldSpacePanelPresenter station = null)
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
                AddPresenter("station_menu", station);
        }

        // Called by the bootstrapper after building the XR rig; also subscribed at runtime via Start.
        public void OnMenuPressed()
        {
            if (router == null) return;
            string active = router.ActiveScreen.ScreenId;
            if (active == MenuActions.GameplayHudScreen)
                router.PushScreen(new ScreenRoute(MenuActions.PauseScreen, pauseGame: true));
            else if (active == MenuActions.PauseScreen && !router.HasModal)
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

            titleMenu?.SetMenu("Blockiverse", MenuActions.Title(false, false, CanQuit()));
            pauseMenu?.SetMenu("Paused", MenuActions.Pause);
            deathMenu?.SetMenu("You Died", MenuActions.Death(false));
            confirmMenu?.SetMenu("Confirm?", MenuActions.Confirm());

            ApplyRouterState();
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;
            inputRig?.MenuPressed.RemoveListener(OnMenuPressed);
            UnwireMenus();
        }

        void HandleAction(string actionId)
        {
            if (router == null || string.IsNullOrEmpty(actionId)) return;

            switch (actionId)
            {
                case MenuActions.TitleContinue:
                    ActionRequested?.Invoke(actionId);
                    router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
                    break;
                case MenuActions.TitleNewWorld:
                    newWorldPanel?.ResetForNewWorld();
                    router.PushScreen(new ScreenRoute(MenuActions.NewWorldScreen, pauseGame: true));
                    break;
                case MenuActions.TitleLoadWorld:
                    router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));
                    break;
                case MenuActions.TitleSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.TitleQuit:
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
                case MenuActions.PauseControls:
                    router.PushScreen(new ScreenRoute(MenuActions.ControlsScreen, pauseGame: true));
                    break;
                case MenuActions.PauseReturnToTitle:
                    ActionRequested?.Invoke(actionId);
                    router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                    titleMenu?.SetMenu("Blockiverse", MenuActions.Title(false, false, CanQuit()));
                    break;
                case MenuActions.PauseQuit:
                    ActionRequested?.Invoke(MenuActions.PauseSaveGame);
                    Application.Quit();
                    break;

                case MenuActions.DeathRespawnBedroll:
                case MenuActions.DeathRespawnWorldSpawn:
                    ActionRequested?.Invoke(actionId);
                    router.PopScreen();
                    break;
                case MenuActions.DeathReturnToTitle:
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
                    if (newWorldPanel?.Config?.IsValid(out _) == true)
                    {
                        ActionRequested?.Invoke(actionId);
                        router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
                    }
                    break;
                case MenuActions.NewWorldCancel:
                    router.PopScreen();
                    break;

                case MenuActions.LoadWorldLoad:
                    if (loadWorldPanel?.SelectedSave.HasValue == true)
                    {
                        ActionRequested?.Invoke(actionId);
                        router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
                    }
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
