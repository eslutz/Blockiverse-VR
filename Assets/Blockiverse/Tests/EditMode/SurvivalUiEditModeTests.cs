using System.Collections.Generic;
using System.IO;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalUiEditModeTests
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
        public void SurvivalHudMenuStateSelectsHotbarAndSwapsBackpackSlot()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 2);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            inventory.SetSlot(2, new ItemStack(ItemId.FieldBandage, 2));
            SurvivalHudController hud = CreateComponent<SurvivalHudController>("SurvivalHudState");

            hud.BindMenuState(inventory, CraftingRecipeBook.CreateDefault(itemRegistry), itemRegistry);
            hud.HandleSlotSelection(1);

            Assert.That(hud.SelectedHotbarSlotIndex, Is.EqualTo(1));

            hud.HandleSlotSelection(2);

            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.FieldBandage, 2)));
            Assert.That(inventory.GetSlot(2), Is.EqualTo(ItemStack.Empty));
        }

        [Test]
        public void SurvivalHudSelectionPlaysUiSelectAndHapticTick()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 3);
            SurvivalHudController hud = CreateComponent<SurvivalHudController>("SurvivalHudState");
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            hud.ConfigureFeedback(audioCuePlayer, haptics);
            hud.BindMenuState(inventory, CraftingRecipeBook.CreateDefault(itemRegistry), itemRegistry);
            hud.HandleSlotSelection(1);

            Assert.That(hud.SelectedHotbarSlotIndex, Is.EqualTo(1));
            Assert.That(playedCues, Is.EqualTo(new[] { BlockiverseAudioCue.UiSelect }));
            Assert.That(uiTicks, Is.EqualTo(1));
        }

        [Test]
        public void SurvivalHudMenuStateCraftsRecipeAndUpdatesStatus()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 4));
            SurvivalHudController hud = CreateComponent<SurvivalHudController>("SurvivalHudState");

            hud.BindMenuState(inventory, recipeBook, itemRegistry, CraftingStationSet.Of(CraftingStation.BuildTable));
            CraftingResult result = hud.TryCraftByOutput(ItemId.WorkPlank);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.WorkPlank), Is.EqualTo(6));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(3));
            Assert.That(hud.CurrentCraftingStatusText, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.CraftingCrafted,
                StackText(itemRegistry, ItemId.WorkPlank, 6))));
        }

        [Test]
        public void SurvivalHudMenuStatePlaysSuccessAndFailureFeedback()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            SurvivalHudController hud = CreateComponent<SurvivalHudController>("SurvivalHudState");
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftSuccess, CreateClip("craft_success"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftFail, CreateClip("craft_fail"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            hud.ConfigureFeedback(audioCuePlayer, haptics);
            hud.BindMenuState(inventory, recipeBook, itemRegistry, CraftingStationSet.Of(CraftingStation.BuildTable));

            CraftingResult success = hud.TryCraftByOutput(ItemId.WorkPlank);
            CraftingResult failure = hud.TryCraftByOutput(ItemId.WorkPlank);

            Assert.That(success.Succeeded, Is.True);
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.CraftSuccess,
                BlockiverseAudioCue.CraftFail
            }));
            Assert.That(uiTicks, Is.EqualTo(2));
        }

        [Test]
        public void UiMenuStateUsesSharedFeedbackHelper()
        {
            string[] sourceFiles =
            {
                "Scripts/UI/SurvivalHudController.cs",
                "Scripts/VR/BlockiverseUiToolkitMenuPresenter.cs",
            };

            foreach (string sourceFile in sourceFiles)
            {
                string source = File.ReadAllText(Path.Combine(Application.dataPath, "Blockiverse", sourceFile));
                Assert.That(source, Does.Contain("BlockiverseUiFeedback.Play"));
                Assert.That(source, Does.Not.Contain("void DiscoverFeedback("));
            }
        }

        [Test]
        public void SurvivalHudUpdatesFromVitalsChanges()
        {
            var vitals = new PlayerVitals(currentHealth: 75);
            SurvivalHudController hud = CreateComponent<SurvivalHudController>("SurvivalHudState");
            BlockiverseHudToolkitSurface surface = hud.gameObject.AddComponent<BlockiverseHudToolkitSurface>();
            surface.Configure(hud.gameObject.AddComponent<UIDocument>());

            hud.Configure(targetHudSurface: surface);
            hud.BindMenuState(
                new Inventory(ItemRegistry.CreateDefault()),
                CraftingRecipeBook.Default,
                ItemRegistry.Default);
            hud.BindVitals(vitals);

            Assert.That(surface.CurrentHealthText, Is.EqualTo("75 / 100"));
            Assert.That(surface.CurrentHealthValue, Is.EqualTo(75f));

            vitals.ApplyDamage(80);
            hud.BindVitals(vitals);

            Assert.That(surface.CurrentHealthText, Is.EqualTo("0 / 100"));
            Assert.That(surface.CurrentHealthValue, Is.EqualTo(0f));
        }

        static string StackText(ItemRegistry registry, ItemId itemId, int count) =>
            BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.CommonStack,
                registry.Get(itemId).Name,
                count);

        T CreateComponent<T>(string name) where T : Component
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target.AddComponent<T>();
        }

        BlockiverseAudioCuePlayer CreateCuePlayer() => CreateComponent<BlockiverseAudioCuePlayer>("AudioCuePlayer");

        BlockiverseInteractionHaptics CreateHaptics() => CreateComponent<BlockiverseInteractionHaptics>("Haptics");

        AudioClip CreateClip(string name) => AudioClip.Create(name, 32, 1, 44100, false);
    }
}
