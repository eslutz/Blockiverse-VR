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

        bool subscribed;

        public void Configure(CreativeInteractionController controller, BlockiverseControllerHaptics haptics)
        {
            Unsubscribe();
            interactionController = controller;
            dominantHandHaptics = haptics;
            Subscribe();
        }

        public void PlayUiTick()
        {
            dominantHandHaptics?.SendPattern(BlockiverseHapticPattern.UiTick);
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

            dominantHandHaptics.SendPattern(
                change.NewBlock == BlockRegistry.Air
                    ? BlockiverseHapticPattern.BlockBreak
                    : BlockiverseHapticPattern.BlockPlace);
        }
    }
}
