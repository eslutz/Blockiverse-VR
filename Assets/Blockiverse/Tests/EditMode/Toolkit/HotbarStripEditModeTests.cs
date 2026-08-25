using System;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode.Toolkit
{
    // Behaviour of the persistent hotbar strip.
    //
    // Written because the panel had NO behavioural coverage at all: the only test touching it was a
    // placement TestCase restating the numbers in its own [UiToolkitScreen] attribute, which cannot
    // fail for any change to what the strip actually renders or selects. The strip is the most
    // frequently read element of the whole HUD — it is on screen for the entire session and the
    // support hand's face buttons drive it — so "renders the right slots, selects the right slot"
    // is exactly what needs pinning.
    public sealed class HotbarStripEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }

            objectsToDestroy.Clear();
        }

        (HotbarStripController controller, VisualElement root) CreateStrip()
        {
            var gameObject = new GameObject(nameof(HotbarStripController));
            objectsToDestroy.Add(gameObject);
            var controller = gameObject.AddComponent<HotbarStripController>();

            var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                typeof(HotbarStripController), typeof(UiToolkitScreenAttribute));
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);
            Assert.That(tree, Is.Not.Null, $"UXML missing at {attribute.DocumentAssetPath}.");

            VisualElement root = tree.Instantiate();
            controller.AttachForTest(root);
            return (controller, root);
        }

        static VisualElement Slot(VisualElement root, int index) =>
            root.Q<VisualElement>($"bv-hotbar-slot-{index}");

        static Label Count(VisualElement root, int index) =>
            root.Q<Label>($"bv-hotbar-slot-{index}-count");

        // Creative keeps its own block-label cycler and its quick block menu; ten empty survival
        // slots must not also be on screen, and the quick block menu (590 x 500 at Y -0.50)
        // overlaps this strip's band by 590 x 70 mm, so "harmless because empty" is not true.
        [Test]
        public void StripIsCollapsedWithoutASurvivalInventory()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();
            VisualElement strip = root.Q<VisualElement>("bv-hotbar-strip");

            controller.Refresh();
            Assert.That(strip.ClassListContains("hb-strip--hidden"), Is.True,
                "with no inventory bound the strip must collapse, not draw ten empty recesses");

            // Positive control: binding a survival inventory brings it back, so the collapse above
            // is the Creative case and not a strip that never shows at all.
            controller.BindForTest(new Inventory());
            Assert.That(strip.ClassListContains("hb-strip--hidden"), Is.False);
        }

        [Test]
        public void EveryDeclaredSlotExistsInTheDocument()
        {
            (_, VisualElement root) = CreateStrip();

            for (int i = 0; i < HotbarStripController.SlotCount; i++)
            {
                Assert.That(Slot(root, i), Is.Not.Null, $"slot {i} missing");
                Assert.That(root.Q<VisualElement>($"bv-hotbar-slot-{i}-icon"), Is.Not.Null,
                    $"slot {i} icon missing");
                Assert.That(Count(root, i), Is.Not.Null, $"slot {i} count missing");
            }

            // The strip indexes the inventory's hotbar range directly — the save format and the
            // wire protocol share this number, so it is not a layout choice.
            Assert.That(HotbarStripController.SlotCount,
                Is.EqualTo(Inventory.DefaultHotbarSlotCount),
                "The strip must show exactly the hotbar the domain defines.");
        }

        [Test]
        public void OccupiedSlotsAreMarkedAndEmptySlotsAreNot()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();

            var inventory = new Inventory();
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, 5));
            controller.BindForTest(inventory);

            Assert.That(Slot(root, 0).ClassListContains("hb-slot--occupied"), Is.True);

            // Positive control in the other direction: an untouched slot must NOT be marked, or
            // the class says nothing about occupancy.
            Assert.That(Slot(root, 1).ClassListContains("hb-slot--occupied"), Is.False);
        }

        [Test]
        public void StackCountShowsOnlyWhenItIsAQuantity()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();

            var inventory = new Inventory();
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, 5));
            inventory.SetSlot(1, new ItemStack(ItemId.LooseLoam, 1));
            controller.BindForTest(inventory);

            Assert.That(Count(root, 0).ClassListContains("hb-slot__count--hidden"), Is.False,
                "a stack of 5 must show its count");
            Assert.That(Count(root, 0).text, Does.Contain("5"));

            // A count of 1 is suppressed: ten slots each announcing "1" is noise, and the icon
            // already says the slot is occupied.
            Assert.That(Count(root, 1).ClassListContains("hb-slot__count--hidden"), Is.True,
                "a stack of 1 must not show a count");

            Assert.That(Count(root, 2).ClassListContains("hb-slot__count--hidden"), Is.True,
                "an empty slot must not show a count");
        }

        [Test]
        public void EmptyingASlotClearsWhatWasDrawnThere()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();

            var inventory = new Inventory();
            inventory.SetSlot(0, new ItemStack(ItemId.LooseLoam, 9));
            controller.BindForTest(inventory);
            Assert.That(Slot(root, 0).ClassListContains("hb-slot--occupied"), Is.True);

            inventory.SetSlot(0, ItemStack.Empty);
            controller.Refresh();

            Assert.That(Slot(root, 0).ClassListContains("hb-slot--occupied"), Is.False,
                "a consumed stack left the slot still reading as occupied");
            Assert.That(Count(root, 0).ClassListContains("hb-slot__count--hidden"), Is.True);
        }

        // Selection wraps in both directions. The double-modulo in Cycle exists because C# %
        // keeps the sign of the dividend, so a naive decrement past zero lands on a negative index
        // and the strip would either throw or mark nothing.
        [Test]
        public void SelectionWrapsForwardAndBackward()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();

            var inventory = new Inventory();
            controller.BindForTest(inventory);

            int slots = inventory.HotbarSlotCount;
            Assert.That(slots, Is.GreaterThan(1), "positive control: need at least two slots");

            controller.SelectSlot(0);
            Assert.That(controller.SelectedSlotIndex, Is.EqualTo(0));

            controller.SelectPrevious();
            Assert.That(controller.SelectedSlotIndex, Is.EqualTo(slots - 1),
                "cycling back from slot 0 must wrap to the last slot, not go negative");

            controller.SelectNext();
            Assert.That(controller.SelectedSlotIndex, Is.EqualTo(0),
                "cycling forward from the last slot must wrap to slot 0");
        }

        [Test]
        public void OnlyTheSelectedSlotCarriesTheSelectionMarker()
        {
            (HotbarStripController controller, VisualElement root) = CreateStrip();

            controller.BindForTest(new Inventory());
            controller.SelectSlot(3);

            int marked = 0;

            for (int i = 0; i < HotbarStripController.SlotCount; i++)
            {
                if (Slot(root, i).ClassListContains("hb-slot--selected"))
                    marked++;
            }

            Assert.That(marked, Is.EqualTo(1),
                "exactly one slot must read as selected — two would make the held item ambiguous");
            Assert.That(Slot(root, 3).ClassListContains("hb-slot--selected"), Is.True);
        }

        [Test]
        public void SelectionIsClampedToTheHotbarRange()
        {
            (HotbarStripController controller, _) = CreateStrip();

            var inventory = new Inventory();
            controller.BindForTest(inventory);

            controller.SelectSlot(999);
            Assert.That(controller.SelectedSlotIndex,
                Is.EqualTo(inventory.HotbarSlotCount - 1),
                "an out-of-range request must clamp rather than select a slot that does not exist");

            controller.SelectSlot(-4);
            Assert.That(controller.SelectedSlotIndex, Is.EqualTo(0));
        }

        // Cycling with nothing bound must not throw: the strip attaches with the HUD, which can
        // happen before a world (and therefore an inventory) exists.
        [Test]
        public void CyclingWithoutAnInventoryIsInert()
        {
            (HotbarStripController controller, _) = CreateStrip();

            Assert.DoesNotThrow(() => controller.SelectNext());
            Assert.DoesNotThrow(() => controller.SelectPrevious());
            Assert.DoesNotThrow(() => controller.SelectSlot(4));
        }

        // ── Creative mode ────────────────────────────────────────────────────
        //
        // Flagged by review: `inventory != null` alone does not detect Creative.
        // SurvivalCreativeModeSwitch.SwitchToCreative clears the bound Inventory's SLOTS but keeps
        // the same instance, with HotbarSlotCount unchanged — so without a mode check the strip
        // would keep drawing ten empty recesses in Creative, overlapping the Creative quick block
        // menu, and the face buttons would keep cycling a selection nobody can see change.

        (HotbarStripController controller, VisualElement root, MultiplayerSurvivalSync sync)
            CreateStripInCreativeWorld()
        {
            var managerObject = new GameObject("World Manager");
            objectsToDestroy.Add(managerObject);
            CreativeWorldManager manager = managerObject.AddComponent<CreativeWorldManager>();
            manager.SetGameMode(WorldGameMode.Creative);

            var syncObject = new GameObject("Survival Sync");
            objectsToDestroy.Add(syncObject);
            MultiplayerSurvivalSync sync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, manager);

            sync.SetMode(PlayerModeState.Creative);
            Assert.That(sync.CurrentMode, Is.EqualTo(PlayerModeState.Creative),
                "fixture failed: could not enter Creative mode");

            (HotbarStripController controller, VisualElement root) = CreateStrip();

            // Bound directly via reflection rather than left to OnAwake's BindFromScene. The full
            // EditMode suite runs many fixtures in one shared domain, and BlockiverseSceneLookup.Find
            // returns whichever MultiplayerSurvivalSync FindFirstObjectByType happens to enumerate
            // first — a leftover instance from an unrelated fixture that has not been torn down.
            // These tests target the CurrentMode check inside Refresh()/Cycle(), not scene discovery,
            // so binding the exact instance this fixture created is what makes them deterministic.
            typeof(HotbarStripController)
                .GetField("survivalSync", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, sync);

            return (controller, root, sync);
        }

        [Test]
        public void StripStaysCollapsedInCreativeEvenWithAPositiveHotbarSlotCount()
        {
            (HotbarStripController controller, VisualElement root, _) = CreateStripInCreativeWorld();
            VisualElement strip = root.Q<VisualElement>("bv-hotbar-strip");

            controller.BindForTest(new Inventory());
            controller.Refresh();

            Assert.That(new Inventory().HotbarSlotCount, Is.GreaterThan(0),
                "positive control: a fresh Inventory reports slots, so collapse must come from " +
                "the mode check and not from an empty inventory");
            Assert.That(strip.ClassListContains("hb-strip--hidden"), Is.True,
                "the strip must stay collapsed in Creative even though the bound inventory still " +
                "reports a positive HotbarSlotCount");
        }

        [Test]
        public void CyclingDoesNothingInCreative()
        {
            (HotbarStripController controller, _, _) = CreateStripInCreativeWorld();
            controller.BindForTest(new Inventory());

            int before = controller.SelectedSlotIndex;
            controller.SelectNext();

            Assert.That(controller.SelectedSlotIndex, Is.EqualTo(before),
                "cycling must be inert in Creative, not silently move a selection the collapsed " +
                "strip gives no feedback about");
        }
    }
}
