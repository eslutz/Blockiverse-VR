using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.UIInteraction;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CompositionLayerUiEditModeTests
    {
        const string VrUiLayerName = "BlockiverseCompositionUI";
        const int VrUiLayerIndex = 11;
        const string InteractionLayerName = "BlockiverseInteractable";
        const int InteractionLayerIndex = 10;
        const string XrVisualLayerName = "BlockiverseXrVisuals";
        const int XrVisualLayerIndex = 12;
        const string MenuCompositionSurfaceName = "Blockiverse Menu Composition Surface";
        const string MenuCompositionCanvasName = "Blockiverse Menu Canvas";
        const string XrVisualProjectionRigName = "Blockiverse XR Visual Projection Rig";
        const float ExpectedRoutedMenuDistanceMeters = 0.95f;
        const float ExpectedRoutedMenuVerticalOffsetMeters = -0.38f;
        const float ExpectedRoutedMenuPitchDegrees = 10.0f;

        static readonly string[] RoutedMenuPanels =
        {
            "Title Menu",
            "Pause Menu",
            "Death Screen",
            "New World Panel",
            "Load World Panel",
            "Settings Panel",
            "Comfort Settings Menu",
            "Audio Settings Panel",
            "Controls Panel",
            "World Details Panel",
            "LAN Multiplayer Panel",
            "Creative Tools Panel",
            "Station Panel",
            "Controller Mapping Popup",
            "Confirm Dialog",
        };

        static readonly string[] WorldSpacePanels =
        {
            "Startup Loading Overlay",
            "Survival HUD",
            "Block Menu",
        };


        [SetUp]
        public void EnsureProjectLayersAvailable()
        {
            EnsureExpectedLayer(BlockiverseProject.InteractionLayerName, BlockiverseProject.InteractionLayerIndex);
            EnsureExpectedLayer(BlockiverseProject.CompositionUiLayerName, BlockiverseProject.CompositionUiLayerIndex);
            EnsureExpectedLayer(BlockiverseProject.XrVisualProjectionLayerName, BlockiverseProject.XrVisualProjectionLayerIndex);
        }

        static void EnsureExpectedLayer(string layerName, int layerIndex)
        {
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            Assert.That(tagManagerAssets, Is.Not.Null.And.Not.Empty, "TagManager settings asset must be available.");

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            tagManager.UpdateIfRequiredOrScript();
            SerializedProperty layers = tagManager.FindProperty("layers");
            Assert.That(layerIndex, Is.InRange(0, layers.arraySize - 1));

            for (int index = 8; index < layers.arraySize; index++)
            {
                if (index == layerIndex)
                    continue;

                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (layer.stringValue == layerName)
                    layer.stringValue = string.Empty;
            }

            SerializedProperty targetLayer = layers.GetArrayElementAtIndex(layerIndex);
            Assert.That(string.IsNullOrEmpty(targetLayer.stringValue) || targetLayer.stringValue == layerName,
                Is.True,
                $"Unity layer {layerIndex} is reserved for {layerName} but currently contains {targetLayer.stringValue}.");

            if (targetLayer.stringValue != layerName)
            {
                targetLayer.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tagManager.targetObject);
                AssetDatabase.ImportAsset("ProjectSettings/TagManager.asset", ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void AndroidOpenXrCompositionLayerStartupSplashIsEnabled()
        {
            OpenXRSettings androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            UnityEngine.XR.OpenXR.Features.OpenXRFeature compositionFeature =
                UnityEditor.XR.OpenXR.Features.FeatureHelpers.GetFeatureWithIdForBuildTarget(
                    BuildTargetGroup.Android,
                    "com.unity.openxr.feature.compositionlayers");
            CompositionLayersRuntimeSettings runtimeSettings = CompositionLayersRuntimeSettings.Instance;

            Assert.That(androidSettings, Is.Not.Null);
            Assert.That(compositionFeature, Is.Not.Null);
            Assert.That(compositionFeature.enabled, Is.True, "Quest builds must enable OpenXR composition layer support for the startup splash.");
            Assert.That(PlayerSettings.SplashScreen.show, Is.True, "Quest startup should restore Unity's Made with Unity splash before Blockiverse branding.");
            Assert.That(PlayerSettings.SplashScreen.showUnityLogo, Is.True, "Unity Personal builds should keep the Made with Unity logo visible.");
            Assert.That(PlayerSettings.virtualRealitySplashScreen, Is.Null);
            Assert.That(runtimeSettings.EnableSplashScreen, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(runtimeSettings.SplashImage), Is.EqualTo(BlockiverseProject.LaunchArtworkPath));
            Assert.That(runtimeSettings.LayerType, Is.EqualTo(CompositionLayersRuntimeSettings.Layer.Quad));
            Assert.That(runtimeSettings.BackgroundType, Is.EqualTo(CompositionLayersRuntimeSettings.SplashBackgroundType.SolidColor));
            Assert.That(runtimeSettings.FollowDistance, Is.EqualTo(2.0f).Within(0.001f));
            Assert.That(runtimeSettings.LockToHorizon, Is.True);
            Assert.That(runtimeSettings.EmulationInStandalone, Is.False);
        }

        [Test]
        public void GeneratedRigUsesSingleCompositionLayerMenuSurface()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(vrUiLayer, Is.EqualTo(VrUiLayerIndex),
                "VR UI uses a dedicated layer so rays can target panels without including unrelated world geometry.");

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);
            Transform menuSurface = cameraOffset.Find(MenuCompositionSurfaceName);
            Assert.That(menuSurface, Is.Not.Null, "Generated routed menus should share one Quest composition-layer surface.");
            CompositionLayer[] allLayers = prefab.GetComponentsInChildren<CompositionLayer>(includeInactive: true);
            Assert.That(allLayers, Has.Length.EqualTo(1),
                "Generated menu routing should not leave duplicate or stale composition layers competing for layer order.");

            CompositionLayer menuLayer = menuSurface.GetComponent<CompositionLayer>();
            TexturesExtension texturesExtension = menuSurface.GetComponent<TexturesExtension>();
            InteractableUIMirror mirror = menuSurface.GetComponent<InteractableUIMirror>();
            BlockiverseCompositionLayerRenderScale renderScale =
                menuSurface.GetComponent<BlockiverseCompositionLayerRenderScale>();

            Assert.That(menuLayer, Is.Not.Null);
            Assert.That(menuLayer.enabled, Is.False,
                "The routed menu composition layer should start hidden so an empty source canvas cannot submit blank bars.");
            Assert.That(menuLayer.Order, Is.EqualTo(10));
            Assert.That(menuLayer.LayerData, Is.TypeOf<QuadLayerData>());
            QuadLayerData quadLayerData = (QuadLayerData)menuLayer.LayerData;
            Vector2 expectedPhysicalMenuSize = new(1040.0f * 0.0013f, 860.0f * 0.0013f);
            Assert.That(quadLayerData.Size.x, Is.EqualTo(expectedPhysicalMenuSize.x).Within(0.0001f));
            Assert.That(quadLayerData.Size.y, Is.EqualTo(expectedPhysicalMenuSize.y).Within(0.0001f));
            Assert.That(quadLayerData.ApplyTransformScale, Is.True);
            Assert.That(Mathf.Max(quadLayerData.Size.x, quadLayerData.Size.y), Is.LessThan(500.0f),
                "QuadUIScale builds a local MeshCollider from QuadLayerData.Size; this must stay in physical meters, not UI pixels.");
            Assert.That(menuSurface.localScale, Is.EqualTo(Vector3.one));
            Assert.That(texturesExtension, Is.Not.Null);
            Assert.That(mirror, Is.Not.Null);
            Assert.That(mirror.enabled, Is.True);
            Assert.That(menuSurface.GetComponent<XRSimpleInteractable>(), Is.Not.Null);
            Assert.That(menuSurface.GetComponent<MeshCollider>(), Is.Not.Null);
            Assert.That(menuSurface.GetComponent<QuadUIScale>(), Is.Not.Null);
            Assert.That(renderScale, Is.Not.Null);
            Assert.That(menuSurface.gameObject.layer, Is.EqualTo(vrUiLayer));
            Assert.That(menuSurface.localPosition.y, Is.EqualTo(1.05f).Within(0.001f),
                "The fallback composition surface pose should be reachable even before a presenter recenters it.");
            Assert.That(menuSurface.localPosition.z, Is.EqualTo(ExpectedRoutedMenuDistanceMeters).Within(0.001f));
            Assert.That(Quaternion.Angle(menuSurface.localRotation, Quaternion.Euler(ExpectedRoutedMenuPitchDegrees, 0.0f, 0.0f)),
                Is.LessThan(0.001f));

            Transform menuCanvasTransform = menuSurface.Find(MenuCompositionCanvasName);
            Assert.That(menuCanvasTransform, Is.Not.Null);
            Assert.That(menuSurface.Find("Canvas"), Is.Null,
                "The composition-layer package creates a default child Canvas if the mirror is added before the routed menu canvas; that stale canvas must not be persisted.");
            Canvas menuCanvas = menuCanvasTransform.GetComponent<Canvas>();
            Assert.That(menuCanvas, Is.Not.Null);
            Assert.That(menuCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(menuCanvas.enabled, Is.True);
            Assert.That(menuCanvasTransform.localScale, Is.EqualTo(Vector3.one * 0.0013f));
            RectTransform menuCanvasRect = menuCanvasTransform.GetComponent<RectTransform>();
            Assert.That(menuCanvasRect.rect.width, Is.EqualTo(1040.0f).Within(0.001f));
            Assert.That(menuCanvasRect.rect.height, Is.EqualTo(860.0f).Within(0.001f));
            Assert.That(menuCanvasTransform.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(menuCanvasTransform.GetComponent<GraphicRaycaster>(), Is.Null);
            Camera canvasCamera = menuCanvasTransform.Find("CanvasCamera")?.GetComponent<Camera>();
            Assert.That(canvasCamera, Is.Not.Null);
            Assert.That(canvasCamera.enabled, Is.False,
                "The composition source camera must never render as an enabled eye camera.");
            Assert.That(canvasCamera.nearClipPlane, Is.GreaterThan(0.0f));
            Assert.That(canvasCamera.backgroundColor.a, Is.EqualTo(0.0f).Within(0.001f));
            CanvasGroup menuInputGate = menuCanvasTransform.GetComponent<CanvasGroup>();
            Assert.That(menuInputGate, Is.Not.Null);
            Assert.That(menuInputGate.interactable, Is.True);
            Assert.That(menuInputGate.blocksRaycasts, Is.True,
                "The shared composition menu canvas owns the tracked-device raycaster, so it must not block routed panel input globally.");
            Assert.That(menuCanvasTransform.gameObject.layer, Is.EqualTo(vrUiLayer));

            var serializedLayer = new SerializedObject(menuLayer);
            Assert.That(serializedLayer.FindProperty("m_UICanvas").objectReferenceValue, Is.SameAs(menuCanvas),
                "The composition layer must submit the routed menu canvas, not a package-created default Canvas.");
            Assert.That(serializedLayer.FindProperty("m_UIMirrorComponent").objectReferenceValue, Is.SameAs(mirror));

            var serializedMirror = new SerializedObject(mirror);
            Assert.That(serializedMirror.FindProperty("canvasRectTransform").objectReferenceValue, Is.SameAs(menuCanvasRect));
            Assert.That(serializedMirror.FindProperty("canvasGroup").objectReferenceValue, Is.SameAs(menuInputGate));
            Assert.That(serializedMirror.FindProperty("trackedDeviceGraphicRaycaster").objectReferenceValue,
                Is.SameAs(menuCanvasTransform.GetComponent<TrackedDeviceGraphicRaycaster>()));

            foreach (string name in RoutedMenuPanels)
            {
                Transform panel = menuCanvasTransform.Find(name);
                Assert.That(panel, Is.Not.Null, $"{name} should be routed under the shared menu composition canvas.");

                Assert.That(panel.GetComponent<Canvas>(), Is.Null, $"{name} should not create an independent world-space canvas.");
                Assert.That(panel.gameObject.layer, Is.EqualTo(vrUiLayer), $"{name} should stay on the dedicated VR UI layer.");
                Assert.That(panel.GetComponent<CompositionLayer>(), Is.Null, $"{name} must not create a per-screen composition layer.");
                Assert.That(panel.GetComponent<TexturesExtension>(), Is.Null, $"{name} must not submit an independent compositor texture.");
                Assert.That(panel.GetComponent<InteractableUIMirror>(), Is.Null, $"{name} must not create an independent composition-layer proxy UI path.");
                Assert.That(panel.Find("CanvasCamera"), Is.Null, $"{name} should not keep a hidden composition-layer mirror camera.");
                Assert.That(panel.GetComponent<GraphicRaycaster>(), Is.Null, $"{name} should not use screen-space GraphicRaycaster input.");
                Assert.That(panel.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Null, $"{name} should not own a separate tracked-device raycaster.");
                Assert.That(panel.gameObject.activeSelf, Is.False, $"{name} visibility should be presenter-controlled through active state.");

                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                Assert.That(presenter, Is.Not.Null, $"{name} should keep presenter-based routing.");
                Assert.That(presenter.TargetRoot, Is.SameAs(panel.gameObject), $"{name} should toggle its routed panel root.");
                Assert.That(presenter.PlacementRoot, Is.SameAs(menuSurface), $"{name} should recenter the shared composition surface.");
                Assert.That(presenter.UsesSharedCompositionRoot, Is.True,
                    $"{name} should preserve the composition layer's meter-sized quad instead of applying world-space canvas scale.");
                var serializedPresenter = new SerializedObject(presenter);
                Assert.That(serializedPresenter.FindProperty("distanceMeters").floatValue,
                    Is.EqualTo(ExpectedRoutedMenuDistanceMeters).Within(0.001f),
                    $"{name} should appear close enough for accurate controller interaction.");
                Assert.That(serializedPresenter.FindProperty("verticalOffsetMeters").floatValue,
                    Is.EqualTo(ExpectedRoutedMenuVerticalOffsetMeters).Within(0.001f),
                    $"{name} should be centered below eye height rather than above the player.");
                Assert.That(serializedPresenter.FindProperty("pitchDegrees").floatValue,
                    Is.EqualTo(ExpectedRoutedMenuPitchDegrees).Within(0.001f),
                    $"{name} should tilt toward the player's natural controller ray.");

                foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(includeInactive: true))
                    Assert.That(graphic.gameObject.layer, Is.EqualTo(vrUiLayer),
                        $"{name}/{graphic.name} should stay on the dedicated VR UI layer.");
            }

            int interactionLayer = LayerMask.NameToLayer(InteractionLayerName);
            Assert.That(interactionLayer, Is.EqualTo(InteractionLayerIndex));
            foreach (string name in WorldSpacePanels)
            {
                Transform panel = cameraOffset.Find(name);
                Assert.That(panel, Is.Not.Null, $"{name} should remain a direct world-space panel.");
                Assert.That(panel.gameObject.layer, Is.EqualTo(interactionLayer),
                    $"{name} should render through the main eye camera while remaining targetable by controller rays.");
                Assert.That(panel.GetComponent<Canvas>(), Is.Not.Null, $"{name} should retain its own world-space canvas.");
                Assert.That(panel.GetComponent<CompositionLayer>(), Is.Null, $"{name} should not use the routed menu composition layer.");
                Assert.That(panel.GetComponent<TexturesExtension>(), Is.Null, $"{name} should not submit an independent compositor texture.");
                Assert.That(panel.GetComponent<InteractableUIMirror>(), Is.Null, $"{name} should not create a second composition-layer proxy.");
            }
        }

        [Test]
        public void SurvivalHudWorldSpacePanelKeepsInventoryCraftingAndCrateSections()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform hud = prefab?.transform.Find("Camera Offset/Survival HUD");

            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.GetComponent<CompositionLayer>(), Is.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalHealthPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalInventoryPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalCraftingPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalCratePanel>(true), Is.Not.Null);
        }

        [Test]
        public void ControllerRayVisualsStayOnMainCameraPathWithCompositionMenuCursor()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Transform staleProjection = cameraOffset.Find("Blockiverse UI Pointer Projection");
            Assert.That(staleProjection, Is.Null, "The deprecated ad-hoc pointer projection object should not be generated.");

            int visualLayer = LayerMask.NameToLayer(XrVisualLayerName);
            Assert.That(visualLayer, Is.EqualTo(XrVisualLayerIndex));

            Transform projectionRig = cameraOffset.Find(XrVisualProjectionRigName);
            Assert.That(projectionRig, Is.Null,
                "Controller/ray visuals should not be rendered by a composition ProjectionLayerRigData camera path.");

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That((mainCamera.cullingMask & (1 << visualLayer)), Is.EqualTo(0),
                "The unused XR visual projection layer should stay hidden from the main eye camera.");
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            int vrUiLayerMask = 1 << vrUiLayer;
            Assert.That((mainCamera.cullingMask & vrUiLayerMask), Is.EqualTo(0),
                "The source composition UI canvas should not be duplicated by the main eye camera.");
            Assert.That((mainCamera.cullingMask & BlockiverseProject.InteractionLayerMask), Is.Not.EqualTo(0),
                "World-space interaction UI should remain visible through the main eye camera.");

            foreach (string path in new[]
            {
                "Left Controller",
                "Right Controller",
                "Left Controller/Interaction Ray",
                "Right Controller/Interaction Ray",
                "Left Controller/Teleport Ray",
                "Right Controller/Teleport Ray",
            })
            {
                Transform target = cameraOffset.Find(path);
                Assert.That(target, Is.Not.Null, path);
                Assert.That(target.gameObject.layer, Is.Not.EqualTo(visualLayer),
                    $"{path} should not be moved to the XR visual projection layer.");
                Assert.That((mainCamera.cullingMask & (1 << target.gameObject.layer)), Is.Not.EqualTo(0),
                    $"{path} should stay on a layer rendered by the main eye camera.");
            }

            foreach (string path in new[]
            {
                "Left Controller/Interaction Ray",
                "Right Controller/Interaction Ray",
            })
            {
                UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray =
                    cameraOffset.Find(path)?.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>();
                Assert.That(ray, Is.Not.Null, path);
                Assert.That((ray.raycastMask.value & BlockiverseProject.InteractionLayerMask), Is.Not.EqualTo(0),
                    $"{path} should still target voxel terrain.");
                Assert.That((ray.raycastMask.value & vrUiLayerMask), Is.Not.EqualTo(0),
                    $"{path} should target the composition-layer UI collider for routed menu input.");
                Assert.That(ray.interactionLayers.value, Is.EqualTo(BlockiverseRayDefaults.DefaultXriInteractionLayerMask),
                    $"{path} should overlap the composition surface's XRI Default interaction layer.");
            }

            Transform menuSurface = cameraOffset.Find(MenuCompositionSurfaceName);
            Assert.That(menuSurface, Is.Not.Null);
            Transform menuCanvasTransform = menuSurface.Find(MenuCompositionCanvasName);
            Assert.That(menuCanvasTransform, Is.Not.Null);
            BlockiverseCompositionMenuCursor cursor = menuSurface.GetComponent<BlockiverseCompositionMenuCursor>();
            Assert.That(cursor, Is.Not.Null, "The composition menu should expose menu-local hover feedback.");
            Assert.That(cursor.MenuCanvas, Is.SameAs(menuCanvasTransform.GetComponent<RectTransform>()));
            Assert.That(cursor.Cursor, Is.Not.Null);
            Assert.That(cursor.Cursor.IsChildOf(menuCanvasTransform), Is.True);
            Image cursorImage = cursor.Cursor.GetComponent<Image>();
            Assert.That(cursorImage, Is.Not.Null);
            Assert.That(cursorImage.raycastTarget, Is.False,
                "The composition cursor must not consume tracked-device UI raycasts.");
            Assert.That(cursor.Cursor.gameObject.activeSelf, Is.False,
                "The generated cursor starts hidden until a valid menu UI hit is reported.");
        }

        [Test]
        public void CompositionMenuCursorHidesWithoutMenuHitAndMapsCanvasLocalHit()
        {
            GameObject surface = new("Composition Cursor Test Surface");
            GameObject menuCanvasObject = new("Composition Cursor Test Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject target = new("Composition Cursor Test Target", typeof(RectTransform));
            GameObject cursorObject = new("Composition Cursor Test Cursor", typeof(RectTransform), typeof(Image));
            GameObject miss = new("Composition Cursor Test Miss");

            try
            {
                menuCanvasObject.transform.SetParent(surface.transform, false);
                target.transform.SetParent(menuCanvasObject.transform, false);
                cursorObject.transform.SetParent(menuCanvasObject.transform, false);

                RectTransform menuCanvas = menuCanvasObject.GetComponent<RectTransform>();
                menuCanvas.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 200.0f);
                menuCanvas.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100.0f);
                RectTransform cursorRect = cursorObject.GetComponent<RectTransform>();
                Image cursorImage = cursorObject.GetComponent<Image>();
                cursorImage.raycastTarget = false;

                BlockiverseCompositionMenuCursor cursor = surface.AddComponent<BlockiverseCompositionMenuCursor>();
                cursor.Configure(null, menuCanvas, cursorRect, cursorImage);

                Assert.That(cursor.TryUpdateCursor(), Is.False);
                Assert.That(cursorObject.activeSelf, Is.False);

                Vector3 worldHit = menuCanvas.TransformPoint(new Vector3(42.0f, -24.0f, 0.0f));
                var menuHit = new RaycastResult
                {
                    gameObject = target,
                    worldPosition = worldHit,
                };

                Assert.That(cursor.TryApplyRaycastResult(menuHit), Is.True);
                Assert.That(cursorObject.activeSelf, Is.True);
                Assert.That(cursorImage.enabled, Is.True);
                Assert.That(cursorRect.anchoredPosition.x, Is.EqualTo(42.0f).Within(0.001f));
                Assert.That(cursorRect.anchoredPosition.y, Is.EqualTo(-24.0f).Within(0.001f));

                var missHit = new RaycastResult
                {
                    gameObject = miss,
                    worldPosition = worldHit,
                };

                Assert.That(cursor.TryApplyRaycastResult(missHit), Is.False);
                Assert.That(cursorObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(miss);
            }
        }

        [Test]
        public void CompositionLayerRenderScaleSubmitsOnlyWhenCanvasHasVisibleGraphics()
        {
            GameObject surface = new("Composition Render Scale Surface");
            GameObject canvasObject = new("Composition Render Scale Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject cameraObject = new("CanvasCamera", typeof(Camera));
            GameObject panel = new("Visible Panel", typeof(RectTransform), typeof(Image));

            try
            {
                surface.layer = VrUiLayerIndex;
                canvasObject.layer = VrUiLayerIndex;
                cameraObject.layer = VrUiLayerIndex;
                panel.layer = VrUiLayerIndex;
                canvasObject.transform.SetParent(surface.transform, false);
                cameraObject.transform.SetParent(canvasObject.transform, false);
                panel.transform.SetParent(canvasObject.transform, false);

                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 200.0f);
                canvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100.0f);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.enabled = true;
                Image panelImage = panel.GetComponent<Image>();
                panelImage.color = Color.white;

                TexturesExtension texturesExtension = surface.AddComponent<TexturesExtension>();
                Camera mirrorCamera = cameraObject.GetComponent<Camera>();
                mirrorCamera.enabled = true;

                BlockiverseCompositionLayerRenderScale renderScale =
                    surface.AddComponent<BlockiverseCompositionLayerRenderScale>();
                renderScale.Configure(canvas, null, texturesExtension, mirrorCamera, 1.0f);

                panel.SetActive(false);
                renderScale.ApplyRenderScale();

                Assert.That(renderScale.IsSubmittingLayer, Is.False,
                    "Blank routed canvases should not submit an opaque composition quad.");
                Assert.That(mirrorCamera.enabled, Is.False,
                    "The mirror camera must stay disabled so it cannot render to the XR eye display.");
                Assert.That(texturesExtension.LeftTexture, Is.Null);
                Assert.That(texturesExtension.RightTexture, Is.Null);

                panel.SetActive(true);
                renderScale.ApplyRenderScale();

                Assert.That(renderScale.IsSubmittingLayer, Is.True);
                Assert.That(mirrorCamera.enabled, Is.False);
                Assert.That(mirrorCamera.targetTexture, Is.Not.Null);
                Assert.That(texturesExtension.LeftTexture, Is.SameAs(mirrorCamera.targetTexture));
                Assert.That(texturesExtension.RightTexture, Is.SameAs(mirrorCamera.targetTexture));
                Assert.That(mirrorCamera.nearClipPlane, Is.GreaterThan(0.0f));
                Assert.That(mirrorCamera.backgroundColor.a, Is.EqualTo(0.0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(surface);
            }
        }

        [Test]
        public void PresenterShowHideControlsCanvasVisibility()
        {
            GameObject panel = new("World Space Presenter Test");
            GameObject headset = new("World Space Presenter Headset");
            try
            {
                headset.transform.SetPositionAndRotation(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                Canvas canvas = panel.AddComponent<Canvas>();
                canvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                presenter.Configure(canvas, headset.transform, 2.0f, 0.5f, 0.25f, 10.0f, 0.003f);

                presenter.Show();

                Assert.That(canvas.enabled, Is.True);
                Assert.That(
                    Vector3.Distance(panel.transform.position, new Vector3(3.0f, 2.25f, 2.5f)),
                    Is.LessThan(0.001f));
                Assert.That(panel.transform.localScale, Is.EqualTo(Vector3.one * 0.003f));

                presenter.Hide();

                Assert.That(canvas.enabled, Is.False);

                presenter.ToggleVisible();

                Assert.That(canvas.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(headset);
            }
        }

        [Test]
        public void PresenterShowHideControlsSharedCompositionRoot()
        {
            GameObject panel = new("Shared Composition Panel Test");
            GameObject surface = new("Shared Composition Surface Test");
            GameObject headset = new("Shared Composition Headset");
            try
            {
                panel.SetActive(false);
                headset.transform.SetPositionAndRotation(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));

                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                presenter.Configure(null, headset.transform, 2.0f, 0.5f, 0.25f, 10.0f, 0.003f);
                presenter.ConfigureSharedCompositionTarget(panel, surface.transform);

                presenter.Show();

                Assert.That(panel.activeSelf, Is.True);
                Assert.That(
                    Vector3.Distance(surface.transform.position, new Vector3(3.0f, 2.25f, 2.5f)),
                    Is.LessThan(0.001f));
                Assert.That(surface.transform.localScale, Is.EqualTo(Vector3.one),
                    "Shared composition-layer roots are already meter-sized through QuadLayerData.Size and must not receive canvas panel scale.");
                Assert.That(presenter.UsesSharedCompositionRoot, Is.True);

                presenter.Hide();

                Assert.That(panel.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(headset);
            }
        }
    }
}
