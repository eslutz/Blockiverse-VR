using System;
using UnityEngine;
using Blockiverse.WorldGen;

namespace Blockiverse.Gameplay
{
    public sealed class WorldTimeClock : MonoBehaviour
    {
        // Canonical day length: 1 game day = 24000 ticks = 20 real minutes (1200 s) at 20 ticks/second.
        // See docs/rulesets/voxel_world_environment_effects.md §2.
        public const float DefaultDayLengthSeconds = 1200.0f;
        public const float DefaultStartNormalizedTime = 0.22f;

        // The daylight window over NormalizedTime, shared by the presentation layers (ambience,
        // music) so they all agree on when night starts.
        public const float DayStartNormalizedTime = 0.05f;
        public const float NightStartNormalizedTime = 0.55f;

        public static bool IsDay(float normalizedTime) =>
            normalizedTime >= DayStartNormalizedTime && normalizedTime < NightStartNormalizedTime;

        [SerializeField] float dayLengthSeconds = DefaultDayLengthSeconds;
        [SerializeField] float normalizedTime = DefaultStartNormalizedTime;
        [SerializeField] float timeScale = 1.0f;

        float tickAccumulator;
        long totalElapsedTicks;
        // The time-of-day at tick 0. Every peer shares this constant (or the Configure'd value),
        // so a normalized time restored from absolute ticks lands on the same phase the live
        // clock would have reached — late joiners and loaded saves agree with the host/pre-save.
        float startNormalizedOffset = DefaultStartNormalizedTime;

        public event Action<int> Ticked;

        public float DayLengthSeconds => dayLengthSeconds;
        public float NormalizedTime => normalizedTime;
        public float TimeScale => timeScale;
        public long TotalElapsedTicks => totalElapsedTicks;

        public void RestoreElapsedTicks(long ticks)
        {
            totalElapsedTicks = ticks;
            long ticksPerDay = (long)(dayLengthSeconds * WorldConstants.TicksPerSecond);
            if (ticksPerDay > 0)
                normalizedTime = Normalize(startNormalizedOffset + (float)((ticks % ticksPerDay) / (double)ticksPerDay));
        }

        public void Configure(float dayLengthSeconds, float startNormalizedTime, float timeScale)
        {
            this.dayLengthSeconds = Mathf.Max(0.001f, dayLengthSeconds);
            normalizedTime = Normalize(startNormalizedTime);
            startNormalizedOffset = normalizedTime;
            this.timeScale = timeScale;
        }

        // Creative env control: jumps the time-of-day phase without touching elapsed ticks. The
        // tick-0 offset shifts with it so RestoreElapsedTicks keeps reproducing this phase.
        public void SetNormalizedTime(float value)
        {
            normalizedTime = Normalize(value);

            long ticksPerDay = (long)(dayLengthSeconds * WorldConstants.TicksPerSecond);
            if (ticksPerDay > 0)
            {
                float ticksFraction = (float)((totalElapsedTicks % ticksPerDay) / (double)ticksPerDay);
                startNormalizedOffset = Normalize(normalizedTime - ticksFraction);
            }
        }

        // Creative env control: speeds up or freezes the day cycle (0 pauses the clock).
        public void SetTimeScale(float value)
        {
            timeScale = Mathf.Max(0.0f, value);
        }

        void Awake()
        {
            // A fresh clock (no elapsed ticks) defines its serialized start time as the tick-0
            // phase, keeping NormalizedTime and TotalElapsedTicks mutually consistent from boot.
            if (totalElapsedTicks == 0)
                startNormalizedOffset = Normalize(normalizedTime);
        }

        public void Tick(float deltaSeconds)
        {
            if (Mathf.Approximately(timeScale, 0.0f))
                return;

            normalizedTime = Normalize(normalizedTime + deltaSeconds * timeScale / dayLengthSeconds);
        }

        void Update()
        {
            Tick(Time.deltaTime);

            if (!Mathf.Approximately(timeScale, 0.0f))
            {
                tickAccumulator += Time.deltaTime * timeScale * WorldConstants.TicksPerSecond;
                int elapsed = (int)tickAccumulator;
                if (elapsed > 0)
                {
                    tickAccumulator -= elapsed;
                    totalElapsedTicks += elapsed;
                    Ticked?.Invoke(elapsed);
                }
            }
        }

        static float Normalize(float value)
        {
            value %= 1.0f;
            return value < 0.0f ? value + 1.0f : value;
        }
    }
}
