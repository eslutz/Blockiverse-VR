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
        public const string WorldLoadingScreen = "world_loading";
        public const string ControllerMappingScreen = "controller_mapping";
        public const string GameplayHudScreen = "gameplay_hud";
        public const string PauseScreen = "pause_menu";

        // The routed hub behind the pause menu's single "Screens" row. See GameplayScreens().
        public const string GameplayScreensScreen = "gameplay_screens";
        public const string SettingsScreen = "settings";
        public const string ComfortSettingsScreen = "settings_comfort";
        public const string AudioSettingsScreen = "settings_audio";
        public const string ControlsScreen = "controls";
        public const string CreativeToolsScreen = "creative_tools";
        public const string DeathScreen = "death_screen";
        public const string LanMultiplayerScreen = "lan_multiplayer";
        public const string StationMenuScreen = "station_menu";
        public const string ConfirmModal = "confirm_dialog";
        public const string ErrorModal = "error_dialog";
        public const string InventoryScreen = "inventory";
        public const string CraftingScreen = "crafting";
        public const string CatalogScreen = "catalog";
        public const string StationCrateScreen = "station_crate";

        // ── Title actions (§6.2) ─────────────────────────────────────────────
        public const string TitleContinue = "title.continue_latest_save";
        public const string TitleNewWorld = "title.open_new_world";
        public const string TitleLoadWorld = "title.open_load_world";
        public const string TitleMultiplayer = "title.open_lan_multiplayer";
        public const string TitleSettings = "title.open_settings";
        public const string TitleQuit = "title.quit_requested";

        // ── LAN multiplayer actions (§6) ─────────────────────────────────────
        public const string LanMultiplayerClose = "lan_multiplayer.close";
        public const string LanReconnect = "lan_multiplayer.reconnect";

        // ── Pause actions (§6.7) ─────────────────────────────────────────────
        public const string PauseResume = "pause.resume";
        public const string PauseSaveGame = "pause.save_game";
        public const string PauseToggleMode = "pause.toggle_survival_creative";
        public const string PauseCreativeTools = "pause.open_creative_tools";
        public const string PauseSettings = "pause.open_settings";

        // ── Gameplay screens hub ─────────────────────────────────────────────
        //
        // Those screens had exactly one entry point — the always-visible action bar — and that bar
        // has moved to the support wrist, where it appears only when the player turns their wrist
        // toward their face. That is the right default, but it depends on the support controller
        // being tracked; if it is off, lost, or dropped, a player would have no way to reach their
        // own inventory and no way to discover why. The support grip (formerly the Creative quick
        // block menu's toggle, retired once the catalog screen covered the same job from here)
        // opens this same hub directly, so it always answers — no pause needed.
        //
        // A hub rather than one binding per destination: the wrist menu and the grip both need to
        // reach all four, and one route behind one button/gesture is what lets a future screen join
        // the list without touching either.
        public const string ScreensOpenInventory = "gameplay_screens.open_inventory";
        public const string ScreensOpenCrafting = "gameplay_screens.open_crafting";
        public const string ScreensOpenCrate = "gameplay_screens.open_crate";
        public const string ScreensOpenCatalog = "gameplay_screens.open_catalog";
        public const string ScreensClose = "gameplay_screens.close";
        public const string PauseReturnToTitle = "pause.return_to_title_requested";
        public const string PauseQuit = "pause.quit_requested";
        public const string CreativeToolsClose = "creative_tools.close";

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
        public const string SettingsOpenComfort = "settings.open_comfort";
        public const string SettingsOpenAudio = "settings.open_audio";
        public const string SettingsOpenControls = "settings.open_controls";

        // Toggles the gameplay diagnostic readout (position, biome, weather, frame time, session).
        // A settings row rather than a controller binding: it is consulted rarely and every
        // gameplay input is already spoken for.
        public const string SettingsToggleDebugOverlay = "settings.toggle_debug_overlay";
        public const string SettingsClose = "settings.close";
        public const string ComfortSettingsClose = "settings_comfort.close";
        public const string AudioSettingsClose = "settings_audio.close";
        public const string ControlsClose = "controls.close";

        // ── World Details actions (§6.5) ─────────────────────────────────────
        public const string WorldDetailsPlay = "world_details.play";
        public const string WorldDetailsRename = "world_details.rename";
        public const string WorldDetailsDuplicate = "world_details.duplicate";
        public const string WorldDetailsDeleteRequested = "world_details.delete_requested";
        public const string WorldDetailsBack = "world_details.back";

        // ── Load World extra actions (§6.4 → §6.5) ───────────────────────────
        public const string LoadWorldDetails = "load_world.open_details";

        // ── Confirmation actions (§6.22) ─────────────────────────────────────
        public const string ConfirmAccept = "confirm.accept";
        public const string ConfirmCancel = "confirm.cancel";
        public const string ErrorClose = "error_dialog.close";

        // Title menu, filtered by what the player can currently do (§6.2 "Enabled When").
        public static IReadOnlyList<MenuAction> Title(bool hasLatestSave, bool hasAnySave, bool canQuit)
        {
            var actions = new List<MenuAction>(6);
            if (hasLatestSave)
                actions.Add(Localized(TitleContinue, BlockiverseLocalization.Keys.TitleContinue, "Continue"));
            actions.Add(Localized(TitleNewWorld, BlockiverseLocalization.Keys.TitleNewWorld, "New World"));
            if (hasAnySave)
                actions.Add(Localized(TitleLoadWorld, BlockiverseLocalization.Keys.TitleLoadWorld, "Load World"));
            actions.Add(Localized(TitleMultiplayer, BlockiverseLocalization.Keys.TitleMultiplayer, "LAN Multiplayer"));
            actions.Add(Localized(TitleSettings, BlockiverseLocalization.Keys.TitleSettingsAction, "Settings"));
            if (canQuit)
                actions.Add(Localized(TitleQuit, BlockiverseLocalization.Keys.TitleQuit, "Quit"));
            return actions;
        }

        public static IReadOnlyList<MenuAction> PauseMenu(bool canToggleMode, bool canOpenCreativeTools, bool canQuit = true)
        {
            var actions = new List<MenuAction>(7)
            {
                Localized(PauseResume, BlockiverseLocalization.Keys.PauseResume, "Resume"),
                Localized(PauseSaveGame, BlockiverseLocalization.Keys.PauseSaveGame, "Save Game"),
            };

            if (canToggleMode)
                actions.Add(Localized(PauseToggleMode, BlockiverseLocalization.Keys.PauseToggleMode, "Switch Survival/Creative"));
            if (canOpenCreativeTools)
                actions.Add(Localized(PauseCreativeTools, BlockiverseLocalization.Keys.PauseCreativeTools, "Creative Tools"));
            actions.Add(Localized(PauseSettings, BlockiverseLocalization.Keys.PauseSettings, "Settings"));
            actions.Add(Localized(PauseReturnToTitle, BlockiverseLocalization.Keys.PauseReturnToTitle, "Return to Title"));
            if (canQuit)
                actions.Add(Localized(PauseQuit, BlockiverseLocalization.Keys.PauseQuit, "Quit Game"));
            return actions;
        }

        // Death respawn options; the bedroll option is offered only when a bedroll spawn is set.
        public static IReadOnlyList<MenuAction> Death(bool hasBedrollSpawn)
        {
            var actions = new List<MenuAction>(3);
            if (hasBedrollSpawn)
                actions.Add(Localized(DeathRespawnBedroll, BlockiverseLocalization.Keys.DeathRespawnBedroll, "Respawn at Bedroll"));
            actions.Add(Localized(DeathRespawnWorldSpawn, BlockiverseLocalization.Keys.DeathRespawnWorldSpawn, "Respawn at World Spawn"));
            actions.Add(Localized(DeathReturnToTitle, BlockiverseLocalization.Keys.DeathReturnToTitle, "Return to Title"));
            return actions;
        }

        public static IReadOnlyList<MenuAction> Confirm(string confirmLabel = null, string cancelLabel = null)
        {
            return new[]
            {
                confirmLabel == null
                    ? Localized(ConfirmAccept, BlockiverseLocalization.Keys.ConfirmAccept, "Confirm")
                    : new MenuAction(ConfirmAccept, confirmLabel),
                cancelLabel == null
                    ? Localized(ConfirmCancel, BlockiverseLocalization.Keys.ConfirmCancel, "Cancel")
                    : new MenuAction(ConfirmCancel, cancelLabel),
            };
        }

        public static IReadOnlyList<MenuAction> Error()
        {
            return new List<MenuAction>(1)
            {
                Localized(ErrorClose, BlockiverseLocalization.Keys.ErrorClose, "Close"),
            };
        }

        public static IReadOnlyList<MenuAction> LanMultiplayer(bool canReconnect)
        {
            var actions = new List<MenuAction>(2);
            if (canReconnect)
                actions.Add(Localized(LanReconnect, BlockiverseLocalization.Keys.LanReconnect, "Join (Reconnect)"));
            actions.Add(Localized(LanMultiplayerClose, BlockiverseLocalization.Keys.CommonClose, "Close"));
            return actions;
        }

        // Settings hub (§6.19, adapted to the VR action-menu layout): comfort, audio, and the
// controls reference are their own screens/panels.
        // A factory rather than a static list, because the debug-overlay row shows its own state in
        // its label. Same shape as Title(...) and PauseMenu(...), which are factories for the same
        // reason — an action list that reports state has to be rebuilt when the state changes, and
        // BlockiverseMenuController.RefreshSettingsMenu does exactly that after the toggle.
        // Every gameplay screen, behind the pause menu's single Screens row.
        //
        // This is the GUARANTEED route: the wrist menu is the primary one and needs a tracked
        // support controller, while pause is on a dedicated button that always answers. Labels are
        // the ones the wrist menu already carries rather than parallel wording for the same
        // destinations.
        //
        // Adding a screen here costs one row in this list and nothing in the pause menu.
        // All four, unconditionally — deliberately identical to the wrist menu's contents.
        //
        // This hub exists to be that menu's fallback, so gating rows differently would mean the
        // guaranteed route offered LESS than the route it stands in for, which is the one thing a
        // fallback must never do. The wrist menu shows all four in every mode; so does this.
        public static IReadOnlyList<MenuAction> GameplayScreens() => new[]
        {
            Localized(ScreensOpenInventory, "ui.generated.survival.inventory", "Inventory"),
            Localized(ScreensOpenCrafting, "ui.generated.survival.crafting", "Crafting"),
            Localized(ScreensOpenCrate, "ui.status.crate.shared", "Shared crate"),
            Localized(ScreensOpenCatalog, "ui.generated.blocks.title", "Blocks"),
            Localized(ScreensClose, BlockiverseLocalization.Keys.SettingsClose, "Close"),
        };

        public const string ScreensTitleKey = "ui.action.pause.screens";

        public static IReadOnlyList<MenuAction> Settings(bool debugOverlayEnabled) => new[]
        {
            Localized(SettingsOpenComfort, BlockiverseLocalization.Keys.SettingsComfort, "Comfort"),
            Localized(SettingsOpenAudio, BlockiverseLocalization.Keys.SettingsAudio, "Audio"),
            Localized(SettingsOpenControls, BlockiverseLocalization.Keys.SettingsControls, "Controls"),
            debugOverlayEnabled
                ? Localized(SettingsToggleDebugOverlay, DebugOverlayOnKey, "Debug Overlay: On")
                : Localized(SettingsToggleDebugOverlay, DebugOverlayOffKey, "Debug Overlay: Off"),
            Localized(SettingsClose, BlockiverseLocalization.Keys.SettingsClose, "Close"),
        };

        // Not in BlockiverseLocalization.Keys: that class is the uGUI shim's key list and dies with
        // it, and these two entries are new with the UI Toolkit settings screen.
        const string DebugOverlayOnKey = "ui.action.settings.debug_overlay_on";
        const string DebugOverlayOffKey = "ui.action.settings.debug_overlay_off";

        // World Details management actions (§6.5).
        public static readonly IReadOnlyList<MenuAction> WorldDetails = new[]
        {
            Localized(WorldDetailsPlay, BlockiverseLocalization.Keys.WorldDetailsPlay, "Play"),
            Localized(WorldDetailsRename, BlockiverseLocalization.Keys.WorldDetailsRename, "Rename"),
            Localized(WorldDetailsDuplicate, BlockiverseLocalization.Keys.WorldDetailsDuplicate, "Duplicate"),
            Localized(WorldDetailsDeleteRequested, BlockiverseLocalization.Keys.WorldDetailsDelete, "Delete"),
            Localized(WorldDetailsBack, BlockiverseLocalization.Keys.WorldDetailsBack, "Back"),
        };

        static MenuAction Localized(string actionId, string labelKey, string fallbackLabel) =>
            new(actionId, labelKey, fallbackLabel);
    }
}
