using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    // The UI Toolkit menu backend (ADR 0010: UiToolkitMenuHost). Owns screen lifecycle and
    // nothing else: BlockiverseMenuController keeps the router, action handling, first-run
    // flows and domain commands; this host renders its state through per-screen controllers
    // and answers its pending-state reads. While an enabled host is present the controller
    // hides every uGUI presenter — disabling this component is the whole dev fallback switch
    // back to the uGUI menus.
    [DisallowMultipleComponent]
    public sealed class UiToolkitMenuHost : MonoBehaviour, IBlockiverseMenuFrontend
    {
        [SerializeField] BlockiverseMenuController menuController;

        readonly List<(string screenId, UiToolkitScreenController controller)> screens = new();
        readonly Dictionary<string, UiToolkitScreenController> screensById = new(StringComparer.Ordinal);
        readonly Dictionary<UiToolkitScreenController, UiToolkitScreenAttribute> screenAttributes = new();
        bool hudPanelsAttached;

        IUiToolkitQuickBlockMenu quickBlockMenu;
        UiToolkitScreenController quickBlockMenuController;
        BlockiverseComfortSettings comfortSettings;
        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;
        Pose titleMenuPose;
        bool hasTitleMenuPose;
        bool registered;

        public BlockiverseMenuController MenuController => menuController;
        public IReadOnlyList<(string screenId, UiToolkitScreenController controller)> Screens => screens;

        public void Configure(BlockiverseMenuController controller) => menuController = controller;

        void Awake()
        {
            DiscoverScreens();
        }

        void Start()
        {
            ResolveMenuController();
            RegisterWithController();
            AttachHudPanels();
            ApplyRouterState();
        }

        // Hud-profile panels ride the rig like the uGUI HUD did: parented under Camera
        // Offset at a fixed local pose, never recentered, never world-placed. Runtime
        // parenting keeps the generated Boot scene free of rig-instance overrides.
        void AttachHudPanels()
        {
            if (hudPanelsAttached)
                return;

            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rigTransform))
                return;

            Transform cameraOffset = rigTransform.Find("Camera Offset");
            if (cameraOffset == null)
                return;

            foreach (var (_, controller) in screens)
                AttachHudPanel(controller, cameraOffset);

            // The quick block menu is excluded from the routed screens list but is still a
            // HUD-family panel and must ride the rig at its declared local pose.
            if (quickBlockMenuController != null)
                AttachHudPanel(quickBlockMenuController, cameraOffset);

            hudPanelsAttached = true;
        }

        void AttachHudPanel(UiToolkitScreenController controller, Transform cameraOffset)
        {
            if (!screenAttributes.TryGetValue(controller, out UiToolkitScreenAttribute attribute) ||
                attribute.PlacementProfile != UiToolkitPlacementProfile.Hud)
            {
                return;
            }

            Transform panel = controller.transform;
            panel.SetParent(cameraOffset, worldPositionStays: false);
            panel.localPosition = new Vector3(attribute.HudLocalX, attribute.HudLocalY, attribute.HudLocalZ);
            panel.localRotation = Quaternion.Euler(attribute.HudPitchDegrees, 0f, 0f);
        }

        bool IsHudProfile(UiToolkitScreenController controller) =>
            screenAttributes.TryGetValue(controller, out UiToolkitScreenAttribute attribute) &&
            attribute.PlacementProfile == UiToolkitPlacementProfile.Hud;

        void OnEnable()
        {
            if (registered)
                return;

            ResolveMenuController();
            RegisterWithController();
        }

        void OnDisable()
        {
            if (menuController != null && registered)
            {
                menuController.UnregisterFrontend(this);
                registered = false;
            }
        }

        void DiscoverScreens()
        {
            screens.Clear();
            screensById.Clear();
            quickBlockMenu = null;

            screenAttributes.Clear();

            foreach (UiToolkitScreenController controller in GetComponentsInChildren<UiToolkitScreenController>(true))
            {
                controller.ConfigureHost(this);

                var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                    controller.GetType(), typeof(UiToolkitScreenAttribute));
                if (attribute != null)
                    screenAttributes[controller] = attribute;

                if (controller is IUiToolkitQuickBlockMenu quickMenu)
                {
                    // Excluded from routed visibility, but still rig-attached and comfort-scaled
                    // like its HUD siblings — AttachHudPanels walks it separately.
                    quickBlockMenu = quickMenu;
                    quickBlockMenuController = controller;
                    continue;
                }

                screens.Add((controller.ScreenId, controller));
                // Several HUD-family panels legitimately share the gameplay_hud id; the
                // by-id index keeps the first, which is only used for targeted pushes
                // (statuses / action menus) that HUD panels do not implement.
                if (!screensById.ContainsKey(controller.ScreenId))
                    screensById[controller.ScreenId] = controller;
            }

            comfortSettings = BlockiverseSceneLookup.Find<BlockiverseComfortSettings>(FindObjectsInactive.Include);

            foreach (var (_, controller) in screens)
            {
                WorldSpaceUiPlacementController placement = controller.GetComponent<WorldSpaceUiPlacementController>();
                if (placement != null)
                    placement.ConfigureComfortSettings(comfortSettings);
            }
        }

        void ResolveMenuController()
        {
            if (menuController == null)
                menuController = BlockiverseSceneLookup.Find<BlockiverseMenuController>(FindObjectsInactive.Include);
        }

        void RegisterWithController()
        {
            if (menuController == null || registered)
                return;

            menuController.RegisterFrontend(this);
            menuController.Router.Changed += ApplyRouterState;
            registered = true;
        }

        void OnDestroy()
        {
            if (menuController != null && menuController.Router != null)
                menuController.Router.Changed -= ApplyRouterState;

            if (menuController != null && registered)
                menuController.UnregisterFrontend(this);
        }

        // Mirror of BlockiverseMenuController.ApplyRouterState's presenter loop, on UI
        // Toolkit screens. The semantics are copied deliberately, not redesigned: modal
        // screens are visible iff a modal is open and they are the input target; normal
        // screens are visible iff they are the routed screen; only the input target accepts
        // input; the world-loading overlay never does.
        void ApplyRouterState()
        {
            if (menuController == null || menuController.Router == null)
                return;

            UiScreenRouter router = menuController.Router;
            string activeId = router.ActiveScreen.ScreenId;
            string inputTarget = router.InputTarget;

            UiToolkitScreenController anchor = FindVisibleAnchoredScreen();

            foreach (var (screenId, controller) in screens)
            {
                bool isModal = screenId == MenuActions.ConfirmModal || screenId == MenuActions.ErrorModal;
                bool visible = isModal
                    ? router.HasModal && string.Equals(inputTarget, screenId, StringComparison.Ordinal)
                    : string.Equals(screenId, activeId, StringComparison.Ordinal);
                bool acceptsInput = visible &&
                    !string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal) &&
                    string.Equals(screenId, inputTarget, StringComparison.Ordinal);

                if (visible && !controller.IsVisible)
                {
                    ApplyPlacementFor(screenId, controller, anchor);
                    BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiConfirm);
                }
                else if (!visible && controller.IsVisible)
                {
                    BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiCancel);
                }

                controller.SetVisible(visible, acceptsInput);
            }

            if (quickBlockMenu != null && !CanUseQuickBlockMenu())
                quickBlockMenu.SetQuickMenuVisible(false);
        }

        void ApplyPlacementFor(string screenId, UiToolkitScreenController controller, UiToolkitScreenController anchor)
        {
            // Rig-attached panels are posed once by AttachHudPanels and never recentered.
            if (IsHudProfile(controller))
                return;

            WorldSpaceUiPlacementController placement = controller.GetComponent<WorldSpaceUiPlacementController>();
            if (placement == null)
                return;

            if (!IsAnchoredMenuScreen(screenId))
            {
                placement.OnShown(recenter: true);
                return;
            }

            bool inSession = SessionActive();
            var mode = inSession
                ? BlockiversePanelPlacementMode.LazyFollow
                : BlockiversePanelPlacementMode.WorldFixed;
            if (placement.PlacementMode != mode)
                placement.SetPlacementMode(mode);

            if (mode == BlockiversePanelPlacementMode.WorldFixed && hasTitleMenuPose && !placement.HasWorldFixedPose)
                placement.SetWorldFixedPose(titleMenuPose);

            // A world-fixed panel with a fixture pose places itself; otherwise keep the
            // stack's shared anchor so navigating within a menu never jumps the panel.
            bool hasFixture = placement.PlacementMode == BlockiversePanelPlacementMode.WorldFixed &&
                placement.HasWorldFixedPose;
            WorldSpaceUiPlacementController anchorPlacement = anchor != null
                ? anchor.GetComponent<WorldSpaceUiPlacementController>()
                : null;
            bool preserveAnchor = !hasFixture && anchorPlacement != null;

            if (preserveAnchor)
                placement.ApplyPlacementFrom(anchorPlacement);

            placement.OnShown(recenter: !preserveAnchor);
        }

        UiToolkitScreenController FindVisibleAnchoredScreen()
        {
            foreach (var (screenId, controller) in screens)
            {
                if (controller.IsVisible && IsAnchoredMenuScreen(screenId))
                    return controller;
            }

            return null;
        }

        static bool IsAnchoredMenuScreen(string screenId)
        {
            return !string.Equals(screenId, MenuActions.GameplayHudScreen, StringComparison.Ordinal) &&
                !string.Equals(screenId, MenuActions.WorldLoadingScreen, StringComparison.Ordinal);
        }

        bool SessionActive()
        {
            BlockiverseWorldSessionController session = menuController != null
                ? menuController.GetComponent<BlockiverseWorldSessionController>()
                : null;
            return session != null && session.HasActiveSession;
        }

        bool CanUseQuickBlockMenu()
        {
            UiScreenRouter router = menuController != null ? menuController.Router : null;
            return router != null &&
                !router.HasModal &&
                string.Equals(router.ActiveScreen.ScreenId, MenuActions.GameplayHudScreen, StringComparison.Ordinal);
        }

        T FindScreen<T>() where T : class
        {
            foreach (var (_, controller) in screens)
            {
                if (controller is T typed)
                    return typed;
            }

            return null;
        }

        // ---- IBlockiverseMenuFrontend ----

        public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions)
        {
            if (screensById.TryGetValue(screenId, out UiToolkitScreenController controller) &&
                controller is IUiToolkitActionMenuScreen actionMenu)
            {
                actionMenu.SetActionMenu(title, actions);
            }
        }

        public void SetScreenStatus(string screenId, string message)
        {
            if (screensById.TryGetValue(screenId, out UiToolkitScreenController controller) &&
                controller is IUiToolkitStatusScreen statusScreen)
            {
                statusScreen.SetStatus(message);
            }
        }

        public void SetSaveList(IEnumerable<WorldSaveSummary> saves) =>
            FindScreen<IUiToolkitSaveListScreen>()?.SetSaves(saves);

        public void ShowWorldDetails(WorldSaveSummary save) =>
            FindScreen<IUiToolkitWorldDetailsScreen>()?.ShowSave(save);

        public void SetTitleMenuPose(Pose pose)
        {
            titleMenuPose = pose;
            hasTitleMenuPose = true;

            foreach (var (screenId, controller) in screens)
            {
                if (!IsAnchoredMenuScreen(screenId))
                    continue;

                WorldSpaceUiPlacementController placement = controller.GetComponent<WorldSpaceUiPlacementController>();
                if (placement != null)
                    placement.SetWorldFixedPose(pose);
            }
        }

        public void RefreshCreativeEnvironmentControls() =>
            FindScreen<IUiToolkitCreativeToolsScreen>()?.RefreshEnvironmentControls();

        public void ToggleQuickBlockMenu()
        {
            if (quickBlockMenu == null || !CanUseQuickBlockMenu())
                return;

            quickBlockMenu.SetQuickMenuVisible(!quickBlockMenu.IsQuickMenuVisible);
        }

        public void HideQuickBlockMenu() => quickBlockMenu?.SetQuickMenuVisible(false);

        public void ResetNewWorldScreen() => FindScreen<IUiToolkitNewWorldScreen>()?.ResetForNewWorld();

        public NewWorldConfig PendingNewWorldConfig => FindScreen<IUiToolkitNewWorldScreen>()?.Config;

        public WorldSaveSummary? PendingLoadSave => FindScreen<IUiToolkitSaveListScreen>()?.SelectedSave;

        public WorldSaveSummary? PendingDetailsSave => FindScreen<IUiToolkitWorldDetailsScreen>()?.CurrentSave;

        public string PendingDetailsRenameText =>
            FindScreen<IUiToolkitWorldDetailsScreen>()?.PendingRenameText ?? string.Empty;

        public bool IsStationOpenAt(BlockPosition position)
        {
            IUiToolkitStationScreen station = FindScreen<IUiToolkitStationScreen>();
            return station != null && station.IsOpenAt(position);
        }

        public void CloseStationView() => FindScreen<IUiToolkitStationScreen>()?.CloseView();
    }
}
