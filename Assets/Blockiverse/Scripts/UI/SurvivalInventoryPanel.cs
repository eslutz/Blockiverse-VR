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
        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.CreateDefault();

        [SerializeField] Button[] slotButtons;
        [SerializeField] TMP_Text[] slotLabels;
        [SerializeField] TMP_Text selectedHotbarLabel;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        Inventory inventory;
        ItemRegistry itemRegistry;
        int selectedHotbarSlotIndex;
        bool enableFeedbackReady;

        public int SelectedHotbarSlotIndex => selectedHotbarSlotIndex;

        public void Configure(TMP_Text[] targetSlotLabels, TMP_Text targetSelectedHotbarLabel)
        {
            Configure(null, targetSlotLabels, targetSelectedHotbarLabel);
        }

        public void Configure(Button[] targetSlotButtons, TMP_Text[] targetSlotLabels, TMP_Text targetSelectedHotbarLabel)
        {
            slotLabels = targetSlotLabels ?? Array.Empty<TMP_Text>();
            slotButtons = targetSlotButtons ?? Array.Empty<Button>();
            selectedHotbarLabel = targetSelectedHotbarLabel;
            WireSlotButtons();
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
                    {
                        SetSelectedHotbarSlotIndex(slotIndex);
                        PlayFeedback(BlockiverseAudioCue.UiSelect);
                    }
                });
            }
        }

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
            DiscoverFeedback();
            audioCuePlayer?.PlayCue(cue);
            interactionHaptics?.PlayUiTick();
        }

        void DiscoverFeedback()
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
                interactionHaptics = FindFirstObjectByType<BlockiverseInteractionHaptics>();
        }
    }
}
