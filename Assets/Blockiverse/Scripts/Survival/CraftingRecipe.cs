using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    public enum CraftingStation
    {
        None,
        BuildTable,
        ClayKiln,
        BellowsForge,
        PrepBoard,
        MendBench,
        Campfire
    }

    public static class CraftingStationNames
    {
        // Human-readable station name for UI ("ClayKiln" → "Clay Kiln"); matches the placed
        // station blocks' display names in BlockRegistry.
        public static string DisplayName(CraftingStation station)
        {
            string name = station.ToString();
            var builder = new System.Text.StringBuilder(name.Length + 2);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    builder.Append(' ');
                builder.Append(name[i]);
            }

            return builder.ToString();
        }
    }

    public sealed class CraftingRecipe
    {
        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, params ItemStack[] ingredients)
            : this(output, requiredStation, 0, ingredients) { }

        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, int timeTicks, ItemStack[] ingredients)
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
            TimeTicks = timeTicks;
            Ingredients = Array.AsReadOnly(ingredientCopy);
        }

        public ItemStack Output { get; }
        public CraftingStation RequiredStation { get; }
        public int TimeTicks { get; }
        public IReadOnlyList<ItemStack> Ingredients { get; }
    }
}
