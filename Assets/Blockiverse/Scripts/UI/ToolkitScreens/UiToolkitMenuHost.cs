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
    // and answers its pending-state reads. It is now the only menu frontend — with no host
    // registered the controller still routes correctly but nothing draws.
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
        // No audio fields here any more: the host plays nothing. Route changes are silent by
        // design, and every remaining cue lives with the screen that knows the outcome.
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

        // Hud-profile panels ride the HEAD, at a fixed local pose, never recentered, never
        // world-placed. Runtime parenting keeps the generated Boot scene free of rig-instance
        // overrides.
        //
        // They used to hang off Camera Offset, which is the rig's floor anchor — the head camera
        // is a CHILD of it, so the panels were the head's siblings. That tracks where the player
        // stands but not where they look: turn your head and the HUD stays behind in rig space.
        // It was survivable while everything sat dead ahead, and became the whole problem the
        // moment the HUD moved to the edge of vision, because anything off-centre leaves the view
        // entirely unless you happen to be facing the rig's forward axis. Eric reported both
        // symptoms together — misplaced AND not following his head — and they are one bug.
        //
        // Head-locking is right for THIS content specifically: small, read-at-a-glance strips that
        // should hold a constant place in the field of view. It is not a licence to head-lock the
        // routed menus, which are large, dwelt on, and world-placed on purpose.
        void AttachHudPanels()
        {
            if (hudPanelsAttached)
                return;

            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rigTransform))
                return;

            Transform cameraOffset = rigTransform.Find("Camera Offset");
            if (cameraOffset == null)
                return;

            // Fall back to Camera Offset if the head is missing rather than dropping the HUD
            // entirely: a mispositioned HUD is recoverable, an absent one is not.
            Transform head = cameraOffset.Find("Main Camera") ?? cameraOffset;

            foreach (var (_, controller) in screens)
                AttachHudPanel(controller, head);

            // The quick block menu is excluded from the routed screens list but is still a
            // HUD-family panel and must ride the head at its declared local pose.
            if (quickBlockMenuController != null)
                AttachHudPanel(quickBlockMenuController, head);

            hudPanelsAttached = true;
        }

        void AttachHudPanel(UiToolkitScreenController controller, Transform head)
        {
            if (!screenAttributes.TryGetValue(controller, out UiToolkitScreenAttribute attribute) ||
                attribute.PlacementProfile != UiToolkitPlacementProfile.Hud)
            {
                return;
            }

            // HudLocal* are now HEAD-relative: Y = 0 is eye level, not floor level. The declared
            // values were rebased when the parent changed — a floor-relative 1.55 would otherwise
            // put a panel a metre and a half above the player's eyes.
            Transform panel = controller.transform;
            panel.SetParent(head, worldPositionStays: false);
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

        // The teardown has to be complete, not just a deregistration. Merely unregistering
        // left the router subscription live and the panels on screen: the "disabled" host
        // went on driving Toolkit screens through every later navigation, and a LAN screen
        // left visible kept its discovery socket listening. Unsubscribe, then hide
        // everything including the quick menu.
        void OnDisable()
        {
            DetachFromController();

            foreach (var (_, controller) in screens)
                controller.SetVisible(false, false);

            quickBlockMenu?.SetQuickMenuVisible(false);
        }

        void DetachFromController()
        {
            if (menuController == null || !registered)
                return;

            if (menuController.Router != null)
                menuController.Router.Changed -= ApplyRouterState;

            menuController.UnregisterFrontend(this);
            registered = false;
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
                ConfigureComfortFor(controller);

            // The quick menu is excluded from `screens`, so a loop over that list silently
            // skips it — the same omission that left it un-parented until AttachHudPanels
            // was given its own case. Without this it is the one panel that ignores the
            // comfort UI scale, which is an accessibility setting, not a preference.
            if (quickBlockMenuController != null)
                ConfigureComfortFor(quickBlockMenuController);
        }

        void ConfigureComfortFor(UiToolkitScreenController controller)
        {
            WorldSpaceUiPlacementController placement = controller.GetComponent<WorldSpaceUiPlacementController>();

            if (placement != null)
                placement.ConfigureComfortSettings(comfortSettings);
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
            DetachFromController();
        }

        // Screen visibility for the whole menu system. The semantics were copied from the
        // uGUI presenter loop this replaced rather than redesigned, and they still hold:
        // modal screens are visible iff a modal is open and they are the input target;
        // normal screens are visible iff they are the routed screen; only the input target
        // accepts input; the world-loading overlay never does.
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
                    ApplyPlacementFor(screenId, controller, anchor);

                controller.SetVisible(visible, acceptsInput);
            }

            // NO SOUND ON NAVIGATION. Eric's ruling (2026-08-24): a confirm noise belongs on an
            // actual confirmation, a cancel noise on an actual cancel, and every other button just
            // clicks. Moving between screens is not an outcome and does not need announcing.
            //
            // This used to play UiConfirm whenever a route revealed anything and UiCancel whenever
            // one only closed things — so the heaviest cue in the set fired on every single menu
            // step, layered on top of the button's own click. It is why softening ui_select changed
            // nothing audible: the sound being complained about was this one, 8 dB louder at peak
            // and 20 dB brighter, drowning the click underneath it.
            //
            // The genuine outcome cues are untouched and live where the outcome is known: the LAN
            // screen plays UiConfirm when a session actually starts and UiCancel when it fails, and
            // the crate screen plays UiCancel on a rejected transfer. Those mean something. A
            // screen appearing does not.

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
