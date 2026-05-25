using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    public sealed class CraftingRecipeBook
    {
        readonly ItemRegistry itemRegistry;
        readonly Dictionary<ItemId, CraftingRecipe> recipesByOutput = new();

        public CraftingRecipeBook(ItemRegistry itemRegistry = null)
        {
            this.itemRegistry = itemRegistry ?? ItemRegistry.CreateDefault();
        }

        public IReadOnlyCollection<CraftingRecipe> All => recipesByOutput.Values;

        public static CraftingRecipeBook CreateDefault(ItemRegistry itemRegistry = null)
        {
            itemRegistry ??= ItemRegistry.CreateDefault();
            var recipeBook = new CraftingRecipeBook(itemRegistry);

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Workbench, 1),
                CraftingStation.None,
                new ItemStack(ItemId.Timber, 4)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Torchbud, 4),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 1),
                new ItemStack(ItemId.Coalstone, 1)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.StorageCrate, 1),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 8)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Chipper, 1),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 3)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Mallet, 1),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 2),
                new ItemStack(ItemId.Slate, 2)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Pick, 1),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 2),
                new ItemStack(ItemId.Copperstone, 3)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.RecoveryWrap, 2),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Leafmass, 3),
                new ItemStack(ItemId.Timber, 1)));

            return recipeBook;
        }

        public void Register(CraftingRecipe recipe)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            itemRegistry.Get(recipe.Output.ItemId);
            foreach (ItemStack ingredient in recipe.Ingredients)
                itemRegistry.Get(ingredient.ItemId);

            if (recipesByOutput.ContainsKey(recipe.Output.ItemId))
                throw new InvalidOperationException($"A recipe is already registered for output item: {recipe.Output.ItemId}");

            recipesByOutput.Add(recipe.Output.ItemId, recipe);
        }

        public CraftingRecipe GetByOutput(ItemId outputItemId)
        {
            if (!recipesByOutput.TryGetValue(outputItemId, out CraftingRecipe recipe))
                throw new KeyNotFoundException($"No crafting recipe is registered for output item: {outputItemId}");

            return recipe;
        }

        public bool TryGetByOutput(ItemId outputItemId, out CraftingRecipe recipe)
        {
            return recipesByOutput.TryGetValue(outputItemId, out recipe);
        }
    }
}
