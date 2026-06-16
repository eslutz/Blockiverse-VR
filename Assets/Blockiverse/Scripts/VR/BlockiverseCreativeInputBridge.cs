using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Blockiverse.VR
{
    /// <summary>
    /// Drives creative block break/place from the native controller ray interactor.
    /// Targeting uses the interactor's current 3D raycast hit; break/place are suppressed while
    /// the ray is over UI (native <see cref="XRRayInteractor.IsOverUIGameObject"/>) or while the
    /// interactor is disabled (e.g. teleport aiming), so menus and locomotion never break blocks.
    /// </summary>
    public sealed class BlockiverseCreativeInputBridge : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] XRRayInteractor leftInteractionRay;
        [SerializeField] XRRayInteractor rightInteractionRay;
        [SerializeField] LineRenderer interactionLineRenderer;
        [SerializeField] XRInteractorLineVisual interactionLineVisual;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] BlockiverseComfortSettings comfortSettings;

        UnityAction breakAction;
        UnityAction breakReleasedAction;
        UnityAction placeAction;
        UnityAction blockEditingToggleAction;
        bool capturedLineRendererDefault;
        bool lineRendererDefaultEnabled;
        bool capturedLineVisualDefault;
        bool lineVisualDefaultEnabled;
        bool lineVisualDefaultOverrideLineLength;
        float lineVisualDefaultLength;

        // Hold-to-mine (§7.3): survival break is a timed hold on a fixed target. ToolHit cues +
        // chip VFX play on a cadence while held; releasing or losing the target cancels; the
        // harvest submits when the block's work time elapses. Creative break stays instant.
        const float MineHitCueIntervalSeconds = 0.4f;

        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        bool mining;
        bool miningStartedByToggle;
        BlockPosition miningTarget;
        float miningElapsedSeconds;
        float miningRequiredSeconds;
        float nextMineCueTime;

        public XRRayInteractor InteractionRay => interactionRay;
        public event Action<BlockPosition, float, float> MiningProgressChanged;
        public event Action MiningProgressCleared;

        public void Configure(
            BlockiverseInputRig rig,
            XRRayInteractor ray,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            SetInteractionRay(ray);
            leftInteractionRay = null;
            rightInteractionRay = null;
            interactionLineRenderer = null;
            interactionLineVisual = null;
            capturedLineRendererDefault = false;
            capturedLineVisualDefault = false;
            interactionController = controller;
            DiscoverInteractionRayVisuals();
            ApplyInteractionRayVisualState();
            Bind();
        }

        void OnEnable()
        {
            DiscoverDependencies();
            Bind();
        }

        void OnDisable()
        {
            Unbind();
        }

        void Update()
        {
            RefreshActiveInteractionRay();
            ApplyInteractionRayVisualState();

            if (interactionController == null)
                return;

            if (TryGetTarget(out BlockPosition target, out Vector3 normal))
                interactionController.UpdatePreview(target, normal);
            else
                interactionController.HidePreview();

            TickMining(target);
        }

        // Advances an active mine action: hold mode cancels when the trigger is released; toggle
        // mode keeps mining until the started target is lost, blocked, completed, or toggled off.
        void TickMining(BlockPosition currentTarget)
        {
            if (!mining)
                return;

            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                CancelMining();
                return;
            }

            bool stillHeld = miningStartedByToggle || inputRig == null || inputRig.IsBreakHeld;
            bool stillAimed = interactionController.CurrentTarget.HasValue && currentTarget == miningTarget;

            if (!stillHeld || !stillAimed || !SurvivalInteractionActive)
            {
                CancelMining();
                return;
            }

            miningElapsedSeconds += Time.deltaTime;
            RaiseMiningProgress();

            if (Time.time >= nextMineCueTime)
            {
                nextMineCueTime = Time.time + MineHitCueIntervalSeconds;
                PlayMineStrikeFeedback();
            }

            if (miningElapsedSeconds >= miningRequiredSeconds)
            {
                mining = false;
                miningStartedByToggle = false;
                MiningProgressCleared?.Invoke();
                survivalSync.TrySubmitHarvest(miningTarget, out _);
            }
        }

        void CancelMining()
        {
            bool wasMining = mining;
            mining = false;
            miningStartedByToggle = false;

            if (wasMining)
                MiningProgressCleared?.Invoke();
        }

        void PlayMineStrikeFeedback()
        {
            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            var worldCenter = new Vector3(miningTarget.X + 0.5f, miningTarget.Y + 0.5f, miningTarget.Z + 0.5f);
            BlockiverseAudioCue cue = interactionController != null &&
                                      interactionController.TryGetBlock(miningTarget, out BlockId targetBlock)
                ? BlockiverseBlockFeedbackCues.ToolHitForBlock(BlockRegistry.Default, targetBlock)
                : BlockiverseAudioCue.ToolHitSoft;
            audioCuePlayer?.PlayCueAt(cue, worldCenter);
            vfxCuePlayer?.PlayCue(BlockiverseVfxCue.BlockChipBurst, worldCenter);
        }

        void Bind()
        {
            DiscoverDependencies();

            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.BreakReleased.RemoveListener(breakReleasedAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            inputRig.BreakPressed.AddListener(breakAction);
            inputRig.BreakReleased.AddListener(breakReleasedAction);
            inputRig.PlacePressed.AddListener(placeAction);
            inputRig.BlockEditingTogglePressed.AddListener(blockEditingToggleAction);
        }

        void Unbind()
        {
            if (inputRig == null)
                return;

            EnsureActions();
            inputRig.BreakPressed.RemoveListener(breakAction);
            inputRig.BreakReleased.RemoveListener(breakReleasedAction);
            inputRig.PlacePressed.RemoveListener(placeAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            CancelMining();
        }

        void EnsureActions()
        {
            breakAction ??= TryBreakTarget;
            breakReleasedAction ??= OnBreakReleased;
            placeAction ??= TryPlaceTarget;
            blockEditingToggleAction ??= ToggleBlockEditing;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            DiscoverInteractionRays();
            RefreshActiveInteractionRay();

            if (interactionRay == null)
                SetInteractionRay(GetComponentInChildren<XRRayInteractor>(true));

            DiscoverInteractionRayVisuals();

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();

            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>();

            if (comfortSettings == null)
                comfortSettings = GetComponentInParent<BlockiverseComfortSettings>() ??
                    FindFirstObjectByType<BlockiverseComfortSettings>(FindObjectsInactive.Include);
        }

        // The current interaction mode for this player (resolved from the survival sync).
        public PlayerModeState CurrentMode =>
            survivalSync != null ? survivalSync.CurrentMode : PlayerModeState.Creative;
        public bool CanToggleSurvivalCreativeMode =>
            survivalSync != null && survivalSync.CanToggleMode;

        // Flips between survival and creative interaction. Invoked by the mode-toggle menu action.
        public bool ToggleSurvivalCreativeMode()
        {
            DiscoverDependencies();
            return survivalSync != null && survivalSync.ToggleMode();
        }

        bool SurvivalInteractionActive => survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;
        bool UseToggleToMine => comfortSettings != null && comfortSettings.ToggleToMineEnabled;

        void DiscoverInteractionRayVisuals()
        {
            if (interactionRay == null)
                return;

            if (interactionLineRenderer == null)
                interactionLineRenderer = interactionRay.GetComponent<LineRenderer>();

            CaptureLineRendererDefault();

            if (interactionLineVisual == null)
                interactionLineVisual = interactionRay.GetComponent<XRInteractorLineVisual>();

            CaptureLineVisualDefault();
        }

        void DiscoverInteractionRays()
        {
            if (inputRig != null)
            {
                leftInteractionRay ??= inputRig.LeftInteractionRay;
                rightInteractionRay ??= inputRig.RightInteractionRay;

                foreach (BlockiverseLocomotionRayMediator mediator in inputRig.GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true))
                    AssignInteractionRay(mediator.Hand, mediator.InteractionRay);
            }

            foreach (BlockiverseLocomotionRayMediator mediator in GetComponentsInChildren<BlockiverseLocomotionRayMediator>(true))
                AssignInteractionRay(mediator.Hand, mediator.InteractionRay);
        }

        void AssignInteractionRay(BlockiverseControllerRole hand, XRRayInteractor ray)
        {
            if (ray == null)
                return;

            if (hand == BlockiverseControllerRole.Left)
                leftInteractionRay ??= ray;
            else
                rightInteractionRay ??= ray;
        }

        void RefreshActiveInteractionRay()
        {
            XRRayInteractor resolved = ResolveActiveInteractionRay();

            if (resolved != null && resolved != interactionRay)
                SetInteractionRay(resolved);
        }

        XRRayInteractor ResolveActiveInteractionRay()
        {
            DiscoverInteractionRays();

            if (inputRig != null)
            {
                XRRayInteractor dominantRay = inputRig.ActiveToolHand == BlockiverseControllerRole.Left
                    ? leftInteractionRay
                    : rightInteractionRay;

                if (dominantRay != null)
                    return dominantRay;
            }

            return interactionRay ?? rightInteractionRay ?? leftInteractionRay;
        }

        void SetInteractionRay(XRRayInteractor ray)
        {
            if (interactionRay == ray)
                return;

            interactionRay = ray;
            interactionLineRenderer = null;
            interactionLineVisual = null;
            capturedLineRendererDefault = false;
            capturedLineVisualDefault = false;
        }

        void CaptureLineRendererDefault()
        {
            if (capturedLineRendererDefault || interactionLineRenderer == null)
                return;

            lineRendererDefaultEnabled = interactionLineRenderer.enabled;
            capturedLineRendererDefault = true;
        }

        void CaptureLineVisualDefault()
        {
            if (capturedLineVisualDefault || interactionLineVisual == null)
                return;

            lineVisualDefaultEnabled = interactionLineVisual.enabled;
            lineVisualDefaultOverrideLineLength = interactionLineVisual.overrideInteractorLineLength;
            lineVisualDefaultLength = interactionLineVisual.lineLength;
            capturedLineVisualDefault = true;
        }

        void TryBreakTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out _))
                return;

            // Survival mode: hold-to-mine or toggle-to-mine starts a timed action whose harvest
            // submits when the block's work time elapses. Preview failures such as full inventory
            // or no harvest rule submit immediately so host-side rejection feedback can play.
            if (SurvivalInteractionActive)
            {
                if (mining && miningStartedByToggle && target == miningTarget)
                {
                    CancelMining();
                    return;
                }

                if (survivalSync.TryEvaluateHarvestWorkSeconds(target, out float requiredSeconds) &&
                    requiredSeconds > 0f)
                {
                    StartMining(target, requiredSeconds, UseToggleToMine);
                    return;
                }

                survivalSync.TrySubmitHarvest(target, out _);
            }
            else
            {
                interactionController.TryBreakBlock(target);
            }
        }

        void OnBreakReleased()
        {
            if (!miningStartedByToggle)
                CancelMining();
        }

        void StartMining(BlockPosition target, float requiredSeconds, bool startedByToggle)
        {
            mining = true;
            miningStartedByToggle = startedByToggle;
            miningTarget = target;
            miningElapsedSeconds = 0f;
            miningRequiredSeconds = requiredSeconds;
            nextMineCueTime = Time.time;
            RaiseMiningProgress();
        }

        void RaiseMiningProgress()
        {
            if (miningRequiredSeconds <= 0f)
                return;

            MiningProgressChanged?.Invoke(
                miningTarget,
                miningElapsedSeconds,
                miningRequiredSeconds);
        }

        void TryPlaceTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out Vector3 normal))
                return;

            // Survival mode: use the held item authoritatively — place the held block into the adjacent
            // cell, or (Feller held on a branchwood_log) strip the log into smooth_branchwood. The sync
            // decides based on the held item. Creative mode: free placement of the selected catalog block.
            if (SurvivalInteractionActive)
            {
                BlockPosition placement = CreativeInteractionController.ComputePlacementPosition(target, normal);
                survivalSync.TrySubmitUse(target, placement, out _);
            }
            else
            {
                interactionController.TryPlaceBlock(target, normal);
            }
        }

        void ToggleBlockEditing()
        {
            if (interactionController == null)
                return;

            interactionController.ToggleBlockEditingEnabled();
            ApplyInteractionRayVisualState();
        }

        bool TryGetTarget(out BlockPosition target, out Vector3 normal)
        {
            target = default;
            normal = Vector3.up;

            if (interactionController == null ||
                !BlockiverseRuntimeState.AllowWorldInput ||
                !interactionController.BlockEditingEnabled ||
                !CanInteract())
            {
                return false;
            }

            if (!interactionRay.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                return false;

            VoxelChunkTarget chunkTarget = hit.collider.GetComponentInParent<VoxelChunkTarget>();

            if (chunkTarget == null || !chunkTarget.TryGetHitBlock(hit, out target))
                return false;

            normal = hit.normal;
            return true;
        }

        void ApplyInteractionRayVisualState()
        {
            DiscoverInteractionRayVisuals();

            bool worldInputAllowed = BlockiverseRuntimeState.AllowWorldInput;
            bool blockEditingVisible = interactionController == null || interactionController.BlockEditingEnabled;
            bool shouldShow = !worldInputAllowed || blockEditingVisible;

            if (interactionLineRenderer != null)
                interactionLineRenderer.enabled = shouldShow && (!capturedLineRendererDefault || lineRendererDefaultEnabled);

            if (interactionLineVisual != null)
                interactionLineVisual.enabled = shouldShow && (!capturedLineVisualDefault || lineVisualDefaultEnabled);

            RestoreInteractionRayLengthState();

            if (!worldInputAllowed || !blockEditingVisible)
                interactionController?.HidePreview();
        }

        void RestoreInteractionRayLengthState()
        {
            if (interactionLineVisual == null || !capturedLineVisualDefault)
                return;

            interactionLineVisual.overrideInteractorLineLength = lineVisualDefaultOverrideLineLength;
            interactionLineVisual.lineLength = lineVisualDefaultLength;
        }

        bool CanInteract()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                !interactionRay.IsOverUIGameObject();
        }
    }
}
