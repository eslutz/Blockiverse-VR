using System;
using System.Collections.Generic;

namespace Blockiverse.Survival
{
    public sealed class CraftingRecipeBook
    {
        readonly ItemRegistry itemRegistry;
        readonly Dictionary<ItemId, CraftingRecipe> recipesByOutput = new();
        // Recipes in registration order, so callers (e.g. the crafting panel) present a stable,
        // deterministic list rather than the dictionary's hash-bucket order.
        readonly List<CraftingRecipe> orderedRecipes = new();

        public CraftingRecipeBook(ItemRegistry itemRegistry = null)
        {
            this.itemRegistry = itemRegistry ?? ItemRegistry.CreateDefault();
        }

        public IReadOnlyCollection<CraftingRecipe> All => orderedRecipes;

        // Seconds → ticks for timed (kiln/forge) recipes (§9.3/§9.4).
        static int Seconds(int seconds) => seconds * SmeltingModel.TicksPerSecond;

        public static CraftingRecipeBook CreateDefault(ItemRegistry itemRegistry = null)
        {
            itemRegistry ??= ItemRegistry.CreateDefault();
            var book = new CraftingRecipeBook(itemRegistry);

            // Where the ruleset names a resource the project tracks under a different id, the
            // existing id is used: flint_shard → flinty_shingle, resin_blob → resin_knot,
            // stone_pebble → surface_pebbles. Recipes whose outputs are not yet registered
            // (furniture, containers, food/drink) are added with those items in later units.

            // ── §9.1 Basic recipes (Handcraft, instant) ──────────────────────
            book.Register(new CraftingRecipe(new ItemStack(ItemId.WorkPlank, 6), CraftingStation.None,
                new ItemStack(ItemId.BranchwoodLog, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.StoutPole, 4), CraftingStation.None,
                new ItemStack(ItemId.WorkPlank, 2)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.FiberCord, 2), CraftingStation.None,
                new ItemStack(ItemId.ReedFiber, 3)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.StoneRubble, 4), CraftingStation.None,
                new ItemStack(ItemId.Graystone, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.Glowwick, 4), CraftingStation.None,
                new ItemStack(ItemId.StoutPole, 1), new ItemStack(ItemId.Embercoal, 1), new ItemStack(ItemId.FiberCord, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.Campfire, 1), CraftingStation.None,
                new ItemStack(ItemId.SurfacePebbles, 4), new ItemStack(ItemId.StoutPole, 3), new ItemStack(ItemId.ResinKnot, 1)));
            book.Register(new CraftingRecipe(itemRegistry.CreateItemStack(ItemId.FlintCarver), CraftingStation.None,
                new ItemStack(ItemId.FlintyShingle, 1), new ItemStack(ItemId.StoutPole, 1), new ItemStack(ItemId.FiberCord, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.BuildTable, 1), CraftingStation.None,
                new ItemStack(ItemId.WorkPlank, 8), new ItemStack(ItemId.FiberCord, 2)));

            // ── §9.2 Build Table recipes (instant) ───────────────────────────
            book.Register(new CraftingRecipe(new ItemStack(ItemId.StorageCrate, 1), CraftingStation.BuildTable,
                new ItemStack(ItemId.WorkPlank, 12), new ItemStack(ItemId.StoutPole, 2)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.ClayKiln, 1), CraftingStation.BuildTable,
                new ItemStack(ItemId.ClayLump, 12), new ItemStack(ItemId.StoneRubble, 8), new ItemStack(ItemId.Embercoal, 2)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.PrepBoard, 1), CraftingStation.BuildTable,
                new ItemStack(ItemId.WorkPlank, 4), new ItemStack(ItemId.FlintyShingle, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.MendBench, 1), CraftingStation.BuildTable,
                new ItemStack(ItemId.WorkPlank, 10), new ItemStack(ItemId.StoneRubble, 6), new ItemStack(ItemId.ResinKnot, 2)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.CutstoneBlock, 4), CraftingStation.BuildTable,
                new ItemStack(ItemId.StoneRubble, 8)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.FiredBrickBlock, 4), CraftingStation.BuildTable,
                new ItemStack(ItemId.FiredBrick, 8)));

            // ── §9.3 Clay Kiln recipes (timed) ───────────────────────────────
            book.Register(new CraftingRecipe(new ItemStack(ItemId.FiredBrick, 1), CraftingStation.ClayKiln, Seconds(8),
                new[] { new ItemStack(ItemId.ClayLump, 2) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.GlassShard, 2), CraftingStation.ClayKiln, Seconds(8),
                new[] { new ItemStack(ItemId.PaleSand, 2) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.ClearpaneGlass, 1), CraftingStation.ClayKiln, Seconds(10),
                new[] { new ItemStack(ItemId.GlassShard, 4), new ItemStack(ItemId.Shellgrit, 1) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.RosycopperBar, 1), CraftingStation.ClayKiln, Seconds(12),
                new[] { new ItemStack(ItemId.RawRosycopper, 2) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.PaletinBar, 1), CraftingStation.ClayKiln, Seconds(12),
                new[] { new ItemStack(ItemId.RawPaletin, 2) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.LumenDust, 2), CraftingStation.ClayKiln, Seconds(6),
                new[] { new ItemStack(ItemId.LumenCrystal, 1) }));

            // ── §9.4 Bellows Forge recipes ───────────────────────────────────
            book.Register(new CraftingRecipe(new ItemStack(ItemId.BellowsForge, 1), CraftingStation.BuildTable,
                new ItemStack(ItemId.FiredBrick, 16), new ItemStack(ItemId.RosycopperBar, 4),
                new ItemStack(ItemId.FiberCord, 4), new ItemStack(ItemId.WorkPlank, 4)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.BronzeBar, 4), CraftingStation.BellowsForge, Seconds(16),
                new[] { new ItemStack(ItemId.RosycopperBar, 3), new ItemStack(ItemId.PaletinBar, 1) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.IronrootBar, 1), CraftingStation.BellowsForge, Seconds(16),
                new[] { new ItemStack(ItemId.RawRustcore, 2), new ItemStack(ItemId.Embercoal, 1) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.SunmetalBar, 1), CraftingStation.BellowsForge, Seconds(18),
                new[] { new ItemStack(ItemId.RawSunmetal, 2), new ItemStack(ItemId.Embercoal, 1) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.DeepsteelBar, 1), CraftingStation.BellowsForge, Seconds(24),
                new[] { new ItemStack(ItemId.RawUmbralite, 2), new ItemStack(ItemId.IronrootBar, 1), new ItemStack(ItemId.LumenDust, 1) }));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.StarforgedCore, 1), CraftingStation.BellowsForge, Seconds(30),
                new[] { new ItemStack(ItemId.StaropalShard, 4), new ItemStack(ItemId.DeepsteelBar, 2), new ItemStack(ItemId.LumenCrystal, 2) }));

            // ── §9.5 Tool recipes ────────────────────────────────────────────
            RegisterToolRecipes(book, itemRegistry);

            // ── §9.6 Utility and survival items (registered outputs only) ─────
            book.Register(new CraftingRecipe(new ItemStack(ItemId.LumenLamp, 2), CraftingStation.BuildTable,
                new ItemStack(ItemId.LumenCrystal, 1), new ItemStack(ItemId.GlassShard, 2), new ItemStack(ItemId.SunmetalBar, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.SparkFlare, 3), CraftingStation.BuildTable,
                new ItemStack(ItemId.SparkNiter, 1), new ItemStack(ItemId.ReedFiber, 1), new ItemStack(ItemId.Embercoal, 1)));
            book.Register(new CraftingRecipe(new ItemStack(ItemId.FieldBandage, 2), CraftingStation.PrepBoard,
                new ItemStack(ItemId.ReedFiber, 4), new ItemStack(ItemId.ResinKnot, 1)));

            return book;
        }

        // §9.5 generic tool recipes. Wood/flint tools at the Build Table; metal and starforged
        // tools at the Bellows Forge.
        static void RegisterToolRecipes(CraftingRecipeBook book, ItemRegistry itemRegistry)
        {
            // Reedwood (work_plank head) and Flint (flinty_shingle head): Delver/Spade/Feller only.
            book.RegisterTool("reedwood_delver", CraftingStation.BuildTable, new ItemStack(ItemId.WorkPlank, 3), new ItemStack(ItemId.StoutPole, 2));
            book.RegisterTool("reedwood_spade",  CraftingStation.BuildTable, new ItemStack(ItemId.WorkPlank, 1), new ItemStack(ItemId.StoutPole, 2));
            book.RegisterTool("reedwood_feller", CraftingStation.BuildTable, new ItemStack(ItemId.WorkPlank, 3), new ItemStack(ItemId.StoutPole, 2));
            book.RegisterTool("flint_delver", CraftingStation.BuildTable, new ItemStack(ItemId.FlintyShingle, 3), new ItemStack(ItemId.StoutPole, 2), new ItemStack(ItemId.FiberCord, 1));
            book.RegisterTool("flint_spade",  CraftingStation.BuildTable, new ItemStack(ItemId.FlintyShingle, 1), new ItemStack(ItemId.StoutPole, 2), new ItemStack(ItemId.FiberCord, 1));
            book.RegisterTool("flint_feller", CraftingStation.BuildTable, new ItemStack(ItemId.FlintyShingle, 3), new ItemStack(ItemId.StoutPole, 2), new ItemStack(ItemId.FiberCord, 1));

            // Metal tools: bar count per class (§9.5), at the Bellows Forge.
            foreach ((string material, ItemId bar) in MetalToolMaterials)
            {
                foreach ((string toolClass, ItemStack[] extras, int barCount) in MetalToolClassParts)
                {
                    var ingredients = new List<ItemStack>(extras.Length + 1) { new ItemStack(bar, barCount) };
                    ingredients.AddRange(extras);
                    book.RegisterTool($"{material}_{toolClass}", CraftingStation.BellowsForge, ingredients.ToArray());
                }
            }

            // Starforged tools: a single recipe shape for every class (§9.5).
            foreach (string toolClass in ToolClassSuffixes)
            {
                book.RegisterTool($"starforged_{toolClass}", CraftingStation.BellowsForge,
                    new ItemStack(ItemId.StarforgedCore, 1), new ItemStack(ItemId.DeepsteelBar, 2), new ItemStack(ItemId.StoutPole, 2));
            }
        }

        static readonly (string material, ItemId bar)[] MetalToolMaterials =
        {
            ("rosycopper", ItemId.RosycopperBar),
            ("bronze",     ItemId.BronzeBar),
            ("ironroot",   ItemId.IronrootBar),
            ("deepsteel",  ItemId.DeepsteelBar),
        };

        // Per §9.5: bar count and non-bar extras for each tool class.
        static readonly (string toolClass, ItemStack[] extras, int barCount)[] MetalToolClassParts =
        {
            ("delver", new[] { new ItemStack(ItemId.StoutPole, 2) }, 3),
            ("spade",  new[] { new ItemStack(ItemId.StoutPole, 2) }, 1),
            ("feller", new[] { new ItemStack(ItemId.StoutPole, 2) }, 3),
            ("sickle", new[] { new ItemStack(ItemId.StoutPole, 1), new ItemStack(ItemId.FiberCord, 1) }, 2),
            ("mallet", new[] { new ItemStack(ItemId.StoutPole, 2) }, 4),
            ("tiller", new[] { new ItemStack(ItemId.StoutPole, 2) }, 2),
            ("carver", new[] { new ItemStack(ItemId.StoutPole, 1), new ItemStack(ItemId.FiberCord, 1) }, 1),
        };

        static readonly string[] ToolClassSuffixes =
        {
            "delver", "spade", "feller", "sickle", "mallet", "tiller", "carver",
        };

        void RegisterTool(string toolId, CraftingStation station, params ItemStack[] ingredients)
        {
            Register(new CraftingRecipe(itemRegistry.CreateItemStack(new ItemId(toolId)), station, 0, ingredients));
        }

        public void Register(CraftingRecipe recipe)
        {
            if (recipe == null)
                throw new ArgumentNullException(nameof(recipe));

            itemRegistry.Get(recipe.Output.ItemId);
            foreach (ItemStack ingredient in recipe.Ingredients)
                itemRegistry.Get(ingredient.ItemId);

            if (recipesByOutput.ContainsKey(recipe.Output.ItemId))
                throw new InvalidOperationException($"A recipe is already registered for output item: {recipe.Output.ItemId}");

            recipesByOutput.Add(recipe.Output.ItemId, recipe);
            orderedRecipes.Add(recipe);
        }

        public CraftingRecipe GetByOutput(ItemId outputItemId)
        {
            if (!recipesByOutput.TryGetValue(outputItemId, out CraftingRecipe recipe))
                throw new KeyNotFoundException($"No crafting recipe is registered for output item: {outputItemId}");

            return recipe;
        }

        public bool TryGetByOutput(ItemId outputItemId, out CraftingRecipe recipe)
        {
            return recipesByOutput.TryGetValue(outputItemId, out recipe);
        }
    }
}
