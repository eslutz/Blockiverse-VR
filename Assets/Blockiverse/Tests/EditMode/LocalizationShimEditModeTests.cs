using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.UI.Toolkit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Plan Phase 3: the composition hazards fixed alongside the shim, each asserted through the
    // discriminating path so the tests fail if the call sites revert to hardcoded English.
    //
    // Two of them moved to UI Toolkit at the uGUI cutover, and the discriminator had to change
    // with them: BlockiverseLocalization.SetOverrideForTesting only rewrites the SHIM's lookup,
    // and UiText reads the string table directly with no override seam at all. So the ported
    // pair asserts the rendered text equals the table-formatted value AND that the call site
    // names the key in source. The source half is the part that can actually go red — an
    // interpolated "$"{a} / {b}"" would still match the table's own pattern.
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

        static string ReadScreenSource(string fileName) =>
            File.ReadAllText(Path.Combine(
                Application.dataPath, "Blockiverse", "Scripts", "UI", "ToolkitScreens", "Screens", fileName));

        TController CreateScreen<TController>() where TController : UiToolkitScreenController
        {
            var gameObject = new GameObject(typeof(TController).Name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<TController>();
        }

        static VisualElement AttachFreshTree(UiToolkitScreenController controller)
        {
            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                controller.GetType(), typeof(UiToolkitScreenAttribute));
            Assert.That(attribute, Is.Not.Null, $"{controller.GetType().Name} has no [UiToolkitScreen] attribute.");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML document missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return root;
        }

        // The age-policy suffix was the one true concatenation violation: hardcoded English
        // appended to an already-localized message. Reflection because the helper is private and
        // making it public for a test would widen the screen's surface for no runtime reason.
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
                MethodInfo method = typeof(LanMultiplayerScreenController).GetMethod(
                    "AppendAgePolicyNotice", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, "AppendAgePolicyNotice was renamed");

                string result = (string)method.Invoke(null, new object[] { "hello" });

                Assert.That(result, Is.EqualTo(UiText.Format(BlockiverseLocalization.Keys.LanAgePolicyNotice, "hello")),
                    "notice text bypassed the localization key");
                Assert.That(result, Is.Not.EqualTo("hello"),
                    "the no-entitlement branch did not run, so this asserted nothing");

                Assert.That(
                    ReadScreenSource("LanMultiplayerScreenController.cs"),
                    Does.Contain("UiText.Format(Keys.AgePolicyNotice"),
                    "the notice must be formatted from the table entry, never concatenated in source");
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

        // The discriminator, driving the actual screen. An earlier version of this test called
        // Format(key, ...) directly and stayed green with the panel's fix reverted — it proved
        // the KEY worked, not that the panel USED it, which is the difference between a test
        // and a tautology. On the uGUI panel the override mechanism supplied that discrimination;
        // UiText has no override seam, so the source assertion carries it instead.
        [Test]
        public void GameplayHudRendersItsRatioThroughTheKey()
        {
            GameplayHudController controller = CreateScreen<GameplayHudController>();
            VisualElement root = AttachFreshTree(controller);

            controller.Bind(new PlayerVitals(currentHealth: 75));

            Assert.That(
                root.Q<Label>("bv-health-ratio").text,
                Is.EqualTo(UiText.Format(BlockiverseLocalization.Keys.HealthVitalsRatio, 75, 100)),
                "the HUD is not rendering its ratio through ui.value.vitals_ratio");
            Assert.That(
                ReadScreenSource("GameplayHudController.cs"),
                Does.Contain("UiText.Format(Keys.HealthVitalsRatio"),
                "the ratio must come from the table entry, not from an interpolated string");
        }

        // The reverse lookup went from a dictionary-derived map to the frozen snapshot in
        // Phase 3b. These two English values collide with other keys and their winners are
        // order-dependent -- they are exactly the pair that would silently flip if anyone
        // "simplifies" the frozen map into a rebuild from table enumeration.
        [Test]
        public void FrozenReverseWinnersHoldTheirDeclarationOrderWinners()
        {
            Assert.That(
                BlockiverseLocalization.TryGetKnownKeyForDefaultText("Settings", out string settingsKey),
                Is.True);
            Assert.That(settingsKey, Is.EqualTo("ui.title.settings"));

            Assert.That(
                BlockiverseLocalization.TryGetKnownKeyForDefaultText("Return to Title", out string returnKey),
                Is.True);
            Assert.That(returnKey, Is.EqualTo("ui.action.pause.return_to_title"));

            Assert.That(
                BlockiverseLocalization.TryGetKnownKeyForDefaultText("Close", out string closeKey),
                Is.True);
            Assert.That(closeKey, Is.EqualTo("ui.action.error.close"),
                "the eleven-Close prefab binding's key changed winners");
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
