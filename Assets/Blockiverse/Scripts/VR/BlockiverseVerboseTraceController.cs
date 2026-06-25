using System;
using System.Globalization;
using System.IO;
using System.Text;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseVerboseTraceController : MonoBehaviour
    {
        const float DefaultSampleIntervalSeconds = 0.2f;

        [SerializeField, Min(0.05f)] float sampleIntervalSeconds = DefaultSampleIntervalSeconds;
        [SerializeField] BlockiverseInputRig inputRig;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] CreativeInteractionController interactionController;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] EnvironmentDynamicsController environmentDynamics;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseVfxCuePlayer vfxCuePlayer;
        [SerializeField] BlockiverseMusicController musicController;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        BlockiverseRollingTraceFileSink runtimeFileSink;
        bool subscribed;
        bool ownsRuntimeTrace;
        float nextSnapshotTime;

        public float SampleIntervalSeconds
        {
            get => sampleIntervalSeconds;
            set => sampleIntervalSeconds = Mathf.Max(0.05f, value);
        }

        public void Configure(
            BlockiverseInputRig targetInputRig,
            CreativeWorldManager targetWorldManager,
            CreativeInteractionController targetInteractionController,
            BlockiverseAudioCuePlayer targetAudioCuePlayer,
            BlockiverseVfxCuePlayer targetVfxCuePlayer,
            BlockiverseMusicController targetMusicController,
            BlockiverseInteractionHaptics targetInteractionHaptics)
        {
            Unsubscribe();
            inputRig = targetInputRig;
            worldManager = targetWorldManager;
            interactionController = targetInteractionController;
            audioCuePlayer = targetAudioCuePlayer;
            vfxCuePlayer = targetVfxCuePlayer;
            musicController = targetMusicController;
            interactionHaptics = targetInteractionHaptics;
            DiscoverOptionalDependencies();
            Subscribe();
        }

        public void CaptureSnapshotNow()
        {
            var payload = new StringBuilder(512);
            payload.Append('{');
            AppendVector3(payload, "rigPosition", transform.position);
            payload.Append(',');
            AppendFloat(payload, "rigYaw", transform.eulerAngles.y);
            payload.Append(',');
            AppendBool(payload, "allowWorldInput", BlockiverseRuntimeState.AllowWorldInput);
            payload.Append(',');
            AppendBool(payload, "gamePaused", BlockiverseRuntimeState.IsGamePaused);

            if (inputRig != null)
            {
                payload.Append(',');
                AppendString(payload, "activeMoveHand", inputRig.ActiveMoveHand.ToString());
                payload.Append(',');
                AppendString(payload, "activeTurnHand", inputRig.ActiveTurnHand.ToString());
                payload.Append(',');
                AppendString(payload, "activeToolHand", inputRig.ActiveToolHand.ToString());
                payload.Append(',');
                AppendBool(payload, "breakHeld", inputRig.IsBreakHeld);
                payload.Append(',');
                AppendBool(payload, "locomotionSuppressed", inputRig.LocomotionSuppressed);
            }

            Camera head = Camera.main;
            if (head != null)
            {
                payload.Append(',');
                AppendVector3(payload, "headPosition", head.transform.position);
                payload.Append(',');
                AppendFloat(payload, "headYaw", head.transform.eulerAngles.y);
            }

            AppendWorldContext(payload);
            payload.Append('}');

            BlockiverseTrace.Write("player", "snapshot", payload.ToString());
        }

        void Awake()
        {
#if !DEVELOPMENT_BUILD && !UNITY_EDITOR
            enabled = false;
#endif
        }

        void OnEnable()
        {
            DiscoverDependencies();
            TryStartRuntimeTrace();
            Subscribe();
            nextSnapshotTime = Time.unscaledTime + sampleIntervalSeconds;
        }

        void OnDisable()
        {
            Unsubscribe();
            StopRuntimeTrace();
        }

        void Update()
        {
            if (!BlockiverseTrace.Enabled)
                return;

            if (Time.unscaledTime < nextSnapshotTime)
                return;

            nextSnapshotTime = Time.unscaledTime + sampleIntervalSeconds;
            CaptureSnapshotNow();
        }

        void DiscoverDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (inputRig == null)
                inputRig = GetComponent<BlockiverseInputRig>() ?? FindAnyObjectByType<BlockiverseInputRig>(FindObjectsInactive.Include);
            if (audioCuePlayer == null)
                audioCuePlayer = GetComponent<BlockiverseAudioCuePlayer>() ?? FindAnyObjectByType<BlockiverseAudioCuePlayer>(FindObjectsInactive.Include);
            if (vfxCuePlayer == null)
                vfxCuePlayer = GetComponent<BlockiverseVfxCuePlayer>() ?? FindAnyObjectByType<BlockiverseVfxCuePlayer>(FindObjectsInactive.Include);
            if (musicController == null)
                musicController = GetComponent<BlockiverseMusicController>() ?? FindAnyObjectByType<BlockiverseMusicController>(FindObjectsInactive.Include);
            if (interactionHaptics == null)
                interactionHaptics = GetComponent<BlockiverseInteractionHaptics>() ?? FindAnyObjectByType<BlockiverseInteractionHaptics>(FindObjectsInactive.Include);

            DiscoverOptionalDependencies();
        }

        void DiscoverOptionalDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (worldManager == null)
                worldManager = FindAnyObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            if (interactionController == null)
                interactionController = FindAnyObjectByType<CreativeInteractionController>(FindObjectsInactive.Include);
            if (survivalSync == null)
                survivalSync = FindAnyObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
            if (chunkAuthoritySync == null)
                chunkAuthoritySync = FindAnyObjectByType<MultiplayerChunkAuthoritySync>(FindObjectsInactive.Include);
            if (environmentDynamics == null)
                environmentDynamics = FindAnyObjectByType<EnvironmentDynamicsController>(FindObjectsInactive.Include);
        }

        void TryStartRuntimeTrace()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (BlockiverseTrace.Enabled || !BlockiverseTrace.ShouldEnableFromRuntimeFlag())
                return;

            runtimeFileSink = BlockiverseTrace.CreateRollingFileSink();
            BlockiverseTrace.ConfigureSink(runtimeFileSink);
            BlockiverseTrace.Enabled = true;
            ownsRuntimeTrace = true;
            BlockiverseTrace.Write("session", "trace_started", "{}");
            BlockiverseLog.Info(
                BlockiverseLogCategory.Trace,
                $"Verbose gameplay trace started session={BlockiverseTrace.SessionId} file={Path.GetFileName(runtimeFileSink.CurrentFilePath)}",
                this);
#endif
        }

        void StopRuntimeTrace()
        {
            if (!ownsRuntimeTrace)
                return;

            BlockiverseTrace.Write("session", "trace_stopped", "{}");
            BlockiverseLog.Info(
                BlockiverseLogCategory.Trace,
                $"Verbose gameplay trace stopped session={BlockiverseTrace.SessionId} file={Path.GetFileName(runtimeFileSink?.CurrentFilePath)}",
                this);
            runtimeFileSink?.Dispose();
            runtimeFileSink = null;
            ownsRuntimeTrace = false;
            BlockiverseTrace.Enabled = false;
            BlockiverseTrace.ConfigureSink(null);
        }

        void Subscribe()
        {
            if (subscribed)
                return;

            if (interactionController != null)
            {
                interactionController.BlockMutationApplied += OnBlockMutationApplied;
                interactionController.BlockEditingEnabledChanged += OnBlockEditingEnabledChanged;
            }

            if (survivalSync != null)
            {
                survivalSync.CommandFeedback += OnSurvivalCommandFeedback;
                survivalSync.LocalInventoryChanged += OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged += OnSharedCrateChanged;
            }

            if (worldManager != null)
                worldManager.ContainerLooted += OnContainerLooted;

            if (environmentDynamics != null)
                environmentDynamics.LightningStruck += OnLightningStruck;

            if (audioCuePlayer != null)
                audioCuePlayer.CuePlayed += OnAudioCuePlayed;

            if (vfxCuePlayer != null)
                vfxCuePlayer.CuePlayed += OnVfxCuePlayed;

            if (musicController != null)
            {
                musicController.ContextChanged += OnMusicContextChanged;
                musicController.TrackStarted += OnMusicTrackStarted;
            }

            if (interactionHaptics != null)
                interactionHaptics.PatternRequested += OnHapticPatternRequested;

            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (interactionController != null)
            {
                interactionController.BlockMutationApplied -= OnBlockMutationApplied;
                interactionController.BlockEditingEnabledChanged -= OnBlockEditingEnabledChanged;
            }

            if (survivalSync != null)
            {
                survivalSync.CommandFeedback -= OnSurvivalCommandFeedback;
                survivalSync.LocalInventoryChanged -= OnLocalInventoryChanged;
                survivalSync.SharedCrateChanged -= OnSharedCrateChanged;
            }

            if (worldManager != null)
                worldManager.ContainerLooted -= OnContainerLooted;

            if (environmentDynamics != null)
                environmentDynamics.LightningStruck -= OnLightningStruck;

            if (audioCuePlayer != null)
                audioCuePlayer.CuePlayed -= OnAudioCuePlayed;

            if (vfxCuePlayer != null)
                vfxCuePlayer.CuePlayed -= OnVfxCuePlayed;

            if (musicController != null)
            {
                musicController.ContextChanged -= OnMusicContextChanged;
                musicController.TrackStarted -= OnMusicTrackStarted;
            }

            if (interactionHaptics != null)
                interactionHaptics.PatternRequested -= OnHapticPatternRequested;

            subscribed = false;
        }

        void OnBlockMutationApplied(BlockChange change)
        {
            BlockiverseTrace.Write(
                "interaction",
                "interaction.block_mutation",
                "{" +
                $"\"position\":{BlockPositionJson(change.Position)}," +
                $"\"previousBlock\":\"{JsonString(BlockName(change.PreviousBlock))}\"," +
                $"\"newBlock\":\"{JsonString(BlockName(change.NewBlock))}\"" +
                "}");
        }

        void OnBlockEditingEnabledChanged(bool enabled)
        {
            BlockiverseTrace.Write(
                "interaction",
                "interaction.block_editing",
                $"{{\"enabled\":{BoolJson(enabled)}}}");
        }

        void OnSurvivalCommandFeedback(SurvivalCommandResult result, BlockPosition position)
        {
            BlockiverseTrace.Write(
                "interaction",
                "interaction.survival_command",
                "{" +
                $"\"command\":\"{JsonString(result.CommandKind.ToString())}\"," +
                $"\"accepted\":{BoolJson(result.Accepted)}," +
                $"\"pendingHostValidation\":{BoolJson(result.PendingHostValidation)}," +
                $"\"duplicate\":{BoolJson(result.IsDuplicate)}," +
                $"\"failureReason\":\"{JsonString(result.FailureReason.ToString())}\"," +
                $"\"requestId\":{result.RequestId.ToString(CultureInfo.InvariantCulture)}," +
                $"\"position\":{BlockPositionJson(position)}" +
                "}");
        }

        void OnLocalInventoryChanged()
        {
            BlockiverseTrace.Write("survival", "survival.local_inventory_changed", "{}");
        }

        void OnSharedCrateChanged()
        {
            BlockiverseTrace.Write("survival", "survival.shared_crate_changed", "{}");
        }

        void OnContainerLooted(BlockPosition position)
        {
            BlockiverseTrace.Write(
                "world",
                "world.container_looted",
                $"{{\"position\":{BlockPositionJson(position)}}}");
        }

        void OnLightningStruck(BlockPosition position)
        {
            BlockiverseTrace.Write(
                "environment",
                "environment.lightning_strike",
                $"{{\"position\":{BlockPositionJson(position)}}}");
        }

        void OnAudioCuePlayed(BlockiverseAudioCue cue, AudioClip clip)
        {
            BlockiverseTrace.Write(
                "feedback",
                "feedback.audio_cue",
                "{" +
                $"\"cue\":\"{JsonString(cue.ToString())}\"," +
                $"\"clip\":\"{JsonString(clip != null ? clip.name : string.Empty)}\"" +
                "}");
        }

        void OnVfxCuePlayed(BlockiverseVfxCue cue, Vector3 position)
        {
            BlockiverseTrace.Write(
                "feedback",
                "feedback.vfx_cue",
                "{" +
                $"\"cue\":\"{JsonString(cue.ToString())}\"," +
                $"\"position\":{Vector3Json(position)}" +
                "}");
        }

        void OnMusicContextChanged(BlockiverseMusicContext context)
        {
            BlockiverseTrace.Write(
                "feedback",
                "feedback.music_context",
                $"{{\"context\":\"{JsonString(context.ToString())}\"}}");
        }

        void OnMusicTrackStarted(BlockiverseMusicContext context, AudioClip clip)
        {
            BlockiverseTrace.Write(
                "feedback",
                "feedback.music_track_started",
                "{" +
                $"\"context\":\"{JsonString(context.ToString())}\"," +
                $"\"clip\":\"{JsonString(clip != null ? clip.name : string.Empty)}\"" +
                "}");
        }

        void OnHapticPatternRequested(BlockiverseHapticPattern pattern)
        {
            BlockiverseTrace.Write(
                "feedback",
                "feedback.haptic_pattern",
                "{" +
                $"\"amplitude\":{FloatJson(pattern.Amplitude)}," +
                $"\"durationSeconds\":{FloatJson(pattern.DurationSeconds)}" +
                "}");
        }

        void AppendWorldContext(StringBuilder payload)
        {
            if (worldManager == null || worldManager.World == null)
                return;

            payload.Append(',');
            AppendString(payload, "gameMode", worldManager.GameModeString);
            payload.Append(',');
            AppendString(payload, "generationPreset", worldManager.GenerationPreset.ToString());
            payload.Append(',');
            AppendInt(payload, "worldSeed", worldManager.World.Seed);

            if (worldManager.WorldTimeClock != null)
            {
                payload.Append(',');
                AppendFloat(payload, "normalizedTime", worldManager.WorldTimeClock.NormalizedTime);
                payload.Append(',');
                AppendLong(payload, "worldTimeTicks", worldManager.WorldTimeClock.TotalElapsedTicks);
            }

            payload.Append(',');
            AppendString(payload, "weather", worldManager.CurrentWeatherState ?? "unknown");
            payload.Append(',');
            AppendInt(payload, "weatherTicks", worldManager.CurrentWeatherTicksInState);

            Camera head = Camera.main;
            if (head != null)
            {
                BlockPosition headCell = CreativeInteractionController.ToBlockPosition(head.transform.position);
                payload.Append(',');
                payload.Append("\"headCell\":").Append(BlockPositionJson(headCell));
                payload.Append(',');
                AppendBool(payload, "underground", worldManager.IsHeadUnderground(head.transform.position));
                if (worldManager.World.Bounds.Contains(headCell))
                {
                    payload.Append(',');
                    AppendString(payload, "headCellBlock", BlockName(worldManager.World.GetBlock(headCell)));
                }

                var feetCell = new BlockPosition(headCell.X, Mathf.Max(0, headCell.Y - 2), headCell.Z);
                if (worldManager.World.Bounds.Contains(feetCell))
                {
                    payload.Append(',');
                    payload.Append("\"feetCell\":").Append(BlockPositionJson(feetCell));
                    payload.Append(',');
                    AppendString(payload, "feetCellBlock", BlockName(worldManager.World.GetBlock(feetCell)));
                }
            }

            if (interactionController != null && interactionController.CurrentTarget.HasValue)
            {
                BlockPosition target = interactionController.CurrentTarget.Value;
                payload.Append(',');
                payload.Append("\"targetCell\":").Append(BlockPositionJson(target));
                if (interactionController.TryGetBlock(target, out BlockId targetBlock))
                {
                    payload.Append(',');
                    AppendString(payload, "targetBlock", BlockName(targetBlock));
                }
            }

            if (chunkAuthoritySync != null)
            {
                ChunkAuthoritySyncDiagnostics diagnostics = chunkAuthoritySync.Diagnostics;
                payload.Append(',');
                AppendInt(payload, "pendingMutationRequests", diagnostics.PendingMutationRequestCount);
                payload.Append(',');
                AppendInt(payload, "appliedRemoteDeltas", diagnostics.AppliedRemoteDeltaCount);
                payload.Append(',');
                AppendInt(payload, "broadcastDeltas", diagnostics.BroadcastDeltaCount);
            }
        }

        string BlockName(BlockId block)
        {
            BlockRegistry registry = worldManager != null && worldManager.Registry != null
                ? worldManager.Registry
                : BlockRegistry.Default;

            return registry.TryGet(block, out BlockDefinition definition)
                ? definition.CanonicalId
                : block.Value.ToString(CultureInfo.InvariantCulture);
        }

        static string BlockPositionJson(BlockPosition position)
        {
            return "{" +
                   $"\"x\":{position.X.ToString(CultureInfo.InvariantCulture)}," +
                   $"\"y\":{position.Y.ToString(CultureInfo.InvariantCulture)}," +
                   $"\"z\":{position.Z.ToString(CultureInfo.InvariantCulture)}" +
                   "}";
        }

        static string Vector3Json(Vector3 value)
        {
            return "{" +
                   $"\"x\":{FloatJson(value.x)}," +
                   $"\"y\":{FloatJson(value.y)}," +
                   $"\"z\":{FloatJson(value.z)}" +
                   "}";
        }

        static void AppendVector3(StringBuilder builder, string name, Vector3 value)
        {
            builder.Append('"').Append(name).Append("\":").Append(Vector3Json(value));
        }

        static void AppendString(StringBuilder builder, string name, string value)
        {
            builder.Append('"').Append(name).Append("\":\"").Append(JsonString(value)).Append('"');
        }

        static void AppendBool(StringBuilder builder, string name, bool value)
        {
            builder.Append('"').Append(name).Append("\":").Append(BoolJson(value));
        }

        static void AppendInt(StringBuilder builder, string name, int value)
        {
            builder.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        static void AppendLong(StringBuilder builder, string name, long value)
        {
            builder.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        static void AppendFloat(StringBuilder builder, string name, float value)
        {
            builder.Append('"').Append(name).Append("\":").Append(FloatJson(value));
        }

        static string BoolJson(bool value)
        {
            return value ? "true" : "false";
        }

        static string FloatJson(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string JsonString(string value)
        {
            return BlockiverseTraceRecord.EscapeJson(value ?? string.Empty);
        }
    }
}
