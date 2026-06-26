#pragma warning disable 0618
using System;
using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.InputSystem;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BootScenePlayModeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneLoadsWithXrRigAndCamera()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            Assert.That(rig, Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);

            Type markerType = Type.GetType("Blockiverse.VR.BlockiverseXRRigMarker, Blockiverse.VR");
            Assert.That(markerType, Is.Not.Null);
            Assert.That(rig.GetComponent(markerType), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator BootSceneShowsBoundSurvivalHudDisplay()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            SurvivalHudController survivalHud =
                UnityEngine.Object.FindAnyObjectByType<SurvivalHudController>(FindObjectsInactive.Include);

            Assert.That(survivalHud, Is.Not.Null);

            BlockiverseHudToolkitSurface hudSurface = survivalHud.GetComponent<BlockiverseHudToolkitSurface>();
            Assert.That(hudSurface, Is.Not.Null);
            Assert.That(hudSurface.IsVisible, Is.False, "The gameplay HUD starts hidden while the title/menu route is active.");
            Assert.That(hudSurface.CurrentHealthText, Is.EqualTo("100 / 100"));
        }

        [UnityTest]
        public IEnumerator BootSceneShowsDismissibleControllerMappingScreen()
        {
            string key = BlockiverseUiToolkitMenuPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

                GameObject uiToolkitSurfaceObject = FindGameObjectIncludingInactive("UI Toolkit Menu Surface");
                BlockiverseUiToolkitMenuSurface uiToolkitSurface =
                    uiToolkitSurfaceObject != null
                        ? uiToolkitSurfaceObject.GetComponent<BlockiverseUiToolkitMenuSurface>()
                        : null;
                BlockiverseMenuController controller =
                    UnityEngine.Object.FindAnyObjectByType<BlockiverseMenuController>(FindObjectsInactive.Include);

                Assert.That(controller, Is.Not.Null);
                Assert.That(uiToolkitSurface, Is.Not.Null);
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(uiToolkitSurface.IsVisible, Is.True,
                    "The first-run controller map must be rendered by the UI Toolkit menu surface.");

                controller.CloseControllerMappingScreen();
                yield return null;

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(uiToolkitSurface.IsVisible, Is.True,
                    "After dismissing the controller map, the title screen remains on the UI Toolkit surface.");
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        static GameObject FindGameObjectIncludingInactive(string name)
        {
            foreach (Transform transform in UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (transform.name == name)
                    return transform.gameObject;
            }

            return null;
        }

        [UnityTest]
        public IEnumerator BootSceneUsesNativeXrUiInteractionStack()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            yield return null;

            XRUIInputModule uiInputModule = UnityEngine.Object.FindAnyObjectByType<XRUIInputModule>();
            XRInteractionManager interactionManager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();

            Assert.That(uiInputModule, Is.Not.Null, "EventSystem should use XRUIInputModule for tracked-device UI.");
            Assert.That(interactionManager, Is.Not.Null, "Scene should contain an XRInteractionManager.");
            Assert.That(uiInputModule.enableXRInput, Is.True);
            Assert.That(uiInputModule.enableMouseInput, Is.False);
            Assert.That(uiInputModule.enableTouchInput, Is.False);
            AssertUiActionReference(
                uiInputModule.leftClickAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiPress);
            AssertUiActionReference(
                uiInputModule.scrollWheelAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiScroll);
            AssertUiActionReference(
                uiInputModule.navigateAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiScroll);
            AssertUiActionReference(
                uiInputModule.submitAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiPress);

            GameObject uiToolkitSurfaceObject = FindGameObjectIncludingInactive("UI Toolkit Menu Surface");
            Assert.That(uiToolkitSurfaceObject, Is.Not.Null);
            Assert.That(uiToolkitSurfaceObject.GetComponent<BlockiverseUiToolkitMenuSurface>(), Is.Not.Null);

            // Both controllers carry UI/block rays; the active dominant/tool hand owns visibility.
            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            foreach (string controllerName in new[] { "Left Controller", "Right Controller" })
            {
                Transform interactionRay = rig.transform.Find($"Camera Offset/{controllerName}/Interaction Ray");
                Assert.That(interactionRay, Is.Not.Null, controllerName);
                XRRayInteractor rayInteractor = interactionRay.GetComponent<XRRayInteractor>();
                Assert.That(rayInteractor, Is.Not.Null, controllerName);
                Assert.That(rayInteractor.enableUIInteraction, Is.True, controllerName);
                Assert.That(rayInteractor.blockUIOnInteractableSelection, Is.False, controllerName);
            }

            CreativeWorldManager worldManager = UnityEngine.Object.FindAnyObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            Assert.That(worldManager, Is.Not.Null);
            Assert.That(worldManager.World, Is.Null, "Boot should wait for Create/Load/Join before generating a voxel world.");
        }

        static void AssertUiActionReference(InputActionReference reference, string expectedMap, string expectedAction)
        {
            Assert.That(reference, Is.Not.Null, $"{expectedAction} reference must be configured explicitly.");
            Assert.That(reference.action, Is.Not.Null);
            Assert.That(reference.action.actionMap?.name, Is.EqualTo(expectedMap));
            Assert.That(reference.action.name, Is.EqualTo(expectedAction));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }
}
