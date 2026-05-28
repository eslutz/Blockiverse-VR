using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public enum BlockiverseAudioCue
    {
        BlockBreak,
        BlockPlace,
        UiSelect,
        UiConfirm,
        UiCancel
    }

    /// <summary>
    /// Central one-shot sound player. Auto-plays break/place cues from a creative interaction
    /// controller's mutation events and exposes <see cref="PlayCue"/> for UI feedback. Clips are
    /// authored placeholders generated under Assets/Blockiverse/Audio and assigned on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BlockiverseAudioCuePlayer : MonoBehaviour
    {
        [SerializeField] AudioSource audioSource;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] AudioClip blockBreakClip;
        [SerializeField] AudioClip blockPlaceClip;
        [SerializeField] AudioClip uiSelectClip;
        [SerializeField] AudioClip uiConfirmClip;
        [SerializeField] AudioClip uiCancelClip;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        bool subscribed;

        public void Configure(CreativeInteractionController controller)
        {
            Unsubscribe();
            interactionController = controller;
            Subscribe();
        }

        public void PlayCue(BlockiverseAudioCue cue)
        {
            AudioClip clip = ResolveClip(cue);
            if (clip == null || audioSource == null)
                return;

            audioSource.PlayOneShot(clip, volume);
        }

        void Awake()
        {
            EnsureReferences();
        }

        void OnEnable()
        {
            EnsureReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void EnsureReferences()
        {
            if (audioSource == null && !TryGetComponent(out audioSource) && Application.isPlaying)
                audioSource = gameObject.AddComponent<AudioSource>();

            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }

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
            PlayCue(change.NewBlock == BlockRegistry.Air ? BlockiverseAudioCue.BlockBreak : BlockiverseAudioCue.BlockPlace);
        }

        AudioClip ResolveClip(BlockiverseAudioCue cue)
        {
            return cue switch
            {
                BlockiverseAudioCue.BlockBreak => blockBreakClip,
                BlockiverseAudioCue.BlockPlace => blockPlaceClip,
                BlockiverseAudioCue.UiSelect => uiSelectClip,
                BlockiverseAudioCue.UiConfirm => uiConfirmClip,
                BlockiverseAudioCue.UiCancel => uiCancelClip,
                _ => null
            };
        }
    }
}
