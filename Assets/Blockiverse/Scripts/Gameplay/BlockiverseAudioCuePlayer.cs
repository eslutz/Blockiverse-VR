using System;
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
        UiCancel,
        Footstep,
        InventoryOpen,
        InventoryClose,
        CraftSuccess,
        CraftFail
    }

    /// <summary>
    /// Central one-shot sound player. Auto-plays break/place cues from a creative interaction
    /// controller's mutation events and exposes <see cref="PlayCue"/> for UI feedback. Clips are
    /// generated original cues under Assets/Blockiverse/Audio and assigned on the prefab.
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
        [SerializeField] AudioClip[] footstepClips = Array.Empty<AudioClip>();
        [SerializeField] AudioClip inventoryOpenClip;
        [SerializeField] AudioClip inventoryCloseClip;
        [SerializeField] AudioClip craftSuccessClip;
        [SerializeField] AudioClip craftFailClip;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        bool subscribed;
        int footstepClipIndex;

        public event Action<BlockiverseAudioCue, AudioClip> CuePlayed;
        public int FootstepClipCount => CountAssignedFootstepClips();

        public void Configure(CreativeInteractionController controller)
        {
            Unsubscribe();
            interactionController = controller;
            Subscribe();
        }

        public void PlayCue(BlockiverseAudioCue cue)
        {
            EnsureReferences();
            AudioClip clip = ResolveClip(cue);
            if (clip == null || audioSource == null)
                return;

            audioSource.PlayOneShot(clip, volume);
            CuePlayed?.Invoke(cue, clip);
        }

        public void ConfigureClip(BlockiverseAudioCue cue, AudioClip clip)
        {
            switch (cue)
            {
                case BlockiverseAudioCue.BlockBreak:
                    blockBreakClip = clip;
                    break;
                case BlockiverseAudioCue.BlockPlace:
                    blockPlaceClip = clip;
                    break;
                case BlockiverseAudioCue.UiSelect:
                    uiSelectClip = clip;
                    break;
                case BlockiverseAudioCue.UiConfirm:
                    uiConfirmClip = clip;
                    break;
                case BlockiverseAudioCue.UiCancel:
                    uiCancelClip = clip;
                    break;
                case BlockiverseAudioCue.Footstep:
                    ConfigureFootstepClips(clip);
                    break;
                case BlockiverseAudioCue.InventoryOpen:
                    inventoryOpenClip = clip;
                    break;
                case BlockiverseAudioCue.InventoryClose:
                    inventoryCloseClip = clip;
                    break;
                case BlockiverseAudioCue.CraftSuccess:
                    craftSuccessClip = clip;
                    break;
                case BlockiverseAudioCue.CraftFail:
                    craftFailClip = clip;
                    break;
            }
        }

        public void ConfigureFootstepClips(params AudioClip[] clips)
        {
            footstepClips = clips ?? Array.Empty<AudioClip>();
            footstepClipIndex = 0;
        }

        public bool HasClipForCue(BlockiverseAudioCue cue)
        {
            return cue == BlockiverseAudioCue.Footstep
                ? CountAssignedFootstepClips() > 0
                : ResolveFixedClip(cue) != null;
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
            if (cue == BlockiverseAudioCue.Footstep)
                return ResolveFootstepClip();

            return ResolveFixedClip(cue);
        }

        AudioClip ResolveFixedClip(BlockiverseAudioCue cue)
        {
            return cue switch
            {
                BlockiverseAudioCue.BlockBreak => blockBreakClip,
                BlockiverseAudioCue.BlockPlace => blockPlaceClip,
                BlockiverseAudioCue.UiSelect => uiSelectClip,
                BlockiverseAudioCue.UiConfirm => uiConfirmClip,
                BlockiverseAudioCue.UiCancel => uiCancelClip,
                BlockiverseAudioCue.InventoryOpen => inventoryOpenClip,
                BlockiverseAudioCue.InventoryClose => inventoryCloseClip,
                BlockiverseAudioCue.CraftSuccess => craftSuccessClip,
                BlockiverseAudioCue.CraftFail => craftFailClip,
                _ => null
            };
        }

        AudioClip ResolveFootstepClip()
        {
            if (footstepClips == null || footstepClips.Length == 0)
                return null;

            for (int attempts = 0; attempts < footstepClips.Length; attempts++)
            {
                int index = footstepClipIndex % footstepClips.Length;
                footstepClipIndex = (footstepClipIndex + 1) % footstepClips.Length;
                AudioClip clip = footstepClips[index];
                if (clip != null)
                    return clip;
            }

            return null;
        }

        int CountAssignedFootstepClips()
        {
            if (footstepClips == null)
                return 0;

            int count = 0;
            foreach (AudioClip clip in footstepClips)
            {
                if (clip != null)
                    count++;
            }

            return count;
        }
    }
}
