using System;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Host-only world-environment dynamics driven by the weather machine: thunderstorm lightning
    // strikes (scorching what they hit) and snowpack accumulation/melt. Accumulation is decided
    // per sampled column from what is actually falling AND sticking THERE: the precipitation kind
    // (WeatherService.PrecipitationKind) says what arrives, and the column's own temperature says
    // whether it settles (§12: at or below freezing), so a rain state lays down snow on a freezing
    // peak while a blizzard over the warm dunes shows snowfall but lays nothing down — falling is
    // not settling. Every world edit goes through the chunk-authority
    // mutation channel, so clients receive the changes as ordinary authoritative deltas — clients
    // never simulate these locally.
    [DisallowMultipleComponent]
    public sealed class EnvironmentDynamicsController : MonoBehaviour
    {
        // Lightning cadence: one strike roll every 10 seconds of storm. The odds of that roll are
        // no longer a constant -- see LightningStormSolver, which makes a storm's violence vary
        // between storms and across each storm's own life.
        public const int LightningCheckIntervalTicks = 200;
        // Strikes keep clear of spawn and of every player head (§ comfort: no point-blank hits).
        public const int StrikeSpawnExclusionRadius = 8;
        public const int StrikePlayerExclusionRadius = 8;

        // Snow cadence: a handful of random columns sampled every 5 seconds of precipitation/clear.
        public const int SnowCheckIntervalTicks = 100;
        public const int SnowColumnsPerCheck = 6;

        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] MultiplayerEnvironmentRelay environmentRelay;

        WorldTimeClock worldTimeClock;
        BlockiverseNetworkSession session;
        int lightningTickAccumulator;
        int snowTickAccumulator;

        // Two streams, deliberately. Lightning selection retries -- it draws a variable number of
        // candidates until one is accepted -- so sharing a stream with snow would let a rejected
        // strike shift every subsequent snow column. Separate salts keep each system's sequence a
        // function of its own draws only.
        System.Random lightningRandom;
        System.Random snowRandom;

        // Reused across checks so a strike roll allocates nothing.
        readonly int[] candidateX = new int[LightningStrikeSelector.MaxSelectionAttempts];
        readonly int[] candidateZ = new int[LightningStrikeSelector.MaxSelectionAttempts];

        // Fired on the local peer when a strike lands (world position of the struck surface block).
        // The host raises it directly and mirrors it to clients as a small presentation event.
        public event Action<BlockPosition> LightningStruck;

        public void Configure(CreativeWorldManager manager, MultiplayerChunkAuthoritySync authoritySync)
        {
            worldManager = manager;
            chunkAuthoritySync = authoritySync;
        }

        void OnEnable()
        {
            ResolveReferences();
            if (environmentRelay != null)
                environmentRelay.LightningStruck += OnRemoteLightningStruck;
        }

        void OnDisable()
        {
            if (worldTimeClock != null)
            {
                worldTimeClock.Ticked -= OnWorldTick;
                worldTimeClock = null;
            }

            if (environmentRelay != null)
                environmentRelay.LightningStruck -= OnRemoteLightningStruck;
        }

        void Update()
        {
            if (worldTimeClock == null)
            {
                ResolveReferences();
                if (worldManager != null && worldManager.WorldTimeClock != null)
                {
                    worldTimeClock = worldManager.WorldTimeClock;
                    worldTimeClock.Ticked += OnWorldTick;
                }
            }
        }

        void ResolveReferences()
        {
            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = FindFirstObjectByType<MultiplayerChunkAuthoritySync>(FindObjectsInactive.Include);

            if (environmentRelay == null)
                environmentRelay = FindFirstObjectByType<MultiplayerEnvironmentRelay>(FindObjectsInactive.Include);

            // Without this the field stays null forever and IsNearAnyPlayerHead silently degrades
            // to a local-head-only check -- remote players could be struck point blank.
            if (session == null)
                session = FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
        }

        void OnWorldTick(int ticks)
        {
            TickDynamics(ticks);
        }

        // Advances the environment dynamics by world ticks. Public so tests drive it directly.
        public void TickDynamics(int ticks)
        {
            if (ticks <= 0 || !OwnsWorldMutations())
                return;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null)
                return;

            lightningRandom ??= new System.Random(world.Seed ^ 0x110b7);
            snowRandom ??= new System.Random(world.Seed ^ 0x5eed);
            WeatherSyncState weatherSync = worldManager.GetWeatherSyncState();
            WeatherState weather = weatherSync.State;

            lightningTickAccumulator += ticks;
            while (lightningTickAccumulator >= LightningCheckIntervalTicks)
            {
                lightningTickAccumulator -= LightningCheckIntervalTicks;
                if (weather == WeatherState.Thunderstorm &&
                    lightningRandom.Next(100) < ResolveStrikeChancePercent(world, weatherSync))
                {
                    TryStrikeNearAnyPlayer(world);
                }
            }

            snowTickAccumulator += ticks;
            while (snowTickAccumulator >= SnowCheckIntervalTicks)
            {
                snowTickAccumulator -= SnowCheckIntervalTicks;

                // Cheap sky-level early-out: with nothing falling anywhere there is no column
                // worth pricing a temperature for. Rain states get in — whether they land as rain
                // or as snow is then a per-column question (see TryAccumulateSnowAt).
                if (WeatherService.IsPrecipitating(weather))
                {
                    for (int i = 0; i < SnowColumnsPerCheck; i++)
                        TryAccumulateSnowAt(world, snowRandom.Next(world.Bounds.Width), snowRandom.Next(world.Bounds.Depth));
                }
                else if (weather == WeatherState.Clear)
                {
                    for (int i = 0; i < SnowColumnsPerCheck; i++)
                        TryMeltSnowAt(world, snowRandom.Next(world.Bounds.Width), snowRandom.Next(world.Bounds.Depth));
                }
            }
        }

        // Whether a weather state is INHERENTLY snowy — it falls as snow no matter how warm the
        // location is. Deliberately NOT the accumulation gate: rain states also fall as snow where
        // the column is at or below freezing, which only a per-column temperature can answer.
        // Renamed from the old IsSnowing, whose name invited exactly that mistake.
        public static bool IsInherentlySnowyState(WeatherState weather) =>
            weather == WeatherState.LightSnow || weather == WeatherState.HeavySnow || weather == WeatherState.Blizzard;

        // Pure scorch rule: what a struck surface block becomes. Meadow turf chars to dry turf;
        // leafmoss burns away. Anything else is unaffected (the strike still flashes/thunders).
        public static bool TryGetScorchResult(BlockId struck, out BlockId result)
        {
            if (struck == BlockRegistry.MeadowTurf)
            {
                result = BlockRegistry.DryTurf;
                return true;
            }

            if (struck == BlockRegistry.Leafmoss)
            {
                result = BlockRegistry.Air;
                return true;
            }

            result = default;
            return false;
        }

        // Pure stacking rule: snow settles on any solid surface but never on snowpack (one layer
        // max) and never on fluids (still or flowing).
        public static bool CanHoldSnowLayer(BlockId surface) =>
            surface != BlockRegistry.Snowpack &&
            !FluidBlocks.IsFluid(surface);

        // The odds for a single lightning check, from this storm's character and how far through
        // the storm we are. Both terms come out of the synced weather state, so a client that ever
        // needed to predict a strike would arrive at the same number.
        int ResolveStrikeChancePercent(VoxelWorld world, WeatherSyncState weatherSync)
        {
            long elapsedTicks = worldManager != null && worldManager.WorldTimeClock != null
                ? worldManager.WorldTimeClock.TotalElapsedTicks
                : weatherSync.Ticks;

            float character = LightningStormSolver.ResolveCharacter(world.Seed, elapsedTicks - weatherSync.Ticks);
            float progress = LightningStormSolver.ResolveProgress(
                weatherSync.Ticks, WeatherService.MinimumStateDurationTicks(weatherSync.State));

            return LightningStormSolver.StrikeChancePercent(character, progress);
        }

        // Picks one known player at random and strikes in a ring around them, so strikes happen
        // where somebody can actually see them. Falls back to a whole-world draw when there is no
        // head to anchor on -- a headless host still has to simulate its weather.
        //
        // Determinism, stated plainly: the cadence, the intensity roll and the RNG stream all stay
        // deterministic, but the struck COLUMN does not, because the anchor is a live head
        // position. Strike placement already depended on head proximity as a rejection input; ring
        // bias makes that dependence total. An invisible deterministic strike is worth less than a
        // visible one.
        void TryStrikeNearAnyPlayer(VoxelWorld world)
        {
            if (!TryPickStrikeAnchor(out Vector3 anchor))
            {
                TryApplyLightningStrike(
                    world, lightningRandom.Next(world.Bounds.Width), lightningRandom.Next(world.Bounds.Depth));
                return;
            }

            TryStrikeNearAnchor(world, Mathf.FloorToInt(anchor.x), Mathf.FloorToInt(anchor.z));
        }

        // Draws a ring of candidate columns around the anchor and strikes the first one the
        // existing validation accepts. Public and camera-free so tests can drive selection with no
        // scene, and so a rejected candidate no longer wastes the whole 10-second interval.
        public bool TryStrikeNearAnchor(VoxelWorld world, int anchorX, int anchorZ)
        {
            if (!OwnsWorldMutations() || world == null)
                return false;

            lightningRandom ??= new System.Random(world.Seed ^ 0x110b7);

            int count = LightningStrikeSelector.DrawRingCandidates(
                lightningRandom, anchorX, anchorZ, world.Bounds, candidateX, candidateZ);

            for (int i = 0; i < count; i++)
            {
                if (TryApplyLightningStrike(world, candidateX[i], candidateZ[i]))
                    return true;
            }

            return false;
        }

        // Every head we know about: the local camera plus each connected player object. One is
        // chosen at random so a multiplayer storm does not always centre on the host.
        bool TryPickStrikeAnchor(out Vector3 anchor)
        {
            anchor = default;
            int found = 0;

            bool usedLocalCamera = false;

            if (Camera.main != null)
            {
                anchor = Camera.main.transform.position;
                found = 1;
                usedLocalCamera = true;
            }

            if (session != null)
            {
                foreach (ulong clientId in session.ConnectedClientIds)
                {
                    // On a host, ConnectedClientIds includes the host's OWN client, whose player
                    // object resolves to the same head the camera above already contributed. Left
                    // in, the host enters the reservoir twice and draws roughly two thirds of the
                    // strikes in a two-player session -- the opposite of the even spread this is
                    // for. Skipped only when the camera actually supplied that head, so a headless
                    // host still anchors on its own player object.
                    if (usedLocalCamera && clientId == session.LocalClientId)
                        continue;

                    if (!session.TryResolvePlayerHeadWorldPosition(clientId, out Vector3 headPosition))
                        continue;

                    found++;

                    // Reservoir sampling: one pass, no allocation, and every head equally likely.
                    if (lightningRandom.Next(found) == 0)
                        anchor = headPosition;
                }
            }

            return found > 0;
        }

        // Attempts a lightning strike at the column's surface. Rejected near spawn and near any
        // player head. Scorch rules: meadow_turf → dry_turf, leafmoss burns away. Returns true
        // when a strike landed (even if the struck block had no scorch rule).
        public bool TryApplyLightningStrike(VoxelWorld world, int x, int z)
        {
            if (!OwnsWorldMutations() || world == null)
                return false;

            int surfaceY = FindTopBlockY(world, x, z);
            if (surfaceY < 0)
                return false;

            var strike = new BlockPosition(x, surfaceY, z);

            if (IsInsideSpawnExclusion(x, z) || IsNearAnyPlayerHead(strike))
                return false;

            if (TryGetScorchResult(world.GetBlock(strike), out BlockId scorched))
                SubmitMutation(strike, scorched);

            RaiseLightningStruck(strike, broadcastToClients: true);
            return true;
        }

        // Accumulates one Snowpack layer on a column's surface when what falls AT THAT COLUMN is
        // snow AND the column is cold enough for it to stick. The weather state only says
        // something is falling; WeatherService then resolves rain vs. snow from this column's own
        // biome and surface altitude, so freezing ground under a thunderstorm collects snowpack.
        // Falling is not settling (§6.3 vs §12): an inherently snowy state over warm ground — a
        // blizzard blowing across the 30 °C dunes — drifts snow VFX but lays no layer, because
        // §12 additionally requires the local temperature at or below freezing to accumulate.
        // The top block of a column has sky access by definition; sheltered cells are never the top.
        //
        // The cheap O(1) rejections run first and the environment evaluation (a biome noise
        // lookup) last, so a column that could never hold a layer never pays for one.
        public bool TryAccumulateSnowAt(VoxelWorld world, int x, int z)
        {
            if (!OwnsWorldMutations() || world == null || worldManager == null)
                return false;

            int surfaceY = FindTopBlockY(world, x, z);
            if (surfaceY < 0 || surfaceY + 1 >= world.Bounds.Height)
                return false;

            var surface = new BlockPosition(x, surfaceY, z);
            if (!CanHoldSnowLayer(world.GetBlock(surface)))
                return false;

            if (IsInsideSpawnExclusion(x, z))
                return false;

            // Evaluated at this column's own surface block, so the temperature carries its
            // altitude and biome rather than the player's position or sea level. Returns
            // false while the world has no weather service yet (a scene without a world clock),
            // and falls back to the temperate base in the biome-less flat/void presets.
            //
            // Two conditions, deliberately: snow must be FALLING here (kind == Snow) and the
            // ground must be at or below freezing for the layer to STICK (§12's accumulation
            // rule). The published temperature carries the §6.2 snow modifier, which only ever
            // pushes it colder, so nothing that would settle is rejected by using it.
            if (!worldManager.TryEvaluateEnvironment(surface, out EnvironmentState environment) ||
                environment.Precipitation != PrecipitationKind.Snow ||
                environment.Temperature > WeatherService.FreezingTemperatureC)
            {
                return false;
            }

            SubmitMutation(new BlockPosition(x, surfaceY + 1, z), BlockRegistry.Snowpack);
            return true;
        }

        // Melts exposed Snowpack during clear weather (the column's top block always has sky
        // access; buried/sheltered snow never melts). Deliberately temperature-blind, unlike
        // settling in TryAccumulateSnowAt: Clear's ~30% weather-state occupancy against ~27%
        // total precipitation is the valve that holds always-freezing terrain (tundra qualifies
        // for accumulation under every precipitating state) at a stable partial snow cover
        // instead of whitening monotonically to 100%. Do not gate this on temperature without
        // adding a sublimation path for sub-zero biomes — §12.3 documents the shipped rule and
        // the trade.
        public bool TryMeltSnowAt(VoxelWorld world, int x, int z)
        {
            if (!OwnsWorldMutations() || world == null)
                return false;

            int surfaceY = FindTopBlockY(world, x, z);
            if (surfaceY < 0)
                return false;

            var top = new BlockPosition(x, surfaceY, z);
            if (world.GetBlock(top) != BlockRegistry.Snowpack)
                return false;

            SubmitMutation(top, BlockRegistry.Air);
            return true;
        }

        void SubmitMutation(BlockPosition position, BlockId newBlock)
        {
            chunkAuthoritySync.TrySubmitMutation(
                position,
                newBlock,
                out _,
                out _,
                BlockMutationSubmissionKind.WorldSimulation);
        }

        bool OwnsWorldMutations()
        {
            if (chunkAuthoritySync == null)
                return false;

            return chunkAuthoritySync.CurrentBoundary.CanCommitMutations;
        }

        bool IsInsideSpawnExclusion(int x, int z)
        {
            WorldGenerationSettings settings = worldManager != null ? worldManager.Settings : null;
            if (settings == null)
                return false;

            return LightningStrikeSelector.IsInsideExclusion(
                x, z, settings.SpawnPosition.X, settings.SpawnPosition.Z, StrikeSpawnExclusionRadius);
        }

        void RaiseLightningStruck(BlockPosition strike, bool broadcastToClients)
        {
            LightningStruck?.Invoke(strike);

            if (broadcastToClients && environmentRelay != null)
                environmentRelay.BroadcastLightningStrike(strike);
        }

        void OnRemoteLightningStruck(BlockPosition strike)
        {
            RaiseLightningStruck(strike, broadcastToClients: false);
        }

        // Horizontal distance check against the local head and every connected player object.
        bool IsNearAnyPlayerHead(BlockPosition strike)
        {
            if (IsHeadNear(Camera.main != null ? Camera.main.transform.position : (Vector3?)null, strike))
                return true;

            if (session == null)
                return false;

            foreach (ulong clientId in session.ConnectedClientIds)
            {
                if (session.TryResolvePlayerHeadWorldPosition(clientId, out Vector3 headPosition) &&
                    IsHeadNear(headPosition, strike))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsHeadNear(Vector3? head, BlockPosition strike)
        {
            if (!head.HasValue)
                return false;

            float dx = head.Value.x - (strike.X + 0.5f);
            float dz = head.Value.z - (strike.Z + 0.5f);
            return dx * dx + dz * dz <= StrikePlayerExclusionRadius * StrikePlayerExclusionRadius;
        }

        // Topmost non-air cell of a column (-1 for an empty or out-of-range column). The top
        // block of a column has sky access by definition.
        public static int FindTopBlockY(VoxelWorld world, int x, int z)
        {
            if (x < 0 || x >= world.Bounds.Width || z < 0 || z >= world.Bounds.Depth)
                return -1;

            for (int y = world.Bounds.Height - 1; y >= 0; y--)
            {
                if (world.GetBlock(new BlockPosition(x, y, z)) != BlockRegistry.Air)
                    return y;
            }

            return -1;
        }
    }
}
