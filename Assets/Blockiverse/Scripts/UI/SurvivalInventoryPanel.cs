using System;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalInventoryPanel : MonoBehaviour
    {
        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.Default;
        static readonly string[] CachedStackCounts = BuildCachedStackCounts();

        [SerializeField] Button[] slotButtons;
        [SerializeField] TMP_Text[] slotLabels;
        [SerializeField] Image[] slotIcons;
        [SerializeField] BlockiverseItemIconLibrary iconLibrary;
        [SerializeField] TMP_Text selectedHotbarLabel;
        [SerializeField] Button previousPageButton;
        [SerializeField] Button nextPageButton;
        [SerializeField] TMP_Text pageLabel;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        Inventory inventory;
        ItemRegistry itemRegistry;
        int selectedHotbarSlotIndex;
        int firstVisibleSlotIndex;
        bool enableFeedbackReady;
        SlotRenderState[] slotRenderCache = Array.Empty<SlotRenderState>();
        string renderedHotbarText;

        struct SlotRenderState
        {
            public int SlotIndex;
            public ItemStack Stack;
            public bool UsesIcon;
            public bool IsValid;
        }

        public int SelectedHotbarSlotIndex => selectedHotbarSlotIndex;
        public int FirstVisibleSlotIndex => firstVisibleSlotIndex;

        // Raised when the selected hotbar slot changes, so the survival runtime can mirror the held
        // tool/block for harvest and placement.
        public event Action<int> SelectionChanged;

        public void Configure(TMP_Text[] targetSlotLabels, TMP_Text targetSelectedHotbarLabel)
        {
            Configure(null, targetSlotLabels, targetSelectedHotbarLabel);
        }

        public void Configure(
            Button[] targetSlotButtons,
            TMP_Text[] targetSlotLabels,
            TMP_Text targetSelectedHotbarLabel,
            Image[] targetSlotIcons = null,
            BlockiverseItemIconLibrary targetIconLibrary = null,
            Button targetPreviousPageButton = null,
            Button targetNextPageButton = null,
            TMP_Text targetPageLabel = null)
        {
            slotLabels = targetSlotLabels ?? Array.Empty<TMP_Text>();
            slotButtons = targetSlotButtons ?? Array.Empty<Button>();
            slotIcons = targetSlotIcons ?? Array.Empty<Image>();
            iconLibrary = targetIconLibrary;
            selectedHotbarLabel = targetSelectedHotbarLabel;
            previousPageButton = targetPreviousPageButton;
            nextPageButton = targetNextPageButton;
            pageLabel = targetPageLabel;
            InvalidateRenderCache();
            WireSlotButtons();
            WirePageButtons();
            Refresh();
        }

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Bind(Inventory targetInventory, ItemRegistry registry = null, int selectedHotbarSlotIndex = 0)
        {
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? DefaultItemRegistry;
            firstVisibleSlotIndex = ClampFirstVisibleSlot(firstVisibleSlotIndex);
            InvalidateRenderCache();
            SetSelectedHotbarSlotIndex(selectedHotbarSlotIndex);
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
            if (slotLabels != null)
            {
                EnsureRenderCache(slotLabels.Length);
                for (int i = 0; i < slotLabels.Length; i++)
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
            }

            if (selectedHotbarLabel != null)
            {
                string hotbarText = inventory == null || inventory.HotbarSlotCount == 0
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.InventoryHotbarEmpty)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.InventoryHotbar,
                        selectedHotbarSlotIndex + 1,
                        inventory.HotbarSlotCount);
                if (!string.Equals(renderedHotbarText, hotbarText, StringComparison.Ordinal))
                {
                    selectedHotbarLabel.text = hotbarText;
                    renderedHotbarText = hotbarText;
                }
            }

            RefreshPageControls();
        }

        void Awake()
        {
            WireSlotButtons();
            WirePageButtons();
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

            if (iconLibrary == null || slotIcons == null || visibleIndex >= slotIcons.Length || slotIcons[visibleIndex] == null)
                return false;

            if (inventory == null || slotIndex < 0 || slotIndex >= inventory.SlotCount || stack.IsEmpty)
                return false;

            return iconLibrary.TryGetIcon(stack.ItemId, out icon);
        }

        void SetSlotIcon(int visibleIndex, Sprite icon)
        {
            if (slotIcons == null || visibleIndex >= slotIcons.Length || slotIcons[visibleIndex] == null)
                return;

            slotIcons[visibleIndex].sprite = icon;
            slotIcons[visibleIndex].enabled = icon != null;
        }

        static string FormatStack(ItemStack stack, ItemRegistry registry)
        {
            if (stack.IsEmpty)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty);

            ItemDefinition definition = (registry ?? DefaultItemRegistry).Get(stack.ItemId);
            return BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStack, definition.Name, stack.Count);
        }

        static string StackCountText(int count) =>
            count >= 0 && count < CachedStackCounts.Length
                ? CachedStackCounts[count]
                : BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStackCount, count);

        static string[] BuildCachedStackCounts()
        {
            var values = new string[100];
            for (int i = 0; i < values.Length; i++)
                values[i] = BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStackCount, i);
            return values;
        }

        static void SetTextIfChanged(TMP_Text label, string text)
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

        void WireSlotButtons()
        {
            if (slotButtons == null)
                return;

            for (int index = 0; index < slotButtons.Length; index++)
            {
                Button button = slotButtons[index];

                if (button == null)
                    continue;

                int slotIndex = index;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    HandleSlotClicked(firstVisibleSlotIndex + slotIndex);
                });
            }
        }

        void WirePageButtons()
        {
            if (previousPageButton != null)
            {
                previousPageButton.onClick.RemoveAllListeners();
                previousPageButton.onClick.AddListener(ShowPreviousPage);
            }

            if (nextPageButton != null)
            {
                nextPageButton.onClick.RemoveAllListeners();
                nextPageButton.onClick.AddListener(ShowNextPage);
            }
        }

        public void ShowPreviousPage()
        {
            SetFirstVisibleSlotIndex(firstVisibleSlotIndex - VisibleSlotCount);
        }

        public void ShowNextPage()
        {
            SetFirstVisibleSlotIndex(firstVisibleSlotIndex + VisibleSlotCount);
        }

        void SetFirstVisibleSlotIndex(int slotIndex)
        {
            firstVisibleSlotIndex = ClampFirstVisibleSlot(slotIndex);
            Refresh();
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

        void RefreshPageControls()
        {
            int visibleSlotCount = VisibleSlotCount;
            int slotCount = inventory != null ? inventory.SlotCount : visibleSlotCount;
            int first = visibleSlotCount == 0 ? 0 : Math.Min(firstVisibleSlotIndex, Math.Max(0, slotCount - 1));
            int last = visibleSlotCount == 0 ? 0 : Math.Min(slotCount, first + visibleSlotCount);

            if (pageLabel != null)
            {
                pageLabel.text = slotCount <= visibleSlotCount || visibleSlotCount == 0
                    ? BlockiverseLocalization.Format(BlockiverseLocalization.Keys.InventorySlotsCount, slotCount)
                    : BlockiverseLocalization.Format(
                        BlockiverseLocalization.Keys.InventorySlotsRange,
                        first + 1,
                        last,
                        slotCount);
            }

            if (previousPageButton != null)
                previousPageButton.interactable = firstVisibleSlotIndex > 0;

            if (nextPageButton != null)
                nextPageButton.interactable = inventory != null && firstVisibleSlotIndex + visibleSlotCount < inventory.SlotCount;
        }

        int ClampFirstVisibleSlot(int slotIndex)
        {
            int visibleSlotCount = VisibleSlotCount;
            if (inventory == null || visibleSlotCount <= 0)
                return 0;

            int maxFirst = ((inventory.SlotCount - 1) / visibleSlotCount) * visibleSlotCount;
            return Math.Clamp(slotIndex, 0, maxFirst);
        }

        int VisibleSlotCount => slotLabels != null ? slotLabels.Length : 0;

        void Start()
        {
            enableFeedbackReady = true;
        }

        void OnEnable()
        {
            if (enableFeedbackReady)
                PlayFeedback(BlockiverseAudioCue.InventoryOpen);
        }

        void OnDisable()
        {
            if (enableFeedbackReady)
                PlayFeedback(BlockiverseAudioCue.InventoryClose);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}
