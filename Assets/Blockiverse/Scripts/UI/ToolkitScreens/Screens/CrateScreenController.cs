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
    // UI Toolkit port of SurvivalCratePanel (migration matrix row 19). Shared co-op crate:
    // clicking a slot withdraws its stack into the player inventory, the deposit button
    // deposits the whole selected-hotbar stack. Every transfer goes through the
    // host-authoritative MultiplayerSurvivalSync.TrySubmitCrateDeposit/Withdraw — the crate
    // mirror is never mutated locally on a client. Matrix §4 item 2, ported exactly:
    // accepted-or-pending counts as UI success (pending shows the transferring status with
    // the success cue), but the CrateChanged event fires ONLY on Accepted; later client-side
    // resolution arrives as SharedCrateChanged/LocalInventoryChanged snapshots and the
    // screen simply repaints from the mirrors.
    // Height is 760 (the prior measured fit for 4 slots, one grid row) plus 456 for the two
    // extra rows twelve slots now wrap into: .crate-slot is a fixed 220px box with 4px top/bottom
    // margins (CrateScreen.uss), so each additional row is exactly 228px — a genuine per-row
    // constant, not the "counting rows" derivation this project's own sizing history warns is
    // unreliable (that trap is about guessing the OTHER things a panel spends height on; here
    // only the grid grew, and its row height comes straight from the CSS, not from an assumption).
    // NOT yet re-verified with Blockiverse/UI Toolkit/Report Screen Content Fit (no live Play-mode
    // Editor session was available while making this fix) -- if it reads with more than ~24px of
    // slack or scrolls, run that pass and correct this constant to the measured value.
    [UiToolkitScreen(MenuActions.StationCrateScreen, "Assets/Blockiverse/UI/Documents/CrateScreen.uxml",
        1000, 1216, UiToolkitPlacementProfile.Menu)]
    public sealed class CrateScreenController : UiToolkitScreenController
    {
        // Must match MultiplayerSurvivalSync's authoritative shared-crate slot count (12). That
        // constant is private to Networking and this UI never should have hard-coded a smaller
        // number against it in the first place: this screen was ported from the uGUI panel at 4
        // slots and no paging, so once deposits filled slots 0-3 anything landing in 4-11 was
        // stored, valid, and permanently unreachable through the only crate UI (Codex review,
        // PR #344). Refresh already renders defensively against the real crate.SlotCount, so a
        // mismatch in either direction degrades gracefully -- but the two must agree in practice.
        public const int CrateSlotElementCount = 12;

        // Table keys shared with the uGUI panel — the copy contract. Values must match
        // BlockiverseLocalization.Keys verbatim; duplicating the strings here keeps this
        // screen off the uGUI shim (screen controllers never call BlockiverseLocalization).
        static class Keys
        {
            public const string CommonEmpty = "ui.common.empty";
            public const string CommonStack = "ui.common.stack";
            public const string Shared = "ui.status.crate.shared";
            public const string Offline = "ui.status.crate.offline";
            public const string NothingHeld = "ui.status.crate.nothing_held";
            public const string Deposited = "ui.status.crate.deposited";
            public const string EmptySlot = "ui.status.crate.empty_slot";
            public const string Withdrew = "ui.status.crate.withdrew";
            public const string Transferring = "ui.status.crate.transferring";
            public const string TransferRejected = "ui.status.crate.transfer_rejected";

            // Requested new entries (the uGUI panel hardcoded "Shared Crate" and
            // "Deposit Held" as scene text). Until they land in the table UiText.Get falls
            // back to the key string.
            public const string Title = "ui.generated.crate.title";
            public const string Deposit = "ui.generated.crate.deposit";
        }

        // The status trio maps onto the Hearthstone status signals: the word always carries
        // the meaning, the class only tints it.
        enum StatusSignal
        {
            None,
            Confirmed,
            Refused,
            Rejected,
        }

        static readonly ItemRegistry DefaultItemRegistry = ItemRegistry.Default;

        readonly Button[] slotButtons = new Button[CrateSlotElementCount];
        Label titleLabel;
        Button depositButton;
        Label statusLabel;
        Button closeButton;

        EventCallback<ClickEvent>[] slotClickCallbacks;
        EventCallback<ClickEvent> depositCallback;
        EventCallback<ClickEvent> closeCallback;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;
        MultiplayerSurvivalSync survivalSync;
        ItemRegistry itemRegistry;

        // Raised after an accepted deposit/withdraw so sibling screens can refresh — the
        // uGUI HUD's CrateChanged consumer semantics, never raised for pending or rejected.
        public event Action CrateChanged;

        public override string ScreenId => MenuActions.StationCrateScreen;

        public void ConfigureFeedback(
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            IBlockiverseInteractionHaptics targetInteractionHaptics)
        {
            audioCuePlayer = targetAudioCuePlayer;
            interactionHaptics = targetInteractionHaptics;
        }

        public void Bind(MultiplayerSurvivalSync sync, ItemRegistry registry = null)
        {
            if (survivalSync != null)
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;

            survivalSync = sync;
            itemRegistry = registry ?? DefaultItemRegistry;

            // Repaint on authoritative crate snapshots — the uGUI HUD held this subscription;
            // the screen owns it now that it binds itself.
            if (survivalSync != null)
            {
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
                survivalSync.SharedCrateChanged += OnSharedCrateChanged;
            }

            SetStatus(UiText.Get(survivalSync != null ? Keys.Shared : Keys.Offline), StatusSignal.None);
            Refresh();
        }

        // Deposits the player's currently selected hotbar item (whole stack) into the shared crate.
        public SurvivalCommandResult DepositHeld()
        {
            if (survivalSync == null)
            {
                SetStatus(UiText.Get(Keys.Offline), StatusSignal.Rejected);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            ItemStack held = survivalSync.EquippedItem;
            if (held.IsEmpty)
            {
                SetStatus(UiText.Get(Keys.NothingHeld), StatusSignal.Refused);
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateDeposit, SurvivalCommandFailureReason.InvalidTransfer);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateDeposit(held.ItemId, held.Count, out bool sentToHost);
            ReportTransfer(
                result,
                sentToHost,
                UiText.Format(Keys.Deposited, FormatStack(held)));
            return result;
        }

        // Withdraws the crate stack at the given slot index back to the player inventory.
        public SurvivalCommandResult WithdrawSlot(int slotIndex)
        {
            if (survivalSync == null)
            {
                SetStatus(UiText.Get(Keys.Offline), StatusSignal.Rejected);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);
            }

            Inventory crate = survivalSync.SharedCrateInventory;
            if (slotIndex < 0 || slotIndex >= crate.SlotCount)
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.InvalidTransfer);

            ItemStack stack = crate.GetSlot(slotIndex);
            if (stack.IsEmpty)
            {
                SetStatus(UiText.Get(Keys.EmptySlot), StatusSignal.Refused);
                PlayFeedback(BlockiverseAudioCue.UiCancel);
                return SurvivalCommandResult.Reject(SurvivalCommandKind.SharedCrateWithdraw, SurvivalCommandFailureReason.SharedCrateEmpty);
            }

            SurvivalCommandResult result = survivalSync.TrySubmitCrateWithdraw(stack.ItemId, stack.Count, out bool sentToHost);
            ReportTransfer(
                result,
                sentToHost,
                UiText.Format(Keys.Withdrew, FormatStack(stack)));
            return result;
        }

        public void Refresh()
        {
            Inventory crate = survivalSync != null ? survivalSync.SharedCrateInventory : null;
            for (int i = 0; i < CrateSlotElementCount; i++)
            {
                if (slotButtons[i] == null)
                    continue;

                slotButtons[i].text = crate != null && i < crate.SlotCount
                    ? FormatStack(crate.GetSlot(i))
                    : UiText.Get(Keys.CommonEmpty);
            }
        }

        public void SimulateClose() => OnClosePressed();

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            titleLabel = Require<Label>(root, "bv-title", ref allFound);
            for (int i = 0; i < CrateSlotElementCount; i++)
                slotButtons[i] = Require<Button>(root, "bv-crate-slot-" + (i + 1), ref allFound);
            depositButton = Require<Button>(root, "bv-deposit", ref allFound);
            statusLabel = Require<Label>(root, "bv-status", ref allFound);
            closeButton = Require<Button>(root, "bv-close", ref allFound);

            ApplyStaticText();
            Refresh();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            slotClickCallbacks = new EventCallback<ClickEvent>[CrateSlotElementCount];
            for (int i = 0; i < CrateSlotElementCount; i++)
            {
                int index = i;
                slotClickCallbacks[i] = _ => WithdrawSlot(index);
                slotButtons[i]?.RegisterCallback(slotClickCallbacks[i]);
            }

            depositCallback = _ => DepositHeld();
            // Slot and deposit clicks cue by transfer OUTCOME (UiSelect/UiCancel above);
            // close is plain navigation and takes the plain click.
            closeCallback = _ => { PlayFeedback(BlockiverseAudioCue.UiSelect); OnClosePressed(); };
            depositButton?.RegisterCallback(depositCallback);
            closeButton?.RegisterCallback(closeCallback);

            // The title and deposit labels are UiText-rendered stand-ins for static bindings
            // (their keys are pending central addition), so they must track locale changes
            // the way a native binding would.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            for (int i = 0; i < CrateSlotElementCount; i++)
            {
                if (slotClickCallbacks != null && slotClickCallbacks[i] != null)
                    slotButtons[i]?.UnregisterCallback(slotClickCallbacks[i]);
            }

            slotClickCallbacks = null;

            if (depositCallback != null)
                depositButton?.UnregisterCallback(depositCallback);
            if (closeCallback != null)
                closeButton?.UnregisterCallback(closeCallback);
            depositCallback = null;
            closeCallback = null;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            titleLabel = null;
            for (int i = 0; i < CrateSlotElementCount; i++)
                slotButtons[i] = null;
            depositButton = null;
            statusLabel = null;
            closeButton = null;
        }

        protected override void OnShown()
        {
            // Discover the sync at the routed-visibility boundary; a session may start after
            // scene load, so a null sync is re-looked-up on every open. Bind also covers the
            // no-sync case with the offline status, exactly like the uGUI Bind(null).
            if (survivalSync == null)
                Bind(BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include), itemRegistry);
            else
                Refresh();
        }

        void OnDestroy()
        {
            if (survivalSync != null)
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
        }

        void OnSharedCrateChanged() => Refresh();

        void OnSelectedLocaleChanged(Locale locale)
        {
            ApplyStaticText();
            Refresh();
        }

        void OnClosePressed()
        {
            MenuController?.CloseStationCrateScreen();
        }

        void ApplyStaticText()
        {
            if (titleLabel != null)
                titleLabel.text = UiText.Get(Keys.Title);
            if (depositButton != null)
                depositButton.text = UiText.Get(Keys.Deposit);
        }

        void ReportTransfer(SurvivalCommandResult result, bool sentToHost, string successText)
        {
            bool ok = result.Accepted || result.PendingHostValidation || sentToHost;
            if (result.Accepted)
                SetStatus(successText, StatusSignal.Confirmed);
            else if (sentToHost)
                SetStatus(UiText.Get(Keys.Transferring), StatusSignal.None);
            else
                SetStatus(UiText.Get(Keys.TransferRejected), StatusSignal.Rejected);
            Refresh();
            PlayFeedback(ok ? BlockiverseAudioCue.UiSelect : BlockiverseAudioCue.UiCancel);
            if (result.Accepted)
                CrateChanged?.Invoke();
        }

        string FormatStack(ItemStack stack)
        {
            if (stack.IsEmpty)
                return UiText.Get(Keys.CommonEmpty);

            ItemDefinition definition = (itemRegistry ?? DefaultItemRegistry).Get(stack.ItemId);
            return UiText.Format(Keys.CommonStack, definition.Name, stack.Count);
        }

        void SetStatus(string message, StatusSignal signal)
        {
            if (statusLabel == null)
                return;

            statusLabel.text = message;
            statusLabel.EnableInClassList("hs-status--confirmed", signal == StatusSignal.Confirmed);
            statusLabel.EnableInClassList("hs-status--refused", signal == StatusSignal.Refused);
            statusLabel.EnableInClassList("hs-status--rejected", signal == StatusSignal.Rejected);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }
    }
}
