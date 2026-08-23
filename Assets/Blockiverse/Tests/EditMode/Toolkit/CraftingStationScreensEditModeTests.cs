using System.Collections.Generic;
using System.Linq;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Screen-level mirror of SurvivalCraftingPanelEditModeTests (matrix row 16): the same
    // paging and availability-marker behaviours observed through the UI Toolkit document,
    // plus the host-authoritative submit contract (accepted-or-pending is UI success, the
    // CraftingChanged domain event fires only on Accepted). UIDocument builds no tree in
    // EditMode, so documents are instantiated directly and attached via AttachForTest;
    // interactions drive the controller's public seams (TryCraftVisibleIndex,
    // ShowNextRecipePage, …) because ClickEvent dispatch needs a live panel.
    public sealed class CraftingScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/CraftingScreen.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        CraftingScreenController CreateAttachedController(out VisualElement root)
        {
            var gameObject = new GameObject("Crafting Screen Under Test");
            objectsToDestroy.Add(gameObject);
            CraftingScreenController controller = gameObject.AddComponent<CraftingScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        static List<string> RowTexts(VisualElement root)
        {
            var texts = new List<string>(CraftingScreenController.RecipeRowCount);
            for (int i = 1; i <= CraftingScreenController.RecipeRowCount; i++)
                texts.Add(root.Q<Label>($"bv-recipe-label-{i}").text ?? string.Empty);
            return texts;
        }

        static string RowTextContaining(VisualElement root, string fragment) =>
            RowTexts(root).First(text => text.Contains(fragment));

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            CraftingScreenController controller = CreateAttachedController(out _);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in CraftingScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1),
                "Attach must leave exactly one callback registration.");

            // Negative control: a controller that returned true from OnAttach without
            // querying anything would also 'pass' the real document — it must fail here.
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var strayObject = new GameObject("Empty Tree Control");
                objectsToDestroy.Add(strayObject);
                CraftingScreenController stray = strayObject.AddComponent<CraftingScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False,
                    "An empty tree has none of the screen's elements; IsBound must be false.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        // Oracle mirror 1: PagingExposesRecipesBeyondGeneratedVisibleRowsAndCraftsVisiblePageSelection.
        // Same recipe book, same five-row page size, same craft-by-visible-index semantics.
        [Test]
        public void PagingExposesRecipesBeyondVisibleRowsAndCraftsVisiblePageSelection()
        {
            CraftingScreenController controller = CreateAttachedController(out VisualElement root);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);
            Inventory inventory = new(registry);
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 8));
            inventory.SetSlot(1, new ItemStack(ItemId.FiberCord, 2));

            controller.Bind(recipeBook, inventory, registry, CraftingStation.None);

            Assert.That(root.Q<Label>("bv-crafting-status").text, Is.EqualTo("Ready"),
                "Bind writes the ready status — a do-nothing controller leaves it blank.");
            Assert.That(root.Q<Label>("bv-page-label").text, Does.StartWith("1/"));
            Assert.That(RowTexts(root).Any(text => text.Contains("Work Plank")), Is.True);
            Assert.That(RowTexts(root).Any(text => text.Contains("Build Table")), Is.False,
                "Build Table sits beyond the five visible rows until the page turns.");

            controller.ShowNextRecipePage();

            Assert.That(root.Q<Label>("bv-page-label").text, Does.StartWith("2/"));
            Assert.That(RowTexts(root).Any(text => text.Contains("Build Table")), Is.True);

            CraftingResult result = controller.TryCraftVisibleIndex(2);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(inventory.CountOf(ItemId.BuildTable), Is.EqualTo(1));
        }

        // Oracle mirror 2: RecipeLabelsIncludeAvailabilityMarkers — the ✗/✓/! prefixes are
        // byte-parity with the uGUI rows; Storage Crate lives on page two at five rows/page.
        [Test]
        public void RecipeLabelsIncludeAvailabilityMarkers()
        {
            CraftingScreenController controller = CreateAttachedController(out VisualElement root);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);
            Inventory inventory = new(registry);

            controller.Bind(recipeBook, inventory, registry, CraftingStation.None);

            Assert.That(RowTextContaining(root, "Work Plank"), Does.StartWith("✗ "));

            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            controller.Refresh();

            Assert.That(RowTextContaining(root, "Work Plank"), Does.StartWith("✓ "));

            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 12));
            inventory.SetSlot(1, new ItemStack(ItemId.StoutPole, 2));
            controller.Refresh();
            controller.ShowNextRecipePage();

            Assert.That(RowTextContaining(root, "Storage Crate"), Does.StartWith("! "),
                "A station-gated recipe out of reach carries the wrong-station marker.");

            controller.SetAvailableStations(CraftingStationSet.Of(CraftingStation.BuildTable));

            Assert.That(RowTextContaining(root, "Storage Crate"), Does.StartWith("✓ "));
        }

        // Matrix §4 item 2 through a real (offline) MultiplayerSurvivalSync: the host path
        // resolves immediately, so Accepted mutates the authoritative inventory and raises
        // CraftingChanged; a host rejection raises nothing and reports the failure. The
        // pending outcome (sentToHost) needs a live client-only network session and is not
        // reachable in EditMode — its acceptedOrPending mapping is ported line-for-line from
        // the uGUI panel.
        [Test]
        public void AuthoritativeCraftRaisesTheDomainEventOnlyOnAccepted()
        {
            CraftingScreenController controller = CreateAttachedController(out VisualElement root);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);

            var syncObject = new GameObject("Survival Sync Under Test");
            objectsToDestroy.Add(syncObject);
            MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, null, registry, recipeBook);

            Inventory inventory = sync.LocalInventory;
            inventory.SetSlot(0, new ItemStack(ItemId.WorkPlank, 8));
            inventory.SetSlot(1, new ItemStack(ItemId.FiberCord, 2));

            controller.ConfigureSurvivalSync(sync);
            controller.Bind(recipeBook, inventory, registry, CraftingStation.None);

            int craftingChangedCount = 0;
            controller.CraftingChanged += () => craftingChangedCount++;

            CraftingResult accepted = controller.TryCraftByOutput(ItemId.BuildTable);

            Assert.That(accepted.Succeeded, Is.True);
            Assert.That(craftingChangedCount, Is.EqualTo(1));
            Assert.That(inventory.CountOf(ItemId.BuildTable), Is.EqualTo(1),
                "The accepted command must have mutated the sync's authoritative inventory.");
            Label status = root.Q<Label>("bv-crafting-status");
            Assert.That(status.text, Is.EqualTo("Crafted Build Table x1"));
            Assert.That(status.ClassListContains("hs-status--confirmed"), Is.True);

            // Storage Crate requires the Build Table station; with no station in reach the
            // screen claims None (never a substitute) and the host rejects MissingStation.
            CraftingResult rejected = controller.TryCraftByOutput(ItemId.StorageCrate);

            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(craftingChangedCount, Is.EqualTo(1),
                "A rejected command must not raise the domain event.");
            Assert.That(status.text, Is.EqualTo("Cannot craft Storage Crate: Missing Station"));
            Assert.That(status.ClassListContains("hs-status--refused"), Is.True);
            Assert.That(status.ClassListContains("hs-status--confirmed"), Is.False);
        }
    }

    // Matrix row 20: the station screen mirrors a SmeltingStationModel and exposes the
    // IsOpenAt/CloseView seam BlockiverseMenuController drives when the backing station block
    // is removed (the frontend half of StationPanelClosesWhenOpenStationIsRemoved; the
    // controller half is covered by MenuFrontendSeamEditModeTests).
    public sealed class StationScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/StationScreen.uxml";

        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        StationScreenController CreateAttachedController(out VisualElement root)
        {
            var gameObject = new GameObject("Station Screen Under Test");
            objectsToDestroy.Add(gameObject);
            StationScreenController controller = gameObject.AddComponent<StationScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            StationScreenController controller = CreateAttachedController(out VisualElement root);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in StationScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));
            Assert.That(controller.IsOpen, Is.False);
            Assert.That(root.Q<Label>("bv-station-title").text, Is.EqualTo("Station"),
                "The never-opened default title renders through UiText — blank means OnAttach did nothing.");

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var strayObject = new GameObject("Empty Tree Control");
                objectsToDestroy.Add(strayObject);
                StationScreenController stray = strayObject.AddComponent<StationScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False,
                    "An empty tree has none of the screen's elements; IsBound must be false.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void OpenRendersTheBoundModelAndScopesIsOpenAtToThePosition()
        {
            StationScreenController controller = CreateAttachedController(out VisualElement root);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            CraftingRecipeBook recipeBook = CraftingRecipeBook.CreateDefault(registry);
            var model = new SmeltingStationModel(CraftingStation.ClayKiln, 1, recipeBook, registry);
            Assert.That(model.TryDepositInput(new ItemStack(ItemId.ClayLump, 2)), Is.True);
            var position = new BlockPosition(2, 1, 2);

            controller.ConfigureItemRegistry(registry);
            Assert.That(controller.IsOpenAt(position), Is.False,
                "Positive control: nothing is open before Open.");

            controller.Open(model, position);

            Assert.That(controller.IsOpen, Is.True);
            Assert.That(controller.OpenPosition, Is.EqualTo(position));
            Assert.That(controller.IsOpenAt(position), Is.True);
            Assert.That(controller.IsOpenAt(new BlockPosition(9, 3, 9)), Is.False,
                "Removing a DIFFERENT station block must not read as this screen's station.");

            Assert.That(root.Q<Label>("bv-station-title").text, Is.EqualTo("Clay Kiln"));
            Assert.That(root.Q<Label>("bv-station-input-1").text, Is.EqualTo("Clay Lump ×2"));
            Assert.That(root.Q<Label>("bv-station-input-2").text, Is.EqualTo("—"),
                "Slots beyond the kiln's single input render the empty glyph.");
            Assert.That(root.Q<Label>("bv-station-fuel").text, Is.EqualTo("No fuel"));
            Assert.That(root.Q<Label>("bv-station-output").text, Is.EqualTo("—"));
            Assert.That(root.Q<Label>("bv-station-status").text, Is.EqualTo("Idle"));

            Slider progress = root.Q<Slider>("bv-station-progress");
            Assert.That(progress.highValue, Is.EqualTo(1f),
                "An idle station clamps the progress range to max(1, RequiredTicks).");
            Assert.That(progress.value, Is.EqualTo(0f));

            controller.Open(model, position, "Old Kiln");
            Assert.That(root.Q<Label>("bv-station-title").text, Is.EqualTo("Old Kiln"),
                "An explicit display title overrides the station-type name, like the uGUI Open.");
        }

        [Test]
        public void CloseViewClosesTheOpenStation()
        {
            StationScreenController controller = CreateAttachedController(out _);
            ItemRegistry registry = ItemRegistry.CreateDefault();
            var model = new SmeltingStationModel(CraftingStation.ClayKiln, 1, itemRegistry: registry);
            var position = new BlockPosition(2, 1, 2);

            controller.ConfigureItemRegistry(registry);
            controller.Open(model, position);
            Assert.That(controller.IsOpen, Is.True, "Positive control: CloseView must have something to close.");

            controller.CloseView();

            Assert.That(controller.IsOpen, Is.False);
            Assert.That(controller.IsOpenAt(position), Is.False,
                "After the close-on-removed path runs, the same position no longer reads as open.");
        }
    }
}
