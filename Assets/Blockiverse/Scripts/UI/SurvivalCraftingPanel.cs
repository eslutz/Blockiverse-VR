using System;
using System.Collections.Generic;
using Blockiverse.Survival;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalCraftingPanel : MonoBehaviour
    {
        [SerializeField] Text[] recipeLabels;
        [SerializeField] Text statusLabel;

        CraftingRecipeBook recipeBook;
        Inventory inventory;
        ItemRegistry itemRegistry;
        CraftingStation availableStation;

        public void Configure(Text[] targetRecipeLabels, Text targetStatusLabel)
        {
            recipeLabels = targetRecipeLabels ?? Array.Empty<Text>();
            statusLabel = targetStatusLabel;
            Refresh();
        }

        public void Bind(
            CraftingRecipeBook targetRecipeBook,
            Inventory targetInventory,
            ItemRegistry registry = null,
            CraftingStation station = CraftingStation.None)
        {
            recipeBook = targetRecipeBook ?? throw new ArgumentNullException(nameof(targetRecipeBook));
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? ItemRegistry.CreateDefault();
            availableStation = station;
            SetStatus("Ready");
            Refresh();
        }

        public CraftingResult TryCraftByOutput(ItemId outputItemId)
        {
            EnsureBound();

            if (!recipeBook.TryGetByOutput(outputItemId, out CraftingRecipe recipe))
            {
                SetStatus("Recipe unavailable");
                return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, outputItemId);
            }

            CraftingResult result = CraftingService.TryCraft(inventory, recipe, availableStation);
            SetStatus(result.Succeeded
                ? $"Crafted {FormatStack(recipe.Output)}"
                : $"Cannot craft {itemRegistry.Get(outputItemId).Name}: {result.FailureReason}");
            Refresh();
            return result;
        }

        public void Refresh()
        {
            if (recipeLabels == null)
                return;

            List<CraftingRecipe> recipes = GetSortedRecipes();
            for (int i = 0; i < recipeLabels.Length; i++)
            {
                if (recipeLabels[i] == null)
                    continue;

                recipeLabels[i].text = i < recipes.Count ? FormatRecipe(recipes[i]) : string.Empty;
            }
        }

        List<CraftingRecipe> GetSortedRecipes()
        {
            var recipes = new List<CraftingRecipe>();
            if (recipeBook == null)
                return recipes;

            recipes.AddRange(recipeBook.All);
            recipes.Sort((left, right) => ((int)left.Output.ItemId).CompareTo((int)right.Output.ItemId));
            return recipes;
        }

        string FormatRecipe(CraftingRecipe recipe)
        {
            return $"{FormatStack(recipe.Output)} - {FormatIngredients(recipe)}";
        }

        string FormatIngredients(CraftingRecipe recipe)
        {
            var parts = new string[recipe.Ingredients.Count];
            for (int i = 0; i < recipe.Ingredients.Count; i++)
                parts[i] = FormatStack(recipe.Ingredients[i]);

            return string.Join(", ", parts);
        }

        string FormatStack(ItemStack stack)
        {
            ItemDefinition definition = itemRegistry.Get(stack.ItemId);
            return $"{definition.Name} x{stack.Count}";
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        void EnsureBound()
        {
            if (recipeBook == null || inventory == null)
                throw new InvalidOperationException("Survival crafting panel has not been bound.");
        }
    }
}
