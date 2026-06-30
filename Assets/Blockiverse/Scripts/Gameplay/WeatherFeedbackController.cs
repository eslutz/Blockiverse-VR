using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Drives the authored weather/ambience audio loops and weather VFX from the live weather
    // simulation: rain/snow loops per WeatherState, thunder one-shots + lightning flashes during
    // storms, fog wisps, precipitation particles around the player, day/night/cave ambience, and
    // the campfire loop at the nearest lit campfire. Pure presentation on a coarse poll.
    [DisallowMultipleComponent]
    public sealed class WeatherFeedbackController : MonoBehaviour
    {
        const float PollIntervalSeconds = 1.0f;
        const float PrecipitationVfxIntervalSeconds = 0.6f;
        const float FogVfxIntervalSeconds = 2.5f;
        const int CampfireSearchRadius = 8;

        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseVfxCuePlayer vfxCuePlayer;
        [SerializeField] EnvironmentDynamicsController environmentDynamics;
        [SerializeField] bool enableAmbientWeatherLoops;

        float nextPollTime;
        float nextPrecipitationVfxTime;
        float nextFogVfxTime;
        float nextThunderTime;
        WeatherState lastWeatherState = WeatherState.Clear;
        BlockiverseAudioCue? activePrecipitationLoop;
        BlockiverseAudioCue? activeAmbienceLoop;
        EnvironmentDynamicsController subscribedEnvironmentDynamics;
        bool campfireLoopActive;

        void OnEnable()
        {
            DiscoverDependencies();
        }

        void OnDisable()
        {
            UnsubscribeLightningStrikes();
            StopLoops();
        }

        void Update()
        {
            if (Time.time >= nextPollTime)
            {
                nextPollTime = Time.time + PollIntervalSeconds;
                Poll();
            }

            TickPrecipitationVfx();
        }

        void DiscoverDependencies()
        {
            if (!Application.isPlaying)
                return;

            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            if (audioCuePlayer == null)
                audioCuePlayer = BlockiverseSceneLookup.Find<BlockiverseAudioCuePlayer>();

            if (vfxCuePlayer == null)
                vfxCuePlayer = BlockiverseSceneLookup.Find<BlockiverseVfxCuePlayer>();

            if (environmentDynamics == null)
                environmentDynamics = BlockiverseSceneLookup.Find<EnvironmentDynamicsController>(FindObjectsInactive.Include);

            SubscribeLightningStrikes();
        }

        void SubscribeLightningStrikes()
        {
            if (subscribedEnvironmentDynamics == environmentDynamics)
                return;

            UnsubscribeLightningStrikes();

            if (environmentDynamics == null)
                return;

            subscribedEnvironmentDynamics = environmentDynamics;
            subscribedEnvironmentDynamics.LightningStruck += OnLightningStruck;
        }

        void UnsubscribeLightningStrikes()
        {
            if (subscribedEnvironmentDynamics == null)
                return;

            subscribedEnvironmentDynamics.LightningStruck -= OnLightningStruck;
            subscribedEnvironmentDynamics = null;
        }

        void Poll()
        {
            DiscoverDependencies();

            if (!BlockiverseRuntimeState.AllowWorldInput)
            {
                StopLoops();
                lastWeatherState = WeatherState.Clear;
                return;
            }

            if (worldManager == null || audioCuePlayer == null ||
                !worldManager.TryEvaluateEnvironment(AltitudeAtPlayer(), out EnvironmentState environment))
            {
                StopLoops();
                return;
            }

            UpdatePrecipitationLoop(environment.Weather);
            UpdateAmbienceLoop();
            UpdateCampfireLoop();
            TickThunder(environment.Weather);
            lastWeatherState = environment.Weather;
        }

        // ── Precipitation loops ───────────────────────────────────────────────

        void UpdatePrecipitationLoop(WeatherState state)
        {
            BlockiverseAudioCue? desired = state switch
            {
                WeatherState.LightRain => BlockiverseAudioCue.RainLightLoop,
                WeatherState.HeavyRain => BlockiverseAudioCue.RainHeavyLoop,
                WeatherState.Thunderstorm => BlockiverseAudioCue.RainHeavyLoop,
                WeatherState.LightSnow => BlockiverseAudioCue.SnowWindLoop,
                WeatherState.HeavySnow => BlockiverseAudioCue.SnowWindLoop,
                WeatherState.Blizzard => BlockiverseAudioCue.SnowWindLoop,
                _ => null,
            };

            if (desired == activePrecipitationLoop)
                return;

            if (activePrecipitationLoop.HasValue)
                audioCuePlayer.StopLoop(activePrecipitationLoop.Value);

            activePrecipitationLoop = desired;
            if (desired.HasValue)
                audioCuePlayer.StartLoop(desired.Value);
        }

        // ── Ambience (cave / day / night) ─────────────────────────────────────

        void UpdateAmbienceLoop()
        {
            if (!enableAmbientWeatherLoops)
            {
                if (activeAmbienceLoop.HasValue)
                    audioCuePlayer.StopLoop(activeAmbienceLoop.Value);

                activeAmbienceLoop = null;
                return;
            }

            BlockiverseAudioCue desired = ResolveAmbienceCue();

            if (activeAmbienceLoop == desired)
                return;

            if (activeAmbienceLoop.HasValue)
                audioCuePlayer.StopLoop(activeAmbienceLoop.Value);

            activeAmbienceLoop = desired;
            audioCuePlayer.StartLoop(desired);
        }

        BlockiverseAudioCue ResolveAmbienceCue()
        {
            // Underground (no sky above the head cell) → cave ambience; the shared O(1) sky-map
            // query the music controller also uses.
            if (TryGetHeadWorldPosition(out Vector3 headPosition) && worldManager.IsHeadUnderground(headPosition))
                return BlockiverseAudioCue.CaveAmbienceLoop;

            float normalizedTime = worldManager.WorldTimeClock != null
                ? worldManager.WorldTimeClock.NormalizedTime
                : 0.25f;
            return WorldTimeClock.IsDay(normalizedTime)
                ? BlockiverseAudioCue.DayAmbienceLoop
                : BlockiverseAudioCue.NightAmbienceLoop;
        }

        // ── Campfire loop ─────────────────────────────────────────────────────

        void UpdateCampfireLoop()
        {
            bool found = TryFindNearestCampfire(out Vector3 campfireCenter);

            if (found)
            {
                audioCuePlayer.StartLoopAt(BlockiverseAudioCue.CampfireLoop, campfireCenter);
                campfireLoopActive = true;
                vfxCuePlayer?.PlayCue(BlockiverseVfxCue.CampfireEmber, campfireCenter);
            }
            else if (campfireLoopActive)
            {
                audioCuePlayer.StopLoop(BlockiverseAudioCue.CampfireLoop);
                campfireLoopActive = false;
            }
        }

        bool TryFindNearestCampfire(out Vector3 center)
        {
            center = default;
            VoxelWorld world = worldManager.World;
            if (world == null || !TryGetHeadCell(out BlockPosition head))
                return false;

            int bestDistanceSquared = int.MaxValue;
            int minX = Mathf.Max(0, head.X - CampfireSearchRadius);
            int maxX = Mathf.Min(world.Bounds.Width - 1, head.X + CampfireSearchRadius);
            int minY = Mathf.Max(0, head.Y - CampfireSearchRadius);
            int maxY = Mathf.Min(world.Bounds.Height - 1, head.Y + CampfireSearchRadius);
            int minZ = Mathf.Max(0, head.Z - CampfireSearchRadius);
            int maxZ = Mathf.Min(world.Bounds.Depth - 1, head.Z + CampfireSearchRadius);

            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (world.GetBlock(new BlockPosition(x, y, z)) != BlockRegistry.Campfire)
                            continue;

                        int dx = x - head.X;
                        int dy = y - head.Y;
                        int dz = z - head.Z;
                        int distanceSquared = dx * dx + dy * dy + dz * dz;
                        if (distanceSquared < bestDistanceSquared)
                        {
                            bestDistanceSquared = distanceSquared;
                            center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        }
                    }
                }
            }

            return bestDistanceSquared != int.MaxValue;
        }

        // ── Thunder + precipitation/fog VFX ───────────────────────────────────

        void OnLightningStruck(BlockPosition strike)
        {
            DiscoverDependencies();

            Vector3 strikePosition = new(strike.X + 0.5f, strike.Y + 1.0f, strike.Z + 0.5f);
            audioCuePlayer?.PlayCueAt(BlockiverseAudioCue.ThunderNear, strikePosition);
            vfxCuePlayer?.PlayCue(BlockiverseVfxCue.LightningFlash, strikePosition + Vector3.up * 6.0f);
        }

        void TickThunder(WeatherState state)
        {
            if (state != WeatherState.Thunderstorm)
                return;

            if (Time.time < nextThunderTime)
                return;

            nextThunderTime = Time.time + Random.Range(6.0f, 14.0f);

            bool near = Random.value < 0.4f;
            audioCuePlayer.PlayCue(near ? BlockiverseAudioCue.ThunderNear : BlockiverseAudioCue.ThunderFar);

            if (near && vfxCuePlayer != null && TryGetHeadWorldPosition(out Vector3 headPosition))
            {
                Vector3 flashPosition = headPosition +
                    new Vector3(Random.Range(-12.0f, 12.0f), Random.Range(8.0f, 16.0f), Random.Range(-12.0f, 12.0f));
                vfxCuePlayer.PlayCue(BlockiverseVfxCue.LightningFlash, flashPosition);
            }
        }

        void TickPrecipitationVfx()
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
                return;

            if (vfxCuePlayer == null || worldManager == null)
                return;

            switch (lastWeatherState)
            {
                case WeatherState.LightRain:
                case WeatherState.HeavyRain:
                case WeatherState.Thunderstorm:
                    PlayScatterVfx(BlockiverseVfxCue.RainSplash, ref nextPrecipitationVfxTime, PrecipitationVfxIntervalSeconds);
                    break;
                case WeatherState.LightSnow:
                case WeatherState.HeavySnow:
                case WeatherState.Blizzard:
                    PlayScatterVfx(BlockiverseVfxCue.SnowflakeDrift, ref nextPrecipitationVfxTime, PrecipitationVfxIntervalSeconds);
                    break;
                case WeatherState.Fog:
                    PlayScatterVfx(BlockiverseVfxCue.FogWisp, ref nextFogVfxTime, FogVfxIntervalSeconds);
                    break;
            }
        }

        void PlayScatterVfx(BlockiverseVfxCue cue, ref float nextTime, float interval)
        {
            if (Time.time < nextTime || !TryGetHeadWorldPosition(out Vector3 headPosition))
                return;

            nextTime = Time.time + interval;
            Vector3 offset = new(Random.Range(-4.0f, 4.0f), Random.Range(0.5f, 3.0f), Random.Range(-4.0f, 4.0f));
            vfxCuePlayer.PlayCue(cue, headPosition + offset);
        }

        int AltitudeAtPlayer() =>
            TryGetHeadCell(out BlockPosition cell) ? cell.Y : WorldConstants.SeaLevel;

        // ── Player position helpers ───────────────────────────────────────────

        static bool TryGetHeadWorldPosition(out Vector3 position)
        {
            Camera head = Camera.main;
            position = head != null ? head.transform.position : default;
            return head != null;
        }

        bool TryGetHeadCell(out BlockPosition cell)
        {
            cell = default;
            if (!TryGetHeadWorldPosition(out Vector3 position) || worldManager.World == null)
                return false;

            cell = CreativeInteractionController.ToBlockPosition(position);
            return worldManager.World.Bounds.Contains(cell);
        }

        void StopLoops()
        {
            if (audioCuePlayer == null)
                return;

            if (activePrecipitationLoop.HasValue)
                audioCuePlayer.StopLoop(activePrecipitationLoop.Value);
            if (activeAmbienceLoop.HasValue)
                audioCuePlayer.StopLoop(activeAmbienceLoop.Value);
            if (campfireLoopActive)
                audioCuePlayer.StopLoop(BlockiverseAudioCue.CampfireLoop);

            activePrecipitationLoop = null;
            activeAmbienceLoop = null;
            campfireLoopActive = false;
        }
    }
}
