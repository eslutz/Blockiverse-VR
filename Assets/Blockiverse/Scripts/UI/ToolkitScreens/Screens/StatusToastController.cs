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

        // A message the player must not miss outlives an ordinary one, rather than merely being
        // harder to displace.
        public const float CriticalMessageSeconds = 5.0f;

        // How a message reads, and how hard it is to displace. Severity and priority are ONE
        // concept deliberately: two would let a caller ask for a low-priority error, which has no
        // coherent meaning and would only ever be a bug.
        //
        // The four non-info values are the visual language's four signals (ADR 0010 §8) and carry
        // the same meanings here — moss confirmed, ochre refused-try-differently, oxide
        // rejected-this-will-not-work. Base.uss already defines a stripe for each, so the styling
        // costs no new rules.
        //
        // Nothing is actually contended today: OnCommandFeedback is the only producer. This exists
        // because the report asks for prioritised messaging, and because the moment a second
        // producer appears — pickups, craft results, connection loss — last-write-wins starts
        // silently erasing whichever message mattered.
        public enum StatusSeverity
        {
            Info = 0,
            Confirmed = 10,
            Refused = 20,
            Rejected = 30,
            Critical = 40,
        }

        // Every class this controller can apply, so all of them can be removed before one is
        // added. Removing only "the previous one" leaves a stale stripe the first time a path
        // forgets to record what it set.
        static readonly string[] SeverityClasses =
        {
            "hs-status--confirmed",
            "hs-status--refused",
            "hs-status--rejected",
        };

        StatusSeverity activeSeverity = StatusSeverity.Info;

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

        // Harvest rejections are Refused: the player can change something — empty a slot, bring a
        // better tool — and try again. That is what separates ochre from oxide in this palette.
        public void ShowTimedStatus(string message) =>
            ShowTimedStatus(message, StatusSeverity.Refused);

        public void ShowTimedStatus(string message, StatusSeverity severity)
        {
            // Equal severity replaces: the newest message of the same weight is the relevant one.
            // Lower is dropped while something heavier is still on screen.
            bool showing = statusVisibleUntil > 0f && !string.IsNullOrEmpty(pendingText);

            if (showing && severity < activeSeverity)
                return;

            activeSeverity = severity;
            SetStatusText(message);

            float seconds = severity == StatusSeverity.Critical
                ? CriticalMessageSeconds
                : StatusMessageSeconds;

            statusVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);
        }

        // Info deliberately has no stripe — the plate's neutral surface already says "nothing
        // happened", and one of four scarce signal colours should not be spent saying it.
        public static string ClassFor(StatusSeverity severity) => severity switch
        {
            StatusSeverity.Confirmed => "hs-status--confirmed",
            StatusSeverity.Refused => "hs-status--refused",
            StatusSeverity.Rejected => "hs-status--rejected",
            StatusSeverity.Critical => "hs-status--rejected",
            _ => null,
        };

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
            activeSeverity = StatusSeverity.Info;
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
            // Drop back to Info as the message clears. Nothing is showing, so the priority gate in
            // ShowTimedStatus does not consult this — but leaving it pinned at Critical means the
            // day someone adds a producer that checks severity outside that gate, it reads a
            // severity for a message that expired minutes ago.
            activeSeverity = StatusSeverity.Info;
            SetStatusText(string.Empty);
        }

        void ApplyDisplay()
        {
            if (toastLabel == null)
                return;

            toastLabel.text = pendingText;

            bool empty = string.IsNullOrEmpty(pendingText);

            toastLabel.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;

            // Clear all of them, then add at most one. Removing only the previously-applied class
            // would leave a stale stripe behind the first time a path forgets to record what it set.
            foreach (string severityClass in SeverityClasses)
                toastLabel.RemoveFromClassList(severityClass);

            if (empty)
                return;

            string active = ClassFor(activeSeverity);

            if (!string.IsNullOrEmpty(active))
                toastLabel.AddToClassList(active);
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
