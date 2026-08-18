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

            // Bind through the shared Survival HUD root: the menu rework added routed
            // per-panel "Inventory Panel"/"Crafting Panel" canvases, so a global
            // FindFirstObjectByType can resolve panels from different canvases.
            GameObject hudRoot = FindGameObjectIncludingInactive(BlockiverseMenuController.SurvivalHudName);
            Assert.That(hudRoot, Is.Not.Null);
            SurvivalInventoryPanel inventoryPanel = hudRoot.GetComponentInChildren<SurvivalInventoryPanel>(true);
            SurvivalCraftingPanel craftingPanel = hudRoot.GetComponentInChildren<SurvivalCraftingPanel>(true);
            SurvivalHealthPanel healthPanel = hudRoot.GetComponentInChildren<SurvivalHealthPanel>(true);

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

                BlockiverseWorldSpacePanelPresenter presenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                Assert.That(presenter, Is.Not.Null);
                Assert.That(presenter.IsVisible, Is.True);
                Assert.That(presenter.ShowOnStart, Is.False);
                // Since the world-space menu pivot, routed menus hide by disabling
                // their Canvas via the presenter; the GameObject stays active.
                BlockiverseWorldSpacePanelPresenter titlePresenter =
                    titleMenu.GetComponent<BlockiverseWorldSpacePanelPresenter>();
                Assert.That(titlePresenter, Is.Not.Null);
                Assert.That(titlePresenter.IsVisible, Is.False,
                    "The title menu must wait until the first-run controller map is dismissed.");

                Button closeButton = popup.transform.Find("Panel/Close Button")?.GetComponent<Button>();
                Assert.That(closeButton, Is.Not.Null);

                closeButton.onClick.Invoke();
                yield return null;

                Assert.That(presenter.IsVisible, Is.False);
                Assert.That(titlePresenter.IsVisible, Is.True);
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
            SurvivalInventoryPanel inventoryPanel =
                UnityEngine.Object.FindFirstObjectByType<SurvivalInventoryPanel>(FindObjectsInactive.Include);
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

            CreativeWorldManager worldManager = UnityEngine.Object.FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            Assert.That(worldManager, Is.Not.Null);
            // Since the M8.5 menu gate, Boot deliberately generates the explorable
            // title mini-world (InitializeDefaultWorldOnAwake); the session itself
            // stays inactive with world input blocked until Create/Load/Join.
            Assert.That(worldManager.World, Is.Not.Null, "Boot generates the title mini-world at startup.");
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False,
                "World input stays blocked until a session grants it.");
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
