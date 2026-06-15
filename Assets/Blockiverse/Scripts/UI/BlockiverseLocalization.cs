using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.WorldGen;

namespace Blockiverse.UI
{
    // Lightweight runtime string lookup for generated XR menus. English values remain the fallback
    // language, while tests and future locale loaders can override keys without rebuilding menus.
    public static class BlockiverseLocalization
    {
        public static class Keys
        {
            public const string TitleBlockiverse = "ui.title.blockiverse";
            public const string TitlePaused = "ui.title.paused";
            public const string TitleDeath = "ui.title.death";
            public const string TitleConfirm = "ui.title.confirm";
            public const string TitleSettings = "ui.title.settings";
            public const string TitleWorldDetails = "ui.title.world_details";

            public const string TitleContinue = "ui.action.title.continue";
            public const string TitleNewWorld = "ui.action.title.new_world";
            public const string TitleLoadWorld = "ui.action.title.load_world";
            public const string TitleMultiplayer = "ui.action.title.lan_multiplayer";
            public const string TitleSettingsAction = "ui.action.title.settings";
            public const string TitleQuit = "ui.action.title.quit";

            public const string PauseResume = "ui.action.pause.resume";
            public const string PauseSaveGame = "ui.action.pause.save_game";
            public const string PauseToggleMode = "ui.action.pause.toggle_mode";
            public const string PauseCreativeTools = "ui.action.pause.creative_tools";
            public const string PauseSettings = "ui.action.pause.settings";
            public const string PauseReturnToTitle = "ui.action.pause.return_to_title";
            public const string PauseQuit = "ui.action.pause.quit";

            public const string DeathRespawnBedroll = "ui.action.death.respawn_bedroll";
            public const string DeathRespawnWorldSpawn = "ui.action.death.respawn_world_spawn";
            public const string DeathReturnToTitle = "ui.action.death.return_to_title";

            public const string ConfirmAccept = "ui.action.confirm.accept";
            public const string ConfirmCancel = "ui.action.confirm.cancel";
            public const string ConfirmOk = "ui.action.confirm.ok";
            public const string ConfirmQuitGame = "ui.prompt.confirm.quit_game";

            public const string SettingsComfort = "ui.action.settings.comfort";
            public const string SettingsAudio = "ui.action.settings.audio";
            public const string SettingsControls = "ui.action.settings.controls";
            public const string SettingsClose = "ui.action.settings.close";

            public const string WorldDetailsPlay = "ui.action.world_details.play";
            public const string WorldDetailsRename = "ui.action.world_details.rename";
            public const string WorldDetailsDuplicate = "ui.action.world_details.duplicate";
            public const string WorldDetailsDelete = "ui.action.world_details.delete";
            public const string WorldDetailsBack = "ui.action.world_details.back";
            public const string WorldDetailsDeletePrompt = "ui.prompt.world_details.delete";

            public const string NewWorldTitle = "ui.generated.new_world.title";
            public const string NewWorldName = "ui.generated.new_world.name";
            public const string NewWorldSeed = "ui.generated.new_world.seed";
            public const string NewWorldGameMode = "ui.generated.new_world.game_mode";
            public const string NewWorldDifficulty = "ui.generated.new_world.difficulty";
            public const string NewWorldSize = "ui.generated.new_world.size";
            public const string NewWorldPreset = "ui.generated.new_world.preset";
            public const string NewWorldStartingBiome = "ui.generated.new_world.starting_biome";
            public const string NewWorldCreate = "ui.generated.new_world.create";
            public const string LoadWorldTitle = "ui.generated.load_world.title";
            public const string LoadWorldNoSaveSelected = "ui.generated.load_world.no_save_selected";
            public const string LoadWorldEntry = "ui.generated.load_world.entry";
            public const string LoadWorldPage = "ui.generated.load_world.page";
            public const string LoadWorldPreviousPage = "ui.generated.load_world.previous_page";
            public const string LoadWorldNextPage = "ui.generated.load_world.next_page";
            public const string LoadWorldLoad = "ui.generated.load_world.load";
            public const string LoadWorldDetails = "ui.generated.load_world.details";
            public const string AudioFeedbackTitle = "ui.generated.audio_feedback.title";
            public const string MasterVolume = "ui.generated.audio_feedback.master_volume";
            public const string EffectsVolume = "ui.generated.audio_feedback.effects_volume";
            public const string UiVolume = "ui.generated.audio_feedback.ui_volume";
            public const string WeatherVolume = "ui.generated.audio_feedback.weather_volume";
            public const string MusicVolume = "ui.generated.audio_feedback.music_volume";
            public const string HapticStrength = "ui.generated.audio_feedback.haptic_strength";
            public const string CreativeToolsTitle = "ui.generated.creative_tools.title";
            public const string CreativeToolsInitialStatus = "ui.generated.creative_tools.initial_status";
            public const string CreativeToolsSetA = "ui.generated.creative_tools.set_a";
            public const string CreativeToolsSetB = "ui.generated.creative_tools.set_b";
            public const string CreativeToolsPickBlock = "ui.generated.creative_tools.pick_block";
            public const string CreativeToolsFill = "ui.generated.creative_tools.fill";
            public const string CreativeToolsReplace = "ui.generated.creative_tools.replace";
            public const string CreativeToolsCopy = "ui.generated.creative_tools.copy";
            public const string CreativeToolsPaste = "ui.generated.creative_tools.paste";
            public const string CreativeToolsUndo = "ui.generated.creative_tools.undo";
            public const string CreativeToolsRedo = "ui.generated.creative_tools.redo";
            public const string CreativeToolsSpawnTree = "ui.generated.creative_tools.spawn_tree";
            public const string CreativeToolsSpawnRuin = "ui.generated.creative_tools.spawn_ruin";
            public const string CreativeToolsTimeOfDay = "ui.generated.creative_tools.time_of_day";
            public const string CreativeToolsDaySpeed = "ui.generated.creative_tools.day_speed";
            public const string CreativeToolsCycleWeather = "ui.generated.creative_tools.cycle_weather";
            public const string ControlsTitle = "ui.generated.controls.title";
            public const string WorldDetailsName = "ui.generated.world_details.name";
            public const string StationTitle = "ui.generated.station.title";
            public const string StationInput = "ui.generated.station.input";
            public const string StationFuel = "ui.generated.station.fuel";
            public const string StationOutput = "ui.generated.station.output";
            public const string StationIdle = "ui.generated.station.idle";
            public const string StationAddInput = "ui.generated.station.add_input";
            public const string StationAddFuel = "ui.generated.station.add_fuel";
            public const string StationCollect = "ui.generated.station.collect";
            public const string StationWithdrawInput = "ui.generated.station.withdraw_input";
            public const string StationWithdrawFuel = "ui.generated.station.withdraw_fuel";
            public const string BlocksTitle = "ui.generated.blocks.title";
            public const string BlocksCategory = "ui.generated.blocks.category";
            public const string BlocksSearchPlaceholder = "ui.generated.blocks.search_placeholder";
            public const string ControllerMapTitle = "ui.generated.controller_map.title";
            public const string SurvivalTitle = "ui.generated.survival.title";
            public const string SurvivalHealth = "ui.generated.survival.health";
            public const string SurvivalInventory = "ui.generated.survival.inventory";
            public const string SurvivalCrafting = "ui.generated.survival.crafting";
            public const string CommonClose = "ui.generated.common.close";
            public const string CommonCancel = "ui.generated.common.cancel";
            public const string CommonDelete = "ui.generated.common.delete";
            public const string CommonEmpty = "ui.common.empty";
            public const string CommonStack = "ui.common.stack";
            public const string CommonStackCount = "ui.common.stack_count";
            public const string CommonListSeparator = "ui.common.list_separator";
            public const string CommonActive = "ui.common.active";
            public const string CommonSending = "ui.common.sending";
            public const string CommonPage = "ui.common.page";
            public const string CatalogSearch = "ui.status.catalog.search";
            public const string WorldDetailsMetadata = "ui.status.world_details.metadata";
            public const string NewWorldSurvivalPresetUnsupported = "ui.status.new_world.survival_preset_unsupported";

            public const string InventoryHotbarEmpty = "ui.status.inventory.hotbar_empty";
            public const string InventoryHotbar = "ui.status.inventory.hotbar";
            public const string InventorySlotsCount = "ui.status.inventory.slots_count";
            public const string InventorySlotsRange = "ui.status.inventory.slots_range";
            public const string SurvivalHudMiningProgress = "ui.status.survival.mining_progress";
            public const string SurvivalHudInventoryFull = "ui.status.survival.inventory_full";
            public const string SurvivalHudToolTooWeak = "ui.status.survival.tool_too_weak";
            public const string SurvivalHudHarvestRejected = "ui.status.survival.harvest_rejected";

            public const string CraftingReady = "ui.status.crafting.ready";
            public const string CraftingRecipeUnavailable = "ui.status.crafting.recipe_unavailable";
            public const string CraftingRecipe = "ui.status.crafting.recipe";
            public const string CraftingCrafted = "ui.status.crafting.crafted";
            public const string CraftingCannotCraft = "ui.status.crafting.cannot_craft";
            public const string CraftingPending = "ui.status.crafting.pending";
            public const string CraftingToolRepaired = "ui.status.crafting.tool_repaired";
            public const string CraftingRepairing = "ui.status.crafting.repairing";
            public const string CraftingCannotRepair = "ui.status.crafting.cannot_repair";
            public const string CraftingNeedsStation = "ui.status.crafting.needs_station";

            public const string CrateShared = "ui.status.crate.shared";
            public const string CrateOffline = "ui.status.crate.offline";
            public const string CrateNothingHeld = "ui.status.crate.nothing_held";
            public const string CrateDeposited = "ui.status.crate.deposited";
            public const string CrateEmptySlot = "ui.status.crate.empty_slot";
            public const string CrateWithdrew = "ui.status.crate.withdrew";
            public const string CrateTransferring = "ui.status.crate.transferring";
            public const string CrateTransferRejected = "ui.status.crate.transfer_rejected";

            public const string StationHoldItem = "ui.status.station.hold_item";
            public const string StationFuelAdded = "ui.status.station.fuel_added";
            public const string StationInputAdded = "ui.status.station.input_added";
            public const string StationCannotDeposit = "ui.status.station.cannot_deposit";
            public const string StationCollected = "ui.status.station.collected";
            public const string StationCannotCollect = "ui.status.station.cannot_collect";
            public const string StationWithdrew = "ui.status.station.withdrew";
            public const string StationCannotWithdraw = "ui.status.station.cannot_withdraw";
            public const string StationNoFuel = "ui.status.station.no_fuel";
            public const string StationStack = "ui.status.station.stack";

            public const string StatusCreatingWorld = "ui.status.world.creating";
            public const string StatusCreateWorldFailed = "ui.status.world.create_failed";
            public const string StatusSaveNotFound = "ui.status.world.save_not_found";
            public const string StatusEnterWorldNameFirst = "ui.status.world.enter_name_first";
            public const string StatusRenameFailed = "ui.status.world.rename_failed";
            public const string StatusDuplicateFailed = "ui.status.world.duplicate_failed";
            public const string StatusDeleteFailed = "ui.status.world.delete_failed";
            public const string StatusLoadingWorld = "ui.status.world.loading";
            public const string StatusLoadFailed = "ui.status.world.load_failed";
            public const string StatusLoadWorldFailed = "ui.status.world.load_world_failed";
            public const string StatusSuspendSinglePlayerFailed = "ui.status.world.suspend_single_player_failed";
            public const string StatusWorldNameEmpty = "ui.status.world.name_empty";
            public const string StatusSaveSucceeded = "ui.status.world.save_succeeded";
            public const string StatusSaveFailed = "ui.status.world.save_failed";
            public const string StatusAutosaveSucceeded = "ui.status.world.autosave_succeeded";
            public const string StatusAutosaveFailed = "ui.status.world.autosave_failed";

            public const string LanUnavailable = "ui.status.lan.unavailable";
            public const string LanJoinAddressPlaceholder = "ui.generated.lan.join_address_placeholder";
            public const string LanStartingHost = "ui.status.lan.starting_host";
            public const string LanStartHostFailed = "ui.status.lan.start_host_failed";
            public const string LanJoining = "ui.status.lan.joining";
            public const string LanJoinFailed = "ui.status.lan.join_failed";
            public const string LanHosting = "ui.status.lan.hosting";
            public const string LanConnected = "ui.status.lan.connected";
            public const string LanStopping = "ui.status.lan.stopping";
            public const string LanStopped = "ui.status.lan.stopped";
            public const string LanStoppedWithDefault = "ui.status.lan.stopped_with_default";
            public const string LanStopFailed = "ui.status.lan.stop_failed";
            public const string LanStopFailedWithReason = "ui.status.lan.stop_failed_with_reason";
            public const string LanStoppingWithoutShutdownSave = "ui.status.lan.stopping_without_shutdown_save";
            public const string LanHostDisconnected = "ui.status.lan.host_disconnected";
            public const string LanUnableToReach = "ui.status.lan.unable_to_reach";
            public const string LanLastDisconnect = "ui.status.lan.last_disconnect";
            public const string LanFailed = "ui.status.lan.failed";

            public const string CreativeWeather = "ui.status.creative.weather";
            public const string CreativeCornerA = "ui.status.creative.corner_a";
            public const string CreativeCornerB = "ui.status.creative.corner_b";
            public const string CreativeSetCornerAim = "ui.status.creative.set_corner_aim";
            public const string CreativeSetCorner = "ui.status.creative.set_corner";
            public const string CreativeCorners = "ui.status.creative.corners";
            public const string CreativeAimReplace = "ui.status.creative.aim_replace";
            public const string CreativeChoosePasteOrigin = "ui.status.creative.choose_paste_origin";
            public const string CreativeSpawnedTree = "ui.status.creative.spawned_tree";
            public const string CreativeSpawnedRuin = "ui.status.creative.spawned_ruin";
            public const string CreativeAimPick = "ui.status.creative.aim_pick";
            public const string CreativePicked = "ui.status.creative.picked";
            public const string CreativeMissingCatalogBlock = "ui.status.creative.missing_catalog_block";
            public const string CreativeWeatherHostOnly = "ui.status.creative.weather_host_only";
            public const string CreativeTimeHostOnly = "ui.status.creative.time_host_only";
            public const string CreativeSetCornersFirst = "ui.status.creative.set_corners_first";
            public const string CreativeNoWorld = "ui.status.creative.no_world";
            public const string CreativeOnly = "ui.status.creative.creative_only";
            public const string CreativeLanUnavailable = "ui.status.creative.lan_unavailable";
            public const string CreativeAimGround = "ui.status.creative.aim_ground";
            public const string CreativeNoRoomAbove = "ui.status.creative.no_room_above";
            public const string CreativeOperationDone = "ui.status.creative.operation_done";
            public const string CreativeVolumeLimit = "ui.status.creative.volume_limit";
            public const string CreativeOutOfBounds = "ui.status.creative.out_of_bounds";
            public const string CreativeNoClipboard = "ui.status.creative.no_clipboard";
            public const string CreativeNothingToUndo = "ui.status.creative.nothing_to_undo";
            public const string CreativeNothingToRedo = "ui.status.creative.nothing_to_redo";
            public const string CreativeNothingToReplace = "ui.status.creative.nothing_to_replace";
            public const string CreativeOperationFailed = "ui.status.creative.operation_failed";

            public const string HealthDown = "ui.status.health.down";
            public const string HealthCritical = "ui.status.health.critical";
            public const string HealthStable = "ui.status.health.stable";
            public const string HealthVitals = "ui.status.health.vitals";
        }

        static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
        {
            [Keys.TitleBlockiverse] = "Blockiverse",
            [Keys.TitlePaused] = "Paused",
            [Keys.TitleDeath] = "You Died",
            [Keys.TitleConfirm] = "Confirm?",
            [Keys.TitleSettings] = "Settings",
            [Keys.TitleWorldDetails] = "World Details",

            [Keys.TitleContinue] = "Continue",
            [Keys.TitleNewWorld] = "New World",
            [Keys.TitleLoadWorld] = "Load World",
            [Keys.TitleMultiplayer] = "LAN Multiplayer",
            [Keys.TitleSettingsAction] = "Settings",
            [Keys.TitleQuit] = "Quit",

            [Keys.PauseResume] = "Resume",
            [Keys.PauseSaveGame] = "Save Game",
            [Keys.PauseToggleMode] = "Switch Survival/Creative",
            [Keys.PauseCreativeTools] = "Creative Tools",
            [Keys.PauseSettings] = "Settings",
            [Keys.PauseReturnToTitle] = "Return to Title",
            [Keys.PauseQuit] = "Quit Game",

            [Keys.DeathRespawnBedroll] = "Respawn at Bedroll",
            [Keys.DeathRespawnWorldSpawn] = "Respawn at World Spawn",
            [Keys.DeathReturnToTitle] = "Return to Title",

            [Keys.ConfirmAccept] = "Confirm",
            [Keys.ConfirmCancel] = "Cancel",
            [Keys.ConfirmOk] = "OK",
            [Keys.ConfirmQuitGame] = "Quit game?",

            [Keys.SettingsComfort] = "Comfort",
            [Keys.SettingsAudio] = "Audio",
            [Keys.SettingsControls] = "Controls",
            [Keys.SettingsClose] = "Close",

            [Keys.WorldDetailsPlay] = "Play",
            [Keys.WorldDetailsRename] = "Rename",
            [Keys.WorldDetailsDuplicate] = "Duplicate",
            [Keys.WorldDetailsDelete] = "Delete",
            [Keys.WorldDetailsBack] = "Back",
            [Keys.WorldDetailsDeletePrompt] = "Delete \"{0}\"? This cannot be undone.",

            [Keys.NewWorldTitle] = "New World",
            [Keys.NewWorldName] = "World Name",
            [Keys.NewWorldSeed] = "Seed",
            [Keys.NewWorldGameMode] = "Game Mode",
            [Keys.NewWorldDifficulty] = "Difficulty",
            [Keys.NewWorldSize] = "World Size",
            [Keys.NewWorldPreset] = "World Preset",
            [Keys.NewWorldStartingBiome] = "Starting Biome",
            [Keys.NewWorldCreate] = "Create World",
            ["ui.value.canonical.small"] = "Small (128x128)",
            ["ui.value.canonical.medium"] = "Medium (192x192)",
            ["ui.value.canonical.large"] = "Large (256x256)",
            ["ui.value.canonical.infinite"] = "Infinite Preview (256x256)",
            [Keys.LoadWorldTitle] = "Load World",
            [Keys.LoadWorldNoSaveSelected] = "No save selected",
            [Keys.LoadWorldEntry] = "{0}  ·  Day {1}",
            [Keys.LoadWorldPage] = "Page {0} / {1}",
            [Keys.LoadWorldPreviousPage] = "Prev",
            [Keys.LoadWorldNextPage] = "Next",
            [Keys.LoadWorldLoad] = "Load World",
            [Keys.LoadWorldDetails] = "Details",
            [Keys.AudioFeedbackTitle] = "Audio & Feedback",
            [Keys.MasterVolume] = "Master Volume",
            [Keys.EffectsVolume] = "Effects Volume",
            [Keys.UiVolume] = "UI Volume",
            [Keys.WeatherVolume] = "Weather Volume",
            [Keys.MusicVolume] = "Music Volume",
            [Keys.HapticStrength] = "Haptic Strength",
            [Keys.CreativeToolsTitle] = "Creative Tools",
            [Keys.CreativeToolsInitialStatus] = "Aim at blocks to select corners.",
            [Keys.CreativeToolsSetA] = "Set A",
            [Keys.CreativeToolsSetB] = "Set B",
            [Keys.CreativeToolsPickBlock] = "Pick Block",
            [Keys.CreativeToolsFill] = "Fill",
            [Keys.CreativeToolsReplace] = "Replace",
            [Keys.CreativeToolsCopy] = "Copy",
            [Keys.CreativeToolsPaste] = "Paste",
            [Keys.CreativeToolsUndo] = "Undo Edit",
            [Keys.CreativeToolsRedo] = "Redo Edit",
            [Keys.CreativeToolsSpawnTree] = "Spawn Tree",
            [Keys.CreativeToolsSpawnRuin] = "Spawn Ruin",
            [Keys.CreativeToolsTimeOfDay] = "Time of Day",
            [Keys.CreativeToolsDaySpeed] = "Day Speed",
            [Keys.CreativeToolsCycleWeather] = "Cycle Weather",
            [Keys.ControlsTitle] = "Controls",
            [Keys.WorldDetailsName] = "Name",
            [Keys.StationTitle] = "Station",
            [Keys.StationInput] = "Input",
            [Keys.StationFuel] = "Fuel",
            [Keys.StationOutput] = "Output",
            [Keys.StationIdle] = "Idle",
            [Keys.StationAddInput] = "Add Input",
            [Keys.StationAddFuel] = "Add Fuel",
            [Keys.StationCollect] = "Collect",
            [Keys.StationWithdrawInput] = "Take Input",
            [Keys.StationWithdrawFuel] = "Take Fuel",
            [Keys.BlocksTitle] = "Blocks",
            [Keys.BlocksCategory] = "Category",
            [Keys.BlocksSearchPlaceholder] = "Search blocks…",
            [Keys.ControllerMapTitle] = "Controller Map",
            [Keys.SurvivalTitle] = "Survival",
            [Keys.SurvivalHealth] = "Health",
            [Keys.SurvivalInventory] = "Inventory",
            [Keys.SurvivalCrafting] = "Crafting",
            [Keys.CommonClose] = "Close",
            [Keys.CommonCancel] = "Cancel",
            [Keys.CommonDelete] = "Delete",
            [Keys.CommonEmpty] = "Empty",
            [Keys.CommonStack] = "{0} x{1}",
            [Keys.CommonStackCount] = "x{0}",
            [Keys.CommonListSeparator] = ", ",
            [Keys.CommonActive] = "Active",
            [Keys.CommonSending] = "Sending…",
            [Keys.CommonPage] = "{0}/{1}",
            [Keys.CatalogSearch] = "Search",
            [Keys.WorldDetailsMetadata] = "Mode: {0}    Difficulty: {1}\nDay: {2}    Seed: {3}\nCreated: {4}    Last Played: {5}",
            [Keys.NewWorldSurvivalPresetUnsupported] = "Survival worlds require the Survival Terrain preset.",

            [Keys.InventoryHotbarEmpty] = "Hotbar -",
            [Keys.InventoryHotbar] = "Hotbar {0} / {1}",
            [Keys.InventorySlotsCount] = "Slots {0}",
            [Keys.InventorySlotsRange] = "Slots {0}-{1} / {2}",
            [Keys.SurvivalHudMiningProgress] = "Mining {0}%",
            [Keys.SurvivalHudInventoryFull] = "Inventory full",
            [Keys.SurvivalHudToolTooWeak] = "Tool is not strong enough",
            [Keys.SurvivalHudHarvestRejected] = "Cannot harvest this block",

            [Keys.CraftingReady] = "Ready",
            [Keys.CraftingRecipeUnavailable] = "Recipe unavailable",
            [Keys.CraftingRecipe] = "{0} - {1}",
            [Keys.CraftingCrafted] = "Crafted {0}",
            [Keys.CraftingCannotCraft] = "Cannot craft {0}: {1}",
            [Keys.CraftingPending] = "Crafting {0}…",
            [Keys.CraftingToolRepaired] = "Tool repaired",
            [Keys.CraftingRepairing] = "Repairing…",
            [Keys.CraftingCannotRepair] = "Cannot repair: {0}",
            [Keys.CraftingNeedsStation] = "{0} - {1} [needs {2}]",

            [Keys.CrateShared] = "Shared crate",
            [Keys.CrateOffline] = "Crate offline",
            [Keys.CrateNothingHeld] = "Nothing held to deposit",
            [Keys.CrateDeposited] = "Deposited {0}",
            [Keys.CrateEmptySlot] = "Empty slot",
            [Keys.CrateWithdrew] = "Withdrew {0}",
            [Keys.CrateTransferring] = "Transferring…",
            [Keys.CrateTransferRejected] = "Transfer rejected",

            [Keys.StationHoldItem] = "Hold an item to deposit",
            [Keys.StationFuelAdded] = "Fuel added",
            [Keys.StationInputAdded] = "Input added",
            [Keys.StationCannotDeposit] = "Cannot deposit: {0}",
            [Keys.StationCollected] = "Collected {0}",
            [Keys.StationCannotCollect] = "Cannot collect: {0}",
            [Keys.StationWithdrew] = "Withdrew {0}",
            [Keys.StationCannotWithdraw] = "Cannot withdraw: {0}",
            [Keys.StationNoFuel] = "No fuel",
            [Keys.StationStack] = "{0} ×{1}",

            [Keys.StatusCreatingWorld] = "Creating world...",
            [Keys.StatusCreateWorldFailed] = "Failed to create the world.",
            [Keys.StatusSaveNotFound] = "Save not found.",
            [Keys.StatusEnterWorldNameFirst] = "Enter a world name first.",
            [Keys.StatusRenameFailed] = "Failed to rename the world.",
            [Keys.StatusDuplicateFailed] = "Failed to duplicate the world.",
            [Keys.StatusDeleteFailed] = "Failed to delete the world.",
            [Keys.StatusLoadingWorld] = "Loading world...",
            [Keys.StatusLoadFailed] = "Failed to load: {0}",
            [Keys.StatusLoadWorldFailed] = "Failed to load the world.",
            [Keys.StatusSuspendSinglePlayerFailed] = "Unable to save the current single-player world before starting LAN.",
            [Keys.StatusWorldNameEmpty] = "World name cannot be empty.",
            [Keys.StatusSaveSucceeded] = "Game saved.",
            [Keys.StatusSaveFailed] = "Save failed.",
            [Keys.StatusAutosaveSucceeded] = "Autosaved.",
            [Keys.StatusAutosaveFailed] = "Autosave failed.",

            [Keys.LanUnavailable] = "LAN session is unavailable.",
            [Keys.LanJoinAddressPlaceholder] = "Host LAN IP",
            [Keys.LanStartingHost] = "Starting LAN host...",
            [Keys.LanStartHostFailed] = "Unable to start LAN host. {0}",
            [Keys.LanJoining] = "Joining LAN session at {0}:{1}...",
            [Keys.LanJoinFailed] = "Unable to join LAN session at {0}:{1}. {2}",
            [Keys.LanHosting] = "Hosting LAN session. Join at {0}:{1}.",
            [Keys.LanConnected] = "Connected to LAN session at {0}:{1}.",
            [Keys.LanStopping] = "Stopping LAN session...",
            [Keys.LanStopped] = "LAN session stopped.",
            [Keys.LanStoppedWithDefault] = "LAN session stopped. Enter the host LAN IP to join.",
            [Keys.LanStopFailed] = "Unable to stop LAN session.",
            [Keys.LanStopFailedWithReason] = "Unable to stop LAN session. {0}",
            [Keys.LanStoppingWithoutShutdownSave] = "Stopping LAN session without the latest shutdown save. {0}",
            [Keys.LanHostDisconnected] = "LAN session ended because the host disconnected. Use Join to reconnect to {0}:{1} when the LAN host is available again.",
            [Keys.LanUnableToReach] = "Unable to reach LAN session at {0}:{1}. Check that the host is on the same LAN and try Join again.",
            [Keys.LanLastDisconnect] = "{0} Last disconnect: {1}",
            [Keys.LanFailed] = "LAN session failed.",

            [Keys.CreativeWeather] = "Weather: {0}",
            [Keys.CreativeCornerA] = "corner A",
            [Keys.CreativeCornerB] = "corner B",
            [Keys.CreativeSetCornerAim] = "Aim at a block to set {0}.",
            [Keys.CreativeSetCorner] = "Set {0} to {1}.",
            [Keys.CreativeCorners] = "A: {0}    B: {1}",
            [Keys.CreativeAimReplace] = "Aim at a block of the type to replace.",
            [Keys.CreativeChoosePasteOrigin] = "Set corner A (or aim at a block) to choose the paste origin.",
            [Keys.CreativeSpawnedTree] = "Spawned tree at {0}.",
            [Keys.CreativeSpawnedRuin] = "Spawned ruin at {0}.",
            [Keys.CreativeAimPick] = "Aim at a block to pick it.",
            [Keys.CreativePicked] = "Picked {0}.",
            [Keys.CreativeMissingCatalogBlock] = "That block is not in the creative catalog.",
            [Keys.CreativeWeatherHostOnly] = "Weather control is host/offline only.",
            [Keys.CreativeTimeHostOnly] = "Time controls are host/offline only.",
            [Keys.CreativeSetCornersFirst] = "Set corners A and B first.",
            [Keys.CreativeNoWorld] = "No world loaded.",
            [Keys.CreativeOnly] = "Region tools work in creative worlds only.",
            [Keys.CreativeLanUnavailable] = "Region tools are unavailable during a LAN session.",
            [Keys.CreativeAimGround] = "Aim at a ground block first.",
            [Keys.CreativeNoRoomAbove] = "No room above the aimed block.",
            [Keys.CreativeOperationDone] = "{0} done.",
            [Keys.CreativeVolumeLimit] = "{0} failed: region exceeds the volume limit.",
            [Keys.CreativeOutOfBounds] = "{0} failed: region leaves the world bounds.",
            [Keys.CreativeNoClipboard] = "Nothing copied yet.",
            [Keys.CreativeNothingToUndo] = "Nothing to undo.",
            [Keys.CreativeNothingToRedo] = "Nothing to redo.",
            [Keys.CreativeNothingToReplace] = "No blocks of that type in the region.",
            [Keys.CreativeOperationFailed] = "{0} failed.",

            [Keys.HealthDown] = "Down",
            [Keys.HealthCritical] = "Critical",
            [Keys.HealthStable] = "Stable",
            [Keys.HealthVitals] = "{0} · Hunger {1} · Thirst {2} · Stamina {3}",
        };

        static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal);
        static readonly Dictionary<string, string> EnglishKeys = BuildEnglishKeys();

        public static string Text(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
                return fallback ?? string.Empty;

            if (Overrides.TryGetValue(key, out string localized))
                return localized;

            if (English.TryGetValue(key, out string english))
                return english;

            return fallback ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Text(key), args ?? Array.Empty<object>());
        }

        public static string DisplayNameForCanonicalId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            string key = "ui.value.canonical." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(CreativeCatalogCategory category)
        {
            string value = category.ToString();
            string key = "ui.value.creative_catalog_category." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(CraftingStation station)
        {
            string value = station.ToString();
            string key = "ui.value.crafting_station." + NormalizeKey(value);
            return Text(key, station == CraftingStation.None ? "Handcraft" : HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(WeatherState state)
        {
            string value = state.ToString();
            string key = "ui.value.weather_state." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(CraftingFailureReason reason)
        {
            string value = reason.ToString();
            string key = "ui.value.crafting_failure." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(SurvivalCommandFailureReason reason)
        {
            string value = reason.ToString();
            string key = "ui.value.survival_command_failure." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static string DisplayName(RepairFailureReason reason)
        {
            string value = reason.ToString();
            string key = "ui.value.repair_failure." + NormalizeKey(value);
            return Text(key, HumanizeIdentifier(value, titleCase: true));
        }

        public static bool TryGetKnownKeyForDefaultText(string defaultText, out string key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(defaultText))
                return false;

            return EnglishKeys.TryGetValue(defaultText, out key);
        }

        public static string GeneratedKeyForDefaultText(string defaultText)
        {
            if (TryGetKnownKeyForDefaultText(defaultText, out string key))
                return key;

            string normalized = NormalizeKey(defaultText);
            return string.IsNullOrEmpty(normalized) ? null : "ui.generated." + normalized;
        }

        public static void SetOverrideForTesting(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Localization keys must be non-empty.", nameof(key));

            if (value == null)
                Overrides.Remove(key);
            else
                Overrides[key] = value;
        }

        public static void ClearOverridesForTesting()
        {
            Overrides.Clear();
        }

        static Dictionary<string, string> BuildEnglishKeys()
        {
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in English)
            {
                if (!string.IsNullOrWhiteSpace(entry.Value) && !keys.ContainsKey(entry.Value))
                    keys.Add(entry.Value, entry.Key);
            }

            return keys;
        }

        static string NormalizeKey(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool lastWasSeparator = false;
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsLetterOrDigit(character))
                {
                    bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                    if (builder.Length > 0 &&
                        !lastWasSeparator &&
                        char.IsUpper(character) &&
                        (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(character));
                    lastWasSeparator = false;
                }
                else if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }

                previous = character;
            }

            while (builder.Length > 0 && builder[builder.Length - 1] == '_')
                builder.Length--;

            return builder.ToString();
        }

        static string HumanizeIdentifier(string value, bool titleCase)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            var words = new List<string>();
            var builder = new StringBuilder(value.Length);
            char previous = '\0';

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '_' || character == '-' || char.IsWhiteSpace(character))
                {
                    FlushWord(builder, words);
                    previous = character;
                    continue;
                }

                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (builder.Length > 0 &&
                    char.IsUpper(character) &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower)))
                {
                    FlushWord(builder, words);
                }

                builder.Append(character);
                previous = character;
            }

            FlushWord(builder, words);
            if (words.Count == 0)
                return "—";

            for (int i = 0; i < words.Count; i++)
            {
                string lower = words[i].ToLowerInvariant();
                if (titleCase)
                {
                    bool lowerMinorWord = i > 0 && (lower == "a" || lower == "an" || lower == "the" || lower == "of" || lower == "to");
                    words[i] = lowerMinorWord ? lower : char.ToUpperInvariant(lower[0]) + lower.Substring(1);
                }
                else
                {
                    words[i] = i == 0 ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
                }
            }

            return string.Join(" ", words);
        }

        static void FlushWord(StringBuilder builder, List<string> words)
        {
            if (builder.Length == 0)
                return;

            words.Add(builder.ToString());
            builder.Length = 0;
        }
    }
}
