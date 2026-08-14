using System;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // Shared co-op crate UI: shows the shared crate contents, withdraws a clicked slot into the player
    // inventory, and deposits the player's selected hotbar item. All transfers go through the
    // host-authoritative survival sync (TrySubmitCrateDeposit/Withdraw) so they are validated and
    // mirrored to every client — the crate is never mutated locally on a client.
    public sealed class SurvivalCratePanel : MonoBehaviour
    {
        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.Default;

        [SerializeField] Button[] slotButtons;
        [SerializeField] TMP_Text[] slotLabels;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Button depositButton;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        ItemRegistry itemRegistry;

        // Raised after a successful deposit/withdraw so the HUD can refresh the other panels.
        public event Action CrateChanged;

        public void Configure(Button[] targetSlotButtons, TMP_Text[] targetSlotLabels, TMP_Text targetStatusLabel, Button targetDepositButton)
        {
            slotButtons = targetSlotButtons ?? Array.Empty<Button>();
            slotLabels = targetSlotLabels ?? Array.Empty<TMP_Text>();
            statusLabel = targetStatusLabel;
            depositButton = targetDepositButton;
            WireButtons();
            Refresh();
        }

        public void ConfigureFeedback(BlockiverseAudioCuePlayer targetAudioCuePlayer, BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Bind(MultiplayerSurvivalSync sync, ItemRegistry registry = null)
        {
            survivalSync = sync;
            itemRegistry = registry ?? DefaultItemRegistry;
            SetStatus(sync != null
                ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateShared)
                : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline));
            Refresh();
        }

        // Deposits the player's currently selected hotbar item (whole stack) into the shared crate.
        public SurvivalCommandResult DepositHeld()
        {
            if (survivalSync == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline));
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateNothingHeld));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateDeposit(held.ItemId, held.Count, out bool sentToHost);
            ReportTransfer(
                result,
                sentToHost,
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CrateDeposited, FormatStack(held)));
            return result;
        }

        // Withdraws the crate stack at the given slot index back to the player inventory.
        public SurvivalCommandResult WithdrawSlot(int slotIndex)
        {
            if (survivalSync == null)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateOffline));
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);
            }

            Inventory crate = survivalSync.SharedCrateInventory;
            if (slotIndex < 0 || slotIndex >= crate.SlotCount)
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);

            ItemStack stack = crate.GetSlot(slotIndex);
            if (stack.IsEmpty)
            {
                SetStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateEmptySlot));
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.SharedCrateEmpty);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateWithdraw(stack.ItemId, stack.Count, out bool sentToHost);
            ReportTransfer(
                result,
                sentToHost,
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CrateWithdrew, FormatStack(stack)));
            return result;
        }

        public void Refresh()
        {
            if (slotLabels == null)
                return;

            Inventory crate = survivalSync != null ? survivalSync.SharedCrateInventory : null;
            for (int i = 0; i < slotLabels.Length; i++)
            {
                if (slotLabels[i] == null)
                    continue;

                slotLabels[i].text = crate != null && i < crate.SlotCount
                    ? FormatStack(crate.GetSlot(i))
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty);
            }
        }

        void ReportTransfer(SurvivalCommandResult result, bool sentToHost, string successText)
        {
            bool ok = result.Accepted || result.PendingHostValidation || sentToHost;
            SetStatus(result.Accepted
                ? successText
                : sentToHost
                    ? BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateTransferring)
                    : BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CrateTransferRejected));
            Refresh();
            PlayFeedback(ok ? BlockiverseAudioCue.UiSelect : BlockiverseAudioCue.UiCancel);
            if (result.Accepted)
                CrateChanged?.Invoke();
        }

        void Awake()
        {
            WireButtons();
        }

        void WireButtons()
        {
            if (slotButtons != null)
            {
                for (int index = 0; index < slotButtons.Length; index++)
                {
                    Button button = slotButtons[index];
                    if (button == null)
                        continue;

                    int slotIndex = index;
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => WithdrawSlot(slotIndex));
                }
            }

            if (depositButton != null)
            {
                depositButton.onClick.RemoveAllListeners();
                depositButton.onClick.AddListener(() => DepositHeld());
            }
        }

        string FormatStack(ItemStack stack)
        {
            if (stack.IsEmpty)
                return BlockiverseLocalization.Text(BlockiverseLocalization.Keys.CommonEmpty);

            ItemDefinition definition = (itemRegistry ?? DefaultItemRegistry).Get(stack.ItemId);
            return BlockiverseLocalization.Format(BlockiverseLocalization.Keys.CommonStack, definition.Name, stack.Count);
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}
