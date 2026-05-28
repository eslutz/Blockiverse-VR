using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Local-only diagnostics overlay that reports frame timing and voxel render stats
    /// (FPS, triangles, chunk count, queued rebuilds) for Quest performance validation.
    /// Renders through IMGUI so it needs no scene canvas; off by default in release builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceStatsOverlay : MonoBehaviour
    {
        [SerializeField] VoxelWorldRenderer worldRenderer;
        [SerializeField] bool visible = true;
        [SerializeField, Min(1)] int sampleWindow = 90;
        [SerializeField, Min(0f)] float logIntervalSeconds = 5f;

        FrameStatisticsSampler sampler;
        GUIStyle overlayStyle;
        float logTimer;

        static readonly Rect OverlayRect = new(12f, 12f, 320f, 96f);

        public bool Visible
        {
            get => visible;
            set => visible = value;
        }

        public FrameStatisticsSampler Sampler => sampler ??= new FrameStatisticsSampler(Mathf.Max(1, sampleWindow));

        public void Configure(VoxelWorldRenderer renderer)
        {
            worldRenderer = renderer;
        }

        public void Toggle()
        {
            visible = !visible;
        }

        void Awake()
        {
            sampler = new FrameStatisticsSampler(Mathf.Max(1, sampleWindow));

            if (worldRenderer == null)
                worldRenderer = FindFirstObjectByType<VoxelWorldRenderer>();
        }

        void Update()
        {
            Sampler.AddFrame(Time.unscaledDeltaTime);

            if (logIntervalSeconds <= 0f)
                return;

            logTimer += Time.unscaledDeltaTime;
            if (logTimer < logIntervalSeconds)
                return;

            logTimer = 0f;
            LogSummary();
        }

        void LogSummary()
        {
            if (!Sampler.HasSamples)
                return;

            VoxelRenderStats stats = worldRenderer != null ? worldRenderer.Stats : default;
            BlockiverseLog.Info(
                BlockiverseLogCategory.Performance,
                $"Performance sample fpsAvg={Sampler.AverageFps:0.0} fpsMin={Sampler.MinFps:0.0} frameMs={Sampler.AverageFrameMilliseconds:0.00} " +
                $"chunks={stats.ChunkCount} triangles={stats.TriangleCount} queuedRebuilds={stats.QueuedRebuildCount}",
                this);
        }

        void OnGUI()
        {
            if (!visible || !Sampler.HasSamples || !(Debug.isDebugBuild || Application.isEditor))
                return;

            VoxelRenderStats stats = worldRenderer != null ? worldRenderer.Stats : default;
            string text =
                $"FPS avg {Sampler.AverageFps:0.0}  min {Sampler.MinFps:0.0}  max {Sampler.MaxFps:0.0}\n" +
                $"Frame {Sampler.AverageFrameMilliseconds:0.00} ms\n" +
                $"Chunks {stats.ChunkCount}  Tris {stats.TriangleCount:n0}\n" +
                $"Rebuild queue {stats.QueuedRebuildCount}";

            GUI.Label(OverlayRect, text, OverlayStyle);
        }

        GUIStyle OverlayStyle
        {
            get
            {
                if (overlayStyle != null)
                    return overlayStyle;

                overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 16,
                    padding = new RectOffset(10, 10, 8, 8)
                };
                return overlayStyle;
            }
        }
    }
}
