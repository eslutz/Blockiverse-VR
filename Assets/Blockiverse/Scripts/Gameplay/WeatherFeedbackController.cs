using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Drives the authored weather/ambience audio loops and weather VFX from the live weather
    // simulation: rain/snow loops and particles chosen from what is falling at the player's own
    // cell (WeatherService.PrecipitationKind, so the same storm is rain in the valley and snow on
    // the peak), thunder one-shots + lightning flashes during storms, fog wisps, day/night/cave
    // ambience, and the campfire loop at the nearest lit campfire. Pure presentation on a coarse
    // poll — it reads the environment and never mutates it.
    [DisallowMultipleComponent]
    public sealed class WeatherFeedbackController : MonoBehaviour
    {
        const float PollIntervalSeconds = 1.0f;
        const float PrecipitationVfxIntervalSeconds = 0.6f;
        const float FogVfxIntervalSeconds = 2.5f;
        const int CampfireSearchRadius = 8;
        // Splits the rain loop between the light and heavy cue. Sits between the light-rain
        // intensity (0.3) and the heavy-rain intensity (0.7) from WeatherService.
        const float HeavyPrecipitationIntensity = 0.5f;

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
        // The cues last selected for what is falling at the PLAYER'S cell, which is not a property
        // of the sky: the same thunderstorm is rain in the valley and snow on the peak above it.
        // Change detection compares freshly selected cues against these, which catches every flip:
        // weather-state changes AND kind flips under an unchanged state (walking up a freezing
        // peak mid-storm, or the night boundary dragging the local temperature across the freeze
        // line). Thunder still keys off lastWeatherState, because a storm rumbles across the whole
        // map either way.
        BlockiverseAudioCue? activePrecipitationLoop;
        BlockiverseVfxCue? activePrecipitationVfx;
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
                !TryEvaluateEnvironmentAtPlayer(out EnvironmentState environment))
            {
                StopLoops();
                return;
            }

            UpdatePrecipitationLoop(environment);
            activePrecipitationVfx = SelectPrecipitationVfx(environment);
            UpdateAmbienceLoop();
            UpdateCampfireLoop();
            TickThunder(environment.Weather);
            lastWeatherState = environment.Weather;
        }

        // ── Precipitation cue selection (pure, EditMode-testable without a rig) ───────────

        // The audio loop for what is falling at the queried location, or null under a dry sky.
        // Driven by PrecipitationKind, not WeatherState: a rain state below freezing is heard as
        // snow, and all three snow states (plus any converted storm) collapse to the one snow
        // loop, exactly as the per-state mapping already did. Light vs. heavy stays a
        // weather-state distinction, expressed through the state-derived intensity, which
        // reproduces the old mapping bit-for-bit whenever no rain→snow conversion happens
        // (light rain 0.3 → light loop; heavy rain 0.7 and thunderstorm 1.0 → heavy loop).
        public static BlockiverseAudioCue? SelectPrecipitationLoop(EnvironmentState environment)
        {
            return environment.Precipitation switch
            {
                PrecipitationKind.Rain => environment.PrecipitationIntensity >= HeavyPrecipitationIntensity
                    ? BlockiverseAudioCue.RainHeavyLoop
                    : BlockiverseAudioCue.RainLightLoop,
                PrecipitationKind.Snow => BlockiverseAudioCue.SnowWindLoop,
                _ => null,
            };
        }

        // The scatter particle for what is falling at the queried location, or null when nothing
        // falls. Fog is deliberately absent here: fog is not precipitation, so fog wisps stay
        // keyed to WeatherState.Fog in TickPrecipitationVfx, exactly as before.
        public static BlockiverseVfxCue? SelectPrecipitationVfx(EnvironmentState environment)
        {
            return environment.Precipitation switch
            {
                PrecipitationKind.Rain => BlockiverseVfxCue.RainSplash,
                PrecipitationKind.Snow => BlockiverseVfxCue.SnowflakeDrift,
                _ => null,
            };
        }

        // ── Precipitation loops ───────────────────────────────────────────────

        // Driven by what is falling HERE, not by the sky-wide weather state, so a storm that turns
        // to snow over a freezing peak is heard as snow while the valley below still hears rain.
        // Comparing the fresh selection against the active loop is the change detection: it reacts
        // to kind flips even while the weather state itself is unchanged.
        void UpdatePrecipitationLoop(EnvironmentState environment)
        {
            BlockiverseAudioCue? desired = SelectPrecipitationLoop(environment);

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

            // Particles follow the locally selected cue for the same reason the audio does: a
            // thunderstorm over a freezing peak has to drift snowflakes, not splash rain. The cue
            // is re-selected from the freshly polled environment on every poll — never derived
            // from a stale weather state — so a kind flip under an unchanged state switches the
            // particles too. Fog is not precipitation, so it stays keyed to the weather state.
            if (activePrecipitationVfx.HasValue)
            {
                PlayScatterVfx(activePrecipitationVfx.Value, ref nextPrecipitationVfxTime, PrecipitationVfxIntervalSeconds);
                return;
            }

            if (lastWeatherState == WeatherState.Fog)
                PlayScatterVfx(BlockiverseVfxCue.FogWisp, ref nextFogVfxTime, FogVfxIntervalSeconds);
        }

        void PlayScatterVfx(BlockiverseVfxCue cue, ref float nextTime, float interval)
        {
            if (Time.time < nextTime || !TryGetHeadWorldPosition(out Vector3 headPosition))
                return;

            nextTime = Time.time + interval;
            Vector3 offset = new(Random.Range(-4.0f, 4.0f), Random.Range(0.5f, 3.0f), Random.Range(-4.0f, 4.0f));
            vfxCuePlayer.PlayCue(cue, headPosition + offset);
        }

        // Environment at the player's head cell, so precipitation feedback matches the local biome
        // and altitude (rain at the shoreline, snow on the same storm up on the peaks). A head
        // outside world bounds — creative flight above the ceiling, or past an edge — clamps to
        // the nearest in-bounds cell so biome and altitude continuity survive: a rain-state storm
        // falling as snow over a freezing biome must not flip to the temperate sea-level rain
        // default the moment the head crosses the world ceiling, then flip back on descent. Only
        // a missing camera or world falls back to the positionless sea-level query — biome
        // unknown, temperate default.
        bool TryEvaluateEnvironmentAtPlayer(out EnvironmentState environment)
        {
            if (TryGetHeadCellClampedIntoBounds(out BlockPosition cell))
                return worldManager.TryEvaluateEnvironment(cell, out environment);

            return worldManager.TryEvaluateEnvironment(WorldConstants.SeaLevel, out environment);
        }

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

        // Head cell for environment queries only: an out-of-bounds head clamps to the nearest
        // in-bounds cell instead of failing, preserving the local biome and the altitude band.
        // Deliberately not used by the campfire scan, whose radius search wants a genuinely
        // in-world anchor.
        bool TryGetHeadCellClampedIntoBounds(out BlockPosition cell)
        {
            cell = default;
            if (!TryGetHeadWorldPosition(out Vector3 position) || worldManager.World == null)
                return false;

            BlockPosition raw = CreativeInteractionController.ToBlockPosition(position);
            WorldBounds bounds = worldManager.World.Bounds;
            cell = new BlockPosition(
                Mathf.Clamp(raw.X, 0, bounds.Width - 1),
                Mathf.Clamp(raw.Y, 0, bounds.Height - 1),
                Mathf.Clamp(raw.Z, 0, bounds.Depth - 1));
            return true;
        }

        void StopLoops()
        {
            // Cleared before the audio-player guard: this drives the particle scatter, which runs
            // on its own cadence and would otherwise keep raining after the weather query fails.
            activePrecipitationVfx = null;

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
