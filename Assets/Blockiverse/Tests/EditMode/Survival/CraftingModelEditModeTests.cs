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

            // 8 handcraft + 7 build-table + 7 kiln + 6 forge + 41 tools + 3 utility + 2 campfire
            // fluid recipes (§9, §5.4).
            Assert.That(recipeBook.All.Count, Is.EqualTo(74));

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

        [Test]
        public void FluidContainerRecipesFollowTheCanonicalChain()
        {
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(ItemRegistry.CreateDefault());

            // §632: buckets are crafted empty at the Build Table; filled ones come from the
            // world fill action, never from a recipe.
            AssertRecipe(recipeBook, ItemId.EmptyBucket, CraftingStation.BuildTable,
                new ItemStack(ItemId.EmptyBucket, 1), new ItemStack(ItemId.RosycopperBar, 3));
            Assert.That(recipeBook.TryGetByOutput(ItemId.FreshwaterBucket, out _), Is.False);
            Assert.That(recipeBook.TryGetByOutput(ItemId.BrineBucket, out _), Is.False);

            // §623: the empty glass flask is a timed kiln fire.
            CraftingRecipe waterFlask = recipeBook.GetByOutput(ItemId.WaterFlask);
            Assert.That(waterFlask.RequiredStation, Is.EqualTo(CraftingStation.ClayKiln));
            Assert.That(waterFlask.TimeTicks, Is.EqualTo(8 * SmeltingModel.TicksPerSecond));

            // §624/§731: filling the flask at the campfire returns the emptied bucket.
            CraftingRecipe cleanFlask = recipeBook.GetByOutput(ItemId.CleanWaterFlask);
            Assert.That(cleanFlask.RequiredStation, Is.EqualTo(CraftingStation.Campfire));
            Assert.That(cleanFlask.TimeTicks, Is.EqualTo(0));
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.WaterFlask, 1), new ItemStack(ItemId.FreshwaterBucket, 1) },
                cleanFlask.Ingredients.ToArray());
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.EmptyBucket, 1) },
                cleanFlask.Byproducts.ToArray());

            // §574 (station/timing deviation documented in the book): brine boils to brightsalt
            // at the campfire, returning the bucket.
            CraftingRecipe brineBoil = recipeBook.GetByOutput(ItemId.Brightsalt);
            Assert.That(brineBoil.RequiredStation, Is.EqualTo(CraftingStation.Campfire));
            Assert.That(brineBoil.Output, Is.EqualTo(new ItemStack(ItemId.Brightsalt, 3)));
            CollectionAssert.AreEquivalent(
                new[] { new ItemStack(ItemId.EmptyBucket, 1) },
                brineBoil.Byproducts.ToArray());
        }

        [Test]
        public void CraftingGrantsByproductsAlongsideTheOutput()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(itemRegistry);
            Inventory inventory = new(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.WaterFlask, 1));
            inventory.SetSlot(1, new ItemStack(ItemId.FreshwaterBucket, 1));

            CraftingResult result = CraftingService.TryCraft(
                inventory, recipeBook.GetByOutput(ItemId.CleanWaterFlask), CraftingStation.Campfire);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(inventory.CountOf(ItemId.CleanWaterFlask), Is.EqualTo(1));
            Assert.That(inventory.CountOf(ItemId.EmptyBucket), Is.EqualTo(1), "The emptied bucket must return (§731).");
            Assert.That(inventory.CountOf(ItemId.WaterFlask), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.FreshwaterBucket), Is.Zero);
        }

        [Test]
        public void CraftingRollsBackEverythingWhenAByproductCannotFit()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            // One slot: the ingredient frees it, the output takes it, the byproduct cannot fit.
            Inventory inventory = new(itemRegistry, slotCount: 1, hotbarSlotCount: 1);
            inventory.SetSlot(0, new ItemStack(ItemId.ReedFiber, 1));
            CraftingRecipe recipe = new(
                new ItemStack(ItemId.WorkPlank, 1),
                CraftingStation.None,
                timeTicks: 0,
                new[] { new ItemStack(ItemId.ReedFiber, 1) },
                new[] { new ItemStack(ItemId.EmptyBucket, 1) });

            CraftingResult result = CraftingService.TryCraft(inventory, recipe, CraftingStation.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CraftingFailureReason.OutputBlocked));
            Assert.That(result.FailedItemId, Is.EqualTo(ItemId.EmptyBucket));
            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.ReedFiber, 1)), "A blocked byproduct must undo the whole craft.");
            Assert.That(inventory.CountOf(ItemId.WorkPlank), Is.Zero);
            Assert.That(inventory.CountOf(ItemId.EmptyBucket), Is.Zero);
        }

        [Test]
        public void TimedRecipesCannotDeclareByproducts()
        {
            // The fueled station model grants only the primary output, so a timed recipe with a
            // byproduct would silently drop it — the constructor forbids the combination.
            Assert.Throws<System.ArgumentException>(() => new CraftingRecipe(
                new ItemStack(ItemId.Brightsalt, 3),
                CraftingStation.ClayKiln,
                timeTicks: 10 * SmeltingModel.TicksPerSecond,
                new[] { new ItemStack(ItemId.BrineBucket, 1) },
                new[] { new ItemStack(ItemId.EmptyBucket, 1) }));
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
