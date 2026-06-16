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

        static readonly string[] ExpectedPanels =
        {
            "Startup Loading Overlay",
            "Survival HUD",
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
            "Block Menu",
            "Confirm Dialog",
        };

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
        public void GeneratedRigPanelsRenderAsWorldSpaceTrackedDeviceUi()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            Assert.That(vrUiLayer, Is.EqualTo(VrUiLayerIndex),
                "VR UI uses a dedicated layer so rays can target panels without including unrelated world geometry.");

            Transform cameraOffset = prefab.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            foreach (string name in ExpectedPanels)
            {
                Transform panel = cameraOffset.Find(name);
                Assert.That(panel, Is.Not.Null, $"{name} should be generated under the XR rig camera offset.");

                Canvas canvas = panel.GetComponent<Canvas>();
                CompositionLayer compositionLayer = panel.GetComponent<CompositionLayer>();
                TexturesExtension texturesExtension = panel.GetComponent<TexturesExtension>();
                InteractableUIMirror mirror = panel.GetComponent<InteractableUIMirror>();
                GraphicRaycaster legacyRaycaster = panel.GetComponent<GraphicRaycaster>();
                TrackedDeviceGraphicRaycaster trackedRaycaster = panel.GetComponent<TrackedDeviceGraphicRaycaster>();
                BlockiverseWorldSpacePanelPresenter presenter =
                    panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();

                Assert.That(canvas, Is.Not.Null, $"{name} should remain a normal world-space canvas.");
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace), $"{name} must render through the XR eye camera.");
                Assert.That(panel.gameObject.layer, Is.EqualTo(vrUiLayer), $"{name} should stay on the dedicated VR UI layer.");
                Assert.That(compositionLayer, Is.Null, $"{name} must not use in-game composition layers; they occlude scene-space rays/controllers.");
                Assert.That(texturesExtension, Is.Null, $"{name} must not submit compositor textures.");
                Assert.That(mirror, Is.Null, $"{name} must not use the composition-layer proxy UI path.");
                Assert.That(panel.Find("CanvasCamera"), Is.Null, $"{name} should not keep a hidden composition-layer mirror camera.");
                Assert.That(legacyRaycaster, Is.Null, $"{name} should not use screen-space GraphicRaycaster input.");
                if (ReceivesDirectTrackedDeviceInput(name))
                    Assert.That(trackedRaycaster, Is.Not.Null, $"{name} should receive the real XR ray directly.");
                else
                    Assert.That(trackedRaycaster, Is.Null, $"{name} should remain decorative and not receive UI rays.");
                Assert.That(presenter, Is.Not.Null, $"{name} should keep presenter-based routing.");

                foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(includeInactive: true))
                    Assert.That(graphic.gameObject.layer, Is.EqualTo(vrUiLayer),
                        $"{name}/{graphic.name} should stay on the dedicated VR UI layer.");
            }
        }

        static bool ReceivesDirectTrackedDeviceInput(string panelName)
        {
            return panelName != "Startup Loading Overlay";
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
        public void ControllerRayAndVrUiRenderThroughMainSceneCamera()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockiverseProject.XrRigPrefabPath);
            Transform cameraOffset = prefab?.transform.Find("Camera Offset");
            Assert.That(cameraOffset, Is.Not.Null);

            Transform projection = cameraOffset.Find("Blockiverse UI Pointer Projection");
            Assert.That(projection, Is.Null, "Controller/ray visuals must not be routed through a projection composition layer.");

            Camera mainCamera = cameraOffset.Find("Main Camera")?.GetComponent<Camera>();
            Assert.That(mainCamera, Is.Not.Null);
            int vrUiLayer = LayerMask.NameToLayer(VrUiLayerName);
            int vrUiLayerMask = 1 << vrUiLayer;
            Assert.That((mainCamera.cullingMask & vrUiLayerMask), Is.Not.EqualTo(0),
                "VR UI must render through the main eye camera so rays/controllers can appear in front of panels.");

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

            foreach (string path in new[]
            {
                "Left Controller/Interaction Ray",
                "Right Controller/Interaction Ray",
            })
            {
                XRRayInteractor ray = cameraOffset.Find(path)?.GetComponent<XRRayInteractor>();
                Assert.That(ray, Is.Not.Null, path);
                Assert.That((ray.raycastMask.value & BlockiverseProject.InteractionLayerMask), Is.Not.EqualTo(0),
                    $"{path} should still target voxel terrain.");
                Assert.That((ray.raycastMask.value & vrUiLayerMask), Is.Not.EqualTo(0),
                    $"{path} should include VR UI graphics in its tracked-device UI model.");
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
    }
}
