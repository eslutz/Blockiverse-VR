using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    public enum CraftingStation
    {
        None,
        Workbench
    }

    public sealed class CraftingRecipe
    {
        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, params ItemStack[] ingredients)
        {
            if (output.IsEmpty)
                throw new ArgumentException("Crafting recipes must produce an output item.", nameof(output));

            if (ingredients == null)
                throw new ArgumentNullException(nameof(ingredients));

            if (ingredients.Length == 0)
                throw new ArgumentException("Crafting recipes must require at least one ingredient.", nameof(ingredients));

            var ingredientCopy = new ItemStack[ingredients.Length];
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (ingredients[i].IsEmpty)
                    throw new ArgumentException("Crafting recipe ingredients cannot be empty.", nameof(ingredients));

                ingredientCopy[i] = ingredients[i];
            }

            Output = output;
            RequiredStation = requiredStation;
            Ingredients = Array.AsReadOnly(ingredientCopy);
        }

        public ItemStack Output { get; }
        public CraftingStation RequiredStation { get; }
        public IReadOnlyList<ItemStack> Ingredients { get; }
    }
}
