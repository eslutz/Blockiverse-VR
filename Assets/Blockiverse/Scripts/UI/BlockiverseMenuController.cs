using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // High-level menu system controller (§2.1). Coordinates between the stack-based UiScreenRouter
    // and the physical UI panels (presenters). Handles hardware button routing and state sync.
    [RequireComponent(typeof(BlockiverseWorldSessionController))]
    public sealed class BlockiverseMenuController : MonoBehaviour
    {
        public const string TitleMenuName = "Title Menu";
        public const string PauseMenuName = "Pause Menu";
        public const string DeathScreenName = "Death Screen";
        public const string ConfirmDialogName = "Confirm Dialog";
        public const string ErrorDialogName = "Error Dialog";
        public const string NewWorldPanelName = "New World Panel";
        public const string LoadWorldPanelName = "Load World Panel";
        public const string SettingsPanelName = "Settings Panel";
        public const string WorldDetailsPanelName = "World Details Panel";
        public const string AudioSettingsPanelName = "Audio Settings Panel";
        public const string ControlsPanelName = "Controls Panel";
        public const string ComfortSettingsPanelName = "Comfort Settings Menu";
        public const string CreativeToolsPanelName = "Creative Tools Panel";
        public const string InventoryPanelName = "Inventory Panel";
        public const string CraftingPanelName = "Crafting Panel";
        public const string CatalogPanelName = "Catalog Panel";
        public const string CratePanelName = "Crate Panel";
        public const string StationPanelName = "Station Panel";
        public const string LanMultiplayerPanelName = "LAN Multiplayer Panel";
        public const string SurvivalHudName = "Survival HUD";
        public const string StartupLoadingOverlayName = "Startup Loading Overlay";
        public const string ControllerMappingPopupName = "Controller Mapping Popup";
        public const string ComfortScreenSeenPrefKey = "Blockiverse.ComfortScreenSeen";

        const float GameplayHudScale = 0.001f;
        const float ControllerMappingCloseFallbackMaxDistanceMeters = 2.0f;

        [SerializeField] MonoBehaviour serializedInputRig;
        IBlockiverseInputRig inputRig;
        [SerializeField] BlockiverseActionMenu titleMenu;
        [SerializeField] BlockiverseActionMenu pauseMenu;
        [SerializeField] BlockiverseActionMenu deathMenu;
        [SerializeField] BlockiverseActionMenu confirmMenu;
        [SerializeField] BlockiverseActionMenu errorMenu;
        [SerializeField] BlockiverseNewWorldPanel newWorldPanel;
        [SerializeField] BlockiverseLoadWorldPanel loadWorldPanel;
        [SerializeField] BlockiverseActionMenu settingsMenu;
        [SerializeField] BlockiverseWorldDetailsPanel worldDetailsPanel;
        [SerializeField] BlockiverseActionMenu worldDetailsMenu;
        [SerializeField] SurvivalInventoryPanel inventoryPanel;
        [SerializeField] SurvivalCraftingPanel craftingPanel;
        [SerializeField] BlockiverseCatalogBrowserPanel catalogPanel;
        [SerializeField] BlockiverseCreativeToolsPanel creativeToolsPanel;
        [SerializeField] SurvivalCratePanel cratePanel;
        [SerializeField] BlockiverseStationPanel stationPanel;

        [SerializeField, HideInInspector]
        List<(string screenId, BlockiverseWorldSpacePanelPresenter presenter)> screenPresenters = new();

        [SerializeField] BlockiverseWorldSpacePanelPresenter blockMenuPresenter;
        [SerializeField] Button controllerMappingCloseButton;

        UiScreenRouter router;
        SurvivalVitalsRuntime vitalsRuntime;
        MultiplayerSurvivalSync survivalSync;
        Action<bool> confirmCallback;

        bool latestSaveExists;
        bool anySaveExists;
        bool pauseCanToggleMode;
        bool pauseCanOpenCreativeTools;

        public event Action<string> ActionRequested;

        public UiScreenRouter Router => router;
        public NewWorldConfig PendingNewWorldConfig => newWorldPanel?.Config;
        public WorldSaveSummary? PendingLoadSave => loadWorldPanel != null ? loadWorldPanel.SelectedSave : null;
        public WorldSaveSummary? PendingDetailsSave => worldDetailsPanel != null ? worldDetailsPanel.CurrentSave : null;
        public string PendingDetailsRenameText => worldDetailsPanel != null ? worldDetailsPanel.PendingRenameText : string.Empty;

        void Awake()
        {
            EnsureRouterInitialized();
        }

        void Start()
        {
            EnsureRouterInitialized();
            ResolveRuntimeReferences();
            WireMenus();
            RefreshStaticMenus();

            if (vitalsRuntime != null)
                vitalsRuntime.LocalPlayerDied += OnPlayerDeath;

            TryRouteFirstRunControllerMapping();
            TryRouteFirstRunComfortSettings();

            ApplyRouterState();
        }

        void EnsureRouterInitialized()
        {
            if (router == null)
            {
                router = new UiScreenRouter(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                router.Changed += ApplyRouterState;
            }
        }

        // On first launch, the controls/comfort prompt owns the root before the title menu — but
        // only when a controller-mapping presenter is actually present in the rig (voxel_survival
        // first-run flow). Subsequent launches (seen flag set) skip straight to the title.
        void TryRouteFirstRunControllerMapping()
        {
            bool firstRun = PlayerPrefs.GetInt(BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey, 0) == 0;
            if (!firstRun || !HasRegisteredScreen(MenuActions.ControllerMappingScreen))
                return;

            router.ClearToRoot(new ScreenRoute(MenuActions.ControllerMappingScreen, pauseGame: true));
        }

        void TryRouteFirstRunComfortSettings()
        {
            bool firstRun = PlayerPrefs.GetInt(ComfortScreenSeenPrefKey, 0) == 0;
            if (!firstRun || !HasRegisteredScreen(MenuActions.ComfortSettingsScreen))
                return;

            router.PushScreen(new ScreenRoute(MenuActions.ComfortSettingsScreen, pauseGame: true));
        }

        bool HasRegisteredScreen(string screenId)
        {
            if (screenPresenters == null)
                return false;

            foreach (var (id, _) in screenPresenters)
                if (string.Equals(id, screenId, StringComparison.Ordinal))
                    return true;

            return false;
        }

        // Runtime population of the static button-list menus. The bootstrapper authors button
        // labels at edit time, but each BlockiverseActionMenu's action-id mapping is runtime-only,
        // so the controller must (re)apply it on Start. Context-dependent menus (death) are
        // populated when they are routed to.
        void RefreshStaticMenus()
        {
            RefreshTitleMenu();
            RefreshPauseMenu();

            if (settingsMenu != null)
                settingsMenu.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleSettings), MenuActions.Settings);

            if (worldDetailsMenu != null)
                worldDetailsMenu.SetMenu(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleWorldDetails), MenuActions.WorldDetails);
        }

        void RefreshTitleMenu()
        {
            if (titleMenu != null)
                titleMenu.SetMenu(BlockiverseProject.ProductName, MenuActions.Title(latestSaveExists, anySaveExists, CanQuit()));
        }

        void RefreshPauseMenu()
        {
            if (pauseMenu != null)
                pauseMenu.SetMenu(
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused),
                    MenuActions.PauseMenu(pauseCanToggleMode, pauseCanOpenCreativeTools, CanQuit()));
        }

        // Lets the world-session layer reflect the active world's permissions in the pause menu.
        public void ConfigurePauseMenuPermissions(bool canToggleMode, bool canOpenCreativeTools)
        {
            pauseCanToggleMode = canToggleMode;
            pauseCanOpenCreativeTools = canOpenCreativeTools;
            RefreshPauseMenu();
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;

            if (vitalsRuntime != null)
                vitalsRuntime.LocalPlayerDied -= OnPlayerDeath;

            if (survivalSync != null)
                survivalSync.StationRemoved -= OnStationRemoved;

            if (inputRig != null)
            {
                inputRig.MenuPressed.RemoveListener(OnMenuPressed);
                inputRig.BreakPressed.RemoveListener(OnControllerMappingCloseFallbackPressed);
                inputRig.QuickMenuPressed.RemoveListener(OnQuickMenuPressed);
            }

            UnwireMenus();
        }

        public void Configure(
            IBlockiverseInputRig inputRig,
            BlockiverseActionMenu titleMenu,
            BlockiverseActionMenu pauseMenu,
            BlockiverseActionMenu deathMenu,
            BlockiverseActionMenu confirmMenu,
            BlockiverseActionMenu errorMenu,
            BlockiverseNewWorldPanel newWorldPanel,
            BlockiverseLoadWorldPanel loadWorldPanel,
            BlockiverseActionMenu settingsMenu = null,
            BlockiverseWorldDetailsPanel worldDetailsPanel = null,
            BlockiverseActionMenu worldDetailsMenu = null,
            SurvivalInventoryPanel inventoryPanel = null,
            SurvivalCraftingPanel craftingPanel = null,
            BlockiverseCatalogBrowserPanel catalogPanel = null,
            BlockiverseCreativeToolsPanel creativeToolsPanel = null,
            SurvivalCratePanel cratePanel = null)
{
            this.inputRig = inputRig;
            this.titleMenu = titleMenu;
            this.pauseMenu = pauseMenu;
            this.deathMenu = deathMenu;
            this.confirmMenu = confirmMenu;
            this.errorMenu = errorMenu;
            this.newWorldPanel = newWorldPanel;
            this.loadWorldPanel = loadWorldPanel;
            this.settingsMenu = settingsMenu;
            this.worldDetailsPanel = worldDetailsPanel;
            this.worldDetailsMenu = worldDetailsMenu;
            this.inventoryPanel = inventoryPanel;
            this.craftingPanel = craftingPanel;
            this.catalogPanel = catalogPanel;
            this.creativeToolsPanel = creativeToolsPanel;
            this.cratePanel = cratePanel;
        }

        public void ConfigurePresenters(
            BlockiverseWorldSpacePanelPresenter title,
            BlockiverseWorldSpacePanelPresenter pause,
            BlockiverseWorldSpacePanelPresenter death,
            BlockiverseWorldSpacePanelPresenter confirm,
            BlockiverseWorldSpacePanelPresenter error,
            BlockiverseWorldSpacePanelPresenter newWorld,
            BlockiverseWorldSpacePanelPresenter loadWorld,
            BlockiverseWorldSpacePanelPresenter settings,
            BlockiverseWorldSpacePanelPresenter station,
            BlockiverseWorldSpacePanelPresenter lan,
            BlockiverseWorldSpacePanelPresenter audio,
            BlockiverseWorldSpacePanelPresenter controls,
            BlockiverseWorldSpacePanelPresenter worldDetails,
            BlockiverseWorldSpacePanelPresenter creativeTools,
            BlockiverseWorldSpacePanelPresenter gameplayHud,
            BlockiverseWorldSpacePanelPresenter comfort,
            BlockiverseWorldSpacePanelPresenter controllerMapping,
            BlockiverseWorldSpacePanelPresenter worldLoading,
            BlockiverseWorldSpacePanelPresenter inventory,
            BlockiverseWorldSpacePanelPresenter crafting,
            BlockiverseWorldSpacePanelPresenter catalog,
            BlockiverseWorldSpacePanelPresenter crate)
        {
            screenPresenters.Clear();
            AddPresenter(MenuActions.TitleScreen, title);
            AddPresenter(MenuActions.PauseScreen, pause);
            AddPresenter(MenuActions.DeathScreen, death);
            AddPresenter(MenuActions.ConfirmModal, confirm);
            AddPresenter(MenuActions.ErrorModal, error);
            AddPresenter(MenuActions.NewWorldScreen, newWorld);
            AddPresenter(MenuActions.LoadWorldScreen, loadWorld);
            AddPresenter(MenuActions.SettingsScreen, settings);
            AddPresenter(MenuActions.StationMenuScreen, station);
            AddPresenter(MenuActions.LanMultiplayerScreen, lan);
            AddPresenter(MenuActions.AudioSettingsScreen, audio);
            AddPresenter(MenuActions.ControlsScreen, controls);
            AddPresenter(MenuActions.WorldDetailsScreen, worldDetails);
            AddPresenter(MenuActions.CreativeToolsScreen, creativeTools);
            AddPresenter(MenuActions.GameplayHudScreen, gameplayHud);
            AddPresenter(MenuActions.ComfortSettingsScreen, comfort);
            AddPresenter(MenuActions.ControllerMappingScreen, controllerMapping);
            AddPresenter(MenuActions.WorldLoadingScreen, worldLoading);
            AddPresenter(MenuActions.InventoryScreen, inventory);
            AddPresenter(MenuActions.CraftingScreen, crafting);
            AddPresenter(MenuActions.CatalogScreen, catalog);
            AddPresenter(MenuActions.StationCrateScreen, crate);
        }

        public void ConfigureStationPanel(BlockiverseStationPanel panel) => this.stationPanel = panel;

        void ResolveRuntimeReferences()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<IBlockiverseInputRig>();

            if (vitalsRuntime == null)
                vitalsRuntime = GetComponent<SurvivalVitalsRuntime>() ?? 
                                BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

            if (survivalSync == null)
            {
                survivalSync = GetComponentInParent<MultiplayerSurvivalSync>() ??
                               BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
                if (survivalSync != null)
                {
                    survivalSync.StationRemoved -= OnStationRemoved;
                    survivalSync.StationRemoved += OnStationRemoved;
                }
            }

            if (titleMenu == null) titleMenu = FindGeneratedComponent<BlockiverseActionMenu>("Title Menu");
            if (pauseMenu == null) pauseMenu = FindGeneratedComponent<BlockiverseActionMenu>("Pause Menu");
            if (deathMenu == null) deathMenu = FindGeneratedComponent<BlockiverseActionMenu>(DeathScreenName);
            if (confirmMenu == null) confirmMenu = FindGeneratedComponent<BlockiverseActionMenu>(ConfirmDialogName);
            if (errorMenu == null) errorMenu = FindGeneratedComponent<BlockiverseActionMenu>(ErrorDialogName);
            if (newWorldPanel == null) newWorldPanel = FindGeneratedComponent<BlockiverseNewWorldPanel>(NewWorldPanelName);
            if (loadWorldPanel == null) loadWorldPanel = FindGeneratedComponent<BlockiverseLoadWorldPanel>(LoadWorldPanelName);
            if (settingsMenu == null) settingsMenu = FindGeneratedComponent<BlockiverseActionMenu>(SettingsPanelName);
            if (worldDetailsPanel == null) worldDetailsPanel = FindGeneratedComponent<BlockiverseWorldDetailsPanel>(WorldDetailsPanelName);
            if (worldDetailsMenu == null) worldDetailsMenu = FindGeneratedComponent<BlockiverseActionMenu>(WorldDetailsPanelName);
            if (inventoryPanel == null) inventoryPanel = FindGeneratedComponent<SurvivalInventoryPanel>(InventoryPanelName);
            if (craftingPanel == null) craftingPanel = FindGeneratedComponent<SurvivalCraftingPanel>(CraftingPanelName);
            if (catalogPanel == null) catalogPanel = FindGeneratedComponent<BlockiverseCatalogBrowserPanel>(CatalogPanelName);
            if (creativeToolsPanel == null) creativeToolsPanel = FindGeneratedComponent<BlockiverseCreativeToolsPanel>(CreativeToolsPanelName);
            if (cratePanel == null) cratePanel = FindGeneratedComponent<SurvivalCratePanel>(CratePanelName);
            if (stationPanel == null) stationPanel = FindGeneratedComponent<BlockiverseStationPanel>(StationPanelName);

            if (screenPresenters == null || screenPresenters.Count == 0)
            {
                screenPresenters = new List<(string screenId, BlockiverseWorldSpacePanelPresenter presenter)>();
                
                void AddFromFieldOrName<TComponent>(string screenId, string panelName, TComponent field) where TComponent : Component
                {
                    BlockiverseWorldSpacePanelPresenter presenter = null;
                    if (field != null)
                        presenter = field.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                    if (presenter == null)
                        presenter = FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(panelName);
                    if (presenter != null)
                        screenPresenters.Add((screenId, presenter));
                }

                AddFromFieldOrName(MenuActions.TitleScreen, "Title Menu", titleMenu);
                AddFromFieldOrName(MenuActions.PauseScreen, "Pause Menu", pauseMenu);
                AddFromFieldOrName(MenuActions.DeathScreen, "Death Menu", deathMenu);
                AddFromFieldOrName(MenuActions.ConfirmModal, ConfirmDialogName, confirmMenu);
                AddFromFieldOrName(MenuActions.ErrorModal, ErrorDialogName, errorMenu);
                AddFromFieldOrName(MenuActions.NewWorldScreen, NewWorldPanelName, newWorldPanel);
                AddFromFieldOrName(MenuActions.LoadWorldScreen, LoadWorldPanelName, loadWorldPanel);
                AddFromFieldOrName(MenuActions.SettingsScreen, SettingsPanelName, settingsMenu);
                AddFromFieldOrName(MenuActions.StationMenuScreen, StationPanelName, stationPanel);
                AddFromFieldOrName(MenuActions.LanMultiplayerScreen, LanMultiplayerPanelName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(LanMultiplayerPanelName));
                AddFromFieldOrName(MenuActions.AudioSettingsScreen, AudioSettingsPanelName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(AudioSettingsPanelName));
                AddFromFieldOrName(MenuActions.ControlsScreen, ControlsPanelName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ControlsPanelName));
                AddFromFieldOrName(MenuActions.WorldDetailsScreen, WorldDetailsPanelName, worldDetailsPanel);
                AddFromFieldOrName(MenuActions.CreativeToolsScreen, CreativeToolsPanelName, creativeToolsPanel);
                BlockiverseWorldSpacePanelPresenter gameplayHud = EnsureGameplayHudPresenter();
                if (gameplayHud != null)
                    screenPresenters.Add((MenuActions.GameplayHudScreen, gameplayHud));
                AddFromFieldOrName(MenuActions.ComfortSettingsScreen, ComfortSettingsPanelName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ComfortSettingsPanelName));
                AddFromFieldOrName(MenuActions.ControllerMappingScreen, ControllerMappingPopupName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ControllerMappingPopupName));
                AddFromFieldOrName(MenuActions.WorldLoadingScreen, StartupLoadingOverlayName, FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(StartupLoadingOverlayName));
                AddFromFieldOrName(MenuActions.InventoryScreen, InventoryPanelName, inventoryPanel);
                AddFromFieldOrName(MenuActions.CraftingScreen, CraftingPanelName, craftingPanel);
                AddFromFieldOrName(MenuActions.CatalogScreen, CatalogPanelName, catalogPanel);
                AddFromFieldOrName(MenuActions.StationCrateScreen, CratePanelName, cratePanel);
            }

            if (blockMenuPresenter == null)
                blockMenuPresenter = FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>("Block Menu");

            if (controllerMappingCloseButton == null)
            {
                var mappingRoot = FindGeneratedComponent<BlockiverseWorldSpacePanelPresenter>(ControllerMappingPopupName);
                if (mappingRoot != null)
                    controllerMappingCloseButton = mappingRoot.transform.Find("Panel/Close Button")?.GetComponent<Button>();
            }

            if (inputRig != null)
            {
                inputRig.MenuPressed.RemoveListener(OnMenuPressed);
                inputRig.MenuPressed.AddListener(OnMenuPressed);
                inputRig.BreakPressed.RemoveListener(OnControllerMappingCloseFallbackPressed);
                inputRig.BreakPressed.AddListener(OnControllerMappingCloseFallbackPressed);
                inputRig.QuickMenuPressed.RemoveListener(OnQuickMenuPressed);
                inputRig.QuickMenuPressed.AddListener(OnQuickMenuPressed);
            }
        }

        // Closes the station panel (and its routed screen) when the world block backing the open
        // station is removed (broken/picked up) by the local player or a host snapshot.
        void OnStationRemoved(BlockPosition position)
        {
            if (stationPanel == null || !stationPanel.IsOpen)
                return;

            if (!stationPanel.OpenPosition.Equals(position))
                return;

            stationPanel.Close();

            if (IsActiveScreen(MenuActions.StationMenuScreen))
                router.PopScreen();
        }

        public bool IsActiveScreen(string screenId) => router != null && string.Equals(router.ActiveScreen.ScreenId, screenId, StringComparison.Ordinal);

        public void ShowTitleScreen() => router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));

        public void EnterGameplay() => router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));

        public void ShowWorldLoadingScreen() => router.PushScreen(new ScreenRoute(MenuActions.WorldLoadingScreen, pauseGame: true));

        public bool ShowLanMultiplayerScreen()
        {
            router.PushScreen(new ScreenRoute(MenuActions.LanMultiplayerScreen));
            return true;
        }

        public void CloseLanMultiplayerScreen()
        {
            if (IsActiveScreen(MenuActions.LanMultiplayerScreen))
                router.PopScreen();
        }

        public void ShowError(string message, string title = null)
        {
            if (errorMenu != null)
                errorMenu.SetMenu(title ?? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleError), MenuActions.Error());
            
            errorMenu?.SetStatus(message);
            router.PushModal(MenuActions.ErrorModal);
        }

        public void SetLoadWorldStatus(string message) => loadWorldPanel?.SetStatus(message);
        public void SetTitleStatus(string message) => titleMenu?.SetStatus(message);
        public void SetPauseStatus(string message) => pauseMenu?.SetStatus(message);

        void CloseErrorDialog() => router.PopModal();

        public void RequestConfirm(string prompt, string confirmLabel, string cancelLabel, Action<bool> callback)
        {
            confirmCallback = callback;
            if (confirmMenu != null)
                confirmMenu.SetMenu(prompt, MenuActions.Confirm(confirmLabel, cancelLabel));
            
            router.PushModal(MenuActions.ConfirmModal);
        }

        public void SetSaveAvailability(bool latestSaveExists, bool anySaveExists)
        {
            this.latestSaveExists = latestSaveExists;
            this.anySaveExists = anySaveExists;
            RefreshTitleMenu();
        }

        public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
        {
            loadWorldPanel?.SetSaves(saves);
        }

        void OnPlayerDeath()
        {
            if (deathMenu != null)
                deathMenu.SetMenu(
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleDeath),
                    MenuActions.Death(vitalsRuntime != null && vitalsRuntime.HasBedrollSpawn));

            router.ClearToRoot(new ScreenRoute(MenuActions.DeathScreen, pauseGame: true));
        }

        public void OnMenuPressed()
        {
            if (router == null) return;

            string activeId = router.ActiveScreen.ScreenId;
            if (activeId == MenuActions.GameplayHudScreen)
            {
                router.PushScreen(new ScreenRoute(MenuActions.PauseScreen, pauseGame: true));
            }
            else if (activeId == MenuActions.PauseScreen)
            {
                router.PopScreen();
            }
            else if (router.ScreenDepth > 1)
            {
                router.PopScreen();
            }
        }

        void HandleAction(string actionId)
        {
            if (router == null || string.IsNullOrEmpty(actionId)) return;

            switch (actionId)
            {
                case MenuActions.TitleContinue:
                case MenuActions.TitleNewWorld:
                case MenuActions.TitleLoadWorld:
                case MenuActions.TitleMultiplayer:
                case MenuActions.TitleSettings:
                case MenuActions.TitleQuit:
                    HandleTitleAction(actionId);
                    break;

                case MenuActions.PauseResume:
                    router.PopScreen();
                    break;
                case MenuActions.PauseSaveGame:
                case MenuActions.PauseToggleMode:
                case MenuActions.PauseCreativeTools:
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.PauseSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.PauseReturnToTitle:
                    RequestQuitConfirmation(MenuActions.PauseReturnToTitle, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseReturnToTitle));
                    break;
                case MenuActions.PauseQuit:
                    RequestQuitConfirmation(MenuActions.PauseQuit, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.PauseQuit));
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

                case MenuActions.ErrorClose:
                    CloseErrorDialog();
                    break;

                case MenuActions.NewWorldCreate:
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.NewWorldCancel:
                    router.PopScreen();
                    break;

                case MenuActions.LoadWorldLoad:
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.LoadWorldCancel:
                    router.PopScreen();
                    break;
                case MenuActions.LoadWorldDetails:
                    if (loadWorldPanel != null && loadWorldPanel.SelectedSave.HasValue)
                    {
                        worldDetailsPanel?.ShowSave(loadWorldPanel.SelectedSave.Value);
                        router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
                    }
                    break;

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
                    if (string.Equals(actionId, MenuActions.ComfortSettingsClose, StringComparison.Ordinal))
                    {
                        PlayerPrefs.SetInt(ComfortScreenSeenPrefKey, 1);
                        PlayerPrefs.Save();
                    }
                    router.PopScreen();
                    break;
                case MenuActions.AudioSettingsClose:
                
                case MenuActions.CreativeToolsClose:
                    router.PopScreen();
                    break;

                case MenuActions.DeathRespawnBedroll:
                    vitalsRuntime?.RespawnAtBedroll();
                    ActionRequested?.Invoke(actionId);
                    EnterGameplay();
                    break;
                case MenuActions.DeathRespawnWorldSpawn:
                    vitalsRuntime?.Respawn();
                    ActionRequested?.Invoke(actionId);
                    EnterGameplay();
                    break;
                case MenuActions.DeathReturnToTitle:
                    vitalsRuntime?.Respawn();
                    ActionRequested?.Invoke(actionId);
                    ShowTitleScreen();
                    break;
            }
        }

        void HandleTitleAction(string actionId)
        {
            switch (actionId)
            {
                case MenuActions.TitleContinue:
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
                case MenuActions.TitleSettings:
                    router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));
                    break;
                case MenuActions.TitleQuit:
                    RequestQuitConfirmation(MenuActions.TitleQuit, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleQuit));
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
                bool isModal = screenId == MenuActions.ConfirmModal || screenId == MenuActions.ErrorModal;
                bool visible = isModal
                    ? router.HasModal && inputTarget == screenId
                    : screenId == activeId;

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

        void OnQuickMenuPressed()
        {
            if (CanUseQuickBlockMenu() && blockMenuPresenter != null)
            {
                blockMenuPresenter.ToggleVisible();
                ApplyRouterState();
            }
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

        public void CloseControllerMappingScreen()
        {
            if (IsActiveScreen(MenuActions.ControllerMappingScreen))
            {
                PlayerPrefs.SetInt(BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey, 1);
                PlayerPrefs.Save();

                if (router.ScreenDepth == 1)
                {
                    router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                }
                else
                {
                    router.PopScreen();
                }
            }
        }

        public void ShowWorldDetails(WorldSaveSummary save)
        {
            worldDetailsPanel?.ShowSave(save);
            router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
        }

        public void CloseWorldDetails()
        {
            if (IsActiveScreen(MenuActions.WorldDetailsScreen))
                router.PopScreen();
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
            if (titleMenu != null) titleMenu.ActionInvoked += HandleAction;
            if (pauseMenu != null) pauseMenu.ActionInvoked += HandleAction;
            if (deathMenu != null) deathMenu.ActionInvoked += HandleAction;
            if (confirmMenu != null) confirmMenu.ActionInvoked += HandleAction;
            if (errorMenu != null) errorMenu.ActionInvoked += HandleAction;
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
            if (errorMenu != null) errorMenu.ActionInvoked -= HandleAction;
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
        public void CloseComfortSettingsScreen() => HandleAction(MenuActions.ComfortSettingsClose);
        public void CloseCreativeToolsScreen() => HandleAction(MenuActions.CreativeToolsClose);
        public void CloseSettingsScreen() => HandleAction(MenuActions.SettingsClose);
        public void OpenInventoryScreen() => router.PushScreen(new ScreenRoute(MenuActions.InventoryScreen));
        public void OpenCraftingScreen() => router.PushScreen(new ScreenRoute(MenuActions.CraftingScreen));
        public void OpenCatalogScreen() => router.PushScreen(new ScreenRoute(MenuActions.CatalogScreen));
        public void CloseInventoryScreen() => router.PopScreen();
public void CloseCraftingScreen() => router.PopScreen();
        public void CloseCatalogScreen() => router.PopScreen();
        public void CloseStationCrateScreen() => router.PopScreen();

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
