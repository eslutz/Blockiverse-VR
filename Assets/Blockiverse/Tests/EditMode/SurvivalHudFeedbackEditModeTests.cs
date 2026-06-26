using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

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
            SurvivalHudController hud = CreateHud(out BlockiverseHudToolkitSurface surface);

            InvokePrivate(
                hud,
                "OnMiningProgressChanged",
                new BlockPosition(1, 2, 3),
                0.5f,
                1.0f);

            Assert.That(surface.CurrentStatusText, Is.EqualTo("Mining 50%"));
            Assert.That(surface.IsMiningProgressVisible, Is.True);
            Assert.That(surface.CurrentMiningProgress, Is.EqualTo(0.5f).Within(0.001f));

            InvokePrivate(hud, "OnMiningProgressCleared");

            Assert.That(surface.CurrentStatusText, Is.Empty);
            Assert.That(surface.IsMiningProgressVisible, Is.False);
        }

        [Test]
        public void InventoryFullHarvestRejectionShowsHudStatus()
        {
            SurvivalHudController hud = CreateHud(out BlockiverseHudToolkitSurface surface);
            SurvivalCommandResult result = SurvivalCommandResult.Reject(
                SurvivalCommandKind.HarvestResource,
                SurvivalCommandFailureReason.HarvestRejected,
                harvestFailureReason: BlockHarvestFailureReason.InventoryFull);

            InvokePrivate(hud, "OnCommandFeedback", result, new BlockPosition(1, 2, 3));

            Assert.That(surface.CurrentStatusText, Is.EqualTo("Inventory full"));
            Assert.That(surface.IsMiningProgressVisible, Is.False);
        }

        SurvivalHudController CreateHud(out BlockiverseHudToolkitSurface surface)
        {
            GameObject hudObject = new("Survival HUD");
            objectsToDestroy.Add(hudObject);
            UIDocument document = hudObject.AddComponent<UIDocument>();
            surface = hudObject.AddComponent<BlockiverseHudToolkitSurface>();
            surface.Configure(document);
            SurvivalHudController hud = hudObject.AddComponent<SurvivalHudController>();
            hud.Configure(targetHudSurface: surface);

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
