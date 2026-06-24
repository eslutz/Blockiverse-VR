using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class UiToolkitMenuMigrationEditModeTests
    {
        [Test]
        public void TargetMenuInventoryIncludesCurrentRulesetAndLegacySurfaces()
        {
            var ids = new HashSet<string>(
                BlockiverseUiToolkitMenuCatalog.AllTargetMenus.Select(menu => menu.ScreenId));

            string[] required =
            {
                MenuActions.TitleScreen,
                MenuActions.NewWorldScreen,
                MenuActions.LoadWorldScreen,
                MenuActions.WorldDetailsScreen,
                MenuActions.SettingsScreen,
                MenuActions.GameplayHudScreen,
                MenuActions.PauseScreen,
                MenuActions.InventoryScreen,
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
                MenuActions.PlayerHubScreen,
                MenuActions.ContextHubScreen,
                MenuActions.FarmingSummaryScreen,
                MenuActions.FarmingActionPopup,
                MenuActions.AvatarStatusScreen,
                MenuActions.MetaPolicyStatusScreen,
                MenuActions.DiagnosticsScreen,
                MenuActions.NetworkCommandStatusScreen,
                MenuActions.SurvivalRejectionScreen,
            };

            foreach (string screenId in required)
                Assert.That(ids, Does.Contain(screenId), $"{screenId} is missing from the UITK migration inventory.");
        }

        [Test]
        public void RuntimeReplacementScopeIncludesStatefulAppShellScreens()
        {
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.TitleScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.NewWorldScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.LoadWorldScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.WorldDetailsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.PauseScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.DeathScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ConfirmModal), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.SettingsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ComfortSettingsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.AudioSettingsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ControlsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.LanMultiplayerScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.PlayerHubScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.InventoryScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.VitalsStatusScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.CraftingScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ContainerScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.StationMenuScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.CampfireStationScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ClayKilnStationScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.BellowsForgeStationScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.PrepBoardStationScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.MendBenchStationScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.MapWayflagScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ItemDetailsPopover), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.RecipePinOverlay), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.BlockCatalogScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.CreativeToolsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ContextHubScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.StatusHubScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.FarmingSummaryScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.FarmingActionPopup), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.AvatarStatusScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.MetaPolicyStatusScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.DiagnosticsScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.NetworkCommandStatusScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.SurvivalRejectionScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.ControllerMappingScreen), Is.True);
            Assert.That(BlockiverseUiToolkitMenuCatalog.SupportsRuntimeReplacement(MenuActions.WorldLoadingScreen), Is.True);
        }

        [Test]
        public void RuntimeTitleViewUsesCurrentActionContract()
        {
            BlockiverseUiToolkitMenuView view = BlockiverseUiToolkitMenuCatalog.CreateRuntimeView(
                MenuActions.TitleScreen,
                hasLatestSave: true,
                hasAnySave: true,
                canQuit: true,
                canToggleMode: false,
                canOpenCreativeTools: false,
                hasBedrollSpawn: false,
                titleStatus: "Ready",
                pauseStatus: string.Empty,
                confirmPrompt: string.Empty,
                confirmActions: null);

            string[] actionIds = view.Actions.Select(action => action.ActionId).ToArray();

            Assert.That(view.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            Assert.That(actionIds[0], Is.EqualTo(MenuActions.TitleContinue));
            Assert.That(actionIds, Does.Contain(MenuActions.TitleNewWorld));
            Assert.That(actionIds, Does.Contain(MenuActions.TitleLoadWorld));
            Assert.That(actionIds, Does.Contain(MenuActions.TitleMultiplayer));
            Assert.That(actionIds, Does.Contain(MenuActions.TitleSettings));
            Assert.That(actionIds, Does.Contain(MenuActions.TitleQuit));
            Assert.That(view.Status, Is.EqualTo("Ready"));
        }

        [Test]
        public void RuntimeNewWorldViewCarriesEditableFieldsAndCycleRows()
        {
            var config = new NewWorldConfig("meadow-home");
            config.SetName("Meadow Home");
            config.CycleDifficulty();

            BlockiverseUiToolkitMenuView view =
                BlockiverseUiToolkitMenuCatalog.CreateNewWorldView(config, "Ready");

            Assert.That(view.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));
            Assert.That(view.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.NewWorldCreate));
            Assert.That(view.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.NewWorldCancel));
            Assert.That(view.TextInputs.Select(input => input.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.NewWorldNameField));
            Assert.That(view.TextInputs.Select(input => input.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.NewWorldSeedField));
            Assert.That(view.CycleRows.Select(row => row.FieldId), Is.EquivalentTo(new[]
            {
                BlockiverseUiToolkitMenuCatalog.NewWorldGameModeField,
                BlockiverseUiToolkitMenuCatalog.NewWorldDifficultyField,
                BlockiverseUiToolkitMenuCatalog.NewWorldSizeField,
                BlockiverseUiToolkitMenuCatalog.NewWorldPresetField,
                BlockiverseUiToolkitMenuCatalog.NewWorldStartingBiomeField,
                BlockiverseUiToolkitMenuCatalog.NewWorldTextureSetField,
            }));
            Assert.That(view.Status, Is.EqualTo("Ready"));
        }

        [Test]
        public void RuntimeLoadWorldViewCarriesSaveSelectionAndPaging()
        {
            DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var saves = new[]
            {
                new WorldSaveSummary("Meadow Home", "1234", "survival", "normal", 4, created, created),
                new WorldSaveSummary("Builder", "5678", "creative", "easy", 1, created, created),
            };

            BlockiverseUiToolkitMenuView view =
                BlockiverseUiToolkitMenuCatalog.CreateLoadWorldView(
                    saves,
                    saves[1],
                    pageIndex: 1,
                    pageCount: 2,
                    status: "Two saves");

            string[] actionIds = view.Actions.Select(action => action.ActionId).ToArray();
            Assert.That(actionIds, Is.EqualTo(new[]
            {
                MenuActions.LoadWorldLoad,
                MenuActions.LoadWorldDetails,
                MenuActions.LoadWorldCancel,
            }));
            Assert.That(view.SelectionRows.Count, Is.EqualTo(2));
            Assert.That(
                view.SelectionRows.Single(row =>
                    row.ValueId == BlockiverseUiToolkitMenuCatalog.LoadWorldSaveSelectionPrefix + "Builder").Selected,
                Is.True);
            Assert.That(view.Paging.HasValue, Is.True);
            Assert.That(view.Paging.Value.PageIndex, Is.EqualTo(1));
            Assert.That(view.Paging.Value.PageCount, Is.EqualTo(2));
            Assert.That(view.Status, Is.EqualTo("Two saves"));
        }

        [Test]
        public void RuntimeLoadWorldViewNamespacesSaveSelectionIds()
        {
            DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var saves = new[]
            {
                new WorldSaveSummary("inventory.slot.0", "1234", "survival", "normal", 4, created, created),
            };

            BlockiverseUiToolkitMenuView view =
                BlockiverseUiToolkitMenuCatalog.CreateLoadWorldView(
                    saves,
                    saves[0],
                    pageIndex: 0,
                    pageCount: 1,
                    status: string.Empty);

            Assert.That(view.SelectionRows.Single().ValueId,
                Is.EqualTo(BlockiverseUiToolkitMenuCatalog.LoadWorldSaveSelectionPrefix + "inventory.slot.0"));
            Assert.That(view.SelectionRows.Single().Label, Does.Contain("inventory.slot.0"));
        }

        [Test]
        public void RuntimeWorldDetailsViewCarriesRenameFieldAndManagementActions()
        {
            DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var save = new WorldSaveSummary("Meadow Home", "1234", "survival", "normal", 4, created, created);

            BlockiverseUiToolkitMenuView view =
                BlockiverseUiToolkitMenuCatalog.CreateWorldDetailsView(save, "Meadow Home 2");

            Assert.That(view.ScreenId, Is.EqualTo(MenuActions.WorldDetailsScreen));
            Assert.That(view.Actions.Select(action => action.ActionId), Is.EqualTo(MenuActions.WorldDetails.Select(action => action.ActionId)));
            Assert.That(view.TextInputs.Single().FieldId, Is.EqualTo(BlockiverseUiToolkitMenuCatalog.WorldDetailsRenameField));
            Assert.That(view.TextInputs.Single().Value, Is.EqualTo("Meadow Home 2"));
            Assert.That(view.Details.Any(row => row.Value.Contains("1234")), Is.True);
        }

        [Test]
        public void RuntimeComfortAndAudioViewsExposeMutableControlRows()
        {
            GameObject root = new("Settings");
            try
            {
                BlockiverseComfortSettings comfort = root.AddComponent<BlockiverseComfortSettings>();
                comfort.LocomotionMode = BlockiverseLocomotionMode.Teleport;
                BlockiverseFeedbackSettings feedback = root.AddComponent<BlockiverseFeedbackSettings>();
                feedback.MuteAll = true;

                BlockiverseUiToolkitMenuView comfortView =
                    BlockiverseUiToolkitMenuCatalog.CreateComfortView(comfort);
                BlockiverseUiToolkitMenuView audioView =
                    BlockiverseUiToolkitMenuCatalog.CreateAudioView(feedback);

                Assert.That(comfortView.ToggleRows.Select(row => row.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.ComfortUseTeleportField));
                Assert.That(comfortView.SliderRows.Select(row => row.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.ComfortEyeHeightField));
                Assert.That(comfortView.ToggleRows.Single(row => row.FieldId == BlockiverseUiToolkitMenuCatalog.ComfortUseTeleportField).Value, Is.True);

                Assert.That(audioView.ToggleRows.Select(row => row.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.AudioMuteAllField));
                Assert.That(audioView.SliderRows.Select(row => row.FieldId), Does.Contain(BlockiverseUiToolkitMenuCatalog.AudioMasterVolumeField));
                Assert.That(audioView.ToggleRows.Single(row => row.FieldId == BlockiverseUiToolkitMenuCatalog.AudioMuteAllField).Value, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeLanViewCarriesAddressFieldAndSessionActions()
        {
            BlockiverseUiToolkitMenuView view =
                BlockiverseUiToolkitMenuCatalog.CreateLanView(
                    "192.168.1.20",
                    "Ready",
                    canStart: true,
                    canStop: false);

            Assert.That(view.TextInputs.Single().FieldId, Is.EqualTo(BlockiverseUiToolkitMenuCatalog.LanAddressField));
            Assert.That(view.TextInputs.Single().Value, Is.EqualTo("192.168.1.20"));
            Assert.That(view.Actions.Select(action => action.ActionId), Is.EqualTo(new[]
            {
                MenuActions.LanMultiplayerHost,
                MenuActions.LanMultiplayerJoin,
                MenuActions.LanMultiplayerClose,
            }));
            Assert.That(view.Status, Is.EqualTo("Ready"));
            Assert.That(BlockiverseNetworkConfig.DefaultAddress, Is.Not.Empty);
        }

        [Test]
        public void RuntimePlayerViewsExposeInventoryVitalsAndCraftingRows()
        {
            var inventory = new Inventory(ItemRegistry.Default, slotCount: 12, hotbarSlotCount: 4);
            inventory.SetSlot(0, new ItemStack(ItemId.SurfacePebbles, 3));
            inventory.SetSlot(4, new ItemStack(ItemId.BranchwoodLog, 2));
            var vitals = new PlayerVitals(maxHealth: 100, currentHealth: 25);
            var survivalVitals = new SurvivalVitals();

            BlockiverseUiToolkitMenuView hub = BlockiverseUiToolkitMenuCatalog.CreatePlayerHubView();
            BlockiverseUiToolkitMenuView inventoryView =
                BlockiverseUiToolkitMenuCatalog.CreateInventoryView(inventory, ItemRegistry.Default, 0, 0, 6);
            BlockiverseUiToolkitMenuView vitalsView =
                BlockiverseUiToolkitMenuCatalog.CreateVitalsView(vitals, survivalVitals, "Ready");
            BlockiverseUiToolkitMenuView craftingView =
                BlockiverseUiToolkitMenuCatalog.CreateCraftingView(
                    CraftingRecipeBook.CreateDefault(ItemRegistry.Default),
                    inventory,
                    ItemRegistry.Default,
                    pageIndex: 0,
                    visibleRecipeCount: 6);

            Assert.That(hub.Actions.Select(action => action.ActionId), Is.EqualTo(new[]
            {
                MenuActions.PlayerHubInventory,
                MenuActions.PlayerHubVitals,
                MenuActions.PlayerHubCrafting,
                MenuActions.PlayerHubContext,
                MenuActions.PlayerHubStatus,
                MenuActions.PlayerHubRecipePin,
                MenuActions.PlayerHubClose,
            }));
            Assert.That(inventoryView.SelectionRows.Count, Is.EqualTo(6));
            Assert.That(inventoryView.SelectionRows[0].ValueId, Is.EqualTo(BlockiverseUiToolkitMenuCatalog.InventorySlotSelectionPrefix + "0"));
            Assert.That(inventoryView.SelectionRows[0].Selected, Is.True);
            Assert.That(vitalsView.Details.Single(row => row.Label == "Health").Value, Is.EqualTo("25 / 100"));
            Assert.That(craftingView.SelectionRows.Count, Is.GreaterThan(0));
            Assert.That(craftingView.SelectionRows[0].ValueId, Does.StartWith(BlockiverseUiToolkitMenuCatalog.CraftingRecipeSelectionPrefix));
            Assert.That(craftingView.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.CraftingCraftSelected));
            Assert.That(craftingView.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.CraftingPinSelected));
        }

        [Test]
        public void RuntimePlayerContextAndStatusRoutesExposeLegacyUsefulSurfaces()
        {
            BlockiverseUiToolkitMenuView context =
                BlockiverseUiToolkitMenuCatalog.CreateContextHubView(
                    hasContainer: true,
                    hasStation: true,
                    canUseCreativeTools: true);
            BlockiverseUiToolkitMenuView status =
                BlockiverseUiToolkitMenuCatalog.CreateStatusHubView();
            BlockiverseUiToolkitMenuView map =
                BlockiverseUiToolkitMenuCatalog.CreateMapWayflagView();
            BlockiverseUiToolkitMenuView farmingSummary =
                BlockiverseUiToolkitMenuCatalog.CreateFarmingSummaryView();
            BlockiverseUiToolkitMenuView farming =
                BlockiverseUiToolkitMenuCatalog.CreateFarmingActionView();

            Assert.That(context.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.ContextHubContainer));
            Assert.That(context.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.ContextHubStation));
            Assert.That(context.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.ContextHubCreativeTools));
            Assert.That(status.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.StatusHubDiagnostics));
            Assert.That(status.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.StatusHubNetwork));
            Assert.That(map.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.MapSetWayflag));
            Assert.That(farmingSummary.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.FarmingOpenActions));
            Assert.That(farmingSummary.Actions.Select(action => action.ActionId), Does.Not.Contain(MenuActions.ContextHubFarming));
            Assert.That(farming.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.FarmingHarvest));
        }

        [Test]
        public void RuntimeContainerStationCatalogAndPinViewsExposeFunctionalRows()
        {
            var crate = new Inventory(ItemRegistry.Default, slotCount: 8, hotbarSlotCount: 0);
            crate.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 4));
            var station = new SmeltingStationModel(CraftingStation.ClayKiln, SmeltingStationModel.InputSlotCountFor(CraftingStation.ClayKiln));
            station.SetFuel(new ItemStack(ItemId.Embercoal, 2));
            station.SetInput(0, new ItemStack(ItemId.Claybed, 1));
            CraftingRecipe recipe = CraftingRecipeBook.CreateDefault(ItemRegistry.Default).GetByOutput(ItemId.WorkPlank);

            BlockiverseUiToolkitMenuView container =
                BlockiverseUiToolkitMenuCatalog.CreateContainerView(crate, ItemRegistry.Default, 0, 6);
            BlockiverseUiToolkitMenuView stationView =
                BlockiverseUiToolkitMenuCatalog.CreateStationView(station, "Idle", ItemRegistry.Default);
            BlockiverseUiToolkitMenuView pin =
                BlockiverseUiToolkitMenuCatalog.CreateRecipePinView(recipe, crate, ItemRegistry.Default);
            BlockiverseUiToolkitMenuView catalog =
                BlockiverseUiToolkitMenuCatalog.CreateBlockCatalogView(
                    CreativeCatalog.CreateDefault(),
                    BlockRegistry.Default,
                    hotbar: null,
                    categoryIndex: 0,
                    pageIndex: 0,
                    visibleCount: 6);

            Assert.That(container.SelectionRows.Single(row => row.ValueId == BlockiverseUiToolkitMenuCatalog.ContainerSlotSelectionPrefix + "0").Label, Does.Contain("Branchwood"));
            Assert.That(container.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.ContainerDepositHeld));
            Assert.That(stationView.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.StationDepositInput));
            Assert.That(stationView.Details.Any(row => row.Label == "Fuel" && row.Value.Contains("Embercoal")), Is.True);
            Assert.That(pin.Actions.Select(action => action.ActionId), Does.Contain(MenuActions.RecipePinClear));
            Assert.That(pin.Details.Any(row => row.Label == "Pinned recipe"), Is.True);
            Assert.That(catalog.SelectionRows.Count, Is.GreaterThan(0));
            Assert.That(catalog.SelectionRows[0].ValueId, Does.StartWith(BlockiverseUiToolkitMenuCatalog.BlockCatalogSelectionPrefix));
        }

        [Test]
        public void UiToolkitMenuAssetsUseSupportedReadableStyleRules()
        {
            string uxml = File.ReadAllText(BlockiverseProject.UiToolkitMenuShellPath);
            string uss = File.ReadAllText(BlockiverseProject.UiToolkitMenuThemePath);

            StringAssert.Contains("<ui:Style src=\"BlockiverseMenuTheme.uss\" />", uxml);
            StringAssert.Contains("name=\"blockiverse-menu-root\"", uxml);
            StringAssert.Contains("style=\"display: none;\"", uxml);
            StringAssert.Contains("name=\"blockiverse-menu-actions-scroll\"", uxml);
            StringAssert.Contains("name=\"blockiverse-menu-actions\"", uxml);
            StringAssert.Contains("name=\"blockiverse-menu-details-scroll\"", uxml);
            StringAssert.Contains("name=\"blockiverse-menu-details\"", uxml);

            string[] requiredTokens =
            {
                "--bv-color-text",
                "--bv-color-panel",
                "--bv-color-accent",
                "--bv-font-md",
                "--bv-control-height",
                ".bv-menu-actions-scroll",
                ".bv-menu-details-scroll",
            };

            foreach (string token in requiredTokens)
                StringAssert.Contains(token, uss);

            string[] unsupported =
            {
                "display: grid",
                "box-shadow",
                "calc(",
                "@media",
                "::before",
                "::after",
                "z-index",
                "linear-gradient",
                "radial-gradient",
            };

            foreach (string pattern in unsupported)
                Assert.That(uss, Does.Not.Contain(pattern), $"USS contains unsupported CSS pattern {pattern}.");
        }

        [Test]
        public void BootstrapperGeneratesWorldSpaceXrUiToolkitSurface()
        {
            string source = File.ReadAllText(
                "Assets/Blockiverse/Scripts/Editor/BlockiverseProjectBootstrapper.UiToolkitMenus.cs");

            StringAssert.Contains("EnsureComponent<XRUIToolkitManager>", source);
            StringAssert.Contains("EnsureComponent<BlockiverseWorldSpacePanelPresenter>", source);
            StringAssert.Contains("ConfigureWorldSpaceTarget", source);
            StringAssert.Contains("ConfigureWorldSpacePanelSettings", source);
            StringAssert.Contains("m_RenderMode", source);
            StringAssert.Contains("m_ColliderUpdateMode", source);
            StringAssert.Contains("m_ColliderIsTrigger", source);
        }
    }
}
