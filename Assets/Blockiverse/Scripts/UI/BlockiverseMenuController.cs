using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    // High-level menu system controller (§2.1). Owns the stack-based UiScreenRouter, hardware
    // button routing, the first-run flows and the domain commands; the UI Toolkit host renders
    // the router's state and answers the pending-state reads.
    [RequireComponent(typeof(BlockiverseWorldSessionController))]
    public sealed class BlockiverseMenuController : MonoBehaviour
    {
        public const string ComfortScreenSeenPrefKey = "Blockiverse.ComfortScreenSeen";

        // Lived on BlockiverseWorldSpacePanelPresenter until the uGUI panels were deleted. The
        // literal is a shipped PlayerPrefs key, so it must not be renamed: a different string
        // re-runs the first-run controller-mapping prompt for everyone who already dismissed it.
        public const string ControllerMappingPopupSeenPrefKey = "Blockiverse.ControllerMappingPopupSeen";

        [SerializeField] MonoBehaviour serializedInputRig;
        IBlockiverseInputRig inputRig;

        UiScreenRouter router;
        BlockiverseWorldSessionController sessionController;
        SurvivalVitalsRuntime vitalsRuntime;
        MultiplayerSurvivalSync survivalSync;
        BlockiverseNetworkSession networkSession;

        // The UI Toolkit host (ADR 0010). This controller keeps the router, action handling,
        // first-run flows and domain commands; every outward state push goes to the host and
        // every pending-state read is answered by it. Null before the host registers in
        // OnEnable, and in EditMode fixtures that never run lifecycle callbacks at all, so the
        // pushes stay null-conditional rather than assuming a host is present.
        IBlockiverseMenuFrontend frontend;

        public bool HasFrontend => frontend != null;

        // A pause route freezes the world clock only when no LAN session is live: a
        // host-authoritative shared world cannot pause for one player's menu.
        public bool IsMultiplayerSessionLive =>
            networkSession != null && networkSession.CurrentMode != NetworkSessionMode.Offline;
        Action<bool> confirmCallback;

        bool latestSaveExists;
        bool anySaveExists;
        bool pauseCanToggleMode;
        bool pauseCanOpenCreativeTools;

        public event Action<string> ActionRequested;

        // Lazy: Awake initializes the router in Play mode, but EditMode fixtures AddComponent
        // without lifecycle callbacks, and every toolkit screen test routes through this
        // property or DispatchAction. EnsureRouterInitialized is idempotent, so the runtime
        // path is unchanged.
        public UiScreenRouter Router
        {
            get
            {
                EnsureRouterInitialized();
                return router;
            }
        }

        // Pending state lives on the host's own screens — those are the fields the player types
        // into — so the session layer reads it back through here.
        public NewWorldConfig PendingNewWorldConfig => frontend?.PendingNewWorldConfig;
        public WorldSaveSummary? PendingLoadSave => frontend?.PendingLoadSave;
        public WorldSaveSummary? PendingDetailsSave => frontend?.PendingDetailsSave;
        public string PendingDetailsRenameText => frontend?.PendingDetailsRenameText ?? string.Empty;

        public void RegisterFrontend(IBlockiverseMenuFrontend menuFrontend)
        {
            if (menuFrontend == null)
                return;

            frontend = menuFrontend;
            EnsureRouterInitialized();

            // The replay is load-bearing, not scaffolding. BlockiverseWorldSessionController.Start
            // pushes the save list and the title pose in the same frame the host registers, and
            // the relative script order of the two is undefined; losing that race would leave the
            // Load World list empty and the title menus unposed for the whole first frame.
            RefreshStaticMenus();
            if (hasTitleMenuPose)
                frontend.SetTitleMenuPose(titleMenuPose);
            if (sessionController == null)
                sessionController = GetComponent<BlockiverseWorldSessionController>();
            sessionController?.RefreshSaveList();

            ApplyRouterState();
        }

        public void UnregisterFrontend(IBlockiverseMenuFrontend menuFrontend)
        {
            if (ReferenceEquals(frontend, menuFrontend))
                frontend = null;
        }

        // Public action entry for the host's screen controllers: every button on every UI
        // Toolkit screen routes its action id through here.
        public void DispatchAction(string actionId)
        {
            EnsureRouterInitialized();
            HandleAction(actionId);
        }

        void Awake()
        {
            EnsureRouterInitialized();
        }

        void Start()
        {
            EnsureRouterInitialized();
            ResolveRuntimeReferences();
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
        // only when the screen is actually present in the rig (voxel_survival first-run flow).
        // Subsequent launches (seen flag set) skip straight to the title.
        void TryRouteFirstRunControllerMapping()
        {
            bool firstRun = PlayerPrefs.GetInt(ControllerMappingPopupSeenPrefKey, 0) == 0;
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

        // Both first-run routes gate on this, so it has to answer from whatever actually renders
        // screens. Ordering is safe: the host discovers its screens in Awake and registers in
        // OnEnable, both of which precede this controller's Start. A frontend that is not the
        // host (EditMode test doubles) reports no screens — the same answer a rig with no
        // generated screens gave before, so the first-run flows stay inert in fixtures.
        bool HasRegisteredScreen(string screenId)
        {
            var host = frontend as UiToolkitMenuHost;
            if (host == null)
                return false;

            foreach (var (id, _) in host.Screens)
                if (string.Equals(id, screenId, StringComparison.Ordinal))
                    return true;

            return false;
        }

        // Runtime population of the static button-list screens. Their action-id mapping is
        // runtime-only, so the controller must (re)apply it on Start and on every host
        // registration. Context-dependent menus (death) are populated when they are routed to.
        void RefreshStaticMenus()
        {
            RefreshTitleMenu();
            RefreshPauseMenu();

            RefreshSettingsMenu();

            string worldDetailsTitle = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleWorldDetails);
            frontend?.SetActionMenu(MenuActions.WorldDetailsScreen, worldDetailsTitle, MenuActions.WorldDetails);
        }

        void RefreshTitleMenu()
        {
            IReadOnlyList<MenuAction> actions = MenuActions.Title(latestSaveExists, anySaveExists, CanQuit());
            frontend?.SetActionMenu(MenuActions.TitleScreen, BlockiverseProject.ProductName, actions);
        }

        // Rebuilt rather than pushed once, because the debug-overlay row reports its own state in
        // its label. Called again immediately after the toggle so the row shows what just happened
        // — without it the setting flips and the button keeps claiming the old value.
        void RefreshGameplayScreensMenu()
        {
            frontend?.SetActionMenu(
                MenuActions.GameplayScreensScreen,
                UiText.Get(MenuActions.ScreensTitleKey),
                MenuActions.GameplayScreens());
        }

        void RefreshSettingsMenu()
        {
            string title = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleSettings);
            frontend?.SetActionMenu(
                MenuActions.SettingsScreen, title, MenuActions.Settings(DebugOverlayEnabled));
        }

        // Reads through to the comfort settings, which own the persisted flag. False when they are
        // absent, so a scene without them shows "Off" rather than throwing.
        bool DebugOverlayEnabled
        {
            get
            {
                BlockiverseComfortSettings settings = ResolveComfortSettings();
                return settings != null && settings.DebugOverlayEnabled;
            }
        }

        BlockiverseComfortSettings ResolveComfortSettings() =>
            comfortSettings != null
                ? comfortSettings
                : comfortSettings = BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(
                    FindObjectsInactive.Include);

        BlockiverseComfortSettings comfortSettings;

        void RefreshPauseMenu()
        {
            string pauseTitle = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused);
            IReadOnlyList<MenuAction> actions = MenuActions.PauseMenu(pauseCanToggleMode, pauseCanOpenCreativeTools, CanQuit());
            frontend?.SetActionMenu(MenuActions.PauseScreen, pauseTitle, actions);
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
                inputRig.ScreensPressed.RemoveListener(OnScreensPressed);
                inputRig.HotbarNextPressed.RemoveListener(OnHotbarNextPressed);
                inputRig.HotbarPreviousPressed.RemoveListener(OnHotbarPreviousPressed);
            }
        }

        void ResolveRuntimeReferences()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<IBlockiverseInputRig>();

            if (sessionController == null)
                sessionController = GetComponent<BlockiverseWorldSessionController>() ??
                                    GetComponentInParent<BlockiverseWorldSessionController>();

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

            if (networkSession == null)
                networkSession = BlockiverseSceneLookup.Find<BlockiverseNetworkSession>(FindObjectsInactive.Include);

            if (inputRig != null)
            {
                inputRig.MenuPressed.RemoveListener(OnMenuPressed);
                inputRig.MenuPressed.AddListener(OnMenuPressed);
                inputRig.ScreensPressed.RemoveListener(OnScreensPressed);
                inputRig.ScreensPressed.AddListener(OnScreensPressed);
                inputRig.HotbarNextPressed.RemoveListener(OnHotbarNextPressed);
                inputRig.HotbarNextPressed.AddListener(OnHotbarNextPressed);
                inputRig.HotbarPreviousPressed.RemoveListener(OnHotbarPreviousPressed);
                inputRig.HotbarPreviousPressed.AddListener(OnHotbarPreviousPressed);
            }
        }

        // Closes the station screen when the world block backing the open station is removed
        // (broken/picked up) by the local player or a host snapshot.
        void OnStationRemoved(BlockPosition position)
        {
            if (frontend == null || !frontend.IsStationOpenAt(position))
                return;

            frontend.CloseStationView();

            if (IsActiveScreen(MenuActions.StationMenuScreen))
                router.PopScreen();
        }

        public bool IsActiveScreen(string screenId) => router != null && string.Equals(router.ActiveScreen.ScreenId, screenId, StringComparison.Ordinal);

        public void ShowTitleScreen() => router.ClearToRoot(new ScreenRoute(MenuActions.TitleScreen, pauseGame: true));

        public void EnterGameplay() => router.ClearToRoot(new ScreenRoute(MenuActions.GameplayHudScreen, allowWorldInput: true));

        public void ShowWorldLoadingScreen() => router.PushScreen(new ScreenRoute(MenuActions.WorldLoadingScreen, pauseGame: true));

        public bool ShowLanMultiplayerScreen()
        {
            // Idempotent: LanMultiplayerScreenController re-enters this through its own
            // session-ended routing, and a second push would stack a duplicate route that a
            // single Close cannot pop past.
            if (!IsActiveScreen(MenuActions.LanMultiplayerScreen))
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
            string errorTitle = title ?? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleError);
            IReadOnlyList<MenuAction> actions = MenuActions.Error();
            frontend?.SetActionMenu(MenuActions.ErrorModal, errorTitle, actions);
            frontend?.SetScreenStatus(MenuActions.ErrorModal, message);
            router.PushModal(MenuActions.ErrorModal);
        }

        public void SetLoadWorldStatus(string message) =>
            frontend?.SetScreenStatus(MenuActions.LoadWorldScreen, message);

        public void SetTitleStatus(string message) =>
            frontend?.SetScreenStatus(MenuActions.TitleScreen, message);

        public void SetPauseStatus(string message) =>
            frontend?.SetScreenStatus(MenuActions.PauseScreen, message);

        void CloseErrorDialog() => router.PopModal();

        public void RequestConfirm(string prompt, string confirmLabel, string cancelLabel, Action<bool> callback)
        {
            confirmCallback = callback;
            frontend?.SetActionMenu(MenuActions.ConfirmModal, prompt, MenuActions.Confirm(confirmLabel, cancelLabel));

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
            // Materialize at the boundary. This is public API, the session layer builds the list
            // lazily, and a caller has no way to know how many times the far side enumerates it —
            // a spent enumerable arrives as an empty Load World screen, not as an error.
            if (saves != null && !(saves is ICollection<WorldSaveSummary>))
            {
                var buffered = new List<WorldSaveSummary>();
                foreach (WorldSaveSummary save in saves)
                    buffered.Add(save);
                saves = buffered;
            }

            frontend?.SetSaveList(saves);
        }

        void OnPlayerDeath()
        {
            string deathTitle = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleDeath);
            IReadOnlyList<MenuAction> actions = MenuActions.Death(vitalsRuntime != null && vitalsRuntime.HasBedrollSpawn);
            frontend?.SetActionMenu(MenuActions.DeathScreen, deathTitle, actions);

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
                    ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.PauseCreativeTools:
                    // The Creative Tools screen has a registered screen and close route but the
                    // menu rework dropped its push; restore it so the pause-menu entry opens the
                    // screen again.
                    ActionRequested?.Invoke(actionId);
                    // Refreshed on every push, not just OnEnable. The screen object is never torn
                    // down, so OnEnable fires once at scene load -- without this the time-of-day
                    // slider still holds its authored value, and the first touch snaps world time
                    // to that value instead of nudging from the live clock.
                    frontend?.RefreshCreativeEnvironmentControls();
                    router.PushScreen(new ScreenRoute(MenuActions.CreativeToolsScreen, pauseGame: true));
                    break;
                // Popping the hub first so the destination REPLACES it rather than stacking under
                // it — otherwise closing inventory would drop the player back into the hub, then
                // pause, instead of back into the world.
                case MenuActions.ScreensOpenInventory:
                    router.PopScreen();
                    OpenInventoryScreen();
                    break;
                case MenuActions.ScreensOpenCrafting:
                    router.PopScreen();
                    OpenCraftingScreen();
                    break;
                case MenuActions.ScreensOpenCrate:
                    router.PopScreen();
                    router.PushScreen(new ScreenRoute(MenuActions.StationCrateScreen));
                    break;
                case MenuActions.ScreensOpenCatalog:
                    router.PopScreen();
                    OpenCatalogScreen();
                    break;
                case MenuActions.ScreensClose:
                    router.PopScreen();
                    break;
                case MenuActions.PauseSettings:
                    // Rebuilt immediately before showing rather than trusting RefreshStaticMenus'
                    // earlier push: that one runs from this controller's own Start/registration,
                    // and persisted comfort settings load in a DIFFERENT component's Start, whose
                    // ordering relative to this one is not guaranteed. A saved
                    // DebugOverlayEnabled=true loaded after the earlier push would otherwise leave
                    // the row reading "Off" while the overlay is actually on, and pressing it would
                    // turn the overlay off while the label — now matching the flipped value —
                    // appeared unchanged.
                    RefreshSettingsMenu();
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
                    if (PendingLoadSave.HasValue)
                    {
                        frontend?.ShowWorldDetails(PendingLoadSave.Value);
                        router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
                    }
                    break;

                case MenuActions.WorldDetailsPlay:
                case MenuActions.WorldDetailsRename:
                case MenuActions.WorldDetailsDuplicate:
                    if (PendingDetailsSave.HasValue)
                        ActionRequested?.Invoke(actionId);
                    break;
                case MenuActions.WorldDetailsDeleteRequested:
                    if (PendingDetailsSave.HasValue)
                    {
                        string worldName = PendingDetailsSave.Value.Name;
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
                case MenuActions.SettingsOpenTextures:
                    router.PushScreen(new ScreenRoute(MenuActions.TexturesSettingsScreen, pauseGame: true));
                    break;
                case MenuActions.SettingsToggleDebugOverlay:
                    ToggleDebugOverlay();
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
                case MenuActions.ControlsClose:
                case MenuActions.CreativeToolsClose:
                case MenuActions.TexturesSettingsClose:
                    router.PopScreen();
                    break;

                case MenuActions.TexturesSettingsSelect:
                    // No screen transition -- the selection applies to the live world in place.
                    // BlockiverseWorldSessionController.HandleAction reads the chosen token back
                    // from BlockiverseTexturePackPreferences, so no payload travels with the id.
                    ActionRequested?.Invoke(actionId);
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
                    frontend?.ResetNewWorldScreen();
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

            ResolveRuntimeReferences();
            bool effectivePause = router.IsGamePaused && !IsMultiplayerSessionLive;
            BlockiverseRuntimeState.SetRouterState(effectivePause, router.AllowWorldInput);
            ApplyLocomotionSuppression();
            if (!HasConfirmModalOpen())
                confirmCallback = null;

            // Screen visibility is the host's job — it has its own Router.Changed subscription.
            // What is left here is the state the router owns: pause, world input, locomotion,
            // and modal bookkeeping.
        }

        void ApplyLocomotionSuppression()
        {
            if (inputRig == null || router == null)
                return;

            ResolveRuntimeReferences();
            // Menus no longer freeze the player: routed menus lazily follow the player in a
            // session and are world fixtures in the title mini-world, so movement stays free
            // in both. Block editing remains gated by AllowWorldInput while a menu has focus.
            // Only the world-loading transition (rig being repositioned) blocks locomotion.
            bool isWorldLoading = string.Equals(router.ActiveScreen.ScreenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal);
            if (!isWorldLoading)
                inputRig.LocomotionSuppressed = false;
        }

        Pose titleMenuPose;
        bool hasTitleMenuPose;

        public bool HasTitleMenuPose => hasTitleMenuPose;
        public Pose TitleMenuPose => titleMenuPose;

        // Sets the world-fixed pose every title-state menu uses. Called when the title
        // mini-world is (re)initialized so the pose is spawn-relative, never head-relative.
        public void SetTitleMenuPose(Pose pose)
        {
            titleMenuPose = pose;
            hasTitleMenuPose = true;
            frontend?.SetTitleMenuPose(pose);
        }

        // The support grip's availability gate. Formerly the quick block menu's ("usable only
        // over the gameplay HUD"); the grip now opens the gameplay-screens hub instead, and needs
        // the exact same guard — don't stack another push while a different screen or modal is
        // already up.
        bool CanOpenScreensHub()
        {
            return router != null &&
                !router.HasModal &&
                string.Equals(router.ActiveScreen.ScreenId, MenuActions.GameplayHudScreen, StringComparison.Ordinal);
        }

        // Support grip. The reliable, tracking-independent route into every gameplay screen —
        // the wrist menu's gesture needs the support controller tracked; this needs only the
        // button. Reuses the exact push RefreshGameplayScreensMenu/PauseOpenScreens used before
        // that pause row was retired in favour of this always-available one.
        void OnScreensPressed()
        {
            if (!CanOpenScreensHub())
                return;

            RefreshGameplayScreensMenu();
            router.PushScreen(new ScreenRoute(MenuActions.GameplayScreensScreen, pauseGame: true));
        }

        // Flips the persisted flag and rebuilds the row so its label reports the new state. The
        // overlay itself polls the setting rather than being pushed to, so nothing else is needed
        // here — and nothing here needs to know the overlay exists.
        void ToggleDebugOverlay()
        {
            BlockiverseComfortSettings settings = ResolveComfortSettings();

            if (settings == null)
                return;

            settings.DebugOverlayEnabled = !settings.DebugOverlayEnabled;
            RefreshSettingsMenu();
        }

        void OnHotbarNextPressed() => frontend?.CycleHotbarSlot(1);

        void OnHotbarPreviousPressed() => frontend?.CycleHotbarSlot(-1);

        public void CloseControllerMappingScreen()
        {
            if (IsActiveScreen(MenuActions.ControllerMappingScreen))
            {
                PlayerPrefs.SetInt(ControllerMappingPopupSeenPrefKey, 1);
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
            frontend?.ShowWorldDetails(save);
            router.PushScreen(new ScreenRoute(MenuActions.WorldDetailsScreen, pauseGame: true));
        }

        public void CloseWorldDetails()
        {
            if (IsActiveScreen(MenuActions.WorldDetailsScreen))
                router.PopScreen();
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

        // Close hooks the host's screen controllers call directly, so a Close verb is one method
        // call rather than a re-entry through DispatchAction.
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
                    if (saveActionId == MenuActions.PauseReturnToTitle)
                        ShowTitleScreen(); // return to title routes like the death path — it must not quit the app
                    else
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
