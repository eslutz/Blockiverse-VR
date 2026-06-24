using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using UnityEngine;
using UnityEngine.UI;

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
        const string BlockMenuName = "Block Menu";
        const string UiToolkitMenuSurfaceName = "UI Toolkit Menu Surface";
        const int UiToolkitLoadWorldMaxEntries = 6;
        const float GameplayHudScale = 0.00105f;
        const float ControllerMappingCloseFallbackMaxDistanceMeters = 3.0f;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseUiToolkitMenuSurface uiToolkitMenuSurface;
        [SerializeField] bool useUiToolkitRuntimeMenus = true;
        BlockiverseUiToolkitMenuSurface wiredUiToolkitMenuSurface;

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
        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;
        [SerializeField] BlockiverseMultiplayerSessionMenu lanMultiplayerMenu;
        [SerializeField] BlockiverseStationPanel stationPanel;
        [SerializeField] BlockiverseCreativeToolsPanel creativeToolsPanel;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] SurvivalHudController survivalHudController;
        [SerializeField] CreativeHotbar creativeHotbar;
        [SerializeField] BlockiverseWorldSpacePanelPresenter controllerMappingPresenter;
        [SerializeField] BlockiverseWorldSpacePanelPresenter worldLoadingPresenter;
        [SerializeField] BlockiverseWorldSpacePanelPresenter blockMenuPresenter;
        [SerializeField] Button controllerMappingCloseButton;

        readonly List<(string screenId, BlockiverseWorldSpacePanelPresenter presenter)> screenPresenters = new();
        Action<bool> confirmCallback;
        IReadOnlyList<MenuAction> confirmActions = MenuActions.Confirm();
        string confirmPrompt = string.Empty;
        string titleStatus = string.Empty;
        string pauseStatus = string.Empty;
        bool deathHasBedrollSpawn;
        UiScreenRouter router;
        bool hasLatestSave;
        bool hasAnySave;
        NewWorldConfig uiToolkitNewWorldConfig;
        string uiToolkitNewWorldStatus = string.Empty;
        readonly SaveListModel uiToolkitSaveList = new();
        int uiToolkitLoadWorldPageIndex;
        string uiToolkitLoadWorldStatus = string.Empty;
        WorldSaveSummary? uiToolkitDetailsSave;
        string uiToolkitDetailsRenameText = string.Empty;
        string uiToolkitLanAddress = BlockiverseNetworkConfig.DefaultAddress;
        int uiToolkitInventoryFirstSlot;
        int uiToolkitSelectedInventorySlot;
        int uiToolkitCraftingPage;
        int uiToolkitSelectedCraftingRecipe = -1;
        int uiToolkitPinnedCraftingRecipe = -1;
        int uiToolkitContainerFirstSlot;
        int uiToolkitCatalogCategoryIndex;
        int uiToolkitCatalogPageIndex;

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
            ApplyRouterState();
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
            ApplyRouterState();
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

        public void ConfigureUiToolkitMenuSurface(BlockiverseUiToolkitMenuSurface surface, bool useRuntimeMenus = true)
        {
            uiToolkitMenuSurface = surface;
            useUiToolkitRuntimeMenus = useRuntimeMenus;
            WireUiToolkitMenuSurface();
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
            comfortSettings ??= GetComponent<BlockiverseComfortSettings>()
                ?? BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);
            heightReset ??= GetComponent<BlockiverseHeightReset>()
                ?? BlockiverseSceneLookup.Find<BlockiverseHeightReset>(FindObjectsInactive.Include);
            feedbackSettings ??= GetComponent<BlockiverseFeedbackSettings>()
                ?? BlockiverseSceneLookup.Find<BlockiverseFeedbackSettings>(FindObjectsInactive.Include);
            newWorldPanel ??= FindGeneratedComponent<BlockiverseNewWorldPanel>(NewWorldPanelName);
            loadWorldPanel ??= FindGeneratedComponent<BlockiverseLoadWorldPanel>(LoadWorldPanelName);
            worldDetailsPanel ??= FindGeneratedComponent<BlockiverseWorldDetailsPanel>(WorldDetailsPanelName);
            worldDetailsMenu ??= FindGeneratedComponent<BlockiverseActionMenu>(WorldDetailsPanelName);
            stationPanel ??= FindGeneratedComponent<BlockiverseStationPanel>(StationPanelName);
            creativeToolsPanel ??= FindGeneratedComponent<BlockiverseCreativeToolsPanel>(CreativeToolsPanelName);
            lanMultiplayerMenu ??= FindGeneratedComponent<BlockiverseMultiplayerSessionMenu>(LanMultiplayerPanelName);
            uiToolkitMenuSurface ??= FindGeneratedComponent<BlockiverseUiToolkitMenuSurface>(UiToolkitMenuSurfaceName);
            WireUiToolkitMenuSurface();
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            if (vitalsRuntime == null)
                vitalsRuntime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            if (survivalHudController == null)
                survivalHudController = FindGeneratedComponent<SurvivalHudController>(SurvivalHudName)
                    ?? BlockiverseSceneLookup.Find<SurvivalHudController>(FindObjectsInactive.Include);
            if (creativeHotbar == null)
                creativeHotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);

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
            blockMenuPresenter ??= FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(BlockMenuName);
            controllerMappingCloseButton ??= controllerMapping != null
                ? controllerMapping.transform.Find("Panel/Close Button")?.GetComponent<Button>()
                : null;

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
            EnsureRouter(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
            router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));
        }

        public void ShowWorldLoadingScreen()
        {
            EnsureRouter(new ScreenRoute(MenuActions.WorldLoadingScreen, pauseGame: true));
            router.ClearToRoot(new ScreenRoute(MenuActions.WorldLoadingScreen, pauseGame: true));
        }

        public void ShowTitleScreen()
        {
            EnsureRouter(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
            router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
            RefreshTitleMenu();
        }

        // Shows the death screen, updating the respawn options.
        public void ShowDeathScreen(bool hasBedrollSpawn)
        {
            deathHasBedrollSpawn = hasBedrollSpawn;
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
            confirmPrompt = prompt ?? string.Empty;
            confirmActions = MenuActions.Confirm(confirmLabel, cancelLabel);
            confirmMenu?.SetMenu(prompt, confirmActions);
            confirmCallback = callback;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        // Feeds the load-world panel when saves are enumerated by the world manager.
        public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
        {
            var saveList = saves != null ? new List<WorldSaveSummary>(saves) : new List<WorldSaveSummary>();
            uiToolkitSaveList.SetSaves(saveList);
            uiToolkitLoadWorldPageIndex = 0;

            if (loadWorldPanel == null)
                ResolveRuntimeReferences();
            loadWorldPanel?.SetSaves(saveList);
            ApplyRouterState();
        }

        public void SetLoadWorldStatus(string message)
        {
            uiToolkitLoadWorldStatus = message ?? string.Empty;
            if (loadWorldPanel == null)
                ResolveRuntimeReferences();
            loadWorldPanel?.SetStatus(message);
            ApplyRouterState();
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
                if (CanReadUiToolkitPendingState(MenuActions.NewWorldScreen) && uiToolkitNewWorldConfig != null)
                    return uiToolkitNewWorldConfig;

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
                if (CanReadUiToolkitPendingState(MenuActions.LoadWorldScreen) && uiToolkitSaveList.SelectedSave.HasValue)
                    return uiToolkitSaveList.SelectedSave;

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
                if (uiToolkitDetailsSave.HasValue)
                    return uiToolkitDetailsSave;

                if (worldDetailsPanel == null)
                    ResolveRuntimeReferences();
                return worldDetailsPanel?.CurrentSave;
            }
        }

        public string PendingDetailsRenameText
        {
            get
            {
                if (uiToolkitDetailsSave.HasValue)
                    return uiToolkitDetailsRenameText;

                if (worldDetailsPanel == null)
                    ResolveRuntimeReferences();
                return worldDetailsPanel?.PendingRenameText ?? string.Empty;
            }
        }

        // Re-shows the details screen content for an updated save (after rename/duplicate).
        public void ShowWorldDetails(WorldSaveSummary save)
        {
            uiToolkitDetailsSave = save;
            uiToolkitDetailsRenameText = save.Name;

            if (worldDetailsPanel == null)
                ResolveRuntimeReferences();
            worldDetailsPanel?.ShowSave(save);
            ApplyRouterState();
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
            confirmPrompt = message ?? string.Empty;
            confirmActions = new[]
            {
                new MenuAction(
                    MenuActions.ConfirmAccept,
                    BlockiverseLocalization.Keys.ConfirmOk,
                    "OK"),
            };
            confirmMenu?.SetMenu(
                message,
                confirmActions);
            confirmCallback = null;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        void Start()
        {
            ResolveRuntimeReferences();

            EnsureRouter(ResolveInitialRoute());

            if (inputRig != null)
            {
                inputRig.MenuPressed.AddListener(OnMenuPressed);
                inputRig.QuickMenuPressed.AddListener(OnQuickMenuPressed);
                inputRig.BreakPressed.AddListener(OnControllerMappingCloseFallbackPressed);
            }

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

        public void OnQuickMenuPressed()
        {
            if (router == null)
                return;

            if (useUiToolkitRuntimeMenus && uiToolkitMenuSurface != null)
            {
                if (string.Equals(router.ActiveScreen.ScreenId, MenuActions.BlockCatalogScreen, StringComparison.Ordinal))
                {
                    router.PopScreen();
                    return;
                }

                if (CanUseQuickBlockMenu())
                {
                    uiToolkitCatalogPageIndex = 0;
                    router.PushScreen(new ScreenRoute(MenuActions.BlockCatalogScreen));
                    return;
                }
            }

            if (blockMenuPresenter == null)
                blockMenuPresenter = FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(BlockMenuName);

            if (!CanUseQuickBlockMenu())
            {
                HideQuickBlockMenu();
                return;
            }

            blockMenuPresenter?.ToggleVisible();
            SetPresenterInputEnabled(blockMenuPresenter, blockMenuPresenter != null && blockMenuPresenter.IsVisible);
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
            EnsureRouter(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));

            if (router.ActiveScreen.ScreenId != MenuActions.LanMultiplayerScreen)
                router.PushScreen(new ScreenRoute(MenuActions.LanMultiplayerScreen, pauseGame: true));

            return true;
        }

        void EnsureRouter(ScreenRoute root)
        {
            if (router != null)
                return;

            router = new UiScreenRouter(root);
            router.Changed += ApplyRouterState;
        }

        // Status line on the title menu — used by the session coordinator to surface
        // save/load failures without leaving the title screen.
        public void SetTitleStatus(string message)
        {
            titleStatus = message ?? string.Empty;
            titleMenu?.SetStatus(message ?? string.Empty);
            ApplyRouterState();
        }

        // Status line on the pause menu — used by save/autosave flows while gameplay is paused.
        public void SetPauseStatus(string message)
        {
            pauseStatus = message ?? string.Empty;
            pauseMenu?.SetStatus(message ?? string.Empty);
            ApplyRouterState();
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;
            BlockiverseRuntimeState.Reset();
            inputRig?.MenuPressed.RemoveListener(OnMenuPressed);
            inputRig?.QuickMenuPressed.RemoveListener(OnQuickMenuPressed);
            inputRig?.BreakPressed.RemoveListener(OnControllerMappingCloseFallbackPressed);
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
                    uiToolkitNewWorldConfig = new NewWorldConfig();
                    uiToolkitNewWorldConfig.SetName(NewWorldConfig.DefaultName);
                    uiToolkitNewWorldConfig.RandomizeSeed(null);
                    uiToolkitNewWorldStatus = string.Empty;
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
                case MenuActions.LanMultiplayerHost:
                    ResolveRuntimeReferences();
                    lanMultiplayerMenu?.StartLanHost();
                    ApplyRouterState();
                    break;
                case MenuActions.LanMultiplayerJoin:
                    ResolveRuntimeReferences();
                    if (!CanReadUiToolkitPendingState(MenuActions.LanMultiplayerScreen))
                    {
                        ApplyRouterState();
                        break;
                    }

                    if (lanMultiplayerMenu?.AddressInput != null)
                        lanMultiplayerMenu.AddressInput.SetTextWithoutNotify(uiToolkitLanAddress);
                    lanMultiplayerMenu?.JoinLanSession();
                    ApplyRouterState();
                    break;
                case MenuActions.LanMultiplayerStop:
                    ResolveRuntimeReferences();
                    lanMultiplayerMenu?.StopSession();
                    ApplyRouterState();
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
                case MenuActions.PausePlayerHub:
                    router.PushScreen(new ScreenRoute(MenuActions.PlayerHubScreen, pauseGame: true));
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

                case MenuActions.PlayerHubInventory:
                    ResolveRuntimeReferences();
                    uiToolkitInventoryFirstSlot = survivalHudController?.InventoryPanel != null
                        ? survivalHudController.InventoryPanel.FirstVisibleSlotIndex
                        : 0;
                    router.PushScreen(new ScreenRoute(MenuActions.InventoryScreen, pauseGame: true));
                    break;
                case MenuActions.PlayerHubVitals:
                    router.PushScreen(new ScreenRoute(MenuActions.VitalsStatusScreen, pauseGame: true));
                    break;
                case MenuActions.PlayerHubCrafting:
                    uiToolkitCraftingPage = 0;
                    router.PushScreen(new ScreenRoute(MenuActions.CraftingScreen, pauseGame: true));
                    break;
                case MenuActions.PlayerHubContext:
                    router.PushScreen(new ScreenRoute(MenuActions.ContextHubScreen, pauseGame: true));
                    break;
                case MenuActions.PlayerHubStatus:
                    router.PushScreen(new ScreenRoute(MenuActions.StatusHubScreen, pauseGame: true));
                    break;
                case MenuActions.PlayerHubRecipePin:
                    router.PushScreen(new ScreenRoute(MenuActions.RecipePinOverlay, pauseGame: true));
                    break;
                case MenuActions.PlayerHubClose:
                case MenuActions.InventoryBack:
                case MenuActions.VitalsBack:
                case MenuActions.CraftingBack:
                case MenuActions.ContextHubBack:
                case MenuActions.StatusHubBack:
                case MenuActions.ContainerBack:
                case MenuActions.MapBack:
                case MenuActions.FarmingBack:
                case MenuActions.ItemDetailsBack:
                case MenuActions.RecipePinBack:
                case MenuActions.BlockCatalogBack:
                case MenuActions.AvatarStatusBack:
                case MenuActions.MetaPolicyStatusBack:
                case MenuActions.DiagnosticsBack:
                case MenuActions.NetworkCommandStatusBack:
                case MenuActions.SurvivalRejectionDismiss:
                    router.PopScreen();
                    break;
                case MenuActions.InventoryItemDetails:
                    router.PushScreen(new ScreenRoute(MenuActions.ItemDetailsPopover, pauseGame: true));
                    break;
                case MenuActions.CraftingCraftSelected:
                    ResolveRuntimeReferences();
                    if (uiToolkitSelectedCraftingRecipe >= 0)
                        survivalHudController?.CraftingPanel?.TryCraftAtIndex(uiToolkitSelectedCraftingRecipe);
                    ApplyRouterState();
                    break;
                case MenuActions.CraftingPinSelected:
                    if (uiToolkitSelectedCraftingRecipe >= 0)
                        uiToolkitPinnedCraftingRecipe = uiToolkitSelectedCraftingRecipe;
                    ApplyRouterState();
                    break;
                case MenuActions.CraftingRepair:
                    ResolveRuntimeReferences();
                    survivalHudController?.CraftingPanel?.TryRepairHeldTool();
                    ApplyRouterState();
                    break;
                case MenuActions.RecipePinClear:
                    uiToolkitPinnedCraftingRecipe = -1;
                    ApplyRouterState();
                    break;
                case MenuActions.ContextHubContainer:
                    uiToolkitContainerFirstSlot = 0;
                    router.PushScreen(new ScreenRoute(MenuActions.ContainerScreen, pauseGame: true));
                    break;
                case MenuActions.ContextHubStation:
                    router.PushScreen(new ScreenRoute(MenuActions.StationMenuScreen, pauseGame: true));
                    break;
                case MenuActions.ContextHubFarming:
                    router.PushScreen(new ScreenRoute(MenuActions.FarmingSummaryScreen, pauseGame: true));
                    break;
                case MenuActions.FarmingOpenActions:
                    router.PushScreen(new ScreenRoute(MenuActions.FarmingActionPopup, pauseGame: true));
                    break;
                case MenuActions.ContextHubMap:
                    router.PushScreen(new ScreenRoute(MenuActions.MapWayflagScreen, pauseGame: true));
                    break;
                case MenuActions.ContextHubCreativeTools:
                    router.PushScreen(new ScreenRoute(MenuActions.CreativeToolsScreen));
                    break;
                case MenuActions.StatusHubVitals:
                    router.PushScreen(new ScreenRoute(MenuActions.VitalsStatusScreen, pauseGame: true));
                    break;
                case MenuActions.StatusHubAvatar:
                    router.PushScreen(new ScreenRoute(MenuActions.AvatarStatusScreen, pauseGame: true));
                    break;
                case MenuActions.StatusHubMetaPolicy:
                    router.PushScreen(new ScreenRoute(MenuActions.MetaPolicyStatusScreen, pauseGame: true));
                    break;
                case MenuActions.StatusHubDiagnostics:
                    router.PushScreen(new ScreenRoute(MenuActions.DiagnosticsScreen, pauseGame: true));
                    break;
                case MenuActions.StatusHubNetwork:
                    router.PushScreen(new ScreenRoute(MenuActions.NetworkCommandStatusScreen, pauseGame: true));
                    break;
                case MenuActions.ContainerDepositHeld:
                    ResolveRuntimeReferences();
                    survivalHudController?.CratePanel?.DepositHeld();
                    ApplyRouterState();
                    break;
                case MenuActions.StationDepositInput:
                    stationPanel?.DepositHeldInput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationDepositFuel:
                    stationPanel?.DepositHeldFuel();
                    ApplyRouterState();
                    break;
                case MenuActions.StationCollectOutput:
                    stationPanel?.CollectOutput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationWithdrawInput:
                    stationPanel?.WithdrawInput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationWithdrawFuel:
                    stationPanel?.WithdrawFuel();
                    ApplyRouterState();
                    break;
                case MenuActions.StationBack:
                    HandleStationCloseRequested();
                    break;
                case MenuActions.MapSetWayflag:
                case MenuActions.MapClearWayflag:
                case MenuActions.FarmingTill:
                case MenuActions.FarmingPlant:
                case MenuActions.FarmingHarvest:
                    ShowError("Use normal world interaction for this action in the current build.");
                    break;
                case MenuActions.BlockCatalogPreviousCategory:
                    CycleUiToolkitCatalogCategory(forward: false);
                    uiToolkitCatalogPageIndex = 0;
                    ApplyRouterState();
                    break;
                case MenuActions.BlockCatalogNextCategory:
                    CycleUiToolkitCatalogCategory(forward: true);
                    uiToolkitCatalogPageIndex = 0;
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSetA:
                    creativeToolsPanel?.SetCornerA();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSetB:
                    creativeToolsPanel?.SetCornerB();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsPickBlock:
                    creativeToolsPanel?.PickBlock();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsFill:
                    creativeToolsPanel?.FillRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsReplace:
                    creativeToolsPanel?.ReplaceRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsDelete:
                    creativeToolsPanel?.DeleteRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsCopy:
                    creativeToolsPanel?.CopyRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsPaste:
                    creativeToolsPanel?.PasteRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsUndo:
                    creativeToolsPanel?.UndoEdit();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsRedo:
                    creativeToolsPanel?.RedoEdit();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSpawnTree:
                    creativeToolsPanel?.SpawnTree();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSpawnRuin:
                    creativeToolsPanel?.SpawnRuin();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsToggleCycle:
                    creativeToolsPanel?.ToggleDayNightCycle();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsCycleWeather:
                    creativeToolsPanel?.CycleWeather();
                    ApplyRouterState();
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

                case MenuActions.ControllerMappingClose:
                    CloseControllerMappingScreen();
                    break;

                case MenuActions.NewWorldCreate:
                    // Routed to gameplay by the session coordinator (EnterGameplay) only after
                    // generation succeeds.
                    NewWorldConfig pendingConfig = PendingNewWorldConfig;
                    string newWorldError = string.Empty;
                    if (pendingConfig != null && pendingConfig.IsValid(out newWorldError))
                    {
                        uiToolkitNewWorldStatus = string.Empty;
                        ActionRequested?.Invoke(actionId);
                    }
                    else
                    {
                        uiToolkitNewWorldStatus = newWorldError ?? string.Empty;
                        ApplyRouterState();
                    }
                    break;
                case MenuActions.NewWorldCancel:
                    router.PopScreen();
                    break;

                case MenuActions.LoadWorldLoad:
                    if (PendingLoadSave.HasValue)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.LoadWorldCancel:
                    router.PopScreen();
                    break;
                case MenuActions.LoadWorldDetails:
                    WorldSaveSummary? selectedSave = PendingLoadSave;
                    if (selectedSave.HasValue)
                    {
                        ShowWorldDetails(selectedSave.Value);
                        router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
                    }
                    break;

                // ── World Details (§6.5): file operations live in the session controller ──
                case MenuActions.WorldDetailsPlay:
                case MenuActions.WorldDetailsRename:
                case MenuActions.WorldDetailsDuplicate:
                    if (PendingDetailsSave.HasValue)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.WorldDetailsDeleteRequested:
                    WorldSaveSummary? detailsSave = PendingDetailsSave;
                    if (detailsSave.HasValue)
                    {
                        string worldName = detailsSave.Value.Name;
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

            bool uiToolkitVisible = TryShowUiToolkitSurface(activeId, inputTarget);

            foreach (var (screenId, presenter) in screenPresenters)
            {
                bool isModal = screenId == MenuActions.ConfirmModal;
                bool visible = isModal
                    ? router.HasModal && inputTarget == screenId
                    : screenId == activeId;
                if (visible &&
                    uiToolkitVisible &&
                    BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(screenId))
                    visible = false;

                if (string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal))
                    presenter.GetComponent<BlockiverseStartupOverlay>()?.SetAutomaticHide(!visible);

                if (visible)
                    presenter.Show();
                else if (presenter.IsVisible)
                    presenter.Hide();

                bool acceptsInput = visible &&
                    !string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal) &&
                    string.Equals(screenId, inputTarget, StringComparison.Ordinal);
                SetPresenterInputEnabled(presenter, acceptsInput);
            }

            if (!CanUseQuickBlockMenu())
                HideQuickBlockMenu();
            else
                SetPresenterInputEnabled(blockMenuPresenter, blockMenuPresenter != null && blockMenuPresenter.IsVisible);

            if (!uiToolkitVisible)
            {
                uiToolkitMenuSurface?.Hide();
                uiToolkitMenuSurface?.GetComponent<BlockiverseWorldSpacePanelPresenter>()?.Hide();
            }
        }

        bool TryShowUiToolkitSurface(string activeId, string inputTarget)
        {
            if (!useUiToolkitRuntimeMenus || uiToolkitMenuSurface == null)
                return false;

            string candidate = router.HasModal ? inputTarget : activeId;
            if (!BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(candidate))
                return false;

            bool acceptsInput = !string.Equals(candidate, MenuActions.WorldLoadingScreen, StringComparison.Ordinal) &&
                string.Equals(candidate, inputTarget, StringComparison.Ordinal);
            uiToolkitMenuSurface.GetComponent<BlockiverseWorldSpacePanelPresenter>()?.Show();
            uiToolkitMenuSurface.Show(CreateUiToolkitMenuView(candidate), acceptsInput);
            return true;
        }

        bool CanReadUiToolkitPendingState(string screenId)
        {
            if (!useUiToolkitRuntimeMenus || uiToolkitMenuSurface == null || !uiToolkitMenuSurface.IsVisible)
                return false;

            return router != null && string.Equals(router.ActiveScreen.ScreenId, screenId, StringComparison.Ordinal);
        }

        BlockiverseUiToolkitMenuView CreateUiToolkitMenuView(string screenId)
        {
            if (string.Equals(screenId, MenuActions.NewWorldScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateNewWorldView(
                    EnsureUiToolkitNewWorldConfig(),
                    uiToolkitNewWorldStatus);
            }

            if (string.Equals(screenId, MenuActions.LoadWorldScreen, StringComparison.Ordinal))
            {
                int pageCount = UiToolkitLoadWorldPageCount();
                ClampUiToolkitLoadWorldPage(pageCount);
                return BlockiverseUiToolkitMenuCatalog.CreateLoadWorldView(
                    UiToolkitLoadWorldPage(),
                    uiToolkitSaveList.SelectedSave,
                    uiToolkitLoadWorldPageIndex,
                    pageCount,
                    uiToolkitLoadWorldStatus);
            }

            if (string.Equals(screenId, MenuActions.WorldDetailsScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateWorldDetailsView(
                    PendingDetailsSave,
                    PendingDetailsRenameText);
            }

            if (string.Equals(screenId, MenuActions.ComfortSettingsScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateComfortView(comfortSettings);
            }

            if (string.Equals(screenId, MenuActions.AudioSettingsScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateAudioView(feedbackSettings);
            }

            if (string.Equals(screenId, MenuActions.ControlsScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateControlsView();

            if (string.Equals(screenId, MenuActions.LanMultiplayerScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                lanMultiplayerMenu?.RefreshStatus();
                bool canStart = lanMultiplayerMenu?.HostButton == null || lanMultiplayerMenu.HostButton.interactable;
                bool canStop = lanMultiplayerMenu?.StopButton != null && lanMultiplayerMenu.StopButton.interactable;
                string status = lanMultiplayerMenu?.StatusText != null ? lanMultiplayerMenu.StatusText.text : string.Empty;
                return BlockiverseUiToolkitMenuCatalog.CreateLanView(uiToolkitLanAddress, status, canStart, canStop);
            }

            if (string.Equals(screenId, MenuActions.PlayerHubScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreatePlayerHubView();

            if (string.Equals(screenId, MenuActions.ContextHubScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateContextHubView(
                    survivalHudController?.CratePanel != null,
                    stationPanel != null && stationPanel.IsOpen,
                    survivalSync != null && survivalSync.CanUseCreativeMode);
            }

            if (string.Equals(screenId, MenuActions.StatusHubScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStatusHubView();

            if (string.Equals(screenId, MenuActions.InventoryScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                SurvivalInventoryPanel inventoryPanel = survivalHudController != null
                    ? survivalHudController.InventoryPanel
                    : null;
                int selectedHotbar = inventoryPanel != null ? inventoryPanel.SelectedHotbarSlotIndex : 0;
                return BlockiverseUiToolkitMenuCatalog.CreateInventoryView(
                    survivalHudController?.Inventory,
                    ItemRegistry.Default,
                    selectedHotbar,
                    uiToolkitInventoryFirstSlot,
                    UiToolkitLoadWorldMaxEntries);
            }

            if (string.Equals(screenId, MenuActions.ItemDetailsPopover, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                Inventory inventory = survivalHudController?.Inventory;
                ItemStack stack = inventory != null &&
                    uiToolkitSelectedInventorySlot >= 0 &&
                    uiToolkitSelectedInventorySlot < inventory.SlotCount
                        ? inventory.GetSlot(uiToolkitSelectedInventorySlot)
                        : ItemStack.Empty;
                return BlockiverseUiToolkitMenuCatalog.CreateItemDetailsView(stack, ItemRegistry.Default);
            }

            if (string.Equals(screenId, MenuActions.VitalsStatusScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateVitalsView(
                    survivalHudController?.Vitals ?? vitalsRuntime?.Vitals,
                    survivalHudController?.VitalsRuntime?.SurvivalVitals ?? vitalsRuntime?.SurvivalVitals,
                    survivalHudController?.CurrentStatusText ?? string.Empty);
            }

            if (string.Equals(screenId, MenuActions.CraftingScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateCraftingView(
                    survivalHudController?.RecipeBook,
                    survivalHudController?.Inventory,
                    ItemRegistry.Default,
                    uiToolkitCraftingPage,
                    UiToolkitLoadWorldMaxEntries,
                    uiToolkitSelectedCraftingRecipe,
                    uiToolkitPinnedCraftingRecipe);
            }

            if (string.Equals(screenId, MenuActions.RecipePinOverlay, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                BlockiverseUiToolkitMenuCatalog.TryGetInstantRecipe(
                    survivalHudController?.RecipeBook,
                    uiToolkitPinnedCraftingRecipe,
                    out CraftingRecipe recipe);
                return BlockiverseUiToolkitMenuCatalog.CreateRecipePinView(
                    recipe,
                    survivalHudController?.Inventory,
                    ItemRegistry.Default);
            }

            if (string.Equals(screenId, MenuActions.ContainerScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateContainerView(
                    survivalSync?.SharedCrateInventory,
                    ItemRegistry.Default,
                    uiToolkitContainerFirstSlot,
                    UiToolkitLoadWorldMaxEntries);
            }

            if (string.Equals(screenId, MenuActions.StationMenuScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                stationPanel?.ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateStationView(
                    stationPanel?.CurrentStation,
                    stationPanel?.CurrentStatusText ?? string.Empty,
                    ItemRegistry.Default);
            }

            if (string.Equals(screenId, MenuActions.CampfireStationScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStationPlaceholderView(screenId, CraftingStation.Campfire);

            if (string.Equals(screenId, MenuActions.ClayKilnStationScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStationPlaceholderView(screenId, CraftingStation.ClayKiln);

            if (string.Equals(screenId, MenuActions.BellowsForgeStationScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStationPlaceholderView(screenId, CraftingStation.BellowsForge);

            if (string.Equals(screenId, MenuActions.PrepBoardStationScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStationPlaceholderView(screenId, CraftingStation.PrepBoard);

            if (string.Equals(screenId, MenuActions.MendBenchStationScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStationPlaceholderView(screenId, CraftingStation.MendBench);

            if (string.Equals(screenId, MenuActions.BlockCatalogScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateBlockCatalogView(
                    CreativeCatalog.CreateDefault(),
                    BlockRegistry.Default,
                    creativeHotbar,
                    uiToolkitCatalogCategoryIndex,
                    uiToolkitCatalogPageIndex,
                    UiToolkitLoadWorldMaxEntries);
            }

            if (string.Equals(screenId, MenuActions.CreativeToolsScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateCreativeToolsView(creativeToolsPanel, creativeHotbar);
            }

            if (string.Equals(screenId, MenuActions.MapWayflagScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateMapWayflagView();

            if (string.Equals(screenId, MenuActions.FarmingSummaryScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateFarmingSummaryView();

            if (string.Equals(screenId, MenuActions.FarmingActionPopup, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateFarmingActionView();

            if (string.Equals(screenId, MenuActions.AvatarStatusScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateSimpleStatusView(
                    screenId,
                    "Avatar Status",
                    "Shows local and remote avatar session state when platform avatar services are available.",
                    MenuActions.AvatarStatusBack);
            }

            if (string.Equals(screenId, MenuActions.MetaPolicyStatusScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateSimpleStatusView(
                    screenId,
                    "Meta Policy Status",
                    "Shows platform, social, and avatar policy decisions when Meta platform checks are available.",
                    MenuActions.MetaPolicyStatusBack);
            }

            if (string.Equals(screenId, MenuActions.DiagnosticsScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateDiagnosticsView(survivalSync);
            }

            if (string.Equals(screenId, MenuActions.NetworkCommandStatusScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateSimpleStatusView(
                    screenId,
                    "Network Command Status",
                    "Shows accepted, duplicate, pending, and rejected survival/network command feedback.",
                    MenuActions.NetworkCommandStatusBack);
            }

            if (string.Equals(screenId, MenuActions.SurvivalRejectionScreen, StringComparison.Ordinal))
            {
                return BlockiverseUiToolkitMenuCatalog.CreateSimpleStatusView(
                    screenId,
                    "Survival Rejection",
                    "Explains a rejected survival command and links back to the relevant gameplay route.",
                    MenuActions.SurvivalRejectionDismiss);
            }

            bool canToggleMode = survivalSync != null && survivalSync.CanToggleMode;
            bool canOpenCreativeTools = survivalSync != null && survivalSync.CanUseCreativeMode;
            return BlockiverseUiToolkitMenuCatalog.CreateRuntimeView(
                screenId,
                hasLatestSave,
                hasAnySave,
                CanQuit(),
                canToggleMode,
                canOpenCreativeTools,
                deathHasBedrollSpawn,
                titleStatus,
                pauseStatus,
                confirmPrompt,
                confirmActions);
        }

        NewWorldConfig EnsureUiToolkitNewWorldConfig()
        {
            if (uiToolkitNewWorldConfig == null)
            {
                uiToolkitNewWorldConfig = new NewWorldConfig();
                uiToolkitNewWorldConfig.SetName(NewWorldConfig.DefaultName);
                uiToolkitNewWorldConfig.RandomizeSeed(null);
            }

            return uiToolkitNewWorldConfig;
        }

        IReadOnlyList<WorldSaveSummary> UiToolkitLoadWorldPage()
        {
            IReadOnlyList<WorldSaveSummary> visible = uiToolkitSaveList.VisibleSaves;
            int pageCount = UiToolkitLoadWorldPageCount(visible.Count);
            ClampUiToolkitLoadWorldPage(pageCount);
            return visible
                .Skip(uiToolkitLoadWorldPageIndex * UiToolkitLoadWorldMaxEntries)
                .Take(UiToolkitLoadWorldMaxEntries)
                .ToArray();
        }

        int UiToolkitLoadWorldPageCount() => UiToolkitLoadWorldPageCount(uiToolkitSaveList.VisibleSaves.Count);

        static int UiToolkitLoadWorldPageCount(int visibleCount) =>
            Mathf.Max(1, Mathf.CeilToInt(visibleCount / (float)UiToolkitLoadWorldMaxEntries));

        void ClampUiToolkitLoadWorldPage(int pageCount)
        {
            uiToolkitLoadWorldPageIndex = Mathf.Clamp(uiToolkitLoadWorldPageIndex, 0, Mathf.Max(0, pageCount - 1));
        }

        void SelectFirstUiToolkitSaveOnCurrentPage()
        {
            IReadOnlyList<WorldSaveSummary> page = UiToolkitLoadWorldPage();
            if (page.Count > 0)
                uiToolkitSaveList.Select(page[0].Name);
        }

        bool CanUseQuickBlockMenu()
        {
            return router != null &&
                !router.HasModal &&
                string.Equals(router.ActiveScreen.ScreenId, MenuActions.GameplayHudScreen, StringComparison.Ordinal);
        }

        void HideQuickBlockMenu()
        {
            if (blockMenuPresenter == null)
                return;

            if (blockMenuPresenter.IsVisible)
                blockMenuPresenter.Hide();

            SetPresenterInputEnabled(blockMenuPresenter, false);
        }

        void OnControllerMappingCloseFallbackPressed()
        {
            if (router == null ||
                router.HasModal ||
                !string.Equals(router.ActiveScreen.ScreenId, MenuActions.ControllerMappingScreen, StringComparison.Ordinal))
            {
                return;
            }

            if (controllerMappingCloseButton == null)
                ResolveRuntimeReferences();

            RectTransform closeRect = controllerMappingCloseButton != null
                ? controllerMappingCloseButton.GetComponent<RectTransform>()
                : null;
            if (closeRect == null ||
                inputRig == null ||
                !AnyInteractionRayIntersectsRect(closeRect))
            {
                return;
            }

            CloseControllerMappingScreen();
        }

        bool AnyInteractionRayIntersectsRect(RectTransform target)
        {
            if (inputRig.TryGetActiveInteractionRayPose(out Vector3 rayOrigin, out Vector3 rayDirection) &&
                RayIntersectsRect(rayOrigin, rayDirection, target))
            {
                return true;
            }

            return TryControllerRayIntersectsRect(BlockiverseControllerRole.Left, target) ||
                   TryControllerRayIntersectsRect(BlockiverseControllerRole.Right, target);
        }

        bool TryControllerRayIntersectsRect(BlockiverseControllerRole hand, RectTransform target)
        {
            return inputRig.TryGetInteractionRayPose(hand, out Vector3 rayOrigin, out Vector3 rayDirection) &&
                   RayIntersectsRect(rayOrigin, rayDirection, target);
        }

        static bool RayIntersectsRect(Vector3 rayOrigin, Vector3 rayDirection, RectTransform target)
        {
            if (rayDirection.sqrMagnitude <= Mathf.Epsilon || target == null)
                return false;

            var ray = new Ray(rayOrigin, rayDirection.normalized);
            var plane = new Plane(target.forward, target.position);
            if (!plane.Raycast(ray, out float distance) ||
                distance < 0.0f ||
                distance > ControllerMappingCloseFallbackMaxDistanceMeters)
            {
                return false;
            }

            Vector3 localHit = target.InverseTransformPoint(ray.GetPoint(distance));
            Rect rect = target.rect;
            return localHit.x >= rect.xMin &&
                   localHit.x <= rect.xMax &&
                   localHit.y >= rect.yMin &&
                   localHit.y <= rect.yMax;
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
            WireUiToolkitMenuSurface();
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
            UnwireUiToolkitMenuSurface();
            if (titleMenu != null) titleMenu.ActionInvoked -= HandleAction;
            if (pauseMenu != null) pauseMenu.ActionInvoked -= HandleAction;
            if (deathMenu != null) deathMenu.ActionInvoked -= HandleAction;
            if (confirmMenu != null) confirmMenu.ActionInvoked -= HandleAction;
            if (settingsMenu != null) settingsMenu.ActionInvoked -= HandleAction;
            if (worldDetailsMenu != null) worldDetailsMenu.ActionInvoked -= HandleAction;
            if (newWorldPanel != null) newWorldPanel.ActionRequested -= HandleAction;
            if (loadWorldPanel != null) loadWorldPanel.ActionRequested -= HandleAction;
        }

        void WireUiToolkitMenuSurface()
        {
            if (uiToolkitMenuSurface == null || wiredUiToolkitMenuSurface == uiToolkitMenuSurface)
                return;

            UnwireUiToolkitMenuSurface();
            uiToolkitMenuSurface.ActionInvoked += HandleAction;
            uiToolkitMenuSurface.TextInputChanged += HandleUiToolkitTextInputChanged;
            uiToolkitMenuSurface.CycleInvoked += HandleUiToolkitCycleInvoked;
            uiToolkitMenuSurface.SelectionInvoked += HandleUiToolkitSelectionInvoked;
            uiToolkitMenuSurface.ToggleChanged += HandleUiToolkitToggleChanged;
            uiToolkitMenuSurface.SliderChanged += HandleUiToolkitSliderChanged;
            uiToolkitMenuSurface.PageInvoked += HandleUiToolkitPageInvoked;
            wiredUiToolkitMenuSurface = uiToolkitMenuSurface;
        }

        void UnwireUiToolkitMenuSurface()
        {
            if (wiredUiToolkitMenuSurface == null)
                return;

            wiredUiToolkitMenuSurface.ActionInvoked -= HandleAction;
            wiredUiToolkitMenuSurface.TextInputChanged -= HandleUiToolkitTextInputChanged;
            wiredUiToolkitMenuSurface.CycleInvoked -= HandleUiToolkitCycleInvoked;
            wiredUiToolkitMenuSurface.SelectionInvoked -= HandleUiToolkitSelectionInvoked;
            wiredUiToolkitMenuSurface.ToggleChanged -= HandleUiToolkitToggleChanged;
            wiredUiToolkitMenuSurface.SliderChanged -= HandleUiToolkitSliderChanged;
            wiredUiToolkitMenuSurface.PageInvoked -= HandleUiToolkitPageInvoked;
            wiredUiToolkitMenuSurface = null;
        }

        void HandleUiToolkitTextInputChanged(string fieldId, string value)
        {
            if (string.Equals(fieldId, BlockiverseUiToolkitMenuCatalog.NewWorldNameField, StringComparison.Ordinal))
            {
                EnsureUiToolkitNewWorldConfig().SetName(value);
                uiToolkitNewWorldStatus = string.Empty;
                return;
            }

            if (string.Equals(fieldId, BlockiverseUiToolkitMenuCatalog.NewWorldSeedField, StringComparison.Ordinal))
            {
                EnsureUiToolkitNewWorldConfig().SetSeed(value);
                uiToolkitNewWorldStatus = string.Empty;
                return;
            }

            if (string.Equals(fieldId, BlockiverseUiToolkitMenuCatalog.WorldDetailsRenameField, StringComparison.Ordinal))
            {
                uiToolkitDetailsRenameText = value ?? string.Empty;
                return;
            }

            if (string.Equals(fieldId, BlockiverseUiToolkitMenuCatalog.LanAddressField, StringComparison.Ordinal))
            {
                uiToolkitLanAddress = string.IsNullOrWhiteSpace(value)
                    ? BlockiverseNetworkConfig.DefaultAddress
                    : value.Trim();
            }
        }

        void HandleUiToolkitCycleInvoked(string fieldId, bool forward)
        {
            NewWorldConfig config = EnsureUiToolkitNewWorldConfig();
            switch (fieldId)
            {
                case BlockiverseUiToolkitMenuCatalog.NewWorldGameModeField:
                    config.CycleGameMode(forward);
                    break;
                case BlockiverseUiToolkitMenuCatalog.NewWorldDifficultyField:
                    config.CycleDifficulty(forward);
                    break;
                case BlockiverseUiToolkitMenuCatalog.NewWorldSizeField:
                    config.CycleWorldSize(forward);
                    break;
                case BlockiverseUiToolkitMenuCatalog.NewWorldPresetField:
                    config.CycleWorldPreset(forward);
                    break;
                case BlockiverseUiToolkitMenuCatalog.NewWorldStartingBiomeField:
                    config.CycleStartingBiome(forward);
                    break;
                case BlockiverseUiToolkitMenuCatalog.NewWorldTextureSetField:
                    config.CycleTextureSet(forward);
                    break;
                default:
                    return;
            }

            uiToolkitNewWorldStatus = string.Empty;
            ApplyRouterState();
        }

        void HandleUiToolkitSelectionInvoked(string valueId)
        {
            if (TryHandleUiToolkitLoadWorldSelection(valueId))
                return;

            if (TryHandleUiToolkitInventorySelection(valueId))
                return;

            if (TryHandleUiToolkitCraftingSelection(valueId))
                return;

            if (TryHandleUiToolkitContainerSelection(valueId))
                return;

            if (TryHandleUiToolkitBlockCatalogSelection(valueId))
                return;

        }

        bool TryHandleUiToolkitLoadWorldSelection(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId) ||
                !valueId.StartsWith(BlockiverseUiToolkitMenuCatalog.LoadWorldSaveSelectionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string saveName = valueId.Substring(BlockiverseUiToolkitMenuCatalog.LoadWorldSaveSelectionPrefix.Length);
            if (uiToolkitSaveList.Select(saveName))
                ApplyRouterState();

            return true;
        }

        bool TryHandleUiToolkitInventorySelection(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId) ||
                !valueId.StartsWith(BlockiverseUiToolkitMenuCatalog.InventorySlotSelectionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            ResolveRuntimeReferences();
            if (!int.TryParse(valueId.Substring(BlockiverseUiToolkitMenuCatalog.InventorySlotSelectionPrefix.Length), out int slotIndex))
                return true;

            Inventory inventory = survivalHudController?.Inventory;
            SurvivalInventoryPanel inventoryPanel = survivalHudController?.InventoryPanel;
            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return true;

            uiToolkitSelectedInventorySlot = slotIndex;
            int selectedHotbar = inventoryPanel != null ? inventoryPanel.SelectedHotbarSlotIndex : 0;
            if (slotIndex < inventory.HotbarSlotCount)
            {
                inventoryPanel?.SetSelectedHotbarSlotIndex(slotIndex);
            }
            else if (inventory.HotbarSlotCount > 0)
            {
                inventory.SwapSlots(selectedHotbar, slotIndex);
                inventoryPanel?.Refresh();
                survivalHudController?.CraftingPanel?.Refresh();
            }

            ApplyRouterState();
            return true;
        }

        bool TryHandleUiToolkitCraftingSelection(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId) ||
                !valueId.StartsWith(BlockiverseUiToolkitMenuCatalog.CraftingRecipeSelectionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            ResolveRuntimeReferences();
            if (int.TryParse(valueId.Substring(BlockiverseUiToolkitMenuCatalog.CraftingRecipeSelectionPrefix.Length), out int recipeIndex))
                uiToolkitSelectedCraftingRecipe = recipeIndex;

            ApplyRouterState();
            return true;
        }

        bool TryHandleUiToolkitContainerSelection(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId) ||
                !valueId.StartsWith(BlockiverseUiToolkitMenuCatalog.ContainerSlotSelectionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            ResolveRuntimeReferences();
            if (int.TryParse(valueId.Substring(BlockiverseUiToolkitMenuCatalog.ContainerSlotSelectionPrefix.Length), out int slotIndex))
                survivalHudController?.CratePanel?.WithdrawSlot(slotIndex);

            ApplyRouterState();
            return true;
        }

        bool TryHandleUiToolkitBlockCatalogSelection(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId) ||
                !valueId.StartsWith(BlockiverseUiToolkitMenuCatalog.BlockCatalogSelectionPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            ResolveRuntimeReferences();
            string idText = valueId.Substring(BlockiverseUiToolkitMenuCatalog.BlockCatalogSelectionPrefix.Length);
            if (int.TryParse(idText, out int blockId))
                creativeHotbar?.SelectBlock(new BlockId(blockId));

            ApplyRouterState();
            return true;
        }

        void HandleUiToolkitToggleChanged(string fieldId, bool value)
        {
            ResolveRuntimeReferences();

            switch (fieldId)
            {
                case BlockiverseUiToolkitMenuCatalog.ComfortUseTeleportField:
                    if (comfortSettings != null)
                        comfortSettings.LocomotionMode = value
                            ? BlockiverseLocomotionMode.Teleport
                            : BlockiverseLocomotionMode.Glide;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortSmoothTurnField:
                    if (comfortSettings != null) comfortSettings.SmoothTurnEnabled = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortSnapTurnAroundField:
                    if (comfortSettings != null) comfortSettings.SnapTurnAroundEnabled = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortVignetteField:
                    if (comfortSettings != null) comfortSettings.VignetteEnabled = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortLeftHandField:
                    if (comfortSettings != null)
                        comfortSettings.DominantHand = value ? BlockiverseControllerRole.Left : BlockiverseControllerRole.Right;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortToggleToMineField:
                    if (comfortSettings != null) comfortSettings.ToggleToMineEnabled = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioMuteAllField:
                    if (feedbackSettings != null) feedbackSettings.MuteAll = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioHapticsField:
                    if (feedbackSettings != null) feedbackSettings.HapticsEnabled = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioReducedFlashField:
                    if (feedbackSettings != null) feedbackSettings.ReducedFlash = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioReducedParticlesField:
                    if (feedbackSettings != null) feedbackSettings.ReducedParticles = value;
                    break;
            }
        }

        void HandleUiToolkitSliderChanged(string fieldId, float value)
        {
            ResolveRuntimeReferences();

            switch (fieldId)
            {
                case BlockiverseUiToolkitMenuCatalog.ComfortSnapTurnDegreesField:
                    if (comfortSettings != null) comfortSettings.SnapTurnDegrees = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortMoveSpeedField:
                    if (comfortSettings != null) comfortSettings.ContinuousMoveSpeed = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortTurnSpeedField:
                    if (comfortSettings != null) comfortSettings.ContinuousTurnSpeed = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortVignetteStrengthField:
                    if (comfortSettings != null) comfortSettings.VignetteStrength = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortEyeHeightField:
                    if (comfortSettings != null)
                    {
                        comfortSettings.StandingEyeHeight = value;
                        heightReset?.ApplyStandingEyeHeight(comfortSettings.StandingEyeHeight);
                    }
                    break;
                case BlockiverseUiToolkitMenuCatalog.ComfortUiScaleField:
                    if (comfortSettings != null) comfortSettings.UiScale = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioMasterVolumeField:
                    if (feedbackSettings != null) feedbackSettings.MasterVolume = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioEffectsVolumeField:
                    if (feedbackSettings != null) feedbackSettings.EffectsVolume = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioUiVolumeField:
                    if (feedbackSettings != null) feedbackSettings.UiVolume = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioWeatherVolumeField:
                    if (feedbackSettings != null) feedbackSettings.WeatherVolume = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioMusicVolumeField:
                    if (feedbackSettings != null) feedbackSettings.MusicVolume = value;
                    break;
                case BlockiverseUiToolkitMenuCatalog.AudioHapticIntensityField:
                    if (feedbackSettings != null) feedbackSettings.HapticIntensity = value;
                    break;
            }
        }

        void HandleUiToolkitPageInvoked(int delta)
        {
            if (router != null && string.Equals(router.ActiveScreen.ScreenId, MenuActions.InventoryScreen, StringComparison.Ordinal))
            {
                int visibleCount = UiToolkitLoadWorldMaxEntries;
                int slotCount = survivalHudController?.Inventory != null ? survivalHudController.Inventory.SlotCount : visibleCount;
                int inventoryPageCount = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)visibleCount));
                int pageIndex = Mathf.Clamp((uiToolkitInventoryFirstSlot / visibleCount) + delta, 0, inventoryPageCount - 1);
                uiToolkitInventoryFirstSlot = pageIndex * visibleCount;
                ApplyRouterState();
                return;
            }

            if (router != null && string.Equals(router.ActiveScreen.ScreenId, MenuActions.CraftingScreen, StringComparison.Ordinal))
            {
                int recipeCount = survivalHudController?.RecipeBook != null
                    ? survivalHudController.RecipeBook.All.Count(recipe => recipe.TimeTicks <= 0)
                    : 0;
                int craftingPageCount = Mathf.Max(1, Mathf.CeilToInt(recipeCount / (float)UiToolkitLoadWorldMaxEntries));
                uiToolkitCraftingPage = Mathf.Clamp(uiToolkitCraftingPage + delta, 0, craftingPageCount - 1);
                ApplyRouterState();
                return;
            }

            if (router != null && string.Equals(router.ActiveScreen.ScreenId, MenuActions.ContainerScreen, StringComparison.Ordinal))
            {
                int visibleCount = UiToolkitLoadWorldMaxEntries;
                int slotCount = survivalSync?.SharedCrateInventory != null ? survivalSync.SharedCrateInventory.SlotCount : visibleCount;
                int containerPageCount = Mathf.Max(1, Mathf.CeilToInt(slotCount / (float)visibleCount));
                int pageIndex = Mathf.Clamp((uiToolkitContainerFirstSlot / visibleCount) + delta, 0, containerPageCount - 1);
                uiToolkitContainerFirstSlot = pageIndex * visibleCount;
                ApplyRouterState();
                return;
            }

            if (router != null && string.Equals(router.ActiveScreen.ScreenId, MenuActions.BlockCatalogScreen, StringComparison.Ordinal))
            {
                int catalogEntryCount = UiToolkitCatalogEntryCount();
                int catalogPageCount = Mathf.Max(1, Mathf.CeilToInt(catalogEntryCount / (float)UiToolkitLoadWorldMaxEntries));
                uiToolkitCatalogPageIndex = Mathf.Clamp(uiToolkitCatalogPageIndex + delta, 0, catalogPageCount - 1);
                ApplyRouterState();
                return;
            }

            int pageCount = UiToolkitLoadWorldPageCount();
            int nextPage = Mathf.Clamp(uiToolkitLoadWorldPageIndex + delta, 0, pageCount - 1);
            if (nextPage == uiToolkitLoadWorldPageIndex)
                return;

            uiToolkitLoadWorldPageIndex = nextPage;
            SelectFirstUiToolkitSaveOnCurrentPage();
            ApplyRouterState();
        }

        int UiToolkitCatalogEntryCount()
        {
            var categories = (CreativeCatalogCategory[])Enum.GetValues(typeof(CreativeCatalogCategory));
            uiToolkitCatalogCategoryIndex = Mathf.Clamp(uiToolkitCatalogCategoryIndex, 0, categories.Length - 1);
            CreativeCatalog catalog = CreativeCatalog.CreateDefault();
            return catalog.InCategory(categories[uiToolkitCatalogCategoryIndex]).Count();
        }

        void CycleUiToolkitCatalogCategory(bool forward)
        {
            var categories = (CreativeCatalogCategory[])Enum.GetValues(typeof(CreativeCatalogCategory));
            if (categories.Length == 0)
            {
                uiToolkitCatalogCategoryIndex = 0;
                return;
            }

            int delta = forward ? 1 : -1;
            uiToolkitCatalogCategoryIndex = (uiToolkitCatalogCategoryIndex + delta + categories.Length) % categories.Length;
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
