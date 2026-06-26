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
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
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
        public void WorldSpacePresenterShowActivatesInactiveWorldSpaceRoot()
        {
            GameObject panel = CreateRoot("UI Toolkit Menu Presenter");
            BlockiverseUiToolkitMenuPresenter presenter = panel.AddComponent<BlockiverseUiToolkitMenuPresenter>();
            presenter.ConfigureWorldSpaceTarget(
                panel,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f);
            panel.SetActive(false);

            presenter.Show();

            Assert.That(panel.activeSelf, Is.True,
                "A world-space UI Toolkit root can be inactive in generated assets; Show must reactivate it.");

            presenter.Hide();

            Assert.That(panel.activeSelf, Is.False);
        }

        [Test]
        public void UiToolkitPresenterShowPreservesFixedMenuSurfaceTransform()
        {
            GameObject headset = CreateRoot("Headset");
            headset.transform.SetPositionAndRotation(new Vector3(1.0f, 1.4f, 2.0f), Quaternion.identity);
            GameObject surface = CreateRoot("UI Toolkit Menu Surface");
            surface.transform.SetPositionAndRotation(new Vector3(0.25f, 1.1f, 0.75f), Quaternion.Euler(4.0f, 12.0f, 0.0f));
            var presenter = surface.AddComponent<BlockiverseUiToolkitMenuPresenter>();
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
            Assert.That(surface.transform.position.x, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(surface.transform.position.y, Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(surface.transform.position.z, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(surface.transform.rotation.eulerAngles.y, Is.EqualTo(12.0f).Within(0.001f));
        }

        [Test]
        public void MenuControllerRoutesTitleActionsFromUiToolkitRuntime()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.SetInt(key, 1);
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

            try
            {
                StartMenuController(controller);

                InvokeMenuAction(controller, MenuActions.TitleNewWorld);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));
                Assert.That(controller.PendingNewWorldConfig, Is.Not.Null);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void UiToolkitLoadWorldSelectionUsesNamespacedSaveId()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            var visibleRoot = new UiVisualElement();
            visibleRoot.style.display = UiDisplayStyle.Flex;
            SetPrivateField(surface, "root", visibleRoot);
            controller.ConfigureUiToolkitMenuSurface(surface);
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
        public void UiToolkitXriPointerConvertsLocalHitsToPanelCoordinates()
        {
            MethodInfo converter = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("LocalPointToPanelPosition", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo inverseConverter = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("PanelPositionToLocalPoint", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo xrRayXRIBridge = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("TryGetTargetFromRayInteractor", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo planeRayXRIBridge = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("TryGetPanelPositionFromWorldRay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo linePointXRIBridge = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("TryGetPanelPositionFromLinePoints", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo boundsXRIBridge = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("FindXriInteractiveElementAt", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo inputRigXRIBridge = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("TryGetTargetFromInputRigRays", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(converter, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must expose the XRI pointer coordinate converter for this regression test.");
            Assert.That(inverseConverter, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must expose the inverse converter so Quest smoke validation can raycast to real button centers.");
            Assert.That(xrRayXRIBridge, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must support the native XRRayInteractor XRI bridge used by the generated Quest rig.");
            Assert.That(planeRayXRIBridge, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must convert direct XR rays to panel coordinates when physics misses the UI Toolkit collider.");
            Assert.That(linePointXRIBridge, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must use XRI line points so XRI bridge hits match the visible controller ray.");
            Assert.That(boundsXRIBridge, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must resolve registered control bounds when Unity's runtime panel pick returns null on Quest.");
            Assert.That(inputRigXRIBridge, Is.Not.Null,
                $"{nameof(BlockiverseUiToolkitMenuSurface)} must resolve targets from the visible BlockiverseInputRig ray pose when XRI hit caches are stale.");

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

            Vector3 localTopLeft = (Vector3)inverseConverter.Invoke(null, new object[] { Vector2.zero });
            Vector3 localBottomRight = (Vector3)inverseConverter.Invoke(null, new object[] { new Vector2(1280.0f, 720.0f) });

            Assert.That(localTopLeft.x, Is.EqualTo(-BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x * 0.5f).Within(0.001f));
            Assert.That(localTopLeft.y, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y * 0.5f).Within(0.001f));
            Assert.That(localBottomRight.x, Is.EqualTo(BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.x * 0.5f).Within(0.001f));
            Assert.That(localBottomRight.y, Is.EqualTo(-BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize.y * 0.5f).Within(0.001f));

            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            object[] centerRayArgs =
            {
                new Ray(new Vector3(0.0f, 0.0f, -1.0f), Vector3.forward),
                2.0f,
                default(Vector2),
            };
            object[] outsideRayArgs =
            {
                new Ray(new Vector3(20.0f, 0.0f, -1.0f), Vector3.forward),
                2.0f,
                default(Vector2),
            };

            Assert.That((bool)planeRayXRIBridge.Invoke(surface, centerRayArgs), Is.True);
            Vector2 rayPanelCenter = (Vector2)centerRayArgs[2];
            Assert.That(rayPanelCenter.x, Is.EqualTo(640.0f).Within(0.001f));
            Assert.That(rayPanelCenter.y, Is.EqualTo(360.0f).Within(0.001f));
            Assert.That((bool)planeRayXRIBridge.Invoke(surface, outsideRayArgs), Is.False);

            Vector3[] centerLinePoints =
            {
                new(0.0f, 0.0f, -1.0f),
                new(0.0f, 0.0f, 1.0f),
            };
            Vector3[] outsideLinePoints =
            {
                new(20.0f, 0.0f, -1.0f),
                new(20.0f, 0.0f, 1.0f),
            };
            object[] centerLineArgs =
            {
                centerLinePoints,
                centerLinePoints.Length,
                default(Vector2),
            };
            object[] outsideLineArgs =
            {
                outsideLinePoints,
                outsideLinePoints.Length,
                default(Vector2),
            };

            Assert.That((bool)linePointXRIBridge.Invoke(surface, centerLineArgs), Is.True);
            Vector2 linePanelCenter = (Vector2)centerLineArgs[2];
            Assert.That(linePanelCenter.x, Is.EqualTo(640.0f).Within(0.001f));
            Assert.That(linePanelCenter.y, Is.EqualTo(360.0f).Within(0.001f));
            Assert.That((bool)linePointXRIBridge.Invoke(surface, outsideLineArgs), Is.False);
        }

        [Test]
        public void UiToolkitXriPointerBridgeRequiresEnabledNativeUiInteraction()
        {
            MethodInfo shouldConsiderRay = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("ShouldConsiderXriRayInteractor", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(shouldConsiderRay, Is.Not.Null);

            GameObject rayObject = CreateRoot("XRI Menu Ray");
            XRRayInteractor rayInteractor = rayObject.AddComponent<XRRayInteractor>();
            rayInteractor.enableUIInteraction = false;

            Assert.That((bool)shouldConsiderRay.Invoke(null, new object[] { rayInteractor }), Is.False,
                "Menu interaction must use an enabled XRI UI interactor.");

            rayInteractor.enableUIInteraction = true;

            Assert.That((bool)shouldConsiderRay.Invoke(null, new object[] { rayInteractor }), Is.True);

            rayObject.SetActive(false);

            Assert.That((bool)shouldConsiderRay.Invoke(null, new object[] { rayInteractor }), Is.False);
        }

        [Test]
        public void UiToolkitInputRigPointerBridgeUsesConfiguredPoseWhileMenuOwnsInput()
        {
            MethodInfo shouldConsiderInputRigRay = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("ShouldConsiderInputRigRayInteractor", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(shouldConsiderInputRigRay, Is.Not.Null);

            GameObject rayObject = CreateRoot("Generated Input Rig Ray");
            XRRayInteractor rayInteractor = rayObject.AddComponent<XRRayInteractor>();
            rayInteractor.enableUIInteraction = true;

            Assert.That((bool)shouldConsiderInputRigRay.Invoke(null, new object[] { rayInteractor }), Is.False,
                "The input-rig pose bridge must stay inactive while gameplay owns input.");

            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false, menuInputActive: true);

            Assert.That((bool)shouldConsiderInputRigRay.Invoke(null, new object[] { rayInteractor }), Is.True);

            rayObject.SetActive(false);

            Assert.That((bool)shouldConsiderInputRigRay.Invoke(null, new object[] { rayInteractor }), Is.True,
                "The generated input-rig pose bridge should survive transient XRI ray GameObject deactivation while the UI Toolkit menu owns input.");

            rayInteractor.enableUIInteraction = false;

            Assert.That((bool)shouldConsiderInputRigRay.Invoke(null, new object[] { rayInteractor }), Is.False,
                "A ray explicitly configured without UI interaction must not drive the menu pose bridge.");
        }

        [Test]
        public void UiToolkitXriPointerBridgeDoesNotCacheStandaloneCastersWithoutPressState()
        {
            FieldInfo standaloneCasterCache = typeof(BlockiverseUiToolkitMenuSurface)
                .GetField("xriCasters", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(standaloneCasterCache, Is.Null,
                "The UI Toolkit menu bridge must not let bare CurveInteractionCaster hits consume the target before a press-aware NearFarInteractor or XRRayInteractor can read the trigger.");
        }

        [Test]
        public void UiToolkitXriPointerBridgePrefersPressedInteractorOverEarlierHoverHit()
        {
            MethodInfo shouldPreferCandidate = typeof(BlockiverseUiToolkitMenuSurface)
                .GetMethod("ShouldPreferPointerCandidate", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(shouldPreferCandidate, Is.Not.Null);

            Assert.That((bool)shouldPreferCandidate.Invoke(null, new object[] { false, false, false }), Is.True,
                "The first hover target should still drive visual feedback when no press is active.");
            Assert.That((bool)shouldPreferCandidate.Invoke(null, new object[] { true, false, false }), Is.False,
                "A later hover-only hand should not replace the first hover target.");
            Assert.That((bool)shouldPreferCandidate.Invoke(null, new object[] { true, false, true }), Is.True,
                "A pressed hand/ray must replace an earlier unpressed target so trigger release activates the button under the active controller.");
            Assert.That((bool)shouldPreferCandidate.Invoke(null, new object[] { true, true, false }), Is.False,
                "An unpressed candidate must never replace the currently pressed target.");
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
            Assert.That(surfaceObject.layer, Is.EqualTo(BlockiverseProject.VrUiLayerIndex),
                "Runtime Configure must keep the UI Toolkit surface on the layer used by Quest controller UI raycasts.");
            Assert.That(colliderProperty, Is.Not.Null);
            Assert.That(colliderProperty.objectReferenceValue, Is.SameAs(worldSpaceCollider),
                "Runtime Configure must restore UIDocument's private world-space collider reference after UIDocument startup clears it.");
        }

        [Test]
        public void UiToolkitSurfaceDoesNotRecenterVisibleFixedMenuPanel()
        {
            GameObject cameraObject = CreateRoot("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject surfaceObject = CreateRoot("UI Toolkit Menu Surface");
            surfaceObject.transform.position = new Vector3(0.0f, 0.0f, 10.0f);
            var presenter = surfaceObject.AddComponent<BlockiverseUiToolkitMenuPresenter>();
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
            Assert.That(surfaceObject.transform.position.y, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(surfaceObject.transform.position.z, Is.EqualTo(10.0f).Within(0.001f));
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
        public void WorldLoadingRoutePausesAndUsesUiToolkitSurface()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

            StartMenuController(controller);
            controller.ShowWorldLoadingScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldLoadingScreen));
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.EnterGameplay();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void SurvivalHudToolkitSurfaceFollowsGameplayRoute()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();

            GameObject hud = CreateChild(rig.transform, "Survival HUD");
            var hudSurface = hud.AddComponent<BlockiverseHudToolkitSurface>();
            hudSurface.Configure(hud.AddComponent<UnityEngine.UIElements.UIDocument>());
            hudSurface.SetVisible(true);

            StartMenuController(controller);

            Assert.That(hudSurface.IsVisible, Is.False, "Title routing must hide the gameplay HUD.");

            controller.EnterGameplay();

            Assert.That(hudSurface.IsVisible, Is.True);

            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));
            Assert.That(hudSurface.IsVisible, Is.False, "Pause routing must hide the gameplay HUD.");
        }

        [Test]
        public void ControllerMappingPresenterShowsWorldSpaceRootOnlyUntilDismissed()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                BlockiverseUiToolkitMenuPresenter firstPresenter = CreatePresenterWithStartGate(
                    "Start Gate Presenter First",
                    key,
                    out GameObject firstRoot);

                StartBehaviour(firstPresenter);

                Assert.That(firstRoot.activeSelf, Is.True);

                firstPresenter.Hide();

                Assert.That(firstRoot.activeSelf, Is.False);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));

                BlockiverseUiToolkitMenuPresenter secondPresenter = CreatePresenterWithStartGate(
                    "Start Gate Presenter Second",
                    key,
                    out GameObject secondRoot);

                StartBehaviour(secondPresenter);

                Assert.That(secondRoot.activeSelf, Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteOwnsFirstLaunchBeforeTitleMenu()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
                controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));

                controller.CloseControllerMappingScreen();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteClosesQuickBlockCatalogAndOwnsRaycasts()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseInputRig inputRig = rig.AddComponent<BlockiverseInputRig>();
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
                controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));

                inputRig.QuickMenuPressed.Invoke();

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen),
                    "Support-grip quick-menu input must be ignored while UI Toolkit menu UI owns input.");

                controller.CloseControllerMappingScreen();
                inputRig.QuickMenuPressed.Invoke();

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen),
                    "The quick block catalog is gameplay-only and must not open over the title menu.");

                controller.EnterGameplay();
                inputRig.QuickMenuPressed.Invoke();

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.BlockCatalogScreen));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteUsesUiToolkitPresenterOnly()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
                controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));

                controller.CloseControllerMappingScreen();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingSeenLaunchesTitleMenuDirectly()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.SetInt(key, 1);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
                controller.ConfigureUiToolkitMenuSurface(surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>());

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
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
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            controller.ConfigureUiToolkitMenuSurface(surface);

            StartMenuController(controller);
            controller.ShowLanMultiplayerScreen();
            SetPrivateField(controller, "uiToolkitLanAddress", "10.0.0.8");

            InvokeMenuAction(controller, MenuActions.LanMultiplayerJoin);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LanMultiplayerScreen));
        }

        [Test]
        public void LanJoinActionKeepsCurrentUiToolkitAddressWhenLanSurfaceIsActive()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            GameObject surfaceObject = CreateChild(rig.transform, "UI Toolkit Menu Surface");
            var surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            var visibleRoot = new UiVisualElement();
            visibleRoot.style.display = UiDisplayStyle.Flex;
            SetPrivateField(surface, "root", visibleRoot);
            controller.ConfigureUiToolkitMenuSurface(surface);

            StartMenuController(controller);
            controller.ShowLanMultiplayerScreen();
            SetPrivateField(controller, "uiToolkitLanAddress", "10.0.0.8");

            InvokeMenuAction(controller, MenuActions.LanMultiplayerJoin);

            Assert.That(GetPrivateField<string>(controller, "uiToolkitLanAddress"), Is.EqualTo("10.0.0.8"));
        }

        [Test]
        public void DeathWhileModalIsOpenClearsModalAndRoutesToDeathScreen()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);

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
        public void TitleQuitOpensConfirmAndCancelSuppressesQuitAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();

            controller.SetSaveAvailability(latestSaveExists: true, anySaveExists: true);
            StartMenuController(controller);

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            InvokeMenuAction(controller, MenuActions.TitleQuit);

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetPrivateField<string>(controller, "confirmPrompt"),
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            InvokeMenuAction(controller, MenuActions.ConfirmCancel);

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void PauseQuitOpensConfirmAndCancelSuppressesSaveAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            InvokeMenuAction(controller, MenuActions.PauseQuit);

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetPrivateField<string>(controller, "confirmPrompt"),
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            InvokeMenuAction(controller, MenuActions.ConfirmCancel);

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void ClearingModalStackClearsPendingConfirmCallback()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();

            StartMenuController(controller);

            bool accepted = false;
            controller.RequestConfirm("Quit?", "Quit", "Cancel", value => accepted = value);
            controller.EnterGameplay();

            InvokeMenuAction(controller, MenuActions.ConfirmAccept);

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

            InvokeMenuAction(controller, MenuActions.DeathReturnToTitle);

            Assert.That(requestedAction, Is.EqualTo(MenuActions.DeathReturnToTitle));
            Assert.That(wasAliveWhenActionRaised, Is.True,
                "The session save handler must observe post-respawn vitals.");
            Assert.That(vitals.Vitals.IsDead, Is.False);
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
        }

        [Test]
        public void StationStateRoutesTransfersWithoutGeneratedMenuControls()
        {
            BlockiverseStationInteractionState state = CreateStationInteractionState(null);
            GameObject syncObject = CreateRoot("Station Survival Sync");
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, null, null);
            survivalSync.LocalInventory.SetSlot(0, new ItemStack(ItemId.ClayLump, 1));
            state.ConfigureSurvivalSync(survivalSync);

            var station = new SmeltingStationModel(CraftingStation.ClayKiln, inputSlotCount: 1);
            station.TryDepositInput(new ItemStack(ItemId.ClayLump, 2));
            state.Open(station, new BlockPosition(1, 2, 3));

            state.DepositHeldInput();
            Assert.That(state.CurrentStatusText, Is.EqualTo("Cannot deposit: Not a Station"));

            state.WithdrawInput();
            Assert.That(state.CurrentStatusText, Is.EqualTo("Cannot withdraw: Not a Station"));

            state.Close();
            Assert.That(state.IsOpen, Is.False);
        }

        [Test]
        public void StationStateClosesWhenOpenStationIsRemoved()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreativeWorldManager worldManager = rig.AddComponent<CreativeWorldManager>();
            MultiplayerSurvivalSync survivalSync = rig.AddComponent<MultiplayerSurvivalSync>();
            worldManager.Configure(CreateTestChunkMaterial(), BlockiverseProject.InteractionLayerIndex);
            BlockiverseStationInteractionState state = CreateStationInteractionState(rig.transform);
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
            state.Open(survivalSync.GetOrCreateStationModel(stationPosition, CraftingStation.ClayKiln), stationPosition);

            world.SetBlock(stationPosition, BlockRegistry.Air, trackChange: false);
            survivalSync.TickStations(1);

            Assert.That(state.IsOpen, Is.False);
        }

        [Test]
        public void WorldDetailsUiToolkitViewContainsRuntimeActions()
        {
            var view = BlockiverseUiToolkitMenuCatalog.CreateWorldDetailsView(
                new WorldSaveSummary(
                "Meadow Home",
                "1234",
                "survival",
                "normal",
                4,
                new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)),
                "Meadow Home");

            Assert.That(view.Actions[0].ActionId, Is.EqualTo(MenuActions.WorldDetailsPlay));
            Assert.That(view.Actions[0].Label, Is.EqualTo("Play"));
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

                string metadata = WorldDetailsMetadataFormatter.BuildMetadataText(save);

                Assert.That(metadata, Does.Contain(created.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Contain(lastPlayed.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Not.Contain("2026-06-01"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        BlockiverseStationInteractionState CreateStationInteractionState(Transform parent)
        {
            GameObject root = CreateChild(parent, "Station Menu State");
            return root.AddComponent<BlockiverseStationInteractionState>();
        }

        static WorldSaveSummary CreateSave(string name) =>
            new(
                name,
                "1234",
                "survival",
                "normal",
                1,
                new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        BlockiverseUiToolkitMenuPresenter CreatePresenterWithStartGate(string name, string key, out GameObject root)
        {
            root = CreateRoot(name);
            root.SetActive(false);
            var presenter = root.AddComponent<BlockiverseUiToolkitMenuPresenter>();
            presenter.ConfigureWorldSpaceTarget(
                root,
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

        static void StartMenuController(BlockiverseMenuController controller)
        {
            BlockiverseUiToolkitMenuSurface surface =
                controller.GetComponentInChildren<BlockiverseUiToolkitMenuSurface>(includeInactive: true);
            if (surface == null)
            {
                GameObject surfaceObject = new("UI Toolkit Menu Surface");
                surfaceObject.transform.SetParent(controller.transform, false);
                surface = surfaceObject.AddComponent<BlockiverseUiToolkitMenuSurface>();
            }

            controller.ConfigureUiToolkitMenuSurface(surface);
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
