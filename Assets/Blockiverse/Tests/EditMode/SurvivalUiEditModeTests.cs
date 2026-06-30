using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
                    UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void InventoryPanelRendersSlotsAndSelectedHotbar()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 3, hotbarSlotCount: 2);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            inventory.SetSlot(2, new ItemStack(ItemId.ReedwoodDelver, 1));
            TMP_Text[] slotLabels = CreateTexts(3);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(slotLabels, selectedHotbarLabel);
            panel.Bind(inventory, itemRegistry, selectedHotbarSlotIndex: 1);

            Assert.That(slotLabels[0].text, Is.EqualTo(StackText(itemRegistry, ItemId.BranchwoodLog, 12)));
            Assert.That(slotLabels[1].text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty)));
            Assert.That(slotLabels[2].text, Is.EqualTo(StackText(itemRegistry, ItemId.ReedwoodDelver, 1)));
            Assert.That(selectedHotbarLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.InventoryHotbar,
                2,
                2)));
        }

        [Test]
        public void InventoryPanelStackFormattingDoesNotCreateDefaultRegistryPerSlot()
        {
            MethodInfo formatStack = typeof(SurvivalInventoryPanel).GetMethod(
                "FormatStack",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(formatStack, Is.Not.Null);
            Assert.That(CallsMethod(formatStack, typeof(ItemRegistry), nameof(ItemRegistry.CreateDefault)), Is.False);
        }

        [Test]
        public void CraftingPanelCraftsRecipeAndUpdatesStatus()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 4));
            TMP_Text[] recipeLabels = CreateTexts(4);
            TMP_Text statusLabel = CreateText("CraftStatus");
            SurvivalCraftingPanel panel = CreateComponent<SurvivalCraftingPanel>("CraftingPanel");

            panel.Configure(recipeLabels, statusLabel);
            panel.Bind(recipeBook, inventory, itemRegistry, CraftingStation.None);

            // Work Plank is the first handcraft recipe (§9.1): branchwood_log ×1 → work_plank ×6.
            Assert.That(recipeLabels[0].text, Does.Contain(StackText(itemRegistry, ItemId.WorkPlank, 6)));

            CraftingResult result = panel.TryCraftByOutput(ItemId.WorkPlank);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.WorkPlank), Is.EqualTo(6));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(3));
            Assert.That(statusLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.CraftingCrafted,
                StackText(itemRegistry, ItemId.WorkPlank, 6))));
        }

        [Test]
        public void CraftingPanelCraftsRecipeFromConfiguredButton()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 4));
            TMP_Text[] recipeLabels = CreateTexts(2);
            Button[] recipeButtons = CreateButtons(2);
            TMP_Text statusLabel = CreateText("CraftStatus");
            SurvivalCraftingPanel panel = CreateComponent<SurvivalCraftingPanel>("CraftingPanel");

            panel.Configure(recipeButtons, recipeLabels, statusLabel);
            panel.Bind(recipeBook, inventory, itemRegistry, CraftingStation.None);

            recipeButtons[0].onClick.Invoke();

            // Button 0 is bound to the first recipe (Work Plank).
            Assert.That(inventory.CountOf(ItemId.WorkPlank), Is.EqualTo(6));
            Assert.That(statusLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.CraftingCrafted,
                StackText(itemRegistry, ItemId.WorkPlank, 6))));
        }

        [Test]
        public void InventoryPanelSelectsHotbarSlotFromConfiguredButton()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 3);
            TMP_Text[] slotLabels = CreateTexts(4);
            Button[] slotButtons = CreateButtons(4);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel);
            panel.Bind(inventory, itemRegistry);

            slotButtons[2].onClick.Invoke();

            Assert.That(panel.SelectedHotbarSlotIndex, Is.EqualTo(2));
            Assert.That(selectedHotbarLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.InventoryHotbar,
                3,
                3)));
        }

        [Test]
        public void UiPanelsUseSharedFeedbackHelper()
        {
            string[] sourceFiles =
            {
                "Scripts/UI/BlockiverseActionMenu.cs",
                "Scripts/UI/SurvivalCratePanel.cs",
                "Scripts/UI/SurvivalInventoryPanel.cs",
                "Scripts/UI/SurvivalCraftingPanel.cs",
                "Scripts/UI/BlockiverseComfortMenu.cs",
                "Scripts/UI/BlockiverseMultiplayerSessionMenu.cs",
                "Scripts/UI/BlockiverseWorldSpacePanelPresenter.cs",
            };

            foreach (string sourceFile in sourceFiles)
            {
                string source = File.ReadAllText(Path.Combine(Application.dataPath, "Blockiverse", sourceFile));
                Assert.That(source, Does.Contain("BlockiverseUiFeedback.Play"));
                Assert.That(source, Does.Not.Contain("void DiscoverFeedback("));
            }
        }

        [Test]
        public void InventoryPanelPagesThroughAllDefaultInventorySlots()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(42, new ItemStack(ItemId.FieldBandage, 2));
            TMP_Text[] slotLabels = CreateTexts(10);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            TMP_Text pageLabel = CreateText("Page");
            Button previousPageButton = CreateComponent<Button>("PreviousPage");
            Button nextPageButton = CreateComponent<Button>("NextPage");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(
                null,
                slotLabels,
                selectedHotbarLabel,
                targetPreviousPageButton: previousPageButton,
                targetNextPageButton: nextPageButton,
                targetPageLabel: pageLabel);
            panel.Bind(inventory, itemRegistry);

            Assert.That(pageLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.InventorySlotsRange,
                1,
                10,
                44)));
            Assert.That(previousPageButton.interactable, Is.False);

            nextPageButton.onClick.Invoke();
            nextPageButton.onClick.Invoke();
            nextPageButton.onClick.Invoke();
            nextPageButton.onClick.Invoke();

            Assert.That(panel.FirstVisibleSlotIndex, Is.EqualTo(40));
            Assert.That(pageLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.InventorySlotsRange,
                41,
                44,
                44)));
            Assert.That(slotLabels[2].text, Is.EqualTo(StackText(itemRegistry, ItemId.FieldBandage, 2)));
            Assert.That(nextPageButton.interactable, Is.False);
        }

        [Test]
        public void InventoryPanelSelectsTenthHotbarSlotFromFirstPage()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            TMP_Text[] slotLabels = CreateTexts(10);
            Button[] slotButtons = CreateButtons(10);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel);
            panel.Bind(inventory, itemRegistry);

            slotButtons[9].onClick.Invoke();

            Assert.That(panel.SelectedHotbarSlotIndex, Is.EqualTo(9));
            Assert.That(selectedHotbarLabel.text, Is.EqualTo(BlockiverseLocalization.Format(
                BlockiverseLocalization.Keys.InventoryHotbar,
                10,
                10)));
        }

        [Test]
        public void InventoryPanelSwapsPagedBackpackSlotIntoSelectedHotbarSlot()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            inventory.SetSlot(10, new ItemStack(ItemId.FieldBandage, 2));
            TMP_Text[] slotLabels = CreateTexts(10);
            Button[] slotButtons = CreateButtons(10);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            Button nextPageButton = CreateComponent<Button>("NextPage");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(
                slotButtons,
                slotLabels,
                selectedHotbarLabel,
                targetNextPageButton: nextPageButton);
            panel.Bind(inventory, itemRegistry, selectedHotbarSlotIndex: 0);

            nextPageButton.onClick.Invoke();
            slotButtons[0].onClick.Invoke();

            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.FieldBandage, 2)));
            Assert.That(inventory.GetSlot(10), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
            Assert.That(slotLabels[0].text, Is.EqualTo(StackText(itemRegistry, ItemId.BranchwoodLog, 1)));
        }

        [Test]
        public void InventoryPanelSelectionPlaysUiSelectAndHapticTick()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 3);
            TMP_Text[] slotLabels = CreateTexts(4);
            Button[] slotButtons = CreateButtons(4);
            TMP_Text selectedHotbarLabel = CreateText("SelectedHotbar");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel);
            panel.ConfigureFeedback(audioCuePlayer, haptics);
            panel.Bind(inventory, itemRegistry);

            slotButtons[1].onClick.Invoke();

            Assert.That(panel.SelectedHotbarSlotIndex, Is.EqualTo(1));
            Assert.That(playedCues, Is.EqualTo(new[] { BlockiverseAudioCue.UiSelect }));
            Assert.That(uiTicks, Is.EqualTo(1));
        }

        [Test]
        public void CraftingPanelPlaysSuccessAndFailureFeedback()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            // One log allows exactly one Work Plank craft; the second attempt fails.
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            TMP_Text[] recipeLabels = CreateTexts(4);
            TMP_Text statusLabel = CreateText("CraftStatus");
            SurvivalCraftingPanel panel = CreateComponent<SurvivalCraftingPanel>("CraftingPanel");
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftSuccess, CreateClip("craft_success"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.CraftFail, CreateClip("craft_fail"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            panel.Configure(recipeLabels, statusLabel);
            panel.ConfigureFeedback(audioCuePlayer, haptics);
            panel.Bind(recipeBook, inventory, itemRegistry, CraftingStation.None);

            CraftingResult success = panel.TryCraftByOutput(ItemId.WorkPlank);
            CraftingResult failure = panel.TryCraftByOutput(ItemId.WorkPlank);

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
        public void HealthPanelUpdatesFromVitalsChanges()
        {
            var vitals = new PlayerVitals(currentHealth: 75);
            TMP_Text healthLabel = CreateText("Health");
            TMP_Text stateLabel = CreateText("HealthState");
            Slider healthSlider = CreateComponent<Slider>("HealthSlider");
            SurvivalHealthPanel panel = CreateComponent<SurvivalHealthPanel>("HealthPanel");

            panel.Configure(healthLabel, healthSlider, stateLabel);
            panel.Bind(vitals);

            Assert.That(healthLabel.text, Is.EqualTo("75 / 100"));
            Assert.That(healthSlider.maxValue, Is.EqualTo(100f));
            Assert.That(healthSlider.value, Is.EqualTo(75f));
            Assert.That(stateLabel.text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthStable)));

            vitals.ApplyDamage(80);

            Assert.That(healthLabel.text, Is.EqualTo("0 / 100"));
            Assert.That(healthSlider.value, Is.EqualTo(0f));
            Assert.That(stateLabel.text, Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.HealthDown)));
        }

        TMP_Text[] CreateTexts(int count)
        {
            var labels = new TMP_Text[count];
            for (int i = 0; i < count; i++)
                labels[i] = CreateText($"Text{i}");

            return labels;
        }

        Button[] CreateButtons(int count)
        {
            var buttons = new Button[count];
            for (int i = 0; i < count; i++)
                buttons[i] = CreateComponent<Button>($"Button{i}");

            return buttons;
        }

        TMP_Text CreateText(string name)
        {
            return CreateComponent<TextMeshProUGUI>(name);
        }

        T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            GameObject gameObject = new("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        BlockiverseInteractionHaptics CreateHaptics()
        {
            GameObject gameObject = new("Interaction Haptics");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseInteractionHaptics>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }

        static string StackText(ItemRegistry itemRegistry, ItemId itemId, int count)
        {
            return $"{itemRegistry.Get(itemId).Name} x{count}";
        }

        static bool CallsMethod(MethodInfo method, Type declaringType, string methodName)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();

            for (int i = 0; i <= il.Length - 5; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F)
                    continue;

                int metadataToken = BitConverter.ToInt32(il, i + 1);

                try
                {
                    MethodBase calledMethod = method.Module.ResolveMethod(metadataToken);
                    if (calledMethod.DeclaringType == declaringType && calledMethod.Name == methodName)
                        return true;
                }
                catch (ArgumentException)
                {
                    // Operand bytes can look like opcodes when scanning raw IL.
                }
            }

            return false;
        }
    }
}
