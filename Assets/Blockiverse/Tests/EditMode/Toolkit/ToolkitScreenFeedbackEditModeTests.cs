using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // The audio/haptic half of the uGUI survival panels, ported at the uGUI cutover. Both
    // tests here came from SurvivalUiEditModeTests and neither had a UI Toolkit mirror:
    // grepping Tests/EditMode/Toolkit for `BlockiverseAudioCue.` before this file returned
    // nothing, while CraftingScreenController fires CraftSuccess/CraftFail from six call
    // sites. Craft feedback could have stopped entirely with every screen test still green.
    public sealed class ToolkitScreenFeedbackEditModeTests
    {
        const string CraftingDocumentPath = "Assets/Blockiverse/UI/Documents/CraftingScreen.uxml";

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

        T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            var gameObject = new GameObject("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        static AudioClip CreateClip(string name) => AudioClip.Create(name, 16, 1, 44100, false);

        // CraftingScreenController has no ConfigureFeedback seam of its own — it resolves the
        // cue player and haptics lazily, and that resolution is a no-op outside Play mode. The
        // fields are set directly rather than adding a public seam for a test's benefit.
        static void InjectFeedback(
            CraftingScreenController controller,
            BlockiverseAudioCuePlayer audioCuePlayer,
            IBlockiverseInteractionHaptics haptics)
        {
            SetPrivateField(controller, "audioCuePlayer", audioCuePlayer);
            SetPrivateField(controller, "interactionHaptics", haptics);
        }

        static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"{target.GetType().Name} must expose private field '{fieldName}' for this feedback test.");
            field.SetValue(target, value);
        }

        // Ported from SurvivalUiEditModeTests.CraftingPanelPlaysSuccessAndFailureFeedback.
        // The ORDER is the assertion, not the membership: a screen that plays both cues on
        // every attempt, or that plays success after the inventory is already empty, sounds
        // wrong in the headset and reads fine in a set-based test.
        [Test]
        public void CraftingScreenPlaysSuccessAndFailureFeedback()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            // One log allows exactly one Work Plank craft; the second attempt fails.
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));

            CraftingScreenController controller = CreateComponent<CraftingScreenController>("Crafting Screen Under Test");
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CraftingDocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {CraftingDocumentPath}");
            controller.AttachForTest(tree.Instantiate());

            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateComponent<BlockiverseInteractionHaptics>("Interaction Haptics");
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftSuccess, CreateClip("craft_success"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftFail, CreateClip("craft_fail"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            InjectFeedback(controller, audioCuePlayer, haptics);
            controller.Bind(recipeBook, inventory, itemRegistry, CraftingStation.None);

            CraftingResult success = controller.TryCraftByOutput(ItemId.WorkPlank);
            CraftingResult failure = controller.TryCraftByOutput(ItemId.WorkPlank);

            Assert.That(success.Succeeded, Is.True);
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.CraftSuccess,
                BlockiverseAudioCue.CraftFail
            }));
            Assert.That(uiTicks, Is.EqualTo(2), "Both outcomes get a haptic tick; only the cue differs.");
        }

        // Ported from SurvivalUiEditModeTests.UiPanelsUseSharedFeedbackHelper, re-pointed from
        // the seven deleted uGUI panels at the Toolkit screens that inherited the behaviour.
        // It is a source-text invariant because the failure it guards is a REGRESSION IN SHAPE,
        // not in output: a screen that re-adds its own DiscoverFeedback() lookup still plays
        // the right cue in a test and silently reintroduces the per-screen scene scan the
        // shared helper exists to centralise.
        [Test]
        public void ToolkitScreensUseSharedFeedbackHelper()
        {
            // Every screen that plays feedback at all. StationScreenController is deliberately
            // absent — it plays none, same as its uGUI predecessor.
            string[] sourceFiles =
            {
                "TitleScreenController.cs",
                "PauseScreenController.cs",
                "DeathScreenController.cs",
                "SettingsHubScreenController.cs",
                "ConfirmDialogController.cs",
                "ErrorDialogController.cs",
                "WorldDetailsScreenController.cs",
                "ComfortSettingsScreenController.cs",
                "InventoryScreenController.cs",
                "CrateScreenController.cs",
                "CraftingScreenController.cs",
                "CreativeHotbarController.cs",
                "GameplayHudController.cs",
                "LanMultiplayerScreenController.cs",
            };

            foreach (string sourceFile in sourceFiles)
            {
                string source = File.ReadAllText(Path.Combine(
                    Application.dataPath, "Blockiverse", "Scripts", "UI", "ToolkitScreens", "Screens", sourceFile));

                Assert.That(source, Does.Contain("BlockiverseUiFeedback.Play"), sourceFile);
                Assert.That(source, Does.Not.Contain("void DiscoverFeedback("), sourceFile);
            }
        }
    }
}
