using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalHudFeedbackEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void MiningProgressShowsHudStatusAndProgressSlider()
        {
            SurvivalHudController hud = CreateHud(out TMP_Text statusLabel, out Slider progressSlider);

            InvokePrivate(
                hud,
                "OnMiningProgressChanged",
                new BlockPosition(1, 2, 3),
                0.5f,
                1.0f);

            Assert.That(statusLabel.gameObject.activeSelf, Is.True);
            Assert.That(statusLabel.text, Is.EqualTo("Mining 50%"));
            Assert.That(progressSlider.gameObject.activeSelf, Is.True);
            Assert.That(progressSlider.value, Is.EqualTo(0.5f).Within(0.001f));

            InvokePrivate(hud, "OnMiningProgressCleared");

            Assert.That(statusLabel.gameObject.activeSelf, Is.False);
            Assert.That(progressSlider.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void InventoryFullHarvestRejectionShowsHudStatus()
        {
            SurvivalHudController hud = CreateHud(out TMP_Text statusLabel, out Slider progressSlider);
            SurvivalCommandResult result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.HarvestResource,
                SurvivalCommandFailureReason.HarvestRejected,
                harvestFailureReason: BlockHarvestFailureReason.InventoryFull);

            InvokePrivate(hud, "OnCommandFeedback", result, new BlockPosition(1, 2, 3));

            Assert.That(statusLabel.gameObject.activeSelf, Is.True);
            Assert.That(statusLabel.text, Is.EqualTo("Inventory full"));
            Assert.That(progressSlider.gameObject.activeSelf, Is.False);
        }

        SurvivalHudController CreateHud(out TMP_Text statusLabel, out Slider progressSlider)
        {
            GameObject hudObject = new("Survival HUD");
            objectsToDestroy.Add(hudObject);
            SurvivalHudController hud = hudObject.AddComponent<SurvivalHudController>();

            GameObject statusObject = new("Status", typeof(RectTransform));
            statusObject.transform.SetParent(hudObject.transform, false);
            statusLabel = statusObject.AddComponent<TextMeshProUGUI>();

            GameObject progressObject = new("Mining Progress", typeof(RectTransform));
            progressObject.transform.SetParent(hudObject.transform, false);
            progressSlider = progressObject.AddComponent<Slider>();
            progressObject.SetActive(false);

            hud.Configure(
                targetInventoryPanel: null,
                targetCraftingPanel: null,
                targetHealthPanel: null,
                targetCratePanel: null,
                targetStatusLabel: statusLabel,
                targetMiningProgressSlider: progressSlider);

            return hud;
        }

        static void InvokePrivate(Object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"{methodName} should exist.");
            method.Invoke(target, args);
        }
    }
}
