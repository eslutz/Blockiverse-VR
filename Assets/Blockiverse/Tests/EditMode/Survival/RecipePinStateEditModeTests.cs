using System.Collections.Generic;
using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    /// <summary>
    /// The pinned-recipe slot (voxel_survival_menus §6.10a). The rules under test are the ones the
    /// ruleset previously left contradictory: one slot rather than a list, pin-vs-toggle, and
    /// auto-unpin firing only for the pinned recipe.
    /// </summary>
    public sealed class RecipePinStateEditModeTests
    {
        static CraftingRecipeBook Book => CraftingRecipeBook.Default;

        static RecipePinState NewState() => new(Book);

        // Two distinct recipes that actually exist in the canonical book, so these tests exercise
        // real ingredient data rather than a hand-built fixture that could drift from it.
        static (ItemId first, ItemId second) TwoRecipeOutputs()
        {
            var outputs = new List<ItemId>();
            foreach (CraftingRecipe recipe in Book.All)
            {
                if (!outputs.Contains(recipe.Output.ItemId))
                    outputs.Add(recipe.Output.ItemId);

                if (outputs.Count == 2)
                    break;
            }

            Assert.That(outputs.Count, Is.EqualTo(2),
                "The canonical recipe book must supply at least two distinct outputs for this fixture.");
            return (outputs[0], outputs[1]);
        }

        [Test]
        public void StartsUnpinned()
        {
            RecipePinState state = NewState();

            Assert.That(state.HasPin, Is.False);
            Assert.That(state.PinnedOutputId.IsNone, Is.True);
        }

        [Test]
        public void PinningASecondRecipeReplacesTheFirst()
        {
            (ItemId first, ItemId second) = TwoRecipeOutputs();
            RecipePinState state = NewState();

            state.Pin(first, RecipePinSource.Crafting);
            state.Pin(second, RecipePinSource.Campfire);

            // The whole point of a single slot: no list, no cap, no eviction rule.
            Assert.That(state.PinnedOutputId, Is.EqualTo(second),
                "Pinning must replace the previous pin — there is exactly one slot.");
            Assert.That(state.Source, Is.EqualTo(RecipePinSource.Campfire),
                "The source must follow the pin, so the HUD can label a station requirement.");
        }

        [Test]
        public void ToggleClearsTheSlotWhenTheSameRecipeIsAlreadyPinned()
        {
            (ItemId first, _) = TwoRecipeOutputs();
            RecipePinState state = NewState();

            Assert.That(state.Toggle(first, RecipePinSource.Crafting), Is.True, "First toggle pins.");
            Assert.That(state.HasPin, Is.True);

            Assert.That(state.Toggle(first, RecipePinSource.Crafting), Is.False, "Second toggle clears.");
            Assert.That(state.HasPin, Is.False);
        }

        [Test]
        public void ToggleReplacesRatherThanClearsWhenADifferentRecipeIsPinned()
        {
            (ItemId first, ItemId second) = TwoRecipeOutputs();
            RecipePinState state = NewState();

            state.Toggle(first, RecipePinSource.Crafting);
            bool pinned = state.Toggle(second, RecipePinSource.Crafting);

            Assert.That(pinned, Is.True);
            Assert.That(state.PinnedOutputId, Is.EqualTo(second),
                "Toggling a DIFFERENT recipe pins it; it must not clear the slot.");
        }

        [Test]
        public void CraftingThePinnedRecipeAutoUnpins()
        {
            (ItemId first, _) = TwoRecipeOutputs();
            RecipePinState state = NewState();
            state.Pin(first, RecipePinSource.Crafting);

            state.OnRecipeCrafted(first);

            Assert.That(state.HasPin, Is.False,
                "Crafting the pinned recipe is the tracked goal completing, so the pin clears itself.");
        }

        [Test]
        public void CraftingSomethingElseLeavesThePinAlone()
        {
            (ItemId first, ItemId second) = TwoRecipeOutputs();
            RecipePinState state = NewState();
            state.Pin(first, RecipePinSource.Crafting);

            state.OnRecipeCrafted(second);

            // Without this check, crafting intermediate parts would silently drop the pin the
            // player set on the final item — the exact workflow the feature exists to support.
            Assert.That(state.HasPin, Is.True);
            Assert.That(state.PinnedOutputId, Is.EqualTo(first));
        }

        [Test]
        public void RequirementsReportHaveAndNeededAgainstTheInventory()
        {
            RecipePinState state = NewState();
            var lines = new List<RecipePinRequirement>();

            // Pick a recipe with at least one ingredient, or the assertions below are vacuous.
            CraftingRecipe target = null;
            foreach (CraftingRecipe recipe in Book.All)
            {
                if (recipe.AggregatedIngredients.Count > 0)
                {
                    target = recipe;
                    break;
                }
            }

            Assert.That(target, Is.Not.Null, "The canonical book must contain a recipe with ingredients.");

            state.Pin(target.Output.ItemId, RecipePinSource.Crafting);

            var empty = new Inventory();
            int count = state.GetRequirements(empty, lines);

            Assert.That(count, Is.EqualTo(target.AggregatedIngredients.Count));
            foreach (RecipePinRequirement line in lines)
            {
                Assert.That(line.Needed, Is.GreaterThan(0));
                Assert.That(line.Have, Is.EqualTo(0), "An empty inventory has none of anything.");
                Assert.That(line.IsSatisfied, Is.False);
            }

            // Now satisfy the first ingredient and confirm the line flips, so the test measures the
            // inventory read rather than just the recipe data.
            ItemStack firstIngredient = target.AggregatedIngredients[0];
            var stocked = new Inventory();
            Assert.That(stocked.TryAddAll(new ItemStack(firstIngredient.ItemId, firstIngredient.Count)), Is.True,
                "Fixture must be able to stock the ingredient.");

            state.GetRequirements(stocked, lines);

            RecipePinRequirement satisfied = lines.Find(l => l.ItemId.Equals(firstIngredient.ItemId));
            Assert.That(satisfied.Have, Is.GreaterThanOrEqualTo(satisfied.Needed));
            Assert.That(satisfied.IsSatisfied, Is.True);
        }

        [Test]
        public void ChangeEventFiresOnPinReplaceAndClearButNotOnANoOpRepin()
        {
            (ItemId first, ItemId second) = TwoRecipeOutputs();
            RecipePinState state = NewState();

            int fired = 0;
            state.PinnedRecipeChanged += () => fired++;

            state.Pin(first, RecipePinSource.Crafting);
            Assert.That(fired, Is.EqualTo(1), "Pinning must notify.");

            // Re-pinning the same recipe from the same source changes nothing observable, so a
            // consumer that rebuilds its HUD on every event should not be woken for it.
            state.Pin(first, RecipePinSource.Crafting);
            Assert.That(fired, Is.EqualTo(1), "A no-op re-pin must not notify.");

            // Same recipe, different source IS observable: the HUD labels station-bound recipes.
            state.Pin(first, RecipePinSource.Campfire);
            Assert.That(fired, Is.EqualTo(2), "A source change must notify — the HUD renders it.");

            state.Pin(second, RecipePinSource.Crafting);
            Assert.That(fired, Is.EqualTo(3), "Replacing the pin must notify.");

            state.Clear();
            Assert.That(fired, Is.EqualTo(4), "Clearing must notify.");

            state.Clear();
            Assert.That(fired, Is.EqualTo(4), "Clearing an empty slot must not notify.");
        }

        [Test]
        public void AutoUnpinNotifiesThroughTheChangeEvent()
        {
            (ItemId first, _) = TwoRecipeOutputs();
            RecipePinState state = NewState();
            state.Pin(first, RecipePinSource.Crafting);

            int fired = 0;
            state.PinnedRecipeChanged += () => fired++;

            state.OnRecipeCrafted(first);

            Assert.That(state.HasPin, Is.False);
            Assert.That(fired, Is.EqualTo(1),
                "Auto-unpin must reach subscribers, or the HUD keeps rendering a recipe that is no longer pinned.");
        }

        [Test]
        public void RequirementsAreEmptyWhenNothingIsPinned()
        {
            RecipePinState state = NewState();
            var lines = new List<RecipePinRequirement> { new(ItemId.None, 1, 1) };

            int count = state.GetRequirements(new Inventory(), lines);

            Assert.That(count, Is.EqualTo(0));
            Assert.That(lines, Is.Empty, "The caller's list must be cleared, not appended to.");
        }
    }
}
