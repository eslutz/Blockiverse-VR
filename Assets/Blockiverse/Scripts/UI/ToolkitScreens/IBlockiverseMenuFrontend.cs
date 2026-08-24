using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.UI
{
    // The seam between BlockiverseMenuController and the menu presentation layer (ADR 0010:
    // the UI Toolkit host). The controller keeps owning the router, action handling, first-run
    // flows and domain commands; every outward state push goes through this interface and every
    // pending-state read is answered through it.
    //
    // UiToolkitMenuHost is the only production implementer. The interface survives the uGUI
    // deletion because it is what lets EditMode tests stand a lightweight double in front of the
    // controller: the host is a sealed MonoBehaviour, so a concrete-typed reference here would
    // force every such test into scene-object construction.
    //
    // The shape follows the controller's existing pushes rather than a redesign, which is what
    // made the uGUI-to-Toolkit swap a mechanical exercise.
    public interface IBlockiverseMenuFrontend
    {
        // Button-list menus (title / pause / death / settings / world-details / confirm /
        // error). The same screen can be re-pushed with a different action list at any time
        // (availability, permissions, bedroll).
        void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions);

        // Status line on a specific screen (title / pause / load-world / error message).
        void SetScreenStatus(string screenId, string message);

        void SetSaveList(IEnumerable<WorldSaveSummary> saves);

        // The details screen renders this summary the next time it is routed to (the route
        // push itself still comes from the router).
        void ShowWorldDetails(WorldSaveSummary save);

        // Spawn-relative pose every title-state menu is fixed at (never head-derived).
        void SetTitleMenuPose(Pose pose);

        // Rides every pause-menu push of the creative tools screen: the screen object is never
        // torn down, so its OnEnable fires once at scene load and cannot be the refresh point
        // (see the call site in BlockiverseMenuController.HandleAction).
        void RefreshCreativeEnvironmentControls();

        // Quick block menu (creative hotbar); usable only over the gameplay HUD.
        void ToggleQuickBlockMenu();
        void HideQuickBlockMenu();

        // Rides the title "New World" action, so stale input from a previous visit never leaks
        // into the next create.
        void ResetNewWorldScreen();

        // Pending-state reads the controller forwards to the session layer. The player types
        // into the frontend's own screens, so this is where the values live.
        NewWorldConfig PendingNewWorldConfig { get; }
        WorldSaveSummary? PendingLoadSave { get; }
        WorldSaveSummary? PendingDetailsSave { get; }
        string PendingDetailsRenameText { get; }

        // Station screen lifecycle: the controller force-closes the station screen when the
        // block backing it disappears.
        bool IsStationOpenAt(BlockPosition position);
        void CloseStationView();
    }
}
