using System;
using System.Collections;
using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BootScenePlayModeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneLoadsWithXrRigAndCamera()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;

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
            AsyncOperation operation = SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;

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
            AssertPanelContainsText(craftingPanel.transform, "Workbench x1");
            AssertPanelContainsText(craftingPanel.transform, "Ready");
            AssertPanelContainsText(healthPanel.transform, "100 / 100");
            AssertPanelContainsText(healthPanel.transform, "Stable");
        }

        [UnityTest]
        public IEnumerator BootSceneShowsDismissibleControllerMappingPopup()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;

            GameObject popup = GameObject.Find("Controller Mapping Popup");
            Assert.That(popup, Is.Not.Null);

            Canvas canvas = popup.GetComponent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.enabled, Is.True);

            BlockiverseWorldSpacePanelPresenter presenter = popup.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsVisible, Is.True);

            Button closeButton = popup.transform.Find("Panel/Close Button")?.GetComponent<Button>();
            Assert.That(closeButton, Is.Not.Null);

            closeButton.onClick.Invoke();
            yield return null;

            Assert.That(canvas.enabled, Is.False);
        }

        static void AssertPanelContainsText(Transform panel, string expectedText)
        {
            Text[] labels = panel.GetComponentsInChildren<Text>(includeInactive: true);

            Assert.That(
                Array.Exists(labels, label => label != null && label.text.Contains(expectedText)),
                Is.True,
                $"Expected panel {panel.name} to contain text '{expectedText}'.");
        }
    }
}
