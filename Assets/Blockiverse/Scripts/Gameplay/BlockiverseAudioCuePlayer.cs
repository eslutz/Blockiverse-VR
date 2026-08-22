using System;
using System.Collections.Generic;
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
        MultiplayerLeave,
        PlayerHurt,
        LowHealth,
        PlayerDeath,
        // Appended only. This enum is serialized BY VALUE into prefab fields
        // (panel show/hide cues, hotbar feedback), so reordering or inserting
        // silently repoints every one of them at a different sound.
        Eat,
        Drink,
        WaterScoop,
        WaterSplash,
        SwimStroke,
        SubmergedLoop,
        EmberflowLoop,
        Landing
    }

    /// <summary>
    /// Footstep clips for one walkable surface. Unity cannot serialize a jagged
    /// array, so the per-surface banks are a flat list of these instead.
    /// </summary>
    [Serializable]
    public sealed class BlockiverseFootstepBank
    {
        public BlockiverseSurfaceFamily Surface;
        public AudioClip[] Clips = Array.Empty<AudioClip>();
    }

    /// <summary>
    /// Break and place clips for one block material family (ruleset §13).
    /// </summary>
    [Serializable]
    public sealed class BlockiverseMaterialBank
    {
        public BlockiverseMaterialFamily Family;
        public AudioClip BreakClip;
        public AudioClip PlaceClip;
    }

    /// <summary>
    /// Central sound player for one-shot and persistent loop cues. Auto-plays break/place cues from
    /// a creative interaction controller's mutation events and exposes cue APIs for UI and world
    /// feedback. Clips are generated original cues under Assets/Blockiverse/Audio and assigned on
    /// the prefab.
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
        [SerializeField] AudioClip playerHurtClip;
        [SerializeField] AudioClip lowHealthClip;
        [SerializeField] AudioClip playerDeathClip;
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
        [SerializeField] AudioClip eatClip;
        [SerializeField] AudioClip drinkClip;
        [SerializeField] AudioClip waterScoopClip;
        [SerializeField] AudioClip waterSplashClip;
        [SerializeField] AudioClip swimStrokeClip;
        [SerializeField] AudioClip submergedLoopClip;
        [SerializeField] AudioClip emberflowLoopClip;
        [SerializeField] AudioClip landingClip;
        // The Classic Block Sounds easter egg: the original synthesized cues,
        // kept alive so break/place can be switched back to how they used to sound.
        [SerializeField] AudioClip classicBlockBreakClip;
        [SerializeField] AudioClip classicBlockPlaceClip;
        // Per-material break/place and per-surface footsteps (ruleset §13, §5).
        [SerializeField] BlockiverseMaterialBank[] materialBanks = Array.Empty<BlockiverseMaterialBank>();
        [SerializeField] BlockiverseFootstepBank[] footstepBanks = Array.Empty<BlockiverseFootstepBank>();
        // Mining fires on a cadence, so a single clip machine-guns; these rotate.
        [SerializeField] AudioClip[] toolHitSoftClips = Array.Empty<AudioClip>();
        [SerializeField] AudioClip[] toolHitStoneClips = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;
        [SerializeField, Range(1, 24)] int worldSpaceSourceCount = 8;

        bool subscribed;
        int footstepClipIndex;
        int toolHitSoftIndex;
        int toolHitStoneIndex;
        AudioSource[] worldSpaceSources = Array.Empty<AudioSource>();
        int worldSpaceSourceIndex;
        readonly Dictionary<BlockiverseAudioCue, AudioSource> loopSources = new();
        readonly Dictionary<BlockiverseSurfaceFamily, int> footstepSurfaceIndices = new();

        public event Action<BlockiverseAudioCue, AudioClip> CuePlayed;
        public int FootstepClipCount => CountAssignedFootstepClips();
        public int ActiveLoopCount => CountActiveLoopSources();
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
            RefreshLoopVolumes();
        }

        public void PlayCue(BlockiverseAudioCue cue)
        {
            if (IsLoopCue(cue))
            {
                StartLoop(cue);
                return;
            }

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
            if (IsLoopCue(cue))
            {
                StartLoopAt(cue, worldPosition);
                return;
            }

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
            ApplyAttenuation(source, MaxDistanceFor(cue));
            source.PlayOneShot(clip, resolvedVolume);
            CuePlayed?.Invoke(cue, clip);
        }

        public bool StartLoop(BlockiverseAudioCue cue)
        {
            return StartLoopInternal(cue, null);
        }

        public bool StartLoopAt(BlockiverseAudioCue cue, Vector3 worldPosition)
        {
            return StartLoopInternal(cue, worldPosition);
        }

        public bool StopLoop(BlockiverseAudioCue cue)
        {
            if (!loopSources.TryGetValue(cue, out AudioSource source))
                return false;

            loopSources.Remove(cue);
            DestroyLoopSource(source);
            return true;
        }

        public void StopAllLoops()
        {
            if (loopSources.Count == 0)
                return;

            var cues = new List<BlockiverseAudioCue>(loopSources.Keys);
            foreach (BlockiverseAudioCue cue in cues)
                StopLoop(cue);
        }

        public bool IsLoopActive(BlockiverseAudioCue cue)
        {
            return loopSources.TryGetValue(cue, out AudioSource source) && source != null;
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
                case BlockiverseAudioCue.PlayerHurt:
                    playerHurtClip = clip;
                    break;
                case BlockiverseAudioCue.LowHealth:
                    lowHealthClip = clip;
                    break;
                case BlockiverseAudioCue.PlayerDeath:
                    playerDeathClip = clip;
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
                case BlockiverseAudioCue.Eat:
                    eatClip = clip;
                    break;
                case BlockiverseAudioCue.Drink:
                    drinkClip = clip;
                    break;
                case BlockiverseAudioCue.WaterScoop:
                    waterScoopClip = clip;
                    break;
                case BlockiverseAudioCue.WaterSplash:
                    waterSplashClip = clip;
                    break;
                case BlockiverseAudioCue.SwimStroke:
                    swimStrokeClip = clip;
                    break;
                case BlockiverseAudioCue.SubmergedLoop:
                    submergedLoopClip = clip;
                    break;
                case BlockiverseAudioCue.EmberflowLoop:
                    emberflowLoopClip = clip;
                    break;
                case BlockiverseAudioCue.Landing:
                    landingClip = clip;
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
            if (cue == BlockiverseAudioCue.Footstep)
                return CountAssignedFootstepClips() > 0 || CountAssignedSurfaceClips() > 0;
            if (cue == BlockiverseAudioCue.ToolHitSoft)
                return toolHitSoftClip != null || CountAssigned(toolHitSoftClips) > 0;
            if (cue == BlockiverseAudioCue.ToolHitStone)
                return toolHitStoneClip != null || CountAssigned(toolHitStoneClips) > 0;
            return ResolveFixedClip(cue) != null;
        }

        void Awake()
        {
            EnsureReferences();
        }

        void Update()
        {
            // Cheap: at most a handful of loop sources, and only while any are
            // active. Without it, volume changes do not reach a playing loop until
            // it is stopped and restarted — barely noticeable with a 1.2 s synth
            // bed, obvious with a 24 s recorded one.
            if (loopSources.Count > 0)
                RefreshLoopVolumes();
        }

        void OnEnable()
        {
            EnsureReferences();
            Subscribe();
        }

        void OnDisable()
        {
            StopAllLoops();
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
            // BlockChange carries both sides of the edit, so the material is known
            // here without any new plumbing: a break should sound like the block
            // that was removed, a placement like the block that arrived.
            bool isBreak = change.NewBlock == BlockRegistry.Air;
            BlockiverseAudioCue cue = isBreak ? BlockiverseAudioCue.BlockBreak : BlockiverseAudioCue.BlockPlace;
            BlockId material = isBreak ? change.PreviousBlock : change.NewBlock;
            BlockiverseMaterialFamily family =
                BlockiverseBlockFeedbackCues.FamilyForBlock(BlockRegistry.Default, material);

            var center = new Vector3(change.Position.X + 0.5f, change.Position.Y + 0.5f, change.Position.Z + 0.5f);
            PlayMaterialCueAt(cue, family, center);
        }

        AudioClip ResolveClip(BlockiverseAudioCue cue)
        {
            if (cue == BlockiverseAudioCue.Footstep)
                return ResolveFootstepClip();

            // Classic Block Sounds replaces the block cues wholesale, including the
            // per-material variants — the point of the setting is to hear the two
            // original sounds again, not a material-aware reinterpretation of them.
            if (ClassicBlockSoundsActive)
            {
                if (cue == BlockiverseAudioCue.BlockBreak && classicBlockBreakClip != null)
                    return classicBlockBreakClip;
                if (cue == BlockiverseAudioCue.BlockPlace && classicBlockPlaceClip != null)
                    return classicBlockPlaceClip;
            }

            if (cue == BlockiverseAudioCue.ToolHitSoft)
                return ResolveRotating(toolHitSoftClips, ref toolHitSoftIndex) ?? toolHitSoftClip;
            if (cue == BlockiverseAudioCue.ToolHitStone)
                return ResolveRotating(toolHitStoneClips, ref toolHitStoneIndex) ?? toolHitStoneClip;

            return ResolveFixedClip(cue);
        }

        bool ClassicBlockSoundsActive =>
            feedbackSettings != null && feedbackSettings.ClassicBlockSoundsEnabled;

        /// <summary>
        /// Break or place clip for a specific material family, falling back to the
        /// generic cue when a family has no bank assigned.
        /// </summary>
        public AudioClip ResolveMaterialClip(BlockiverseAudioCue cue, BlockiverseMaterialFamily family)
        {
            if (cue != BlockiverseAudioCue.BlockBreak && cue != BlockiverseAudioCue.BlockPlace)
                return ResolveClip(cue);

            if (ClassicBlockSoundsActive)
                return ResolveClip(cue);

            if (materialBanks != null)
            {
                for (int i = 0; i < materialBanks.Length; i++)
                {
                    BlockiverseMaterialBank bank = materialBanks[i];
                    if (bank == null || bank.Family != family)
                        continue;

                    AudioClip clip = cue == BlockiverseAudioCue.BlockBreak ? bank.BreakClip : bank.PlaceClip;
                    if (clip != null)
                        return clip;
                    break;
                }
            }

            return ResolveFixedClip(cue);
        }

        /// <summary>
        /// Plays a block break/place cue using the clip for that block's material
        /// family. Falls back to the generic cue when the family is unmapped.
        /// </summary>
        public void PlayMaterialCueAt(BlockiverseAudioCue cue, BlockiverseMaterialFamily family,
                                      Vector3 worldPosition)
        {
            EnsureReferences();
            AudioClip clip = ResolveMaterialClip(cue, family);
            if (clip == null)
                return;

            float resolvedVolume = ResolveVolume(cue);
            if (resolvedVolume <= 0f)
                return;

            AudioSource source = ResolveWorldSpaceSource();
            if (source == null)
                return;

            source.transform.position = worldPosition;
            ApplyAttenuation(source, MaxDistanceFor(cue));
            source.PlayOneShot(clip, resolvedVolume);
            CuePlayed?.Invoke(cue, clip);
        }

        /// <summary>
        /// Plays the next footstep variant for a surface, rotating within that
        /// surface's bank so consecutive steps on the same ground differ.
        /// </summary>
        public void PlayFootstepAt(BlockiverseSurfaceFamily surface, Vector3 worldPosition)
        {
            EnsureReferences();
            AudioClip clip = ResolveSurfaceFootstepClip(surface) ?? ResolveFootstepClip();
            if (clip == null)
                return;

            float resolvedVolume = ResolveVolume(BlockiverseAudioCue.Footstep);
            if (resolvedVolume <= 0f)
                return;

            AudioSource source = ResolveWorldSpaceSource();
            if (source == null)
                return;

            source.transform.position = worldPosition;
            ApplyAttenuation(source, MaxDistanceFor(BlockiverseAudioCue.Footstep));
            source.PlayOneShot(clip, resolvedVolume);
            CuePlayed?.Invoke(BlockiverseAudioCue.Footstep, clip);
        }

        public AudioClip ResolveSurfaceFootstepClip(BlockiverseSurfaceFamily surface)
        {
            if (footstepBanks == null)
                return null;

            for (int i = 0; i < footstepBanks.Length; i++)
            {
                BlockiverseFootstepBank bank = footstepBanks[i];
                if (bank == null || bank.Surface != surface || bank.Clips == null)
                    continue;

                int index = footstepSurfaceIndices.TryGetValue(surface, out int stored) ? stored : 0;
                AudioClip clip = ResolveRotating(bank.Clips, ref index);
                footstepSurfaceIndices[surface] = index;
                return clip;
            }

            return null;
        }

        // Advances through a clip bank, skipping empty slots, and gives up after a
        // full lap rather than spinning on an all-null array.
        static AudioClip ResolveRotating(AudioClip[] clips, ref int index)
        {
            if (clips == null || clips.Length == 0)
                return null;

            for (int attempts = 0; attempts < clips.Length; attempts++)
            {
                AudioClip clip = clips[index % clips.Length];
                index = (index + 1) % clips.Length;
                if (clip != null)
                    return clip;
            }

            return null;
        }

        public void ConfigureMaterialBanks(BlockiverseMaterialBank[] banks)
        {
            materialBanks = banks ?? Array.Empty<BlockiverseMaterialBank>();
        }

        public void ConfigureFootstepBanks(BlockiverseFootstepBank[] banks)
        {
            footstepBanks = banks ?? Array.Empty<BlockiverseFootstepBank>();
            footstepSurfaceIndices.Clear();
        }

        public void ConfigureToolHitClips(AudioClip[] soft, AudioClip[] stone)
        {
            toolHitSoftClips = soft ?? Array.Empty<AudioClip>();
            toolHitStoneClips = stone ?? Array.Empty<AudioClip>();
            toolHitSoftIndex = 0;
            toolHitStoneIndex = 0;
        }

        public void ConfigureClassicBlockClips(AudioClip breakClip, AudioClip placeClip)
        {
            classicBlockBreakClip = breakClip;
            classicBlockPlaceClip = placeClip;
        }

        public int MaterialBankCount => materialBanks?.Length ?? 0;
        public int FootstepBankCount => footstepBanks?.Length ?? 0;
        public bool HasClassicBlockClips => classicBlockBreakClip != null && classicBlockPlaceClip != null;

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
                BlockiverseAudioCue.PlayerHurt => playerHurtClip != null ? playerHurtClip : toolHitStoneClip,
                BlockiverseAudioCue.LowHealth => lowHealthClip != null ? lowHealthClip : craftFailClip,
                BlockiverseAudioCue.PlayerDeath => playerDeathClip != null ? playerDeathClip : thunderNearClip,
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
                BlockiverseAudioCue.Eat => eatClip,
                BlockiverseAudioCue.Drink => drinkClip,
                BlockiverseAudioCue.WaterScoop => waterScoopClip,
                BlockiverseAudioCue.WaterSplash => waterSplashClip,
                BlockiverseAudioCue.SwimStroke => swimStrokeClip,
                BlockiverseAudioCue.SubmergedLoop => submergedLoopClip,
                BlockiverseAudioCue.EmberflowLoop => emberflowLoopClip,
                BlockiverseAudioCue.Landing => landingClip,
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
                BlockiverseAudioCue.NightAmbienceLoop or
                BlockiverseAudioCue.SubmergedLoop or
                BlockiverseAudioCue.EmberflowLoop => BlockiverseAudioCategory.Weather,
                _ => BlockiverseAudioCategory.Effects
            };
        }

        public static bool IsLoopCue(BlockiverseAudioCue cue)
        {
            return cue is BlockiverseAudioCue.TorchLoop or
                BlockiverseAudioCue.CampfireLoop or
                BlockiverseAudioCue.RainLightLoop or
                BlockiverseAudioCue.RainHeavyLoop or
                BlockiverseAudioCue.SnowWindLoop or
                BlockiverseAudioCue.CaveAmbienceLoop or
                BlockiverseAudioCue.DayAmbienceLoop or
                BlockiverseAudioCue.NightAmbienceLoop or
                BlockiverseAudioCue.SubmergedLoop or
                BlockiverseAudioCue.EmberflowLoop;
        }

        float ResolveVolume(BlockiverseAudioCue cue)
        {
            float categoryVolume = feedbackSettings != null
                ? feedbackSettings.ResolveVolume(GetCategory(cue))
                : 1.0f;
            return Mathf.Clamp01(volume * categoryVolume);
        }

        bool StartLoopInternal(BlockiverseAudioCue cue, Vector3? worldPosition)
        {
            if (!IsLoopCue(cue))
                return false;

            EnsureReferences();
            AudioClip clip = ResolveClip(cue);
            if (clip == null)
                return false;

            float resolvedVolume = ResolveVolume(cue);
            if (resolvedVolume <= 0f)
                return false;

            if (loopSources.TryGetValue(cue, out AudioSource existingSource) && existingSource != null)
            {
                ApplyLoopSourceSettings(existingSource, cue, worldPosition);
                return false;
            }

            AudioSource source = CreateLoopSource(cue);
            source.clip = clip;
            source.loop = true;
            ApplyLoopSourceSettings(source, cue, worldPosition);
            loopSources[cue] = source;
            source.Play();
            CuePlayed?.Invoke(cue, clip);
            return true;
        }

        AudioSource CreateLoopSource(BlockiverseAudioCue cue)
        {
            GameObject sourceObject = new($"{cue} Loop Audio Source");
            sourceObject.transform.SetParent(transform, worldPositionStays: false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }

        void ApplyLoopSourceSettings(AudioSource source, BlockiverseAudioCue cue, Vector3? worldPosition)
        {
            source.volume = ResolveVolume(cue);
            source.spatialBlend = worldPosition.HasValue ? 1f : 0f;
            if (worldPosition.HasValue)
            {
                source.transform.position = worldPosition.Value;
                ApplyAttenuation(source, MaxDistanceFor(cue));
            }
            else
            {
                source.transform.localPosition = Vector3.zero;
            }
        }

        // Ruleset §8 distance attenuation. Without this every spatial source runs
        // Unity's default logarithmic 1 m / 500 m curve, which puts a torch on the
        // far side of the world at an audible level and gives a block break no
        // sense of distance at all.
        const float DefaultMaxDistanceMeters = 12.0f;

        static void ApplyAttenuation(AudioSource source, float maxDistance)
        {
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.0f;
            source.maxDistance = maxDistance;
            // Voxel worlds have no fast-moving sources; Doppler only smears cues.
            source.dopplerLevel = 0f;
        }

        static float MaxDistanceFor(BlockiverseAudioCue cue)
        {
            return cue switch
            {
                BlockiverseAudioCue.TorchLoop => 4.0f,
                BlockiverseAudioCue.CampfireLoop => 10.0f,
                BlockiverseAudioCue.EmberflowLoop => 8.0f,
                BlockiverseAudioCue.SubmergedLoop => 6.0f,
                BlockiverseAudioCue.Footstep => 8.0f,
                BlockiverseAudioCue.ContainerOpen or
                BlockiverseAudioCue.ContainerClose => 8.0f,
                _ => DefaultMaxDistanceMeters,
            };
        }

        void RefreshLoopVolumes()
        {
            foreach (KeyValuePair<BlockiverseAudioCue, AudioSource> loopSource in loopSources)
            {
                if (loopSource.Value != null)
                    loopSource.Value.volume = ResolveVolume(loopSource.Key);
            }
        }

        int CountActiveLoopSources()
        {
            int count = 0;
            foreach (AudioSource source in loopSources.Values)
            {
                if (source != null)
                    count++;
            }

            return count;
        }

        static void DestroyLoopSource(AudioSource source)
        {
            if (source == null)
                return;

            GameObject sourceObject = source.gameObject;
            source.Stop();
            if (Application.isPlaying)
                Destroy(sourceObject);
            else
                DestroyImmediate(sourceObject);
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
                ApplyAttenuation(source, DefaultMaxDistanceMeters);
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
            return CountAssigned(footstepClips);
        }

        static int CountAssigned(AudioClip[] clips)
        {
            if (clips == null)
                return 0;

            int count = 0;
            foreach (AudioClip clip in clips)
            {
                if (clip != null)
                    count++;
            }

            return count;
        }

        int CountAssignedSurfaceClips()
        {
            if (footstepBanks == null)
                return 0;

            int count = 0;
            foreach (BlockiverseFootstepBank bank in footstepBanks)
            {
                if (bank != null)
                    count += CountAssigned(bank.Clips);
            }

            return count;
        }

        /// <summary>Number of surfaces with at least one footstep clip assigned.</summary>
        public int AssignedFootstepSurfaceCount()
        {
            if (footstepBanks == null)
                return 0;

            int count = 0;
            foreach (BlockiverseFootstepBank bank in footstepBanks)
            {
                if (bank != null && CountAssigned(bank.Clips) > 0)
                    count++;
            }

            return count;
        }

        /// <summary>Number of material families with both a break and a place clip.</summary>
        public int AssignedMaterialFamilyCount()
        {
            if (materialBanks == null)
                return 0;

            int count = 0;
            foreach (BlockiverseMaterialBank bank in materialBanks)
            {
                if (bank != null && bank.BreakClip != null && bank.PlaceClip != null)
                    count++;
            }

            return count;
        }
    }
}
