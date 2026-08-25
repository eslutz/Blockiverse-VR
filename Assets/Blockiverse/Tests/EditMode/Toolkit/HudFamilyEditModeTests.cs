using System;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
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

            public void CycleHotbarSlot(int delta)
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

            // SurvivalVitals.DefaultMax. Defaulted rather than left at zero so a fixture that only
            // sets the three vitals still produces meters with a sane denominator; a zero max would
            // make every reading read as empty, which is the same as starving.
            public int Max { get; set; } = 100;
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

        // These numbers are a composition, not tuning nudges — change one and check the whole set
        // against HudPanelOverlapEditModeTests, which computes every pair.
        //
        // Revised 2026-08-25 after live simulator validation: Eric reported the persistent HUD as
        // clumped in front of the view, and the FPV report agrees ("central vision should remain
        // mostly world"; "vitals lower-left or lower-peripheral"). The vitals readout had been at
        // X +0.40 / Y +0.14 — right of centre and ABOVE eye level, near the opposite corner from
        // what the report asks for — and is now lower-left; the hotbar narrowed from 1000 to 760
        // and dropped; the debug overlay moved right to vacate the lower-left slot.
        // HudLocalY is HEAD-relative: 0 is eye level, negative is below it. These were
        // floor-relative until the HUD was reparented from Camera Offset to the head — panels
        // followed where the player stood but not where they looked, so anything off-centre left
        // the view the moment they turned. A floor-relative 1.55 read as eye-level-plus-1.55m
        // after the reparent, which is why every value here changed at once.
        [TestCase(typeof(GameplayStatsController), 460, 250, -0.165f)]
        [TestCase(typeof(HotbarStripController), 760, 92, -0.340f)]
        [TestCase(typeof(ViewAnchorController), 64, 64, 0f)]
        [TestCase(typeof(GameplayDebugController), 520, 360, -0.02f)]
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

        // The action menu is NO LONGER part of the head-anchored family. It moved to the support
        // wrist on 2026-08-25 because a permanent bar across the lower-centre of view was the
        // substance of the crowding complaint, and the FPV report puts inventory/crafting entry
        // points in routed screens rather than in chrome.
        //
        // Asserted separately rather than deleted: this panel is the ONLY route into inventory and
        // crafting, so its placement contract is worth pinning explicitly, and a silent revert to
        // the Hud profile would put the bar straight back in front of the player.
        [Test]
        public void ActionMenuIsWristAnchoredAndStillShareTheGameplayRoute()
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(GameplayHudController), typeof(UiToolkitScreenAttribute));

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.PlacementProfile, Is.EqualTo(UiToolkitPlacementProfile.Wrist),
                "The action menu must not go back onto the head-anchored HUD.");
            Assert.That(attribute.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen),
                "It still appears and disappears with the gameplay route.");

            // Interactive by necessity — it is the only way into inventory and crafting, so unlike
            // every other panel in this family it must keep a collider for the ray to hit.
            Assert.That(attribute.NonInteractive, Is.False);

            // A forearm panel, not a 59 cm bar. Height is the looser bound because the buttons
            // stack: four 64 px controls plus margins and the screen's own padding need 350.
            // WristMenuEditModeTests pins the sizing against the labels themselves.
            Assert.That(attribute.WidthPixels, Is.LessThanOrEqualTo(320));
            Assert.That(attribute.HeightPixels, Is.LessThanOrEqualTo(400));
        }

        [Test]
        public void HealthRowFollowsVitalsTransitionsAcrossAllFourChannels()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);
            Assert.That(controller.IsBound, Is.True);

            Label value = root.Q<Label>("bv-vital-health-value");
            Label marker = root.Q<Label>("bv-vital-health-marker");
            VisualElement fill = root.Q<VisualElement>("bv-vital-health-fill");
            VisualElement row = root.Q<VisualElement>("bv-vital-health");

            // Negative control: unbound, the readout renders nothing.
            Assert.That(value.text, Is.Empty);
            Assert.That(marker.text, Is.Empty);

            var vitals = new PlayerVitals(currentHealth: 75);
            controller.Bind(vitals);

            Assert.That(value.text, Is.EqualTo("75 / 100"));
            Assert.That(FillPercentOf(fill), Is.EqualTo(75f).Within(0.01f));
            Assert.That(marker.text, Is.Empty, "An OK vital carries no marker.");
            Assert.That(row.ClassListContains("gs-vital--low"), Is.False);
            Assert.That(row.ClassListContains("gs-vital--critical"), Is.False);

            // HealthChanged drives the refresh — no cadence tick exists in EditMode.
            vitals.ApplyDamage(30);

            Assert.That(value.text, Is.EqualTo("45 / 100"));
            Assert.That(marker.text, Is.Not.Empty, "Below half must carry a marker, not colour alone.");
            Assert.That(row.ClassListContains("gs-vital--low"), Is.True);
            Assert.That(fill.ClassListContains("gs-vital__fill--low"), Is.True);

            vitals.ApplyDamage(25);

            Assert.That(value.text, Is.EqualTo("20 / 100"));
            Assert.That(row.ClassListContains("gs-vital--critical"), Is.True);
            Assert.That(row.ClassListContains("gs-vital--low"), Is.False,
                "Low and critical must be mutually exclusive, or two signals contradict each other.");
            Assert.That(fill.ClassListContains("gs-vital__fill--critical"), Is.True);
        }

        // The marker exists so a warning is never colour-only. It must differ between the two
        // warning levels, or it carries no information the colour did not already carry.
        [Test]
        public void LowAndCriticalMarkersAreDistinct()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);
            Label marker = root.Q<Label>("bv-vital-health-marker");

            var vitals = new PlayerVitals(currentHealth: 45);
            controller.Bind(vitals);
            string low = marker.text;

            vitals.ApplyDamage(25);
            string critical = marker.text;

            Assert.That(low, Is.Not.Empty);
            Assert.That(critical, Is.Not.Empty);
            Assert.That(critical, Is.Not.EqualTo(low));
        }

        [TestCase(100, 100, GameplayStatsController.VitalLevel.Ok)]
        [TestCase(51, 100, GameplayStatsController.VitalLevel.Ok)]
        [TestCase(50, 100, GameplayStatsController.VitalLevel.Low)]
        [TestCase(26, 100, GameplayStatsController.VitalLevel.Low)]
        [TestCase(25, 100, GameplayStatsController.VitalLevel.Critical)]
        [TestCase(0, 100, GameplayStatsController.VitalLevel.Critical)]
        // A max of zero is reachable: Creative has no survival vitals, so an unbound reading is
        // (0, 0). It must not read as critical, or an absent vital looks like a dying one.
        [TestCase(0, 0, GameplayStatsController.VitalLevel.Ok)]
        public void LevelThresholdsAreOnTheBoundary(int current, int max, GameplayStatsController.VitalLevel expected)
        {
            Assert.That(GameplayStatsController.LevelFor(current, max), Is.EqualTo(expected));
        }

        [Test]
        public void SurvivalVitalsRenderAsTheirOwnMeteredRows()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);

            controller.Bind(new PlayerVitals(currentHealth: 75));
            controller.BindSurvivalVitals(new FakeSurvivalVitals { Hunger = 80, Thirst = 60, Stamina = 40 });

            Assert.That(root.Q<Label>("bv-vital-hunger-value").text, Is.EqualTo("80 / 100"));
            Assert.That(root.Q<Label>("bv-vital-thirst-value").text, Is.EqualTo("60 / 100"));
            Assert.That(root.Q<Label>("bv-vital-stamina-value").text, Is.EqualTo("40 / 100"));

            Assert.That(FillPercentOf(root.Q<VisualElement>("bv-vital-stamina-fill")),
                Is.EqualTo(40f).Within(0.01f));

            // 40/100 is below half, so stamina carries the low signal while hunger does not.
            Assert.That(root.Q<VisualElement>("bv-vital-stamina").ClassListContains("gs-vital--low"), Is.True);
            Assert.That(root.Q<VisualElement>("bv-vital-hunger").ClassListContains("gs-vital--low"), Is.False);
        }

        // Flagged by review: `survivalVitals != null` alone does not detect Creative mode.
        // SurvivalVitalsRuntime.SurvivalVitalsView always returns a live SurvivalVitals instance
        // regardless of mode — switching to Creative stops TICKING it, it does not remove it — so
        // without a mode check a Creative session would render hunger/thirst/stamina from
        // stale/default values instead of hiding the rows behind gs-vital--absent.
        [Test]
        public void SurvivalRowsStayHiddenInCreativeEvenThoughTheRuntimeStillHasAVitalsInstance()
        {
            var managerObject = new GameObject("World Manager");
            objectsToDestroy.Add(managerObject);
            CreativeWorldManager manager = managerObject.AddComponent<CreativeWorldManager>();
            manager.SetGameMode(WorldGameMode.Creative);

            var syncObject = new GameObject("Survival Sync");
            objectsToDestroy.Add(syncObject);
            MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, manager);
            sync.SetMode(PlayerModeState.Creative);

            var runtimeObject = new GameObject("Vitals Runtime");
            objectsToDestroy.Add(runtimeObject);
            SurvivalVitalsRuntime runtime = runtimeObject.AddComponent<SurvivalVitalsRuntime>();
            runtime.Configure(sync, manager);

            Assert.That(runtime.IsSurvivalModeActive, Is.False,
                "fixture failed: the runtime must agree it is not in survival mode");
            Assert.That(runtime.SurvivalVitalsView, Is.Not.Null,
                "positive control: the runtime still hands back a live instance in Creative, which " +
                "is exactly the trap `survivalVitals != null` alone falls into");

            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);
            controller.Bind(new PlayerVitals(currentHealth: 75));

            // BindFromScene is invoked explicitly rather than relied on from Awake/OnShown: this
            // fixture creates the runtime and the controller in the same synchronous test method,
            // and Awake's automatic discovery does not reliably observe a scene object created
            // moments earlier in this harness. Calling the real method directly still exercises the
            // exact production binding and the exact survivalPresent computation this test targets
            // — it is the discovery TIMING being sidestepped, not the logic under test.
            MethodInfo bindFromScene = typeof(GameplayStatsController).GetMethod(
                "BindFromScene", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bindFromScene, Is.Not.Null, "BindFromScene is the seam this test targets.");
            bindFromScene.Invoke(controller, null);

            var vitalsRuntimeField = typeof(GameplayStatsController).GetField(
                "vitalsRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
            var survivalVitalsField = typeof(GameplayStatsController).GetField(
                "survivalVitals", BindingFlags.Instance | BindingFlags.NonPublic);

            // Positive control: confirm BindFromScene actually discovered the runtime and bound
            // survivalVitals through it, rather than the rows happening to read "hidden" below
            // because nothing was ever bound at all.
            Assert.That(vitalsRuntimeField.GetValue(controller), Is.Not.Null,
                "positive control failed: BindFromScene never discovered the runtime.");
            Assert.That(survivalVitalsField.GetValue(controller), Is.Not.Null,
                "positive control failed: survivalVitals was never bound.");

            controller.Refresh();

            foreach (string vital in new[] { "hunger", "thirst", "stamina" })
            {
                Assert.That(
                    root.Q<VisualElement>($"bv-vital-{vital}").ClassListContains("gs-vital--absent"),
                    Is.True,
                    $"{vital} rendered from the runtime's stale Creative-mode values instead of " +
                    "being hidden.");
            }
        }

        // Creative has health and nothing else. An unbound meter draws as an empty bar, which reads
        // as starvation rather than as "this does not apply here".
        [Test]
        public void SurvivalOnlyRowsAreHiddenWithoutSurvivalVitals()
        {
            GameplayStatsController controller = CreateScreen<GameplayStatsController>();
            VisualElement root = AttachFreshTree(controller);

            controller.Bind(new PlayerVitals(currentHealth: 75));

            Assert.That(root.Q<VisualElement>("bv-vital-health").ClassListContains("gs-vital--absent"),
                Is.False, "Health must stay visible in Creative.");

            foreach (string vital in new[] { "hunger", "thirst", "stamina" })
            {
                Assert.That(
                    root.Q<VisualElement>($"bv-vital-{vital}").ClassListContains("gs-vital--absent"),
                    Is.True,
                    $"{vital} rendered as an empty meter instead of being hidden.");
            }

            // POSITIVE CONTROL. Without this the assertions above pass just as well if the class is
            // hard-coded into the UXML and never removed — "hidden in Creative" and "hidden always"
            // are indistinguishable from the absence check alone, and the survival HUD would be
            // three blank rows with nothing failing.
            controller.BindSurvivalVitals(
                new FakeSurvivalVitals { Hunger = 80, Thirst = 60, Stamina = 40 });

            foreach (string vital in new[] { "hunger", "thirst", "stamina" })
            {
                Assert.That(
                    root.Q<VisualElement>($"bv-vital-{vital}").ClassListContains("gs-vital--absent"),
                    Is.False,
                    $"{vital} stayed hidden after survival vitals were bound — the row is never " +
                    "shown, in any mode.");
            }
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

        // Asserts the class lands ON THE ELEMENT, not that ClassFor() returns the right string.
        //
        // The severity stripe shipped half-built exactly once: ClassFor and the SeverityClasses
        // array both existed and were both public, but ApplyDisplay never called either, so every
        // toast rendered with the neutral border and nothing failed. A test written against
        // ClassFor would have been green throughout. The runtime path is the only thing worth
        // asserting here.
        [Test]
        public void StatusToastPaintsTheSeverityStripeOnTheLabel()
        {
            StatusToastController controller = CreateScreen<StatusToastController>();
            VisualElement root = AttachFreshTree(controller);
            Label label = root.Q<Label>("bv-toast-label");

            controller.ShowTimedStatus("Saved", StatusToastController.StatusSeverity.Confirmed);
            Assert.That(label.ClassListContains("hs-status--confirmed"), Is.True,
                "confirmed message must carry the moss stripe");

            // Escalating swaps the stripe rather than accumulating one.
            controller.ShowTimedStatus("Denied", StatusToastController.StatusSeverity.Rejected);
            Assert.That(label.ClassListContains("hs-status--rejected"), Is.True);
            Assert.That(label.ClassListContains("hs-status--confirmed"), Is.False,
                "the previous stripe must be removed, not layered under the new one");

            // Info spends none of the four scarce signal colours. Cleared first: while the
            // rejection is still showing, the priority gate would drop this call outright and the
            // red stripe staying put would be correct behaviour, not the absence being asserted.
            controller.SetStatusText(string.Empty);
            controller.ShowTimedStatus("Note", StatusToastController.StatusSeverity.Info);
            Assert.That(label.text, Is.EqualTo("Note"), "gate must not have suppressed this");
            Assert.That(label.ClassListContains("hs-status--rejected"), Is.False);

            // Clearing the text clears the stripe: an empty plate must not keep a coloured edge.
            controller.ShowTimedStatus("Denied", StatusToastController.StatusSeverity.Rejected);
            controller.SetStatusText(string.Empty);
            Assert.That(label.ClassListContains("hs-status--rejected"), Is.False,
                "a hidden toast must not keep its stripe for the next message to inherit");
        }

        [Test]
        public void StatusToastDropsLowerSeverityWhileHeavierMessageShows()
        {
            StatusToastController controller = CreateScreen<StatusToastController>();
            AttachFreshTree(controller);

            controller.ShowTimedStatus("Drowning", StatusToastController.StatusSeverity.Critical);
            controller.ShowTimedStatus("Inventory full", StatusToastController.StatusSeverity.Refused);
            Assert.That(controller.CurrentStatusText, Is.EqualTo("Drowning"),
                "a refusal must not erase a critical message that is still on screen");

            // Equal weight replaces: the newest message of the same severity is the relevant one.
            controller.ShowTimedStatus("Freezing", StatusToastController.StatusSeverity.Critical);
            Assert.That(controller.CurrentStatusText, Is.EqualTo("Freezing"));

            // Positive control: with nothing showing, the same low-severity call DOES land — so the
            // suppression above is the priority gate working, not a dead path.
            controller.SetStatusText(string.Empty);
            controller.ShowTimedStatus("Inventory full", StatusToastController.StatusSeverity.Refused);
            Assert.That(controller.CurrentStatusText, Is.EqualTo("Inventory full"));
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
