using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    public enum CraftingFailureReason
    {
        None,
        MissingStation,
        MissingIngredient,
        OutputBlocked,
        // Timed (kiln/forge) recipes run on the fueled SmeltingStationModel, never as an
        // instant craft — rejecting here closes the fuel/time bypass for every caller.
        TimedRecipeRequiresStation
    }

    public readonly struct CraftingResult
    {
        CraftingResult(bool succeeded, CraftingFailureReason failureReason, ItemId failedItemId)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            FailedItemId = failedItemId;
        }

        public bool Succeeded { get; }
        public CraftingFailureReason FailureReason { get; }
        public ItemId FailedItemId { get; }

        public static CraftingResult Success()
        {
            return new CraftingResult(true, CraftingFailureReason.None, ItemId.None);
        }

        public static CraftingResult Failure(CraftingFailureReason reason, ItemId failedItemId = default)
        {
            if (reason == CraftingFailureReason.None)
                throw new ArgumentException("Failure results must include a concrete reason.", nameof(reason));

            return new CraftingResult(false, reason, failedItemId);
        }
    }

    public static class CraftingService
    {
        public static CraftingResult TryCraft(Inventory inventory, CraftingRecipe recipe, CraftingStation availableStation)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            // Timed recipes consume fuel and progress over ticks at their station model; crafting
            // them instantly here would bypass both (§8.2/§9.3/§9.4).
            if (recipe.TimeTicks > 0)
                return CraftingResult.Failure(CraftingFailureReason.TimedRecipeRequiresStation, recipe.Output.ItemId);

            if (recipe.RequiredStation != CraftingStation.None && recipe.RequiredStation != availableStation)
                return CraftingResult.Failure(CraftingFailureReason.MissingStation);

            ItemStack[] requiredIngredients = AggregateIngredients(recipe.Ingredients);
            foreach (ItemStack ingredient in requiredIngredients)
            {
                if (inventory.CountOf(ingredient.ItemId) < ingredient.Count)
                    return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, ingredient.ItemId);
            }

            ItemStack[] snapshot = CaptureSnapshot(inventory);
            foreach (ItemStack ingredient in requiredIngredients)
            {
                if (!inventory.Remove(ingredient.ItemId, ingredient.Count))
                {
                    RestoreSnapshot(inventory, snapshot);
                    return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, ingredient.ItemId);
                }
            }

            if (!inventory.TryAddAll(recipe.Output))
            {
                RestoreSnapshot(inventory, snapshot);
                return CraftingResult.Failure(CraftingFailureReason.OutputBlocked, recipe.Output.ItemId);
            }

            // Byproducts (e.g. the empty bucket back from a consumed filled one, §731) are part
            // of the same all-or-nothing transaction: if one cannot fit, the craft never happened.
            foreach (ItemStack byproduct in recipe.Byproducts)
            {
                if (!inventory.TryAddAll(byproduct))
                {
                    RestoreSnapshot(inventory, snapshot);
                    return CraftingResult.Failure(CraftingFailureReason.OutputBlocked, byproduct.ItemId);
                }
            }

            return CraftingResult.Success();
        }

        static ItemStack[] AggregateIngredients(IReadOnlyList<ItemStack> ingredients)
        {
            var aggregate = new List<ItemStack>(ingredients.Count);
            var indexes = new Dictionary<ItemId, int>();

            foreach (ItemStack ingredient in ingredients)
            {
                if (indexes.TryGetValue(ingredient.ItemId, out int index))
                {
                    ItemStack existing = aggregate[index];
                    aggregate[index] = new ItemStack(existing.ItemId, existing.Count + ingredient.Count);
                    continue;
                }

                indexes.Add(ingredient.ItemId, aggregate.Count);
                aggregate.Add(ingredient);
            }

            return aggregate.ToArray();
        }

        static ItemStack[] CaptureSnapshot(Inventory inventory)
        {
            var snapshot = new ItemStack[inventory.SlotCount];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = inventory.GetSlot(i);

            return snapshot;
        }

        static void RestoreSnapshot(Inventory inventory, ItemStack[] snapshot)
        {
            for (int i = 0; i < snapshot.Length; i++)
                inventory.SetSlot(i, snapshot[i]);
        }
    }
}
