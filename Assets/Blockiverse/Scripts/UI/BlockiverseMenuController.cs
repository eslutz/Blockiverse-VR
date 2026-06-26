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

namespace Blockiverse.UI
{
    // Owns the UiScreenRouter and coordinates the UI Toolkit menu surface
    // (voxel_survival_menus §2/§8.1). The menu button cycles between gameplay and the pause screen;
    // all menu transitions are triggered by action ids dispatched from BlockiverseUiToolkitMenuSurface.
    public sealed class BlockiverseMenuController : MonoBehaviour
    {
        const string CreativeToolsMenuStateName = "Creative Tools Menu State";
        const string StationMenuStateName = "Station Menu State";
        const string SurvivalHudName = "Survival HUD";
        const string UiToolkitMenuSurfaceName = "UI Toolkit Menu Surface";
        const int UiToolkitLoadWorldMaxEntries = 6;

        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] BlockiverseUiToolkitMenuSurface uiToolkitMenuSurface;
        BlockiverseUiToolkitMenuSurface wiredUiToolkitMenuSurface;

        [SerializeField] BlockiverseComfortSettings comfortSettings;
        [SerializeField] BlockiverseHeightReset heightReset;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;
        [SerializeField] BlockiverseStationInteractionState stationInteractionState;
        [SerializeField] BlockiverseCreativeToolsInteractionState creativeToolsInteractionState;
        [SerializeField] BlockiverseNetworkSession lanSession;
        [SerializeField] BlockiverseWorldSessionController worldSessionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] SurvivalHudController survivalHudController;
        [SerializeField] BlockiverseHudToolkitSurface hudToolkitSurface;
        [SerializeField] CreativeHotbar creativeHotbar;

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
        string uiToolkitLanStatus = string.Empty;
        BlockiverseConnectionState lastDisplayedLanState = BlockiverseConnectionState.Stopped;
        NetworkSessionMode lastDisplayedLanMode = NetworkSessionMode.Offline;
        string lastDisplayedLanDisconnectReason = string.Empty;
        bool enteredGameplayForCurrentLanSession;
        bool lanSessionEndedRouteRequested;
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
            ApplyRouterState();
        }

        void RefreshPauseMenu()
        {
            if (survivalSync == null)
                ResolveRuntimeReferences();

            ApplyRouterState();
        }

        public void Configure(BlockiverseInputRig rig)
        {
            inputRig = rig;
        }

        public void ConfigureUiToolkitMenuSurface(BlockiverseUiToolkitMenuSurface surface)
        {
            uiToolkitMenuSurface = surface;
            WireUiToolkitMenuSurface();
        }

        public void ConfigureHudToolkitSurface(BlockiverseHudToolkitSurface surface)
        {
            hudToolkitSurface = surface;
            if (survivalHudController != null)
                survivalHudController.Configure(targetHudSurface: hudToolkitSurface);
            creativeHotbar?.ConfigureHudSurface(hudToolkitSurface);
        }

        // Wires the smelting-station state so a "use" on a kiln/forge block opens its UITK menu (§8.4).
        public void ConfigureStationInteractionState(BlockiverseStationInteractionState state)
        {
            stationInteractionState = state;
        }

        public void ConfigureLanSession(
            BlockiverseNetworkSession session,
            BlockiverseWorldSessionController sessionController)
        {
            if (lanSession != session)
            {
                enteredGameplayForCurrentLanSession = false;
                lanSessionEndedRouteRequested = false;
            }

            lanSession = session;
            worldSessionController = sessionController;
            RefreshLanStatus();
        }

        public void ResolveRuntimeReferences()
        {
            if (inputRig == null)
                inputRig = GetComponent<BlockiverseInputRig>()
                    ?? GetComponentInChildren<BlockiverseInputRig>(true)
                    ?? BlockiverseSceneLookup.Find<BlockiverseInputRig>(FindObjectsInactive.Include);

            comfortSettings ??= GetComponent<BlockiverseComfortSettings>()
                ?? BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);
            heightReset ??= GetComponent<BlockiverseHeightReset>()
                ?? BlockiverseSceneLookup.Find<BlockiverseHeightReset>(FindObjectsInactive.Include);
            feedbackSettings ??= GetComponent<BlockiverseFeedbackSettings>()
                ?? BlockiverseSceneLookup.Find<BlockiverseFeedbackSettings>(FindObjectsInactive.Include);
            stationInteractionState ??= FindGeneratedComponent<BlockiverseStationInteractionState>(StationMenuStateName);
            creativeToolsInteractionState ??= FindGeneratedComponent<BlockiverseCreativeToolsInteractionState>(CreativeToolsMenuStateName);
            lanSession ??= BlockiverseSceneLookup.Find<BlockiverseNetworkSession>(FindObjectsInactive.Include);
            worldSessionController ??= BlockiverseSceneLookup.Find<BlockiverseWorldSessionController>(FindObjectsInactive.Include);
            uiToolkitMenuSurface ??= FindGeneratedComponent<BlockiverseUiToolkitMenuSurface>(UiToolkitMenuSurfaceName);
            WireUiToolkitMenuSurface();
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            if (vitalsRuntime == null)
                vitalsRuntime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
            if (survivalHudController == null)
                survivalHudController = FindGeneratedComponent<SurvivalHudController>(SurvivalHudName)
                    ?? BlockiverseSceneLookup.Find<SurvivalHudController>(FindObjectsInactive.Include);
            if (hudToolkitSurface == null)
                hudToolkitSurface = FindGeneratedComponent<BlockiverseHudToolkitSurface>(SurvivalHudName)
                    ?? BlockiverseSceneLookup.Find<BlockiverseHudToolkitSurface>(FindObjectsInactive.Include);
            if (creativeHotbar == null)
                creativeHotbar = BlockiverseSceneLookup.Find<CreativeHotbar>(FindObjectsInactive.Include);
            if (hudToolkitSurface != null)
                ConfigureHudToolkitSurface(hudToolkitSurface);
            creativeHotbar?.EnsureConfigured();

            stationInteractionState?.ResolveRuntimeReferences();
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
            confirmCallback = callback;
            router?.PushModal(MenuActions.ConfirmModal);
        }

        // Feeds the load-world panel when saves are enumerated by the world manager.
        public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
        {
            var saveList = saves != null ? new List<WorldSaveSummary>(saves) : new List<WorldSaveSummary>();
            uiToolkitSaveList.SetSaves(saveList);
            uiToolkitLoadWorldPageIndex = 0;

            ApplyRouterState();
        }

        public void SetLoadWorldStatus(string message)
        {
            uiToolkitLoadWorldStatus = message ?? string.Empty;
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
                return uiToolkitNewWorldConfig;
            }
        }

        // Exposes the selected save so the world manager can read it on LoadWorldLoad.
        public WorldSaveSummary? PendingLoadSave
        {
            get
            {
                return uiToolkitSaveList.SelectedSave;
            }
        }

        // The save shown on the World Details screen (§6.5) and its pending rename text.
        public WorldSaveSummary? PendingDetailsSave
        {
            get
            {
                return uiToolkitDetailsSave;
            }
        }

        public string PendingDetailsRenameText
        {
            get
            {
                return uiToolkitDetailsRenameText;
            }
        }

        // Re-shows the details screen content for an updated save (after rename/duplicate).
        public void ShowWorldDetails(WorldSaveSummary save)
        {
            uiToolkitDetailsSave = save;
            uiToolkitDetailsRenameText = save.Name;

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
            }

            WireMenus();
            WireStationInteractionState();
            WireVitalsRuntime();

            RefreshTitleMenu();
            RefreshPauseMenu();

            ApplyRouterState();
        }

        void Update()
        {
            if (lanSession == null)
                return;

            if (lastDisplayedLanState != lanSession.CurrentState ||
                lastDisplayedLanMode != lanSession.CurrentMode ||
                lastDisplayedLanDisconnectReason != lanSession.LastDisconnectReason)
            {
                RefreshLanStatus();
                ApplyRouterState();
            }
        }

        public void OnQuickMenuPressed()
        {
            if (router == null)
                return;

            if (uiToolkitMenuSurface != null)
            {
                if (string.Equals(router.ActiveScreen.ScreenId, MenuActions.BlockCatalogScreen, StringComparison.Ordinal))
                {
                    router.PopScreen();
                    return;
                }

                if (!router.HasModal &&
                    string.Equals(router.ActiveScreen.ScreenId, MenuActions.GameplayHudScreen, StringComparison.Ordinal))
                {
                    uiToolkitCatalogPageIndex = 0;
                    router.PushScreen(new ScreenRoute(MenuActions.BlockCatalogScreen));
                    return;
                }
            }
        }

        ScreenRoute ResolveInitialRoute()
        {
            return ShouldShowControllerMappingOnStart()
                ? new ScreenRoute(MenuActions.ControllerMappingScreen, pauseGame: true)
                : new ScreenRoute(MenuActions.TitleScreen, pauseGame: true);
        }

        static bool ShouldShowControllerMappingOnStart() =>
            PlayerPrefs.GetInt(BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey, 0) == 0;

        public void CloseControllerMappingScreen()
        {
            PlayerPrefs.SetInt(BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey, 1);
            PlayerPrefs.Save();

            if (router != null && router.ActiveScreen.ScreenId == MenuActions.ControllerMappingScreen)
            {
                router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));
                RefreshTitleMenu();
                return;
            }

        }

        // Close hook for the current LAN multiplayer route.
        public void CloseLanMultiplayerScreen()
        {
            HandleAction(MenuActions.LanMultiplayerClose);
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
            ApplyRouterState();
        }

        // Status line on the pause menu — used by save/autosave flows while gameplay is paused.
        public void SetPauseStatus(string message)
        {
            pauseStatus = message ?? string.Empty;
            ApplyRouterState();
        }

        void OnDestroy()
        {
            if (router != null)
                router.Changed -= ApplyRouterState;
            BlockiverseRuntimeState.Reset();
            inputRig?.MenuPressed.RemoveListener(OnMenuPressed);
            inputRig?.QuickMenuPressed.RemoveListener(OnQuickMenuPressed);
            UnwireMenus();
            UnwireStationInteractionState();
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

            if (stationInteractionState != null && stationInteractionState.IsOpen)
                stationInteractionState.Close();

            ShowDeathScreen(vitalsRuntime != null && vitalsRuntime.HasBedrollSpawn);
        }

        // ── Smelting-station interaction state (§8.4) ───────────────────────────────────────────

        void WireStationInteractionState()
        {
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (survivalSync != null)
            {
                survivalSync.StationOpenRequested += HandleStationOpenRequested;
                survivalSync.StationRemoved += HandleStationRemoved;
            }

            if (stationInteractionState != null)
            {
                stationInteractionState.ConfigureSurvivalSync(survivalSync);
            }
        }

        void UnwireStationInteractionState()
        {
            if (survivalSync != null)
            {
                survivalSync.StationOpenRequested -= HandleStationOpenRequested;
                survivalSync.StationRemoved -= HandleStationRemoved;
            }

        }

        void HandleStationOpenRequested(BlockPosition position, CraftingStation stationType)
        {
            if (router == null || stationInteractionState == null || survivalSync == null)
                return;

            if (router.ActiveScreen.ScreenId != MenuActions.GameplayHudScreen)
                return;

            SmeltingStationModel model = survivalSync.GetOrCreateStationModel(position, stationType);
            stationInteractionState.Open(model, position);
            // Pulls the authoritative state onto remote-client mirrors; a no-op validation on the host.
            survivalSync.TrySubmitStationOpen(position, out _);
            router.PushScreen(new ScreenRoute(MenuActions.StationMenuScreen));
            PlayStationCue(BlockiverseAudioCue.ContainerOpen);
        }

        void HandleStationCloseRequested()
        {
            bool wasOpen = stationInteractionState != null && stationInteractionState.IsOpen;
            stationInteractionState?.Close();

            if (router != null && router.ActiveScreen.ScreenId == MenuActions.StationMenuScreen)
                router.PopScreen();

            if (wasOpen)
                PlayStationCue(BlockiverseAudioCue.ContainerClose);
        }

        void HandleStationRemoved(BlockPosition position)
        {
            if (stationInteractionState == null || !stationInteractionState.IsOpen || stationInteractionState.OpenPosition != position)
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
                    StartUiToolkitLanHost();
                    ApplyRouterState();
                    break;
                case MenuActions.LanMultiplayerJoin:
                    ResolveRuntimeReferences();
                    JoinUiToolkitLanSession();
                    ApplyRouterState();
                    break;
                case MenuActions.LanMultiplayerStop:
                    ResolveRuntimeReferences();
                    StopUiToolkitLanSession();
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
                    uiToolkitInventoryFirstSlot = 0;
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
                        survivalHudController?.TryCraftAtIndex(uiToolkitSelectedCraftingRecipe);
                    ApplyRouterState();
                    break;
                case MenuActions.CraftingPinSelected:
                    if (uiToolkitSelectedCraftingRecipe >= 0)
                        uiToolkitPinnedCraftingRecipe = uiToolkitSelectedCraftingRecipe;
                    ApplyRouterState();
                    break;
                case MenuActions.CraftingRepair:
                    ResolveRuntimeReferences();
                    survivalHudController?.TryRepairHeldTool();
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
                    survivalHudController?.DepositHeldToCrate();
                    ApplyRouterState();
                    break;
                case MenuActions.StationDepositInput:
                    stationInteractionState?.DepositHeldInput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationDepositFuel:
                    stationInteractionState?.DepositHeldFuel();
                    ApplyRouterState();
                    break;
                case MenuActions.StationCollectOutput:
                    stationInteractionState?.CollectOutput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationWithdrawInput:
                    stationInteractionState?.WithdrawInput();
                    ApplyRouterState();
                    break;
                case MenuActions.StationWithdrawFuel:
                    stationInteractionState?.WithdrawFuel();
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
                    creativeToolsInteractionState?.SetCornerA();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSetB:
                    creativeToolsInteractionState?.SetCornerB();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsPickBlock:
                    creativeToolsInteractionState?.PickBlock();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsFill:
                    creativeToolsInteractionState?.FillRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsReplace:
                    creativeToolsInteractionState?.ReplaceRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsDelete:
                    creativeToolsInteractionState?.DeleteRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsCopy:
                    creativeToolsInteractionState?.CopyRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsPaste:
                    creativeToolsInteractionState?.PasteRegion();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsUndo:
                    creativeToolsInteractionState?.UndoEdit();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsRedo:
                    creativeToolsInteractionState?.RedoEdit();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSpawnTree:
                    creativeToolsInteractionState?.SpawnTree();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsSpawnRuin:
                    creativeToolsInteractionState?.SpawnRuin();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsToggleCycle:
                    creativeToolsInteractionState?.ToggleDayNightCycle();
                    ApplyRouterState();
                    break;
                case MenuActions.CreativeToolsCycleWeather:
                    creativeToolsInteractionState?.CycleWeather();
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

            string activeId = router.ActiveScreen.ScreenId;
            string inputTarget = router.InputTarget;
            bool menuInputActive = !string.Equals(inputTarget, MenuActions.GameplayHudScreen, StringComparison.Ordinal);
            BlockiverseRuntimeState.SetRouterState(router.IsGamePaused, router.AllowWorldInput, menuInputActive);
            if (!HasConfirmModalOpen())
                confirmCallback = null;

            bool menuSurfaceVisible = ShowCurrentMenuSurface(activeId, inputTarget);

            bool showGameplayHud = string.Equals(activeId, MenuActions.GameplayHudScreen, StringComparison.Ordinal);
            hudToolkitSurface?.SetVisible(showGameplayHud);

            if (!menuSurfaceVisible)
            {
                uiToolkitMenuSurface?.Hide();
                uiToolkitMenuSurface?.GetComponent<BlockiverseUiToolkitMenuPresenter>()?.Hide();
            }
        }

        bool ShowCurrentMenuSurface(string activeId, string inputTarget)
        {
            string candidate = router.HasModal ? inputTarget : activeId;
            if (!BlockiverseUiToolkitMenuCatalog.SupportsRuntimeMenu(candidate))
                return false;

            if (uiToolkitMenuSurface == null)
            {
                throw new InvalidOperationException(
                    $"UI Toolkit menu surface is required for menu screen '{candidate}'. " +
                    "Configure the generated UI Toolkit surface before routing menu screens.");
            }

            bool acceptsInput = !string.Equals(candidate, MenuActions.WorldLoadingScreen, StringComparison.Ordinal) &&
                string.Equals(candidate, inputTarget, StringComparison.Ordinal);
            uiToolkitMenuSurface.GetComponent<BlockiverseUiToolkitMenuPresenter>()?.Show();
            uiToolkitMenuSurface.Show(CreateUiToolkitMenuView(candidate), acceptsInput);
            return true;
        }

        void StartUiToolkitLanHost()
        {
            if (lanSession == null)
            {
                SetLanStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                return;
            }

            if (!TrySuspendSinglePlayerSessionForLan())
                return;

            bool started = lanSession.StartHost();
            SetLanStatus(started
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStartingHost)
                : BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LanStartHostFailed, DescribeLanSessionState()));
            RefreshLanStatus();
        }

        void JoinUiToolkitLanSession()
        {
            if (lanSession == null)
            {
                SetLanStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                return;
            }

            string address = ResolveUiToolkitLanAddress();
            if (!TrySuspendSinglePlayerSessionForLan())
                return;

            bool started = lanSession.StartClient(address);
            SetLanStatus(started
                ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LanJoining, address, lanSession.Config.Port)
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanJoinFailed,
                    address,
                    lanSession.Config.Port,
                    DescribeLanSessionState()));
            RefreshLanStatus();
        }

        void StopUiToolkitLanSession()
        {
            if (lanSession == null)
            {
                SetLanStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                return;
            }

            bool wasActive = lanSession.NetworkManager.IsListening || lanSession.NetworkManager.ShutdownInProgress;
            lanSession.StopSession();
            SetLanStatus(DescribeLanStopResult(wasActive));
            RefreshLanStatus();
        }

        bool TrySuspendSinglePlayerSessionForLan()
        {
            if (worldSessionController == null)
                worldSessionController = BlockiverseSceneLookup.Find<BlockiverseWorldSessionController>(FindObjectsInactive.Include);

            if (worldSessionController == null)
                return true;

            if (worldSessionController.TrySuspendActiveSessionForMultiplayer(out string failureReason))
                return true;

            SetLanStatus(string.IsNullOrWhiteSpace(failureReason)
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSuspendSinglePlayerFailed)
                : failureReason);
            return false;
        }

        void RefreshLanStatus()
        {
            if (lanSession == null)
            {
                enteredGameplayForCurrentLanSession = false;
                lanSessionEndedRouteRequested = false;
                SetLanStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable));
                return;
            }

            SetLanStatus(DescribeLanSessionState());
            if (!IsShowingLanSessionEndedMessage())
                lanSessionEndedRouteRequested = false;

            TryEnterGameplayForConnectedLanSession();
            EnsureLanSessionEndedMenuAvailable();
            lastDisplayedLanState = lanSession.CurrentState;
            lastDisplayedLanMode = lanSession.CurrentMode;
            lastDisplayedLanDisconnectReason = lanSession.LastDisconnectReason;
        }

        void ComputeLanControlState(out bool canStart, out bool canStop)
        {
            canStart = lanSession != null &&
                !lanSession.NetworkManager.IsListening &&
                !lanSession.NetworkManager.ShutdownInProgress;
            canStop = lanSession != null &&
                (lanSession.NetworkManager.IsListening || lanSession.NetworkManager.ShutdownInProgress);
        }

        string ResolveUiToolkitLanAddress() =>
            string.IsNullOrWhiteSpace(uiToolkitLanAddress)
                ? BlockiverseNetworkConfig.DefaultAddress
                : uiToolkitLanAddress.Trim();

        string DescribeLanSessionState()
        {
            if (lanSession == null)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable);

            return lanSession.CurrentState switch
            {
                BlockiverseConnectionState.StartingHost => BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStartingHost),
                BlockiverseConnectionState.Hosting => lanSession.LastStopRequestSucceeded
                    ? BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanHosting,
                        DescribeLanHostJoinAddresses(),
                        lanSession.Config.Port)
                    : DescribeLanStopResult(wasActive: true),
                BlockiverseConnectionState.StartingClient => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanJoining,
                    ResolveUiToolkitLanAddress(),
                    lanSession.Config.Port),
                BlockiverseConnectionState.ConnectedClient => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanConnected,
                    ResolveUiToolkitLanAddress(),
                    lanSession.Config.Port),
                BlockiverseConnectionState.Disconnecting => DescribeLanStoppingState(),
                BlockiverseConnectionState.Disconnected => DescribeLanDisconnectedState(),
                BlockiverseConnectionState.Failed => DescribeLanFailedState(),
                _ => BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppedWithDefault,
                    BlockiverseNetworkConfig.DefaultAddress),
            };
        }

        string DescribeLanStopResult(bool wasActive)
        {
            if (lanSession == null)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanUnavailable);

            if (!lanSession.LastStopRequestSucceeded)
            {
                return string.IsNullOrWhiteSpace(lanSession.LastDisconnectReason)
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopFailed)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.LanStopFailedWithReason,
                        lanSession.LastDisconnectReason);
            }

            if (lanSession.LastStopForcedAfterPreparationFailure)
            {
                return BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppingWithoutShutdownSave,
                    lanSession.LastDisconnectReason);
            }

            return wasActive
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopping)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopped);
        }

        string DescribeLanStoppingState()
        {
            if (lanSession != null && lanSession.LastStopForcedAfterPreparationFailure)
            {
                return BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanStoppingWithoutShutdownSave,
                    lanSession.LastDisconnectReason);
            }

            return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStopping);
        }

        string DescribeLanDisconnectedState()
        {
            if (!IsShowingLanSessionEndedMessage())
                return DescribeLanUnableToReachHostState();

            string reconnectMessage =
                BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanHostDisconnected,
                    ResolveUiToolkitLanAddress(),
                    lanSession.Config.Port);

            return string.IsNullOrWhiteSpace(lanSession.LastDisconnectReason)
                ? reconnectMessage
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanLastDisconnect,
                    reconnectMessage,
                    lanSession.LastDisconnectReason);
        }

        string DescribeLanUnableToReachHostState()
        {
            string retryMessage =
                BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanUnableToReach,
                    ResolveUiToolkitLanAddress(),
                    lanSession.Config.Port);

            return string.IsNullOrWhiteSpace(lanSession.LastDisconnectReason)
                ? retryMessage
                : BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.LanLastDisconnect,
                    retryMessage,
                    lanSession.LastDisconnectReason);
        }

        string DescribeLanHostJoinAddresses()
        {
            if (lanSession == null)
                return BlockiverseNetworkConfig.DefaultAddress;

            string listenAddress = lanSession.Config.ListenAddress;
            return BlockiverseLanAddressUtility.IsWildcardListenAddress(listenAddress)
                ? BlockiverseLanAddressUtility.DescribeLocalIPv4Addresses(BlockiverseNetworkConfig.DefaultAddress)
                : listenAddress.Trim();
        }

        string DescribeLanFailedState()
        {
            return lanSession == null || string.IsNullOrWhiteSpace(lanSession.LastDisconnectReason)
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanFailed)
                : lanSession.LastDisconnectReason;
        }

        void SetLanStatus(string message)
        {
            uiToolkitLanStatus = message ?? string.Empty;
        }

        bool IsShowingLanSessionEndedMessage() =>
            lanSession != null &&
            lanSession.CurrentState == BlockiverseConnectionState.Disconnected &&
            lanSession.HasConnectedAsClient;

        void TryEnterGameplayForConnectedLanSession()
        {
            if (lanSession == null)
                return;

            if (lanSession.CurrentState != BlockiverseConnectionState.Hosting &&
                lanSession.CurrentState != BlockiverseConnectionState.ConnectedClient)
            {
                enteredGameplayForCurrentLanSession = false;
                return;
            }

            if (enteredGameplayForCurrentLanSession)
                return;

            enteredGameplayForCurrentLanSession = true;
            EnterGameplay();
        }

        void EnsureLanSessionEndedMenuAvailable()
        {
            if (!IsShowingLanSessionEndedMessage() || lanSessionEndedRouteRequested)
                return;

            if (ShowLanMultiplayerScreen())
                lanSessionEndedRouteRequested = true;
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
                RefreshLanStatus();
                ComputeLanControlState(out bool canStart, out bool canStop);
                return BlockiverseUiToolkitMenuCatalog.CreateLanView(uiToolkitLanAddress, uiToolkitLanStatus, canStart, canStop);
            }

            if (string.Equals(screenId, MenuActions.PlayerHubScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreatePlayerHubView();

            if (string.Equals(screenId, MenuActions.ContextHubScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateContextHubView(
                    survivalHudController != null && survivalHudController.HasSharedCrate,
                    stationInteractionState != null && stationInteractionState.IsOpen,
                    survivalSync != null && survivalSync.CanUseCreativeMode);
            }

            if (string.Equals(screenId, MenuActions.StatusHubScreen, StringComparison.Ordinal))
                return BlockiverseUiToolkitMenuCatalog.CreateStatusHubView();

            if (string.Equals(screenId, MenuActions.InventoryScreen, StringComparison.Ordinal))
            {
                ResolveRuntimeReferences();
                int selectedHotbar = survivalHudController != null ? survivalHudController.SelectedHotbarSlotIndex : 0;
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
                stationInteractionState?.ResolveRuntimeReferences();
                return BlockiverseUiToolkitMenuCatalog.CreateStationView(
                    stationInteractionState?.CurrentStation,
                    stationInteractionState?.CurrentStatusText ?? string.Empty,
                    ItemRegistry.Default);
            }

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
                return BlockiverseUiToolkitMenuCatalog.CreateCreativeToolsView(creativeToolsInteractionState, creativeHotbar);
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

        bool HasConfirmModalOpen()
        {
            if (router == null)
                return false;

            foreach (string modalId in router.ModalStack)
                if (string.Equals(modalId, MenuActions.ConfirmModal, StringComparison.Ordinal))
                    return true;

            return false;
        }

        void WireMenus()
        {
            WireUiToolkitMenuSurface();
        }

        void UnwireMenus()
        {
            UnwireUiToolkitMenuSurface();
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
            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return true;

            uiToolkitSelectedInventorySlot = slotIndex;
            survivalHudController?.HandleSlotSelection(slotIndex);

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
                survivalHudController?.WithdrawCrateSlot(slotIndex);

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
