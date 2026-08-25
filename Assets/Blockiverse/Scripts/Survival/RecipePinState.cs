using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    // Which screen pinned the recipe. The HUD needs this to label a station requirement: a recipe
    // pinned at a campfire cannot be made by hand, and an overlay that only listed materials would
    // imply it could.
    public enum RecipePinSource
    {
        Crafting,
        Campfire,
        PrepBoard,
    }

    // One ingredient line of the pinned recipe, as the HUD shows it.
    public readonly struct RecipePinRequirement
    {
        public RecipePinRequirement(ItemId itemId, int have, int needed)
        {
            ItemId = itemId;
            Have = have;
            Needed = needed;
        }

        public readonly ItemId ItemId;
        public readonly int Have;
        public readonly int Needed;

        public bool IsSatisfied => Have >= Needed;
    }

    /// <summary>
    /// The single pinned recipe (voxel_survival_menus §6.10a).
    ///
    /// ONE slot, not a list: pinning replaces whatever was pinned, so there is no cap, ordering or
    /// eviction rule. Four entry points (the Crafting Menu toggle, campfire, prep board, and the
    /// HUD refresh) all write this one slot, last-write-wins.
    ///
    /// Deliberately client-local and session-only. A pin changes no world state, so routing it
    /// through the host-authoritative survival command channel would buy nothing and would put a
    /// HUD convenience on the wire; and keeping it out of the save avoids touching the v4 player
    /// save. Nothing here persists — the owner clears it on world unload.
    ///
    /// Lives in Survival rather than UI because it is pure logic over recipes and inventory with no
    /// UnityEngine dependency, which keeps it testable with plain NUnit and independent of which UI
    /// framework renders it.
    /// </summary>
    public sealed class RecipePinState
    {
        readonly CraftingRecipeBook recipeBook;

        public RecipePinState(CraftingRecipeBook book)
        {
            recipeBook = book ?? throw new ArgumentNullException(nameof(book));
        }

        /// <summary>Raised whenever the pinned recipe or its source changes, including when the
        /// slot is cleared. Without this a HUD would have to poll PinnedOutputId and diff it every
        /// frame; the survival sync's LocalInventoryChanged / StationRemoved events are the
        /// precedent this follows.
        ///
        /// Requirement COUNTS deliberately do not raise this — they change with the inventory, and
        /// consumers already refresh on LocalInventoryChanged. Firing here as well would just
        /// double the refreshes.</summary>
        public event Action PinnedRecipeChanged;

        /// <summary>Output item of the pinned recipe. Recipes are identified by output item id
        /// throughout this codebase (see TrySubmitCraft and CraftingRecipeBook.GetByOutput), so the
        /// pin uses the same identity rather than inventing a parallel one.</summary>
        public ItemId PinnedOutputId { get; private set; } = ItemId.None;

        public RecipePinSource Source { get; private set; }

        public bool HasPin => !PinnedOutputId.IsNone;

        /// <summary>Pin this recipe, replacing any previous pin. Pin-only; used by the station
        /// screens, which have no "currently pinned" affordance to toggle against.</summary>
        public void Pin(ItemId outputItemId, RecipePinSource source)
        {
            if (outputItemId.IsNone)
            {
                Clear();
                return;
            }

            if (outputItemId.Equals(PinnedOutputId) && source == Source)
                return;

            PinnedOutputId = outputItemId;
            Source = source;
            PinnedRecipeChanged?.Invoke();
        }

        /// <summary>Crafting Menu behaviour: pins the recipe, or clears the slot if this recipe is
        /// already the pinned one. Returns true when a recipe ends up pinned.</summary>
        public bool Toggle(ItemId outputItemId, RecipePinSource source)
        {
            if (!outputItemId.IsNone && outputItemId.Equals(PinnedOutputId))
            {
                Clear();
                return false;
            }

            Pin(outputItemId, source);
            return HasPin;
        }

        public void Clear()
        {
            if (!HasPin)
                return;

            PinnedOutputId = ItemId.None;
            Source = RecipePinSource.Crafting;
            PinnedRecipeChanged?.Invoke();
        }

        /// <summary>Auto-unpin (§6.10a): crafting the pinned recipe is the tracked goal completing,
        /// so the pin clears itself. Crafting anything else leaves it alone — without that check a
        /// player crafting intermediate parts would lose the pin they set on the final item.</summary>
        public void OnRecipeCrafted(ItemId craftedOutputItemId)
        {
            if (HasPin && craftedOutputItemId.Equals(PinnedOutputId))
                Clear();
        }

        /// <summary>Fills <paramref name="into"/> with the pinned recipe's ingredients as
        /// have/needed. Returns the number of lines written, or 0 when nothing is pinned or the
        /// pinned output has no recipe.
        ///
        /// Reads AggregatedIngredients so a recipe listing the same item twice produces ONE line
        /// with the summed count, matching what the crafting screens already show. The caller
        /// supplies the list so a per-frame HUD refresh allocates nothing.</summary>
        public int GetRequirements(Inventory inventory, List<RecipePinRequirement> into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (!HasPin || inventory == null)
                return 0;

            if (!recipeBook.TryGetByOutput(PinnedOutputId, out CraftingRecipe recipe))
                return 0;

            IReadOnlyList<ItemStack> ingredients = recipe.AggregatedIngredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                ItemStack ingredient = ingredients[i];
                into.Add(new RecipePinRequirement(
                    ingredient.ItemId,
                    inventory.CountOf(ingredient.ItemId),
                    ingredient.Count));
            }

            return into.Count;
        }

        /// <summary>The station the pinned recipe needs, or None. The HUD labels this so a recipe
        /// pinned at a station does not read as hand-craftable.</summary>
        public CraftingStation RequiredStation =>
            HasPin && recipeBook.TryGetByOutput(PinnedOutputId, out CraftingRecipe recipe)
                ? recipe.RequiredStation
                : CraftingStation.None;
    }
}
