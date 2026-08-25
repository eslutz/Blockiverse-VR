using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.UI;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Screen-level tests for the UI Toolkit catalog browser (matrix row 17), mirroring the
    // uGUI BlockiverseCatalogBrowserPanel behaviours: category cycling with search-clear,
    // whole-catalog search that ignores the active category, 3x4 grid paging with clamped
    // page indices, and selection into the scene CreativeHotbar. UIDocument builds no tree in
    // EditMode, so documents are instantiated directly and attached via AttachForTest; clicks
    // and text edits drive the controller's public handler seams because ChangeEvent/ClickEvent
    // dispatch needs a live panel.
    public sealed class CatalogScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/CatalogScreen.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        CatalogScreenController CreateAttachedController(out VisualElement root)
        {
            CatalogScreenController controller =
                CreateRoot("Catalog Screen Under Test").AddComponent<CatalogScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        // The uGUI panel's search filter, reproduced over the same default catalog: the grid
        // must list exactly these names in catalog order when the term is active.
        static List<string> CatalogNamesContaining(string term)
        {
            BlockRegistry registry = BlockRegistry.Default;
            CreativeCatalog catalog = CreativeCatalog.CreateDefault(registry);
            var names = new List<string>();

            foreach (CreativeCatalogEntry entry in catalog.All)
            {
                string name = registry.Get(entry.BlockId).Name;
                if (name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    names.Add(name);
            }

            return names;
        }

        static List<string> VisibleEntryTexts(VisualElement root)
        {
            var texts = new List<string>();
            for (int i = 0; i < CatalogScreenController.EntryCount; i++)
            {
                Button entry = root.Q<Button>($"bv-entry-{i + 1}");
                if (entry.style.visibility.value == Visibility.Visible)
                    texts.Add(entry.text);
            }

            return texts;
        }

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            CatalogScreenController controller = CreateAttachedController(out _);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in CatalogScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1),
                "Attach must leave exactly one callback registration.");

            // Negative control: a controller that returned true from OnAttach without querying
            // anything would also 'pass' the real document — it must fail here.
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                CatalogScreenController stray =
                    CreateRoot("Empty Tree Control").AddComponent<CatalogScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False,
                    "An empty tree has none of the screen's elements; IsBound must be false.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void DefaultCategoryRendersTheTerrainGridPageLabelAndPlaceholder()
        {
            CatalogScreenController controller = CreateAttachedController(out VisualElement root);

            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Terrain"),
                "The first catalog category renders by its display name.");
            Assert.That(root.Q<Label>("bv-page-label").text, Is.EqualTo("1/1"),
                "Eleven terrain entries fit one twelve-slot page.");

            BlockRegistry registry = BlockRegistry.Default;
            List<string> visible = VisibleEntryTexts(root);
            // Eleven since snow_block joined Terrain with the vegetation blocks. Pinned rather
            // than derived from the catalog on purpose: the point is to notice when the category
            // changes size, which is exactly what caught this.
            Assert.That(visible.Count, Is.EqualTo(11),
                "The Terrain category has exactly eleven catalog entries.");
            Assert.That(visible[0], Is.EqualTo(registry.Get(BlockRegistry.MeadowTurf).Name));
            // Positive control: a do-nothing Refresh would leave every entry blank.
            Assert.That(visible[0], Is.Not.Empty);
            Assert.That(root.Q<Button>("bv-entry-11").style.visibility.value, Is.EqualTo(Visibility.Visible),
                "The eleventh slot now holds an entry.");
            Assert.That(root.Q<Button>("bv-entry-12").style.visibility.value, Is.EqualTo(Visibility.Hidden),
                "Unused grid slots hide without collapsing so entries never reflow.");

            string placeholder = root.Q<TextField>("bv-search").textEdition.placeholder;
            Assert.That(placeholder, Is.EqualTo(UiText.Get("ui.generated.blocks.search_placeholder")));
            Assert.That(placeholder, Does.Contain("Search blocks"),
                "The placeholder must resolve to the English table value, not echo the key.");
        }

        [Test]
        public void CycleCategoryAdvancesWrapsAndClearsTheSearchFilter()
        {
            CatalogScreenController controller = CreateAttachedController(out VisualElement root);
            TextField search = root.Q<TextField>("bv-search");

            search.value = "Turf";
            controller.ApplySearchFilter();
            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Search"),
                "An active filter replaces the category name with the search marker.");

            controller.CycleCategory();

            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Stone"),
                "Cycling advances to the next category and drops the filter.");
            Assert.That(search.value, Is.Empty,
                "Cycling clears the search field without re-firing its callback.");

            // 13 more cycles wrap all 14 categories back to the first.
            for (int i = 0; i < 13; i++)
                controller.CycleCategory();

            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Terrain"));
        }

        [Test]
        public void SearchSpansTheWholeCatalogRegardlessOfActiveCategory()
        {
            CatalogScreenController controller = CreateAttachedController(out VisualElement root);
            controller.CycleCategory();
            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Stone"));

            root.Q<TextField>("bv-search").value = "Turf";
            controller.ApplySearchFilter();

            List<string> expected = CatalogNamesContaining("Turf");
            Assert.That(expected.Count, Is.GreaterThanOrEqualTo(3),
                "Positive control: the default catalog has several Turf blocks.");
            Assert.That(expected.Count, Is.LessThanOrEqualTo(CatalogScreenController.EntryCount));
            Assert.That(VisibleEntryTexts(root), Is.EqualTo(expected),
                "Search lists whole-catalog matches in catalog order.");

            // The matches are Terrain blocks while Stone is the active category — the filter
            // must ignore the category entirely.
            BlockRegistry registry = BlockRegistry.Default;
            var stoneNames = new List<string>();
            foreach (CreativeCatalogEntry entry in
                CreativeCatalog.CreateDefault(registry).InCategory(CreativeCatalogCategory.Stone))
            {
                stoneNames.Add(registry.Get(entry.BlockId).Name);
            }

            Assert.That(expected, Is.Not.SubsetOf(stoneNames));

            // Whitespace is not a filter: the category listing returns.
            root.Q<TextField>("bv-search").value = "   ";
            controller.ApplySearchFilter();
            Assert.That(root.Q<Label>("bv-category-value").text, Is.EqualTo("Stone"));
        }

        [Test]
        public void PagingSlicesSearchResultsAndClampsAtBothEnds()
        {
            CatalogScreenController controller = CreateAttachedController(out VisualElement root);
            Label pageLabel = root.Q<Label>("bv-page-label");

            root.Q<TextField>("bv-search").value = "e";
            controller.ApplySearchFilter();

            List<string> expected = CatalogNamesContaining("e");
            Assert.That(expected.Count, Is.GreaterThan(CatalogScreenController.EntryCount),
                "Positive control: the broad term must overflow one page for paging to be exercised.");
            int pageCount = (expected.Count + CatalogScreenController.EntryCount - 1) /
                CatalogScreenController.EntryCount;

            Assert.That(pageLabel.text, Is.EqualTo($"1/{pageCount}"));
            Assert.That(root.Q<Button>("bv-entry-1").text, Is.EqualTo(expected[0]));

            controller.PreviousPage();
            Assert.That(pageLabel.text, Is.EqualTo($"1/{pageCount}"),
                "Backing up from the first page clamps.");

            controller.NextPage();
            Assert.That(pageLabel.text, Is.EqualTo($"2/{pageCount}"));
            Assert.That(root.Q<Button>("bv-entry-1").text,
                Is.EqualTo(expected[CatalogScreenController.EntryCount]),
                "The second page starts at the thirteenth match.");

            for (int i = 0; i < pageCount; i++)
                controller.NextPage();
            Assert.That(pageLabel.text, Is.EqualTo($"{pageCount}/{pageCount}"),
                "Advancing past the last page clamps.");
        }

        [Test]
        public void EntryClickSelectsTheBlockIntoTheCreativeHotbar()
        {
            CatalogScreenController controller = CreateAttachedController(out _);
            CreativeHotbar hotbar = CreateRoot("Creative Hotbar").AddComponent<CreativeHotbar>();
            hotbar.ConfigureFromDefaultCatalog(null);
            controller.ConfigureCatalog(hotbar);

            Assert.That(hotbar.SelectedBlockId, Is.EqualTo(BlockRegistry.MeadowTurf),
                "Sanity: the default catalog hotbar starts on its first entry.");

            controller.SelectEntry(1);
            Assert.That(hotbar.SelectedBlockId, Is.EqualTo(BlockRegistry.LooseLoam),
                "Picking the second Terrain entry selects it in the hotbar.");

            // Negative controls: empty grid slots and junk indices change nothing. Index 11 is the
            // first empty slot now that Terrain holds eleven entries — 10 became a real entry when
            // snow_block joined the category, which quietly turned this control into a no-op test.
            controller.SelectEntry(11);
            controller.SelectEntry(-1);
            Assert.That(hotbar.SelectedBlockId, Is.EqualTo(BlockRegistry.LooseLoam));
        }

        [Test]
        public void CloseRoutesBackThroughCloseCatalogScreen()
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            BlockiverseMenuController menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            menu.RegisterFrontend(host);

            CatalogScreenController controller = CreateAttachedController(out _);
            controller.ConfigureHost(host);

            menu.OpenCatalogScreen();
            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.CatalogScreen));

            controller.SubmitClose();

            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen),
                "Close pops the catalog route exactly like the uGUI close button.");
        }
    }

    // Screen-level mirror of the CreativeWorldSwitchStateEditModeTests oracle on the UI Toolkit
    // creative tools screen (matrix row 18): authority gating (offline + Creative + world only),
    // host-only time/weather with slider revert-before-report, no-echo environment refresh,
    // undo/redo and clipboard state transitions, and world-swap resets. Slider ChangeEvents do
    // not dispatch without a live panel in EditMode, so the tests drive the controller's public
    // handler seams (ApplyTimeOfDay/ApplyTimeScale) after setting element values.
    public sealed class CreativeToolsScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/CreativeToolsScreen.uxml";
        const string CornersKey = "ui.status.creative.corners";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        CreativeToolsScreenController CreateAttachedController(out VisualElement root)
        {
            CreativeToolsScreenController controller =
                CreateRoot("Creative Tools Screen Under Test").AddComponent<CreativeToolsScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        static void ConfigureWorldManager(
            CreativeWorldManager manager,
            CreativeInteractionController controller = null)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);
            Assert.That(material, Is.Not.Null,
                "Creative world tests should use the committed authored chunk material.");
            BlockiverseWorldPresentation.Attach(manager, material, layer: -1, controller: controller);
        }

        // The screen tracks the aim in its Unity Update callback exactly like the uGUI panel;
        // EditMode never ticks it, so the oracle's reflection seam is reused.
        static void InvokeControllerUpdate(CreativeToolsScreenController controller)
        {
            MethodInfo method = typeof(CreativeToolsScreenController).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null,
                "Creative tools screen should expose the expected Unity update callback.");
            method.Invoke(controller, null);
        }

        static void AimAndSetBothCorners(
            CreativeToolsScreenController controller,
            CreativeInteractionController interaction,
            BlockPosition target)
        {
            interaction.UpdatePreview(target, Vector3.up);
            InvokeControllerUpdate(controller);
            controller.SetCornerA();
            controller.SetCornerB();
        }

        [Test]
        public void AttachBindsEveryElementSeedsTheInitialStatusAndAnEmptyTreeRefusesToBind()
        {
            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in CreativeToolsScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));

            Assert.That(root.Q<Label>("bv-status").text,
                Is.EqualTo("Aim at blocks to select corners."),
                "Attach seeds the uGUI panel's authored initial status line.");
            Assert.That(root.Q<Label>("bv-corners").text,
                Is.EqualTo(UiText.Format(CornersKey, "—", "—")),
                "Attach renders the empty corners readout.");

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                CreativeToolsScreenController stray =
                    CreateRoot("Empty Tree Control").AddComponent<CreativeToolsScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void RegionOperationsAreRefusedWithoutALoadedWorld()
        {
            CreativeInteractionController interaction =
                CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ConfigureCreativeTools(interaction, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => false);

            AimAndSetBothCorners(controller, interaction, new BlockPosition(1, 1, 1));
            controller.FillRegion();

            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("No world loaded."));
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(0),
                "A refused operation must not enter the undo history.");
        }

        [Test]
        public void RegionOperationsAreGatedToOfflineCreativeAndSucceedOnlyThere()
        {
            CreativeInteractionController interaction =
                CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager, interaction);
            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            bool sessionActive = false;
            controller.ConfigureCreativeTools(interaction, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => sessionActive);

            GeneratedCreativeWorld generated = CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 21);
            var target = new BlockPosition(1, 1, 1);
            generated.World.SetBlock(target, BlockRegistry.Graystone);
            manager.InitializeGeneratedWorld(generated);
            manager.SetGameMode(WorldGameMode.Survival);
            AimAndSetBothCorners(controller, interaction, target);
            Label status = root.Q<Label>("bv-status");

            controller.FillRegion();
            Assert.That(status.text, Is.EqualTo("Region tools work in creative worlds only."));
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(0));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Graystone),
                "A mode-refused fill must not touch the world.");

            manager.SetGameMode(WorldGameMode.Creative);
            sessionActive = true;
            controller.CopyRegion();
            Assert.That(status.text, Is.EqualTo("Region tools are unavailable during a LAN session."));
            Assert.That(controller.HasWorldEditClipboard, Is.False);

            // Positive control: the identical calls succeed once every gate opens.
            sessionActive = false;
            controller.CopyRegion();
            Assert.That(status.text, Is.EqualTo("Copy done."));
            Assert.That(controller.HasWorldEditClipboard, Is.True);

            controller.DeleteRegion();
            Assert.That(status.text, Is.EqualTo("Delete done."));
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(1));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Air));
        }

        [Test]
        public void TimeControlsRevertTheSliderBeforeReportingDuringLanSession()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            bool sessionActive = true;
            controller.ConfigureCreativeTools(null, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => sessionActive);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 23));
            WorldTimeClock activeClock = manager.WorldTimeClock;
            Assert.That(activeClock, Is.Not.Null);
            activeClock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);
            controller.RefreshEnvironmentControls();

            Slider timeOfDay = root.Q<Slider>("bv-time-of-day");
            Slider daySpeed = root.Q<Slider>("bv-day-speed");
            timeOfDay.value = 0.75f;
            controller.ApplyTimeOfDay(0.75f);
            daySpeed.value = 3.0f;
            controller.ApplyTimeScale(3.0f);

            Assert.That(activeClock.NormalizedTime, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(activeClock.TimeScale, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(timeOfDay.value, Is.EqualTo(0.25f).Within(0.0001f),
                "The refused slider is reverted before the refusal is reported.");
            Assert.That(daySpeed.value, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(root.Q<Label>("bv-status").text,
                Is.EqualTo("Time controls are host/offline only."));

            // Positive control: offline, the same seam drives the clock.
            sessionActive = false;
            controller.ApplyTimeOfDay(0.6f);
            Assert.That(activeClock.NormalizedTime, Is.EqualTo(0.6f).Within(0.0001f));
        }

        [Test]
        public void RefreshEnvironmentControlsRepullsLiveValuesWithoutNotifying()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            // Session active: had the refresh routed through the slider handlers, the host-only
            // refusal would overwrite the status — the untouched status is the no-echo signal.
            controller.ConfigureCreativeTools(null, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => true);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 24));
            WorldTimeClock activeClock = manager.WorldTimeClock;
            activeClock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            Slider timeOfDay = root.Q<Slider>("bv-time-of-day");
            Slider daySpeed = root.Q<Slider>("bv-day-speed");
            timeOfDay.value = 0.9f;
            daySpeed.value = 7.0f;

            controller.RefreshEnvironmentControls();

            Assert.That(timeOfDay.value, Is.EqualTo(0.25f).Within(0.0001f),
                "Refresh re-pulls the live clock into the control.");
            Assert.That(daySpeed.value, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(activeClock.NormalizedTime, Is.EqualTo(0.25f).Within(0.0001f),
                "Refresh must never write control values back into the clock.");
            Assert.That(root.Q<Label>("bv-status").text,
                Is.EqualTo("Aim at blocks to select corners."),
                "Refresh must not fire the sliders' change handlers (no host-only refusal).");
            Assert.That(root.Q<Label>("bv-weather").text, Does.StartWith("Weather: "));
        }

        [Test]
        public void DayCycleToggleAndWeatherAreHostGatedAndTransitionOffline()
        {
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager);
            WorldTimeClock clock = manager.gameObject.AddComponent<WorldTimeClock>();
            clock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);

            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            bool sessionActive = false;
            controller.ConfigureCreativeTools(null, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => sessionActive);

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 26));
            WorldTimeClock activeClock = manager.WorldTimeClock;
            Assert.That(activeClock, Is.Not.Null);
            activeClock.Configure(
                WorldTimeClock.DefaultDayLengthSeconds,
                startNormalizedTime: 0.25f,
                timeScale: 1.0f);
            controller.RefreshEnvironmentControls();
            Label status = root.Q<Label>("bv-status");

            controller.ToggleDayNightCycle();
            float frozenTime = activeClock.NormalizedTime;
            activeClock.AdvanceRuntime(60.0f);

            Assert.That(activeClock.TimeScale, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(activeClock.NormalizedTime, Is.EqualTo(frozenTime).Within(0.0001f));
            Assert.That(status.text, Is.EqualTo("Day/night cycle paused."));

            controller.ToggleDayNightCycle();
            Assert.That(activeClock.TimeScale, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(status.text, Is.EqualTo("Day/night cycle resumed."));

            WeatherState before = manager.GetWeatherSyncState().State;
            controller.CycleWeather();
            Assert.That(manager.GetWeatherSyncState().State, Is.Not.EqualTo(before));
            Assert.That(root.Q<Label>("bv-weather").text, Does.StartWith("Weather: "));

            // Host gates: in a session neither control reaches the world.
            sessionActive = true;
            WeatherState gated = manager.GetWeatherSyncState().State;
            controller.CycleWeather();
            Assert.That(status.text, Is.EqualTo("Weather control is host/offline only."));
            Assert.That(manager.GetWeatherSyncState().State, Is.EqualTo(gated));

            controller.ToggleDayNightCycle();
            Assert.That(status.text, Is.EqualTo("Time controls are host/offline only."));
            Assert.That(activeClock.TimeScale, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void UndoRedoHistoryClipboardAndCornersFollowTheOracleAcrossAWorldSwap()
        {
            CreativeInteractionController interaction =
                CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager, interaction);
            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ConfigureCreativeTools(interaction, manager, null);
            controller.ConfigureNetworkSessionActiveProvider(() => false);

            GeneratedCreativeWorld firstWorld = CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 21);
            var target = new BlockPosition(1, 1, 1);
            firstWorld.World.SetBlock(target, BlockRegistry.Graystone);
            manager.InitializeGeneratedWorld(firstWorld);
            AimAndSetBothCorners(controller, interaction, target);
            Label status = root.Q<Label>("bv-status");

            controller.UndoEdit();
            Assert.That(status.text, Is.EqualTo("Nothing to undo."),
                "Undo on an empty history reports instead of mutating.");

            controller.CopyRegion();
            controller.DeleteRegion();
            Assert.That(controller.HasWorldEditClipboard, Is.True);
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(1));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Air));

            controller.UndoEdit();
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(0));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Graystone),
                "Undo restores the deleted block.");
            Assert.That(status.text, Is.EqualTo("Undo Edit done."));

            controller.RedoEdit();
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(1));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Air),
                "Redo re-applies the delete.");
            Assert.That(status.text, Is.EqualTo("Redo Edit done."));

            manager.InitializeGeneratedWorld(CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 22));
            InvokeControllerUpdate(controller);

            Assert.That(controller.HasWorldEditClipboard, Is.False,
                "Clipboard never survives a world swap.");
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(0),
                "Undo history never survives a world swap.");
            Assert.That(root.Q<Label>("bv-corners").text,
                Is.EqualTo(UiText.Format(CornersKey, "—", "—")),
                "Corners reset with the world.");
        }

        [Test]
        public void DestructiveRegionOperationsConfirmThroughTheMenuControllerAndMutateOnlyOnAccept()
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            BlockiverseMenuController menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            menu.RegisterFrontend(host);

            CreativeInteractionController interaction =
                CreateRoot("Creative Controller").AddComponent<CreativeInteractionController>();
            CreativeWorldManager manager = CreateRoot("World Manager").AddComponent<CreativeWorldManager>();
            ConfigureWorldManager(manager, interaction);
            CreativeHotbar hotbar = CreateRoot("Creative Hotbar").AddComponent<CreativeHotbar>();
            hotbar.ConfigureFromDefaultCatalog(null);

            CreativeToolsScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ConfigureHost(host);
            controller.ConfigureCreativeTools(interaction, manager, hotbar);
            controller.ConfigureNetworkSessionActiveProvider(() => false);

            GeneratedCreativeWorld generated = CreativeWorldManager.CreateDefaultGeneratedWorld(seed: 31);
            var target = new BlockPosition(1, 1, 1);
            generated.World.SetBlock(target, BlockRegistry.Graystone);
            manager.InitializeGeneratedWorld(generated);
            AimAndSetBothCorners(controller, interaction, target);

            controller.FillRegion();

            Assert.That(menu.Router.HasModal, Is.True,
                "Fill must request confirmation before touching the world.");
            Assert.That(menu.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(BlockRegistry.Graystone),
                "Nothing mutates while the confirm modal is open.");
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(0));

            menu.DispatchAction(MenuActions.ConfirmAccept);

            Assert.That(menu.Router.HasModal, Is.False);
            Assert.That(manager.World.GetBlock(target), Is.EqualTo(hotbar.SelectedBlockId),
                "Accepting executes the fill with the hotbar selection.");
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(1));
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("Fill done."));

            controller.FillRegion();
            Assert.That(menu.Router.HasModal, Is.True);
            menu.DispatchAction(MenuActions.ConfirmCancel);
            Assert.That(controller.WorldEditUndoCount, Is.EqualTo(1),
                "Cancelling the confirm must execute nothing.");
        }

        [Test]
        public void CloseDispatchesCreativeToolsCloseThroughTheRouter()
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            BlockiverseMenuController menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            menu.RegisterFrontend(host);

            CreativeToolsScreenController controller = CreateAttachedController(out _);
            controller.ConfigureHost(host);

            menu.DispatchAction(MenuActions.PauseCreativeTools);
            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.CreativeToolsScreen));

            controller.SubmitClose();

            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen),
                "creative_tools.close routes as a screen pop, verbatim id required.");
        }
    }
}
