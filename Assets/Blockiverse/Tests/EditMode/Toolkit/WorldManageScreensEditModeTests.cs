using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // EditMode coverage for the UI Toolkit Load World and World Details screens, mirroring the
    // uGUI oracles in MenuRuntimeWiringEditModeTests (row text, "Page 1 / 2" paging,
    // culture-formatted metadata dates) against the real UXML documents. UIDocument never
    // builds rootVisualElement in EditMode, so every test instantiates the VisualTreeAsset
    // itself and drives the controller through AttachForTest plus its public seams — ClickEvent
    // cannot be raised without a live panel.
    public sealed class WorldManageScreensEditModeTests
    {
        const string LoadWorldDocumentPath = "Assets/Blockiverse/UI/Documents/LoadWorldScreen.uxml";
        const string WorldDetailsDocumentPath = "Assets/Blockiverse/UI/Documents/WorldDetailsScreen.uxml";

        static readonly DateTime CreatedUtc = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        readonly List<UnityEngine.Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object target in objectsToDestroy)
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void LoadWorldRendersSaveRowsAndSelectsNewestByDefault()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);

            screen.SetSaves(new[]
            {
                Save("Meadow Home", 12, CreatedUtc.AddDays(10)),
                Save("Old Camp", 3, CreatedUtc.AddDays(1)),
            });

            Button firstRow = root.Q<Button>("bv-save-1");
            Button secondRow = root.Q<Button>("bv-save-2");
            Button thirdRow = root.Q<Button>("bv-save-3");

            Assert.That(firstRow.text, Does.Contain("Meadow Home"));
            Assert.That(firstRow.text, Does.Contain("Day 12"));
            Assert.That(firstRow.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(secondRow.text, Does.Contain("Old Camp"));
            Assert.That(thirdRow.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(thirdRow.text, Is.Empty);

            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("Meadow Home"));
            Assert.That(firstRow.ClassListContains("hs-button--selected"), Is.True);
            Assert.That(secondRow.ClassListContains("hs-button--selected"), Is.False);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("Meadow Home"));
            Assert.That(root.Q<Button>("bv-load").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("bv-details").enabledSelf, Is.True);
        }

        [Test]
        public void LoadWorldEmptyListDisablesLoadAndDetailsAndCollapsesRows()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);

            screen.SetSaves(Array.Empty<WorldSaveSummary>());

            for (int i = 1; i <= 6; i++)
                Assert.That(root.Q<Button>($"bv-save-{i}").style.display.value, Is.EqualTo(DisplayStyle.None));

            Assert.That(screen.SelectedSave, Is.Null);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("No save selected"));
            Assert.That(root.Q<Button>("bv-load").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("bv-details").enabledSelf, Is.False);

            // Negative control: an entry click on an empty slot must not conjure a selection.
            screen.SelectEntry(0);
            Assert.That(screen.SelectedSave, Is.Null);
        }

        [Test]
        public void LoadWorldSelectionUpdatesSelectedSaveAndHighlight()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);

            screen.SetSaves(new[]
            {
                Save("World A", 5, CreatedUtc.AddDays(3)),
                Save("World B", 4, CreatedUtc.AddDays(2)),
                Save("World C", 3, CreatedUtc.AddDays(1)),
            });

            // LastPlayed-descending: row 1 = A, row 2 = B, row 3 = C.
            screen.SelectEntry(2);

            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("World C"));
            Assert.That(root.Q<Button>("bv-save-3").ClassListContains("hs-button--selected"), Is.True);
            Assert.That(root.Q<Button>("bv-save-1").ClassListContains("hs-button--selected"), Is.False);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("World C"));

            // Negative control: clicking an empty slot leaves the selection alone.
            screen.SelectEntry(5);
            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("World C"));
        }

        [Test]
        public void LoadWorldPagesSevenSavesAcrossTwoPagesWithClampedNavigation()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);

            var saves = new List<WorldSaveSummary>();
            for (int i = 1; i <= 7; i++)
                saves.Add(Save($"World {i}", i, CreatedUtc.AddDays(i)));

            screen.SetSaves(saves);

            Button previousPage = root.Q<Button>("bv-previous-page");
            Button nextPage = root.Q<Button>("bv-next-page");
            Label pageLabel = root.Q<Label>("bv-page-label");
            Button firstRow = root.Q<Button>("bv-save-1");

            Assert.That(screen.PageCount, Is.EqualTo(2));
            Assert.That(pageLabel.text, Is.EqualTo("Page 1 / 2"));
            Assert.That(pageLabel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(firstRow.text, Does.Contain("World 7"));
            Assert.That(previousPage.enabledSelf, Is.False);
            Assert.That(nextPage.enabledSelf, Is.True);

            // Clamped at the first page: no page change, no selection reset.
            screen.ChangePage(-1);
            Assert.That(screen.PageIndex, Is.EqualTo(0));
            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("World 7"));

            screen.ChangePage(1);
            Assert.That(screen.PageIndex, Is.EqualTo(1));
            Assert.That(pageLabel.text, Is.EqualTo("Page 2 / 2"));
            Assert.That(firstRow.text, Does.Contain("World 1"));
            Assert.That(root.Q<Button>("bv-save-2").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("World 1"));
            Assert.That(previousPage.enabledSelf, Is.True);
            Assert.That(nextPage.enabledSelf, Is.False);

            // Clamped at the last page.
            screen.ChangePage(1);
            Assert.That(screen.PageIndex, Is.EqualTo(1));
            Assert.That(screen.SelectedSave?.Name, Is.EqualTo("World 1"));
        }

        [Test]
        public void LoadWorldHidesPagingControlsForSinglePage()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);

            screen.SetSaves(new[]
            {
                Save("World A", 2, CreatedUtc.AddDays(2)),
                Save("World B", 1, CreatedUtc.AddDays(1)),
            });

            Assert.That(screen.PageCount, Is.EqualTo(1));
            Assert.That(root.Q<Button>("bv-previous-page").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<Button>("bv-next-page").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q<Label>("bv-page-label").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void LoadWorldStatusPushWritesStatusUntilSelectionRefreshes()
        {
            LoadWorldScreenController screen = CreateLoadWorldScreen(out VisualElement root);
            Label status = root.Q<Label>("bv-status");

            screen.SetSaves(new[] { Save("Meadow Home", 12, CreatedUtc.AddDays(10)) });

            ((IUiToolkitStatusScreen)screen).SetStatus("Load failed.");
            Assert.That(status.text, Is.EqualTo("Load failed."));

            // A selection refresh reclaims the shared label, as in the uGUI panel.
            screen.SelectEntry(0);
            Assert.That(status.text, Is.EqualTo("Meadow Home"));

            ((IUiToolkitStatusScreen)screen).SetStatus(null);
            Assert.That(status.text, Is.Empty);
        }

        [Test]
        public void WorldDetailsShowSaveRendersMetadataWithCurrentCultureDates()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            try
            {
                WorldDetailsScreenController screen = CreateWorldDetailsScreen(out VisualElement root);
                DateTime lastPlayed = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
                var save = new WorldSaveSummary("Meadow Home", "918273645", "survival", "normal", 4, lastPlayed, CreatedUtc);

                screen.ShowSave(save);

                Label metadata = root.Q<Label>("bv-metadata");
                Assert.That(metadata.text, Does.Contain(CreatedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata.text, Does.Contain(lastPlayed.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata.text, Does.Not.Contain("2026-06-01"));
                Assert.That(metadata.text, Does.Contain("918273645"));
                Assert.That(root.Q<Label>("bv-world-name").text, Is.EqualTo("Meadow Home"));
                Assert.That(root.Q<TextField>("bv-rename-field").value, Is.EqualTo("Meadow Home"));
                Assert.That(screen.CurrentSave?.Name, Is.EqualTo("Meadow Home"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void WorldDetailsMinValueDatesRenderAsDash()
        {
            WorldDetailsScreenController screen = CreateWorldDetailsScreen(out VisualElement root);
            var save = new WorldSaveSummary(
                "Fresh World", "42", "creative", "easy", 1,
                CreatedUtc.AddDays(1), DateTime.MinValue);

            screen.ShowSave(save);

            Assert.That(root.Q<Label>("bv-metadata").text, Does.Contain("—"));
        }

        [Test]
        public void WorldDetailsPendingRenameTextTracksRenameField()
        {
            WorldDetailsScreenController screen = CreateWorldDetailsScreen(out VisualElement root);
            var save = new WorldSaveSummary("Meadow Home", "1234", "survival", "normal", 4, CreatedUtc.AddDays(9), CreatedUtc);

            screen.ShowSave(save);
            Assert.That(screen.PendingRenameText, Is.EqualTo("Meadow Home"));

            root.Q<TextField>("bv-rename-field").value = "Meadow Camp";
            Assert.That(screen.PendingRenameText, Is.EqualTo("Meadow Camp"));

            screen.Clear();
            Assert.That(screen.PendingRenameText, Is.Empty);
            Assert.That(screen.CurrentSave, Is.Null);
            Assert.That(root.Q<Label>("bv-world-name").text, Is.Empty);
            Assert.That(root.Q<Label>("bv-metadata").text, Is.Empty);
        }

        [Test]
        public void WorldDetailsActionMenuRendersAndRebuildsOnEveryPush()
        {
            WorldDetailsScreenController screen = CreateWorldDetailsScreen(out VisualElement root);
            VisualElement actionList = root.Q<VisualElement>("bv-action-list");

            screen.SetActionMenu("World Details", MenuActions.WorldDetails);

            Assert.That(actionList.childCount, Is.EqualTo(5));
            Assert.That(root.Q<Button>("bv-action-1").text, Is.EqualTo("Play"));
            Assert.That(root.Q<Button>("bv-action-1").ClassListContains("hs-button"), Is.True);
            Assert.That(root.Q<Label>("bv-title").text, Is.EqualTo("World Details"));
            Assert.That(screen.ActionIds, Is.EqualTo(new[]
            {
                MenuActions.WorldDetailsPlay,
                MenuActions.WorldDetailsRename,
                MenuActions.WorldDetailsDuplicate,
                MenuActions.WorldDetailsDeleteRequested,
                MenuActions.WorldDetailsBack,
            }));

            // Every push rebuilds the list wholesale — availability changes shrink or grow it.
            screen.SetActionMenu("World Details", MenuActions.Error());

            Assert.That(actionList.childCount, Is.EqualTo(1));
            Assert.That(root.Q<Button>("bv-action-1").text, Is.EqualTo("Close"));
            Assert.That(screen.ActionIds, Is.EqualTo(new[] { MenuActions.ErrorClose }));

            Assert.That(
                () => screen.SetActionMenu("World Details", null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void WorldManageScreensDispatchThroughMenuControllerFrontend()
        {
            GameObject controllerObject = CreateRoot("Menu Controller");
            BlockiverseMenuController menuController = controllerObject.AddComponent<BlockiverseMenuController>();

            GameObject hostRoot = CreateRoot("Toolkit Menus");
            GameObject loadPanel = CreateChild(hostRoot.transform, "LoadWorldScreen Panel");
            loadPanel.AddComponent<UIDocument>();
            LoadWorldScreenController loadScreen = loadPanel.AddComponent<LoadWorldScreenController>();
            GameObject detailsPanel = CreateChild(hostRoot.transform, "WorldDetailsScreen Panel");
            detailsPanel.AddComponent<UIDocument>();
            WorldDetailsScreenController detailsScreen = detailsPanel.AddComponent<WorldDetailsScreenController>();

            UiToolkitMenuHost host = hostRoot.AddComponent<UiToolkitMenuHost>();
            host.Configure(menuController);
            InvokeLifecycle(host, "Awake");

            VisualElement loadRoot = InstantiateDocument(LoadWorldDocumentPath);
            loadScreen.AttachForTest(loadRoot);
            Assert.That(loadScreen.IsBound, Is.True, "Load World controller failed to bind its document.");
            VisualElement detailsRoot = InstantiateDocument(WorldDetailsDocumentPath);
            detailsScreen.AttachForTest(detailsRoot);
            Assert.That(detailsScreen.IsBound, Is.True, "World Details controller failed to bind its document.");

            // Start registers the frontend; the replay pushes the static world-details menu.
            InvokeLifecycle(host, "Start");

            Assert.That(detailsScreen.ActionIds, Is.EqualTo(new[]
            {
                MenuActions.WorldDetailsPlay,
                MenuActions.WorldDetailsRename,
                MenuActions.WorldDetailsDuplicate,
                MenuActions.WorldDetailsDeleteRequested,
                MenuActions.WorldDetailsBack,
            }));
            Assert.That(detailsRoot.Q<Button>("bv-action-1").text, Is.EqualTo("Play"));

            var invoked = new List<string>();
            menuController.ActionRequested += id => invoked.Add(id);

            // No saves: Load must not dispatch, mirroring the disabled uGUI button.
            host.SetSaveList(Array.Empty<WorldSaveSummary>());
            Assert.That(loadRoot.Q<Button>("bv-load").enabledSelf, Is.False);
            loadScreen.RequestLoad();
            Assert.That(invoked, Is.Empty);

            host.SetSaveList(new[] { Save("Meadow Home", 12, CreatedUtc.AddDays(10)) });
            Assert.That(loadScreen.SelectedSave?.Name, Is.EqualTo("Meadow Home"));
            loadScreen.RequestLoad();
            Assert.That(invoked, Is.EqualTo(new[] { MenuActions.LoadWorldLoad }));

            // Cancel is pure routing: it pops the load-world route without forwarding.
            menuController.Router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));
            loadScreen.RequestCancel();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            Assert.That(invoked.Count, Is.EqualTo(1));

            // Details: the selected save reaches the details screen through the frontend and
            // the router lands on world_details.
            menuController.Router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));
            loadScreen.RequestDetails();
            Assert.That(menuController.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldDetailsScreen));
            Assert.That(detailsScreen.CurrentSave?.Name, Is.EqualTo("Meadow Home"));
            Assert.That(detailsRoot.Q<Label>("bv-metadata").text, Does.Contain("918273645"));

            // The pending details save gates world_details.play forwarding.
            detailsScreen.SimulateActionClicked(0);
            Assert.That(invoked, Is.EqualTo(new[] { MenuActions.LoadWorldLoad, MenuActions.WorldDetailsPlay }));

            // Negative control: an out-of-range action click dispatches nothing.
            detailsScreen.SimulateActionClicked(99);
            Assert.That(invoked.Count, Is.EqualTo(2));
        }

        static WorldSaveSummary Save(string name, int dayCount, DateTime lastPlayedUtc) =>
            new(name, "918273645", "survival", "normal", dayCount, lastPlayedUtc, CreatedUtc);

        static VisualElement InstantiateDocument(string path)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {path}.");
            return tree.Instantiate();
        }

        LoadWorldScreenController CreateLoadWorldScreen(out VisualElement root)
        {
            GameObject panel = CreateRoot("Load World Screen Panel");
            panel.AddComponent<UIDocument>();
            LoadWorldScreenController screen = panel.AddComponent<LoadWorldScreenController>();
            root = InstantiateDocument(LoadWorldDocumentPath);
            screen.AttachForTest(root);
            Assert.That(screen.IsBound, Is.True, "Load World controller failed to bind — element names drifted.");
            return screen;
        }

        WorldDetailsScreenController CreateWorldDetailsScreen(out VisualElement root)
        {
            GameObject panel = CreateRoot("World Details Screen Panel");
            panel.AddComponent<UIDocument>();
            WorldDetailsScreenController screen = panel.AddComponent<WorldDetailsScreenController>();
            root = InstantiateDocument(WorldDetailsDocumentPath);
            screen.AttachForTest(root);
            Assert.That(screen.IsBound, Is.True, "World Details controller failed to bind — element names drifted.");
            return screen;
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        GameObject CreateChild(Transform parent, string name)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);
            return target;
        }

        // EditMode never runs MonoBehaviour lifecycle methods; the host's discovery and
        // frontend registration live in Awake/Start, so the integration test invokes them the
        // same way MenuRuntimeWiringEditModeTests starts the menu controller.
        static void InvokeLifecycle(MonoBehaviour behaviour, string methodName)
        {
            MethodInfo method = behaviour
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{behaviour.GetType().Name}.{methodName} not found.");
            method.Invoke(behaviour, null);
        }
    }
}
