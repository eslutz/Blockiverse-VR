using System;
using System.Collections.Generic;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
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
        // Consumable effect amounts live with the effect table in ConsumableEffects; hazard
        // damage amounts and rates live with the hazard table in BlockHazards.
        const float HazardScanIntervalSeconds = 0.25f;
        const float ClockSearchIntervalSeconds = 1.0f;

        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] CreativeWorldManager worldManager;

        WorldTimeClock worldTimeClock;
        float nextClockSearchTime;
        float nextHazardScanTime;
        readonly Dictionary<string, float> nextHazardApplyTimes = new();

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
            // The clock may not exist until a world is loaded; bind lazily. The world manager
            // exposes it directly; the scene-wide search is a throttled fallback for setups
            // without a manager (it walks all loaded objects, too costly to run per frame).
            if (worldTimeClock == null)
            {
                if (worldManager != null && worldManager.WorldTimeClock != null)
                {
                    worldTimeClock = worldManager.WorldTimeClock;
                }
                else if (Time.time >= nextClockSearchTime)
                {
                    nextClockSearchTime = Time.time + ClockSearchIntervalSeconds;
                    worldTimeClock = FindFirstObjectByType<WorldTimeClock>();
                }

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

            BlockPosition headCell = CreativeInteractionController.ToBlockPosition(head.position);
            var feetCell = new BlockPosition(headCell.X, headCell.Y - 1, headCell.Z);
            var groundCell = new BlockPosition(headCell.X, headCell.Y - 2, headCell.Z);

            CheckHazardCell(world, headCell, HazardContactCells.Head);
            CheckHazardCell(world, feetCell, HazardContactCells.Feet);
            CheckHazardCell(world, groundCell, HazardContactCells.GroundBelow);
        }

        // Applies the cell's hazard when the block is hazardous and triggers on this contact
        // cell; TryApplyHazard's per-hazard throttle dedupes multi-cell contact in one scan.
        void CheckHazardCell(VoxelWorld world, BlockPosition cell, HazardContactCells contact)
        {
            if (!world.Bounds.Contains(cell))
                return;

            if (BlockHazards.TryGetHazard(world.GetBlock(cell), out BlockHazard hazard) &&
                (hazard.ContactCells & contact) != 0)
            {
                TryApplyHazard(hazard.Hazard);
            }
        }

        void TryApplyHazard(HazardVolumeDefinition hazard)
        {
            if (nextHazardApplyTimes.TryGetValue(hazard.Id, out float nextApply) && Time.time < nextApply)
                return;

            nextHazardApplyTimes[hazard.Id] = Time.time + hazard.TickIntervalSeconds;
            hazard.ApplyTick(Vitals);
        }

        void OnConsumableConsumed(ItemStack consumed)
        {
            if (!consumed.IsEmpty)
                ConsumableEffects.TryApply(consumed.ItemId, Vitals, SurvivalVitals);
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
            CreativeWorldManager.PositionRigAtSpawn(spawn);
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
            int surfaceY = StructureService.FindSurfaceY(world, x, z);
            return new BlockPosition(x, surfaceY >= 0 ? surfaceY + 1 : 1, z);
        }
    }
}
