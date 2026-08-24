using System.IO;
using Blockiverse.Core;
using Blockiverse.VR;
using NUnit.Framework;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.UIInteraction;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
        const string CompositionRenderScaleScriptGuid = "7286615c28b0643b79b89c8fea0a07f5";

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

        // Menus now render through UI Toolkit UIDocuments, so the per-panel Canvas/presenter
        // sweep this test used to run over the 20 generated uGUI panels went with them. What
        // it always guarded is still here and still specific to the rig: no compositor layer,
        // no UI mirror, no texture extension anywhere under it. Gameplay UI submitting a
        // composition layer over the eye buffer is a device-only regression — it looks correct
        // in the editor and wrong on the headset — which is why the negative stays.
        [Test]
        public void GeneratedRigUsesWorldSpaceXrMenusWithoutCompositionMenuSurface()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);
            
            Transform menuSurface = cameraOffset.Find(MenuCompositionSurfaceName);
            Assert.That(menuSurface, Is.Null,
                "Gameplay menus must render as normal world-space XR canvases; only the startup splash may use composition layers.");

            Assert.That(prefab.GetComponentInChildren<CompositionLayer>(includeInactive: true), Is.Null,
                "Gameplay UI should not submit compositor layers over the eye buffer.");
            Assert.That(prefab.GetComponentInChildren<InteractableUIMirror>(includeInactive: true), Is.Null,
                "Gameplay menus should receive XRI UI rays directly instead of through composition-layer mirroring.");
            Assert.That(prefab.GetComponentInChildren<TexturesExtension>(includeInactive: true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<BlockiverseCompositionMenuCursor>(includeInactive: true), Is.Null);

            int interactionLayerIndex = LayerMask.NameToLayer(InteractionLayerName);
            Assert.That(interactionLayerIndex, Is.EqualTo(InteractionLayerIndex));
        }

        [Test]
        public void GeneratedRigDoesNotShipStaleCompositionMenuArtifacts()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            Assert.That(FindDescendant(prefab.transform, MenuCompositionSurfaceName), Is.Null);
            Assert.That(FindDescendant(prefab.transform, MenuCompositionCanvasName), Is.Null);
            Assert.That(FindDescendant(prefab.transform, "CanvasCamera"), Is.Null);
            Assert.That(FindDescendant(prefab.transform, "Composition Render Scale Surface"), Is.Null);
            Assert.That(FindDescendant(prefab.transform, "Composition Layer Plane"), Is.Null);
            Assert.That(ReadXrRigPrefabYaml(), Does.Not.Contain(CompositionRenderScaleScriptGuid),
                "The generated rig must not retain the deleted custom composition RenderTexture renderer as a stale script reference.");

            Assert.That(prefab.GetComponentInChildren<CompositionLayer>(includeInactive: true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<CompositionOutline>(includeInactive: true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<TexturesExtension>(includeInactive: true), Is.Null);
            Assert.That(prefab.GetComponentInChildren<InteractableUIMirror>(includeInactive: true), Is.Null);
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
            Assert.That(cameraOffset.Find(MenuCompositionSurfaceName), Is.Null,
                "Gameplay menus should stay on the main eye camera path.");

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

        static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
                return null;

            foreach (Transform child in root)
            {
                if (child.name == name)
                    return child;

                Transform descendant = FindDescendant(child, name);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        static string ReadXrRigPrefabYaml() => File.ReadAllText(BlockiverseProject.XrRigPrefabPath);
    }
}
