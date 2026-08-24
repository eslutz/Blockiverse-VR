using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the uGUI Survival HUD's status label (matrix row 23): timed
    // harvest-rejection toasts with SurvivalHudController's SetStatusText semantics — the
    // line hides entirely whenever the text is empty, and timed messages expire on UNSCALED
    // time so a paused clock cannot pin a toast on screen.
    //
    // Split-panel divergence, recorded rather than silent: in uGUI the mining "Mining {0}%"
    // text shared this label (which is why ShowTimedStatus and OnMiningProgressCleared
    // arbitrated over it); the percent line now renders on the MiningProgress panel, so the
    // showingMiningProgress arbitration has no counterpart here. The command-feedback filter
    // is unchanged: only FINAL harvest rejections (not accepted, not pending, not duplicate)
    // produce a toast.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/StatusToast.uxml",
        640, 120, UiToolkitPlacementProfile.Hud, HudLocalY = 0.34f, NonInteractive = true)]
    public sealed class StatusToastController : UiToolkitScreenController
    {
        // uGUI serialized default (statusMessageSeconds).
        public const float StatusMessageSeconds = 2.5f;

        static class Keys
        {
            public const string InventoryFull = "ui.status.survival.inventory_full";
            public const string ToolTooWeak = "ui.status.survival.tool_too_weak";
            public const string HarvestRejected = "ui.status.survival.harvest_rejected";
        }

        Label toastLabel;
        MultiplayerSurvivalSync subscribedSync;
        string pendingText = string.Empty;
        float statusVisibleUntil;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public string CurrentStatusText => pendingText;

        // Public event seam: EditMode tests drive it directly; the survival sync subscribes
        // it at show time.
        public void OnCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            if (result.CommandKind != SurvivalCommandKind.HarvestResource ||
                result.Accepted ||
                result.PendingHostValidation ||
                result.IsDuplicate)
            {
                return;
            }

            string message = result.HarvestFailureReason switch
            {
                BlockHarvestFailureReason.InventoryFull => UiText.Get(Keys.InventoryFull),
                BlockHarvestFailureReason.InsufficientTool => UiText.Get(Keys.ToolTooWeak),
                _ when result.FailureReason == SurvivalCommandFailureReason.InventoryFull =>
                    UiText.Get(Keys.InventoryFull),
                _ => UiText.Get(Keys.HarvestRejected)
            };

            ShowTimedStatus(message);
        }

        public void ShowTimedStatus(string message)
        {
            SetStatusText(message);
            statusVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, StatusMessageSeconds);
        }

        public void SetStatusText(string message)
        {
            pendingText = message ?? string.Empty;
            ApplyDisplay();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            toastLabel = Require<Label>(root, "bv-toast-label", ref allFound);

            ApplyDisplay();
            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
        }

        protected override void OnUnregisterCallbacks()
        {
        }

        protected override void OnDetach()
        {
            toastLabel = null;
        }

        protected override void OnShown()
        {
            SubscribeSync();
        }

        protected override void OnHidden()
        {
            UnsubscribeSync();
            // Toasts are transient; a stale rejection must not greet the player on return.
            statusVisibleUntil = 0f;
            SetStatusText(string.Empty);
        }

        void OnDestroy()
        {
            UnsubscribeSync();
        }

        void Update()
        {
            if (statusVisibleUntil <= 0f || Time.unscaledTime < statusVisibleUntil)
                return;

            statusVisibleUntil = 0f;
            SetStatusText(string.Empty);
        }

        void ApplyDisplay()
        {
            if (toastLabel == null)
                return;

            toastLabel.text = pendingText;
            toastLabel.style.display = string.IsNullOrEmpty(pendingText)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        void SubscribeSync()
        {
            UnsubscribeSync();

            subscribedSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            if (subscribedSync != null)
                subscribedSync.CommandFeedback += OnCommandFeedback;
        }

        void UnsubscribeSync()
        {
            if (subscribedSync == null)
                return;

            subscribedSync.CommandFeedback -= OnCommandFeedback;
            subscribedSync = null;
        }
    }
}
