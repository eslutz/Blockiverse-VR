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
        [SerializeField] BlockiverseControllerHaptics dominantHandHaptics;
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;

        bool subscribed;

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
        }

        void Subscribe()
        {
            if (subscribed || interactionController == null)
                return;

            interactionController.BlockMutationApplied += OnBlockMutationApplied;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || interactionController == null)
                return;

            interactionController.BlockMutationApplied -= OnBlockMutationApplied;
            subscribed = false;
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
