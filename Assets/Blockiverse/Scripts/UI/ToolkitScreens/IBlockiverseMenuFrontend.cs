using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    // The seam between BlockiverseMenuController and a replacement menu presentation layer
    // (ADR 0010: the UI Toolkit host). While a frontend is registered the controller keeps
    // owning the router, action handling, first-run flows and domain commands, but every
    // outward state push is mirrored here and every pending-state read is answered here —
    // the uGUI panels stay in the scene as the development fallback and are hidden.
    //
    // The interface is deliberately shaped after the controller's existing pushes rather
    // than redesigned, so parity between the two backends is a mechanical property.
    public interface IBlockiverseMenuFrontend
    {
        // Button-list menus (title / pause / death / settings / world-details / confirm /
        // error). Mirrors BlockiverseActionMenu.SetMenu: the same screen can be re-pushed
        // with a different action list at any time (availability, permissions, bedroll).
        void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions);

        // Status line on a specific screen (title / pause / load-world / error message).
        void SetScreenStatus(string screenId, string message);

        void SetSaveList(IEnumerable<WorldSaveSummary> saves);

        // Mirrors worldDetailsPanel.ShowSave: the details screen renders this summary the
        // next time it is routed to (the route push itself still comes from the router).
        void ShowWorldDetails(WorldSaveSummary save);

        // Spawn-relative pose every title-state menu is fixed at (never head-derived).
        void SetTitleMenuPose(Pose pose);

        // Mirrors creativeToolsPanel.RefreshEnvironmentControls on every pause-menu push of
        // the creative tools screen (see the comment at the uGUI call site: OnEnable fires
        // once at scene load, so the refresh must ride the route push).
        void RefreshCreativeEnvironmentControls();

        // Quick block menu (creative hotbar); usable only over the gameplay HUD.
        void ToggleQuickBlockMenu();
        void HideQuickBlockMenu();

        // Mirrors newWorldPanel.ResetForNewWorld on the title "New World" action, so stale
        // input from a previous visit never leaks into the next create.
        void ResetNewWorldScreen();

        // Pending-state reads the controller forwards to the session layer. With a frontend
        // registered the uGUI input fields never receive input, so these must come from the
        // frontend's own screens.
        NewWorldConfig PendingNewWorldConfig { get; }
        WorldSaveSummary? PendingLoadSave { get; }
        WorldSaveSummary? PendingDetailsSave { get; }
        string PendingDetailsRenameText { get; }

        // Station screen lifecycle (parity with stationPanel.IsOpen/OpenPosition/Close):
        // the controller force-closes the station screen when its backing block disappears.
        bool IsStationOpenAt(BlockPosition position);
        void CloseStationView();
    }
}
