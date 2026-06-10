using System.Collections.Generic;

namespace Blockiverse.UI
{
    // Canonical screen ids and action ids for the menu system (voxel_survival_menus §3, §6, §7),
    // plus factories for the button-list menus driven by BlockiverseActionMenu.
    public static class MenuActions
    {
        // ── Screen ids (§3) ──────────────────────────────────────────────────
        public const string TitleScreen = "title_menu";
        public const string NewWorldScreen = "new_world";
        public const string LoadWorldScreen = "load_world";
        public const string WorldDetailsScreen = "world_details";
        public const string GameplayHudScreen = "gameplay_hud";
        public const string PauseScreen = "pause_menu";
        public const string SettingsScreen = "settings";
        public const string ControlsScreen = "controls";
        public const string DeathScreen = "death_screen";
        public const string LanMultiplayerScreen = "lan_multiplayer";
        public const string StationMenuScreen = "station_menu";
        public const string ConfirmModal = "confirm_dialog";

        // ── Title actions (§6.2) ─────────────────────────────────────────────
        public const string TitleContinue = "title.continue_latest_save";
        public const string TitleNewWorld = "title.open_new_world";
        public const string TitleLoadWorld = "title.open_load_world";
        public const string TitleSettings = "title.open_settings";
        public const string TitleCredits = "title.open_credits";
        public const string TitleQuit = "title.quit_requested";

        // ── Pause actions (§6.7) ─────────────────────────────────────────────
        public const string PauseResume = "pause.resume";
        public const string PauseSaveGame = "pause.save_game";
        public const string PauseToggleMode = "pause.toggle_survival_creative";
        public const string PauseSettings = "pause.open_settings";
        public const string PauseControls = "pause.open_controls";
        public const string PauseReturnToTitle = "pause.return_to_title_requested";
        public const string PauseQuit = "pause.quit_requested";

        // ── Death actions (§6.21) ────────────────────────────────────────────
        public const string DeathRespawnBedroll = "death.respawn_bedroll";
        public const string DeathRespawnWorldSpawn = "death.respawn_world_spawn";
        public const string DeathReturnToTitle = "death.return_to_title";

        // ── New World actions (§6.3) ─────────────────────────────────────────
        public const string NewWorldCreate = "new_world.create";
        public const string NewWorldCancel = "new_world.cancel";

        // ── Load World actions (§6.4) ─────────────────────────────────────────
        public const string LoadWorldLoad = "load_world.load";
        public const string LoadWorldCancel = "load_world.cancel";

        // ── Settings actions ──────────────────────────────────────────────────
        public const string SettingsClose = "settings.close";

        // ── Confirmation actions (§6.22) ─────────────────────────────────────
        public const string ConfirmAccept = "confirm.accept";
        public const string ConfirmCancel = "confirm.cancel";

        // Title menu, filtered by what the player can currently do (§6.2 "Enabled When").
        public static IReadOnlyList<MenuAction> Title(bool hasLatestSave, bool hasAnySave, bool canQuit)
        {
            var actions = new List<MenuAction>(6);
            if (hasLatestSave)
                actions.Add(new MenuAction(TitleContinue, "Continue"));
            actions.Add(new MenuAction(TitleNewWorld, "New World"));
            if (hasAnySave)
                actions.Add(new MenuAction(TitleLoadWorld, "Load World"));
            actions.Add(new MenuAction(TitleSettings, "Settings"));
            actions.Add(new MenuAction(TitleCredits, "Credits"));
            if (canQuit)
                actions.Add(new MenuAction(TitleQuit, "Quit"));
            return actions;
        }

        public static readonly IReadOnlyList<MenuAction> Pause = new[]
        {
            new MenuAction(PauseResume, "Resume"),
            new MenuAction(PauseSaveGame, "Save Game"),
            new MenuAction(PauseToggleMode, "Switch Survival/Creative"),
            new MenuAction(PauseSettings, "Settings"),
            new MenuAction(PauseControls, "Controls"),
            new MenuAction(PauseReturnToTitle, "Return to Title"),
            new MenuAction(PauseQuit, "Quit Game"),
        };

        // Death respawn options; the bedroll option is offered only when a bedroll spawn is set.
        public static IReadOnlyList<MenuAction> Death(bool hasBedrollSpawn)
        {
            var actions = new List<MenuAction>(3);
            if (hasBedrollSpawn)
                actions.Add(new MenuAction(DeathRespawnBedroll, "Respawn at Bedroll"));
            actions.Add(new MenuAction(DeathRespawnWorldSpawn, "Respawn at World Spawn"));
            actions.Add(new MenuAction(DeathReturnToTitle, "Return to Title"));
            return actions;
        }

        public static IReadOnlyList<MenuAction> Confirm(string confirmLabel = "Confirm", string cancelLabel = "Cancel")
        {
            return new[]
            {
                new MenuAction(ConfirmAccept, confirmLabel),
                new MenuAction(ConfirmCancel, cancelLabel),
            };
        }
    }
}
