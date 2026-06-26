using System;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.UIInteraction;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR;
using UIDocument = UnityEngine.UIElements.UIDocument;

namespace Blockiverse.Tests.EditMode
{
    public sealed class CompositionLayerUiEditModeTests
    {
        const string VrUiLayerName = "BlockiverseVRUI";
        const int VrUiLayerIndex = 13;
        const string InteractionLayerName = "BlockiverseInteractable";
        const int InteractionLayerIndex = 10;
        const string XrVisualLayerName = "BlockiverseXrVisuals";
        const int XrVisualLayerIndex = 12;
        const string XrVisualProjectionRigName = "Blockiverse XR Visual Projection Rig";
        const string NamedLaunchArtworkPath = "Assets/Blockiverse/Art/Sprites/Branding/blockiverse_launch_landscape_named.png";

        [SetUp]
        public void EnsureProjectLayersAvailable()
        {
            EnsureExpectedLayer(BlockiverseProject.InteractionLayerName, BlockiverseProject.InteractionLayerIndex);
            EnsureExpectedLayer(BlockiverseProject.CompositionUiLayerName, BlockiverseProject.CompositionUiLayerIndex);
            EnsureExpectedLayer(BlockiverseProject.XrVisualProjectionLayerName, BlockiverseProject.XrVisualProjectionLayerIndex);
            EnsureExpectedLayer(BlockiverseProject.VrUiLayerName, BlockiverseProject.VrUiLayerIndex);
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
        public void GeneratedRigUsesUiToolkitMenuSurfaceWithoutCompositionMenuInput()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<CompositionLayer>(includeInactive: true), Is.Empty,
                "The generated rig should not contain interactive composition layers.");
            Assert.That(prefab.GetComponentsInChildren<InteractableUIMirror>(includeInactive: true), Is.Empty,
                "The generated rig should not proxy menu input through composition-layer UI mirroring.");

            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(vrUiLayer, Is.EqualTo(VrUiLayerIndex));

            Assert.That(cameraOffset.Find("UI Toolkit Menu Surface"), Is.Null,
                "The generated rig should not parent the fixed title/menu UI surface to the player rig.");

            AssertNonVisualMenuState(cameraOffset.Find("Creative Tools Menu State"), typeof(BlockiverseCreativeToolsInteractionState));
            AssertNonVisualMenuState(cameraOffset.Find("Station Menu State"), typeof(BlockiverseStationInteractionState));
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

            int visualLayer = LayerMask.NameToLayer(XrVisualLayerName);
            Assert.That(visualLayer, Is.EqualTo(XrVisualLayerIndex));
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(vrUiLayer, Is.EqualTo(VrUiLayerIndex));

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            Assert.That((mainCamera.cullingMask & (1 << visualLayer)), Is.EqualTo(0));
            Assert.That((mainCamera.cullingMask & (1 << vrUiLayer)), Is.Not.EqualTo(0),
                "World-space UI Toolkit menus must render through the main eye camera.");
            Assert.That((mainCamera.cullingMask & BlockiverseProject.InteractionLayerMask), Is.Not.EqualTo(0),
                "Voxel terrain/interactables should remain visible through the main eye camera.");

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
                Assert.That(ray.raycastMask.value, Is.EqualTo(BlockiverseProject.VrUiRaycastLayerMask),
                    $"{path} should target only the dedicated UI Toolkit menu raycast layer.");
                Assert.That((ray.raycastMask.value & BlockiverseProject.CompositionUiLayerMask), Is.EqualTo(0),
                    $"{path} should not target the composition UI layer for menu input.");
                Assert.That(ray.interactionLayers.value, Is.EqualTo(BlockiverseRayDefaults.DefaultXriInteractionLayerMask));
            }
        }

        static void AssertNonVisualMenuState(Transform panel, Type expectedComponentType)
        {
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.GetComponent(expectedComponentType), Is.Not.Null);
            Assert.That(panel.GetComponent("Canvas"), Is.Null);
            Assert.That(panel.GetComponent("TrackedDeviceGraphicRaycaster"), Is.Null);
            Assert.That(panel.GetComponent("GraphicRaycaster"), Is.Null);
            Assert.That(panel.GetComponent<BlockiverseUiToolkitMenuPresenter>(), Is.Null);
            Assert.That(panel.childCount, Is.EqualTo(0));
        }
    }
}
