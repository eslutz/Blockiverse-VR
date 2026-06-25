#pragma warning disable 0618
using System;
using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using TMPro;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
        public IEnumerator BootSceneShowsBoundSurvivalHudPanels()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            SurvivalInventoryPanel inventoryPanel =
                UnityEngine.Object.FindAnyObjectByType<SurvivalInventoryPanel>(FindObjectsInactive.Include);
            SurvivalCraftingPanel craftingPanel =
                UnityEngine.Object.FindAnyObjectByType<SurvivalCraftingPanel>(FindObjectsInactive.Include);
            SurvivalHealthPanel healthPanel =
                UnityEngine.Object.FindAnyObjectByType<SurvivalHealthPanel>(FindObjectsInactive.Include);

            Assert.That(inventoryPanel, Is.Not.Null);
            Assert.That(craftingPanel, Is.Not.Null);
            Assert.That(healthPanel, Is.Not.Null);

            Canvas canvas = inventoryPanel.GetComponentInParent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.enabled, Is.False, "The gameplay HUD starts hidden while the title/menu route is active.");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(craftingPanel.GetComponentInParent<Canvas>(), Is.SameAs(canvas));
            Assert.That(healthPanel.GetComponentInParent<Canvas>(), Is.SameAs(canvas));

            AssertPanelContainsText(inventoryPanel.transform, "Hotbar 1 /");
            AssertPanelContainsText(inventoryPanel.transform, "Empty");
            AssertPanelContainsText(craftingPanel.transform, "Work Plank x6");
            AssertPanelContainsText(craftingPanel.transform, "Ready");
            AssertPanelContainsText(healthPanel.transform, "100 / 100");
            AssertPanelContainsText(healthPanel.transform, "Stable");
        }

        [UnityTest]
        public IEnumerator BootSceneShowsDismissibleControllerMappingPopup()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

                GameObject popup = FindGameObjectIncludingInactive("Controller Mapping Popup");
                Assert.That(popup, Is.Not.Null);
                GameObject titleMenu = FindGameObjectIncludingInactive("Title Menu");
                Assert.That(titleMenu, Is.Not.Null);
                Canvas titleCanvas = titleMenu.GetComponent<Canvas>();
                Assert.That(titleCanvas, Is.Not.Null);
                GameObject uiToolkitSurfaceObject = FindGameObjectIncludingInactive("UI Toolkit Menu Surface");
                BlockiverseUiToolkitMenuSurface uiToolkitSurface =
                    uiToolkitSurfaceObject != null
                        ? uiToolkitSurfaceObject.GetComponent<BlockiverseUiToolkitMenuSurface>()
                        : null;

                BlockiverseWorldSpacePanelPresenter presenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                Assert.That(presenter, Is.Not.Null);
                Assert.That(presenter.ShowOnStart, Is.False);
                Assert.That(presenter.IsVisible || (uiToolkitSurface != null && uiToolkitSurface.IsVisible), Is.True,
                    "The first-run controller map should be visible through either the legacy presenter or the UI Toolkit replacement.");
                Assert.That(titleCanvas.enabled, Is.False,
                    "The title menu must stay visually hidden until the first-run controller map is dismissed.");

                Button closeButton = popup.transform.Find("Panel/Close Button")?.GetComponent<Button>();
                Assert.That(closeButton, Is.Not.Null);

                closeButton.onClick.Invoke();
                yield return null;

                Assert.That(presenter.IsVisible, Is.False);
                Assert.That(titleCanvas.enabled || (uiToolkitSurface != null && uiToolkitSurface.IsVisible), Is.True,
                    "After dismissing the first-run controller map, the title menu should be visible through either the legacy canvas or the UI Toolkit replacement.");
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

            // World-space menus are raycast by the tracked-device raycaster, not the screen raycaster.
            SurvivalInventoryPanel inventoryPanel =
                UnityEngine.Object.FindAnyObjectByType<SurvivalInventoryPanel>(FindObjectsInactive.Include);
            Assert.That(inventoryPanel, Is.Not.Null);
            Canvas hudCanvas = inventoryPanel.GetComponentInParent<Canvas>();
            Assert.That(hudCanvas.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(hudCanvas.GetComponent<GraphicRaycaster>(), Is.Null);

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

        static void AssertPanelContainsText(Transform panel, string expectedText)
        {
            Text[] labels = panel.GetComponentsInChildren<Text>(includeInactive: true);
            TMP_Text[] tmpLabels = panel.GetComponentsInChildren<TMP_Text>(includeInactive: true);

            Assert.That(
                Array.Exists(labels, label => label != null && label.text.Contains(expectedText))
                    || Array.Exists(tmpLabels, label => label != null && label.text.Contains(expectedText)),
                Is.True,
                $"Expected panel {panel.name} to contain text '{expectedText}'.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }
}
