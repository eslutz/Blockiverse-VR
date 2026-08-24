using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI.Toolkit;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of SurvivalInventoryPanel (migration matrix row 15) plus the slice of
    // SurvivalHudController.BindValidationState that wired it: this controller resolves the
    // survival sync itself, binds its LocalInventory, and mirrors hotbar selection into
    // MultiplayerSurvivalSync.SetSelectedHotbarSlot — the exact SelectionChanged consumer the
    // uGUI HUD subscribed. Behaviour ported verbatim: paged 10-slot window, hotbar-click
    // select vs backpack-click swap-with-selected-hotbar-slot (a direct local mutation, not
    // host-routed — only the selection is mirrored to the sync), per-slot SlotRenderState
    // render-diff cache, the lazy static stack-count string cache, and the open/close and
    // select audio cues.
    [UiToolkitScreen(MenuActions.InventoryScreen, "Assets/Blockiverse/UI/Documents/InventoryScreen.uxml",
        1000, 760, UiToolkitPlacementProfile.Menu)]
    public sealed class InventoryScreenController : UiToolkitScreenController
    {
        // The document carries exactly ten slots — the same visible-window size the generated
        // uGUI panel got from its ten "Slot N" labels, so every paging clamp matches.
        public const int SlotElementCount = 10;

        // Table keys shared with the uGUI panel — the copy contract. Values must match
        // BlockiverseLocalization.Keys verbatim; duplicating the strings here keeps this
        // screen off the uGUI shim (screen controllers never call BlockiverseLocalization).
        static class Keys
        {
            public const string CommonEmpty = "ui.common.empty";
            public const string CommonStack = "ui.common.stack";
            public const string CommonStackCount = "ui.common.stack_count";
            public const string HotbarEmpty = "ui.status.inventory.hotbar_empty";
            public const string Hotbar = "ui.status.inventory.hotbar";
            public const string SlotsCount = "ui.status.inventory.slots_count";
            public const string SlotsRange = "ui.status.inventory.slots_range";
        }

        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.Default;

        // Lazy, not a field initializer, same constraint as the uGUI panel: the build reaches
        // into the localization table, and a static field initializer runs whenever the type
        // is first touched — including mid-deserialization, where the table lookup throws.
        static string[] cachedStackCounts;
        static string[] CachedStackCounts => cachedStackCounts ??= BuildCachedStackCounts();

        readonly Button[] slotButtons = new Button[SlotElementCount];
        readonly Label[] slotLabels = new Label[SlotElementCount];
        readonly VisualElement[] slotIcons = new VisualElement[SlotElementCount];
        Label hotbarLabel;
        Button previousPageButton;
        Button nextPageButton;
        Label pageLabel;
        Button closeButton;

        EventCallback<ClickEvent>[] slotClickCallbacks;
        EventCallback<ClickEvent> previousPageCallback;
        EventCallback<ClickEvent> nextPageCallback;
        EventCallback<ClickEvent> closeCallback;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;
        BlockiverseItemIconLibrary iconLibrary;
        MultiplayerSurvivalSync survivalSync;
        // The exact mirror subscription, stored so re-configuring against a different sync
        // instance detaches the previous handler instead of leaking it (same discipline as
        // SurvivalHudController.UnsubscribeSelectionChanged).
        Action<int> selectionMirrorHandler;

        Inventory inventory;
        ItemRegistry itemRegistry;
        int selectedHotbarSlotIndex;
        int firstVisibleSlotIndex;
        SlotRenderState[] slotRenderCache = Array.Empty<SlotRenderState>();
        string renderedHotbarText;

        struct SlotRenderState
        {
            public int SlotIndex;
            public ItemStack Stack;
            public bool UsesIcon;
            public bool IsValid;
        }

        public override string ScreenId => MenuActions.InventoryScreen;

        public Inventory BoundInventory => inventory;
        public int SelectedHotbarSlotIndex => selectedHotbarSlotIndex;
        public int FirstVisibleSlotIndex => firstVisibleSlotIndex;

        // Raised when the selected hotbar slot changes, so the survival runtime can mirror the
        // held tool/block for harvest and placement.
        public event Action<int> SelectionChanged;

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            IBlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void ConfigureIconLibrary(BlockiverseItemIconLibrary targetIconLibrary)
        {
            iconLibrary = targetIconLibrary;
            InvalidateRenderCache();
        }

        // Mirrors the HUD wiring: seed the sync's selected slot, subscribe the selection
        // mirror, and repaint on authoritative local-inventory changes (host snapshots on
        // clients, host-side command results, mode switches).
        public void ConfigureSurvivalSync(MultiplayerSurvivalSync sync)
        {
            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                if (selectionMirrorHandler != null)
                    SelectionChanged -= selectionMirrorHandler;
                selectionMirrorHandler = null;
            }

            survivalSync = sync;

            if (survivalSync != null)
            {
                survivalSync.SelectedHotbarSlotIndex = selectedHotbarSlotIndex;
                selectionMirrorHandler = survivalSync.SetSelectedHotbarSlot;
                SelectionChanged += selectionMirrorHandler;
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
            }
        }

        public void Bind(Inventory targetInventory, ItemRegistry registry = null, int targetSelectedHotbarSlotIndex = 0)
        {
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? DefaultItemRegistry;
            firstVisibleSlotIndex = ClampFirstVisibleSlot(firstVisibleSlotIndex);
            InvalidateRenderCache();
            SetSelectedHotbarSlotIndex(targetSelectedHotbarSlotIndex);
        }

        public void SetSelectedHotbarSlotIndex(int slotIndex)
        {
            if (inventory != null && !IsValidHotbarSlot(slotIndex, inventory.HotbarSlotCount))
                throw new ArgumentOutOfRangeException(nameof(slotIndex), "Selected hotbar slot must fit inside the inventory hotbar.");

            selectedHotbarSlotIndex = slotIndex;
            Refresh();
            SelectionChanged?.Invoke(selectedHotbarSlotIndex);
        }

        public void Refresh()
        {
            EnsureRenderCache(SlotElementCount);
            for (int i = 0; i < SlotElementCount; i++)
            {
                if (slotLabels[i] == null)
                    continue;

                int slotIndex = firstVisibleSlotIndex + i;
                ItemStack stack = GetSlotStack(slotIndex);
                bool hasIcon = TryGetSlotIcon(slotIndex, i, stack, out Sprite icon);
                SlotRenderState previous = slotRenderCache[i];
                if (previous.IsValid &&
                    previous.SlotIndex == slotIndex &&
                    previous.Stack.Equals(stack) &&
                    previous.UsesIcon == hasIcon)
                {
                    continue;
                }

                // Icon + count when the item has an icon; the text-only fallback keeps slots
                // readable for icons that don't exist (and for icon-less configurations).
                if (hasIcon)
                {
                    SetSlotIcon(i, icon);
                    SetTextIfChanged(slotLabels[i], StackCountText(stack.Count));
                }
                else
                {
                    SetSlotIcon(i, null);
                    SetTextIfChanged(slotLabels[i], FormatStack(stack, itemRegistry));
                }

                slotRenderCache[i] = new SlotRenderState
                {
                    SlotIndex = slotIndex,
                    Stack = stack,
                    UsesIcon = hasIcon,
                    IsValid = true,
                };
            }

            if (hotbarLabel != null)
            {
                string hotbarText = inventory == null || inventory.HotbarSlotCount == 0
                    ? UiText.Get(Keys.HotbarEmpty)
                    : UiText.Format(Keys.Hotbar, selectedHotbarSlotIndex + 1, inventory.HotbarSlotCount);
                if (!string.Equals(renderedHotbarText, hotbarText, StringComparison.Ordinal))
                {
                    hotbarLabel.text = hotbarText;
                    renderedHotbarText = hotbarText;
                }
            }

            RefreshPageControls();
        }

        public void ShowPreviousPage()
        {
            SetFirstVisibleSlotIndex(firstVisibleSlotIndex - SlotElementCount);
        }

        public void ShowNextPage()
        {
            SetFirstVisibleSlotIndex(firstVisibleSlotIndex + SlotElementCount);
        }

        // Public handler seam (also the click target): visibleIndex is the on-screen slot,
        // resolved against the current page exactly like the uGUI closure did at click time.
        public void ClickSlot(int visibleIndex)
        {
            HandleSlotClicked(firstVisibleSlotIndex + visibleIndex);
        }

        public void SimulateClose() => OnClosePressed();

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            for (int i = 0; i < SlotElementCount; i++)
            {
                string prefix = "bv-slot-" + (i + 1);
                slotButtons[i] = Require<Button>(root, prefix, ref allFound);
                slotIcons[i] = Require<VisualElement>(root, prefix + "-icon", ref allFound);
                slotLabels[i] = Require<Label>(root, prefix + "-label", ref allFound);
            }

            hotbarLabel = Require<Label>(root, "bv-hotbar-label", ref allFound);
            previousPageButton = Require<Button>(root, "bv-prev-page", ref allFound);
            nextPageButton = Require<Button>(root, "bv-next-page", ref allFound);
            pageLabel = Require<Label>(root, "bv-page-label", ref allFound);
            closeButton = Require<Button>(root, "bv-close", ref allFound);

            // A rebuild mid-session gets brand-new blank elements; the render cache keyed on
            // stack contents would skip repainting them.
            InvalidateRenderCache();
            if (inventory != null)
                Refresh();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            slotClickCallbacks = new EventCallback<ClickEvent>[SlotElementCount];
            for (int i = 0; i < SlotElementCount; i++)
            {
                int index = i;
                slotClickCallbacks[i] = _ => ClickSlot(index);
                slotButtons[i]?.RegisterCallback(slotClickCallbacks[i]);
            }

            previousPageCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); ShowPreviousPage(); };
            nextPageCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); ShowNextPage(); };
            // Click cue plus the InventoryClose hide cue — deliberately the same pairing as the
            // HUD's open buttons (UiSelect + InventoryOpen).
            closeCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); OnClosePressed(); };
            previousPageButton?.RegisterCallback(previousPageCallback);
            nextPageButton?.RegisterCallback(nextPageCallback);
            closeButton?.RegisterCallback(closeCallback);

            // Live language switching: the static stack-count cache and the per-slot render
            // cache both key off stack contents, not locale, so neither one notices a locale
            // change on its own — Refresh() would silently keep rendering the old language.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            for (int i = 0; i < SlotElementCount; i++)
            {
                if (slotClickCallbacks != null && slotClickCallbacks[i] != null)
                    slotButtons[i]?.UnregisterCallback(slotClickCallbacks[i]);
            }

            slotClickCallbacks = null;

            if (previousPageCallback != null)
                previousPageButton?.UnregisterCallback(previousPageCallback);
            if (nextPageCallback != null)
                nextPageButton?.UnregisterCallback(nextPageCallback);
            if (closeCallback != null)
                closeButton?.UnregisterCallback(closeCallback);
            previousPageCallback = null;
            nextPageCallback = null;
            closeCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            for (int i = 0; i < SlotElementCount; i++)
            {
                slotButtons[i] = null;
                slotIcons[i] = null;
                slotLabels[i] = null;
            }

            hotbarLabel = null;
            previousPageButton = null;
            nextPageButton = null;
            pageLabel = null;
            closeButton = null;
        }

        protected override void OnShown()
        {
            EnsureRuntimeBindings();
            Refresh();
            PlayFeedback(BlockiverseAudioCue.InventoryOpen);
        }

        protected override void OnHidden()
        {
            PlayFeedback(BlockiverseAudioCue.InventoryClose);
        }

        void OnDestroy()
        {
            if (survivalSync != null)
            {
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                if (selectionMirrorHandler != null)
                    SelectionChanged -= selectionMirrorHandler;
                selectionMirrorHandler = null;
            }
        }

        // The SurvivalHudController.BindValidationState slice this screen owns: discover the
        // sync and icon library at the routed-visibility boundary (never OnEnable — screens
        // hide by collapsing the root), fall back to a standalone inventory offline.
        void EnsureRuntimeBindings()
        {
            if (iconLibrary == null)
                iconLibrary = BlockiverseSceneLookup.Find<BlockiverseItemIconLibrary>(FindObjectsInactive.Include);

            if (survivalSync == null)
            {
                MultiplayerSurvivalSync sync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
                if (sync != null)
                    ConfigureSurvivalSync(sync);
            }

            if (inventory == null)
            {
                Bind(
                    survivalSync != null ? survivalSync.LocalInventory : new Inventory(DefaultItemRegistry),
                    DefaultItemRegistry,
                    selectedHotbarSlotIndex);
            }
        }

        void OnLocalInventoryChanged()
        {
            // The sync replaced its inventory instance (explicit Configure): rebind so the
            // slots render the authoritative inventory, keeping the current selection.
            if (survivalSync != null && !ReferenceEquals(inventory, survivalSync.LocalInventory))
                Bind(survivalSync.LocalInventory, itemRegistry, selectedHotbarSlotIndex);

            Refresh();
        }

        void OnSelectedLocaleChanged(Locale locale)
        {
            cachedStackCounts = null;
            InvalidateRenderCache();
            Refresh();
        }

        void OnClosePressed()
        {
            MenuController?.CloseInventoryScreen();
        }

        void HandleSlotClicked(int slotIndex)
        {
            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return;

            if (slotIndex < inventory.HotbarSlotCount)
            {
                SetSelectedHotbarSlotIndex(slotIndex);
                PlayFeedback(BlockiverseAudioCue.UiSelect);
                return;
            }

            if (inventory.HotbarSlotCount == 0)
                return;

            inventory.SwapSlots(selectedHotbarSlotIndex, slotIndex);
            Refresh();
            PlayFeedback(BlockiverseAudioCue.UiSelect);
        }

        void SetFirstVisibleSlotIndex(int slotIndex)
        {
            firstVisibleSlotIndex = ClampFirstVisibleSlot(slotIndex);
            Refresh();
        }

        void RefreshPageControls()
        {
            int slotCount = inventory != null ? inventory.SlotCount : SlotElementCount;
            int first = Math.Min(firstVisibleSlotIndex, Math.Max(0, slotCount - 1));
            int last = Math.Min(slotCount, first + SlotElementCount);

            if (pageLabel != null)
            {
                pageLabel.text = slotCount <= SlotElementCount
                    ? UiText.Format(Keys.SlotsCount, slotCount)
                    : UiText.Format(Keys.SlotsRange, first + 1, last, slotCount);
            }

            previousPageButton?.SetEnabled(firstVisibleSlotIndex > 0);
            nextPageButton?.SetEnabled(inventory != null && firstVisibleSlotIndex + SlotElementCount < inventory.SlotCount);
        }

        int ClampFirstVisibleSlot(int slotIndex)
        {
            if (inventory == null)
                return 0;

            int maxFirst = ((inventory.SlotCount - 1) / SlotElementCount) * SlotElementCount;
            return Math.Clamp(slotIndex, 0, maxFirst);
        }

        ItemStack GetSlotStack(int slotIndex)
        {
            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return ItemStack.Empty;

            return inventory.GetSlot(slotIndex);
        }

        bool TryGetSlotIcon(int slotIndex, int visibleIndex, ItemStack stack, out Sprite icon)
        {
            icon = null;

            if (iconLibrary == null || slotIcons[visibleIndex] == null)
                return false;

            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount || stack.IsEmpty)
                return false;

            return iconLibrary.TryGetIcon(stack.ItemId, out icon);
        }

        // Visibility rather than display so a vanished icon keeps its rect — the uGUI panel
        // toggled Image.enabled the same way.
        void SetSlotIcon(int visibleIndex, Sprite icon)
        {
            VisualElement iconElement = slotIcons[visibleIndex];
            if (iconElement == null)
                return;

            iconElement.style.backgroundImage = icon != null
                ? new StyleBackground(icon)
                : new StyleBackground(StyleKeyword.None);
            iconElement.style.visibility = icon != null ? Visibility.Visible : Visibility.Hidden;
        }

        static string FormatStack(ItemStack stack, ItemRegistry registry)
        {
            if (stack.IsEmpty)
                return UiText.Get(Keys.CommonEmpty);

            ItemDefinition definition = (registry ?? DefaultItemRegistry).Get(stack.ItemId);
            return UiText.Format(Keys.CommonStack, definition.Name, stack.Count);
        }

        static string StackCountText(int count) =>
            count >= 0 && count < CachedStackCounts.Length
                ? CachedStackCounts[count]
                : UiText.Format(Keys.CommonStackCount, count);

        static string[] BuildCachedStackCounts()
        {
            var values = new string[100];
            for (int i = 0; i < values.Length; i++)
                values[i] = UiText.Format(Keys.CommonStackCount, i);
            return values;
        }

        static void SetTextIfChanged(Label label, string text)
        {
            if (label != null && !string.Equals(label.text, text, StringComparison.Ordinal))
                label.text = text;
        }

        void EnsureRenderCache(int length)
        {
            if (slotRenderCache.Length == length)
                return;

            slotRenderCache = new SlotRenderState[length];
            InvalidateRenderCache();
        }

        void InvalidateRenderCache()
        {
            for (int i = 0; i < slotRenderCache.Length; i++)
                slotRenderCache[i].IsValid = false;
            renderedHotbarText = null;
        }

        static bool IsValidHotbarSlot(int slotIndex, int hotbarSlotCount)
        {
            if (hotbarSlotCount == 0)
                return slotIndex == 0;

            return slotIndex >= 0 && slotIndex < hotbarSlotCount;
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}
