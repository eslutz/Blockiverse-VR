using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.UIInteraction;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
        const string NamedLaunchArtworkPath = "Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape_named.png";
        const float ExpectedMenuDistanceMeters = 0.95f;
        const float ExpectedMenuVerticalOffsetMeters = -0.38f;
        const float ExpectedMenuPitchDegrees = 10.0f;

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
            "Error Dialog",
            "Inventory Panel",
            "Crafting Panel",
            "Catalog Panel",
            "Crate Panel",
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
        public void AndroidOpenXrCompositionLayerStartupSplashUsesBlockiverseBranding()
        {
            OpenXRSettings androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            UnityEngine.XR.OpenXR.Features.OpenXRFeature compositionFeature =
                UnityEditor.XR.OpenXR.Features.FeatureHelpers.GetFeatureWithIdForBuildTarget(
                    BuildTargetGroup.Android,
                    "com.unity.openxr.feature.compositionlayers");
            CompositionLayersRuntimeSettings runtimeSettings = CompositionLayersRuntimeSettings.Instance;
            Texture2D namedSplash = AssetDatabase.LoadAssetAtPath<Texture2D>(NamedLaunchArtworkPath);

            Assert.That(androidSettings, Is.Not.Null);
            Assert.That(compositionFeature, Is.Not.Null);
            Assert.That(compositionFeature.enabled, Is.True, "Quest builds keep OpenXR composition support for startup splash only.");
            Assert.That(namedSplash, Is.Not.Null, "The old Blockiverse named launch screen should be copied into this project.");
            Assert.That(PlayerSettings.SplashScreen.show, Is.True);
            Assert.That(PlayerSettings.SplashScreen.showUnityLogo, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(PlayerSettings.virtualRealitySplashScreen), Is.EqualTo(NamedLaunchArtworkPath));
            Assert.That(runtimeSettings.EnableSplashScreen, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(runtimeSettings.SplashImage), Is.EqualTo(NamedLaunchArtworkPath));
            Assert.That(runtimeSettings.LayerType, Is.EqualTo(CompositionLayersRuntimeSettings.Layer.Quad));
            Assert.That(runtimeSettings.BackgroundType, Is.EqualTo(CompositionLayersRuntimeSettings.SplashBackgroundType.SolidColor));
            Assert.That(runtimeSettings.FollowDistance, Is.EqualTo(2.0f).Within(0.001f));
            Assert.That(runtimeSettings.LockToHorizon, Is.True);
            Assert.That(runtimeSettings.EmulationInStandalone, Is.False);
        }

        [Test]
        public void GeneratedRigUsesWorldSpaceXrMenusWithoutCompositionMenuSurface()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);
            
            Transform menuSurface = cameraOffset.Find(MenuCompositionSurfaceName);
            Assert.That(menuSurface, Is.Not.Null, "Expected composition layer menu surface.");
            
            Transform menuCanvas = menuSurface.Find(MenuCompositionCanvasName);
            Assert.That(menuCanvas, Is.Not.Null, "Expected composition layer menu canvas.");

            Assert.That(prefab.GetComponentInChildren<CompositionLayer>(includeInactive: true), Is.Not.Null,
                "The generated rig should contain interactive composition layers.");
            Assert.That(prefab.GetComponentInChildren<InteractableUIMirror>(includeInactive: true), Is.Not.Null,
                "The generated rig should proxy menu input through composition-layer UI mirroring.");
            Assert.That(prefab.GetComponentInChildren<BlockiverseCompositionLayerRenderScale>(includeInactive: true), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<BlockiverseCompositionMenuCursor>(includeInactive: true), Is.Not.Null);

            int compositionLayerIndex = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(compositionLayerIndex, Is.EqualTo(VrUiLayerIndex));

            foreach (string name in RoutedMenuPanels)
            {
                Transform panel = menuCanvas.Find(name);
                Assert.That(panel, Is.Not.Null, $"{name} should be a child of Menu Canvas.");
                AssertRoutedMenuPanel(panel, compositionLayerIndex, name, menuSurface);
            }

            Transform controllerMap = menuCanvas.Find("Controller Mapping Popup");
            Button closeButton = controllerMap?.Find("Panel/Close Button")?.GetComponent<Button>();
            Assert.That(closeButton, Is.Not.Null, "The Controller Map close button must be a real UGUI Button.");
            Assert.That(closeButton.interactable, Is.True);
            Image closeImage = closeButton.GetComponent<Image>();
            Assert.That(closeImage, Is.Not.Null);
            Assert.That(closeImage.raycastTarget, Is.True,
                "The close button must be reachable by XRI tracked-device UI raycasts.");
        }

        static void AssertRoutedMenuPanel(Transform panel, int expectedLayer, string panelName, Transform menuSurface)
        {
            Assert.That(panel.gameObject.layer, Is.EqualTo(expectedLayer), $"{panelName} should render through the composition layer.");
            Assert.That(panel.gameObject.activeSelf, Is.False,
                $"{panelName} should start inactive because visibility is controlled at runtime.");
            
            Assert.That(panel.GetComponent<Canvas>(), Is.Null, $"{panelName} should not have its own Canvas.");
            Assert.That(panel.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(panel.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Null);
            Assert.That(panel.GetComponent<CanvasScaler>(), Is.Null);

            BlockiverseWorldSpacePanelPresenter presenter = panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null, $"{panelName} should keep presenter-based visibility.");
            Assert.That(presenter.TargetCanvas, Is.Null);
            Assert.That(presenter.TargetRoot, Is.SameAs(panel.gameObject));
            Assert.That(presenter.PlacementRoot, Is.SameAs(menuSurface));
            Assert.That(presenter.UsesSharedCompositionRoot, Is.True);

            var serializedPresenter = new SerializedObject(presenter);
            Assert.That(serializedPresenter.FindProperty("distanceMeters").floatValue,
                Is.EqualTo(ExpectedMenuDistanceMeters).Within(0.001f));
            Assert.That(serializedPresenter.FindProperty("verticalOffsetMeters").floatValue,
                Is.EqualTo(ExpectedMenuVerticalOffsetMeters).Within(0.001f));
            Assert.That(serializedPresenter.FindProperty("pitchDegrees").floatValue,
                Is.EqualTo(ExpectedMenuPitchDegrees).Within(0.001f));

            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(includeInactive: true))
                Assert.That(graphic.gameObject.layer, Is.EqualTo(expectedLayer),
                    $"{panelName}/{graphic.name} should stay on the composition UI layer.");
        }

        [Test]
        public void ControllerRayVisualsStayOnMainCameraPathForWorldSpaceMenus()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Assert.That(cameraOffset.Find("Blockiverse UI Pointer Projection"), Is.Null);
            Assert.That(cameraOffset.Find(XrVisualProjectionRigName), Is.Null,
                "Controller/ray visuals should not be rendered by a composition ProjectionLayerRigData camera path.");
            Assert.That(cameraOffset.Find(MenuCompositionSurfaceName), Is.Not.Null,
                "The shared quad composition surface is required for routed VR menus.");

            int visualLayer = LayerMask.NameToLayer(XrVisualLayerName);
            Assert.That(visualLayer, Is.EqualTo(XrVisualLayerIndex));
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(vrUiLayer, Is.EqualTo(VrUiLayerIndex));

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That((mainCamera.cullingMask & (1 << visualLayer)), Is.EqualTo(0));
            Assert.That((mainCamera.cullingMask & (1 << vrUiLayer)), Is.EqualTo(0));
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
                XRRayInteractor ray = cameraOffset.Find(path)?.GetComponent<XRRayInteractor>();
                Assert.That(ray, Is.Not.Null, path);
                Assert.That((ray.raycastMask.value & BlockiverseProject.InteractionLayerMask), Is.Not.EqualTo(0),
                    $"{path} should target voxel terrain and world-space UI.");
                Assert.That((ray.raycastMask.value & BlockiverseProject.CompositionUiLayerMask), Is.EqualTo(0),
                    $"{path} should not target the unused composition UI layer for menu input.");
                Assert.That(ray.interactionLayers.value, Is.EqualTo(BlockiverseRayDefaults.DefaultXriInteractionLayerMask));
            }
        }

        static void AssertWorldSpaceXrMenuPanel(Transform panel, int expectedLayer, string panelName)
        {
            Assert.That(panel.gameObject.layer, Is.EqualTo(expectedLayer), $"{panelName} should render through the main eye camera.");
            Assert.That(panel.gameObject.activeSelf, Is.True,
                $"{panelName} should keep its GameObject active; routed visibility is controlled by Canvas.enabled.");
            Canvas canvas = panel.GetComponent<Canvas>();
            Assert.That(canvas, Is.Not.Null, $"{panelName} should retain its own world-space canvas.");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.enabled, Is.False,
                $"{panelName} should start hidden by disabling its Canvas, not by deactivating the GameObject.");
            Assert.That(panel.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null,
                $"{panelName} should receive XRI tracked-device UI raycasts directly.");
            Assert.That(panel.GetComponent<GraphicRaycaster>(), Is.Null,
                $"{panelName} should not use the screen-space GraphicRaycaster.");

            CanvasGroup inputGate = panel.GetComponent<CanvasGroup>();
            Assert.That(inputGate, Is.Not.Null, $"{panelName} should expose a canvas input gate.");
            Assert.That(inputGate.interactable, Is.True);
            Assert.That(inputGate.blocksRaycasts, Is.True);

            Assert.That(panel.GetComponent<CompositionLayer>(), Is.Null);
            Assert.That(panel.GetComponent<TexturesExtension>(), Is.Null);
            Assert.That(panel.GetComponent<InteractableUIMirror>(), Is.Null);
            Assert.That(panel.Find("CanvasCamera"), Is.Null);

            BlockiverseWorldSpacePanelPresenter presenter = panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null, $"{panelName} should keep presenter-based visibility.");
            Assert.That(presenter.TargetCanvas, Is.SameAs(canvas));
            Assert.That(presenter.TargetRoot, Is.SameAs(panel.gameObject));
            Assert.That(presenter.PlacementRoot, Is.SameAs(panel));
            Assert.That(presenter.UsesSharedCompositionRoot, Is.False);

            var serializedPresenter = new SerializedObject(presenter);
            Assert.That(serializedPresenter.FindProperty("distanceMeters").floatValue,
                Is.EqualTo(ExpectedMenuDistanceMeters).Within(0.001f));
            Assert.That(serializedPresenter.FindProperty("verticalOffsetMeters").floatValue,
                Is.EqualTo(ExpectedMenuVerticalOffsetMeters).Within(0.001f));
            Assert.That(serializedPresenter.FindProperty("pitchDegrees").floatValue,
                Is.EqualTo(ExpectedMenuPitchDegrees).Within(0.001f));

            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(includeInactive: true))
                Assert.That(graphic.gameObject.layer, Is.EqualTo(expectedLayer),
                    $"{panelName}/{graphic.name} should stay on the world-space interaction UI layer.");
        }
    }
}
