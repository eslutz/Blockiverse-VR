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

            SurvivalInventoryPanel inventoryPanel = UnityEngine.Object.FindFirstObjectByType<SurvivalInventoryPanel>();
            SurvivalCraftingPanel craftingPanel = UnityEngine.Object.FindFirstObjectByType<SurvivalCraftingPanel>();
            SurvivalHealthPanel healthPanel = UnityEngine.Object.FindFirstObjectByType<SurvivalHealthPanel>();

            Assert.That(inventoryPanel, Is.Not.Null);
            Assert.That(craftingPanel, Is.Not.Null);
            Assert.That(healthPanel, Is.Not.Null);

            Canvas canvas = inventoryPanel.GetComponentInParent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.enabled, Is.True);
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

                GameObject popup = GameObject.Find("Controller Mapping Popup");
                Assert.That(popup, Is.Not.Null);
                GameObject titleMenu = GameObject.Find("Title Menu");
                Assert.That(titleMenu, Is.Not.Null);

                Canvas canvas = popup.GetComponent<Canvas>();
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.enabled, Is.True);
                Canvas titleCanvas = titleMenu.GetComponent<Canvas>();
                Assert.That(titleCanvas, Is.Not.Null);
                Assert.That(titleCanvas.enabled, Is.False,
                    "The title menu must wait until the first-run controller map is dismissed.");

                BlockiverseWorldSpacePanelPresenter presenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                Assert.That(presenter, Is.Not.Null);
                Assert.That(presenter.IsVisible, Is.True);
                Assert.That(presenter.ShowOnStart, Is.False);

                Button closeButton = popup.transform.Find("Panel/Close Button")?.GetComponent<Button>();
                Assert.That(closeButton, Is.Not.Null);

                closeButton.onClick.Invoke();
                yield return null;

                Assert.That(canvas.enabled, Is.False);
                Assert.That(titleCanvas.enabled, Is.True);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneUsesNativeXrUiInteractionStack()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            yield return null;

            XRUIInputModule uiInputModule = UnityEngine.Object.FindFirstObjectByType<XRUIInputModule>();
            XRInteractionManager interactionManager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();

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
            SurvivalInventoryPanel inventoryPanel = UnityEngine.Object.FindFirstObjectByType<SurvivalInventoryPanel>();
            Assert.That(inventoryPanel, Is.Not.Null);
            Canvas hudCanvas = inventoryPanel.GetComponentInParent<Canvas>();
            Assert.That(hudCanvas.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(hudCanvas.GetComponent<GraphicRaycaster>(), Is.Null);

            // The right controller drives UI + block targeting with a UI-enabled native ray interactor.
            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            Transform interactionRay = rig.transform.Find("Camera Offset/Right Controller/Interaction Ray");
            Assert.That(interactionRay, Is.Not.Null);
            XRRayInteractor rayInteractor = interactionRay.GetComponent<XRRayInteractor>();
            Assert.That(rayInteractor, Is.Not.Null);
            Assert.That(rayInteractor.enableUIInteraction, Is.True);
            Assert.That(rayInteractor.blockUIOnInteractableSelection, Is.False);

            CreativeWorldManager worldManager = UnityEngine.Object.FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
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
