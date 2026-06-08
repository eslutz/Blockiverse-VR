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

            Assert.That(recipeBook.All.Count, Is.EqualTo(12));
            AssertRecipe(recipeBook, ItemId.BuildTable, CraftingStation.None, new ItemStack(ItemId.BuildTable, 1), new ItemStack(ItemId.BranchwoodLog, 4));
            AssertRecipe(recipeBook, ItemId.Glowwick, CraftingStation.BuildTable, new ItemStack(ItemId.Glowwick, 4), new ItemStack(ItemId.BranchwoodLog, 1), new ItemStack(ItemId.Embercoal, 1));
            AssertRecipe(recipeBook, ItemId.StorageCrate, CraftingStation.BuildTable, new ItemStack(ItemId.StorageCrate, 1), new ItemStack(ItemId.BranchwoodLog, 8));
            AssertRecipe(recipeBook, ItemId.ReedwoodFeller, CraftingStation.BuildTable, new ItemStack(ItemId.ReedwoodFeller, 1), new ItemStack(ItemId.BranchwoodLog, 3));
            AssertRecipe(recipeBook, ItemId.ReedwoodMallet, CraftingStation.BuildTable, new ItemStack(ItemId.ReedwoodMallet, 1), new ItemStack(ItemId.BranchwoodLog, 2), new ItemStack(ItemId.Graystone, 2));
            AssertRecipe(recipeBook, ItemId.ReedwoodDelver, CraftingStation.BuildTable, new ItemStack(ItemId.ReedwoodDelver, 1), new ItemStack(ItemId.BranchwoodLog, 2), new ItemStack(ItemId.RawRosycopper, 3));
            AssertRecipe(recipeBook, ItemId.FieldBandage, CraftingStation.BuildTable, new ItemStack(ItemId.FieldBandage, 2), new ItemStack(ItemId.Leafmoss, 3), new ItemStack(ItemId.BranchwoodLog, 1));
        }

        [Test]
        public void CraftingValidationRequiresBuildTableBeforeConsumingInputs()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.Embercoal, 1));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.Glowwick);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingStation));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Embercoal, 1)));
            Assert.That(inventory.CountOf(ItemId.Glowwick), Is.Zero);
        }

        [Test]
        public void CraftingConsumesIngredientsAndAddsOutputWhenRequirementsAreMet()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 2));
            inventory.SetSlot(1, new ItemStack(ItemId.RawRosycopper, 3));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.ReedwoodDelver);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.BuildTable);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.None));
            Assert.That(inventory.CountOf(ItemId.BranchwoodLog), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.RawRosycopper), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.ReedwoodDelver), Is.EqualTo(1));
        }

        [Test]
        public void CraftingRejectsMissingIngredientsWithoutChangingInventory()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.Graystone, 1));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.ReedwoodMallet);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.BuildTable);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingIngredient));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.BranchwoodLog));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.Graystone, 1)));
            Assert.That(inventory.CountOf(ItemId.ReedwoodMallet), Is.Zero);
        }

        [Test]
        public void CraftingAggregatesDuplicateIngredientsBeforeConsumingInventory()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 2));
            CraftingRecipe recipe = new(
                new ItemStack(ItemId.Glowwick, 1),
                CraftingStation.BuildTable,
                new ItemStack(ItemId.BranchwoodLog, 2),
                new ItemStack(ItemId.BranchwoodLog, 2));

            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.BuildTable);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingIngredient));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.BranchwoodLog));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 2)));
            Assert.That(inventory.CountOf(ItemId.Glowwick), Is.Zero);
        }

        [Test]
        public void CampfireRecipeIsInstant()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());
            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.CutstoneBlock);

            Assert.That(recipe.RequiredStation, Is.EqualTo(CraftingStation.Campfire));
            Assert.That(recipe.TimeTicks, Is.EqualTo(0));
        }

        [Test]
        public void ClayKilnRecipeHasNonZeroTimeTicks()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());
            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.FiredBrick);

            Assert.That(recipe.RequiredStation, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(recipe.TimeTicks, Is.GreaterThan(0));
        }

        [Test]
        public void BellowsForgeRecipeHasNonZeroTimeTicks()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());
            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.FlintDelver);

            Assert.That(recipe.RequiredStation, Is.EqualTo(CraftingStation.BellowsForge));
            Assert.That(recipe.TimeTicks, Is.GreaterThan(0));
        }

        [Test]
        public void PrepBoardAndMendBenchRecipesAreInstant()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());

            Assert.That(recipeBook.GetByOutput(ItemId.WorkPlank).RequiredStation, Is.EqualTo(CraftingStation.PrepBoard));
            Assert.That(recipeBook.GetByOutput(ItemId.WorkPlank).TimeTicks, Is.EqualTo(0));
            Assert.That(recipeBook.GetByOutput(ItemId.ReedwoodSpade).RequiredStation, Is.EqualTo(CraftingStation.MendBench));
            Assert.That(recipeBook.GetByOutput(ItemId.ReedwoodSpade).TimeTicks, Is.EqualTo(0));
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
