using System;
using Blockiverse.Survival;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    public sealed class SurvivalInventoryPanel : MonoBehaviour
    {
        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.CreateDefault();

        [SerializeField] Button[] slotButtons;
        [SerializeField] Text[] slotLabels;
        [SerializeField] Text selectedHotbarLabel;

        Inventory inventory;
        ItemRegistry itemRegistry;
        int selectedHotbarSlotIndex;

        public int SelectedHotbarSlotIndex => selectedHotbarSlotIndex;

        public void Configure(Text[] targetSlotLabels, Text targetSelectedHotbarLabel)
        {
            Configure(null, targetSlotLabels, targetSelectedHotbarLabel);
        }

        public void Configure(Button[] targetSlotButtons, Text[] targetSlotLabels, Text targetSelectedHotbarLabel)
        {
            slotLabels = targetSlotLabels ?? Array.Empty<Text>();
            slotButtons = targetSlotButtons ?? Array.Empty<Button>();
            selectedHotbarLabel = targetSelectedHotbarLabel;
            WireSlotButtons();
            Refresh();
        }

        public void Bind(Inventory targetInventory, ItemRegistry registry = null, int selectedHotbarSlotIndex = 0)
        {
            inventory = targetInventory ?? throw new ArgumentNullException(nameof(targetInventory));
            itemRegistry = registry ?? DefaultItemRegistry;
            SetSelectedHotbarSlotIndex(selectedHotbarSlotIndex);
        }

        public void SetSelectedHotbarSlotIndex(int slotIndex)
        {
            if (inventory != null && !IsValidHotbarSlot(slotIndex, inventory.HotbarSlotCount))
                throw new ArgumentOutOfRangeException(nameof(slotIndex), "Selected hotbar slot must fit inside the inventory hotbar.");

            selectedHotbarSlotIndex = slotIndex;
            Refresh();
        }

        public void Refresh()
        {
            if (slotLabels != null)
            {
                for (int i = 0; i < slotLabels.Length; i++)
                {
                    if (slotLabels[i] == null)
                        continue;

                    slotLabels[i].text = FormatSlot(i);
                }
            }

            if (selectedHotbarLabel != null)
            {
                selectedHotbarLabel.text = inventory == null || inventory.HotbarSlotCount == 0
                    ? "Hotbar -"
                    : $"Hotbar {selectedHotbarSlotIndex + 1} / {inventory.HotbarSlotCount}";
            }
        }

        void Awake()
        {
            WireSlotButtons();
        }

        string FormatSlot(int slotIndex)
        {
            if (inventory == null)
                return string.Empty;

            if (slotIndex < 0 || slotIndex >= inventory.SlotCount)
                return string.Empty;

            return FormatStack(inventory.GetSlot(slotIndex), itemRegistry);
        }

        static string FormatStack(ItemStack stack, ItemRegistry registry)
        {
            if (stack.IsEmpty)
                return "Empty";

            ItemDefinition definition = (registry ?? DefaultItemRegistry).Get(stack.ItemId);
            return $"{definition.Name} x{stack.Count}";
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
                    if (inventory == null || slotIndex < inventory.HotbarSlotCount)
                        SetSelectedHotbarSlotIndex(slotIndex);
                });
            }
        }
    }
}
