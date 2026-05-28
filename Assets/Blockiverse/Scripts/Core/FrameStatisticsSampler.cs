using System;

namespace Blockiverse.Core
{
    /// <summary>
    /// Rolling-window aggregator for frame durations. Engine-free so it can be unit tested
    /// without entering play mode; the in-game overlay feeds it <c>Time.unscaledDeltaTime</c>.
    /// </summary>
    public sealed class FrameStatisticsSampler
    {
        readonly float[] frameDurations;
        int writeIndex;
        int sampleCount;
        double durationSum;

        public FrameStatisticsSampler(int windowSize = 90)
        {
            if (windowSize < 1)
                throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be at least one frame.");

            frameDurations = new float[windowSize];
        }

        public int WindowSize => frameDurations.Length;
        public int SampleCount => sampleCount;
        public bool HasSamples => sampleCount > 0;

        public void AddFrame(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                return;

            if (sampleCount == frameDurations.Length)
                durationSum -= frameDurations[writeIndex];
            else
                sampleCount++;

            frameDurations[writeIndex] = deltaSeconds;
            durationSum += deltaSeconds;
            writeIndex = (writeIndex + 1) % frameDurations.Length;
        }

        public void Reset()
        {
            Array.Clear(frameDurations, 0, frameDurations.Length);
            writeIndex = 0;
            sampleCount = 0;
            durationSum = 0d;
        }

        public float AverageFps => durationSum > 0d ? (float)(sampleCount / durationSum) : 0f;

        public float AverageFrameMilliseconds => sampleCount > 0 ? (float)(durationSum / sampleCount * 1000d) : 0f;

        public float MinFps
        {
            get
            {
                float worstDuration = WorstFrameSeconds();
                return worstDuration > 0f ? 1f / worstDuration : 0f;
            }
        }

        public float MaxFps
        {
            get
            {
                float bestDuration = BestFrameSeconds();
                return bestDuration > 0f ? 1f / bestDuration : 0f;
            }
        }

        float WorstFrameSeconds()
        {
            float worst = 0f;
            for (int i = 0; i < sampleCount; i++)
                worst = Math.Max(worst, frameDurations[i]);

            return worst;
        }

        float BestFrameSeconds()
        {
            if (sampleCount == 0)
                return 0f;

            float best = float.MaxValue;
            for (int i = 0; i < sampleCount; i++)
                best = Math.Min(best, frameDurations[i]);

            return best;
        }
    }
}
