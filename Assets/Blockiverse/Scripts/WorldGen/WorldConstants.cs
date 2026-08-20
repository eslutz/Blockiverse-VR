using Blockiverse.Voxel;

namespace Blockiverse.WorldGen
{
    public static class WorldConstants
    {
        public const int ChunkSize = 16;
        public const int WorldMaxY = 127;
        public const int SeaLevel = 64;
        public const int BedrockTopY = 3;
        public const int TicksPerSecond = SimulationTime.TicksPerSecond;
        public const int TicksPerDay = SimulationTime.TicksPerDay;

        // The canonical daylight window over normalized time-of-day (0..1). Every system that has
        // to agree on when night starts reads it from here: the §6.2 night temperature modifier in
        // WeatherService, and WorldTimeClock.IsDay — which the presentation layers (ambience,
        // music) and SurvivalVitals' night-cold threshold go through — forwards to these values.
        //
        // This lives in WorldGen rather than beside WorldTimeClock because WorldGen is the lowest
        // assembly that needs it (Networking references WorldGen, not the reverse) and because it
        // must stay engine-free. WeatherService previously carried its own `t > 0.6 || t < 0.1`
        // definition, which disagreed with WorldTimeClock over [0.05, 0.10) and [0.55, 0.60]: the
        // -5 °C night modifier switched on roughly a real minute away from the dusk and dawn every
        // other system rendered, so cold onset never lined up with visible nightfall.
        public const float DayStartNormalizedTime = 0.05f;
        public const float NightStartNormalizedTime = 0.55f;

        public static bool IsDay(float normalizedTime) =>
            normalizedTime >= DayStartNormalizedTime && normalizedTime < NightStartNormalizedTime;

        public static bool IsNight(float normalizedTime) => !IsDay(normalizedTime);
    }
}
