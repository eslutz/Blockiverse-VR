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
                new ItemStack(ItemId.BuildTable, 1),
                CraftingStation.None,
                new ItemStack(ItemId.BranchwoodLog, 4)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.Glowwick, 4),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 1),
                new ItemStack(ItemId.Embercoal, 1)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.StorageCrate, 1),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 8)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.ReedwoodFeller, 1),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 3)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.ReedwoodMallet, 1),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 2),
                new ItemStack(ItemId.Graystone, 2)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.ReedwoodDelver, 1),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 2),
                new ItemStack(ItemId.RawRosycopper, 3)));

            recipeBook.Register(new CraftingRecipe(
                new ItemStack(ItemId.FieldBandage, 2),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.Leafmoss, 3),
                new ItemStack(ItemId.BranchwoodLog, 1)));

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
