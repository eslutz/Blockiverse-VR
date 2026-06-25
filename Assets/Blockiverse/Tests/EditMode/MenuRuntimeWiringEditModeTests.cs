using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.VR;
using Blockiverse.WorldGen;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UiDisplayStyle = UnityEngine.UIElements.DisplayStyle;
using UiVisualElement = UnityEngine.UIElements.VisualElement;

namespace Blockiverse.Tests.EditMode
{
    public sealed class MenuRuntimeWiringEditModeTests
    {
        readonly List<UnityEngine.Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                foreach (UnityEngine.Object target in objectsToDestroy)
                    if (target != null)
                        UnityEngine.Object.DestroyImmediate(target);
                objectsToDestroy.Clear();
                BlockiverseRuntimeState.Reset();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void WorldSpacePresenterShowActivatesInactiveCanvasRoot()
        {
            GameObject panel = CreateRoot("Routed Panel");
            Canvas canvas = panel.AddComponent<Canvas>();
            canvas.enabled = false;
            BlockiverseWorldSpacePanelPresenter presenter = panel.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                canvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f);
            panel.SetActive(false);

            presenter.Show();

            Assert.That(panel.activeSelf, Is.True,
                "A routed menu root can be inactive in generated assets; Show must reactivate it before enabling the Canvas.");
            Assert.That(canvas.enabled, Is.True);

            presenter.Hide();

            Assert.That(panel.activeSelf, Is.True,
                "Hidden routed menus should keep their scripts initialized and hide through Canvas.enabled.");
            Assert.That(canvas.enabled, Is.False);
        }

        [Test]
        public void WorldSpacePresenterRecentersNonCanvasUiToolkitRoot()
        {
            GameObject headset = CreateRoot("Headset");
            headset.transform.SetPositionAndRotation(new Vector3(1.0f, 1.4f, 2.0f), Quaternion.identity);
            GameObject surface = CreateRoot("UI Toolkit Menu Surface");
            var presenter = surface.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.ConfigureWorldSpaceTarget(
                surface,
                headset.transform,
                distance: 1.25f,
                horizontalOffset: 0.0f,
                verticalOffset: -0.2f,
                pitch: 10.0f,
                scale: 0.0013f);
            surface.SetActive(false);

            presenter.Show();

            Assert.That(surface.activeSelf, Is.True);
            Assert.That(surface.transform.position.x, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(surface.transform.position.y, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(surface.transform.position.z, Is.EqualTo(3.25f).Within(0.001f));
            Assert.That(surface.transform.localScale.x, Is.EqualTo(0.0013f).Within(0.00001f));
        }

        [Test]
        public void MenuControllerDiscoversGeneratedMenusAndRoutesTitleActionsAtRuntime()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseNewWorldPanel newWorldPanel = CreateGeneratedNewWorldPanel(rig.transform);

            StartMenuController(controller);

            Assert.That(titleMenu.ActionIds[0], Is.EqualTo(MenuActions.TitleNewWorld));

            GetChildComponent<Button>(titleMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));
            Assert.That(newWorldPanel.Config, Is.Not.Null);
        }

        [Test]
        public void PendingNewWorldConfigUsesVisibleLegacyPanelWhenUiToolkitIsUnavailable()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseNewWorldPanel newWorldPanel = CreateGeneratedNewWorldPanel(rig.transform);

            StartMenuController(controller);

            GetChildComponent<Button>(titleMenu.transform, "Panel/Action 1").onClick.Invoke();
            newWorldPanel.Config.SetName("Visible Legacy World");

            Assert.That(controller.PendingNewWorldConfig, Is.SameAs(newWorldPanel.Config));
            Assert.That(controller.PendingNewWorldConfig.Name, Is.EqualTo("Visible Legacy World"));
        }

        [Test]
        public void PendingLoadSaveUsesVisibleLegacySelectionWhenUiToolkitIsUnavailable()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseLoadWorldPanel loadWorldPanel = CreateGeneratedLoadWorldPanel(rig.transform);
            WorldSaveSummary first = CreateSave("First World");
            WorldSaveSummary second = CreateSave("Second World");

            StartMenuController(controller);
            controller.SetSaveList(new[] { first, second });

            GetChildComponent<Button>(loadWorldPanel.transform, "Panel/Save 2").onClick.Invoke();

            Assert.That(loadWorldPanel.SelectedSave?.Name, Is.EqualTo("Second World"));
            Assert.That(controller.PendingLoadSave?.Name, Is.EqualTo("Second World"));
        }

        [Test]
        public void UiToolkitLoadWorldSelectionUsesNamespacedSaveId()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedLoadWorldPanel(rig.transform);
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            var visibleRoot = new UiVisualElement();
            visibleRoot.style.display = UiDisplayStyle.Flex;
            SetPrivateField(surface, "root", visibleRoot);
            controller.ConfigureUiToolkitMenuSurface(surface, useRuntimeMenus: true);
            WorldSaveSummary first = CreateSave("First World");
            WorldSaveSummary colliding = CreateSave("inventory.slot.0");

            StartMenuController(controller);
            controller.SetSaveList(new[] { first, colliding });
            controller.Router.PushScreen(new ScreenRoute(MenuActions.LoadWorldScreen, pauseGame: true));

            InvokeMenuSelection(
                controller,
                BlockiverseUiToolkitMenuCatalog.LoadWorldSaveSelectionPrefix + "inventory.slot.0");

            Assert.That(controller.PendingLoadSave?.Name, Is.EqualTo("inventory.slot.0"));
        }

        [Test]
        public void UiToolkitSurfaceAppliesPendingViewAfterVisualTreeResolves()
        {
            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            var view = new BlockiverseUiToolkitMenuView(
                MenuActions.TitleScreen,
                "Title",
                "Purpose",
                new[] { new MenuAction(MenuActions.TitleNewWorld, "New World") });

            surface.Show(view, acceptsInput: true);

            var root = new UiVisualElement();
            var actions = new UiVisualElement();
            var details = new UiVisualElement();
            var title = new UnityEngine.UIElements.Label();
            SetPrivateField(surface, "root", root);
            SetPrivateField(surface, "actionsRoot", actions);
            SetPrivateField(surface, "detailsRoot", details);
            SetPrivateField(surface, "titleLabel", title);

            EnableBehaviour(surface);

            Assert.That(title.text, Is.EqualTo("Title"));
            Assert.That(actions.childCount, Is.EqualTo(1));
            Assert.That(actions[0], Is.TypeOf<UnityEngine.UIElements.Button>());
            Assert.That(((UnityEngine.UIElements.Button)actions[0]).text, Is.EqualTo("New World"));
            Assert.That(details.childCount, Is.GreaterThan(0));
        }

        [Test]
        public void UiToolkitSurfaceButtonsApplyVisualAndHapticFeedback()
        {
            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            var haptics = surfaceObject.AddComponent<BlockiverseInteractionHaptics>();
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            SetPrivateField(surface, "interactionHaptics", haptics);

            int tickCount = 0;
            int clickCount = 0;
            var requestedPatterns = new List<BlockiverseHapticPattern>();
            haptics.UiTickRequested += () => tickCount++;
            haptics.UiClickRequested += () => clickCount++;
            haptics.PatternRequested += requestedPatterns.Add;

            var view = new BlockiverseUiToolkitMenuView(
                MenuActions.TitleScreen,
                "Title",
                "Purpose",
                new[] { new MenuAction(MenuActions.TitleNewWorld, "New World") },
                textInputs: new[] { new MenuTextInputRow("test.name", "Name", "World") },
                toggleRows: new[] { new MenuToggleRow("test.toggle", "Toggle", true) },
                sliderRows: new[] { new MenuSliderRow("test.slider", "Slider", 0.5f, 0.0f, 1.0f) });

            surface.Show(view, acceptsInput: true);

            var root = new UiVisualElement();
            var actions = new UiVisualElement();
            var details = new UiVisualElement();
            SetPrivateField(surface, "root", root);
            SetPrivateField(surface, "actionsRoot", actions);
            SetPrivateField(surface, "detailsRoot", details);

            EnableBehaviour(surface);

            Assert.That(actions.childCount, Is.EqualTo(1));
            var button = (UnityEngine.UIElements.Button)actions[0];
            AssertInteractiveFeedback(surface, button, ref tickCount, ref clickCount, requestedPatterns, clickOnPointerDown: false);

            var textField = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.TextField>(details, "test.name");
            Assert.That(textField, Is.Not.Null);
            AssertInteractiveFeedback(surface, textField, ref tickCount, ref clickCount, requestedPatterns, clickOnPointerDown: true);

            var toggle = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Toggle>(details, "test.toggle");
            Assert.That(toggle, Is.Not.Null);
            AssertInteractiveFeedback(surface, toggle, ref tickCount, ref clickCount, requestedPatterns, clickOnPointerDown: true);

            var slider = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Slider>(details, "test.slider");
            Assert.That(slider, Is.Not.Null);
            AssertInteractiveFeedback(surface, slider, ref tickCount, ref clickCount, requestedPatterns, clickOnPointerDown: true);
        }

        [Test]
        public void UiToolkitFallbackPointerConvertsLocalHitsToPanelCoordinates()
        {
            MethodInfo converter = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("LocalPointToPanelPosition", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(converter, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must expose the fallback pointer coordinate converter for this regression test.");

            Vector2 center = (Vector2)converter.Invoke(null, new object[] { Vector3.zero });
            Vector2 topLeft = (Vector2)converter.Invoke(
                null,
                new object[]
                {
                    new Vector3(
                        -BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x * 0.5f,
                        BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y * 0.5f,
                        0.0f),
                });
            Vector2 bottomRight = (Vector2)converter.Invoke(
                null,
                new object[]
                {
                    new Vector3(
                        BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x * 0.5f,
                        -BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y * 0.5f,
                        0.0f),
                });

            Assert.That(center.x, Is.EqualTo(640.0f).Within(0.001f));
            Assert.That(center.y, Is.EqualTo(360.0f).Within(0.001f));
            Assert.That(topLeft, Is.EqualTo(Vector2.zero));
            Assert.That(bottomRight.x, Is.EqualTo(1280.0f).Within(0.001f));
            Assert.That(bottomRight.y, Is.EqualTo(720.0f).Within(0.001f));
        }

        [Test]
        public void UiToolkitSurfaceRestoresRuntimeWorldSpaceColliderReference()
        {
            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            var document = surfaceObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();

            surface.Configure(document);

            BoxCollider worldSpaceCollider = surfaceObject.GetComponent<BoxCollider>();
            var serializedDocument = new SerializedObject(document);
            SerializedProperty colliderProperty = serializedDocument.FindProperty("m_WorldSpaceCollider");

            Assert.That(document.worldSpaceSizeMode, Is.EqualTo(UnityEngine.UIElements.UIDocument.WorldSpaceSizeMode.Fixed));
            Assert.That(document.worldSpaceSize.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceSize.x).Within(0.001f));
            Assert.That(document.worldSpaceSize.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceSize.y).Within(0.001f));
            Assert.That(worldSpaceCollider, Is.Not.Null);
            Assert.That(worldSpaceCollider.isTrigger, Is.True);
            Assert.That(worldSpaceCollider.size.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x).Within(0.001f));
            Assert.That(worldSpaceCollider.size.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y).Within(0.001f));
            Assert.That(colliderProperty, Is.Not.Null);
            Assert.That(colliderProperty.objectReferenceValue, Is.SameAs(worldSpaceCollider),
                "Runtime Configure must restore UIDocument's private world-space collider reference after UIDocument startup clears it.");
        }

        [Test]
        public void UiToolkitSurfaceRecentersWhenVisiblePanelDriftsOutOfReadableRange()
        {
            GameObject cameraObject = CreateRoot("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            surfaceObject.transform.position = new Vector3(0.0f, 0.0f, 10.0f);
            var presenter = surfaceObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.ConfigureWorldSpaceTarget(
                surfaceObject,
                cameraObject.transform,
                distance: 1.0f,
                horizontalOffset: 0.0f,
                verticalOffset: -0.2f,
                pitch: 0.0f,
                scale: BlockiverseUiToolkitMenuSurface.ReadableTransformScale);
            var document = surfaceObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            surface.Configure(document);
            surface.Show(new BlockiverseUiToolkitMenuView(
                MenuActions.TitleScreen,
                "Menu",
                "Choose an option.",
                new[] { new MenuAction(MenuActions.TitleNewWorld, "New World") }), acceptsInput: true);
            var visibleRoot = new UiVisualElement();
            visibleRoot.style.display = UiDisplayStyle.Flex;
            SetPrivateField(surface, "root", visibleRoot);

            InvokeBehaviourMethod(surface, "LateUpdate");

            Assert.That(surfaceObject.transform.position.x, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(surfaceObject.transform.position.y, Is.EqualTo(-0.2f).Within(0.001f));
            Assert.That(surfaceObject.transform.position.z, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(surfaceObject.transform.localScale.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableTransformScale).Within(0.00001f));
            Assert.That(document.worldSpaceSize.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceSize.x).Within(0.001f));
        }

        [Test]
        public void DeathWhilePausedRoutesToDeathScreen()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.IsGamePaused, Is.True);
        }

        [Test]
        public void MenuRouterPublishesPauseAndWorldInputState()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);

            StartMenuController(controller);

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.EnterGameplay();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.False);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);

            controller.OnMenuPressed();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
        }

        [Test]
        public void WorldLoadingRoutePausesAndKeepsOverlayNonInteractive()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            GameObject loading = CreateChild(rig.transform, "Startup Loading Overlay");
            Canvas loadingCanvas = loading.AddComponent<Canvas>();
            loadingCanvas.enabled = false;
            BlockiverseStartupOverlay startupOverlay = loading.AddComponent<BlockiverseStartupOverlay>();
            BlockiverseWorldSpacePanelPresenter loadingPresenter = loading.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            loadingPresenter.Configure(
                loadingCanvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);
            startupOverlay.Configure(loadingCanvas, loadingPresenter);

            StartMenuController(controller);
            controller.ShowWorldLoadingScreen();

            CanvasGroup loadingInput = loading.GetComponent<CanvasGroup>();
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldLoadingScreen));
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
            Assert.That(loadingCanvas.enabled, Is.True);
            Assert.That(loadingInput.interactable, Is.False);
            Assert.That(loadingInput.blocksRaycasts, Is.False);
            Assert.That(startupOverlay.HideAutomatically, Is.False);

            controller.EnterGameplay();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(loadingCanvas.enabled, Is.False);
            Assert.That(startupOverlay.HideAutomatically, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void SurvivalHudPresenterFollowsGameplayRoute()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);

            GameObject hud = CreateChild(rig.transform, "Survival HUD");
            Canvas hudCanvas = hud.AddComponent<Canvas>();
            hudCanvas.enabled = true;

            StartMenuController(controller);

            Assert.That(hud.GetComponent<BlockiverseWorldSpacePanelPresenter>(), Is.Not.Null);
            Assert.That(hudCanvas.enabled, Is.False, "Title routing must hide the gameplay HUD.");

            controller.EnterGameplay();

            Assert.That(hudCanvas.enabled, Is.True);

            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));
            Assert.That(hudCanvas.enabled, Is.False, "Pause routing must hide the gameplay HUD.");
        }

        [Test]
        public void ControllerMappingPresenterShowsOnlyUntilDismissed()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                BlockiverseWorldSpacePanelPresenter firstPresenter = CreatePresenterWithStartGate(
                    "Controller Mapping Popup First",
                    key,
                    out Canvas firstCanvas);

                StartBehaviour(firstPresenter);

                Assert.That(firstCanvas.enabled, Is.True);

                firstPresenter.Hide();

                Assert.That(firstCanvas.enabled, Is.False);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));

                BlockiverseWorldSpacePanelPresenter secondPresenter = CreatePresenterWithStartGate(
                    "Controller Mapping Popup Second",
                    key,
                    out Canvas secondCanvas);

                StartBehaviour(secondPresenter);

                Assert.That(secondCanvas.enabled, Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteOwnsFirstLaunchBeforeTitleMenu()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                AddPresenter(titleMenu.gameObject);
                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                StartMenuController(controller);

                Canvas titleCanvas = titleMenu.GetComponent<Canvas>();
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(mappingCanvas.enabled, Is.True);

                StartBehaviour(mappingPresenter);

                Assert.That(mappingCanvas.enabled, Is.True,
                    "Presenter Start must not hide a controller-mapping route already activated by the menu router.");
                Assert.That(mappingPresenter.GetComponent<CanvasGroup>().interactable, Is.True);
                Assert.That(mappingPresenter.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
                Assert.That(titleCanvas.enabled, Is.False, "The title menu must not sit in front of first-run controls.");

                controller.CloseControllerMappingScreen();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(mappingCanvas.enabled, Is.False);
                Assert.That(titleCanvas.enabled, Is.True);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteClosesQuickBlockMenuAndOwnsRaycasts()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseInputRig inputRig = rig.AddComponent<BlockiverseInputRig>();
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                AddPresenter(titleMenu.gameObject);

                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                GameObject blockMenu = CreateChild(rig.transform, "Block Menu");
                Canvas blockMenuCanvas = blockMenu.AddComponent<Canvas>();
                BlockiverseWorldSpacePanelPresenter blockMenuPresenter =
                    blockMenu.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                blockMenuPresenter.Configure(
                    blockMenuCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false);
                blockMenuPresenter.Show();

                StartMenuController(controller);

                CanvasGroup blockMenuInput = blockMenu.GetComponent<CanvasGroup>();
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(mappingCanvas.enabled, Is.True);
                Assert.That(blockMenuCanvas.enabled, Is.False,
                    "The Blocks quick menu must not remain visible behind the first-run controller map.");
                Assert.That(blockMenuInput, Is.Not.Null);
                Assert.That(blockMenuInput.interactable, Is.False);
                Assert.That(blockMenuInput.blocksRaycasts, Is.False,
                    "The Blocks quick menu must not steal tracked-device raycasts from the controller-map Close button.");

                inputRig.QuickMenuPressed.Invoke();

                Assert.That(blockMenuCanvas.enabled, Is.False,
                    "Support-grip quick-menu input must be ignored while routed menu UI owns input.");
                Assert.That(blockMenuInput.blocksRaycasts, Is.False);

                controller.CloseControllerMappingScreen();
                inputRig.QuickMenuPressed.Invoke();

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(blockMenuCanvas.enabled, Is.False,
                    "The quick block menu is gameplay-only and must not open over the title menu.");

                controller.EnterGameplay();
                inputRig.QuickMenuPressed.Invoke();

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
                Assert.That(blockMenuCanvas.enabled, Is.True);
                Assert.That(blockMenuInput.interactable, Is.True);
                Assert.That(blockMenuInput.blocksRaycasts, Is.True);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingCloseFallbackUsesEitherInteractionRay()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseInputRig inputRig = rig.AddComponent<BlockiverseInputRig>();
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                AddPresenter(titleMenu.gameObject);

                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                GameObject panel = CreateChild(controllerMapping.transform, "Panel");
                Button closeButton = AddButton(panel.transform, "Close Button");
                RectTransform closeRect = closeButton.GetComponent<RectTransform>();
                closeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 120.0f);
                closeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 48.0f);
                closeRect.localPosition = Vector3.zero;
                closeRect.localRotation = Quaternion.identity;

                GameObject rightRayObject = CreateChild(rig.transform, "Right Interaction Ray");
                XRRayInteractor rightInteractionRay = rightRayObject.AddComponent<XRRayInteractor>();
                GameObject rightRayOrigin = CreateChild(rightRayObject.transform, "Ray Origin");
                rightInteractionRay.rayOriginTransform = rightRayOrigin.transform;
                SetPrivateField(inputRig, "rightInteractionRay", rightInteractionRay);

                GameObject leftRayObject = CreateChild(rig.transform, "Left Interaction Ray");
                XRRayInteractor leftInteractionRay = leftRayObject.AddComponent<XRRayInteractor>();
                GameObject leftRayOrigin = CreateChild(leftRayObject.transform, "Ray Origin");
                leftInteractionRay.rayOriginTransform = leftRayOrigin.transform;
                SetPrivateField(inputRig, "leftInteractionRay", leftInteractionRay);
                SetPrivateField(controller, "controllerMappingCloseButton", closeButton);

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));

                Vector3 closeForward = closeRect.forward;
                rightRayOrigin.transform.SetPositionAndRotation(
                    closeRect.position + Vector3.right,
                    Quaternion.LookRotation(closeForward, Vector3.up));
                leftRayOrigin.transform.SetPositionAndRotation(
                    closeRect.position - closeForward,
                    Quaternion.LookRotation(closeForward, Vector3.up));

                inputRig.BreakPressed.Invoke();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(mappingCanvas.enabled, Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingSeenLaunchesTitleMenuDirectly()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.SetInt(key, 1);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                BlockiverseWorldSpacePanelPresenter titlePresenter = AddPresenter(titleMenu.gameObject);
                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(mappingCanvas.enabled, Is.False);
                Assert.That(titleMenu.GetComponent<Canvas>().enabled, Is.True);

                StartBehaviour(titlePresenter);

                Assert.That(titleMenu.GetComponent<Canvas>().enabled, Is.True,
                    "Presenter Start must not hide a title route already activated by the menu router.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void LanSessionEndedRouteClosesThroughRouter()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);

            StartMenuController(controller);
            controller.EnterGameplay();

            controller.ShowLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LanMultiplayerScreen));

            controller.CloseLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void LanJoinActionDoesNotRequireGeneratedLanPanel()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            controller.ConfigureUiToolkitMenuSurface(surface, useRuntimeMenus: true);

            StartMenuController(controller);
            controller.ShowLanMultiplayerScreen();
            SetPrivateField(controller, "uiToolkitLanAddress", "10.0.0.8");

            InvokeMenuAction(controller, MenuActions.LanMultiplayerJoin);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LanMultiplayerScreen));
            Assert.That(rig.transform.Find("LAN Multiplayer Panel"), Is.Null);
        }

        [Test]
        public void LanJoinActionKeepsCurrentUiToolkitAddressWhenLanSurfaceIsActive()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            var visibleRoot = new UiVisualElement();
            visibleRoot.style.display = UiDisplayStyle.Flex;
            SetPrivateField(surface, "root", visibleRoot);
            controller.ConfigureUiToolkitMenuSurface(surface, useRuntimeMenus: true);

            StartMenuController(controller);
            controller.ShowLanMultiplayerScreen();
            SetPrivateField(controller, "uiToolkitLanAddress", "10.0.0.8");

            InvokeMenuAction(controller, MenuActions.LanMultiplayerJoin);

            Assert.That(GetPrivateField<string>(controller, "uiToolkitLanAddress"), Is.EqualTo("10.0.0.8"));
            Assert.That(rig.transform.Find("LAN Multiplayer Panel"), Is.Null);
        }

        [Test]
        public void SettingsComfortPanelRoutesAndClosesThroughRouter()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu settingsMenu = CreateGeneratedActionMenu(rig.transform, "Settings Panel", 4);
            AddPresenter(settingsMenu.gameObject);

            GameObject comfortObject = CreateChild(rig.transform, "Comfort Settings Menu");
            Canvas comfortCanvas = comfortObject.AddComponent<Canvas>();
            comfortCanvas.enabled = false;
            BlockiverseComfortMenu comfortMenu = comfortObject.AddComponent<BlockiverseComfortMenu>();
            comfortMenu.Configure(comfortCanvas, rig.AddComponent<BlockiverseComfortSettings>());
            BlockiverseWorldSpacePanelPresenter comfortPresenter =
                comfortObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            comfortPresenter.Configure(
                comfortCanvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);

            StartMenuController(controller);
            controller.Router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));

            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(comfortCanvas.enabled, Is.False);

            GetChildComponent<Button>(settingsMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ComfortSettingsScreen));
            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(comfortCanvas.enabled, Is.True);
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.CloseComfortSettingsScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen));
            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(comfortCanvas.enabled, Is.False);
        }

        [Test]
        public void DeathWhileModalIsOpenClearsModalAndRoutesToDeathScreen()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            Assert.That(controller.Router.HasModal, Is.True);

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.DeathScreen));
        }

        [Test]
        public void ConfirmModalDisablesUnderlyingScreenInput()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu pauseMenu = CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);
            AddPresenter(pauseMenu.gameObject);
            AddPresenter(confirmMenu.gameObject);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            CanvasGroup pauseInput = pauseMenu.GetComponent<CanvasGroup>();
            CanvasGroup confirmInput = confirmMenu.GetComponent<CanvasGroup>();

            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(pauseMenu.GetComponent<Canvas>().enabled, Is.True, "The underlying screen remains visible.");
            Assert.That(confirmMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(pauseInput.interactable, Is.False);
            Assert.That(pauseInput.blocksRaycasts, Is.False);
            Assert.That(confirmInput.interactable, Is.True);
            Assert.That(confirmInput.blocksRaycasts, Is.True);
        }

        [Test]
        public void UiToolkitConfirmModalHidesReplacedLegacyPresenters()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);
            AddPresenter(titleMenu.gameObject);
            AddPresenter(confirmMenu.gameObject);
            GameObject uiToolkitSurface = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            controller.ConfigureUiToolkitMenuSurface(
                uiToolkitSurface.AddComponent<BlockiverseUiToolkitMenuSurface>(),
                useRuntimeMenus: true);

            StartMenuController(controller);
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(titleMenu.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(confirmMenu.GetComponent<Canvas>().enabled, Is.False);
        }

        [Test]
        public void TitleQuitOpensConfirmAndCancelSuppressesQuitAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            controller.SetSaveAvailability(latestSaveExists: true, anySaveExists: true);
            StartMenuController(controller);

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            int quitIndex = IndexOfAction(titleMenu.ActionIds, MenuActions.TitleQuit);
            Assert.That(quitIndex, Is.GreaterThanOrEqualTo(0));
            GetChildComponent<Button>(titleMenu.transform, $"Panel/Action {quitIndex + 1}").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetChildComponent<TMP_Text>(confirmMenu.transform, "Panel/Title").text,
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void PauseQuitOpensConfirmAndCancelSuppressesSaveAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu pauseMenu = CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            int quitIndex = IndexOfAction(pauseMenu.ActionIds, MenuActions.PauseQuit);
            Assert.That(quitIndex, Is.GreaterThanOrEqualTo(0));
            GetChildComponent<Button>(pauseMenu.transform, $"Panel/Action {quitIndex + 1}").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetChildComponent<TMP_Text>(confirmMenu.transform, "Panel/Title").text,
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void ClearingModalStackClearsPendingConfirmCallback()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);

            bool accepted = false;
            controller.RequestConfirm("Quit?", "Quit", "Cancel", value => accepted = value);
            controller.EnterGameplay();

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(accepted, Is.False, "A confirm callback from a cleared modal must not survive behind hidden UI.");
        }

        [Test]
        public void DeathReturnToTitleRespawnsBeforeSaveActionIsRaised()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu deathMenu = CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(vitals.Vitals.IsDead, Is.True);

            string requestedAction = null;
            bool wasAliveWhenActionRaised = false;
            controller.ActionRequested += actionId =>
            {
                requestedAction = actionId;
                wasAliveWhenActionRaised = !vitals.Vitals.IsDead;
            };

            GetChildComponent<Button>(deathMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(requestedAction, Is.EqualTo(MenuActions.DeathReturnToTitle));
            Assert.That(wasAliveWhenActionRaised, Is.True,
                "The session save handler must observe post-respawn vitals.");
            Assert.That(vitals.Vitals.IsDead, Is.False);
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
        }

        [Test]
        public void NewWorldPanelDiscoversGeneratedControlsAndEmitsActionsAtRuntime()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_Text gameModeLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Row Game Mode/Value");
            Assert.That(gameModeLabel.text, Is.EqualTo("Survival"));

            GetChildComponent<Button>(panel.transform, "Panel/Row Game Mode/Next").onClick.Invoke();
            Assert.That(gameModeLabel.text, Is.EqualTo("Creative"));

            TMP_Text textureSetLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Row Texture Set/Value");
            Assert.That(textureSetLabel.text, Is.EqualTo("Enhanced"));

            GetChildComponent<Button>(panel.transform, "Panel/Row Texture Set/Next").onClick.Invoke();
            Assert.That(textureSetLabel.text, Is.EqualTo("AI Simplified"));

            string invoked = null;
            panel.ActionRequested += id => invoked = id;

            GetChildComponent<Button>(panel.transform, "Panel/Create Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.NewWorldCreate));

            GetChildComponent<Button>(panel.transform, "Panel/Cancel Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.NewWorldCancel));
        }

        [Test]
        public void NewWorldPanelConfiguresSeedInputForEditableTextEntry()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            Assert.That(seedInput.interactable, Is.True);
            Assert.That(seedInput.readOnly, Is.False);
            Assert.That(seedInput.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            Assert.That(seedInput.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
            Assert.That(seedInput.characterValidation, Is.EqualTo(TMP_InputField.CharacterValidation.None));
            Assert.That(seedInput.keyboardType, Is.EqualTo(TouchScreenKeyboardType.Default));
            Assert.That(seedInput.GetComponent<BlockiverseSystemKeyboardField>().KeyboardType,
                Is.EqualTo(TouchScreenKeyboardType.Default));

            seedInput.text = "meadow-home";
            seedInput.onValueChanged.Invoke(seedInput.text);

            Assert.That(panel.Config.SeedText, Is.EqualTo("meadow-home"));
            Assert.That(panel.Config.Seed, Is.EqualTo(NewWorldConfig.HashSeed("meadow-home")));
        }

        [Test]
        public void NewWorldTextFieldsAcceptXrPressAndSubmitKeyboardEvents()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_InputField nameInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Name Input");
            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            AssertKeyboardFieldAcceptsXrPressEvents(nameInput);
            AssertKeyboardFieldAcceptsXrPressEvents(seedInput);
        }

        [Test]
        public void SystemKeyboardFieldOwnsOnlyOneActiveFieldAtATime()
        {
            Type keyboardFieldType = typeof(BlockiverseSystemKeyboardField);

            Assert.That(keyboardFieldType.GetProperty("ActiveField"), Is.Not.Null,
                "The Quest keyboard bridge must expose the single field that owns streamed keyboard text.");
            Assert.That(keyboardFieldType.GetProperty("AnyKeyboardVisible"), Is.Not.Null,
                "Keyboard visibility must be observable so first-person hand visuals can hide while the system keyboard is open.");
            Assert.That(keyboardFieldType.GetEvent("KeyboardVisibilityChanged"), Is.Not.Null,
                "Keyboard visibility changes must be broadcast without coupling the keyboard bridge to avatar internals.");
            Assert.That(typeof(IDeselectHandler).IsAssignableFrom(keyboardFieldType), Is.True,
                "Losing focus must release ownership so a stale field cannot keep streaming keyboard text.");
        }

        [Test]
        public void NewWorldTextFieldsUseInputBackgroundAsOnlyRaycastTarget()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            TMP_InputField nameInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Name Input");
            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            Assert.That(nameInput.textComponent.raycastTarget, Is.True,
                "Test setup should reproduce generated TMP text that can steal ray hits.");
            Assert.That(seedInput.textComponent.raycastTarget, Is.True,
                "Test setup should reproduce generated TMP text that can steal ray hits.");

            panel.ResolveRuntimeReferences();

            AssertInputFieldRaycastSetup(nameInput);
            AssertInputFieldRaycastSetup(seedInput);
        }

        [Test]
        public void NewWorldPanelCycleButtonsUseWholeButtonAsRaycastTarget()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            Button backButton = GetChildComponent<Button>(panel.transform, "Panel/Row Game Mode/Back");
            TMP_Text backLabel = GetChildComponent<TMP_Text>(backButton.transform, "Label");

            Assert.That(backLabel.raycastTarget, Is.True, "Test setup should reproduce generated labels that can steal ray hits.");

            panel.ResolveRuntimeReferences();

            Image buttonImage = backButton.GetComponent<Image>();
            Assert.That(backButton.targetGraphic, Is.SameAs(buttonImage));
            Assert.That(buttonImage.raycastTarget, Is.True);
            Assert.That(backLabel.raycastTarget, Is.False);
        }

        static void AssertKeyboardFieldAcceptsXrPressEvents(TMP_InputField input)
        {
            BlockiverseSystemKeyboardField keyboardField = input.GetComponent<BlockiverseSystemKeyboardField>();
            Assert.That(keyboardField, Is.Not.Null);
            Assert.That(keyboardField, Is.InstanceOf<IPointerDownHandler>(),
                "XR UI sends press feedback through pointer-down; text fields must open the system keyboard on that event.");
            Assert.That(keyboardField, Is.InstanceOf<ISubmitHandler>(),
                "Submit events should also open the system keyboard for controller-driven UI activation.");
        }

        static void AssertInputFieldRaycastSetup(TMP_InputField input)
        {
            Image background = input.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(input.targetGraphic, Is.SameAs(background));
            Assert.That(background.raycastTarget, Is.True);
            Assert.That(input.textComponent.raycastTarget, Is.False);
            Assert.That(((TMP_Text)input.placeholder).raycastTarget, Is.False);
        }

        [Test]
        public void LoadWorldPanelDiscoversGeneratedControlsAndEmitsActionsAtRuntime()
        {
            BlockiverseLoadWorldPanel panel = CreateGeneratedLoadWorldPanel(null);
            panel.ResolveRuntimeReferences();

            var save = new WorldSaveSummary(
                "Meadow Home",
                "918273645",
                "survival",
                "normal",
                12,
                new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

            panel.SetSaves(new[] { save });

            TMP_Text firstEntry = GetChildComponent<TMP_Text>(panel.transform, "Panel/Save 1");
            Assert.That(firstEntry.text, Does.Contain("Meadow Home"));
            Assert.That(firstEntry.text, Does.Contain("Day 12"));
            Assert.That(panel.SelectedSave?.Name, Is.EqualTo("Meadow Home"));

            string invoked = null;
            panel.ActionRequested += id => invoked = id;

            GetChildComponent<Button>(panel.transform, "Panel/Details Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldDetails));

            GetChildComponent<Button>(panel.transform, "Panel/Load Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldLoad));

            GetChildComponent<Button>(panel.transform, "Panel/Cancel Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldCancel));
        }

        [Test]
        public void LoadWorldPanelPagesBeyondGeneratedRows()
        {
            BlockiverseLoadWorldPanel panel = CreateGeneratedLoadWorldPanel(null);
            panel.ResolveRuntimeReferences();

            DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var saves = new List<WorldSaveSummary>();
            for (int i = 1; i <= 8; i++)
            {
                saves.Add(new WorldSaveSummary(
                    $"World {i}",
                    i.ToString(),
                    "survival",
                    "normal",
                    i,
                    created.AddDays(i),
                    created));
            }

            panel.SetSaves(saves);

            TMP_Text pageLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Page");
            TMP_Text firstEntry = GetChildComponent<TMP_Text>(panel.transform, "Panel/Save 1");
            Button previousPage = GetChildComponent<Button>(panel.transform, "Panel/Previous Page Button");
            Button nextPage = GetChildComponent<Button>(panel.transform, "Panel/Next Page Button");

            Assert.That(panel.PageCount, Is.EqualTo(2));
            Assert.That(pageLabel.text, Is.EqualTo("Page 1 / 2"));
            Assert.That(firstEntry.text, Does.Contain("World 8"));
            Assert.That(previousPage.interactable, Is.False);
            Assert.That(nextPage.interactable, Is.True);

            nextPage.onClick.Invoke();

            Assert.That(panel.PageIndex, Is.EqualTo(1));
            Assert.That(pageLabel.text, Is.EqualTo("Page 2 / 2"));
            Assert.That(firstEntry.text, Does.Contain("World 2"));
            Assert.That(panel.SelectedSave?.Name, Is.EqualTo("World 2"));
            Assert.That(previousPage.interactable, Is.True);
            Assert.That(nextPage.interactable, Is.False);
        }

        [Test]
        public void StationPanelDiscoversGeneratedControlsAndRoutesButtonsAtRuntime()
        {
            BlockiverseStationPanel panel = CreateGeneratedStationPanel(null);
            GameObject syncObject = CreateRoot("Station Survival Sync");
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, null, null);
            survivalSync.LocalInventory.SetSlot(0, new ItemStack(ItemId.ClayLump, 1));
            panel.ConfigureSurvivalSync(survivalSync);
            panel.ResolveRuntimeReferences();

            var station = new SmeltingStationModel(CraftingStation.ClayKiln, inputSlotCount: 1);
            station.TryDepositInput(new ItemStack(ItemId.ClayLump, 2));
            panel.Open(station, new BlockPosition(1, 2, 3));

            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Title").text, Is.EqualTo("Clay Kiln"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Input Slot 1").text, Is.EqualTo("Clay Lump ×2"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Fuel Slot").text, Is.EqualTo("No fuel"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Output Slot").text, Is.EqualTo("—"));

            GetChildComponent<Button>(panel.transform, "Panel/Deposit Input Button").onClick.Invoke();
            Assert.That(
                GetChildComponent<TMP_Text>(panel.transform, "Panel/Status").text,
                Is.EqualTo("Cannot deposit: Not a Station"));

            GetChildComponent<Button>(panel.transform, "Panel/Withdraw Input Button").onClick.Invoke();
            Assert.That(
                GetChildComponent<TMP_Text>(panel.transform, "Panel/Status").text,
                Is.EqualTo("Cannot withdraw: Not a Station"));

            bool closed = false;
            panel.CloseRequested += () => closed = true;
            GetChildComponent<Button>(panel.transform, "Panel/Close Button").onClick.Invoke();
            Assert.That(closed, Is.True);
        }

        [Test]
        public void StationPanelClosesWhenOpenStationIsRemoved()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreativeWorldManager worldManager = rig.AddComponent<CreativeWorldManager>();
            MultiplayerSurvivalSync survivalSync = rig.AddComponent<MultiplayerSurvivalSync>();
            worldManager.Configure(CreateTestChunkMaterial(), BlockiverseProject.InteractionLayerIndex);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseStationPanel panel = CreateGeneratedStationPanel(rig.transform);
            var stationPosition = new BlockPosition(2, 1, 2);

            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 6,
                height: 4,
                depth: 6,
                chunkSize: 2,
                seed: 47,
                groundHeight: 1);
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            world.SetBlock(stationPosition, BlockRegistry.ClayKiln, trackChange: false);
            worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            worldManager.SetGameMode(WorldGameMode.Survival);
            survivalSync.Configure(null, null, worldManager);

            StartMenuController(controller);
            controller.EnterGameplay();
            panel.Open(survivalSync.GetOrCreateStationModel(stationPosition, CraftingStation.ClayKiln), stationPosition);

            world.SetBlock(stationPosition, BlockRegistry.Air, trackChange: false);
            survivalSync.TickStations(1);

            Assert.That(panel.IsOpen, Is.False);
        }

        [Test]
        public void WorldDetailsMenuDiscoversGeneratedActionIdsAtRuntime()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);
            (BlockiverseWorldDetailsPanel detailsPanel, BlockiverseActionMenu detailsMenu) =
                CreateGeneratedWorldDetailsPanel(rig.transform);

            StartMenuController(controller);

            detailsPanel.ShowSave(new WorldSaveSummary(
                "Meadow Home",
                "1234",
                "survival",
                "normal",
                4,
                new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)));

            Assert.That(detailsMenu.ActionIds[0], Is.EqualTo(MenuActions.WorldDetailsPlay));
            Assert.That(GetChildComponent<TMP_Text>(detailsMenu.transform, "Panel/Action 1/Label").text, Is.EqualTo("Play"));

            string invoked = null;
            controller.ActionRequested += id => invoked = id;
            GetChildComponent<Button>(detailsMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(invoked, Is.EqualTo(MenuActions.WorldDetailsPlay));
        }

        [Test]
        public void WorldDetailsMetadataFormatsDatesWithCurrentCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            try
            {
                DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
                DateTime lastPlayed = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
                var save = new WorldSaveSummary(
                    "Meadow Home",
                    "1234",
                    "survival",
                    "normal",
                    4,
                    lastPlayed,
                    created);

                string metadata = BlockiverseWorldDetailsPanel.BuildMetadataText(save);

                Assert.That(metadata, Does.Contain(created.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Contain(lastPlayed.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Not.Contain("2026-06-01"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        BlockiverseActionMenu CreateGeneratedActionMenu(Transform parent, string name, int actionCount)
        {
            GameObject root = CreateChild(parent, name);
            BlockiverseActionMenu menu = root.AddComponent<BlockiverseActionMenu>();
            GameObject panel = CreateChild(root.transform, "Panel");
            AddText(panel.transform, "Title");
            AddText(panel.transform, "Status");

            for (int i = 0; i < actionCount; i++)
            {
                GameObject button = CreateChild(panel.transform, $"Action {i + 1}");
                button.AddComponent<Button>();
                AddText(button.transform, "Label");
            }

            return menu;
        }

        BlockiverseNewWorldPanel CreateGeneratedNewWorldPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "New World Panel");
            BlockiverseNewWorldPanel panel = root.AddComponent<BlockiverseNewWorldPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            AddInput(bg.transform, "Name Input");
            AddInput(bg.transform, "Seed Input");

            string[] rows = { "Game Mode", "Difficulty", "World Size", "World Preset", "Starting Biome", "Texture Set" };
            foreach (string row in rows)
            {
                GameObject rowRoot = CreateChild(bg.transform, $"Row {row}");
                AddButton(rowRoot.transform, "Back");
                AddText(rowRoot.transform, "Value");
                AddButton(rowRoot.transform, "Next");
            }

            AddButton(bg.transform, "Create Button");
            AddButton(bg.transform, "Cancel Button");
            AddText(bg.transform, "Error");

            return panel;
        }

        BlockiverseLoadWorldPanel CreateGeneratedLoadWorldPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "Load World Panel");
            BlockiverseLoadWorldPanel panel = root.AddComponent<BlockiverseLoadWorldPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            for (int i = 0; i < 6; i++)
                AddTextButton(bg.transform, $"Save {i + 1}");

            AddText(bg.transform, "Selection");
            AddButton(bg.transform, "Previous Page Button");
            AddText(bg.transform, "Page");
            AddButton(bg.transform, "Next Page Button");
            AddButton(bg.transform, "Load Button");
            AddButton(bg.transform, "Details Button");
            AddButton(bg.transform, "Cancel Button");

            return panel;
        }

        static WorldSaveSummary CreateSave(string name)
        {
            return new WorldSaveSummary(
                name,
                "918273645",
                "survival",
                "normal",
                12,
                new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        }

        (BlockiverseWorldDetailsPanel, BlockiverseActionMenu) CreateGeneratedWorldDetailsPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "World Details Panel");
            BlockiverseActionMenu menu = root.AddComponent<BlockiverseActionMenu>();
            BlockiverseWorldDetailsPanel detailsPanel = root.AddComponent<BlockiverseWorldDetailsPanel>();
            GameObject panel = CreateChild(root.transform, "Panel");

            AddText(panel.transform, "Title");
            AddText(panel.transform, "Status");
            TMP_Text metadata = AddText(panel.transform, "Metadata");
            TMP_InputField rename = AddInput(panel.transform, "Rename Input");

            for (int i = 0; i < 5; i++)
            {
                GameObject button = CreateChild(panel.transform, $"Action {i + 1}");
                button.AddComponent<Button>();
                AddText(button.transform, "Label");
            }

            detailsPanel.Configure(metadata, rename);
            return (detailsPanel, menu);
        }

        BlockiverseStationPanel CreateGeneratedStationPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "Station Panel");
            BlockiverseStationPanel panel = root.AddComponent<BlockiverseStationPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            AddText(bg.transform, "Title");
            AddText(bg.transform, "Input Slot 1");
            AddText(bg.transform, "Input Slot 2");
            AddText(bg.transform, "Input Slot 3");
            AddText(bg.transform, "Fuel Slot");
            AddText(bg.transform, "Output Slot");
            AddText(bg.transform, "Status");
            CreateChild(bg.transform, "Progress").AddComponent<Slider>();
            AddButton(bg.transform, "Deposit Input Button");
            AddButton(bg.transform, "Deposit Fuel Button");
            AddButton(bg.transform, "Collect Output Button");
            AddButton(bg.transform, "Withdraw Input Button");
            AddButton(bg.transform, "Withdraw Fuel Button");
            AddButton(bg.transform, "Close Button");

            return panel;
        }

        Button AddButton(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            Image image = target.AddComponent<Image>();
            image.raycastTarget = false;
            Button button = target.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text label = AddText(target.transform, "Label");
            label.raycastTarget = true;
            return button;
        }

        TMP_InputField AddInput(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            Image image = target.AddComponent<Image>();
            image.raycastTarget = true;
            var input = target.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textComponent = AddText(target.transform, "Text");
            input.placeholder = AddText(target.transform, "Placeholder");
            target.AddComponent<BlockiverseSystemKeyboardField>().Configure(input);
            return input;
        }

        TMP_Text AddText(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            return target.AddComponent<TextMeshProUGUI>();
        }

        Button AddTextButton(Transform parent, string name)
        {
            TMP_Text label = AddText(parent, name);
            return label.gameObject.AddComponent<Button>();
        }

        BlockiverseWorldSpacePanelPresenter AddPresenter(GameObject target)
        {
            Canvas canvas = target.AddComponent<Canvas>();
            var presenter = target.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                canvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);
            return presenter;
        }

        BlockiverseWorldSpacePanelPresenter CreatePresenterWithStartGate(string name, string key, out Canvas canvas)
        {
            GameObject target = CreateRoot(name);
            canvas = target.AddComponent<Canvas>();
            canvas.enabled = false;
            var presenter = target.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                canvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: true,
                showWhenStartedPlayerPrefsKey: key);
            return presenter;
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
            if (parent == null)
                objectsToDestroy.Add(target);
            else
                target.transform.SetParent(parent, false);
            return target;
        }

        T GetChildComponent<T>(Transform root, string path) where T : Component
        {
            Transform target = root.Find(path);
            Assert.That(target, Is.Not.Null, $"Missing generated child '{path}'.");
            T component = target.GetComponent<T>();
            Assert.That(component, Is.Not.Null, $"Missing {typeof(T).Name} on '{path}'.");
            return component;
        }

        static void StartMenuController(BlockiverseMenuController controller)
        {
            StartBehaviour(controller);
        }

        static void StartBehaviour(MonoBehaviour behaviour)
        {
            MethodInfo start = behaviour
                .GetType()
                .GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(start, Is.Not.Null, $"{behaviour.GetType().Name} must expose a Start method for this wiring test.");
            start.Invoke(behaviour, null);
        }

        static void EnableBehaviour(MonoBehaviour behaviour)
        {
            InvokeBehaviourMethod(behaviour, "OnEnable");
        }

        static void InvokeBehaviourMethod(MonoBehaviour behaviour, string methodName, params object[] arguments)
        {
            MethodInfo method = behaviour
                .GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{behaviour.GetType().Name} must expose a {methodName} method for this wiring test.");
            method.Invoke(behaviour, arguments == null || arguments.Length == 0 ? null : arguments);
        }

        static void SendTargetedEvent(UnityEngine.UIElements.VisualElement target, UnityEngine.UIElements.EventBase evt)
        {
            Assert.That(target, Is.Not.Null);
            try
            {
                evt.target = target;
                target.SendEvent(evt);
            }
            finally
            {
                evt.Dispose();
            }
        }

        static void AssertInteractiveFeedback(
            BlockiverseUiToolkitMenuSurface surface,
            UnityEngine.UIElements.VisualElement element,
            ref int tickCount,
            ref int clickCount,
            List<BlockiverseHapticPattern> requestedPatterns,
            bool clickOnPointerDown)
        {
            Assert.That(element, Is.Not.Null);
            Assert.That(element.ClassListContains("bv-interactive--hovered"), Is.False);
            Assert.That(element.ClassListContains("bv-interactive--pressed"), Is.False);

            int expectedTicks = tickCount + 1;
            int expectedPatternCount = requestedPatterns.Count + 1;
            InvokeBehaviourMethod(surface, "ApplyInteractiveHover", element);

            Assert.That(element.ClassListContains("bv-interactive--hovered"), Is.True,
                "Pointer enter should apply the visible hover class.");
            Assert.That(tickCount, Is.EqualTo(expectedTicks),
                "Crossing onto a UI Toolkit control should trigger the hover haptic.");
            Assert.That(requestedPatterns.Count, Is.EqualTo(expectedPatternCount));
            Assert.That(requestedPatterns[requestedPatterns.Count - 1], Is.EqualTo(BlockiverseHapticPattern.UiTick));

            InvokeBehaviourMethod(surface, "ApplyInteractiveHover", element);

            Assert.That(tickCount, Is.EqualTo(expectedTicks),
                "Staying over the same control should not repeat the boundary-crossing haptic.");
            Assert.That(requestedPatterns.Count, Is.EqualTo(expectedPatternCount));

            int expectedClicks = clickCount + 1;
            expectedPatternCount = requestedPatterns.Count + 1;
            element.AddToClassList("bv-interactive--pressed");

            Assert.That(element.ClassListContains("bv-interactive--pressed"), Is.True,
                "Pointer-down should apply the visible pressed class.");

            if (!clickOnPointerDown)
                InvokeButtonClicked((UnityEngine.UIElements.Button)element);
            else
                InvokeBehaviourMethod(surface, "PlayUiClickFeedback");

            Assert.That(clickCount, Is.EqualTo(expectedClicks),
                "Clicking or pressing a UI Toolkit control should trigger the click haptic.");
            Assert.That(requestedPatterns.Count, Is.EqualTo(expectedPatternCount));
            Assert.That(requestedPatterns[requestedPatterns.Count - 1], Is.EqualTo(BlockiverseHapticPattern.UiClick));

            element.RemoveFromClassList("bv-interactive--pressed");

            Assert.That(element.ClassListContains("bv-interactive--pressed"), Is.False,
                "Pointer-up should remove the pressed class.");

            element.RemoveFromClassList("bv-interactive--hovered");

            Assert.That(element.ClassListContains("bv-interactive--hovered"), Is.False,
                "Pointer-out cleanup should remove the hover class.");
        }

        static void InvokeButtonClicked(UnityEngine.UIElements.Button button)
        {
            Assert.That(button, Is.Not.Null);
            FieldInfo clickableField = button
                .GetType()
                .GetField("m_Clickable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clickableField, Is.Not.Null,
                $"{nameof(UnityEngine.UIElements.Button)} must expose its clickable for this wiring test.");
            var clickable = clickableField.GetValue(button);
            Assert.That(clickable, Is.Not.Null);

            FieldInfo clickedField = clickable
                .GetType()
                .GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clickedField, Is.Not.Null,
                $"{nameof(UnityEngine.UIElements.Clickable)} must expose its clicked delegate for this wiring test.");
            var callback = clickedField.GetValue(clickable) as Action;
            Assert.That(callback, Is.Not.Null);
            callback.Invoke();
        }

        static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} must expose private field '{fieldName}' for this wiring test.");
            field.SetValue(target, value);
        }

        static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name} must expose private field '{fieldName}' for this wiring test.");
            return (T)field.GetValue(target);
        }

        static void InvokeMenuAction(BlockiverseMenuController controller, string actionId)
        {
            MethodInfo handleAction = controller
                .GetType()
                .GetMethod("HandleAction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handleAction, Is.Not.Null, $"{nameof(BlockiverseMenuController)} must expose HandleAction for this wiring test.");
            handleAction.Invoke(controller, new object[] { actionId });
        }

        static void InvokeMenuSelection(BlockiverseMenuController controller, string valueId)
        {
            MethodInfo handleSelection = controller
                .GetType()
                .GetMethod("HandleUiToolkitSelectionInvoked", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handleSelection, Is.Not.Null, $"{nameof(BlockiverseMenuController)} must expose HandleUiToolkitSelectionInvoked for this wiring test.");
            handleSelection.Invoke(controller, new object[] { valueId });
        }

        Material CreateTestChunkMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null, "EditMode test requires a basic renderable shader.");

            var texture = new Texture2D(BlockVisualAtlas.AtlasWidthPixels, BlockVisualAtlas.AtlasHeightPixels);
            texture.name = BlockVisualAtlas.AuthoredAtlasName;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            var material = new Material(shader)
            {
                mainTexture = texture
            };
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            objectsToDestroy.Add(texture);
            objectsToDestroy.Add(material);
            return material;
        }

        static int IndexOfAction(IReadOnlyList<string> actionIds, string actionId)
        {
            for (int i = 0; i < actionIds.Count; i++)
                if (string.Equals(actionIds[i], actionId, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
