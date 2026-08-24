using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.UI.Toolkit;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // Screen-level mirror of the SurvivalUiEditModeTests inventory oracle: the same slot
    // rendering, paging, hotbar select/swap and feedback behaviours observed through the UI
    // Toolkit screen. UIDocument builds no tree in EditMode, so the document is instantiated
    // directly and attached via AttachForTest; clicks are driven through the controller's
    // public handler seams (ClickSlot/ShowNextPage/SimulateClose) because ClickEvent dispatch
    // needs a live panel.
    public sealed class InventoryScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/InventoryScreen.uxml";

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

        InventoryScreenController CreateAttachedController(out VisualElement root)
        {
            var gameObject = new GameObject("Inventory Screen Under Test");
            objectsToDestroy.Add(gameObject);
            InventoryScreenController controller = gameObject.AddComponent<InventoryScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        InventoryScreenController CreateRoutedController(out BlockiverseMenuController menu)
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            menu.RegisterFrontend(host);

            InventoryScreenController controller = CreateAttachedController(out _);
            controller.ConfigureHost(host);
            return controller;
        }

        MultiplayerSurvivalSync CreateSurvivalSync()
        {
            var gameObject = new GameObject("Survival Sync Under Test");
            objectsToDestroy.Add(gameObject);
            MultiplayerSurvivalSync sync = gameObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, null);
            return sync;
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            GameObject gameObject = new("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        BlockiverseInteractionHaptics CreateHaptics()
        {
            GameObject gameObject = new("Interaction Haptics");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseInteractionHaptics>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }

        static string StackText(ItemRegistry itemRegistry, ItemId itemId, int count)
        {
            return $"{itemRegistry.Get(itemId).Name} x{count}";
        }

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            InventoryScreenController controller = CreateAttachedController(out _);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in InventoryScreen.uxml must resolve.");
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
                InventoryScreenController stray = strayObject.AddComponent<InventoryScreenController>();
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
        public void RendersSlotsAndSelectedHotbar()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 3, hotbarSlotCount: 2);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            inventory.SetSlot(2, new ItemStack(ItemId.ReedwoodDelver, 1));
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);

            controller.Bind(inventory, itemRegistry, targetSelectedHotbarSlotIndex: 1);

            Assert.That(root.Q<Label>("bv-slot-1-label").text,
                Is.EqualTo(StackText(itemRegistry, ItemId.BranchwoodLog, 12)));
            Assert.That(root.Q<Label>("bv-slot-2-label").text, Is.EqualTo(UiText.Get("ui.common.empty")));
            Assert.That(root.Q<Label>("bv-slot-3-label").text,
                Is.EqualTo(StackText(itemRegistry, ItemId.ReedwoodDelver, 1)));

            // Literal English pins the copy contract, not just internal consistency — a
            // do-nothing controller (or a broken table) cannot pass both assertions.
            Assert.That(root.Q<Label>("bv-hotbar-label").text, Is.EqualTo("Hotbar 2 / 2"));
            Assert.That(root.Q<Label>("bv-hotbar-label").text,
                Is.EqualTo(UiText.Format("ui.status.inventory.hotbar", 2, 2)));
        }

        [Test]
        public void PagesThroughAllDefaultInventorySlots()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(42, new ItemStack(ItemId.FieldBandage, 2));
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);

            controller.Bind(inventory, itemRegistry);

            Assert.That(root.Q<Label>("bv-page-label").text, Is.EqualTo("Slots 1-10 / 44"));
            Assert.That(root.Q<Button>("bv-prev-page").enabledSelf, Is.False);

            controller.ShowNextPage();
            controller.ShowNextPage();
            controller.ShowNextPage();
            controller.ShowNextPage();

            Assert.That(controller.FirstVisibleSlotIndex, Is.EqualTo(40));
            Assert.That(root.Q<Label>("bv-page-label").text, Is.EqualTo("Slots 41-44 / 44"));
            Assert.That(root.Q<Label>("bv-slot-3-label").text,
                Is.EqualTo(StackText(itemRegistry, ItemId.FieldBandage, 2)));
            Assert.That(root.Q<Button>("bv-next-page").enabledSelf, Is.False);

            // Clamp: paging past the end stays on the last page (uGUI ClampFirstVisibleSlot).
            controller.ShowNextPage();
            Assert.That(controller.FirstVisibleSlotIndex, Is.EqualTo(40));
        }

        [Test]
        public void SelectsTenthHotbarSlotFromFirstPage()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);

            controller.Bind(inventory, itemRegistry);
            controller.ClickSlot(9);

            Assert.That(controller.SelectedHotbarSlotIndex, Is.EqualTo(9));
            Assert.That(root.Q<Label>("bv-hotbar-label").text,
                Is.EqualTo(UiText.Format("ui.status.inventory.hotbar", 10, 10)));
        }

        [Test]
        public void SwapsPagedBackpackSlotIntoSelectedHotbarSlot()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));
            inventory.SetSlot(10, new ItemStack(ItemId.FieldBandage, 2));
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);

            controller.Bind(inventory, itemRegistry, targetSelectedHotbarSlotIndex: 0);
            controller.ShowNextPage();
            controller.ClickSlot(0);

            Assert.That(inventory.GetSlot(0), Is.EqualTo(new ItemStack(ItemId.FieldBandage, 2)));
            Assert.That(inventory.GetSlot(10), Is.EqualTo(new ItemStack(ItemId.BranchwoodLog, 1)));
            Assert.That(root.Q<Label>("bv-slot-1-label").text,
                Is.EqualTo(StackText(itemRegistry, ItemId.BranchwoodLog, 1)),
                "The visible slot must repaint the swapped-in stack.");
        }

        [Test]
        public void RenderCacheSkipsRewritesUntilTheSlotActuallyChanges()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(inventory, itemRegistry);

            Label slotLabel = root.Q<Label>("bv-slot-1-label");
            Label hotbarLabel = root.Q<Label>("bv-hotbar-label");
            string slotTextBefore = slotLabel.text;
            string hotbarTextBefore = hotbarLabel.text;

            controller.Refresh();

            // FormatStack allocates a fresh string every time it runs, so an identical
            // reference proves the render-diff cache skipped the write entirely.
            Assert.That(slotLabel.text, Is.SameAs(slotTextBefore),
                "An unchanged slot must not be reformatted or rewritten.");
            Assert.That(hotbarLabel.text, Is.SameAs(hotbarTextBefore),
                "An unchanged hotbar readout must not be rewritten.");

            // Positive control: a real change must break through the cache.
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 13));
            controller.Refresh();
            Assert.That(slotLabel.text, Is.EqualTo(StackText(itemRegistry, ItemId.BranchwoodLog, 13)));
            Assert.That(slotLabel.text, Is.Not.SameAs(slotTextBefore));
        }

        [Test]
        public void IconSlotsRenderSpriteAndCountAndTextSlotsFallBack()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry);
            inventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 12));
            inventory.SetSlot(1, new ItemStack(ItemId.FieldBandage, 2));
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);

            var libraryObject = new GameObject("Icon Library Under Test");
            objectsToDestroy.Add(libraryObject);
            BlockiverseItemIconLibrary library = libraryObject.AddComponent<BlockiverseItemIconLibrary>();
            var texture = new Texture2D(8, 8);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f));
            library.Configure(new[] { ItemId.BranchwoodLog.Value }, new[] { sprite });

            controller.ConfigureIconLibrary(library);
            controller.Bind(inventory, itemRegistry);

            VisualElement iconWithSprite = root.Q<VisualElement>("bv-slot-1-icon");
            Assert.That(iconWithSprite.style.backgroundImage.value.sprite, Is.SameAs(sprite));
            Assert.That(iconWithSprite.style.visibility.value, Is.EqualTo(Visibility.Visible));
            Assert.That(root.Q<Label>("bv-slot-1-label").text,
                Is.EqualTo(UiText.Format("ui.common.stack_count", 12)),
                "An icon slot renders only the count.");
            Assert.That(root.Q<Label>("bv-slot-1-label").text, Is.EqualTo("x12"));

            VisualElement iconWithoutSprite = root.Q<VisualElement>("bv-slot-2-icon");
            Assert.That(iconWithoutSprite.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(root.Q<Label>("bv-slot-2-label").text,
                Is.EqualTo(StackText(itemRegistry, ItemId.FieldBandage, 2)),
                "A slot without an icon falls back to the full stack text.");
        }

        [Test]
        public void SelectionMirrorsIntoTheSurvivalSyncEquip()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            var held = new ItemStack(ItemId.FieldBandage, 2);
            sync.LocalInventory.SetSlot(3, held);
            InventoryScreenController controller = CreateAttachedController(out _);

            controller.ConfigureSurvivalSync(sync);
            controller.Bind(sync.LocalInventory);
            controller.ClickSlot(3);

            Assert.That(controller.SelectedHotbarSlotIndex, Is.EqualTo(3));
            Assert.That(sync.SelectedHotbarSlotIndex, Is.EqualTo(3),
                "Hotbar selection must mirror into MultiplayerSurvivalSync.SetSelectedHotbarSlot.");
            Assert.That(sync.EquippedItem, Is.EqualTo(held),
                "The mirrored selection drives the equipped item for harvest/placement.");
        }

        [Test]
        public void RebindsWhenTheSyncReplacesItsLocalInventoryInstance()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            InventoryScreenController controller = CreateAttachedController(out VisualElement root);
            controller.ConfigureSurvivalSync(sync);
            controller.Bind(sync.LocalInventory);

            Inventory first = sync.LocalInventory;
            first.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 3));
            controller.Refresh();
            Assert.That(root.Q<Label>("bv-slot-1-label").text, Does.Contain("x3"));

            // Re-Configure replaces the sync's inventory instance and raises
            // LocalInventoryChanged — the screen must rebind, not keep painting the corpse.
            sync.Configure(null, null, null);

            Assert.That(controller.BoundInventory, Is.SameAs(sync.LocalInventory));
            Assert.That(controller.BoundInventory, Is.Not.SameAs(first));
            Assert.That(root.Q<Label>("bv-slot-1-label").text, Is.EqualTo(UiText.Get("ui.common.empty")),
                "The repaint must come from the replacement inventory.");
        }

        [Test]
        public void SelectionPlaysUiSelectAndHapticTick()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            var inventory = new Inventory(itemRegistry, slotCount: 4, hotbarSlotCount: 3);
            InventoryScreenController controller = CreateAttachedController(out _);
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();
            int uiTicks = 0;

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);
            haptics.UiTickRequested += () => uiTicks++;

            controller.ConfigureFeedback(audioCuePlayer, haptics);
            controller.Bind(inventory, itemRegistry);
            controller.ClickSlot(1);

            Assert.That(controller.SelectedHotbarSlotIndex, Is.EqualTo(1));
            Assert.That(playedCues, Is.EqualTo(new[] { BlockiverseAudioCue.UiSelect }));
            Assert.That(uiTicks, Is.EqualTo(1));
        }

        [Test]
        public void RoutedVisibilityPlaysInventoryOpenAndCloseCues()
        {
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            InventoryScreenController controller = CreateAttachedController(out _);
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryOpen, CreateClip("inventory_open"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.InventoryClose, CreateClip("inventory_close"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);

            controller.ConfigureFeedback(audioCuePlayer, haptics);
            controller.Bind(new Inventory(itemRegistry), itemRegistry);

            controller.SetVisible(true, true);
            controller.SetVisible(false, false);

            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.InventoryOpen,
                BlockiverseAudioCue.InventoryClose
            }));
        }

        [Test]
        public void CloseRoutesThroughTheMenuControllerVerb()
        {
            InventoryScreenController controller = CreateRoutedController(out BlockiverseMenuController menu);

            menu.OpenInventoryScreen();
            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.InventoryScreen));

            controller.SimulateClose();

            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.Not.EqualTo(MenuActions.InventoryScreen),
                "Close must pop the inventory route via MenuController.CloseInventoryScreen.");
        }
    }

    // Screen-level tests for the shared-crate flow. There is no uGUI EditMode oracle for the
    // crate panel (its coverage lives in the multiplayer PlayMode suite), so these pin the
    // ported behaviour against the real MultiplayerSurvivalSync host path — the same seam the
    // PlayMode tests submit through, minus the network hop.
    public sealed class CrateScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/CrateScreen.uxml";

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

        CrateScreenController CreateAttachedController(out VisualElement root)
        {
            var gameObject = new GameObject("Crate Screen Under Test");
            objectsToDestroy.Add(gameObject);
            CrateScreenController controller = gameObject.AddComponent<CrateScreenController>();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, $"Document failed to load: {DocumentPath}");

            root = tree.Instantiate();
            controller.AttachForTest(root);
            return controller;
        }

        MultiplayerSurvivalSync CreateSurvivalSync()
        {
            var gameObject = new GameObject("Survival Sync Under Test");
            objectsToDestroy.Add(gameObject);
            MultiplayerSurvivalSync sync = gameObject.AddComponent<MultiplayerSurvivalSync>();
            sync.Configure(null, null, null);
            return sync;
        }

        BlockiverseAudioCuePlayer CreateCuePlayer()
        {
            GameObject gameObject = new("Audio Cue Player");
            objectsToDestroy.Add(gameObject);
            gameObject.AddComponent<AudioSource>();
            return gameObject.AddComponent<BlockiverseAudioCuePlayer>();
        }

        BlockiverseInteractionHaptics CreateHaptics()
        {
            GameObject gameObject = new("Interaction Haptics");
            objectsToDestroy.Add(gameObject);
            return gameObject.AddComponent<BlockiverseInteractionHaptics>();
        }

        static AudioClip CreateClip(string name)
        {
            return AudioClip.Create(name, 16, 1, 44100, false);
        }

        static string StackText(ItemId itemId, int count)
        {
            return $"{ItemRegistry.Default.Get(itemId).Name} x{count}";
        }

        [Test]
        public void AttachBindsEveryElementAndAnEmptyTreeRefusesToBind()
        {
            CrateScreenController controller = CreateAttachedController(out VisualElement root);

            Assert.That(controller.IsBound, Is.True,
                "Every named element in CrateScreen.uxml must resolve.");
            Assert.That(controller.CallbackRegistrationBalance, Is.EqualTo(1));

            // The title and deposit labels render through UiText against requested keys, so
            // asserting the resolution keeps this test tracking the central table addition.
            Assert.That(root.Q<Label>("bv-title").text, Is.EqualTo(UiText.Get("ui.generated.crate.title")));
            Assert.That(root.Q<Label>("bv-title").text, Is.Not.Empty,
                "A do-nothing controller would leave the title blank.");
            Assert.That(root.Q<Button>("bv-deposit").text, Is.EqualTo(UiText.Get("ui.generated.crate.deposit")));
            Assert.That(root.Q<Button>("bv-deposit").text, Is.Not.Empty);

            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var strayObject = new GameObject("Empty Tree Control");
                objectsToDestroy.Add(strayObject);
                CrateScreenController stray = strayObject.AddComponent<CrateScreenController>();
                stray.AttachForTest(new VisualElement());
                Assert.That(stray.IsBound, Is.False);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [Test]
        public void BindSetsSharedOrOfflineStatusAndPaintsEmptySlots()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            CrateScreenController controller = CreateAttachedController(out VisualElement root);

            controller.Bind(sync);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo("Shared crate"));
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(UiText.Get("ui.status.crate.shared")));
            Assert.That(root.Q<Button>("bv-crate-slot-1").text, Is.EqualTo(UiText.Get("ui.common.empty")));

            controller.Bind(null);
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(UiText.Get("ui.status.crate.offline")));
        }

        [Test]
        public void DepositHeldMovesTheWholeStackAndReportsSuccess()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            var held = new ItemStack(ItemId.BranchwoodLog, 5);
            sync.LocalInventory.SetSlot(0, held);
            sync.SetSelectedHotbarSlot(0);
            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);
            int crateChangedCount = 0;
            controller.CrateChanged += () => crateChangedCount++;

            SurvivalCommandResult result = controller.DepositHeld();

            Assert.That(result.Accepted, Is.True, result.FailureReason.ToString());
            Assert.That(sync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(5));
            Assert.That(sync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(0));
            Assert.That(crateChangedCount, Is.EqualTo(1));

            Label status = root.Q<Label>("bv-status");
            Assert.That(status.text, Is.EqualTo(UiText.Format(
                "ui.status.crate.deposited", StackText(ItemId.BranchwoodLog, 5))));
            Assert.That(status.text, Does.Contain(StackText(ItemId.BranchwoodLog, 5)));
            Assert.That(status.ClassListContains("hs-status--confirmed"), Is.True,
                "An accepted transfer carries the confirmed signal alongside the message.");
            Assert.That(root.Q<Button>("bv-crate-slot-1").text,
                Is.EqualTo(StackText(ItemId.BranchwoodLog, 5)),
                "The crate slot must repaint the deposited stack.");
        }

        [Test]
        public void DepositWithNothingHeldRefusesWithoutTouchingTheCrate()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);
            int crateChangedCount = 0;
            controller.CrateChanged += () => crateChangedCount++;

            SurvivalCommandResult result = controller.DepositHeld();

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.InvalidTransfer));
            Assert.That(crateChangedCount, Is.EqualTo(0));
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(UiText.Get("ui.status.crate.nothing_held")));
        }

        [Test]
        public void WithdrawReturnsTheStackAndEmptySlotsAreRefused()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            sync.SharedCrateInventory.SetSlot(0, new ItemStack(ItemId.FieldBandage, 2));
            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);
            int crateChangedCount = 0;
            controller.CrateChanged += () => crateChangedCount++;

            SurvivalCommandResult withdraw = controller.WithdrawSlot(0);

            Assert.That(withdraw.Accepted, Is.True, withdraw.FailureReason.ToString());
            Assert.That(sync.LocalInventory.CountOf(ItemId.FieldBandage), Is.EqualTo(2));
            Assert.That(sync.SharedCrateInventory.CountOf(ItemId.FieldBandage), Is.EqualTo(0));
            Assert.That(crateChangedCount, Is.EqualTo(1));
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(UiText.Format(
                "ui.status.crate.withdrew", StackText(ItemId.FieldBandage, 2))));
            Assert.That(root.Q<Button>("bv-crate-slot-1").text, Is.EqualTo(UiText.Get("ui.common.empty")),
                "The emptied slot must repaint.");

            SurvivalCommandResult emptyWithdraw = controller.WithdrawSlot(1);

            Assert.That(emptyWithdraw.Accepted, Is.False);
            Assert.That(emptyWithdraw.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.SharedCrateEmpty));
            Assert.That(crateChangedCount, Is.EqualTo(1), "A refused withdraw must not raise CrateChanged.");
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(UiText.Get("ui.status.crate.empty_slot")));

            // Out-of-range indices reject silently, exactly like the uGUI panel.
            string statusBefore = root.Q<Label>("bv-status").text;
            SurvivalCommandResult outOfRange = controller.WithdrawSlot(99);
            Assert.That(outOfRange.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.InvalidTransfer));
            Assert.That(root.Q<Label>("bv-status").text, Is.EqualTo(statusBefore));
        }

        [Test]
        public void HostRejectedDepositShowsRejectionAndRaisesNoCrateChanged()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            // Fill every crate slot with a different item: zero capacity for the deposit, so
            // the host path rejects with InventoryFull — a real host rejection, not a local
            // pre-check, which is exactly the path matrix §4 item 2 protects.
            for (int i = 0; i < sync.SharedCrateInventory.SlotCount; i++)
                sync.SharedCrateInventory.SetSlot(i, new ItemStack(ItemId.FieldBandage, 1));
            sync.LocalInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 5));
            sync.SetSelectedHotbarSlot(0);
            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);
            int crateChangedCount = 0;
            controller.CrateChanged += () => crateChangedCount++;

            SurvivalCommandResult result = controller.DepositHeld();

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.InventoryFull));
            Assert.That(crateChangedCount, Is.EqualTo(0),
                "CrateChanged fires ONLY on Accepted — never on a rejection.");
            Assert.That(sync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(5),
                "A rejected deposit must not mutate the player inventory.");

            Label status = root.Q<Label>("bv-status");
            Assert.That(status.text, Is.EqualTo(UiText.Get("ui.status.crate.transfer_rejected")));
            Assert.That(status.ClassListContains("hs-status--rejected"), Is.True);
            Assert.That(status.ClassListContains("hs-status--confirmed"), Is.False);
        }

        [Test]
        public void TransfersPlayUiSelectOnSuccessAndUiCancelOnRefusal()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            sync.LocalInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 5));
            sync.SetSelectedHotbarSlot(0);
            CrateScreenController controller = CreateAttachedController(out _);
            BlockiverseAudioCuePlayer audioCuePlayer = CreateCuePlayer();
            BlockiverseInteractionHaptics haptics = CreateHaptics();
            var playedCues = new List<BlockiverseAudioCue>();

            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiSelect, CreateClip("ui_select"));
            audioCuePlayer.ConfigureClip(BlockiverseAudioCue.UiCancel, CreateClip("ui_cancel"));
            audioCuePlayer.CuePlayed += (cue, _) => playedCues.Add(cue);

            controller.ConfigureFeedback(audioCuePlayer, haptics);
            controller.Bind(sync);

            controller.DepositHeld();
            controller.DepositHeld();

            // First deposit succeeds (UiSelect); the hand is then empty, so the second is
            // refused before submission (UiCancel).
            Assert.That(playedCues, Is.EqualTo(new[]
            {
                BlockiverseAudioCue.UiSelect,
                BlockiverseAudioCue.UiCancel
            }));
        }

        [Test]
        public void SharedCrateSnapshotsRepaintTheSlots()
        {
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);

            sync.SharedCrateInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 7));
            controller.Refresh();
            Assert.That(root.Q<Button>("bv-crate-slot-1").text, Is.EqualTo(StackText(ItemId.BranchwoodLog, 7)));

            // Re-Configure replaces the crate mirror with a fresh empty one and raises
            // SharedCrateChanged — the same signal a client receives with a host snapshot.
            // Only the screen's subscription can flip the painted slot back to Empty here;
            // a controller that never subscribed would keep showing the stale stack.
            sync.Configure(null, null, null);

            Assert.That(root.Q<Button>("bv-crate-slot-1").text, Is.EqualTo(UiText.Get("ui.common.empty")),
                "SharedCrateChanged must repaint the slots from the current mirror.");
        }

        [Test]
        public void CloseRoutesThroughTheMenuControllerVerb()
        {
            var hostObject = new GameObject("Toolkit Host Under Test");
            objectsToDestroy.Add(hostObject);
            UiToolkitMenuHost host = hostObject.AddComponent<UiToolkitMenuHost>();
            BlockiverseMenuController menu = hostObject.AddComponent<BlockiverseMenuController>();
            host.Configure(menu);
            menu.RegisterFrontend(host);

            CrateScreenController controller = CreateAttachedController(out _);
            controller.ConfigureHost(host);

            menu.Router.PushScreen(new ScreenRoute(MenuActions.StationCrateScreen));
            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.StationCrateScreen));

            controller.SimulateClose();

            Assert.That(menu.Router.ActiveScreen.ScreenId, Is.Not.EqualTo(MenuActions.StationCrateScreen),
                "Close must pop the crate route via MenuController.CloseStationCrateScreen.");
        }

        [Test]
        public void ElementCountMatchesTheAuthoritativeSharedCrateSlotCount()
        {
            // The regression this guards: CrateSlotElementCount was hard-coded to 4 while the
            // real shared crate (MultiplayerSurvivalSync's authoritative SharedCrateInventory)
            // has always had 12. Deposits into slots 4-11 were stored, valid, and permanently
            // unreachable through the only crate UI (Codex review, PR #344). Tying the constant
            // to the REAL inventory's SlotCount, rather than to a second hard-coded number, means
            // this cannot silently drift out of sync again in either direction.
            MultiplayerSurvivalSync sync = CreateSurvivalSync();

            Assert.That(CrateScreenController.CrateSlotElementCount,
                Is.EqualTo(sync.SharedCrateInventory.SlotCount));
        }

        [Test]
        public void EveryDeclaredSlotButtonExistsInTheDocumentAndIsWired()
        {
            // Require<Button> fails allFound for any missing element, so this is what would have
            // caught the mismatch immediately: CrateSlotElementCount at 12 against a UXML that
            // still only declared bv-crate-slot-1..4 would fail attachment outright, loudly,
            // rather than leaving slots quietly unreachable.
            CrateScreenController controller = CreateAttachedController(out VisualElement root);

            for (int i = 1; i <= CrateScreenController.CrateSlotElementCount; i++)
            {
                Assert.That(root.Q<Button>("bv-crate-slot-" + i), Is.Not.Null,
                    $"bv-crate-slot-{i} is missing from CrateScreen.uxml.");
            }

            Assert.That(controller.IsBound, Is.True);
        }

        [Test]
        public void AStackInTheLastSlotIsVisibleAndWithdrawable()
        {
            // The exact failure Codex described, reproduced end to end: something lands past
            // slot 4 and the player needs to get it back. Slot index 11 is the crate's LAST slot
            // (12 slots, zero-based) -- the one furthest from ever being reachable under the old
            // 4-element limit.
            MultiplayerSurvivalSync sync = CreateSurvivalSync();
            var stack = new ItemStack(ItemId.BranchwoodLog, 3);
            sync.SharedCrateInventory.SetSlot(11, stack);

            CrateScreenController controller = CreateAttachedController(out VisualElement root);
            controller.Bind(sync);

            Assert.That(root.Q<Button>("bv-crate-slot-12").text, Is.EqualTo(StackText(ItemId.BranchwoodLog, 3)),
                "Slot 12 must render the stack deposited there.");

            SurvivalCommandResult result = controller.WithdrawSlot(11);

            Assert.That(result.Accepted, Is.True, result.FailureReason.ToString());
            Assert.That(sync.SharedCrateInventory.GetSlot(11).IsEmpty, Is.True);
            Assert.That(sync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(3));
        }
    }
}
