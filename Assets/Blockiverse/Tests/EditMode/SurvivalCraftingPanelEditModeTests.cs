using System.Collections.Generic;
using System.Linq;
using Blockiverse.Survival;
using Blockiverse.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class SurvivalCraftingPanelEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
        }

        [Test]
        public void PagingExposesRecipesBeyondGeneratedVisibleRowsAndCraftsVisiblePageSelection()
        {
            SurvivalCraftingPanel panel = CreatePanel(
                rowCount: 5,
                out Button[] recipeButtons,
                out TMP_Text[] recipeLabels,
                out Button nextButton,
                out TMP_Text pageLabel);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);
            Inventory inventory = new(registry);
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 8));
            inventory.SetSlot(1, new ItemStack(ItemId.FiberCord, 2));

            panel.Bind(recipeBook, inventory, registry, CraftingStation.None);

            Assert.That(recipeLabels.Any(label => label.text.Contains("Work Plank")), Is.True);
            Assert.That(recipeLabels.Any(label => label.text.Contains("Build Table")), Is.False);

            nextButton.onClick.Invoke();

            Assert.That(pageLabel.text, Does.StartWith("2/"));
            Assert.That(recipeLabels.Any(label => label.text.Contains("Build Table")), Is.True);

            recipeButtons[2].onClick.Invoke();

            Assert.That(inventory.CountOf(ItemId.BuildTable), Is.EqualTo(1));
        }

        [Test]
        public void RecipeLabelsIncludeAvailabilityMarkers()
        {
            SurvivalCraftingPanel panel = CreatePanel(
                rowCount: 12,
                out _,
                out TMP_Text[] recipeLabels,
                out _,
                out _);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);
            Inventory inventory = new(registry);

            panel.Bind(recipeBook, inventory, registry, CraftingStation.None);

            Assert.That(LabelContaining(recipeLabels, "Work Plank").text, Does.StartWith("✗ "));

            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            panel.Refresh();

            Assert.That(LabelContaining(recipeLabels, "Work Plank").text, Does.StartWith("✓ "));

            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 12));
            inventory.SetSlot(1, new ItemStack(ItemId.StoutPole, 2));
            panel.Refresh();

            Assert.That(LabelContaining(recipeLabels, "Storage Crate").text, Does.StartWith("! "));

            panel.SetAvailableStations(CraftingStationSet.Of(CraftingStation.BuildTable));

            Assert.That(LabelContaining(recipeLabels, "Storage Crate").text, Does.StartWith("✓ "));
        }

        static TMP_Text LabelContaining(TMP_Text[] labels, string text) =>
            labels.First(label => label != null && label.text.Contains(text));

        SurvivalCraftingPanel CreatePanel(
            int rowCount,
            out Button[] recipeButtons,
            out TMP_Text[] recipeLabels,
            out Button nextButton,
            out TMP_Text pageLabel)
        {
            GameObject panelObject = CreateObject("Crafting Panel");
            SurvivalCraftingPanel panel = panelObject.AddComponent<SurvivalCraftingPanel>();

            recipeButtons = new Button[rowCount];
            recipeLabels = new TMP_Text[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                recipeButtons[i] = CreateButton($"Recipe Button {i + 1}", panelObject.transform);
                recipeLabels[i] = CreateText($"Recipe Label {i + 1}", recipeButtons[i].transform);
            }

            Button previousButton = CreateButton("Previous Recipes", panelObject.transform);
            nextButton = CreateButton("Next Recipes", panelObject.transform);
            pageLabel = CreateText("Recipe Page", panelObject.transform);

            panel.Configure(recipeButtons, recipeLabels, CreateText("Status", panelObject.transform));
            panel.ConfigurePaging(previousButton, nextButton, pageLabel);
            return panel;
        }

        Button CreateButton(string name, Transform parent)
        {
            GameObject target = CreateObject(name);
            target.transform.SetParent(parent, false);
            return target.AddComponent<Button>();
        }

        TMP_Text CreateText(string name, Transform parent)
        {
            GameObject target = CreateObject(name);
            target.transform.SetParent(parent, false);
            return target.AddComponent<TextMeshProUGUI>();
        }

        GameObject CreateObject(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }
    }
}
