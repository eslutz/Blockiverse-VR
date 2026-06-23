using System;
using System.Collections.Generic;
using System.Linq;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using UnityEngine;

namespace Blockiverse.UI
{
    public readonly struct MenuDetailRow
    {
        public MenuDetailRow(string label, string value)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }
    }

    public readonly struct MenuTextInputRow
    {
        public MenuTextInputRow(string fieldId, string label, string value, string placeholder = null)
        {
            FieldId = fieldId ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            Placeholder = placeholder ?? string.Empty;
        }

        public string FieldId { get; }
        public string Label { get; }
        public string Value { get; }
        public string Placeholder { get; }
    }

    public readonly struct MenuCycleRow
    {
        public MenuCycleRow(string fieldId, string label, string value)
        {
            FieldId = fieldId ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string FieldId { get; }
        public string Label { get; }
        public string Value { get; }
    }

    public readonly struct MenuSelectionRow
    {
        public MenuSelectionRow(string valueId, string label, string description, bool selected)
        {
            ValueId = valueId ?? string.Empty;
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
            Selected = selected;
        }

        public string ValueId { get; }
        public string Label { get; }
        public string Description { get; }
        public bool Selected { get; }
    }

    public readonly struct MenuToggleRow
    {
        public MenuToggleRow(string fieldId, string label, bool value, string description = null)
        {
            FieldId = fieldId ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value;
            Description = description ?? string.Empty;
        }

        public string FieldId { get; }
        public string Label { get; }
        public bool Value { get; }
        public string Description { get; }
    }

    public readonly struct MenuSliderRow
    {
        public MenuSliderRow(string fieldId, string label, float value, float minValue, float maxValue, string valueLabel = null)
        {
            FieldId = fieldId ?? string.Empty;
            Label = label ?? string.Empty;
            Value = value;
            MinValue = minValue;
            MaxValue = maxValue;
            ValueLabel = valueLabel ?? value.ToString("0.##");
        }

        public string FieldId { get; }
        public string Label { get; }
        public float Value { get; }
        public float MinValue { get; }
        public float MaxValue { get; }
        public string ValueLabel { get; }
    }

    public readonly struct MenuPagingState
    {
        public MenuPagingState(int pageIndex, int pageCount)
        {
            PageIndex = pageIndex < 0 ? 0 : pageIndex;
            PageCount = pageCount < 1 ? 1 : pageCount;
        }

        public int PageIndex { get; }
        public int PageCount { get; }
        public bool HasMultiplePages => PageCount > 1;
        public string DisplayText => BlockiverseLocalization.Format(
            BlockiverseLocalization.Keys.LoadWorldPage,
            PageIndex + 1,
            PageCount);
    }

    public sealed class BlockiverseUiToolkitMenuView
    {
        public BlockiverseUiToolkitMenuView(
            string screenId,
            string title,
            string purpose,
            IReadOnlyList<MenuAction> actions = null,
            IReadOnlyList<MenuDetailRow> details = null,
            IReadOnlyList<string> tags = null,
            string status = null,
            string kicker = null,
            IReadOnlyList<MenuTextInputRow> textInputs = null,
            IReadOnlyList<MenuCycleRow> cycleRows = null,
            IReadOnlyList<MenuSelectionRow> selectionRows = null,
            IReadOnlyList<MenuToggleRow> toggleRows = null,
            IReadOnlyList<MenuSliderRow> sliderRows = null,
            MenuPagingState? paging = null)
        {
            if (string.IsNullOrWhiteSpace(screenId))
                throw new ArgumentException("Menu view screen ids must be non-empty.", nameof(screenId));

            ScreenId = screenId;
            Title = title ?? screenId;
            Purpose = purpose ?? string.Empty;
            Actions = actions ?? Array.Empty<MenuAction>();
            Details = details ?? Array.Empty<MenuDetailRow>();
            Tags = tags ?? Array.Empty<string>();
            Status = status ?? string.Empty;
            Kicker = string.IsNullOrWhiteSpace(kicker) ? "Blockiverse VR" : kicker;
            TextInputs = textInputs ?? Array.Empty<MenuTextInputRow>();
            CycleRows = cycleRows ?? Array.Empty<MenuCycleRow>();
            SelectionRows = selectionRows ?? Array.Empty<MenuSelectionRow>();
            ToggleRows = toggleRows ?? Array.Empty<MenuToggleRow>();
            SliderRows = sliderRows ?? Array.Empty<MenuSliderRow>();
            Paging = paging;
        }

        public string ScreenId { get; }
        public string Title { get; }
        public string Purpose { get; }
        public IReadOnlyList<MenuAction> Actions { get; }
        public IReadOnlyList<MenuDetailRow> Details { get; }
        public IReadOnlyList<string> Tags { get; }
        public string Status { get; }
        public string Kicker { get; }
        public IReadOnlyList<MenuTextInputRow> TextInputs { get; }
        public IReadOnlyList<MenuCycleRow> CycleRows { get; }
        public IReadOnlyList<MenuSelectionRow> SelectionRows { get; }
        public IReadOnlyList<MenuToggleRow> ToggleRows { get; }
        public IReadOnlyList<MenuSliderRow> SliderRows { get; }
        public MenuPagingState? Paging { get; }
    }

    public static class BlockiverseUiToolkitMenuCatalog
    {
        public const string NewWorldNameField = "new_world.name";
        public const string NewWorldSeedField = "new_world.seed";
        public const string NewWorldGameModeField = "new_world.game_mode";
        public const string NewWorldDifficultyField = "new_world.difficulty";
        public const string NewWorldSizeField = "new_world.size";
        public const string NewWorldPresetField = "new_world.preset";
        public const string NewWorldStartingBiomeField = "new_world.starting_biome";
        public const string NewWorldTextureSetField = "new_world.texture_set";
        public const string WorldDetailsRenameField = "world_details.rename_text";
        public const string ComfortUseTeleportField = "settings_comfort.use_teleport";
        public const string ComfortSmoothTurnField = "settings_comfort.smooth_turn";
        public const string ComfortSnapTurnAroundField = "settings_comfort.snap_turn_around";
        public const string ComfortVignetteField = "settings_comfort.vignette";
        public const string ComfortLeftHandField = "settings_comfort.left_hand";
        public const string ComfortToggleToMineField = "settings_comfort.toggle_to_mine";
        public const string ComfortSnapTurnDegreesField = "settings_comfort.snap_turn_degrees";
        public const string ComfortMoveSpeedField = "settings_comfort.move_speed";
        public const string ComfortTurnSpeedField = "settings_comfort.turn_speed";
        public const string ComfortVignetteStrengthField = "settings_comfort.vignette_strength";
        public const string ComfortEyeHeightField = "settings_comfort.eye_height";
        public const string ComfortUiScaleField = "settings_comfort.ui_scale";
        public const string AudioMuteAllField = "settings_audio.mute_all";
        public const string AudioHapticsField = "settings_audio.haptics";
        public const string AudioReducedFlashField = "settings_audio.reduced_flash";
        public const string AudioReducedParticlesField = "settings_audio.reduced_particles";
        public const string AudioMasterVolumeField = "settings_audio.master_volume";
        public const string AudioEffectsVolumeField = "settings_audio.effects_volume";
        public const string AudioUiVolumeField = "settings_audio.ui_volume";
        public const string AudioWeatherVolumeField = "settings_audio.weather_volume";
        public const string AudioMusicVolumeField = "settings_audio.music_volume";
        public const string AudioHapticIntensityField = "settings_audio.haptic_intensity";
        public const string LanAddressField = "lan_multiplayer.address";
        public const string InventorySlotSelectionPrefix = "inventory.slot.";
        public const string CraftingRecipeSelectionPrefix = "crafting.recipe.";
        public const string ContainerSlotSelectionPrefix = "container.slot.";
        public const string BlockCatalogSelectionPrefix = "block_catalog.block.";

        static readonly string[] RuntimeReplacementScreens =
        {
            MenuActions.TitleScreen,
            MenuActions.NewWorldScreen,
            MenuActions.LoadWorldScreen,
            MenuActions.WorldDetailsScreen,
            MenuActions.PauseScreen,
            MenuActions.DeathScreen,
            MenuActions.ConfirmModal,
            MenuActions.SettingsScreen,
            MenuActions.ComfortSettingsScreen,
            MenuActions.AudioSettingsScreen,
            MenuActions.ControlsScreen,
            MenuActions.LanMultiplayerScreen,
            MenuActions.PlayerHubScreen,
            MenuActions.InventoryScreen,
            MenuActions.VitalsStatusScreen,
            MenuActions.CraftingScreen,
            MenuActions.ContainerScreen,
            MenuActions.StationMenuScreen,
            MenuActions.CampfireStationScreen,
            MenuActions.ClayKilnStationScreen,
            MenuActions.BellowsForgeStationScreen,
            MenuActions.PrepBoardStationScreen,
            MenuActions.MendBenchStationScreen,
            MenuActions.MapWayflagScreen,
            MenuActions.ItemDetailsPopover,
            MenuActions.RecipePinOverlay,
            MenuActions.BlockCatalogScreen,
            MenuActions.CreativeToolsScreen,
            MenuActions.ContextHubScreen,
            MenuActions.StatusHubScreen,
            MenuActions.FarmingSummaryScreen,
            MenuActions.FarmingActionPopup,
            MenuActions.AvatarStatusScreen,
            MenuActions.MetaPolicyStatusScreen,
            MenuActions.DiagnosticsScreen,
            MenuActions.NetworkCommandStatusScreen,
            MenuActions.SurvivalRejectionScreen,
            MenuActions.ControllerMappingScreen,
            MenuActions.WorldLoadingScreen,
        };

        static readonly BlockiverseUiToolkitMenuView[] TargetMenuInventory =
        {
            Informational(MenuActions.WorldLoadingScreen, "Boot / Startup Loading", "Shows startup, loading, and world-transition state while normal input is blocked."),
            Informational(MenuActions.ControllerMappingScreen, "Controller Mapping", "First-run controller reference with a clear close path before title."),
            Informational(MenuActions.TitleScreen, "Title", "Entry point for continuing, creating, loading, multiplayer, settings, and platform-safe quit."),
            Informational(MenuActions.NewWorldScreen, "New World", "Configures world name, seed, mode, difficulty, size, preset, biome, texture set, create, and cancel."),
            Informational(MenuActions.LoadWorldScreen, "Load World", "Browses saves, pages results, loads the selected save, opens details, or cancels."),
            Informational(MenuActions.WorldDetailsScreen, "World Details", "Inspects save metadata and manages play, rename, duplicate, delete, and back."),
            Informational(MenuActions.ConfirmModal, "Confirm / Error", "Reusable modal for destructive choices, terminal actions, and error/OK prompts."),
            Informational(MenuActions.LanMultiplayerScreen, "LAN Multiplayer", "Hosts, joins, stops, reports status, and links to multiplayer diagnostics."),
            Informational(MenuActions.GameplayHudScreen, "Gameplay HUD", "Shows lightweight health, vitals, mining, hotbar, and pinned survival status."),
            Informational(MenuActions.PauseScreen, "Pause", "Pauses session flow for resume, save, mode switch, creative tools, settings, title return, and quit."),
            Informational(MenuActions.SettingsScreen, "Settings Hub", "Routes to comfort, audio/feedback, controls, and accessibility preferences."),
            Informational(MenuActions.ComfortSettingsScreen, "Comfort", "Controls locomotion, vignette, turn behavior, dominant hand, height, and UI scale."),
            Informational(MenuActions.AudioSettingsScreen, "Audio / Feedback", "Controls volume, haptics, reduced effects, subtitles, and feedback preferences."),
            Informational(MenuActions.ControlsScreen, "Controls", "Shows controller reference, remap/reset affordances, and input status."),
            Informational(MenuActions.InventoryScreen, "Inventory", "Manages inventory, equip/use/drop/split/move, hotbar assignment, and item details."),
            Informational(MenuActions.VitalsStatusScreen, "Vitals / Status", "Shows health, hunger, thirst, stamina, effects, and survival state."),
            Informational(MenuActions.CraftingScreen, "Crafting", "Browses recipes, craftability, ingredients, craft count, pinning, craft, and cancel."),
            Informational(MenuActions.ContainerScreen, "Container", "Transfers items between player and world containers."),
            Informational(MenuActions.StationMenuScreen, "Station Shell", "Common input, fuel, output, progress, recipe, and transfer frame for stations."),
            Informational(MenuActions.CampfireStationScreen, "Campfire", "Station variant for cooking, light, and fuel handling."),
            Informational(MenuActions.ClayKilnStationScreen, "Clay Kiln", "Station variant for clay and ceramic smelting."),
            Informational(MenuActions.BellowsForgeStationScreen, "Bellows Forge", "Station variant for forge and smelting recipes."),
            Informational(MenuActions.PrepBoardStationScreen, "Prep Board", "Station variant for food preparation."),
            Informational(MenuActions.MendBenchStationScreen, "Mend Bench", "Station variant for repair and mending."),
            Informational(MenuActions.MapWayflagScreen, "Map / Wayflag", "Shows map, marker, wayflag, waypoint, and close actions."),
            Informational(MenuActions.ItemDetailsPopover, "Item Details Popover", "Reusable item stats, actions, and context hints."),
            Informational(MenuActions.RecipePinOverlay, "Recipe Pin Overlay", "Tracks pinned recipe ingredients while playing."),
            Informational(MenuActions.BlockCatalogScreen, "Block Menu / Block Catalog", "Searches, filters, pages, and selects creative block entries."),
            Informational(MenuActions.CreativeToolsScreen, "Creative Tools", "Manages region edit, copy/paste, undo/redo, structures, time, and weather."),
            Informational(MenuActions.DeathScreen, "Death", "Blocks gameplay until respawn at bedroll/world spawn or return to title."),
            Informational(MenuActions.PlayerHubScreen, "Player Hub", "Fast access to inventory, vitals, crafting, context, and status."),
            Informational(MenuActions.ContextHubScreen, "Context Hub", "Groups container, farming, station, and creative context actions."),
            Informational(MenuActions.FarmingSummaryScreen, "Farming Summary", "Summarizes crop, soil, seed, and ready-state context."),
            Informational(MenuActions.FarmingActionPopup, "Farming Action Popup", "Presents till, plant, harvest, and close actions for farming targets."),
            Informational(MenuActions.AvatarStatusScreen, "Avatar Status", "Shows local/remote avatar session and fallback state."),
            Informational(MenuActions.MetaPolicyStatusScreen, "Meta Policy Status", "Shows platform, social, and avatar policy decisions."),
            Informational(MenuActions.DiagnosticsScreen, "Diagnostics", "Shows trace, networking, scene/session, and performance evidence."),
            Informational(MenuActions.NetworkCommandStatusScreen, "Network Command Status", "Shows accepted, duplicate, and rejected network command status."),
            Informational(MenuActions.SurvivalRejectionScreen, "Survival Rejection", "Blocks rejected survival/network commands with retry, dismiss, and open hinted panel."),
        };

        public static IReadOnlyList<BlockiverseUiToolkitMenuView> AllTargetMenus => TargetMenuInventory;

        public static bool SupportsRuntimeReplacement(string screenId)
        {
            return RuntimeReplacementScreens.Any(id => string.Equals(id, screenId, StringComparison.Ordinal));
        }

        public static BlockiverseUiToolkitMenuView CreateRuntimeView(
            string screenId,
            bool hasLatestSave,
            bool hasAnySave,
            bool canQuit,
            bool canToggleMode,
            bool canOpenCreativeTools,
            bool hasBedrollSpawn,
            string titleStatus,
            string pauseStatus,
            string confirmPrompt,
            IReadOnlyList<MenuAction> confirmActions)
        {
            return screenId switch
            {
                MenuActions.TitleScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.TitleScreen,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleBlockiverse),
                    "Continue a save, create a new world, join LAN, or adjust settings.",
                    MenuActions.Title(hasLatestSave, hasAnySave, canQuit),
                    new[]
                    {
                        new MenuDetailRow("Latest save", hasLatestSave ? "Available" : "None yet"),
                        new MenuDetailRow("Saved worlds", hasAnySave ? "Available" : "No saves"),
                        new MenuDetailRow("Runtime", "Quest VR / LAN ready"),
                    },
                    new[] { "Main", "Worlds", "Multiplayer" },
                    titleStatus),

                MenuActions.PauseScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.PauseScreen,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitlePaused),
                    "Pause gameplay, save progress, adjust settings, or leave the current session.",
                    MenuActions.PauseMenu(canToggleMode, canOpenCreativeTools, canQuit),
                    new[]
                    {
                        new MenuDetailRow("World input", "Blocked while paused"),
                        new MenuDetailRow("Save", "Manual save available"),
                        new MenuDetailRow("Creative tools", canOpenCreativeTools ? "Available" : "Unavailable"),
                    },
                    new[] { "Session", "Settings", "Safety" },
                    pauseStatus),

                MenuActions.DeathScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.DeathScreen,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleDeath),
                    "Choose a respawn point before gameplay resumes.",
                    MenuActions.Death(hasBedrollSpawn),
                    new[]
                    {
                        new MenuDetailRow("Bedroll", hasBedrollSpawn ? "Available" : "Not set"),
                        new MenuDetailRow("World spawn", "Available"),
                    },
                    new[] { "Respawn", "Survival" }),

                MenuActions.ConfirmModal => new BlockiverseUiToolkitMenuView(
                    MenuActions.ConfirmModal,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleConfirm),
                    string.IsNullOrWhiteSpace(confirmPrompt) ? "Confirm the requested action." : confirmPrompt,
                    confirmActions ?? MenuActions.Confirm(),
                    new[]
                    {
                        new MenuDetailRow("Input", "Modal priority"),
                        new MenuDetailRow("Underlying screen", "Input blocked"),
                    },
                    new[] { "Confirm", "Modal" }),

                MenuActions.SettingsScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.SettingsScreen,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleSettings),
                    "Adjust comfort, audio, feedback, and controls.",
                    MenuActions.Settings,
                    new[]
                    {
                        new MenuDetailRow("Comfort", "Locomotion and vignette"),
                        new MenuDetailRow("Audio", "Volume, haptics, reduced effects"),
                        new MenuDetailRow("Controls", "Controller reference"),
                    },
                    new[] { "Comfort", "Audio", "Controls" }),

                MenuActions.ControllerMappingScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.ControllerMappingScreen,
                    "Controller Mapping",
                    "Review core controller actions before entering the title menu.",
                    new[] { new MenuAction(MenuActions.ControllerMappingClose, "Close") },
                    new[]
                    {
                        new MenuDetailRow("Menu", "Pause / back"),
                        new MenuDetailRow("Support grip", "Quick block menu"),
                        new MenuDetailRow("Dominant trigger", "Mine / select"),
                        new MenuDetailRow("Either stick up", "Teleport aim"),
                    },
                    new[] { "First run", "Controls" }),

                MenuActions.WorldLoadingScreen => new BlockiverseUiToolkitMenuView(
                    MenuActions.WorldLoadingScreen,
                    "Loading World",
                    "Preparing the world and blocking normal input until the session is ready.",
                    Array.Empty<MenuAction>(),
                    new[]
                    {
                        new MenuDetailRow("World", "Generating or loading"),
                        new MenuDetailRow("Input", "Temporarily blocked"),
                    },
                    new[] { "Loading" }),

                _ => Informational(screenId, screenId, "This route is not yet mapped to the UI Toolkit runtime replacement surface."),
            };
        }

        public static BlockiverseUiToolkitMenuView CreateNewWorldView(NewWorldConfig config, string status)
        {
            if (config == null)
                config = new NewWorldConfig();

            return new BlockiverseUiToolkitMenuView(
                MenuActions.NewWorldScreen,
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldTitle),
                "Configure the world before generation.",
                new[]
                {
                    new MenuAction(MenuActions.NewWorldCreate, BlockiverseLocalization.Keys.NewWorldCreate, "Create World"),
                    new MenuAction(MenuActions.NewWorldCancel, BlockiverseLocalization.Keys.ConfirmCancel, "Cancel"),
                },
                new[]
                {
                    new MenuDetailRow("Seed value", config.Seed.ToString()),
                    new MenuDetailRow("Generation", config.WorldPreset),
                },
                new[] { "World setup", "Seeded generation" },
                status,
                textInputs: new[]
                {
                    new MenuTextInputRow(NewWorldNameField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldName), config.Name, NewWorldConfig.DefaultName),
                    new MenuTextInputRow(NewWorldSeedField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldSeed), config.SeedText, "0"),
                },
                cycleRows: new[]
                {
                    new MenuCycleRow(NewWorldGameModeField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldGameMode), Display(config.GameMode)),
                    new MenuCycleRow(NewWorldDifficultyField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldDifficulty), Display(config.Difficulty)),
                    new MenuCycleRow(NewWorldSizeField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldSize), Display(config.WorldSize)),
                    new MenuCycleRow(NewWorldPresetField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldPreset), Display(config.WorldPreset)),
                    new MenuCycleRow(NewWorldStartingBiomeField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldStartingBiome), Display(config.StartingBiome)),
                    new MenuCycleRow(NewWorldTextureSetField, BlockiverseLocalization.Text(BlockiverseLocalization.Keys.NewWorldTextureSet), Display(config.TextureSet)),
                });
        }

        public static BlockiverseUiToolkitMenuView CreateLoadWorldView(
            IReadOnlyList<WorldSaveSummary> pageSaves,
            WorldSaveSummary? selectedSave,
            int pageIndex,
            int pageCount,
            string status)
        {
            pageSaves ??= Array.Empty<WorldSaveSummary>();
            var rows = new MenuSelectionRow[pageSaves.Count];
            for (int i = 0; i < pageSaves.Count; i++)
            {
                WorldSaveSummary save = pageSaves[i];
                bool selected = selectedSave.HasValue &&
                    string.Equals(selectedSave.Value.Name, save.Name, StringComparison.OrdinalIgnoreCase);
                rows[i] = new MenuSelectionRow(
                    save.Name,
                    BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LoadWorldEntry, save.Name, save.DayCount),
                    $"{Display(save.GameMode)} / {Display(save.Difficulty)} / Seed {save.Seed}",
                    selected);
            }

            string selectionText = selectedSave.HasValue
                ? selectedSave.Value.Name
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldNoSaveSelected);

            return new BlockiverseUiToolkitMenuView(
                MenuActions.LoadWorldScreen,
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldTitle),
                "Select a saved world to load or inspect.",
                new[]
                {
                    new MenuAction(MenuActions.LoadWorldLoad, BlockiverseLocalization.Keys.LoadWorldLoad, "Load World"),
                    new MenuAction(MenuActions.LoadWorldDetails, BlockiverseLocalization.Keys.LoadWorldDetails, "Details"),
                    new MenuAction(MenuActions.LoadWorldCancel, BlockiverseLocalization.Keys.ConfirmCancel, "Cancel"),
                },
                new[]
                {
                    new MenuDetailRow("Selected", selectionText),
                    new MenuDetailRow("Visible saves", pageSaves.Count.ToString()),
                },
                new[] { "Saves", "Worlds" },
                status,
                selectionRows: rows,
                paging: new MenuPagingState(pageIndex, pageCount));
        }

        public static BlockiverseUiToolkitMenuView CreateWorldDetailsView(WorldSaveSummary? save, string renameText)
        {
            if (!save.HasValue)
            {
                return new BlockiverseUiToolkitMenuView(
                    MenuActions.WorldDetailsScreen,
                    BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleWorldDetails),
                    "No save is selected.",
                    new[] { new MenuAction(MenuActions.WorldDetailsBack, BlockiverseLocalization.Keys.WorldDetailsBack, "Back") },
                    new[] { new MenuDetailRow("Selected", "None") },
                    new[] { "Saves" });
            }

            WorldSaveSummary value = save.Value;
            return new BlockiverseUiToolkitMenuView(
                MenuActions.WorldDetailsScreen,
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.TitleWorldDetails),
                "Review save metadata and manage the selected world.",
                MenuActions.WorldDetails,
                new[]
                {
                    new MenuDetailRow("Name", value.Name),
                    new MenuDetailRow("Metadata", BlockiverseWorldDetailsPanel.BuildMetadataText(value)),
                },
                new[] { "Save management", "Destructive actions confirm" },
                textInputs: new[]
                {
                    new MenuTextInputRow(
                        WorldDetailsRenameField,
                        BlockiverseLocalization.Text(BlockiverseLocalization.Keys.WorldDetailsName),
                        string.IsNullOrWhiteSpace(renameText) ? value.Name : renameText,
                        value.Name),
                });
        }

        public static BlockiverseUiToolkitMenuView CreateComfortView(BlockiverseComfortSettings settings)
        {
            bool hasSettings = settings != null;
            return new BlockiverseUiToolkitMenuView(
                MenuActions.ComfortSettingsScreen,
                "Comfort",
                "Tune locomotion, turning, vignette, hand preference, height, and menu scale.",
                new[] { new MenuAction(MenuActions.ComfortSettingsClose, BlockiverseLocalization.Keys.SettingsClose, "Close") },
                new[]
                {
                    new MenuDetailRow("Settings source", hasSettings ? "Live rig settings" : "Not found"),
                    new MenuDetailRow("Locomotion", hasSettings ? settings.LocomotionMode.ToString() : "Unavailable"),
                },
                new[] { "XR comfort", "Runtime settings" },
                hasSettings ? string.Empty : "Comfort settings are not available in this scene.",
                toggleRows: hasSettings
                    ? new[]
                    {
                        new MenuToggleRow(ComfortUseTeleportField, "Teleport locomotion", settings.LocomotionMode == BlockiverseLocomotionMode.Teleport, "Off uses glide locomotion."),
                        new MenuToggleRow(ComfortSmoothTurnField, "Smooth turning", settings.SmoothTurnEnabled),
                        new MenuToggleRow(ComfortSnapTurnAroundField, "Snap turn around", settings.SnapTurnAroundEnabled),
                        new MenuToggleRow(ComfortVignetteField, "Comfort vignette", settings.VignetteEnabled),
                        new MenuToggleRow(ComfortLeftHandField, "Left dominant hand", settings.DominantHand == BlockiverseControllerRole.Left),
                        new MenuToggleRow(ComfortToggleToMineField, "Toggle to mine", settings.ToggleToMineEnabled),
                    }
                    : Array.Empty<MenuToggleRow>(),
                sliderRows: hasSettings
                    ? new[]
                    {
                        new MenuSliderRow(ComfortSnapTurnDegreesField, "Snap turn degrees", settings.SnapTurnDegrees, 15.0f, 90.0f, $"{settings.SnapTurnDegrees:0} deg"),
                        new MenuSliderRow(ComfortMoveSpeedField, "Move speed", settings.ContinuousMoveSpeed, 0.5f, 4.0f, $"{settings.ContinuousMoveSpeed:0.0} m/s"),
                        new MenuSliderRow(ComfortTurnSpeedField, "Turn speed", settings.ContinuousTurnSpeed, 30.0f, 180.0f, $"{settings.ContinuousTurnSpeed:0} deg/s"),
                        new MenuSliderRow(ComfortVignetteStrengthField, "Vignette strength", settings.VignetteStrength, 0.0f, 1.0f, Percent(settings.VignetteStrength)),
                        new MenuSliderRow(ComfortEyeHeightField, "Standing eye height", settings.StandingEyeHeight, 1.0f, 2.2f, $"{settings.StandingEyeHeight:0.00} m"),
                        new MenuSliderRow(ComfortUiScaleField, "Menu scale", settings.UiScale, 0.85f, 1.35f, $"{settings.UiScale:0.00}x"),
                    }
                    : Array.Empty<MenuSliderRow>());
        }

        public static BlockiverseUiToolkitMenuView CreateAudioView(BlockiverseFeedbackSettings settings)
        {
            bool hasSettings = settings != null;
            return new BlockiverseUiToolkitMenuView(
                MenuActions.AudioSettingsScreen,
                "Audio / Feedback",
                "Tune volume, haptics, and reduced-effects preferences.",
                new[] { new MenuAction(MenuActions.AudioSettingsClose, BlockiverseLocalization.Keys.SettingsClose, "Close") },
                new[]
                {
                    new MenuDetailRow("Settings source", hasSettings ? "Live rig settings" : "Not found"),
                    new MenuDetailRow("Resolved UI volume", hasSettings ? Percent(settings.ResolveVolume(BlockiverseAudioCategory.Ui)) : "Unavailable"),
                },
                new[] { "Audio", "Haptics", "Accessibility" },
                hasSettings ? string.Empty : "Audio settings are not available in this scene.",
                toggleRows: hasSettings
                    ? new[]
                    {
                        new MenuToggleRow(AudioMuteAllField, "Mute all", settings.MuteAll),
                        new MenuToggleRow(AudioHapticsField, "Haptics", settings.HapticsEnabled),
                        new MenuToggleRow(AudioReducedFlashField, "Reduced flash", settings.ReducedFlash),
                        new MenuToggleRow(AudioReducedParticlesField, "Reduced particles", settings.ReducedParticles),
                    }
                    : Array.Empty<MenuToggleRow>(),
                sliderRows: hasSettings
                    ? new[]
                    {
                        new MenuSliderRow(AudioMasterVolumeField, "Master volume", settings.MasterVolume, 0.0f, 1.0f, Percent(settings.MasterVolume)),
                        new MenuSliderRow(AudioEffectsVolumeField, "Effects volume", settings.EffectsVolume, 0.0f, 1.0f, Percent(settings.EffectsVolume)),
                        new MenuSliderRow(AudioUiVolumeField, "UI volume", settings.UiVolume, 0.0f, 1.0f, Percent(settings.UiVolume)),
                        new MenuSliderRow(AudioWeatherVolumeField, "Weather volume", settings.WeatherVolume, 0.0f, 1.0f, Percent(settings.WeatherVolume)),
                        new MenuSliderRow(AudioMusicVolumeField, "Music volume", settings.MusicVolume, 0.0f, 1.0f, Percent(settings.MusicVolume)),
                        new MenuSliderRow(AudioHapticIntensityField, "Haptic intensity", settings.HapticIntensity, 0.0f, 1.0f, Percent(settings.HapticIntensity)),
                    }
                    : Array.Empty<MenuSliderRow>());
        }

        public static BlockiverseUiToolkitMenuView CreateControlsView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.ControlsScreen,
                "Controls",
                "Review the controller reference for core menu and gameplay input.",
                new[] { new MenuAction(MenuActions.ControlsClose, BlockiverseLocalization.Keys.SettingsClose, "Close") },
                new[]
                {
                    new MenuDetailRow("Menu", "Pause, back, close current menu"),
                    new MenuDetailRow("Support grip", "Quick block catalog"),
                    new MenuDetailRow("Dominant trigger", "Mine, place, select"),
                    new MenuDetailRow("Either stick up", "Teleport aim when teleport locomotion is enabled"),
                    new MenuDetailRow("Dominant primary", "Jump while glide locomotion is enabled"),
                },
                new[] { "Controller reference", "XR input" });
        }

        public static BlockiverseUiToolkitMenuView CreateLanView(
            string address,
            string status,
            bool canStart,
            bool canStop)
        {
            var actions = new List<MenuAction>(4);
            if (canStart)
            {
                actions.Add(new MenuAction(MenuActions.LanMultiplayerHost, "Host"));
                actions.Add(new MenuAction(MenuActions.LanMultiplayerJoin, "Join"));
            }
            if (canStop)
                actions.Add(new MenuAction(MenuActions.LanMultiplayerStop, "Stop"));
            actions.Add(new MenuAction(MenuActions.LanMultiplayerClose, BlockiverseLocalization.Keys.SettingsClose, "Close"));

            return new BlockiverseUiToolkitMenuView(
                MenuActions.LanMultiplayerScreen,
                "LAN Multiplayer",
                "Host or join a local network session.",
                actions,
                new[]
                {
                    new MenuDetailRow("Session", canStop ? "Active or stopping" : "Ready"),
                    new MenuDetailRow("Default address", BlockiverseNetworkConfig.DefaultAddress),
                },
                new[] { "LAN", "Multiplayer" },
                status,
                textInputs: new[]
                {
                    new MenuTextInputRow(
                        LanAddressField,
                        BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanJoinAddressPlaceholder),
                        string.IsNullOrWhiteSpace(address) ? BlockiverseNetworkConfig.DefaultAddress : address,
                        BlockiverseNetworkConfig.DefaultAddress),
                });
        }

        public static BlockiverseUiToolkitMenuView CreatePlayerHubView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.PlayerHubScreen,
                "Player Hub",
                "Open survival player menus without leaving the paused session.",
                new[]
                {
                    new MenuAction(MenuActions.PlayerHubInventory, "Inventory"),
                    new MenuAction(MenuActions.PlayerHubVitals, "Vitals"),
                    new MenuAction(MenuActions.PlayerHubCrafting, "Crafting"),
                    new MenuAction(MenuActions.PlayerHubContext, "Context"),
                    new MenuAction(MenuActions.PlayerHubStatus, "Status"),
                    new MenuAction(MenuActions.PlayerHubRecipePin, "Pinned Recipe"),
                    new MenuAction(MenuActions.PlayerHubClose, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Inventory", "Slots, hotbar, and held item"),
                    new MenuDetailRow("Vitals", "Health, hunger, thirst, and stamina"),
                    new MenuDetailRow("Crafting", "Instant recipes and repair"),
                    new MenuDetailRow("Context", "Container, farming, station, map, and creative flows"),
                    new MenuDetailRow("Status", "Avatar, Meta policy, diagnostics, and network command state"),
                },
                new[] { "Player", "Survival" });
        }

        public static BlockiverseUiToolkitMenuView CreateInventoryView(
            Inventory inventory,
            ItemRegistry registry,
            int selectedHotbarSlot,
            int firstVisibleSlot,
            int visibleSlotCount)
        {
            registry ??= ItemRegistry.Default;
            visibleSlotCount = Math.Max(1, visibleSlotCount);
            int slotCount = inventory != null ? inventory.SlotCount : visibleSlotCount;
            int pageCount = Math.Max(1, (slotCount + visibleSlotCount - 1) / visibleSlotCount);
            int pageIndex = Mathf.Clamp(firstVisibleSlot / visibleSlotCount, 0, pageCount - 1);
            int first = pageIndex * visibleSlotCount;
            int rowCount = Math.Min(visibleSlotCount, Math.Max(0, slotCount - first));
            var rows = new MenuSelectionRow[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                int slotIndex = first + i;
                ItemStack stack = inventory != null ? inventory.GetSlot(slotIndex) : ItemStack.Empty;
                bool hotbar = inventory != null && slotIndex < inventory.HotbarSlotCount;
                rows[i] = new MenuSelectionRow(
                    InventorySlotSelectionPrefix + slotIndex,
                    $"{slotIndex + 1}. {FormatStack(stack, registry)}",
                    hotbar ? "Hotbar slot" : "Backpack slot",
                    hotbar && slotIndex == selectedHotbarSlot);
            }

            return new BlockiverseUiToolkitMenuView(
                MenuActions.InventoryScreen,
                "Inventory",
                "Select a hotbar slot or swap the selected hotbar slot with a backpack slot.",
                new[]
                {
                    new MenuAction(MenuActions.InventoryItemDetails, "Item Details"),
                    new MenuAction(MenuActions.InventoryBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Slots", slotCount.ToString()),
                    new MenuDetailRow("Selected hotbar", (selectedHotbarSlot + 1).ToString()),
                },
                new[] { "Inventory", "Hotbar" },
                selectionRows: rows,
                paging: new MenuPagingState(pageIndex, pageCount));
        }

        public static BlockiverseUiToolkitMenuView CreateItemDetailsView(
            ItemStack stack,
            ItemRegistry registry)
        {
            registry ??= ItemRegistry.Default;
            string name = stack.IsEmpty
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty)
                : registry.Get(stack.ItemId).Name;

            var details = new List<MenuDetailRow>
            {
                new("Item", name),
                new("Count", stack.IsEmpty ? "0" : stack.Count.ToString()),
                new("Canonical id", stack.IsEmpty ? "none" : stack.ItemId.Value),
            };

            if (!stack.IsEmpty && stack.Durability >= 0)
                details.Add(new MenuDetailRow("Durability", stack.Durability.ToString()));

            return new BlockiverseUiToolkitMenuView(
                MenuActions.ItemDetailsPopover,
                "Item Details",
                "Review the selected inventory item and its canonical runtime identity.",
                new[] { new MenuAction(MenuActions.ItemDetailsBack, "Back") },
                details,
                new[] { "Inventory", "Details" });
        }

        public static BlockiverseUiToolkitMenuView CreateVitalsView(
            PlayerVitals playerVitals,
            SurvivalVitals survivalVitals,
            string status)
        {
            string health = playerVitals != null
                ? $"{playerVitals.CurrentHealth} / {playerVitals.MaxHealth}"
                : "Unavailable";
            string state = playerVitals == null
                ? "Unavailable"
                : playerVitals.IsDead
                    ? "Down"
                    : playerVitals.CurrentHealth <= playerVitals.MaxHealth / 4
                        ? "Critical"
                        : "Stable";

            var details = new List<MenuDetailRow>
            {
                new("Health", health),
                new("State", state),
            };

            if (survivalVitals != null)
            {
                details.Add(new MenuDetailRow("Hunger", survivalVitals.Hunger.ToString()));
                details.Add(new MenuDetailRow("Thirst", survivalVitals.Thirst.ToString()));
                details.Add(new MenuDetailRow("Stamina", survivalVitals.Stamina.ToString()));
            }

            return new BlockiverseUiToolkitMenuView(
                MenuActions.VitalsStatusScreen,
                "Vitals / Status",
                "Review survival vitals and recent player feedback.",
                new[] { new MenuAction(MenuActions.VitalsBack, "Back") },
                details,
                new[] { "Survival", "Status" },
                status);
        }

        public static BlockiverseUiToolkitMenuView CreateCraftingView(
            CraftingRecipeBook recipeBook,
            Inventory inventory,
            ItemRegistry registry,
            int pageIndex,
            int visibleRecipeCount,
            int selectedRecipeIndex = -1,
            int pinnedRecipeIndex = -1)
        {
            registry ??= ItemRegistry.Default;
            visibleRecipeCount = Math.Max(1, visibleRecipeCount);
            List<CraftingRecipe> recipes = InstantRecipes(recipeBook);
            int pageCount = Math.Max(1, (recipes.Count + visibleRecipeCount - 1) / visibleRecipeCount);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            int first = pageIndex * visibleRecipeCount;
            int rowCount = Math.Min(visibleRecipeCount, Math.Max(0, recipes.Count - first));
            var rows = new MenuSelectionRow[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                int recipeIndex = first + i;
                CraftingRecipe recipe = recipes[recipeIndex];
                rows[i] = new MenuSelectionRow(
                    CraftingRecipeSelectionPrefix + recipeIndex,
                    FormatStack(recipe.Output, registry),
                    FormatIngredients(recipe, registry),
                    recipeIndex == selectedRecipeIndex);
            }

            string selected = TryGetInstantRecipe(recipeBook, selectedRecipeIndex, out CraftingRecipe selectedRecipe)
                ? FormatStack(selectedRecipe.Output, registry)
                : "None";
            string pinned = TryGetInstantRecipe(recipeBook, pinnedRecipeIndex, out CraftingRecipe pinnedRecipe)
                ? FormatStack(pinnedRecipe.Output, registry)
                : "None";

            return new BlockiverseUiToolkitMenuView(
                MenuActions.CraftingScreen,
                "Crafting",
                "Select an instant recipe, craft it, pin its ingredients, or repair the held tool at a mend bench.",
                new[]
                {
                    new MenuAction(MenuActions.CraftingCraftSelected, "Craft Selected"),
                    new MenuAction(MenuActions.CraftingPinSelected, "Pin Recipe"),
                    new MenuAction(MenuActions.CraftingRepair, "Repair Held Tool"),
                    new MenuAction(MenuActions.CraftingBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Recipes", recipes.Count.ToString()),
                    new MenuDetailRow("Selected recipe", selected),
                    new MenuDetailRow("Pinned recipe", pinned),
                    new MenuDetailRow("Inventory", inventory != null ? $"{inventory.SlotCount} slots" : "Unavailable"),
                },
                new[] { "Crafting", "Survival" },
                selectionRows: rows,
                paging: new MenuPagingState(pageIndex, pageCount));
        }

        public static BlockiverseUiToolkitMenuView CreateRecipePinView(
            CraftingRecipe recipe,
            Inventory inventory,
            ItemRegistry registry)
        {
            registry ??= ItemRegistry.Default;
            if (recipe == null)
            {
                return new BlockiverseUiToolkitMenuView(
                    MenuActions.RecipePinOverlay,
                    "Pinned Recipe",
                    "No recipe is pinned.",
                    new[] { new MenuAction(MenuActions.RecipePinBack, "Back") },
                    new[] { new MenuDetailRow("Pinned recipe", "None") },
                    new[] { "Crafting", "HUD overlay" });
            }

            var details = new List<MenuDetailRow>
            {
                new("Pinned recipe", FormatStack(recipe.Output, registry)),
                new("Ingredients", FormatIngredients(recipe, registry)),
                new("Station", BlockiverseLocalization.DisplayName(recipe.RequiredStation)),
            };

            foreach (ItemStack ingredient in recipe.Ingredients)
            {
                int available = inventory != null ? inventory.CountOf(ingredient.ItemId) : 0;
                details.Add(new MenuDetailRow(
                    registry.Get(ingredient.ItemId).Name,
                    $"{available} / {ingredient.Count}"));
            }

            return new BlockiverseUiToolkitMenuView(
                MenuActions.RecipePinOverlay,
                "Pinned Recipe",
                "Track pinned ingredients while gathering or crafting.",
                new[]
                {
                    new MenuAction(MenuActions.RecipePinClear, "Clear Pin"),
                    new MenuAction(MenuActions.RecipePinBack, "Back"),
                },
                details,
                new[] { "Crafting", "HUD overlay" });
        }

        public static BlockiverseUiToolkitMenuView CreateContainerView(
            Inventory crateInventory,
            ItemRegistry registry,
            int firstVisibleSlot,
            int visibleSlotCount)
        {
            registry ??= ItemRegistry.Default;
            visibleSlotCount = Math.Max(1, visibleSlotCount);
            int slotCount = crateInventory != null ? crateInventory.SlotCount : visibleSlotCount;
            int pageCount = Math.Max(1, (slotCount + visibleSlotCount - 1) / visibleSlotCount);
            int pageIndex = Mathf.Clamp(firstVisibleSlot / visibleSlotCount, 0, pageCount - 1);
            int first = pageIndex * visibleSlotCount;
            int rowCount = Math.Min(visibleSlotCount, Math.Max(0, slotCount - first));
            var rows = new MenuSelectionRow[rowCount];

            for (int i = 0; i < rowCount; i++)
            {
                int slotIndex = first + i;
                ItemStack stack = crateInventory != null ? crateInventory.GetSlot(slotIndex) : ItemStack.Empty;
                rows[i] = new MenuSelectionRow(
                    ContainerSlotSelectionPrefix + slotIndex,
                    $"{slotIndex + 1}. {FormatStack(stack, registry)}",
                    "Select to withdraw this slot into the player inventory.",
                    false);
            }

            return new BlockiverseUiToolkitMenuView(
                MenuActions.ContainerScreen,
                "Container",
                "Deposit the held hotbar stack or withdraw a shared crate slot.",
                new[]
                {
                    new MenuAction(MenuActions.ContainerDepositHeld, "Deposit Held"),
                    new MenuAction(MenuActions.ContainerBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Shared crate", crateInventory != null ? "Available" : "Unavailable"),
                    new MenuDetailRow("Slots", slotCount.ToString()),
                },
                new[] { "Container", "Co-op" },
                selectionRows: rows,
                paging: new MenuPagingState(pageIndex, pageCount));
        }

        public static BlockiverseUiToolkitMenuView CreateStationView(
            SmeltingStationModel station,
            string status,
            ItemRegistry registry)
        {
            registry ??= ItemRegistry.Default;
            if (station == null)
            {
                return new BlockiverseUiToolkitMenuView(
                    MenuActions.StationMenuScreen,
                    "Station",
                    "No timed station is currently open.",
                    new[] { new MenuAction(MenuActions.StationBack, "Back") },
                    new[] { new MenuDetailRow("Station", "Unavailable") },
                    new[] { "Station" });
            }

            var details = new List<MenuDetailRow>
            {
                new("Station", BlockiverseLocalization.DisplayName(station.StationType)),
                new("State", station.IsActive ? "Active" : "Idle"),
                new("Progress", station.RequiredTicks > 0 ? $"{station.ProgressTicks} / {station.RequiredTicks} ticks" : "Idle"),
                new("Fuel", FormatStack(station.Fuel, registry)),
                new("Output", FormatStack(station.Output, registry)),
            };

            for (int i = 0; i < station.InputSlotCount; i++)
                details.Add(new MenuDetailRow($"Input {i + 1}", FormatStack(station.GetInput(i), registry)));

            return new BlockiverseUiToolkitMenuView(
                MenuActions.StationMenuScreen,
                BlockiverseLocalization.DisplayName(station.StationType),
                "Manage station input, fuel, output, and progress.",
                new[]
                {
                    new MenuAction(MenuActions.StationDepositInput, "Add Input"),
                    new MenuAction(MenuActions.StationDepositFuel, "Add Fuel"),
                    new MenuAction(MenuActions.StationCollectOutput, "Collect"),
                    new MenuAction(MenuActions.StationWithdrawInput, "Take Input"),
                    new MenuAction(MenuActions.StationWithdrawFuel, "Take Fuel"),
                    new MenuAction(MenuActions.StationBack, "Back"),
                },
                details,
                new[] { "Station", "Timed crafting" },
                status);
        }

        public static BlockiverseUiToolkitMenuView CreateStationPlaceholderView(string screenId, CraftingStation station)
        {
            return new BlockiverseUiToolkitMenuView(
                screenId,
                BlockiverseLocalization.DisplayName(station),
                "This station route is present for ruleset parity. Runtime behavior uses crafting proximity or the timed station shell when the placed block supports a station model.",
                new[] { new MenuAction(MenuActions.StationBack, "Back") },
                new[]
                {
                    new MenuDetailRow("Station", BlockiverseLocalization.DisplayName(station)),
                    new MenuDetailRow("Runtime state", "No open station model"),
                },
                new[] { "Station", "Ruleset route" });
        }

        public static BlockiverseUiToolkitMenuView CreateContextHubView(bool hasContainer, bool hasStation, bool canUseCreativeTools)
        {
            var actions = new List<MenuAction>
            {
                new(MenuActions.ContextHubContainer, "Container"),
                new(MenuActions.ContextHubStation, "Station"),
                new(MenuActions.ContextHubFarming, "Farming"),
                new(MenuActions.ContextHubMap, "Map / Wayflag"),
            };
            if (canUseCreativeTools)
                actions.Add(new MenuAction(MenuActions.ContextHubCreativeTools, "Creative Tools"));
            actions.Add(new MenuAction(MenuActions.ContextHubBack, "Back"));

            return new BlockiverseUiToolkitMenuView(
                MenuActions.ContextHubScreen,
                "Context Hub",
                "Open nearby or situational world interaction menus.",
                actions,
                new[]
                {
                    new MenuDetailRow("Container", hasContainer ? "Shared crate available" : "No container panel found"),
                    new MenuDetailRow("Station", hasStation ? "Open station available" : "No open station"),
                    new MenuDetailRow("Farming", "Context actions route is available"),
                    new MenuDetailRow("Map", "Wayflag route is available"),
                },
                new[] { "Context", "World" });
        }

        public static BlockiverseUiToolkitMenuView CreateStatusHubView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.StatusHubScreen,
                "Status Hub",
                "Inspect player, avatar, platform, diagnostics, and network-command status.",
                new[]
                {
                    new MenuAction(MenuActions.StatusHubVitals, "Vitals"),
                    new MenuAction(MenuActions.StatusHubAvatar, "Avatar"),
                    new MenuAction(MenuActions.StatusHubMetaPolicy, "Meta Policy"),
                    new MenuAction(MenuActions.StatusHubDiagnostics, "Diagnostics"),
                    new MenuAction(MenuActions.StatusHubNetwork, "Network Commands"),
                    new MenuAction(MenuActions.StatusHubBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Player", "Vitals and survival status"),
                    new MenuDetailRow("Platform", "Avatar and Meta policy state"),
                    new MenuDetailRow("Diagnostics", "Runtime and network evidence"),
                },
                new[] { "Status", "Diagnostics" });
        }

        public static BlockiverseUiToolkitMenuView CreateBlockCatalogView(
            CreativeCatalog catalog,
            BlockRegistry registry,
            CreativeHotbar hotbar,
            int categoryIndex,
            int pageIndex,
            int visibleCount)
        {
            registry ??= BlockRegistry.Default;
            catalog ??= CreativeCatalog.CreateDefault(registry);
            visibleCount = Math.Max(1, visibleCount);
            CreativeCatalogCategory[] categories = CatalogCategories();
            categoryIndex = Mathf.Clamp(categoryIndex, 0, categories.Length - 1);
            CreativeCatalogCategory category = categories[categoryIndex];
            List<BlockId> blocks = catalog.InCategory(category).Select(entry => entry.BlockId).ToList();
            int pageCount = Math.Max(1, (blocks.Count + visibleCount - 1) / visibleCount);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            var rows = blocks
                .Skip(pageIndex * visibleCount)
                .Take(visibleCount)
                .Select(blockId => new MenuSelectionRow(
                    BlockCatalogSelectionPrefix + blockId.Value,
                    registry.Get(blockId).Name,
                    blockId.Value.ToString(),
                    hotbar != null && hotbar.SelectedBlockId == blockId))
                .ToArray();

            return new BlockiverseUiToolkitMenuView(
                MenuActions.BlockCatalogScreen,
                "Block Catalog",
                "Choose a creative block by category and page.",
                new[]
                {
                    new MenuAction(MenuActions.BlockCatalogPreviousCategory, "Previous Category"),
                    new MenuAction(MenuActions.BlockCatalogNextCategory, "Next Category"),
                    new MenuAction(MenuActions.BlockCatalogBack, "Close"),
                },
                new[]
                {
                    new MenuDetailRow("Category", BlockiverseLocalization.DisplayName(category)),
                    new MenuDetailRow("Selected block", hotbar != null ? registry.Get(hotbar.SelectedBlockId).Name : "Unavailable"),
                },
                new[] { "Creative", "Catalog" },
                selectionRows: rows,
                paging: new MenuPagingState(pageIndex, pageCount));
        }

        public static BlockiverseUiToolkitMenuView CreateCreativeToolsView(
            BlockiverseCreativeToolsPanel panel,
            CreativeHotbar hotbar)
        {
            BlockRegistry registry = BlockRegistry.Default;
            return new BlockiverseUiToolkitMenuView(
                MenuActions.CreativeToolsScreen,
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CreativeToolsTitle),
                "Edit regions, pick blocks, spawn structures, and control world time/weather in creative mode.",
                new[]
                {
                    new MenuAction(MenuActions.CreativeToolsSetA, "Set A"),
                    new MenuAction(MenuActions.CreativeToolsSetB, "Set B"),
                    new MenuAction(MenuActions.CreativeToolsPickBlock, "Pick Block"),
                    new MenuAction(MenuActions.CreativeToolsFill, "Fill"),
                    new MenuAction(MenuActions.CreativeToolsReplace, "Replace"),
                    new MenuAction(MenuActions.CreativeToolsDelete, "Delete"),
                    new MenuAction(MenuActions.CreativeToolsCopy, "Copy"),
                    new MenuAction(MenuActions.CreativeToolsPaste, "Paste"),
                    new MenuAction(MenuActions.CreativeToolsUndo, "Undo"),
                    new MenuAction(MenuActions.CreativeToolsRedo, "Redo"),
                    new MenuAction(MenuActions.CreativeToolsSpawnTree, "Spawn Tree"),
                    new MenuAction(MenuActions.CreativeToolsSpawnRuin, "Spawn Ruin"),
                    new MenuAction(MenuActions.CreativeToolsToggleCycle, "Pause / Resume Cycle"),
                    new MenuAction(MenuActions.CreativeToolsCycleWeather, "Cycle Weather"),
                    new MenuAction(MenuActions.CreativeToolsClose, "Close"),
                },
                new[]
                {
                    new MenuDetailRow("Selected block", hotbar != null ? registry.Get(hotbar.SelectedBlockId).Name : "Unavailable"),
                    new MenuDetailRow("Undo steps", panel != null ? panel.WorldEditUndoCount.ToString() : "Unavailable"),
                    new MenuDetailRow("Clipboard", panel != null && panel.HasWorldEditClipboard ? "Available" : "Empty"),
                },
                new[] { "Creative", "World edit" });
        }

        public static BlockiverseUiToolkitMenuView CreateMapWayflagView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.MapWayflagScreen,
                "Map / Wayflag",
                "Wayflag route for map markers and navigation status.",
                new[]
                {
                    new MenuAction(MenuActions.MapSetWayflag, "Set Wayflag"),
                    new MenuAction(MenuActions.MapClearWayflag, "Clear Wayflag"),
                    new MenuAction(MenuActions.MapBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Map", "Runtime map renderer not yet present"),
                    new MenuDetailRow("Wayflag", "Route available for future waypoint binding"),
                },
                new[] { "Map", "Navigation" },
                "Map visuals are not implemented yet.");
        }

        public static BlockiverseUiToolkitMenuView CreateFarmingSummaryView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.FarmingSummaryScreen,
                "Farming Summary",
                "Review farming context and open available actions.",
                new[]
                {
                    new MenuAction(MenuActions.FarmingOpenActions, "Actions"),
                    new MenuAction(MenuActions.FarmingBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Crop target", "Use held seed/tool against world blocks"),
                    new MenuDetailRow("Water check", "Validated by farming gameplay commands"),
                },
                new[] { "Farming", "Context" });
        }

        public static BlockiverseUiToolkitMenuView CreateFarmingActionView()
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.FarmingActionPopup,
                "Farming Actions",
                "Contextual farming commands are routed through the existing block interaction system.",
                new[]
                {
                    new MenuAction(MenuActions.FarmingTill, "Till"),
                    new MenuAction(MenuActions.FarmingPlant, "Plant"),
                    new MenuAction(MenuActions.FarmingHarvest, "Harvest"),
                    new MenuAction(MenuActions.FarmingBack, "Back"),
                },
                new[]
                {
                    new MenuDetailRow("Input source", "Held tool or seed"),
                    new MenuDetailRow("Validation", "World interaction command"),
                },
                new[] { "Farming", "Context" },
                "Use normal world interaction to execute farming actions.");
        }

        public static BlockiverseUiToolkitMenuView CreateDiagnosticsView(MultiplayerSurvivalSync survivalSync)
        {
            return new BlockiverseUiToolkitMenuView(
                MenuActions.DiagnosticsScreen,
                "Diagnostics",
                "Inspect runtime menu, survival, and network readiness signals.",
                new[] { new MenuAction(MenuActions.DiagnosticsBack, "Back") },
                new[]
                {
                    new MenuDetailRow("Survival sync", survivalSync != null ? "Found" : "Not found"),
                    new MenuDetailRow("Mode toggle", survivalSync != null && survivalSync.CanToggleMode ? "Available" : "Unavailable"),
                    new MenuDetailRow("Creative tools", survivalSync != null && survivalSync.CanUseCreativeMode ? "Available" : "Unavailable"),
                },
                new[] { "Diagnostics", "Runtime" });
        }

        public static BlockiverseUiToolkitMenuView CreateSimpleStatusView(string screenId, string title, string purpose, string backActionId)
        {
            return new BlockiverseUiToolkitMenuView(
                screenId,
                title,
                purpose,
                new[] { new MenuAction(backActionId, "Back") },
                new[] { new MenuDetailRow("Runtime binding", "Route available") },
                new[] { "Status" });
        }

        public static bool TryGetInstantRecipe(CraftingRecipeBook recipeBook, int index, out CraftingRecipe recipe)
        {
            List<CraftingRecipe> recipes = InstantRecipes(recipeBook);
            if (index >= 0 && index < recipes.Count)
            {
                recipe = recipes[index];
                return true;
            }

            recipe = null;
            return false;
        }

        static string Display(string canonicalId) =>
            BlockiverseLocalization.DisplayNameForCanonicalId(canonicalId);

        static string Percent(float value) => $"{value * 100.0f:0}%";

        static List<CraftingRecipe> InstantRecipes(CraftingRecipeBook recipeBook)
        {
            var recipes = new List<CraftingRecipe>();
            if (recipeBook == null)
                return recipes;

            foreach (CraftingRecipe recipe in recipeBook.All)
                if (recipe.TimeTicks <= 0)
                    recipes.Add(recipe);
            return recipes;
        }

        static CreativeCatalogCategory[] CatalogCategories() =>
            (CreativeCatalogCategory[])Enum.GetValues(typeof(CreativeCatalogCategory));

        static string FormatIngredients(CraftingRecipe recipe, ItemRegistry registry)
        {
            if (recipe == null || recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                return "No ingredients";

            return string.Join(", ", recipe.Ingredients.Select(stack => FormatStack(stack, registry)));
        }

        static string FormatStack(ItemStack stack, ItemRegistry registry)
        {
            if (stack.IsEmpty)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty);

            ItemDefinition definition = (registry ?? ItemRegistry.Default).Get(stack.ItemId);
            return BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStack, definition.Name, stack.Count);
        }

        static BlockiverseUiToolkitMenuView Informational(string screenId, string title, string purpose)
        {
            return new BlockiverseUiToolkitMenuView(
                screenId,
                title,
                purpose,
                Array.Empty<MenuAction>(),
                new[] { new MenuDetailRow("Status", "Target menu inventory") },
                Array.Empty<string>());
        }
    }
}
