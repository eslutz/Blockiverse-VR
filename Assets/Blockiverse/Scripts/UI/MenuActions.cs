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
        public const string SettingsScreen = "settings";
        public const string ComfortSettingsScreen = "settings_comfort";
        public const string AudioSettingsScreen = "settings_audio";
        public const string ControlsScreen = "controls";
        public const string CreativeToolsScreen = "creative_tools";
        public const string DeathScreen = "death_screen";
        public const string LanMultiplayerScreen = "lan_multiplayer";
        public const string StationMenuScreen = "station_menu";
        public const string ConfirmModal = "confirm_dialog";
        public const string InventoryScreen = "inventory";
        public const string VitalsStatusScreen = "vitals_status";
        public const string CraftingScreen = "crafting";
        public const string ContainerScreen = "container";
        public const string CampfireStationScreen = "station_campfire";
        public const string ClayKilnStationScreen = "station_clay_kiln";
        public const string BellowsForgeStationScreen = "station_bellows_forge";
        public const string PrepBoardStationScreen = "station_prep_board";
        public const string MendBenchStationScreen = "station_mend_bench";
        public const string MapWayflagScreen = "map_wayflag";
        public const string ItemDetailsPopover = "item_details_popover";
        public const string RecipePinOverlay = "recipe_pin_overlay";
        public const string BlockCatalogScreen = "block_catalog";
        public const string PlayerHubScreen = "player_hub";
        public const string ContextHubScreen = "context_hub";
        public const string StatusHubScreen = "status_hub";
        public const string FarmingSummaryScreen = "farming_summary";
        public const string FarmingActionPopup = "farming_action_popup";
        public const string AvatarStatusScreen = "avatar_status";
        public const string MetaPolicyStatusScreen = "meta_policy_status";
        public const string DiagnosticsScreen = "diagnostics";
        public const string NetworkCommandStatusScreen = "network_command_status";
        public const string SurvivalRejectionScreen = "survival_rejection";

        // ── Title actions (§6.2) ─────────────────────────────────────────────
        public const string TitleContinue = "title.continue_latest_save";
        public const string TitleNewWorld = "title.open_new_world";
        public const string TitleLoadWorld = "title.open_load_world";
        public const string TitleMultiplayer = "title.open_lan_multiplayer";
        public const string TitleSettings = "title.open_settings";
        public const string TitleQuit = "title.quit_requested";

        // ── First-run controller mapping actions ─────────────────────────────
        public const string ControllerMappingClose = "controller_mapping.close";

        // ── LAN multiplayer actions (§6) ─────────────────────────────────────
        public const string LanMultiplayerHost = "lan_multiplayer.host";
        public const string LanMultiplayerJoin = "lan_multiplayer.join";
        public const string LanMultiplayerStop = "lan_multiplayer.stop";
        public const string LanMultiplayerClose = "lan_multiplayer.close";

        // ── Pause actions (§6.7) ─────────────────────────────────────────────
        public const string PauseResume = "pause.resume";
        public const string PauseSaveGame = "pause.save_game";
        public const string PausePlayerHub = "pause.open_player_hub";
        public const string PauseToggleMode = "pause.toggle_survival_creative";
        public const string PauseCreativeTools = "pause.open_creative_tools";
        public const string PauseSettings = "pause.open_settings";
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

        // ── Player hub actions (§6/§8 survival menu surfaces) ────────────────
        public const string PlayerHubInventory = "player_hub.open_inventory";
        public const string PlayerHubVitals = "player_hub.open_vitals";
        public const string PlayerHubCrafting = "player_hub.open_crafting";
        public const string PlayerHubClose = "player_hub.close";
        public const string InventoryItemDetails = "inventory.item_details";
        public const string InventoryBack = "inventory.back";
        public const string VitalsBack = "vitals_status.back";
        public const string CraftingRepair = "crafting.repair";
        public const string CraftingCraftSelected = "crafting.craft_selected";
        public const string CraftingPinSelected = "crafting.pin_selected";
        public const string CraftingBack = "crafting.back";
        public const string PlayerHubContext = "player_hub.open_context";
        public const string PlayerHubStatus = "player_hub.open_status";
        public const string PlayerHubRecipePin = "player_hub.open_recipe_pin";
        public const string ContextHubContainer = "context_hub.open_container";
        public const string ContextHubStation = "context_hub.open_station";
        public const string ContextHubFarming = "context_hub.open_farming";
        public const string ContextHubMap = "context_hub.open_map";
        public const string ContextHubCreativeTools = "context_hub.open_creative_tools";
        public const string ContextHubBack = "context_hub.back";
        public const string StatusHubVitals = "status_hub.open_vitals";
        public const string StatusHubAvatar = "status_hub.open_avatar";
        public const string StatusHubMetaPolicy = "status_hub.open_meta_policy";
        public const string StatusHubDiagnostics = "status_hub.open_diagnostics";
        public const string StatusHubNetwork = "status_hub.open_network";
        public const string StatusHubBack = "status_hub.back";
        public const string ContainerDepositHeld = "container.deposit_held";
        public const string ContainerBack = "container.back";
        public const string StationDepositInput = "station.deposit_input";
        public const string StationDepositFuel = "station.deposit_fuel";
        public const string StationCollectOutput = "station.collect_output";
        public const string StationWithdrawInput = "station.withdraw_input";
        public const string StationWithdrawFuel = "station.withdraw_fuel";
        public const string StationBack = "station.back";
        public const string MapSetWayflag = "map_wayflag.set";
        public const string MapClearWayflag = "map_wayflag.clear";
        public const string MapBack = "map_wayflag.back";
        public const string FarmingOpenActions = "farming.open_actions";
        public const string FarmingTill = "farming_action.till";
        public const string FarmingPlant = "farming_action.plant";
        public const string FarmingHarvest = "farming_action.harvest";
        public const string FarmingBack = "farming.back";
        public const string ItemDetailsBack = "item_details.back";
        public const string RecipePinClear = "recipe_pin.clear";
        public const string RecipePinBack = "recipe_pin.back";
        public const string BlockCatalogPreviousCategory = "block_catalog.previous_category";
        public const string BlockCatalogNextCategory = "block_catalog.next_category";
        public const string BlockCatalogBack = "block_catalog.back";
        public const string CreativeToolsSetA = "creative_tools.set_a";
        public const string CreativeToolsSetB = "creative_tools.set_b";
        public const string CreativeToolsPickBlock = "creative_tools.pick_block";
        public const string CreativeToolsFill = "creative_tools.fill";
        public const string CreativeToolsReplace = "creative_tools.replace";
        public const string CreativeToolsDelete = "creative_tools.delete";
        public const string CreativeToolsCopy = "creative_tools.copy";
        public const string CreativeToolsPaste = "creative_tools.paste";
        public const string CreativeToolsUndo = "creative_tools.undo";
        public const string CreativeToolsRedo = "creative_tools.redo";
        public const string CreativeToolsSpawnTree = "creative_tools.spawn_tree";
        public const string CreativeToolsSpawnRuin = "creative_tools.spawn_ruin";
        public const string CreativeToolsToggleCycle = "creative_tools.toggle_cycle";
        public const string CreativeToolsCycleWeather = "creative_tools.cycle_weather";
        public const string AvatarStatusBack = "avatar_status.back";
        public const string MetaPolicyStatusBack = "meta_policy_status.back";
        public const string DiagnosticsBack = "diagnostics.back";
        public const string NetworkCommandStatusBack = "network_command_status.back";
        public const string SurvivalRejectionDismiss = "survival_rejection.dismiss";

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
            var actions = new List<MenuAction>(8)
            {
                Localized(PauseResume, BlockiverseLocalization.Keys.PauseResume, "Resume"),
                Localized(PauseSaveGame, BlockiverseLocalization.Keys.PauseSaveGame, "Save Game"),
                Localized(PausePlayerHub, BlockiverseLocalization.Keys.PausePlayerHub, "Player Hub"),
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

        // Settings hub (§6.19, adapted to the VR action-menu layout): comfort, audio, and the
        // controls reference are their own screens/panels.
        public static readonly IReadOnlyList<MenuAction> Settings = new[]
        {
            Localized(SettingsOpenComfort, BlockiverseLocalization.Keys.SettingsComfort, "Comfort"),
            Localized(SettingsOpenAudio, BlockiverseLocalization.Keys.SettingsAudio, "Audio"),
            Localized(SettingsOpenControls, BlockiverseLocalization.Keys.SettingsControls, "Controls"),
            Localized(SettingsClose, BlockiverseLocalization.Keys.SettingsClose, "Close"),
        };

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
