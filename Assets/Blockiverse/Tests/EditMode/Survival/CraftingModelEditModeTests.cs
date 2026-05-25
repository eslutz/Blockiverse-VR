using System.Linq;
using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class CraftingModelEditModeTests
    {
        [Test]
        public void DefaultRecipeBookContainsCoreCraftingRecipes()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());

            Assert.That(recipeBook.All.Count, Is.EqualTo(7));
            AssertRecipe(recipeBook, ItemId.Workbench, CraftingStation.None, new ItemStack(ItemId.Workbench, 1), new ItemStack(ItemId.Timber, 4));
            AssertRecipe(recipeBook, ItemId.Torchbud, CraftingStation.Workbench, new ItemStack(ItemId.Torchbud, 4), new ItemStack(ItemId.Timber, 1), new ItemStack(ItemId.Coalstone, 1));
            AssertRecipe(recipeBook, ItemId.StorageCrate, CraftingStation.Workbench, new ItemStack(ItemId.StorageCrate, 1), new ItemStack(ItemId.Timber, 8));
            AssertRecipe(recipeBook, ItemId.Chipper, CraftingStation.Workbench, new ItemStack(ItemId.Chipper, 1), new ItemStack(ItemId.Timber, 3));
            AssertRecipe(recipeBook, ItemId.Mallet, CraftingStation.Workbench, new ItemStack(ItemId.Mallet, 1), new ItemStack(ItemId.Timber, 2), new ItemStack(ItemId.Slate, 2));
            AssertRecipe(recipeBook, ItemId.Pick, CraftingStation.Workbench, new ItemStack(ItemId.Pick, 1), new ItemStack(ItemId.Timber, 2), new ItemStack(ItemId.Copperstone, 3));
            AssertRecipe(recipeBook, ItemId.RecoveryWrap, CraftingStation.Workbench, new ItemStack(ItemId.RecoveryWrap, 2), new ItemStack(ItemId.Leafmass, 3), new ItemStack(ItemId.Timber, 1));
        }

        [Test]
        public void CraftingValidationRequiresWorkbenchBeforeConsumingInputs()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.Coalstone, 1));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.Torchbud);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingStation));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Timber, 1)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Coalstone, 1)));
            Assert.That(inventory.CountOf(ItemId.Torchbud), Is.Zero);
        }

        [Test]
        public void CraftingConsumesIngredientsAndAddsOutputWhenRequirementsAreMet()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 2));
            inventory.SetSlot(1, new ItemStack(ItemId.Copperstone, 3));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.Pick);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.Workbench);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.None));
            Assert.That(inventory.CountOf(ItemId.Timber), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.Copperstone), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.Pick), Is.EqualTo(1));
        }

        [Test]
        public void CraftingRejectsMissingIngredientsWithoutChangingInventory()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.Slate, 1));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.Mallet);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.Workbench);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingIngredient));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.Timber));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Timber, 1)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Slate, 1)));
            Assert.That(inventory.CountOf(ItemId.Mallet), Is.Zero);
        }

        [Test]
        public void CraftingAggregatesDuplicateIngredientsBeforeConsumingInventory()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.Timber, 2));
            CraftingRecipe recipe = new(
                new ItemStack(ItemId.Torchbud, 1),
                CraftingStation.Workbench,
                new ItemStack(ItemId.Timber, 2),
                new ItemStack(ItemId.Timber, 2));

            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.Workbench);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingIngredient));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.Timber));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.Timber, 2)));
            Assert.That(inventory.CountOf(ItemId.Torchbud), Is.Zero);
        }

        static void AssertRecipe(CraftingRecipeBook recipeBook, ItemId outputItemId, CraftingStation requiredStation, ItemStack output, params ItemStack[] ingredients)
        {
            CraftingRecipe recipe = recipeBook.GetByOutput(outputItemId);

            Assert.That(recipe.RequiredStation, Is.EqualTo(requiredStation));
            Assert.That(recipe.Output, Is.EqualTo(output));
            CollectionAssert.AreEquivalent(ingredients, recipe.Ingredients.ToArray());
        }
    }
}
