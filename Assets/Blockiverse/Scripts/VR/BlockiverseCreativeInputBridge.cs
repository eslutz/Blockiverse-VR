using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Blockiverse.VR
{
    /// <summary>
    /// Drives creative block break/place/undo from the native controller ray interactor.
    /// Targeting uses the interactor's current 3D raycast hit; break/place are suppressed while
    /// the ray is over UI (native <see cref="XRRayInteractor.IsOverUIGameObject"/>) or while the
    /// interactor is disabled (e.g. teleport aiming), so menus and locomotion never break blocks.
    /// </summary>
    public sealed class BlockiverseCreativeInputBridge : MonoBehaviour
    {
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] XRRayInteractor interactionRay;
        [SerializeField] LineRenderer interactionLineRenderer;
        [SerializeField] XRInteractorLineVisual interactionLineVisual;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;

        UnityAction breakAction;
        UnityAction breakReleasedAction;
        UnityAction placeAction;
        UnityAction undoAction;
        UnityAction blockEditingToggleAction;
        bool capturedLineRendererDefault;
        bool lineRendererDefaultEnabled;
        bool capturedLineVisualDefault;
        bool lineVisualDefaultEnabled;

        // Hold-to-mine (§7.3): survival break is a timed hold on a fixed target. ToolHit cues +
        // chip VFX play on a cadence while held; releasing or losing the target cancels; the
        // harvest submits when the block's work time elapses. Creative break stays instant.
        const float MineHitCueIntervalSeconds = 0.4f;

        BlockiverseAudioCuePlayer audioCuePlayer;
        BlockiverseVfxCuePlayer vfxCuePlayer;
        bool mining;
        BlockPosition miningTarget;
        float miningElapsedSeconds;
        float miningRequiredSeconds;
        float nextMineCueTime;

        public XRRayInteractor InteractionRay => interactionRay;

        public void Configure(
            BlockiverseInputRig rig,
            XRRayInteractor ray,
            CreativeInteractionController controller)
        {
            Unbind();
            inputRig = rig;
            interactionRay = ray;
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
            ApplyInteractionRayVisualState();

            if (interactionController == null)
                return;

            if (TryGetTarget(out BlockPosition target, out Vector3 normal))
                interactionController.UpdatePreview(target, normal);
            else
                interactionController.HidePreview();

            TickMining(target);
        }

        // Advances an active hold-to-mine: cancels when the aim leaves the started block or the
        // trigger is no longer held, plays the strike cadence, and submits the harvest when the
        // block's work time elapses.
        void TickMining(BlockPosition currentTarget)
        {
            if (!mining)
                return;

            bool stillHeld = inputRig == null || inputRig.IsBreakHeld;
            bool stillAimed = interactionController.CurrentTarget.HasValue && currentTarget == miningTarget;

            if (!stillHeld || !stillAimed || !SurvivalInteractionActive)
            {
                CancelMining();
                return;
            }

            miningElapsedSeconds += Time.deltaTime;

            if (Time.time >= nextMineCueTime)
            {
                nextMineCueTime = Time.time + MineHitCueIntervalSeconds;
                PlayMineStrikeFeedback();
            }

            if (miningElapsedSeconds >= miningRequiredSeconds)
            {
                mining = false;
                survivalSync.TrySubmitHarvest(miningTarget, out _);
            }
        }

        void CancelMining()
        {
            mining = false;
        }

        void PlayMineStrikeFeedback()
        {
            if (audioCuePlayer == null)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
            if (vfxCuePlayer == null)
                vfxCuePlayer = FindFirstObjectByType<BlockiverseVfxCuePlayer>();

            var worldCenter = new Vector3(miningTarget.X + 0.5f, miningTarget.Y + 0.5f, miningTarget.Z + 0.5f);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ToolHitSoft, worldCenter);
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
            inputRig.UndoPressed.RemoveListener(undoAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            inputRig.BreakPressed.AddListener(breakAction);
            inputRig.BreakReleased.AddListener(breakReleasedAction);
            inputRig.PlacePressed.AddListener(placeAction);
            inputRig.UndoPressed.AddListener(undoAction);
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
            inputRig.UndoPressed.RemoveListener(undoAction);
            inputRig.BlockEditingTogglePressed.RemoveListener(blockEditingToggleAction);
            CancelMining();
        }

        void EnsureActions()
        {
            breakAction ??= TryBreakTarget;
            breakReleasedAction ??= OnBreakReleased;
            placeAction ??= TryPlaceTarget;
            undoAction ??= TryUndo;
            blockEditingToggleAction ??= ToggleBlockEditing;
        }

        void DiscoverDependencies()
        {
            if (inputRig == null)
                inputRig = GetComponentInParent<BlockiverseInputRig>() ?? FindFirstObjectByType<BlockiverseInputRig>();

            if (interactionRay == null)
                interactionRay = GetComponentInChildren<XRRayInteractor>(true);

            DiscoverInteractionRayVisuals();

            if (interactionController == null && Application.isPlaying)
                interactionController = FindFirstObjectByType<CreativeInteractionController>();

            if (survivalSync == null && Application.isPlaying)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>();
        }

        // The current interaction mode for this player (resolved from the survival sync).
        public PlayerModeState CurrentMode =>
            survivalSync != null ? survivalSync.CurrentMode : PlayerModeState.Creative;

        // Flips between survival and creative interaction. Invoked by the mode-toggle menu action.
        public void ToggleSurvivalCreativeMode()
        {
            DiscoverDependencies();
            survivalSync?.ToggleMode();
        }

        bool SurvivalInteractionActive => survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;

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
            capturedLineVisualDefault = true;
        }

        void TryBreakTarget()
        {
            if (!TryGetTarget(out BlockPosition target, out _))
                return;

            // Survival mode: hold-to-mine — the press starts a timed hold whose harvest submits
            // when the block's work time elapses (server-authoritative on submit). Blocks the
            // preview already rejects (wrong tool, full inventory, no rule) submit immediately so
            // the host's rejection feedback (e.g. ToolWrong) plays. Creative mode: instant delete.
            if (SurvivalInteractionActive)
            {
                if (survivalSync.TryEvaluateHarvestWorkSeconds(target, out float requiredSeconds) &&
                    requiredSeconds > 0f)
                {
                    mining = true;
                    miningTarget = target;
                    miningElapsedSeconds = 0f;
                    miningRequiredSeconds = requiredSeconds;
                    nextMineCueTime = Time.time;
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
            CancelMining();
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

        void TryUndo()
        {
            interactionController?.UndoLast();
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

            if (interactionController == null || !interactionController.BlockEditingEnabled || !CanInteract())
                return false;

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

            bool shouldShow = interactionController == null || interactionController.BlockEditingEnabled;

            if (interactionLineRenderer != null)
                interactionLineRenderer.enabled = shouldShow && (!capturedLineRendererDefault || lineRendererDefaultEnabled);

            if (interactionLineVisual != null)
                interactionLineVisual.enabled = shouldShow && (!capturedLineVisualDefault || lineVisualDefaultEnabled);

            if (!shouldShow)
                interactionController?.HidePreview();
        }

        bool CanInteract()
        {
            return interactionRay != null &&
                interactionRay.isActiveAndEnabled &&
                !interactionRay.IsOverUIGameObject();
        }
    }
}
