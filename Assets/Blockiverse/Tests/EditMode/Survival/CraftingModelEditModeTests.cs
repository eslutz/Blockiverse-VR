using System.Linq;
using Blockiverse.Survival;
using NUnit.Framework;

namespace Blockiverse.Tests.Survival.EditMode
{
    public sealed class CraftingModelEditModeTests
    {
        [Test]
        public void DefaultRecipeBookContainsCanonicalRecipes()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);

            // 8 handcraft + 6 build-table + 6 kiln + 6 forge + 41 tools + 3 utility (§9).
            Assert.That(recipeBook.All.Count, Is.EqualTo(70));

            // §9.1 basics are handcraft (no station).
            AssertRecipe(recipeBook, ItemId.WorkPlank, CraftingStation.None, new ItemStack(ItemId.WorkPlank, 6), new ItemStack(ItemId.BranchwoodLog, 1));
            AssertRecipe(recipeBook, ItemId.StoutPole, CraftingStation.None, new ItemStack(ItemId.StoutPole, 4), new ItemStack(ItemId.WorkPlank, 2));
            AssertRecipe(recipeBook, ItemId.FiberCord, CraftingStation.None, new ItemStack(ItemId.FiberCord, 2), new ItemStack(ItemId.ReedFiber, 3));
            AssertRecipe(recipeBook, ItemId.BuildTable, CraftingStation.None, new ItemStack(ItemId.BuildTable, 1), new ItemStack(ItemId.WorkPlank, 8), new ItemStack(ItemId.FiberCord, 2));

            // §9.2 build-table.
            AssertRecipe(recipeBook, ItemId.StorageCrate, CraftingStation.BuildTable, new ItemStack(ItemId.StorageCrate, 1), new ItemStack(ItemId.WorkPlank, 12), new ItemStack(ItemId.StoutPole, 2));
            AssertRecipe(recipeBook, ItemId.FiredBrickBlock, CraftingStation.BuildTable, new ItemStack(ItemId.FiredBrickBlock, 4), new ItemStack(ItemId.FiredBrick, 8));

            // §9.6 utility.
            AssertRecipe(recipeBook, ItemId.LumenLamp, CraftingStation.BuildTable, new ItemStack(ItemId.LumenLamp, 2),
                new ItemStack(ItemId.LumenCrystal, 1), new ItemStack(ItemId.GlassShard, 2), new ItemStack(ItemId.SunmetalBar, 1));
            AssertRecipe(recipeBook, ItemId.FieldBandage, CraftingStation.PrepBoard, new ItemStack(ItemId.FieldBandage, 2),
                new ItemStack(ItemId.ReedFiber, 4), new ItemStack(ItemId.ResinKnot, 1));
        }

        [Test]
        public void KilnAndForgeRecipesCarryCanonicalDurations()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());

            CraftingRecipe firedBrick = recipeBook.GetByOutput(ItemId.FiredBrick);
            Assert.That(firedBrick.RequiredStation, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(firedBrick.TimeTicks, Is.EqualTo(8 * SmeltingModel.TicksPerSecond));

            CraftingRecipe rosycopperBar = recipeBook.GetByOutput(ItemId.RosycopperBar);
            Assert.That(rosycopperBar.RequiredStation, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(rosycopperBar.TimeTicks, Is.EqualTo(12 * SmeltingModel.TicksPerSecond));

            CraftingRecipe starforgedCore = recipeBook.GetByOutput(ItemId.StarforgedCore);
            Assert.That(starforgedCore.RequiredStation, Is.EqualTo(CraftingStation.BellowsForge));
            Assert.That(starforgedCore.TimeTicks, Is.EqualTo(30 * SmeltingModel.TicksPerSecond));
        }

        [Test]
        public void ToolRecipesFollowCanonicalStationsAndIngredients()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);

            // Wood/flint tools at the Build Table, instant.
            CraftingRecipe reedwoodDelver = recipeBook.GetByOutput(ItemId.ReedwoodDelver);
            Assert.That(reedwoodDelver.RequiredStation, Is.EqualTo(CraftingStation.BuildTable));
            Assert.That(reedwoodDelver.TimeTicks, Is.EqualTo(0));
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.WorkPlank, 3), new ItemStack(ItemId.StoutPole, 2) },
                reedwoodDelver.Ingredients.ToArray());

            // Metal tools at the Bellows Forge, from bars.
            CraftingRecipe rosycopperDelver = recipeBook.GetByOutput(new ItemId("rosycopper_delver"));
            Assert.That(rosycopperDelver.RequiredStation, Is.EqualTo(CraftingStation.BellowsForge));
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.RosycopperBar, 3), new ItemStack(ItemId.StoutPole, 2) },
                rosycopperDelver.Ingredients.ToArray());

            // Starforged tools share one recipe shape across all seven classes.
            CraftingRecipe starforgedMallet = recipeBook.GetByOutput(new ItemId("starforged_mallet"));
            Assert.That(starforgedMallet.RequiredStation, Is.EqualTo(CraftingStation.BellowsForge));
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.StarforgedCore, 1), new ItemStack(ItemId.DeepsteelBar, 2), new ItemStack(ItemId.StoutPole, 2) },
                starforgedMallet.Ingredients.ToArray());
        }

        [Test]
        public void CraftingValidationRequiresStationBeforeConsumingInputs()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 12));
            inventory.SetSlot(1, new ItemStack(ItemId.StoutPole, 2));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.StorageCrate);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingStation));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.WorkPlank, 12)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.StoutPole, 2)));
            Assert.That(inventory.CountOf(ItemId.StorageCrate), Is.Zero);
        }

        [Test]
        public void CraftingConsumesIngredientsAndAddsOutputWhenRequirementsAreMet()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 3));
            inventory.SetSlot(1, new ItemStack(ItemId.StoutPole, 2));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.ReedwoodDelver);
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.BuildTable);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.None));
            Assert.That(inventory.CountOf(ItemId.WorkPlank), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.StoutPole), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.ReedwoodDelver), Is.EqualTo(1));
        }

        [Test]
        public void CraftingRejectsMissingIngredientsWithoutChangingInventory()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.StoutPole, 2));

            CraftingRecipe recipe = recipeBook.GetByOutput(ItemId.ReedwoodDelver); // needs work_plank ×3
            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.BuildTable);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.MissingIngredient));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.WorkPlank));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.WorkPlank, 1)));
            Assert.That(inventory.GetSlot(1), Is.EqualTo(new ItemStack(ItemId.StoutPole, 2)));
            Assert.That(inventory.CountOf(ItemId.ReedwoodDelver), Is.Zero);
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
        public void HandcraftAndBuildTableRecipesAreInstantWhileSmeltingIsTimed()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());

            Assert.That(recipeBook.GetByOutput(ItemId.Campfire).RequiredStation, Is.EqualTo(CraftingStation.None));
            Assert.That(recipeBook.GetByOutput(ItemId.Campfire).TimeTicks, Is.EqualTo(0));
            Assert.That(recipeBook.GetByOutput(ItemId.FlintDelver).RequiredStation, Is.EqualTo(CraftingStation.BuildTable));
            Assert.That(recipeBook.GetByOutput(ItemId.FlintDelver).TimeTicks, Is.EqualTo(0));
            Assert.That(recipeBook.GetByOutput(ItemId.BronzeBar).TimeTicks, Is.GreaterThan(0));
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
