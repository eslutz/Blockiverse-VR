using System;

namespace Blockiverse.Survival
{
    public enum CraftingFailureReason
    {
        None,
        MissingStation,
        MissingIngredient,
        OutputBlocked
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

        public static CraftingResult Failure(CraftingFailureReason reason, ItemId failedItemId = ItemId.None)
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

            if (recipe.RequiredStation != CraftingStation.None && recipe.RequiredStation != availableStation)
                return CraftingResult.Failure(CraftingFailureReason.MissingStation);

            foreach (ItemStack ingredient in recipe.Ingredients)
            {
                if (inventory.CountOf(ingredient.ItemId) < ingredient.Count)
                    return CraftingResult.Failure(CraftingFailureReason.MissingIngredient, ingredient.ItemId);
            }

            ItemStack[] snapshot = CaptureSnapshot(inventory);
            foreach (ItemStack ingredient in recipe.Ingredients)
                inventory.Remove(ingredient.ItemId, ingredient.Count);

            if (!inventory.TryAddAll(recipe.Output))
            {
                RestoreSnapshot(inventory, snapshot);
                return CraftingResult.Failure(CraftingFailureReason.OutputBlocked, recipe.Output.ItemId);
            }

            return CraftingResult.Success();
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
