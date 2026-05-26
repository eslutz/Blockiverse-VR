using System.Collections.Generic;
using Blockiverse.Survival;
using Blockiverse.UI;
using NUnit.Framework;
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
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void InventoryPanelRendersSlotsAndSelectedHotbar()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 3, hotbarSlotCount: 2);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 12));
            inventory.SetSlot(2, new ItemStack(ItemId.Pick, 1));
            Text[] slotLabels = CreateTexts(3);
            Text selectedHotbarLabel = CreateText("SelectedHotbar");
            SurvivalInventoryPanel panel = CreateComponent<SurvivalInventoryPanel>("InventoryPanel");

            panel.Configure(slotLabels, selectedHotbarLabel);
            panel.Bind(inventory, itemRegistry, selectedHotbarSlotIndex: 1);

            Assert.That(slotLabels[0].text, Is.EqualTo("Timber x12"));
            Assert.That(slotLabels[1].text, Is.EqualTo("Empty"));
            Assert.That(slotLabels[2].text, Is.EqualTo("Pick x1"));
            Assert.That(selectedHotbarLabel.text, Is.EqualTo("Hotbar 2 / 2"));
        }

        [Test]
        public void CraftingPanelCraftsRecipeAndUpdatesStatus()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 4));
            Text[] recipeLabels = CreateTexts(4);
            Text statusLabel = CreateText("CraftStatus");
            SurvivalCraftingPanel panel = CreateComponent<SurvivalCraftingPanel>("CraftingPanel");

            panel.Configure(recipeLabels, statusLabel);
            panel.Bind(recipeBook, inventory, itemRegistry, CraftingStation.None);

            Assert.That(recipeLabels[0].text, Does.Contain("Workbench x1"));

            CraftingResult result = panel.TryCraftByOutput(ItemId.Workbench);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(inventory.CountOf(ItemId.Workbench), Is.EqualTo(1));
            Assert.That(inventory.CountOf(ItemId.Timber), Is.Zero);
            Assert.That(statusLabel.text, Is.EqualTo("Crafted Workbench x1"));
        }

        [Test]
        public void HealthPanelUpdatesFromVitalsChanges()
        {
            var vitals = new PlayerVitals(currentHealth: 75);
            Text healthLabel = CreateText("Health");
            Text stateLabel = CreateText("HealthState");
            Slider healthSlider = CreateComponent<Slider>("HealthSlider");
            SurvivalHealthPanel panel = CreateComponent<SurvivalHealthPanel>("HealthPanel");

            panel.Configure(healthLabel, healthSlider, stateLabel);
            panel.Bind(vitals);

            Assert.That(healthLabel.text, Is.EqualTo("75 / 100"));
            Assert.That(healthSlider.maxValue, Is.EqualTo(100f));
            Assert.That(healthSlider.value, Is.EqualTo(75f));
            Assert.That(stateLabel.text, Is.EqualTo("Stable"));

            vitals.ApplyDamage(80);

            Assert.That(healthLabel.text, Is.EqualTo("0 / 100"));
            Assert.That(healthSlider.value, Is.EqualTo(0f));
            Assert.That(stateLabel.text, Is.EqualTo("Down"));
        }

        Text[] CreateTexts(int count)
        {
            var labels = new Text[count];
            for (int i = 0; i < count; i++)
                labels[i] = CreateText($"Text{i}");

            return labels;
        }

        Text CreateText(string name)
        {
            return CreateComponent<Text>(name);
        }

        T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<T>();
        }
    }
}
