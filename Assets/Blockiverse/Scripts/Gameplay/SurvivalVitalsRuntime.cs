using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Runtime integration of the local player's survival vitals (voxel_survival_ruleset §13):
    // owns the PlayerVitals + SurvivalVitals instances, ticks hunger/thirst/stamina from the
    // world clock (with starvation/dehydration damage), applies contact hazard damage from
    // hazardous blocks (thornbrush, campfire), applies consumable effects when the survival
    // sync confirms an item was consumed, and exposes death/respawn to the menu layer.
    //
    // Vitals are local-player simulation state (each peer simulates its own player); only the
    // consumable inventory decrement is host-authoritative, via MultiplayerSurvivalSync.
    [DisallowMultipleComponent]
    public sealed class SurvivalVitalsRuntime : MonoBehaviour
    {
        // Balance constants — canon fixes the 0..100 vital ranges and the consumable items
        // (clean_water_flask "restores thirst or stamina", field_bandage heals); the amounts
        // and hazard rates are tunable.
        public const int CleanWaterThirstRestore = 40;
        public const int CleanWaterStaminaRestore = 20;
        public const int ThornbrushDamage = 1;
        public const float ThornbrushIntervalSeconds = 0.5f;
        public const int CampfireDamage = 2;
        public const float CampfireIntervalSeconds = 0.5f;
        const float HazardScanIntervalSeconds = 0.25f;

        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] CreativeWorldManager worldManager;

        WorldTimeClock worldTimeClock;
        float nextHazardScanTime;
        readonly Dictionary<string, float> nextHazardApplyTimes = new();

        static readonly HazardVolumeDefinition ThornbrushHazard = new(
            "thornbrush",
            new HazardDamage(ThornbrushDamage, HazardDamageKind.Environmental, "thornbrush"),
            ThornbrushIntervalSeconds);

        static readonly HazardVolumeDefinition CampfireHazard = new(
            "campfire",
            new HazardDamage(CampfireDamage, HazardDamageKind.Heat, "campfire"),
            CampfireIntervalSeconds);

        public PlayerVitals Vitals { get; } = new PlayerVitals();
        public SurvivalVitals SurvivalVitals { get; } = new SurvivalVitals();

        // Fired when the local player's health reaches zero; the menu layer shows the death screen.
        public event Action LocalPlayerDied;

        // Bedroll respawn anchors are not implemented yet (no bedroll block exists); the death
        // menu offers world-spawn respawn only.
        public bool HasBedrollSpawn => false;

        public void Configure(MultiplayerSurvivalSync sync, CreativeWorldManager manager)
        {
            UnwireSurvivalSync();
            survivalSync = sync;
            worldManager = manager;
            WireSurvivalSync();
        }

        void OnEnable()
        {
            Vitals.Died += OnVitalsDied;
            ResolveReferences();
            WireSurvivalSync();
        }

        void OnDisable()
        {
            Vitals.Died -= OnVitalsDied;
            UnwireSurvivalSync();
            if (worldTimeClock != null)
            {
                worldTimeClock.Ticked -= OnWorldTick;
                worldTimeClock = null;
            }
        }

        void Update()
        {
            // The clock may not exist until a world is loaded; bind lazily.
            if (worldTimeClock == null)
            {
                worldTimeClock = FindFirstObjectByType<WorldTimeClock>();
                if (worldTimeClock != null)
                    worldTimeClock.Ticked += OnWorldTick;
            }

            TickHazards();
        }

        void ResolveReferences()
        {
            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
        }

        void WireSurvivalSync()
        {
            if (survivalSync == null)
                return;

            survivalSync.ConsumableConsumed -= OnConsumableConsumed;
            survivalSync.ConsumableConsumed += OnConsumableConsumed;
        }

        void UnwireSurvivalSync()
        {
            if (survivalSync != null)
                survivalSync.ConsumableConsumed -= OnConsumableConsumed;
        }

        // Vitals only deplete (and hazards only damage) in survival mode; creative players are immune.
        bool InSurvivalMode =>
            survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;

        void OnWorldTick(int ticks)
        {
            if (!InSurvivalMode || Vitals.IsDead)
                return;

            int starvationDamage = SurvivalVitals.Tick(ticks);
            if (starvationDamage > 0)
                Vitals.ApplyDamage(starvationDamage);
        }

        // Periodic scan of the cells the player occupies/stands on for hazardous blocks; each
        // hazard applies its damage on its own cadence while contact persists.
        void TickHazards()
        {
            if (!InSurvivalMode || Vitals.IsDead)
                return;

            if (Time.time < nextHazardScanTime)
                return;

            nextHazardScanTime = Time.time + HazardScanIntervalSeconds;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            Transform head = Camera.main != null ? Camera.main.transform : null;
            if (world == null || head == null)
                return;

            Vector3 position = head.position;
            var headCell = new BlockPosition(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.y),
                Mathf.FloorToInt(position.z));
            var feetCell = new BlockPosition(headCell.X, headCell.Y - 1, headCell.Z);
            var groundCell = new BlockPosition(headCell.X, headCell.Y - 2, headCell.Z);

            // Thornbrush damages entities walking through it (body cells); campfire burns when
            // stood in or directly on (feet/ground cells).
            if (IsBlockAt(world, feetCell, BlockRegistry.Thornbrush) || IsBlockAt(world, headCell, BlockRegistry.Thornbrush))
                TryApplyHazard(ThornbrushHazard);

            if (IsBlockAt(world, feetCell, BlockRegistry.Campfire) || IsBlockAt(world, groundCell, BlockRegistry.Campfire))
                TryApplyHazard(CampfireHazard);
        }

        static bool IsBlockAt(VoxelWorld world, BlockPosition position, BlockId block) =>
            world.Bounds.Contains(position) && world.GetBlock(position) == block;

        void TryApplyHazard(HazardVolumeDefinition hazard)
        {
            if (nextHazardApplyTimes.TryGetValue(hazard.Id, out float nextApply) && Time.time < nextApply)
                return;

            nextHazardApplyTimes[hazard.Id] = Time.time + hazard.TickIntervalSeconds;
            hazard.ApplyTick(Vitals);
        }

        void OnConsumableConsumed(ItemStack consumed)
        {
            if (consumed.IsEmpty)
                return;

            if (consumed.ItemId == ItemId.FieldBandage)
            {
                RecoveryWrap.ApplyTo(Vitals);
            }
            else if (consumed.ItemId == ItemId.CleanWaterFlask)
            {
                SurvivalVitals.Drink(CleanWaterThirstRestore);
                SurvivalVitals.RecoverStamina(CleanWaterStaminaRestore);
            }
        }

        void OnVitalsDied(HealthChangeResult result)
        {
            LocalPlayerDied?.Invoke();
        }

        // Respawns the local player at the world spawn: restores all vitals and moves the rig.
        public void Respawn()
        {
            BlockPosition spawn = ResolveSpawnPosition();
            Vitals.RespawnAt(spawn);
            SurvivalVitals.ResetToFull();

            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            if (rig != null)
                rig.transform.position = new Vector3(spawn.X + 0.5f, spawn.Y, spawn.Z + 0.5f);
        }

        BlockPosition ResolveSpawnPosition()
        {
            if (worldManager != null && worldManager.Settings != null)
                return worldManager.Settings.SpawnPosition;

            // No generation settings (e.g. an authoritative snapshot client): stand on the highest
            // solid block at the world's center column.
            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null)
                return new BlockPosition(0, 1, 0);

            int x = world.Bounds.Width / 2;
            int z = world.Bounds.Depth / 2;
            for (int y = world.Bounds.Height - 1; y >= 0; y--)
            {
                var probe = new BlockPosition(x, y, z);
                if (world.GetBlock(probe) != BlockRegistry.Air)
                    return new BlockPosition(x, y + 1, z);
            }

            return new BlockPosition(x, 1, z);
        }
    }
}
