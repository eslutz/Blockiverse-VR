using System.Collections.Generic;
using Blockiverse.Persistence;
using Blockiverse.Voxel;

namespace Blockiverse.UI
{
    // Capability interfaces the UiToolkitMenuHost uses to route BlockiverseMenuController's
    // outward pushes to the right screen controller. A screen implements only what it needs;
    // the host discovers capabilities by cast, so adding a capability never edits the host.

    // Button-list screens (title / pause / death / settings / world-details / confirm / error).
    public interface IUiToolkitActionMenuScreen
    {
        void SetActionMenu(string title, IReadOnlyList<MenuAction> actions);
    }

    // Screens with a status line the controller writes to (title / pause / load-world / error).
    public interface IUiToolkitStatusScreen
    {
        void SetStatus(string message);
    }

    public interface IUiToolkitSaveListScreen
    {
        void SetSaves(IEnumerable<WorldSaveSummary> saves);
        WorldSaveSummary? SelectedSave { get; }
    }

    public interface IUiToolkitWorldDetailsScreen
    {
        void ShowSave(WorldSaveSummary save);
        WorldSaveSummary? CurrentSave { get; }
        string PendingRenameText { get; }
    }

    public interface IUiToolkitNewWorldScreen
    {
        NewWorldConfig Config { get; }
        void ResetForNewWorld();
    }

    public interface IUiToolkitStationScreen
    {
        bool IsOpenAt(BlockPosition position);
        void CloseView();
    }

    public interface IUiToolkitCreativeToolsScreen
    {
        void RefreshEnvironmentControls();
    }
}
