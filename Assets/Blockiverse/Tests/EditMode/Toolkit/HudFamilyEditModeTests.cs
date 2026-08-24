using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit mirrors of the HUD-family oracles (matrix rows 21-24):
    // SurvivalUiEditModeTests.HealthPanelUpdatesFromVitalsChanges,
    // SurvivalHudFeedbackEditModeTests (mining progress + harvest-rejection status), and
    // CreativeInteractionPlayModeTests.HotbarSelectionUpdatesSelectedBlockLabel at EditMode
    // component level. UIDocument never builds a panel in EditMode, so trees come from the
    // VisualTreeAsset via AttachForTest, and interactions drive each controller's public
    // seams because ClickEvent dispatch requires a runtime panel.
    public sealed class HudFamilyEditModeTests
    {
        sealed class StubMenuFrontend : IBlockiverseMenuFrontend
        {
            public void SetActionMenu(string screenId, string title, IReadOnlyList<MenuAction> actions)
            {
            }

            public void SetScreenStatus(string screenId, string message)
            {
            }

            public void SetSaveList(IEnumerable<WorldSaveSummary> saves)
            {
            }

            public void ShowWorldDetails(WorldSaveSummary save)
            {
            }

            public void SetTitleMenuPose(Pose pose)
            {
            }

            public void RefreshCreativeEnvironmentControls()
            {
            }

            public void ToggleQuickBlockMenu()
            {
            }

            public void HideQuickBlockMenu()
            {
            }

            public void ResetNewWorldScreen()
            {
            }

            public NewWorldConfig PendingNewWorldConfig => null;
            public WorldSaveSummary? PendingLoadSave => null;
            public WorldSaveSummary? PendingDetailsSave => null;
            public string PendingDetailsRenameText => string.Empty;

            public bool IsStationOpenAt(BlockPosition position) => false;

            public void CloseStationView()
            {
            }
        }

        sealed class FakeSurvivalVitals : ISurvivalVitalsView
        {
            public int Hunger { get; set; }
            public int Thirst { get; set; }
            public int Stamina { get; set; }
        }

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        TController CreateScreen<TController>() where TController : UiToolkitScreenController
        {
            var gameObject = new GameObject(typeof(TController).Name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<TController>();
        }

        static VisualElement AttachFreshTree(UiToolkitScreenController controller)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controller.GetType(), typeof(UiToolkitScreenAttribute));
            Assert.That(attribute, Is.Not.Null, $"{controller.GetType().Name} has no [UiToolkitScreen] attribute.");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return root;
        }

        (BlockiverseMenuController menuController, UiToolkitMenuHost host) CreateMenuControllerWithHost()
        {
            var controllerObject = new GameObject("Menu Controller Under Test");
            objectsToDestroy.Add(controllerObject);
            BlockiverseMenuController menuController = controllerObject.AddComponent<BlockiverseMenuController>();
            menuController.RegisterFrontend(new StubMenuFrontend());

            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);
            return (menuController, host);
        }

        static DisplayStyle DisplayOf(VisualElement element) => element.style.display.value;

        static float FillPercentOf(VisualElement fill)
        {
            StyleLength width = fill.style.width;
            Assert.That(width.value.unit, Is.EqualTo(LengthUnit.Percent), "fill width must be a percentage");
            return width.value.value;
        }

        // The action bar sits LOW and the stats readout sits top-right; they used to be one panel
        // at the Hud profile default (dead centre, 1.30 eye height), which is what Eric reported as
        // the HUD blocking his view. These numbers are the split, not a tuning nudge.
        // HudLocalY is HEAD-relative: 0 is eye level, negative is below it. These were
        // floor-relative until the HUD was reparented from Camera Offset to the head — panels
        // followed where the player stood but not where they looked, so anything off-centre left
        // the view the moment they turned. A floor-relative 1.55 read as eye-level-plus-1.55m
        // after the reparent, which is why every value here changed at once.
        [TestCase(typeof(GameplayHudController), 590, 150, -0.40f)]
        [TestCase(typeof(GameplayStatsController), 460, 190, 0.24f)]
        [TestCase(typeof(MiningProgressController), 400, 90, -0.16f)]
        [TestCase(typeof(StatusToastController), 640, 120, 0.34f)]
        [TestCase(typeof(CreativeHotbarController), 590, 500, -0.50f)]
        public void HudFamilyDeclaresTheSharedScreenIdAndHudProfile(Type controllerType, int width, int height, float hudLocalY)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controllerType, typeof(UiToolkitScreenAttribute));

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(attribute.WidthPixels, Is.EqualTo(width));
            Assert.That(attribute.HeightPixels, Is.EqualTo(height));
            Assert.That(attribute.PlacementProfile, Is.EqualTo(UiToolkitPlacementProfile.Hud));
            // The co-visible family stacks vertically off one rig anchor; a drifted local Y
            // overlaps two panels in the headset with nothing failing in the editor.
            Assert.That(attribute.HudLocalY, Is.EqualTo(hudLocalY).Within(0.001f));

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");

            // The quick menu — and only the quick menu — must carry the interface that makes
            // the host exclude it from routed visibility.
            Assert.That(
                typeof(IUiToolkitQuickBlockMenu).IsAssignableFrom(controllerType),
                Is.EqualTo(controllerType == typeof(CreativeHotbarController)));
        }

        [Test]
        public void HealthRatioBarAndStateFollowVitalsTransitions()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);
            Assert.That(controller.IsBound, Is.True);

            Label ratio = root.Q<Label>("bv-health-ratio");
            Label state = root.Q<Label>("bv-vitals-state");
            VisualElement fill = root.Q<VisualElement>("bv-health-fill");

            // Negative control: unbound, the readout renders nothing.
            Assert.That(ratio.text, Is.Empty);
            Assert.That(state.text, Is.Empty);

            var vitals = new PlayerVitals(currentHealth: 75);
            controller.Bind(vitals);

            Assert.That(ratio.text, Is.EqualTo("75 / 100"));
            Assert.That(FillPercentOf(fill), Is.EqualTo(75f).Within(0.01f));
            Assert.That(state.text, Is.EqualTo("Stable"));

            // HealthChanged drives the refresh — no cadence tick exists in EditMode.
            vitals.ApplyDamage(55);

            Assert.That(ratio.text, Is.EqualTo("20 / 100"));
            Assert.That(FillPercentOf(fill), Is.EqualTo(20f).Within(0.01f));
            Assert.That(state.text, Is.EqualTo("Critical"));

            vitals.ApplyDamage(20);

            Assert.That(ratio.text, Is.EqualTo("0 / 100"));
            Assert.That(FillPercentOf(fill), Is.EqualTo(0f).Within(0.01f));
            Assert.That(state.text, Is.EqualTo("Down"));
        }

        [Test]
        public void StateLineIncludesHungerThirstStaminaWhenSurvivalVitalsBound()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);

            controller.Bind(new PlayerVitals(currentHealth: 75));
            controller.BindSurvivalVitals(new FakeSurvivalVitals { Hunger = 80, Thirst = 60, Stamina = 40 });

            Assert.That(
                root.Q<Label>("bv-vitals-state").text,
                Is.EqualTo("Stable · Hunger 80 · Thirst 60 · Stamina 40"));
        }

        [Test]
        public void HudOpenButtonsRouteToTheRoutedPanels()
        {
            (BlockiverseMenuController menuController, UiToolkitMenuHost host) = CreateMenuControllerWithHost();
            GameplayHudController controller = CreateScreen<GameplayHudController>();
            AttachFreshTree(controller);
            controller.ConfigureHost(host);

            // Positive control on the precondition so the assertions cannot pass vacuously.
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));

            controller.OpenInventory();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.InventoryScreen));

            controller.OpenCrafting();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.CraftingScreen));

            controller.OpenCatalog();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.CatalogScreen));

            controller.OpenCrate();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.StationCrateScreen));

            // Negative control: without a host, a press must be a safe no-op.
            GameplayHudController orphan = CreateScreen<GameplayHudController>();
            AttachFreshTree(orphan);
            Assert.DoesNotThrow(orphan.OpenInventory);
            Assert.DoesNotThrow(orphan.OpenCrate);
        }

        [Test]
        public void MiningProgressShowsPercentTextAndFillThenClears()
        {
            MiningProgressController controller = CreateScreen<MiningProgressController>();
            VisualElement root = AttachFreshTree(controller);
            Assert.That(controller.IsBound, Is.True);

            VisualElement body = root.Q<VisualElement>("bv-mining-body");
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.None), "must start hidden");

            controller.OnMiningProgressChanged(new BlockPosition(1, 2, 3), 0.5f, 1.0f);

            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Label>("bv-mining-label").text, Is.EqualTo("Mining 50%"));
            Assert.That(FillPercentOf(root.Q<VisualElement>("bv-mining-fill")), Is.EqualTo(50f).Within(0.01f));

            controller.OnMiningProgressCleared();

            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.None));
            Assert.That(controller.IsShowingMiningProgress, Is.False);
        }

        [Test]
        public void MiningProgressHidesOnlyOnFinalHarvestRejection()
        {
            MiningProgressController controller = CreateScreen<MiningProgressController>();
            VisualElement root = AttachFreshTree(controller);
            VisualElement body = root.Q<VisualElement>("bv-mining-body");
            var position = new BlockPosition(1, 2, 3);

            controller.OnMiningProgressChanged(position, 0.5f, 1.0f);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex), "positive control: bar is showing");

            // None of these are FINAL harvest rejections; the bar must survive each.
            controller.OnHarvestCommandFeedback(
                SurvivalCommandResult.Accept(SurvivalCommandKind.HarvestResource, 1), position);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex), "accepted must not hide");

            controller.OnHarvestCommandFeedback(
                SurvivalCommandResult.RequestSent(SurvivalCommandKind.HarvestResource, 2), position);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex), "pending must not hide");

            controller.OnHarvestCommandFeedback(
                SurvivalCommandResult.DuplicateResult(SurvivalCommandKind.HarvestResource, 3), position);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex), "duplicate must not hide");

            controller.OnHarvestCommandFeedback(
                SurvivalCommandResult.Reject(SurvivalCommandKind.CraftRecipe, SurvivalCommandFailureReason.CraftingRejected),
                position);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.Flex), "non-harvest rejection must not hide");

            controller.OnHarvestCommandFeedback(
                SurvivalCommandResult.Reject(SurvivalCommandKind.HarvestResource, SurvivalCommandFailureReason.HarvestRejected),
                position);
            Assert.That(DisplayOf(body), Is.EqualTo(DisplayStyle.None), "final harvest rejection hides the bar");
        }

        [Test]
        public void StatusToastShowsTimedHarvestRejectionMessages()
        {
            StatusToastController controller = CreateScreen<StatusToastController>();
            VisualElement root = AttachFreshTree(controller);
            Assert.That(controller.IsBound, Is.True);

            Label label = root.Q<Label>("bv-toast-label");
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.None), "must start hidden");

            var position = new BlockPosition(1, 2, 3);

            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.HarvestRejected,
                    harvestFailureReason: BlockHarvestFailureReason.InventoryFull),
                position);
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.Flex));
            Assert.That(label.text, Is.EqualTo("Inventory full"));

            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.HarvestRejected,
                    harvestFailureReason: BlockHarvestFailureReason.InsufficientTool),
                position);
            Assert.That(label.text, Is.EqualTo("Tool is not strong enough"));

            // The command-level InventoryFull reason maps to the same message even without a
            // harvest-level reason (uGUI's `_ when` arm).
            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.InventoryFull),
                position);
            Assert.That(label.text, Is.EqualTo("Inventory full"));

            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(
                    SurvivalCommandKind.HarvestResource,
                    SurvivalCommandFailureReason.HarvestRejected),
                position);
            Assert.That(label.text, Is.EqualTo("Cannot harvest this block"));
        }

        [Test]
        public void StatusToastIgnoresNonFinalOrNonHarvestFeedback()
        {
            StatusToastController controller = CreateScreen<StatusToastController>();
            VisualElement root = AttachFreshTree(controller);
            Label label = root.Q<Label>("bv-toast-label");
            var position = new BlockPosition(1, 2, 3);

            controller.OnCommandFeedback(
                SurvivalCommandResult.Accept(SurvivalCommandKind.HarvestResource, 1), position);
            controller.OnCommandFeedback(
                SurvivalCommandResult.RequestSent(SurvivalCommandKind.HarvestResource, 2), position);
            controller.OnCommandFeedback(
                SurvivalCommandResult.DuplicateResult(SurvivalCommandKind.HarvestResource, 3), position);
            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(SurvivalCommandKind.CraftRecipe, SurvivalCommandFailureReason.CraftingRejected),
                position);

            Assert.That(controller.CurrentStatusText, Is.Empty);
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.None));

            // Positive control: the same seam CAN show a toast, so the silence above is the
            // filter working rather than a dead code path.
            controller.OnCommandFeedback(
                SurvivalCommandResult.Reject(SurvivalCommandKind.HarvestResource, SurvivalCommandFailureReason.HarvestRejected),
                position);
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void StatusToastSetStatusTextHidesWhenEmpty()
        {
            StatusToastController controller = CreateScreen<StatusToastController>();
            VisualElement root = AttachFreshTree(controller);
            Label label = root.Q<Label>("bv-toast-label");

            controller.SetStatusText("Testing");
            Assert.That(label.text, Is.EqualTo("Testing"));
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.Flex));

            controller.SetStatusText(string.Empty);
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.None));

            controller.SetStatusText("Testing");
            controller.SetStatusText(null);
            Assert.That(controller.CurrentStatusText, Is.Empty);
            Assert.That(DisplayOf(label), Is.EqualTo(DisplayStyle.None));
        }

        // EditMode mirror of CreativeInteractionPlayModeTests.HotbarSelectionUpdatesSelectedBlockLabel.
        [Test]
        public void HotbarSelectionUpdatesSelectedBlockLabel()
        {
            CreativeHotbarController controller = CreateScreen<CreativeHotbarController>();
            VisualElement root = AttachFreshTree(controller);
            controller.Configure(
                BlockRegistry.CreateDefault(),
                new[] { BlockRegistry.LooseLoam, BlockRegistry.LumenQuartzCluster });

            Assert.That(root.Q<ScrollView>("bv-hotbar-slots").Query<Button>().ToList(), Has.Count.EqualTo(2));
            Assert.That(controller.SelectedBlockId, Is.EqualTo(BlockRegistry.LooseLoam));

            controller.SelectNext();

            Assert.That(controller.SelectedBlockId, Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(root.Q<Label>("bv-hotbar-selected").text, Does.Contain("Lumen Quartz Cluster"));

            List<Button> slots = root.Q<ScrollView>("bv-hotbar-slots").Query<Button>().ToList();
            Assert.That(slots[1].ClassListContains("hs-button--selected"), Is.True);
            Assert.That(slots[0].ClassListContains("hs-button--selected"), Is.False);

            controller.SelectIndex(0);

            Assert.That(controller.SelectedBlockId, Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(slots[0].ClassListContains("hs-button--selected"), Is.True);
            Assert.That(slots[1].ClassListContains("hs-button--selected"), Is.False);
        }

        [Test]
        public void HotbarConfigureFiltersAirBlocks()
        {
            CreativeHotbarController controller = CreateScreen<CreativeHotbarController>();
            VisualElement root = AttachFreshTree(controller);

            controller.Configure(
                BlockRegistry.CreateDefault(),
                new[] { BlockRegistry.Air, BlockRegistry.LooseLoam });

            Assert.That(controller.BlockIds, Has.Count.EqualTo(1));
            Assert.That(controller.SelectedBlockId, Is.EqualTo(BlockRegistry.LooseLoam));
            Assert.That(root.Q<ScrollView>("bv-hotbar-slots").Query<Button>().ToList(), Has.Count.EqualTo(1));
        }

        [Test]
        public void QuickMenuVisibilityCollapsesTheScreenRoot()
        {
            CreativeHotbarController controller = CreateScreen<CreativeHotbarController>();
            VisualElement root = AttachFreshTree(controller);
            VisualElement screenRoot = root.Q<VisualElement>("bv-screen-root");

            Assert.That(controller.IsQuickMenuVisible, Is.False, "must start hidden");

            controller.SetQuickMenuVisible(true);
            Assert.That(controller.IsQuickMenuVisible, Is.True);
            Assert.That(DisplayOf(screenRoot), Is.EqualTo(DisplayStyle.Flex));

            controller.SetQuickMenuVisible(false);
            Assert.That(controller.IsQuickMenuVisible, Is.False);
            Assert.That(DisplayOf(screenRoot), Is.EqualTo(DisplayStyle.None));

            // Redundant host pushes (every router change pushes false) must stay no-ops.
            Assert.DoesNotThrow(() => controller.SetQuickMenuVisible(false));
            Assert.That(DisplayOf(screenRoot), Is.EqualTo(DisplayStyle.None));
        }
    }
}
