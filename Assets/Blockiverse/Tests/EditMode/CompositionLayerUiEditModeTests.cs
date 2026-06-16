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
            Assert.That(runtimeSettings.SplashImage, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPath)));
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

            CompositionLayer menuLayer = menuSurface.GetComponent<CompositionLayer>();
            TexturesExtension texturesExtension = menuSurface.GetComponent<TexturesExtension>();
            InteractableUIMirror mirror = menuSurface.GetComponent<InteractableUIMirror>();
            BlockiverseCompositionLayerRenderScale renderScale =
                menuSurface.GetComponent<BlockiverseCompositionLayerRenderScale>();

            Assert.That(menuLayer, Is.Not.Null);
            Assert.That(menuLayer.LayerData, Is.TypeOf<QuadLayerData>());
            Assert.That(texturesExtension, Is.Not.Null);
            Assert.That(mirror, Is.Not.Null);
            Assert.That(mirror.enabled, Is.True);
            Assert.That(menuSurface.GetComponent<XRSimpleInteractable>(), Is.Not.Null);
            Assert.That(menuSurface.GetComponent<MeshCollider>(), Is.Not.Null);
            Assert.That(menuSurface.GetComponent<QuadUIScale>(), Is.Not.Null);
            Assert.That(renderScale, Is.Not.Null);
            Assert.That(menuSurface.gameObject.layer, Is.EqualTo(vrUiLayer));

            Transform menuCanvasTransform = menuSurface.Find(MenuCompositionCanvasName);
            Assert.That(menuCanvasTransform, Is.Not.Null);
            Canvas menuCanvas = menuCanvasTransform.GetComponent<Canvas>();
            Assert.That(menuCanvas, Is.Not.Null);
            Assert.That(menuCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(menuCanvas.enabled, Is.True);
            Assert.That(menuCanvasTransform.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(menuCanvasTransform.GetComponent<GraphicRaycaster>(), Is.Null);
            CanvasGroup menuInputGate = menuCanvasTransform.GetComponent<CanvasGroup>();
            Assert.That(menuInputGate, Is.Not.Null);
            Assert.That(menuInputGate.interactable, Is.True);
            Assert.That(menuInputGate.blocksRaycasts, Is.True,
                "The shared composition menu canvas owns the tracked-device raycaster, so it must not block routed panel input globally.");
            Assert.That(menuCanvasTransform.gameObject.layer, Is.EqualTo(vrUiLayer));

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
        public void ControllerRayVisualsRenderThroughProjectionEyeRigAboveMenus()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Transform staleProjection = cameraOffset.Find("Blockiverse UI Pointer Projection");
            Assert.That(staleProjection, Is.Null, "The deprecated ad-hoc pointer projection object should not be generated.");

            int visualLayer = LayerMask.NameToLayer(XrVisualLayerName);
            Assert.That(visualLayer, Is.EqualTo(XrVisualLayerIndex));

            Transform projectionRig = cameraOffset.Find(XrVisualProjectionRigName);
            Assert.That(projectionRig, Is.Not.Null, "Controller/ray visuals need a Projection Eye Rig above the composition menu layer.");
            Assert.That(projectionRig.gameObject.layer, Is.EqualTo(visualLayer));

            CompositionLayer projectionLayer = projectionRig.GetComponent<CompositionLayer>();
            CompositionLayer menuLayer = cameraOffset.Find(MenuCompositionSurfaceName)?.GetComponent<CompositionLayer>();
            Assert.That(projectionLayer, Is.Not.Null);
            Assert.That(projectionLayer.LayerData, Is.TypeOf<ProjectionLayerRigData>());
            Assert.That(menuLayer, Is.Not.Null);
            Assert.That(projectionLayer.Order, Is.GreaterThan(menuLayer.Order));

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That((mainCamera.cullingMask & (1 << visualLayer)), Is.EqualTo(0),
                "XR visual objects should be culled from the default scene camera and rendered by the projection layer.");

            foreach (string eyeCameraName in new[] { "Left Camera", "Right Camera" })
            {
                Camera eyeCamera = projectionRig.Find(eyeCameraName)?.GetComponent<Camera>();
                Assert.That(eyeCamera, Is.Not.Null, eyeCameraName);
                Assert.That(eyeCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(eyeCamera.backgroundColor, Is.EqualTo(Color.clear));
                Assert.That(eyeCamera.cullingMask, Is.EqualTo(1 << visualLayer));
                Assert.That(eyeCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>(), Is.Not.Null);
            }

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
                Assert.That(target.gameObject.layer, Is.EqualTo(visualLayer),
                    $"{path} should render through the Projection Eye Rig layer.");
            }

            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            int vrUiLayerMask = 1 << vrUiLayer;
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
                Assert.That(surface.transform.localScale, Is.EqualTo(Vector3.one * 0.003f));

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
