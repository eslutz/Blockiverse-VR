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
        public void GeneratedRigPanelsUseCompositionLayerMirrors()
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
                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();

                Assert.That(canvas, Is.Not.Null, $"{name} should remain a world-space canvas for existing UI code.");
                Assert.That(compositionLayer, Is.Not.Null, $"{name} should have a CompositionLayer.");
                Assert.That(compositionLayer.LayerData, Is.TypeOf<QuadLayerData>(), $"{name} should use a flat Quad layer.");
                Assert.That(compositionLayer.Order, Is.EqualTo(order), $"{name} should use the planned compositor order.");
                Assert.That(compositionLayer.enabled, Is.EqualTo(canvas.enabled), $"{name} layer visibility should match canvas visibility in the prefab.");
                Assert.That(texturesExtension, Is.Not.Null, $"{name} should have a source texture extension.");
                Assert.That(mirror, Is.Not.Null, $"{name} should use the interactive UI mirror.");
                Assert.That(renderScale, Is.Not.Null, $"{name} should supersample the mirror render target.");
                Assert.That(renderScale.RenderScale, Is.EqualTo(2.0f).Within(0.001f));
                Assert.That(presenter, Is.Not.Null, $"{name} should keep presenter-based routing.");
                Assert.That(presenter.CompositionLayer, Is.SameAs(compositionLayer));
            }
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
        public void PointerProjectionLayerRendersControllerAndRayVisualsAboveMenus()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Transform projection = cameraOffset.Find("Blockiverse UI Pointer Projection");
            Assert.That(projection, Is.Not.Null);

            CompositionLayer projectionLayer = projection.GetComponent<CompositionLayer>();
            TexturesExtension texturesExtension = projection.GetComponent<TexturesExtension>();
            int projectionUnityLayer = projection.gameObject.layer;

            Assert.That(projectionLayer, Is.Not.Null);
            Assert.That(projectionLayer.LayerData, Is.TypeOf<ProjectionLayerRigData>());
            Assert.That(projectionLayer.Order, Is.EqualTo(30));
            Assert.That(texturesExtension, Is.Not.Null);
            Assert.That(LayerMask.LayerToName(projectionUnityLayer), Is.EqualTo("Blockiverse UI Pointer Projection"));
            AssertProjectionEyeCamera(projection, "Left Camera", projectionUnityLayer, "<XRHMD>/leftEyePosition", "<XRHMD>/leftEyeRotation");
            AssertProjectionEyeCamera(projection, "Right Camera", projectionUnityLayer, "<XRHMD>/rightEyePosition", "<XRHMD>/rightEyeRotation");

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That((mainCamera.cullingMask & (1 << projectionUnityLayer)), Is.EqualTo(0),
                "Main scene rendering should cull pointer projection objects so they are only composited above UI layers.");

            foreach (string path in new[]
            {
                "Left Controller",
                "Right Controller",
                "Right Controller/Interaction Ray",
                "Left Controller/Teleport Ray",
                "Right Controller/Teleport Ray",
                "Left Aim Pose",
                "Right Aim Pose",
            })
            {
                Transform target = cameraOffset.Find(path);
                Assert.That(target, Is.Not.Null, path);
                Assert.That(target.gameObject.layer, Is.EqualTo(projectionUnityLayer), path);
            }
        }

        static void AssertProjectionEyeCamera(
            Transform projection,
            string cameraName,
            int projectionUnityLayer,
            string expectedPositionBinding,
            string expectedRotationBinding)
        {
            Transform cameraTransform = projection.Find(cameraName);
            Assert.That(cameraTransform, Is.Not.Null, cameraName);

            Camera camera = cameraTransform.GetComponent<Camera>();
            TrackedPoseDriver poseDriver = cameraTransform.GetComponent<TrackedPoseDriver>();

            Assert.That(camera, Is.Not.Null, cameraName);
            Assert.That(camera.cullingMask, Is.EqualTo(1 << projectionUnityLayer), cameraName);
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor), cameraName);
            Assert.That(camera.backgroundColor, Is.EqualTo(Color.clear), cameraName);
            Assert.That(camera.targetTexture, Is.Null, "Projection eye rigs generate textures at runtime.");
            Assert.That(poseDriver, Is.Not.Null, cameraName);
            Assert.That(poseDriver.positionAction.bindings[0].path, Is.EqualTo(expectedPositionBinding));
            Assert.That(poseDriver.rotationAction.bindings[0].path, Is.EqualTo(expectedRotationBinding));
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
