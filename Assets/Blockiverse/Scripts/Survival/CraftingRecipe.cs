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
        static readonly IReadOnlyList<ItemStack> NoByproducts = Array.AsReadOnly(Array.Empty<ItemStack>());

        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, params ItemStack[] ingredients)
            : this(output, requiredStation, 0, ingredients) { }

        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, int timeTicks, ItemStack[] ingredients)
            : this(output, requiredStation, timeTicks, ingredients, null) { }

        public CraftingRecipe(ItemStack output, CraftingStation requiredStation, int timeTicks, ItemStack[] ingredients, ItemStack[] byproducts)
        {
            if (output.IsEmpty)
                throw new ArgumentException("Crafting recipes must produce an output item.", nameof(output));

            if (ingredients == null)
                throw new ArgumentNullException(nameof(ingredients));

            if (ingredients.Length == 0)
                throw new ArgumentException("Crafting recipes must require at least one ingredient.", nameof(ingredients));

            // The sign of TimeTicks is load-bearing: CraftingService treats <= 0 as an instant
            // craft while smelting stations only accept > 0, so a negative value would silently
            // turn a timed recipe into a fuel-free instant craft. Fail fast at the data layer.
            if (timeTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(timeTicks), "TimeTicks must be non-negative.");

            var ingredientCopy = new ItemStack[ingredients.Length];
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (ingredients[i].IsEmpty)
                    throw new ArgumentException("Crafting recipe ingredients cannot be empty.", nameof(ingredients));

                ingredientCopy[i] = ingredients[i];
            }

            if (byproducts != null && byproducts.Length > 0)
            {
                // Timed recipes run on the fueled SmeltingStationModel, which grants only the
                // primary output — a byproduct there would be silently dropped (§731 container
                // returns are instant crafts only).
                if (timeTicks > 0)
                    throw new ArgumentException("Timed recipes cannot declare byproducts; the station model grants only the primary output.", nameof(byproducts));

                var byproductCopy = new ItemStack[byproducts.Length];
                for (int i = 0; i < byproducts.Length; i++)
                {
                    if (byproducts[i].IsEmpty)
                        throw new ArgumentException("Crafting recipe byproducts cannot be empty.", nameof(byproducts));

                    byproductCopy[i] = byproducts[i];
                }

                Byproducts = Array.AsReadOnly(byproductCopy);
            }
            else
            {
                Byproducts = NoByproducts;
            }

            Output = output;
            RequiredStation = requiredStation;
            TimeTicks = timeTicks;
            Ingredients = Array.AsReadOnly(ingredientCopy);
            AggregatedIngredients = Aggregate(ingredientCopy) ?? Ingredients;
        }

        public ItemStack Output { get; }
        public CraftingStation RequiredStation { get; }
        public int TimeTicks { get; }
        public IReadOnlyList<ItemStack> Ingredients { get; }

        // Ingredients merged into one stack per item id (first-seen order), built once here so
        // per-tick recipe checks (e.g. smelting stations) can iterate without allocating.
        public IReadOnlyList<ItemStack> AggregatedIngredients { get; }

        // Extra stacks granted alongside the output (e.g. the empty bucket returned when a
        // recipe consumes a filled one, §731). Empty for almost every recipe.
        public IReadOnlyList<ItemStack> Byproducts { get; }

        // Returns the merged ingredient list, or null when there are no duplicate item ids and
        // the original list can be reused as-is.
        static IReadOnlyList<ItemStack> Aggregate(ItemStack[] ingredients)
        {
            var aggregate = new List<ItemStack>(ingredients.Length);
            foreach (ItemStack ingredient in ingredients)
            {
                int existing = -1;
                for (int i = 0; i < aggregate.Count; i++)
                {
                    if (aggregate[i].ItemId == ingredient.ItemId)
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                    aggregate[existing] = new ItemStack(ingredient.ItemId, aggregate[existing].Count + ingredient.Count);
                else
                    aggregate.Add(ingredient);
            }

            return aggregate.Count == ingredients.Length ? null : aggregate.AsReadOnly();
        }
    }
}
