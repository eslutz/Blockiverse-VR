using UnityEngine;

namespace Blockiverse.Gameplay
{
    public sealed class WorldTimeClock : MonoBehaviour
    {
        public const float DefaultDayLengthSeconds = 600.0f;
        public const float DefaultStartNormalizedTime = 0.22f;

        [SerializeField] float dayLengthSeconds = DefaultDayLengthSeconds;
        [SerializeField] float normalizedTime = DefaultStartNormalizedTime;
        [SerializeField] float timeScale = 1.0f;

        public float DayLengthSeconds => dayLengthSeconds;
        public float NormalizedTime => normalizedTime;
        public float TimeScale => timeScale;

        public void Configure(float dayLengthSeconds, float startNormalizedTime, float timeScale)
        {
            this.dayLengthSeconds = Mathf.Max(0.001f, dayLengthSeconds);
            normalizedTime = Normalize(startNormalizedTime);
            this.timeScale = timeScale;
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
        }

        static float Normalize(float value)
        {
            value %= 1.0f;
            return value < 0.0f ? value + 1.0f : value;
        }
    }
}
