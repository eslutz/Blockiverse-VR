using System;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Host-only world-environment dynamics driven by the weather machine: thunderstorm lightning
    // strikes (scorching what they hit) and tundra snow accumulation/melt. Every world edit goes
    // through the chunk-authority mutation channel, so clients receive the changes as ordinary
    // authoritative deltas — clients never simulate these locally.
    [DisallowMultipleComponent]
    public sealed class EnvironmentDynamicsController : MonoBehaviour
    {
        // Lightning cadence/odds: roughly one strike roll every 10 seconds of storm, ~35% each.
        public const int LightningCheckIntervalTicks = 200;
        public const int LightningStrikeChancePercent = 35;
        // Strikes keep clear of spawn and of every player head (§ comfort: no point-blank hits).
        public const int StrikeSpawnExclusionRadius = 8;
        public const int StrikePlayerExclusionRadius = 8;

        // Snow cadence: a handful of random columns sampled every 5 seconds of snowfall/clear.
        public const int SnowCheckIntervalTicks = 100;
        public const int SnowColumnsPerCheck = 6;

        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;

        WorldTimeClock worldTimeClock;
        int lightningTickAccumulator;
        int snowTickAccumulator;
        System.Random random;
        // Biome lookups are pure seed math; cache the resolver per settings instance.
        SurvivalBiomeResolver biomeResolver;
        WorldGenerationSettings biomeResolverSettings;

        // Fired on the host when a strike lands (world position of the struck surface block).
        // Feedback layers can flash/thunder from it; clients get the block change via deltas.
        public event Action<BlockPosition> LightningStruck;

        public void Configure(CreativeWorldManager manager, MultiplayerChunkAuthoritySync authoritySync)
        {
            worldManager = manager;
            chunkAuthoritySync = authoritySync;
        }

        void OnEnable()
        {
            ResolveReferences();
        }

        void OnDisable()
        {
            if (worldTimeClock != null)
            {
                worldTimeClock.Ticked -= OnWorldTick;
                worldTimeClock = null;
            }
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

            random ??= new System.Random(world.Seed ^ 0x5eed);
            WeatherState weather = worldManager.GetWeatherSyncState().State;

            lightningTickAccumulator += ticks;
            while (lightningTickAccumulator >= LightningCheckIntervalTicks)
            {
                lightningTickAccumulator -= LightningCheckIntervalTicks;
                if (weather == WeatherState.Thunderstorm && random.Next(100) < LightningStrikeChancePercent)
                    TryStrikeRandomColumn(world);
            }

            snowTickAccumulator += ticks;
            while (snowTickAccumulator >= SnowCheckIntervalTicks)
            {
                snowTickAccumulator -= SnowCheckIntervalTicks;

                if (IsSnowing(weather))
                {
                    for (int i = 0; i < SnowColumnsPerCheck; i++)
                        TryAccumulateSnowAt(world, random.Next(world.Bounds.Width), random.Next(world.Bounds.Depth));
                }
                else if (weather == WeatherState.Clear)
                {
                    for (int i = 0; i < SnowColumnsPerCheck; i++)
                        TryMeltSnowAt(world, random.Next(world.Bounds.Width), random.Next(world.Bounds.Depth));
                }
            }
        }

        public static bool IsSnowing(WeatherState weather) =>
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
        // max) and never on fluids.
        public static bool CanHoldSnowLayer(BlockId surface) =>
            surface != BlockRegistry.Snowpack &&
            surface != BlockRegistry.Freshwater &&
            surface != BlockRegistry.Brine;

        void TryStrikeRandomColumn(VoxelWorld world)
        {
            TryApplyLightningStrike(world, random.Next(world.Bounds.Width), random.Next(world.Bounds.Depth));
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

            LightningStruck?.Invoke(strike);
            return true;
        }

        // Accumulates one Snowpack layer on a tundra column's surface during snowfall. The top
        // block of a column has sky access by definition; sheltered cells are never the top.
        public bool TryAccumulateSnowAt(VoxelWorld world, int x, int z)
        {
            if (!OwnsWorldMutations() || world == null || !IsTundraColumn(x, z))
                return false;

            int surfaceY = FindTopBlockY(world, x, z);
            if (surfaceY < 0 || surfaceY + 1 >= world.Bounds.Height)
                return false;

            if (!CanHoldSnowLayer(world.GetBlock(new BlockPosition(x, surfaceY, z))))
                return false;

            if (IsInsideSpawnExclusion(x, z))
                return false;

            SubmitMutation(new BlockPosition(x, surfaceY + 1, z), BlockRegistry.Snowpack);
            return true;
        }

        // Melts exposed Snowpack during clear weather (the column's top block always has sky
        // access; buried/sheltered snow never melts).
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
            chunkAuthoritySync.TrySubmitMutation(position, newBlock, out _, out _);
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

            int dx = x - settings.SpawnPosition.X;
            int dz = z - settings.SpawnPosition.Z;
            return dx * dx + dz * dz <= StrikeSpawnExclusionRadius * StrikeSpawnExclusionRadius;
        }

        // Horizontal distance check against the local head and every connected player object.
        bool IsNearAnyPlayerHead(BlockPosition strike)
        {
            if (IsHeadNear(Camera.main != null ? Camera.main.transform.position : (Vector3?)null, strike))
                return true;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
                return false;

            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (client.PlayerObject != null &&
                    IsHeadNear(client.PlayerObject.transform.position, strike))
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

        bool IsTundraColumn(int x, int z)
        {
            WorldGenerationSettings settings = worldManager != null ? worldManager.Settings : null;
            if (settings == null ||
                worldManager.GenerationPreset != CreativeWorldGenerationPreset.SurvivalLite)
            {
                return false;
            }

            if (biomeResolver == null || !ReferenceEquals(biomeResolverSettings, settings))
            {
                biomeResolver = new SurvivalBiomeResolver(settings.Seed, settings.Bounds.Height);
                biomeResolverSettings = settings;
            }

            return biomeResolver.BiomeIndexAt(x, z) == 5; // canonical biome order: Tundra = 5
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
