using Blockiverse.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Local-only diagnostics overlay that reports frame timing and voxel render stats
    /// (FPS, triangles, chunk count, queued rebuilds) for Quest performance validation.
    /// Renders through UI Toolkit; off by default in release builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceStatsOverlay : MonoBehaviour
    {
        [SerializeField] VoxelWorldRenderer worldRenderer;
        [SerializeField] bool visible = true;
        [SerializeField, Min(1)] int sampleWindow = 90;
        [SerializeField, Min(0f)] float logIntervalSeconds = 5f;

        FrameStatisticsSampler sampler;
        UIDocument document;
        Label overlayLabel;
        float logTimer;

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
#if !DEVELOPMENT_BUILD && !UNITY_EDITOR
            enabled = false;
            return;
#endif
            sampler = new FrameStatisticsSampler(Mathf.Max(1, sampleWindow));

            if (worldRenderer == null)
                worldRenderer = FindAnyObjectByType<VoxelWorldRenderer>();
            EnsureOverlay();
        }

        void Update()
        {
            Sampler.AddFrame(Time.unscaledDeltaTime);
            UpdateOverlay();

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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        void EnsureOverlay()
        {
            if (document != null && overlayLabel != null)
                return;

            GameObject overlayObject = new("Performance Stats UI Toolkit Overlay");
            overlayObject.transform.SetParent(transform, false);
            document = overlayObject.AddComponent<UIDocument>();
            overlayLabel = new Label { name = "blockiverse-performance-stats" };
            overlayLabel.style.position = Position.Absolute;
            overlayLabel.style.left = 12.0f;
            overlayLabel.style.top = 12.0f;
            overlayLabel.style.width = 360.0f;
            overlayLabel.style.paddingLeft = 10.0f;
            overlayLabel.style.paddingRight = 10.0f;
            overlayLabel.style.paddingTop = 8.0f;
            overlayLabel.style.paddingBottom = 8.0f;
            overlayLabel.style.fontSize = 16.0f;
            overlayLabel.style.color = Color.white;
            overlayLabel.style.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.72f);
            document.rootVisualElement.Add(overlayLabel);
        }

        void UpdateOverlay()
        {
            EnsureOverlay();
            if (overlayLabel == null)
                return;

            overlayLabel.style.display = visible && Sampler.HasSamples ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible || !Sampler.HasSamples)
                return;

            VoxelRenderStats stats = worldRenderer != null ? worldRenderer.Stats : default;
            overlayLabel.text =
                $"FPS avg {Sampler.AverageFps:0.0}  min {Sampler.MinFps:0.0}  max {Sampler.MaxFps:0.0}\n" +
                $"Frame {Sampler.AverageFrameMilliseconds:0.00} ms\n" +
                $"Chunks {stats.ChunkCount}  Tris {stats.TriangleCount:n0}\n" +
                $"Rebuild queue {stats.QueuedRebuildCount}";
        }
#endif
    }
}
