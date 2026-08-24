using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.UI.Toolkit;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the uGUI Survival HUD's mining-progress slider (matrix row 22),
    // including its "Mining {0}%" readout — in uGUI that text shared the HUD status label,
    // but with the status toast split onto its own panel the percent line lives here beside
    // the bar it describes.
    //
    // Event sources mirror SurvivalHudController exactly: the creative input bridge's
    // MiningProgressChanged/MiningProgressCleared drive show/update/hide, and a FINAL host
    // rejection of a harvest (not accepted, not pending, not a duplicate) hides the display,
    // porting the SetMiningProgressVisible(false) half of ShowTimedStatus. Subscriptions are
    // keyed on routed visibility (matrix §4 item 7): mining can only happen while the
    // gameplay HUD is the routed screen, and re-discovering on every show picks up replaced
    // bridge/sync instances the way uGUI's rebind did.
    [UiToolkitScreen(MenuActions.GameplayHudScreen, "Assets/Blockiverse/UI/Documents/MiningProgress.uxml",
        400, 90, UiToolkitPlacementProfile.Hud, HudLocalY = -0.16f, NonInteractive = true)]
    public sealed class MiningProgressController : UiToolkitScreenController
    {
        static class Keys
        {
            public const string MiningProgress = "ui.status.survival.mining_progress";
        }

        VisualElement miningBody;
        Label miningLabel;
        VisualElement miningFill;

        IBlockiverseCreativeInputBridge subscribedBridge;
        MultiplayerSurvivalSync subscribedSync;
        bool showingMiningProgress;
        string pendingLabelText = string.Empty;
        float pendingFillPercent;

        public override string ScreenId => MenuActions.GameplayHudScreen;

        public bool IsShowingMiningProgress => showingMiningProgress;

        // Public event seams: EditMode tests drive these directly, and the input bridge
        // subscribes them at show time.
        public void OnMiningProgressChanged(BlockPosition position, float elapsedSeconds, float requiredSeconds)
        {
            float progress = requiredSeconds > 0f
                ? Mathf.Clamp01(elapsedSeconds / requiredSeconds)
                : 1.0f;
            int percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            showingMiningProgress = true;
            pendingLabelText = UiText.Format(Keys.MiningProgress, percent);
            pendingFillPercent = progress * 100f;
            ApplyDisplay();
        }

        public void OnMiningProgressCleared()
        {
            showingMiningProgress = false;
            ApplyDisplay();
        }

        public void OnHarvestCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            if (result.CommandKind != SurvivalCommandKind.HarvestResource ||
                result.Accepted ||
                result.PendingHostValidation ||
                result.IsDuplicate)
            {
                return;
            }

            showingMiningProgress = false;
            ApplyDisplay();
        }

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            miningBody = Require<VisualElement>(root, "bv-mining-body", ref allFound);
            miningLabel = Require<Label>(root, "bv-mining-label", ref allFound);
            miningFill = Require<VisualElement>(root, "bv-mining-fill", ref allFound);

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
            miningBody = null;
            miningLabel = null;
            miningFill = null;
        }

        protected override void OnShown()
        {
            SubscribeSources();
        }

        protected override void OnHidden()
        {
            UnsubscribeSources();
            // Routing away interrupts mining input; a stale bar must not survive the return.
            showingMiningProgress = false;
            ApplyDisplay();
        }

        void OnDestroy()
        {
            UnsubscribeSources();
        }

        void ApplyDisplay()
        {
            if (miningBody != null)
                miningBody.style.display = showingMiningProgress ? DisplayStyle.Flex : DisplayStyle.None;

            if (!showingMiningProgress)
                return;

            if (miningLabel != null)
                miningLabel.text = pendingLabelText;

            if (miningFill != null)
                miningFill.style.width = Length.Percent(pendingFillPercent);
        }

        void SubscribeSources()
        {
            UnsubscribeSources();

            subscribedBridge = FindInputBridge();
            subscribedSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (subscribedBridge != null)
            {
                subscribedBridge.MiningProgressChanged += OnMiningProgressChanged;
                subscribedBridge.MiningProgressCleared += OnMiningProgressCleared;
            }

            if (subscribedSync != null)
                subscribedSync.CommandFeedback += OnHarvestCommandFeedback;
        }

        void UnsubscribeSources()
        {
            if (subscribedBridge != null)
            {
                subscribedBridge.MiningProgressChanged -= OnMiningProgressChanged;
                subscribedBridge.MiningProgressCleared -= OnMiningProgressCleared;
                subscribedBridge = null;
            }

            if (subscribedSync != null)
            {
                subscribedSync.CommandFeedback -= OnHarvestCommandFeedback;
                subscribedSync = null;
            }
        }

        // The bridge is an interface with no scene-lookup helper; mirror SurvivalHudController's
        // discovery scan over live MonoBehaviours.
        static IBlockiverseCreativeInputBridge FindInputBridge()
        {
            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IBlockiverseCreativeInputBridge bridge)
                    return bridge;
            }

            return null;
        }
    }
}
