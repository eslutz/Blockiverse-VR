using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // When thunder arrives, how loud, and which clip -- all from the strike's distance.
    //
    // Pure statics beside the controller, following BlockiverseMusicScheduling, so the three
    // decisions that make distance audible can be pinned in EditMode without an AudioSource.
    public static class BlockiverseThunderScheduling
    {
        // Deliberately ~10x slower than the real 343 m/s. Over a 96-block strike ring, honest
        // propagation peaks at 0.28 s, which no player perceives as a delay at all; at 34 the same
        // strike lands 2.8 s after the flash, which is the whole cue. voxel_world_environment_effects.md
        // section 10.5 is the source of this constant, and the reason it is not physical is
        // recorded there so it does not get "fixed" later.
        public const float SoundBlocksPerSecond = 34.0f;

        // Where thunder falls silent. The ruleset's original 256 was written for a world where
        // strikes could be that far away; against the 96-block ring it left the most distant
        // thunder still playing at 62%, flattening the exact distinction this is building. 128
        // puts the ring's outer edge at ~0.25.
        public const float SilenceDistanceBlocks = 128.0f;

        // Past this a strike gets the far clip. Roughly the middle of the ring.
        public const float NearThunderDistanceBlocks = 40.0f;

        public static float ResolveDelaySeconds(float distanceBlocks) =>
            Mathf.Max(distanceBlocks, 0.0f) / SoundBlocksPerSecond;

        public static float ResolveVolumeScale(float distanceBlocks) =>
            Mathf.Clamp01(1.0f - Mathf.Max(distanceBlocks, 0.0f) / SilenceDistanceBlocks);

        public static BlockiverseAudioCue SelectThunderCue(float distanceBlocks) =>
            distanceBlocks <= NearThunderDistanceBlocks
                ? BlockiverseAudioCue.ThunderNear
                : BlockiverseAudioCue.ThunderFar;
    }

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
        // Distinct from every other DeterministicHash consumer, so a bolt's shape cannot correlate
        // with terrain or structure rolls at the same column.
        const int LightningBoltSalt = 0x6017;

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

        // Deliberately NOT [SerializeField]s: adding one would re-serialize the generated XR rig
        // prefab for no visible change, and this branch is meant to leave prefabs untouched.
        BlockiverseLightingCycleController lightingCycle;
        LightningBoltView boltView;
        BlockiverseWeatherVolume weatherVolume;

        // A LIST, not a single nextThunderTime like the other timers in this file: strikes can
        // overlap, and a distant one still travelling must not be cancelled by a closer one
        // behind it. There is no scheduling utility in the project to reuse and no delayed-play
        // API on the cue player, so this follows the codebase's dominant Time.time pattern.
        readonly List<PendingThunder> pendingThunder = new();

        // Explicit wiring, following the Configure methods on the other feedback components.
        // Scene lookup still fills in whatever is left null, but a caller that already knows its
        // dependencies should not have to hope BlockiverseSceneLookup returns the same instance --
        // with a Boot scene loaded alongside, it may not.
        public void Configure(
            BlockiverseAudioCuePlayer audio,
            BlockiverseVfxCuePlayer vfx = null,
            CreativeWorldManager manager = null)
        {
            if (audio != null)
                audioCuePlayer = audio;
            if (vfx != null)
                vfxCuePlayer = vfx;
            if (manager != null)
                worldManager = manager;
        }

        void OnEnable()
        {
            DiscoverDependencies();
        }

        void OnDisable()
        {
            UnsubscribeLightningStrikes();
            StopLoops();
        }

        // The weather volume and the bolt view are created at runtime and deliberately parented to
        // nothing, so they follow the head in position without inheriting its rotation. Nothing
        // else owns them -- without this they outlive the controller, and in a PlayMode run each
        // scene load leaves another one behind still ticking its LateUpdate.
        void OnDestroy()
        {
            DestroyRuntimeChild(weatherVolume != null ? weatherVolume.gameObject : null);
            DestroyRuntimeChild(boltView != null ? boltView.gameObject : null);
            weatherVolume = null;
            boltView = null;
        }

        static void DestroyRuntimeChild(GameObject host)
        {
            if (host == null)
                return;

            if (Application.isPlaying)
                Destroy(host);
            else
                DestroyImmediate(host);
        }

        void Update()
        {
            if (Time.time >= nextPollTime)
            {
                nextPollTime = Time.time + PollIntervalSeconds;
                Poll();
            }

            TickPrecipitationVfx();
            TickPendingThunder();
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

            if (lightingCycle == null)
                lightingCycle = BlockiverseSceneLookup.Find<BlockiverseLightingCycleController>(FindObjectsInactive.Include);

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
            UpdateWeatherVolume(environment);
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

            // No head means no listener and nothing to measure distance from, so there is nothing
            // to schedule -- a headless host simulates its weather in silence.
            bool haveHead = TryGetHeadWorldPosition(out Vector3 headPosition);
            float distance = haveHead ? Vector3.Distance(headPosition, strikePosition) : 0.0f;

            // Queued BEFORE the comfort gate below: Reduced Flash suppresses the visuals, not the
            // storm. A player using it should still hear the thunder.
            if (haveHead)
                QueueThunder(distance);

            // Every flash the strike produces goes through the same gate. PlayCue enforces it for
            // its own cue, but the sky flash never touches PlayCue, so the check has to happen
            // here for it.
            if (vfxCuePlayer == null || !vfxCuePlayer.AllowFlashEffects)
                return;

            if (haveHead)
            {
                // The flash is scaled by how far away the bolt was, across the whole ring and down
                // to exactly nothing at its outer edge: a close strike washes out the sky, a
                // distant one is a bolt you see with no flash at all. That pairing is what makes
                // distance legible before the player has finished turning their head.
                if (lightingCycle != null)
                    lightingCycle.PulseSkyFlash(LightningFlashSolver.DistanceStrength(distance));

                // Seeded from the struck column so the same strike draws the same bolt on every
                // peer -- clients receive the strike as a relayed event and build it themselves.
                EnsureBoltView().Strike(
                    new Vector3(strike.X + 0.5f, strike.Y + 1.0f, strike.Z + 0.5f),
                    seed: unchecked((int)DeterministicHash.Hash(0, strike.X, strike.Y, strike.Z, LightningBoltSalt)),
                    distance,
                    reducedParticles: vfxCuePlayer.ParticleIntensityScale < 1.0f);
            }

            vfxCuePlayer.PlayCue(BlockiverseVfxCue.LightningFlash, strikePosition + Vector3.up * 6.0f);
            vfxCuePlayer.PlayCue(BlockiverseVfxCue.BlockChipBurst, strikePosition);
        }

        // Thunder plays 2D rather than positionally, on purpose. PlayCueAt routes through an
        // 8-source round-robin pool that MOVES whichever source it picks, so a clip still ringing
        // when the pool wraps gets teleported mid-tail; those sources also apply Unity's default
        // logarithmic rolloff, which would attenuate a second time on top of this curve. Thunder
        // is a sky-filling sound rather than a point source, and the audio ruleset already
        // specifies it as "global with distance-based delay".
        // Public and distance-only, the same shape as EnvironmentDynamicsController.TryStrikeNearAnchor:
        // a seam that lets the delay be driven without a strike, a world or a camera.
        public void QueueThunder(float distance)
        {
            pendingThunder.Add(new PendingThunder(
                Time.time + BlockiverseThunderScheduling.ResolveDelaySeconds(distance),
                BlockiverseThunderScheduling.SelectThunderCue(distance),
                BlockiverseThunderScheduling.ResolveVolumeScale(distance)));
        }

        // How many thunder claps are still in flight. Exposed because "the clip never arrived" and
        // "the clip arrived instantly" look identical from outside otherwise.
        public int PendingThunderCount => pendingThunder.Count;

        void TickPendingThunder()
        {
            // Reverse iteration so removals cannot skip an entry, and every due cue fires on the
            // frame it comes due rather than one per frame.
            for (int i = pendingThunder.Count - 1; i >= 0; i--)
            {
                if (Time.time < pendingThunder[i].DueTime)
                    continue;

                audioCuePlayer?.PlayCue(pendingThunder[i].Cue, pendingThunder[i].VolumeScale);
                pendingThunder.RemoveAt(i);
            }
        }

        readonly struct PendingThunder
        {
            public readonly float DueTime;
            public readonly BlockiverseAudioCue Cue;
            public readonly float VolumeScale;

            public PendingThunder(float dueTime, BlockiverseAudioCue cue, float volumeScale)
            {
                DueTime = dueTime;
                Cue = cue;
                VolumeScale = volumeScale;
            }
        }

        // Drives the continuous precipitation volume from what is falling at the PLAYER'S cell,
        // for the same reason the audio and the old cue selection did: one thunderstorm is rain in
        // the valley and snow on the peak above it.
        void UpdateWeatherVolume(EnvironmentState environment)
        {
            BlockiverseWeatherVolume volume = EnsureWeatherVolume();

            if (volume == null)
                return;

            volume.SetPrecipitation(environment.Precipitation, environment.PrecipitationIntensity);
        }

        BlockiverseWeatherVolume EnsureWeatherVolume()
        {
            if (weatherVolume != null)
                return weatherVolume;

            if (!TryGetHeadTransform(out Transform headTransform))
                return null;

            // Created at runtime and parented to nothing: it follows the head in POSITION only.
            // Parenting to the camera would inherit its rotation, which swings the whole volume on
            // every snap turn -- the artefact the old Local-space burst had.
            var host = new GameObject("Weather Volume");
            weatherVolume = host.AddComponent<BlockiverseWeatherVolume>();

            BlockiverseVfxPool pool = vfxCuePlayer != null ? vfxCuePlayer.Pool : null;

            weatherVolume.Configure(
                headTransform,
                pool != null ? pool.ParticleMaterial : null,
                pool != null ? pool.RainSprite : null,
                pool != null ? pool.SnowSprite : null);

            return weatherVolume;
        }

        static bool TryGetHeadTransform(out Transform headTransform)
        {
            Camera head = Camera.main;
            headTransform = head != null ? head.transform : null;
            return headTransform != null;
        }

        LightningBoltView EnsureBoltView()
        {
            if (boltView != null)
                return boltView;

            // Created at runtime, following CreativeWorldManager.CreatePlacementPreview. One
            // instance restarted per strike: the flash refuses to retrigger inside its own window
            // for comfort reasons, so two visible bolts never overlap and a pool would be
            // machinery with nothing to hold.
            var host = new GameObject("Lightning Bolt");
            boltView = host.AddComponent<LightningBoltView>();

            if (Camera.main != null)
                boltView.Configure(Camera.main.transform);

            return boltView;
        }

        void TickThunder(WeatherState state)
        {
            if (state != WeatherState.Thunderstorm)
                return;

            if (Time.time < nextThunderTime)
                return;

            nextThunderTime = Time.time + Random.Range(6.0f, 14.0f);

            // Audio only. This used to also fire a "flash" near the player every 6-14 seconds with
            // no strike behind it and no relation to LightningStruck -- an effect that pretended
            // lightning was happening while the real strikes went unseen somewhere else. Distant
            // rumble with no visible bolt is correct ambience; a flash with no bolt is a lie.
            //
            // Always the far clip now, where it used to be a 40% coin flip. Ambient thunder has no
            // strike behind it and therefore no distance -- a near crack from nowhere is the same
            // lie the flash was, and it would fight the real strikes, whose whole point is that
            // near means near.
            audioCuePlayer.PlayCue(BlockiverseAudioCue.ThunderFar);
        }

        void TickPrecipitationVfx()
        {
            if (!BlockiverseRuntimeState.AllowWorldInput)
                return;

            if (vfxCuePlayer == null || worldManager == null)
                return;

            // Rain and snow no longer come through here. They are a continuous head-locked volume
            // (BlockiverseWeatherVolume) because the burst path could not physically render them:
            // two 4.5 cm particles every 0.6 seconds is roughly one drop on screen at a time,
            // against a spec asking for a couple of hundred.
            //
            // Fog wisps stay a scatter cue. They are an accent on top of real distance fog rather
            // than the fog itself, and they are sparse by design.
            if (!activePrecipitationVfx.HasValue && lastWeatherState == WeatherState.Fog)
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
