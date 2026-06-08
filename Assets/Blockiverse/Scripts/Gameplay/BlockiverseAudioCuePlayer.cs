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
        CraftFail,
        ToolHitSoft,
        ToolHitStone,
        ToolWrong,
        PickupItem,
        ContainerOpen,
        ContainerClose,
        TorchIgnite,
        TorchLoop,
        CampfireLoop,
        RainLightLoop,
        RainHeavyLoop,
        ThunderNear,
        ThunderFar,
        SnowWindLoop,
        CaveAmbienceLoop,
        DayAmbienceLoop,
        NightAmbienceLoop,
        MultiplayerJoin,
        MultiplayerLeave
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
        [SerializeField] BlockiverseFeedbackSettings feedbackSettings;
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
        [SerializeField] AudioClip toolHitSoftClip;
        [SerializeField] AudioClip toolHitStoneClip;
        [SerializeField] AudioClip toolWrongClip;
        [SerializeField] AudioClip pickupItemClip;
        [SerializeField] AudioClip containerOpenClip;
        [SerializeField] AudioClip containerCloseClip;
        [SerializeField] AudioClip torchIgniteClip;
        [SerializeField] AudioClip torchLoopClip;
        [SerializeField] AudioClip campfireLoopClip;
        [SerializeField] AudioClip rainLightLoopClip;
        [SerializeField] AudioClip rainHeavyLoopClip;
        [SerializeField] AudioClip thunderNearClip;
        [SerializeField] AudioClip thunderFarClip;
        [SerializeField] AudioClip snowWindLoopClip;
        [SerializeField] AudioClip caveAmbienceLoopClip;
        [SerializeField] AudioClip dayAmbienceLoopClip;
        [SerializeField] AudioClip nightAmbienceLoopClip;
        [SerializeField] AudioClip multiplayerJoinClip;
        [SerializeField] AudioClip multiplayerLeaveClip;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;
        [SerializeField, Range(1, 24)] int worldSpaceSourceCount = 8;

        bool subscribed;
        int footstepClipIndex;
        AudioSource[] worldSpaceSources = Array.Empty<AudioSource>();
        int worldSpaceSourceIndex;

        public event Action<BlockiverseAudioCue, AudioClip> CuePlayed;
        public int FootstepClipCount => CountAssignedFootstepClips();
        public BlockiverseFeedbackSettings FeedbackSettings => feedbackSettings;
        public CreativeInteractionController InteractionController => interactionController;

        public void Configure(CreativeInteractionController controller)
        {
            Unsubscribe();
            interactionController = controller;
            Subscribe();
        }

        public void ConfigureFeedbackSettings(BlockiverseFeedbackSettings settings)
        {
            feedbackSettings = settings;
        }

        public void PlayCue(BlockiverseAudioCue cue)
        {
            EnsureReferences();
            AudioClip clip = ResolveClip(cue);
            if (clip == null || audioSource == null)
                return;

            float resolvedVolume = ResolveVolume(cue);
            if (resolvedVolume <= 0f)
                return;

            audioSource.PlayOneShot(clip, resolvedVolume);
            CuePlayed?.Invoke(cue, clip);
        }

        public void PlayCueAt(BlockiverseAudioCue cue, Vector3 worldPosition)
        {
            EnsureReferences();
            AudioClip clip = ResolveClip(cue);
            if (clip == null)
                return;

            float resolvedVolume = ResolveVolume(cue);
            if (resolvedVolume <= 0f)
                return;

            AudioSource source = ResolveWorldSpaceSource();
            if (source == null)
                return;

            source.transform.position = worldPosition;
            source.PlayOneShot(clip, resolvedVolume);
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
                case BlockiverseAudioCue.ToolHitSoft:
                    toolHitSoftClip = clip;
                    break;
                case BlockiverseAudioCue.ToolHitStone:
                    toolHitStoneClip = clip;
                    break;
                case BlockiverseAudioCue.ToolWrong:
                    toolWrongClip = clip;
                    break;
                case BlockiverseAudioCue.PickupItem:
                    pickupItemClip = clip;
                    break;
                case BlockiverseAudioCue.ContainerOpen:
                    containerOpenClip = clip;
                    break;
                case BlockiverseAudioCue.ContainerClose:
                    containerCloseClip = clip;
                    break;
                case BlockiverseAudioCue.TorchIgnite:
                    torchIgniteClip = clip;
                    break;
                case BlockiverseAudioCue.TorchLoop:
                    torchLoopClip = clip;
                    break;
                case BlockiverseAudioCue.CampfireLoop:
                    campfireLoopClip = clip;
                    break;
                case BlockiverseAudioCue.RainLightLoop:
                    rainLightLoopClip = clip;
                    break;
                case BlockiverseAudioCue.RainHeavyLoop:
                    rainHeavyLoopClip = clip;
                    break;
                case BlockiverseAudioCue.ThunderNear:
                    thunderNearClip = clip;
                    break;
                case BlockiverseAudioCue.ThunderFar:
                    thunderFarClip = clip;
                    break;
                case BlockiverseAudioCue.SnowWindLoop:
                    snowWindLoopClip = clip;
                    break;
                case BlockiverseAudioCue.CaveAmbienceLoop:
                    caveAmbienceLoopClip = clip;
                    break;
                case BlockiverseAudioCue.DayAmbienceLoop:
                    dayAmbienceLoopClip = clip;
                    break;
                case BlockiverseAudioCue.NightAmbienceLoop:
                    nightAmbienceLoopClip = clip;
                    break;
                case BlockiverseAudioCue.MultiplayerJoin:
                    multiplayerJoinClip = clip;
                    break;
                case BlockiverseAudioCue.MultiplayerLeave:
                    multiplayerLeaveClip = clip;
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
                BlockiverseAudioCue.ToolHitSoft => toolHitSoftClip,
                BlockiverseAudioCue.ToolHitStone => toolHitStoneClip,
                BlockiverseAudioCue.ToolWrong => toolWrongClip,
                BlockiverseAudioCue.PickupItem => pickupItemClip,
                BlockiverseAudioCue.ContainerOpen => containerOpenClip,
                BlockiverseAudioCue.ContainerClose => containerCloseClip,
                BlockiverseAudioCue.TorchIgnite => torchIgniteClip,
                BlockiverseAudioCue.TorchLoop => torchLoopClip,
                BlockiverseAudioCue.CampfireLoop => campfireLoopClip,
                BlockiverseAudioCue.RainLightLoop => rainLightLoopClip,
                BlockiverseAudioCue.RainHeavyLoop => rainHeavyLoopClip,
                BlockiverseAudioCue.ThunderNear => thunderNearClip,
                BlockiverseAudioCue.ThunderFar => thunderFarClip,
                BlockiverseAudioCue.SnowWindLoop => snowWindLoopClip,
                BlockiverseAudioCue.CaveAmbienceLoop => caveAmbienceLoopClip,
                BlockiverseAudioCue.DayAmbienceLoop => dayAmbienceLoopClip,
                BlockiverseAudioCue.NightAmbienceLoop => nightAmbienceLoopClip,
                BlockiverseAudioCue.MultiplayerJoin => multiplayerJoinClip,
                BlockiverseAudioCue.MultiplayerLeave => multiplayerLeaveClip,
                _ => null
            };
        }

        public static BlockiverseAudioCategory GetCategory(BlockiverseAudioCue cue)
        {
            return cue switch
            {
                BlockiverseAudioCue.UiSelect or
                BlockiverseAudioCue.UiConfirm or
                BlockiverseAudioCue.UiCancel or
                BlockiverseAudioCue.InventoryOpen or
                BlockiverseAudioCue.InventoryClose or
                BlockiverseAudioCue.CraftSuccess or
                BlockiverseAudioCue.CraftFail or
                BlockiverseAudioCue.MultiplayerJoin or
                BlockiverseAudioCue.MultiplayerLeave => BlockiverseAudioCategory.Ui,
                BlockiverseAudioCue.RainLightLoop or
                BlockiverseAudioCue.RainHeavyLoop or
                BlockiverseAudioCue.ThunderNear or
                BlockiverseAudioCue.ThunderFar or
                BlockiverseAudioCue.SnowWindLoop or
                BlockiverseAudioCue.CaveAmbienceLoop or
                BlockiverseAudioCue.DayAmbienceLoop or
                BlockiverseAudioCue.NightAmbienceLoop => BlockiverseAudioCategory.Weather,
                _ => BlockiverseAudioCategory.Effects
            };
        }

        float ResolveVolume(BlockiverseAudioCue cue)
        {
            float categoryVolume = feedbackSettings != null
                ? feedbackSettings.ResolveVolume(GetCategory(cue))
                : 1.0f;
            return Mathf.Clamp01(volume * categoryVolume);
        }

        AudioSource ResolveWorldSpaceSource()
        {
            EnsureWorldSpaceSources();
            if (worldSpaceSources.Length == 0)
                return null;

            AudioSource source = worldSpaceSources[worldSpaceSourceIndex % worldSpaceSources.Length];
            worldSpaceSourceIndex = (worldSpaceSourceIndex + 1) % worldSpaceSources.Length;
            return source;
        }

        void EnsureWorldSpaceSources()
        {
            int desiredCount = Mathf.Clamp(worldSpaceSourceCount, 1, 24);
            if (worldSpaceSources != null && worldSpaceSources.Length == desiredCount)
                return;

            worldSpaceSources = new AudioSource[desiredCount];
            for (int index = 0; index < desiredCount; index++)
            {
                GameObject sourceObject = new($"World Audio Source {index + 1:00}");
                sourceObject.transform.SetParent(transform, worldPositionStays: false);
                AudioSource source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                worldSpaceSources[index] = source;
            }
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
