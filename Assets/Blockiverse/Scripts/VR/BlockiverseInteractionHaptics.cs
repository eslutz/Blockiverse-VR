using System;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    /// <summary>
    /// Plays controller haptics on the dominant hand in response to creative block mutations,
    /// mirroring <see cref="BlockiverseAudioCuePlayer"/> so feedback stays decoupled from edit logic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseInteractionHaptics : MonoBehaviour
    {
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] BlockiverseControllerHaptics dominantHandHaptics;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;

        bool subscribed;
        bool subscribedToSurvival;

        public event Action UiTickRequested;
        public CreativeInteractionController InteractionController => interactionController;
        public BlockiverseControllerHaptics DominantHandHaptics => dominantHandHaptics;
        public BlockiverseFeedbackSettings FeedbackSettings => feedbackSettings;

        public void Configure(CreativeInteractionController controller, BlockiverseControllerHaptics haptics)
        {
            Unsubscribe();
            interactionController = controller;
            dominantHandHaptics = haptics;
            Subscribe();
        }

        public void ConfigureFeedbackSettings(BlockiverseFeedbackSettings settings)
        {
            feedbackSettings = settings;
        }

        public void PlayUiTick()
        {
            UiTickRequested?.Invoke();
            SendPattern(BlockiverseHapticPattern.UiTick);
        }

        void OnEnable()
        {
            DiscoverDependencies();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void DiscoverDependencies()
        {
            if (dominantHandHaptics == null)
                dominantHandHaptics = GetComponentInChildren<BlockiverseControllerHaptics>(true);

            if (feedbackSettings == null)
                feedbackSettings = GetComponent<BlockiverseFeedbackSettings>();

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();

            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
        }

        void Subscribe()
        {
            if (!subscribed && interactionController != null)
            {
                interactionController.BlockMutationApplied += OnBlockMutationApplied;
                subscribed = true;
            }

            if (!subscribedToSurvival && survivalSync != null)
            {
                survivalSync.CommandFeedback += OnSurvivalCommandFeedback;
                subscribedToSurvival = true;
            }
        }

        void Unsubscribe()
        {
            if (subscribed && interactionController != null)
                interactionController.BlockMutationApplied -= OnBlockMutationApplied;
            subscribed = false;

            if (subscribedToSurvival && survivalSync != null)
                survivalSync.CommandFeedback -= OnSurvivalCommandFeedback;
            subscribedToSurvival = false;
        }

        // Survival break/place resolve through the host-authoritative command channel rather
        // than the creative controller; mirror the same break/place buzz for them.
        void OnSurvivalCommandFeedback(Blockiverse.Gameplay.SurvivalCommandResult result, BlockPosition position)
        {
            if (!result.Accepted)
                return;

            switch (result.CommandKind)
            {
                case Blockiverse.Gameplay.SurvivalCommandKind.HarvestResource:
                    SendPattern(BlockiverseHapticPattern.BlockBreak);
                    break;
                case Blockiverse.Gameplay.SurvivalCommandKind.PlaceBlock:
                case Blockiverse.Gameplay.SurvivalCommandKind.StripLog:
                case Blockiverse.Gameplay.SurvivalCommandKind.TillSoil:
                case Blockiverse.Gameplay.SurvivalCommandKind.PlantSeed:
                    SendPattern(BlockiverseHapticPattern.BlockPlace);
                    break;
            }
        }

        void OnBlockMutationApplied(BlockChange change)
        {
            if (dominantHandHaptics == null)
                return;

            SendPattern(
                change.NewBlock == BlockRegistry.Air
                    ? BlockiverseHapticPattern.BlockBreak
                    : BlockiverseHapticPattern.BlockPlace);
        }

        void SendPattern(BlockiverseHapticPattern pattern)
        {
            if (dominantHandHaptics == null)
                return;

            dominantHandHaptics.SendPattern(
                feedbackSettings != null
                    ? pattern.Scale(feedbackSettings.ResolveHapticIntensity())
                    : pattern);
        }
    }
}
