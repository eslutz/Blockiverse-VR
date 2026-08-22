using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Runtime integration of the local player's survival vitals (voxel_survival_ruleset §13):
    // owns the PlayerVitals + SurvivalVitals instances, ticks hunger/thirst/stamina from the
    // world clock (with starvation/dehydration and cold-exposure damage), applies contact hazard
    // damage from hazardous blocks (thornbrush, campfire, emberflow), applies fall impact damage,
    // applies consumable effects when the survival sync confirms an item was consumed, and exposes
    // death/respawn to the menu layer.
    //
    // Vitals are local-player simulation state (each peer simulates its own player); only the
    // consumable inventory decrement is host-authoritative, via MultiplayerSurvivalSync.
    [DisallowMultipleComponent]
    public sealed class SurvivalVitalsRuntime : MonoBehaviour, ISurvivalVitalsContext
    {
        // Consumable effect amounts live with the effect table in ConsumableEffects; hazard
        // damage amounts and rates live with the hazard table in BlockHazards.
        const float HazardScanIntervalSeconds = 0.25f;
        // Lifted off the capsule base so the sample lands inside the cell the player is standing
        // in rather than the one below it when the capsule rests exactly on a block boundary.
        const float FeetSampleHeightMeters = 0.1f;
        // How far below the capsule base IsControllerLanded probes for solid ground. Must exceed
        // the hover gap GravityProvider can leave (its grounding sphere-cast reaches 0.04 m below
        // the rig origin while the capsule base sits skinWidth above it, so the capsule can come
        // to rest ~0.06 m off the floor with no contact ever reported), and stay below the
        // per-frame fall step at damaging speeds so a genuinely airborne frame never reads as
        // grounded (a fall just past the 3 m safe distance already moves ~0.13 m per 60 Hz frame).
        const float LandingProbeDepthMeters = 0.1f;
        const float ClockSearchIntervalSeconds = 1.0f;
        const float WorldDrinkCooldownSeconds = 1.5f;
        public const int HarvestStaminaCost = 2;
        public const int WorldDrinkThirstRestore = 15;

        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] CharacterController characterController;

        WorldTimeClock worldTimeClock;
        Transform cachedRigTransform;
        Transform cachedHeadTransform;
        float nextClockSearchTime;
        float nextHazardScanTime;
        float nextWorldDrinkTime;
        bool trackingFall;
        bool wasFeetInFluid;
        float airbornePeakY;
        bool deathDropSubmitted;
        bool hasLastDeathDropPosition;
        bool vitalsEventsWired;
        BlockPosition lastDeathDropPosition;
        readonly Dictionary<string, float> nextHazardApplyTimes = new();
        readonly List<BlockPosition> bedrollPositions = new();
        SurvivalDifficultyProfile difficulty = SurvivalDifficultyProfile.Normal;

        public PlayerVitals Vitals { get; } = new PlayerVitals();
        public SurvivalVitals SurvivalVitals { get; } = new SurvivalVitals();

        // Core read-model seams (IPlayerVitalsView / ISurvivalVitalsView) so UI binds vitals
        // without referencing the Blockiverse.Survival.Health assembly.
        public IPlayerVitalsView HealthView => Vitals;
        public ISurvivalVitalsView SurvivalVitalsView => SurvivalVitals;

        // Fired when the local player's health reaches zero; the menu layer shows the death screen.
        public event Action LocalPlayerDied;
        public event Action<HealthChangeResult> LocalPlayerDamaged;
        public event Action<HealthChangeResult> LocalPlayerLowHealth;
        // Presentation hooks. Vitals is simulation state, so it raises events and lets the
        // feedback bridge decide what they sound like (ruleset §1 decoupled presentation).
        public event Action WorldDrinkTaken;
        public event Action<FluidFamily, float> LocalPlayerEnteredFluid;

        public bool HasBedrollSpawn => TryResolveBedrollSpawnPosition(out _);
        public bool HasDeathDropPosition => hasLastDeathDropPosition;
        public BlockPosition LastDeathDropPosition => lastDeathDropPosition;
        public SurvivalDifficultyProfile Difficulty => difficulty;
        public string DifficultyId => difficulty.Id;

        public void Configure(MultiplayerSurvivalSync sync, CreativeWorldManager manager)
        {
            UnwireSurvivalSync();
            survivalSync = sync;
            worldManager = manager;
            WireSurvivalSync();
        }

        public void ConfigureDifficulty(string difficultyId)
        {
            difficulty = SurvivalDifficultyProfile.FromId(difficultyId);
        }

        void OnEnable()
        {
            WireVitalsEvents();
            cachedHeadTransform = Camera.main != null ? Camera.main.transform : null;
            ResolveReferences();
            WireSurvivalSync();
        }

        void OnDisable()
        {
            UnwireVitalsEvents();
            cachedRigTransform = null;
            cachedHeadTransform = null;
            characterController = null;
            trackingFall = false;
            UnwireSurvivalSync();
            if (worldTimeClock != null)
            {
                worldTimeClock.Ticked -= OnWorldTick;
                worldTimeClock = null;
            }
        }

        void WireVitalsEvents()
        {
            if (vitalsEventsWired)
                return;

            Vitals.HealthChanged += OnVitalsHealthChanged;
            Vitals.Died += OnVitalsDied;
            vitalsEventsWired = true;
        }

        void UnwireVitalsEvents()
        {
            if (!vitalsEventsWired)
                return;

            Vitals.HealthChanged -= OnVitalsHealthChanged;
            Vitals.Died -= OnVitalsDied;
            vitalsEventsWired = false;
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
                    worldTimeClock = BlockiverseSceneLookup.Find<WorldTimeClock>();
                }

                if (worldTimeClock != null)
                    worldTimeClock.Ticked += OnWorldTick;
            }

            TickHazards();
            TickFallDamage();
        }

        void ResolveReferences()
        {
            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            if (characterController == null)
                characterController = ResolveRigTransform()?.GetComponent<CharacterController>();
        }

        void WireSurvivalSync()
        {
            if (survivalSync == null)
                return;

            survivalSync.ConsumableConsumed -= OnConsumableConsumed;
            survivalSync.ConsumableConsumed += OnConsumableConsumed;
            survivalSync.WorldDrinkRequested -= OnWorldDrinkRequested;
            survivalSync.WorldDrinkRequested += OnWorldDrinkRequested;
            survivalSync.CommandFeedback -= OnCommandFeedback;
            survivalSync.CommandFeedback += OnCommandFeedback;
        }

        void UnwireSurvivalSync()
        {
            if (survivalSync == null)
                return;

            survivalSync.ConsumableConsumed -= OnConsumableConsumed;
            survivalSync.WorldDrinkRequested -= OnWorldDrinkRequested;
            survivalSync.CommandFeedback -= OnCommandFeedback;
        }

        // Vitals only deplete (and hazards only damage) in survival mode; creative players are immune.
        bool InSurvivalMode =>
            survivalSync != null && survivalSync.CurrentMode == PlayerModeState.Survival;

        void OnWorldTick(int ticks)
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead)
                return;

            int starvationDamage = SurvivalVitals.Tick(ticks, difficulty);
            if (starvationDamage > 0)
                Vitals.ApplyDamage(starvationDamage);

            int environmentDamage = TickEnvironmentExposure(ticks);
            if (environmentDamage > 0)
                Vitals.ApplyDamage(environmentDamage);
        }

        // Periodic scan of the cells the player occupies/stands on for hazardous blocks; each
        // hazard applies its damage on its own cadence while contact persists.
        void TickHazards()
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead)
                return;

            if (Time.time < nextHazardScanTime)
                return;

            nextHazardScanTime = Time.time + HazardScanIntervalSeconds;

            // XR cameras can spawn after enable; refresh the cache lazily until one exists.
            if (cachedHeadTransform == null)
                cachedHeadTransform = Camera.main != null ? Camera.main.transform : null;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null || cachedHeadTransform == null)
                return;

            BlockPosition headCell = CreativeInteractionController.ToBlockPosition(cachedHeadTransform.position);
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
            Vitals.ApplyDamage(difficulty.ScaleHazardDamage(hazard.DamagePerTick.Amount));
        }

        int TickEnvironmentExposure(int ticks)
        {
            if (worldManager == null || !TryResolveHeadPosition(out Vector3 headPosition))
            {
                SurvivalVitals.ResetEnvironmentExposure();
                return 0;
            }

            // Query at the head cell, not just its altitude: cold exposure is what makes the tundra
            // and the highland peaks dangerous, and both of those are biome/altitude facts about
            // where the player is standing rather than properties of the sky overhead.
            BlockPosition headCell = CreativeInteractionController.ToBlockPosition(headPosition);
            if (!worldManager.TryEvaluateEnvironment(headCell, out EnvironmentState environment))
            {
                SurvivalVitals.ResetEnvironmentExposure();
                return 0;
            }

            bool isNight = worldTimeClock != null && !WorldTimeClock.IsDay(worldTimeClock.NormalizedTime);
            var exposure = new SurvivalEnvironmentExposure(
                environment.Temperature,
                skyExposed: !worldManager.IsHeadUnderground(headPosition),
                isNight,
                environment.PrecipitationIntensity,
                environment.StormIntensity);
            return SurvivalVitals.TickEnvironmentExposure(ticks, exposure, difficulty);
        }

        void TickFallDamage()
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead)
            {
                trackingFall = false;
                return;
            }

            CharacterController controller = ResolveCharacterController();
            if (controller == null)
            {
                trackingFall = false;
                return;
            }

            float currentY = controller.transform.position.y;

            // Water breaks a fall (voxel_survival_ruleset §5.6): dropping into freshwater or brine
            // cancels the tracked fall instead of charging impact damage on the seabed. Emberflow
            // does not — landing in lava hurts, on top of the contact hazard it already applies.
            // Before the fluid physics layer existed the player could not fall at all, so this
            // path never ran; it becomes reachable the moment gravity stops treating water as
            // ground, which is why it ships alongside that fix rather than after it.
            bool feetInFluid = TryGetFeetFluidFamily(controller, out FluidFamily feetFluid);
            if (feetInFluid && !wasFeetInFluid)
            {
                // Entry edge only — a splash every frame while wading would be a
                // machine gun. Impact height scales the cue so stepping in is not
                // as loud as falling in.
                float entrySpeed = trackingFall ? Mathf.Max(airbornePeakY - currentY, 0f) : 0f;
                LocalPlayerEnteredFluid?.Invoke(feetFluid, entrySpeed);
            }
            wasFeetInFluid = feetInFluid;

            if (feetInFluid && feetFluid != FluidFamily.Emberflow)
            {
                trackingFall = false;
                airbornePeakY = currentY;
                return;
            }

            if (!IsControllerLanded(controller))
            {
                if (!trackingFall)
                {
                    trackingFall = true;
                    airbornePeakY = currentY;
                }
                else if (currentY > airbornePeakY)
                {
                    airbornePeakY = currentY;
                }
                return;
            }

            if (!trackingFall)
            {
                airbornePeakY = currentY;
                return;
            }

            float fallMeters = airbornePeakY - currentY;
            trackingFall = false;
            airbornePeakY = currentY;
            ApplyFallImpact(fallMeters);
        }

        // Whether the player's capsule has landed for fall-damage purposes. CharacterController
        // .isGrounded alone is not enough: it only reflects the latest Move call, and XRI's
        // GravityProvider stops issuing moves the moment its own head-based grounding sphere-cast
        // sees solid terrain — a cast whose reach is 0.04 m below the rig origin (radius 0.09,
        // distance buffer -0.05) while the capsule base sits skinWidth above the origin. A fall
        // whose last airborne frame ends inside that window stops with the capsule hovering a few
        // centimetres off the floor: gravity is blocked, no further Move ever runs, isGrounded
        // stays false forever, and the tracked fall would never be charged. Deterministic at the
        // water-physics fixture's pinned 60 Hz step; a per-fall frame-phase lottery on device.
        // So also treat solid ground just below the capsule as landed, probing with the same
        // solid-terrain-only mask gravity grounds on — fluid must stay invisible to this probe
        // (water is never a landing surface; the §5.6 feet-fluid check above handles fluids).
        static bool IsControllerLanded(CharacterController controller)
        {
            if (controller.isGrounded)
                return true;

            Bounds bounds = controller.bounds;
            float probeRadius = Mathf.Max(0.05f, controller.radius - controller.skinWidth);
            var probeOrigin = new Vector3(bounds.center.x, bounds.min.y + probeRadius, bounds.center.z);
            return Physics.SphereCast(
                new Ray(probeOrigin, Vector3.down),
                probeRadius,
                LandingProbeDepthMeters,
                BlockiverseProject.VoxelGroundLayerMask,
                QueryTriggerInteraction.Ignore);
        }

        // The fluid family occupying the player's feet cell, if any. Sampled from the collision
        // capsule rather than the rig transform so it stays correct while crouching and under
        // real-player-height tracking, both of which move the capsule relative to the origin.
        bool TryGetFeetFluidFamily(CharacterController controller, out FluidFamily family)
        {
            family = default;

            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null)
                return false;

            Bounds bounds = controller.bounds;
            var feetPoint = new Vector3(bounds.center.x, bounds.min.y + FeetSampleHeightMeters, bounds.center.z);
            BlockPosition feetCell = CreativeInteractionController.ToBlockPosition(feetPoint);

            if (!world.Bounds.Contains(feetCell))
                return false;

            return FluidBlocks.TryGetFamily(world.GetBlock(feetCell), out family);
        }

        public int ApplyFallImpact(float fallMeters)
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead)
                return 0;

            int damage = SurvivalVitals.ComputeFallDamage(fallMeters, difficulty);
            if (damage > 0)
                Vitals.ApplyDamage(damage);
            return damage;
        }

        public int ApplyEnvironmentExposure(int ticks, SurvivalEnvironmentExposure exposure)
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead)
                return 0;

            int damage = SurvivalVitals.TickEnvironmentExposure(ticks, exposure, difficulty);
            if (damage > 0)
                Vitals.ApplyDamage(damage);
            return damage;
        }

        CharacterController ResolveCharacterController()
        {
            if (characterController != null)
                return characterController;

            Transform rig = ResolveRigTransform();
            characterController = rig != null ? rig.GetComponent<CharacterController>() : null;
            return characterController;
        }

        bool TryResolveHeadPosition(out Vector3 position)
        {
            if (cachedHeadTransform == null)
                cachedHeadTransform = Camera.main != null ? Camera.main.transform : null;

            if (cachedHeadTransform != null)
            {
                position = cachedHeadTransform.position;
                return true;
            }

            Transform rig = ResolveRigTransform();
            if (rig != null)
            {
                position = rig.position;
                return true;
            }

            position = default;
            return false;
        }

        Transform ResolveRigTransform()
        {
            if (cachedRigTransform != null)
                return cachedRigTransform;

            cachedRigTransform = BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig)
                ? rig
                : null;
            return cachedRigTransform;
        }

        void OnConsumableConsumed(ItemStack consumed)
        {
            if (!consumed.IsEmpty)
                ConsumableEffects.TryApply(consumed.ItemId, Vitals, SurvivalVitals);
        }

        void OnWorldDrinkRequested()
        {
            TryDrinkFromWorldSource();
        }

        void OnCommandFeedback(SurvivalCommandResult result, BlockPosition _)
        {
            if (result.Accepted && result.CommandKind == SurvivalCommandKind.HarvestResource)
                TrySpendHarvestStamina();
        }

        public bool TrySpendHarvestStamina()
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead || SurvivalVitals.Stamina <= 0)
                return false;

            int cost = Math.Min(HarvestStaminaCost, SurvivalVitals.Stamina);
            return SurvivalVitals.TrySpendStamina(cost);
        }

        // ── Player save state (world persistence) ────────────────────────────

        // Captures the local player's presence (rig position/heading) and vitals for a save.
        // Returns null when no rig exists (headless/server contexts).
        public SavedPlayerState BuildPlayerSaveState()
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return null;

            Vector3 position = rig.position;
            return new SavedPlayerState
            {
                PositionX = position.x,
                PositionY = position.y,
                PositionZ = position.z,
                YawDegrees = rig.eulerAngles.y,
                Health = Vitals.CurrentHealth,
                Hunger = SurvivalVitals.Hunger,
                Thirst = SurvivalVitals.Thirst,
                Stamina = SurvivalVitals.Stamina
            };
        }

        // Death-return saves must persist the state the player will resume from, even if the menu
        // action order changes. Build a post-respawn snapshot without mutating live vitals.
        public SavedPlayerState BuildRespawnedPlayerSaveState()
        {
            SavedPlayerState state = BuildPlayerSaveState();
            if (state == null || !Vitals.IsDead)
                return state;

            BlockPosition spawn = ResolveSpawnPosition();
            state.PositionX = spawn.X + 0.5f;
            state.PositionY = spawn.Y;
            state.PositionZ = spawn.Z + 0.5f;
            state.Health = Vitals.MaxHealth;
            state.Hunger = SurvivalVitals.Max;
            state.Thirst = SurvivalVitals.Max;
            state.Stamina = SurvivalVitals.Max;
            return state;
        }

        // Restores the local player's presence and vitals from a save. A save without player
        // state is a fresh spawn: vitals reset to full so the previous session's hunger/damage
        // never leaks into the loaded world (the caller positions the rig at the world spawn).
        public void RestorePlayerSaveState(SavedPlayerState state)
        {
            deathDropSubmitted = false;
            hasLastDeathDropPosition = false;

            if (state == null)
            {
                ResetVitalsToFull();
                return;
            }

            CreativeWorldManager.PositionRig(
                new Vector3(state.PositionX, state.PositionY, state.PositionZ),
                state.YawDegrees);
            Vitals.RestoreHealth(state.Health);
            SurvivalVitals.RestoreFrom(state.Hunger, state.Thirst, state.Stamina);
        }

        // Full health/hunger/thirst/stamina without moving the rig.
        public void ResetVitalsToFull()
        {
            deathDropSubmitted = false;
            hasLastDeathDropPosition = false;
            Vitals.RestoreHealth(Vitals.MaxHealth);
            SurvivalVitals.RestoreFrom(SurvivalVitals.Max, SurvivalVitals.Max, SurvivalVitals.Max);
        }

        // Drinks directly from a world fluid source (freshwater): restores thirst on a short
        // cooldown. Vitals are local-player simulation state, so no host round-trip is needed.
        public bool TryDrinkFromWorldSource()
        {
            if (BlockiverseRuntimeState.IsGamePaused || !InSurvivalMode || Vitals.IsDead || Time.time < nextWorldDrinkTime)
                return false;

            nextWorldDrinkTime = Time.time + WorldDrinkCooldownSeconds;
            SurvivalVitals.Drink(WorldDrinkThirstRestore);
            WorldDrinkTaken?.Invoke();
            return true;
        }

        void OnVitalsDied(HealthChangeResult result)
        {
            SubmitDeathDropIfNeeded();
            LocalPlayerDied?.Invoke();
        }

        void OnVitalsHealthChanged(HealthChangeResult result)
        {
            if (result.Kind != HealthChangeKind.Damage || result.AppliedAmount <= 0 || result.DidDie)
                return;

            LocalPlayerDamaged?.Invoke(result);

            int lowHealthThreshold = result.MaxHealth / 4;
            if (result.PreviousHealth > lowHealthThreshold && result.CurrentHealth <= lowHealthThreshold)
                LocalPlayerLowHealth?.Invoke(result);
        }

        // Respawns the local player at the world spawn: restores all vitals and moves the rig.
        public void Respawn()
        {
            BlockPosition spawn = ResolveSpawnPosition();
            RespawnAt(spawn);
        }

        // Respawns over a placed bedroll when one is available; falls back to world spawn if the
        // bedroll was removed after the death screen was shown.
        public void RespawnAtBedroll()
        {
            BlockPosition spawn = TryResolveBedrollSpawnPosition(out BlockPosition bedrollSpawn)
                ? bedrollSpawn
                : ResolveSpawnPosition();
            RespawnAt(spawn);
        }

        void RespawnAt(BlockPosition spawn)
        {
            deathDropSubmitted = false;
            Vitals.RespawnAt(spawn);
            SurvivalVitals.ResetToFull();
            CreativeWorldManager.PositionRigAtSpawn(spawn);
        }

        void SubmitDeathDropIfNeeded()
        {
            if (deathDropSubmitted || !InSurvivalMode || survivalSync == null)
                return;

            deathDropSubmitted = true;
            BlockPosition dropPosition = ResolveCurrentRigBlockPosition();
            SurvivalCommandResult result = survivalSync.TrySubmitDeathDrop(dropPosition, out _);
            if (result.Accepted || result.PendingHostValidation)
            {
                lastDeathDropPosition = dropPosition;
                hasLastDeathDropPosition = true;
            }
        }

        BlockPosition ResolveCurrentRigBlockPosition()
        {
            if (BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return CreativeInteractionController.ToBlockPosition(rig.position);

            if (cachedHeadTransform != null)
                return CreativeInteractionController.ToBlockPosition(cachedHeadTransform.position);

            return ResolveSpawnPosition();
        }

        bool TryResolveBedrollSpawnPosition(out BlockPosition spawnPosition)
        {
            VoxelWorld world = worldManager != null ? worldManager.World : null;
            if (world == null)
            {
                spawnPosition = default;
                return false;
            }

            bedrollPositions.Clear();
            world.CollectBlockPositions(BlockRegistry.Bedroll, bedrollPositions);
            if (bedrollPositions.Count == 0)
            {
                spawnPosition = default;
                return false;
            }

            BlockPosition reference = ResolveSpawnPosition();
            BlockPosition best = default;
            int bestDistance = int.MaxValue;
            bool found = false;
            foreach (BlockPosition bedroll in bedrollPositions)
            {
                var candidate = new BlockPosition(bedroll.X, bedroll.Y + 1, bedroll.Z);
                if (!world.Bounds.Contains(candidate) || world.GetBlock(candidate) != BlockRegistry.Air)
                    continue;

                int dx = bedroll.X - reference.X;
                int dy = bedroll.Y - reference.Y;
                int dz = bedroll.Z - reference.Z;
                int distance = dx * dx + dy * dy + dz * dz;
                if (!found || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                    found = true;
                }
            }

            spawnPosition = best;
            return found;
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
