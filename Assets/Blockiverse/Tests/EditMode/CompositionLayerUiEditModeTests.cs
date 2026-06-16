using System.Collections.Generic;
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
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CompositionLayerUiEditModeTests
    {
        static readonly (string Name, int Order)[] ExpectedPanels =
        {
            ("Startup Loading Overlay", 5),
            ("Survival HUD", 5),
            ("Title Menu", 10),
            ("Pause Menu", 10),
            ("Death Screen", 10),
            ("New World Panel", 10),
            ("Load World Panel", 10),
            ("Settings Panel", 10),
            ("Comfort Settings Menu", 10),
            ("Audio Settings Panel", 10),
            ("Controls Panel", 10),
            ("World Details Panel", 10),
            ("LAN Multiplayer Panel", 10),
            ("Creative Tools Panel", 10),
            ("Station Panel", 10),
            ("Controller Mapping Popup", 10),
            ("Block Menu", 10),
            ("Confirm Dialog", 20),
        };

        [Test]
        public void AndroidOpenXrCompositionLayersAndRuntimeSplashAreEnabled()
        {
            OpenXRSettings androidSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            UnityEngine.XR.OpenXR.Features.OpenXRFeature compositionFeature =
                UnityEditor.XR.OpenXR.Features.FeatureHelpers.GetFeatureWithIdForBuildTarget(
                    BuildTargetGroup.Android,
                    "com.unity.openxr.feature.compositionlayers");
            CompositionLayersRuntimeSettings runtimeSettings = CompositionLayersRuntimeSettings.Instance;

            Assert.That(androidSettings, Is.Not.Null);
            Assert.That(compositionFeature, Is.Not.Null);
            Assert.That(compositionFeature.enabled, Is.True, "Quest builds must enable OpenXR composition layer support.");
            Assert.That(PlayerSettings.SplashScreen.show, Is.False, "Unity's default splash should not compete with the composition-layer splash.");
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
        public void GeneratedRigPanelsUseCompositionLayerVisualsWithDirectTrackedDeviceInput()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            foreach ((string name, int order) in ExpectedPanels)
            {
                Transform panel = cameraOffset.Find(name);
                Assert.That(panel, Is.Not.Null, $"{name} should be generated under the XR rig camera offset.");

                Canvas canvas = panel.GetComponent<Canvas>();
                CompositionLayer compositionLayer = panel.GetComponent<CompositionLayer>();
                TexturesExtension texturesExtension = panel.GetComponent<TexturesExtension>();
                InteractableUIMirror mirror = panel.GetComponent<InteractableUIMirror>();
                BlockiverseCompositionLayerRenderScale renderScale =
                    panel.GetComponent<BlockiverseCompositionLayerRenderScale>();
                GraphicRaycaster legacyRaycaster = panel.GetComponent<GraphicRaycaster>();
                TrackedDeviceGraphicRaycaster trackedRaycaster = panel.GetComponent<TrackedDeviceGraphicRaycaster>();
                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();

                Assert.That(canvas, Is.Not.Null, $"{name} should remain a world-space canvas for existing UI code.");
                Assert.That(compositionLayer, Is.Not.Null, $"{name} should have a CompositionLayer.");
                Assert.That(compositionLayer.LayerData, Is.TypeOf<QuadLayerData>(), $"{name} should use a flat Quad layer.");
                Assert.That(compositionLayer.Order, Is.EqualTo(order), $"{name} should use the planned compositor order.");
                Assert.That(compositionLayer.enabled, Is.EqualTo(canvas.enabled), $"{name} layer visibility should match canvas visibility in the prefab.");
                Assert.That(texturesExtension, Is.Not.Null, $"{name} should have a source texture extension.");
                Assert.That(mirror == null || !mirror.enabled, Is.True,
                    $"{name} should not run InteractableUIMirror's proxy interactor path; direct tracked-device input owns interaction.");
                Assert.That(legacyRaycaster, Is.Null, $"{name} should not use screen-space GraphicRaycaster input.");
                if (ReceivesDirectTrackedDeviceInput(name))
                    Assert.That(trackedRaycaster, Is.Not.Null, $"{name} should receive the real XR ray directly.");
                else
                    Assert.That(trackedRaycaster, Is.Null, $"{name} should remain decorative and not receive UI rays.");
                Assert.That(renderScale, Is.Not.Null, $"{name} should supersample the mirror render target.");
                Assert.That(renderScale.RenderScale, Is.EqualTo(2.0f).Within(0.001f));
                Assert.That(presenter, Is.Not.Null, $"{name} should keep presenter-based routing.");
                Assert.That(presenter.CompositionLayer, Is.SameAs(compositionLayer));
            }
        }

        static bool ReceivesDirectTrackedDeviceInput(string panelName)
        {
            return panelName != "Startup Loading Overlay";
        }

        [Test]
        public void SurvivalHudCompositionLayerKeepsInventoryCraftingAndCrateSections()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform hud = prefab?.transform.Find("Camera Offset/Survival HUD");

            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.GetComponent<CompositionLayer>(), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalHealthPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalInventoryPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalCraftingPanel>(true), Is.Not.Null);
            Assert.That(hud.GetComponentInChildren<SurvivalCratePanel>(true), Is.Not.Null);
        }

        [Test]
        public void ControllerAndRayVisualsRenderThroughMainSceneCamera()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Transform projection = cameraOffset.Find("Blockiverse UI Pointer Projection");
            Assert.That(projection, Is.Null, "Controller/ray visuals must not be routed through a projection composition layer.");

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);

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
                Assert.That((mainCamera.cullingMask & (1 << target.gameObject.layer)), Is.Not.EqualTo(0),
                    $"{path} should render through the normal scene camera.");
            }
        }

        [Test]
        public void CompositionLayerRenderScaleFramesTheCanvasCameraAtRuntime()
        {
            GameObject panel = new("Composition Render Scale Test", typeof(RectTransform));
            GameObject cameraObject = new("CanvasCamera");

            try
            {
                RectTransform rectTransform = panel.GetComponent<RectTransform>();
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 600.0f);
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 360.0f);
                panel.transform.localScale = Vector3.one * 0.002f;

                Canvas canvas = panel.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.enabled = true;
                panel.AddComponent<GraphicRaycaster>();

                TexturesExtension texturesExtension = panel.AddComponent<TexturesExtension>();
                Camera canvasCamera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetParent(panel.transform, false);

                BlockiverseCompositionLayerRenderScale renderScale =
                    panel.AddComponent<BlockiverseCompositionLayerRenderScale>();
                renderScale.Configure(canvas, null, texturesExtension, canvasCamera, 2.0f);

                renderScale.ApplyRenderScale();

                Assert.That(panel.GetComponent<GraphicRaycaster>(), Is.Null);
                Assert.That(panel.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
                Assert.That(panel.GetComponent<CanvasGroup>()?.blocksRaycasts, Is.True);
                Assert.That(canvasCamera.enabled, Is.True);
                Assert.That(canvasCamera.orthographic, Is.True);
                Assert.That(canvasCamera.orthographicSize, Is.EqualTo(0.36f).Within(0.001f));
                Assert.That(canvasCamera.aspect, Is.EqualTo(600.0f / 360.0f).Within(0.001f));
                Assert.That(canvasCamera.cullingMask, Is.EqualTo(1 << panel.layer));
                Assert.That(canvasCamera.targetTexture, Is.Not.Null);
                Assert.That(canvasCamera.targetTexture.width, Is.EqualTo(1200));
                Assert.That(canvasCamera.targetTexture.height, Is.EqualTo(720));
                Assert.That(texturesExtension.LeftTexture, Is.SameAs(canvasCamera.targetTexture));
                Assert.That(texturesExtension.RightTexture, Is.SameAs(canvasCamera.targetTexture));
            }
            finally
            {
                Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void PresenterShowHideControlsCompositionLayerVisibility()
        {
            GameObject panel = new("Composition Presenter Test");
            GameObject headset = new("Composition Presenter Headset");
            try
            {
                headset.transform.SetPositionAndRotation(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    Quaternion.LookRotation(Vector3.right, Vector3.up));
                Canvas canvas = panel.AddComponent<Canvas>();
                canvas.enabled = false;
                CompositionLayer compositionLayer = panel.AddComponent<CompositionLayer>();
                compositionLayer.ChangeLayerDataType(new QuadLayerData());
                compositionLayer.enabled = false;
                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                presenter.Configure(canvas, headset.transform, 2.0f, 0.5f, 0.25f, 10.0f, 0.003f);
                presenter.ConfigureCompositionLayer(compositionLayer);

                presenter.Show();

                Assert.That(canvas.enabled, Is.True);
                Assert.That(compositionLayer.enabled, Is.True);
                Assert.That(
                    Vector3.Distance(panel.transform.position, new Vector3(3.0f, 2.25f, 2.5f)),
                    Is.LessThan(0.001f));
                Assert.That(panel.transform.localScale, Is.EqualTo(Vector3.one * 0.003f));

                presenter.Hide();

                Assert.That(canvas.enabled, Is.False);
                Assert.That(compositionLayer.enabled, Is.False);

                presenter.ToggleVisible();

                Assert.That(canvas.enabled, Is.True);
                Assert.That(compositionLayer.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(headset);
            }
        }
    }
}
