using System;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Survival;
using Blockiverse.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    // Plan Phase 3: the composition hazards fixed alongside the shim, each asserted through the
    // discriminating path — the override mechanism — so the tests fail if the call sites revert
    // to hardcoded English (verified by reverting each fix and watching the red).
    public sealed class LocalizationShimEditModeTests
    {
        readonly List<UnityEngine.Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            BlockiverseLocalization.ClearOverridesForTesting();

            foreach (UnityEngine.Object target in objectsToDestroy)
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        // The age-policy suffix was the one true concatenation violation: hardcoded English
        // appended to an already-localized message. Reflection because the helper is private and
        // making it public for a test would widen a surface that dies with uGUI Phase 6.
        //
        // The policy callback is forced to "no entitlement" so the notice branch is reachable on
        // every machine — the first version of this test went Inconclusive wherever a Meta
        // entitlement existed, which made it no discriminator at all exactly where it mattered.
        [Test]
        public void AgePolicyNoticeRoutesThroughItsKey()
        {
            Func<bool> saved = BlockiverseMetaSocialPolicy.CanUseMetaSocialFeatureCallback;
            BlockiverseMetaSocialPolicy.CanUseMetaSocialFeatureCallback = () => false;

            try
            {
                BlockiverseLocalization.SetOverrideForTesting(
                    BlockiverseLocalization.Keys.LanAgePolicyNotice, "OVERRIDE[{0}]");

                MethodInfo method = typeof(BlockiverseMultiplayerSessionMenu).GetMethod(
                    "AppendAgePolicyNotice", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, "AppendAgePolicyNotice was renamed");

                string result = (string)method.Invoke(null, new object[] { "hello" });

                Assert.That(result, Is.EqualTo("OVERRIDE[hello]"),
                    "notice text bypassed the localization key");
            }
            finally
            {
                BlockiverseMetaSocialPolicy.CanUseMetaSocialFeatureCallback = saved;
            }
        }

        [Test]
        public void AgePolicyPatternFormatsTheMessageIn()
        {
            string result = BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.LanAgePolicyNotice, "Session ended.");

            Assert.That(result, Does.StartWith("Session ended.\n"));
            Assert.That(result, Does.Contain("Meta social features"));
        }

        [Test]
        public void VitalsRatioFormatsThroughItsKey()
        {
            Assert.That(
                BlockiverseLocalization.Format(
                    BlockiverseLocalization.Keys.HealthVitalsRatio, 17, 20),
                Is.EqualTo("17 / 20"));
        }

        // The discriminator, driving the actual panel. An earlier version of this test called
        // Format(key, ...) directly and stayed green with the panel's fix reverted — it proved
        // the KEY worked, not that the panel USED it, which is the difference between a test
        // and a tautology. Overriding the key and reading the label the panel wrote is the only
        // assertion the call site can fail.
        [Test]
        public void HealthPanelRendersItsRatioThroughTheKey()
        {
            BlockiverseLocalization.SetOverrideForTesting(
                BlockiverseLocalization.Keys.HealthVitalsRatio, "{0} of {1} HP");

            var vitals = new PlayerVitals(currentHealth: 75);
            TMP_Text healthLabel = CreateComponent<TextMeshProUGUI>("Health");
            TMP_Text stateLabel = CreateComponent<TextMeshProUGUI>("HealthState");
            Slider healthSlider = CreateComponent<Slider>("HealthSlider");
            SurvivalHealthPanel panel = CreateComponent<SurvivalHealthPanel>("HealthPanel");

            panel.Configure(healthLabel, healthSlider, stateLabel);
            panel.Bind(vitals);

            Assert.That(healthLabel.text, Is.EqualTo("75 of 100 HP"),
                "the panel is not rendering its ratio through Keys.HealthVitalsRatio");
        }

        // Handcraft moved from a hardcoded fallback argument into the table; the entry itself is
        // guarded by HandcraftEntryExists, this guards the DisplayName path end to end.
        [Test]
        public void HandcraftStillResolvesForStationNone()
        {
            Assert.That(
                BlockiverseLocalization.DisplayName(CraftingStation.None),
                Is.EqualTo("Handcraft"));
        }
    }
}
