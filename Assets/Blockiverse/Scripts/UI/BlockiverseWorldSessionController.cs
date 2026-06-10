using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.UI
{
    // Bridges the menu system to the world runtime: subscribes to BlockiverseMenuController's
    // action ids and implements the single-player session verbs — create a world from the
    // New World config, save/load directory-schema saves under persistentDataPath/Saves,
    // continue the most recent save, and keep the title menu's save-dependent entries and the
    // Load World list current (voxel_survival_menus §6.2–§6.4, §6.7).
    [DisallowMultipleComponent]
    public sealed class BlockiverseWorldSessionController : MonoBehaviour
    {
        const string SaveDirectoryExtension = ".vxlworld";

        [SerializeField] BlockiverseMenuController menuController;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;

        string currentSavePath;
        string currentWorldName;
        string currentDifficulty = string.Empty;
        string currentWorldPreset = "survival_terrain";
        float lastSaveTime;

        public string CurrentSavePath => currentSavePath;
        public bool HasActiveSession => !string.IsNullOrEmpty(currentSavePath);

        static string SavesRoot => Path.Combine(Application.persistentDataPath, "Saves");

        public void Configure(
            BlockiverseMenuController controller,
            CreativeWorldManager manager,
            MultiplayerSurvivalSync sync = null,
            MultiplayerChunkAuthoritySync authoritySync = null)
        {
            Unwire();
            menuController = controller;
            worldManager = manager;
            survivalSync = sync;
            chunkAuthoritySync = authoritySync;
            Wire();
        }

        void Start()
        {
            ResolveReferences();
            Wire();
            RefreshSaveList();
        }

        void OnDestroy()
        {
            Unwire();
        }

        void ResolveReferences()
        {
            if (menuController == null)
                menuController = GetComponent<BlockiverseMenuController>() ??
                    FindFirstObjectByType<BlockiverseMenuController>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (survivalSync == null)
                survivalSync = FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = FindFirstObjectByType<MultiplayerChunkAuthoritySync>(FindObjectsInactive.Include);

            if (vitalsRuntime == null)
                vitalsRuntime = FindFirstObjectByType<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
        }

        // Autosave: while a session is active, save on the WorldSaveService cadence (§6.7).
        // Multiplayer-hosted worlds are autosaved by MultiplayerWorldPersistence instead.
        void Update()
        {
            if (!HasActiveSession)
                return;

            if (Time.unscaledTime - lastSaveTime < WorldSaveService.AutoSaveIntervalSeconds)
                return;

            SaveCurrentWorld();
        }

        void Wire()
        {
            if (menuController == null)
                return;

            menuController.ActionRequested -= HandleAction;
            menuController.ActionRequested += HandleAction;
        }

        void Unwire()
        {
            if (menuController != null)
                menuController.ActionRequested -= HandleAction;
        }

        void HandleAction(string actionId)
        {
            switch (actionId)
            {
                case MenuActions.TitleContinue:
                    ContinueLatestSave();
                    break;
                case MenuActions.TitleLoadWorld:
                    RefreshSaveList();
                    break;
                case MenuActions.LoadWorldLoad:
                    LoadSelectedSave();
                    break;
                case MenuActions.WorldDetailsPlay:
                    PlayDetailsSave();
                    break;
                case MenuActions.WorldDetailsRename:
                    RenameDetailsSave();
                    break;
                case MenuActions.WorldDetailsDuplicate:
                    DuplicateDetailsSave();
                    break;
                case MenuActions.WorldDetailsDeleteRequested:
                    // The menu controller already ran the delete confirmation modal.
                    DeleteDetailsSave();
                    break;
                case MenuActions.NewWorldCreate:
                    CreateNewWorld();
                    break;
                case MenuActions.PauseSaveGame:
                case MenuActions.PauseReturnToTitle:
                case MenuActions.DeathReturnToTitle:
                case MenuActions.TitleQuit:
                    SaveCurrentWorld();
                    break;
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────

        public bool SaveCurrentWorld()
        {
            ResolveReferences();

            if (worldManager == null || worldManager.World == null || !HasActiveSession)
                return false;

            // Stamp before the attempt so a persistent failure logs once per autosave interval,
            // not once per frame.
            lastSaveTime = Time.unscaledTime;

            try
            {
                Inventory inventory = survivalSync != null
                    ? survivalSync.BuildPersistedInventory()
                    : new Inventory(ItemRegistry.CreateDefault());
                int selectedHotbarSlot = survivalSync != null ? survivalSync.SelectedHotbarSlotIndex : 0;

                new WorldSaveService().Save(
                    currentSavePath,
                    currentWorldName,
                    worldManager.World,
                    inventory,
                    selectedHotbarSlot,
                    weatherState: worldManager.CurrentWeatherState,
                    gameMode: worldManager.GameModeString,
                    worldTimeTicks: worldManager.WorldTimeClock != null ? worldManager.WorldTimeClock.TotalElapsedTicks : 0L,
                    containers: BuildSavedContainers(worldManager.ContainerStore),
                    difficulty: currentDifficulty,
                    worldPreset: currentWorldPreset,
                    extras: BuildSaveExtras());

                RefreshSaveList();
                return true;
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to save world session name={currentWorldName} exception={exception.GetType().Name}",
                    context: this);
                return false;
            }
        }

        // World-sim state beyond blocks/inventory: weather machine, vegetation/farming queues
        // (world manager), timed-station contents (survival sync), and player presence/vitals.
        WorldSaveExtras BuildSaveExtras()
        {
            var extras = new WorldSaveExtras();
            worldManager.FillSaveExtras(extras);

            if (survivalSync != null)
                extras.Stations = WorldSaveStateMapper.ToSavedStations(survivalSync.ExportStationStates());

            extras.PlayerState = vitalsRuntime != null ? vitalsRuntime.BuildPlayerSaveState() : null;
            return extras;
        }

        static IReadOnlyList<SavedContainer> BuildSavedContainers(ContainerInventoryStore store)
        {
            if (store == null || store.Count == 0)
                return null;

            var result = new List<SavedContainer>(store.Count);
            foreach (BlockPosition position in store.Positions)
            {
                if (!store.TryGet(position, out Inventory inventory) || inventory == null)
                    continue;

                var slots = new List<SavedContainerSlot>();
                for (int index = 0; index < inventory.SlotCount; index++)
                {
                    ItemStack stack = inventory.GetSlot(index);
                    if (stack.IsEmpty)
                        continue;

                    slots.Add(new SavedContainerSlot { CanonicalId = stack.ItemId.Value, Count = stack.Count });
                }

                // Persist the position even when empty so an emptied crate stays empty on reload.
                result.Add(new SavedContainer { X = position.X, Y = position.Y, Z = position.Z, Slots = slots.ToArray() });
            }

            return result.Count > 0 ? result : null;
        }

        // ── New world ─────────────────────────────────────────────────────────

        void CreateNewWorld()
        {
            ResolveReferences();
            NewWorldConfig config = menuController != null ? menuController.PendingNewWorldConfig : null;

            if (worldManager == null || config == null || !config.IsValid(out _))
                return;

            try
            {
                int seed = FoldSeed(config.Seed);
                (int width, int depth) = SizeFor(config.WorldSize);
                GeneratedCreativeWorld generated = GenerateWorld(config.WorldPreset, seed, width, depth, config.StartingBiome);

                worldManager.InitializeGeneratedWorld(generated, chunkAuthoritySync);
                worldManager.SetGameMode(CreativeWorldManager.ParseGameMode(config.GameMode));
                ApplyPlayerMode();

                currentDifficulty = config.Difficulty;
                currentWorldPreset = config.WorldPreset;
                // The allocated slot may suffix the name ("Camp (2)") — the manifest world name
                // follows it so Load World rows stay unique and selectable.
                (currentSavePath, currentWorldName) = AllocateSavePath(config.Name.Trim());

                // Write the slot immediately so the world exists on disk (and Continue works)
                // even if the player never opens the pause menu.
                SaveCurrentWorld();
                menuController?.EnterGameplay();
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to create new world name={config.Name} exception={exception.GetType().Name}",
                    context: this);
                menuController?.SetTitleStatus("Failed to create the world.");
            }
        }

        // Builder presets are mid-height worlds (§11.2/§11.3): the flat surface sits low for
        // stacking room; the void platform sits centered for build space above and below it.
        const int BuilderWorldHeight = 64;
        const int FlatBuilderGroundHeight = 8;
        const int VoidBuilderGroundHeight = 32;

        GeneratedCreativeWorld GenerateWorld(string worldPreset, int seed, int width, int depth, string startingBiome)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            if (string.Equals(worldPreset, "flat_builder", StringComparison.OrdinalIgnoreCase))
            {
                var flatSettings = new WorldGenerationSettings(width, BuilderWorldHeight, depth, WorldConstants.ChunkSize, seed, FlatBuilderGroundHeight);
                VoxelWorld flatWorld = new FlatBuilderPreset(registry, flatSettings).Generate();
                return new GeneratedCreativeWorld(registry, flatSettings, flatWorld, CreativeWorldGenerationPreset.FlatCreative);
            }

            if (string.Equals(worldPreset, "void_builder", StringComparison.OrdinalIgnoreCase))
            {
                var voidSettings = new WorldGenerationSettings(width, BuilderWorldHeight, depth, WorldConstants.ChunkSize, seed, VoidBuilderGroundHeight);
                VoxelWorld voidWorld = new VoidBuilderPreset(registry, voidSettings).Generate();
                return new GeneratedCreativeWorld(registry, voidSettings, voidWorld, CreativeWorldGenerationPreset.VoidBuilder);
            }

            BlockPosition? spawn = FindSpawnForBiome(seed, width, 256, depth, WorldConstants.SeaLevel, startingBiome);
            var settings = new WorldGenerationSettings(width, 256, depth, WorldConstants.ChunkSize, seed, WorldConstants.SeaLevel, spawn);
            var preset = new SurvivalTerrainPreset(registry, settings);
            VoxelWorld world = preset.Generate();
            return new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.SurvivalLite, preset.ContainerLoot);
        }

        // Maps the menu's world-size selector to bounded dimensions (§6.3; "infinite" is not a
        // bounded-world option and falls back to large).
        static (int width, int depth) SizeFor(string worldSize)
        {
            switch (worldSize)
            {
                case "medium": return (192, 192);
                case "large":
                case "infinite": return (256, 256);
                default: return (128, 128);
            }
        }

        // Searches outward from the world center for a dry-land column of the requested starting
        // biome ("balanced" accepts any biome). Columns below sea level flood with fluid (§5.4),
        // so the spawn prefers terrain at or above it rather than terraforming an island into a
        // lake. Pure seed math via SurvivalBiomeResolver, so the search agrees with what the
        // generator will build.
        static BlockPosition? FindSpawnForBiome(int seed, int width, int height, int depth, int groundHeight, string startingBiome)
        {
            int target = BiomeIndexFor(startingBiome); // -1 = any biome (balanced)

            var resolver = new SurvivalBiomeResolver(seed, height);
            int centerX = width / 2;
            int centerZ = depth / 2;
            const int edgeMargin = 8;
            const int step = 4;

            bool Matches(int x, int z) =>
                (target < 0 || resolver.BiomeIndexAt(x, z) == target) &&
                resolver.SurfaceHeight(x, z) >= groundHeight;

            if (Matches(centerX, centerZ))
                return null; // center already matches — keep the default spawn

            int maxRadius = Math.Max(width, depth);
            for (int radius = step; radius < maxRadius; radius += step)
            {
                for (int dx = -radius; dx <= radius; dx += step)
                {
                    for (int dz = -radius; dz <= radius; dz += step)
                    {
                        // Ring only — interior radii were already covered.
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                            continue;

                        int x = centerX + dx;
                        int z = centerZ + dz;
                        if (x < edgeMargin || x >= width - edgeMargin || z < edgeMargin || z >= depth - edgeMargin)
                            continue;

                        if (Matches(x, z))
                            return new BlockPosition(x, groundHeight + 1, z);
                    }
                }
            }

            return null; // no dry match in this seed — fall back to the center spawn
        }

        // TerrainBiome is internal to WorldGen; the resolver exposes biome indexes, whose order
        // is canonical: Meadow, Pinewild, Wetland, Drybrush, Dunes, Tundra, Highlands.
        static int BiomeIndexFor(string startingBiome)
        {
            switch (startingBiome)
            {
                case "meadow": return 0;
                case "pinewild": return 1;
                case "wetland": return 2;
                case "drybrush": return 3;
                case "dunes": return 4;
                case "tundra": return 5;
                case "highlands": return 6;
                default: return -1; // "balanced"
            }
        }

        // Folds the 64-bit menu seed into the generator's int seed deterministically.
        static int FoldSeed(ulong seed) => unchecked((int)(seed ^ (seed >> 32)));

        // ── Load / continue ───────────────────────────────────────────────────

        void ContinueLatestSave()
        {
            WorldSaveService.WorldSaveInfo latest = null;
            foreach (WorldSaveService.WorldSaveInfo info in WorldSaveService.EnumerateSaves(SavesRoot))
            {
                if (latest == null ||
                    ParseUtc(info.Manifest.ModifiedAtUtc) > ParseUtc(latest.Manifest.ModifiedAtUtc))
                {
                    latest = info;
                }
            }

            if (latest != null)
                LoadSave(latest.Path);
        }

        void LoadSelectedSave()
        {
            WorldSaveSummary? selected = menuController != null ? menuController.PendingLoadSave : null;
            if (!selected.HasValue)
                return;

            if (TryResolveSavePathByName(selected.Value.Name, out string path))
            {
                LoadSave(path);
                return;
            }

            menuController?.SetTitleStatus("Save not found.");
        }

        bool TryResolveSavePathByName(string worldName, out string path)
        {
            foreach (WorldSaveService.WorldSaveInfo info in WorldSaveService.EnumerateSaves(SavesRoot))
            {
                if (string.Equals(info.Manifest.WorldName, worldName, StringComparison.OrdinalIgnoreCase))
                {
                    path = info.Path;
                    return true;
                }
            }

            path = null;
            return false;
        }

        // ── World Details management (§6.5): play/rename/duplicate/delete ─────

        void PlayDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            if (!save.HasValue)
                return;

            if (TryResolveSavePathByName(save.Value.Name, out string path))
                LoadSave(path);
            else
                menuController?.ShowError("Save not found.");
        }

        void RenameDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            string newName = menuController?.PendingDetailsRenameText?.Trim();
            if (!save.HasValue || string.IsNullOrEmpty(newName))
            {
                menuController?.ShowError("Enter a world name first.");
                return;
            }

            if (string.Equals(newName, save.Value.Name, StringComparison.Ordinal))
                return; // unchanged

            if (!TryResolveSavePathByName(save.Value.Name, out string path))
            {
                menuController?.ShowError("Save not found.");
                return;
            }

            (string destinationPath, string uniqueName) = AllocateSavePath(newName);
            if (!WorldSaveService.TryRenameSave(path, destinationPath, uniqueName))
            {
                menuController?.ShowError("Failed to rename the world.");
                return;
            }

            // The renamed save may be the active session — keep saving to the new location.
            if (string.Equals(currentSavePath, path, StringComparison.Ordinal))
            {
                currentSavePath = destinationPath;
                currentWorldName = uniqueName;
            }

            RefreshSaveList();
            RefreshDetailsPanel(uniqueName);
        }

        void DuplicateDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            if (!save.HasValue)
                return;

            if (!TryResolveSavePathByName(save.Value.Name, out string path))
            {
                menuController?.ShowError("Save not found.");
                return;
            }

            (string destinationPath, string uniqueName) = AllocateSavePath(save.Value.Name + " Copy");
            if (!WorldSaveService.TryDuplicateSave(path, destinationPath, uniqueName))
            {
                menuController?.ShowError("Failed to duplicate the world.");
                return;
            }

            RefreshSaveList();
        }

        void DeleteDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            if (!save.HasValue)
                return;

            if (!TryResolveSavePathByName(save.Value.Name, out string path))
            {
                menuController?.ShowError("Save not found.");
                return;
            }

            if (!WorldSaveService.TryDeleteSave(path))
            {
                menuController?.ShowError("Failed to delete the world.");
                return;
            }

            // Deleting the active session's save ends its autosave target.
            if (string.Equals(currentSavePath, path, StringComparison.Ordinal))
            {
                currentSavePath = null;
                currentWorldName = null;
            }

            RefreshSaveList();
            menuController?.CloseWorldDetails();
        }

        // Re-points the details screen at the (renamed) save's fresh summary.
        void RefreshDetailsPanel(string worldName)
        {
            foreach (WorldSaveService.WorldSaveInfo info in WorldSaveService.EnumerateSaves(SavesRoot))
            {
                if (!string.Equals(info.Manifest.WorldName, worldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                menuController?.ShowWorldDetails(new WorldSaveSummary(
                    info.Manifest.WorldName,
                    info.Manifest.Seed.ToString(),
                    info.Manifest.GameMode,
                    info.Manifest.Difficulty,
                    dayCount: (int)(info.WorldTimeTicks / WorldConstants.TicksPerDay) + 1,
                    lastPlayedUtc: ParseUtc(info.Manifest.ModifiedAtUtc),
                    createdUtc: ParseUtc(info.Manifest.CreatedAtUtc)));
                return;
            }
        }

        public bool LoadSave(string path)
        {
            ResolveReferences();

            if (worldManager == null)
                return false;

            WorldLoadResult result = new WorldSaveService().Load(path);
            if (!result.Success)
            {
                menuController?.SetTitleStatus($"Failed to load: {result.Error}");
                RefreshSaveList();
                return false;
            }

            try
            {
                WorldSaveData data = result.Data;
                GeneratedCreativeWorld generated = RegenerateBaseWorld(data);
                worldManager.InitializeGeneratedWorld(generated, chunkAuthoritySync);

                // Saved state is authoritative over the regenerated baseline: block deltas,
                // weather + simulation queues, world time, game mode, container contents,
                // station contents, then the inventory.
                worldManager.SuppressContainerAutoLoot = true;
                try
                {
                    result.ApplyTo(worldManager.World, preserveLoadedBlockChanges: true);
                    worldManager.RestoreSimulationState(data);
                    worldManager.RestoreWorldTimeTicks(data.WorldTimeTicks);
                    worldManager.SetGameMode(CreativeWorldManager.ParseGameMode(data.GameMode));
                    RestoreContainers(data.Containers);
                    RestoreStations(data.Stations);
                }
                finally
                {
                    worldManager.SuppressContainerAutoLoot = false;
                }

                worldManager.Renderer?.RebuildAll();
                RestoreInventory(result, data);
                ApplyPlayerMode();
                RestorePlayer(data.PlayerState);

                currentSavePath = path;
                currentWorldName = data.WorldName;
                currentDifficulty = data.Difficulty ?? string.Empty;
                currentWorldPreset = data.WorldPreset;
                lastSaveTime = Time.unscaledTime;
                menuController?.EnterGameplay();
                return true;
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to enter loaded world file={Path.GetFileName(path)} exception={exception.GetType().Name}",
                    context: this);
                menuController?.SetTitleStatus("Failed to load the world.");
                return false;
            }
        }

        GeneratedCreativeWorld RegenerateBaseWorld(WorldSaveData data)
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            if (string.Equals(data.WorldPreset, "flat_builder", StringComparison.OrdinalIgnoreCase))
            {
                var flatSettings = new WorldGenerationSettings(
                    data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed,
                    groundHeight: Math.Min(FlatBuilderGroundHeight, data.Height - 2));
                VoxelWorld flatWorld = new FlatBuilderPreset(registry, flatSettings).Generate();
                return new GeneratedCreativeWorld(registry, flatSettings, flatWorld, CreativeWorldGenerationPreset.FlatCreative);
            }

            if (string.Equals(data.WorldPreset, "void_builder", StringComparison.OrdinalIgnoreCase))
            {
                var voidSettings = new WorldGenerationSettings(
                    data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed,
                    groundHeight: Math.Min(VoidBuilderGroundHeight, data.Height - 2));
                VoxelWorld voidWorld = new VoidBuilderPreset(registry, voidSettings).Generate();
                return new GeneratedCreativeWorld(registry, voidSettings, voidWorld, CreativeWorldGenerationPreset.VoidBuilder);
            }

            var settings = new WorldGenerationSettings(
                data.Width, data.Height, data.Depth, data.ChunkSize, data.Seed, WorldConstants.SeaLevel);
            var preset = new SurvivalTerrainPreset(registry, settings);
            VoxelWorld world = preset.Generate();
            return new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.SurvivalLite, preset.ContainerLoot);
        }

        void RestoreContainers(SavedContainer[] saved)
        {
            if (saved == null || saved.Length == 0)
                return;

            var restored = new List<(BlockPosition, IEnumerable<(string, int)>)>(saved.Length);
            foreach (SavedContainer container in saved)
            {
                SavedContainerSlot[] slots = container.Slots ?? Array.Empty<SavedContainerSlot>();
                var items = new List<(string, int)>(slots.Length);
                foreach (SavedContainerSlot slot in slots)
                    items.Add((slot.CanonicalId, slot.Count));
                restored.Add((new BlockPosition(container.X, container.Y, container.Z), items));
            }

            worldManager.RestoreContainerStore(restored);
        }

        void RestoreInventory(WorldLoadResult result, WorldSaveData data)
        {
            if (survivalSync == null)
                return;

            Inventory loaded = result.CreateInventory();
            int selectedHotbarSlot = data.PlayerInventory != null ? data.PlayerInventory.SelectedHotbarSlotIndex : 0;
            survivalSync.RestoreLocalInventory(loaded, selectedHotbarSlot);
        }

        // Rebuilds timed-station contents (kiln/forge slots and in-flight crafts) from the save.
        // Runs even when the save carries none: RestoreStationStates is what clears the previous
        // world's station models, so skipping it would leak a prior session's kiln/forge contents
        // into this world (and into its next autosave).
        void RestoreStations(VxlwStation[] saved)
        {
            if (survivalSync == null)
                return;

            survivalSync.RestoreStationStates(WorldSaveStateMapper.FromSavedStations(saved));
        }

        // Places the rig at the saved position/heading and restores vitals; without saved player
        // state the player starts at the world spawn with full vitals (the reset keeps a previous
        // session's hunger/damage from leaking into the loaded world).
        void RestorePlayer(SavedPlayerState state)
        {
            if (state == null || vitalsRuntime == null)
            {
                CreativeWorldManager.PositionRigAtSpawn(ResolveSpawnPosition());
                vitalsRuntime?.ResetVitalsToFull();
                return;
            }

            vitalsRuntime.RestorePlayerSaveState(state);
        }

        // Aligns the player's interaction mode with the world's rules mode (creative worlds
        // stash the freshly restored survival inventory, exactly like a manual switch).
        void ApplyPlayerMode()
        {
            if (survivalSync == null || worldManager == null)
                return;

            survivalSync.SetMode(worldManager.GameMode == WorldGameMode.Creative
                ? PlayerModeState.Creative
                : PlayerModeState.Survival);
        }

        BlockPosition ResolveSpawnPosition()
        {
            if (worldManager.Settings != null)
                return worldManager.Settings.SpawnPosition;

            VoxelWorld world = worldManager.World;
            int x = world.Bounds.Width / 2;
            int z = world.Bounds.Depth / 2;
            int surfaceY = StructureService.FindSurfaceY(world, x, z);
            return new BlockPosition(x, surfaceY >= 0 ? surfaceY + 1 : 1, z);
        }

        // ── Save list / title state ───────────────────────────────────────────

        public void RefreshSaveList()
        {
            ResolveReferences();

            IReadOnlyList<WorldSaveService.WorldSaveInfo> saves = WorldSaveService.EnumerateSaves(SavesRoot);
            var summaries = new List<WorldSaveSummary>(saves.Count);

            foreach (WorldSaveService.WorldSaveInfo info in saves)
            {
                summaries.Add(new WorldSaveSummary(
                    info.Manifest.WorldName,
                    info.Manifest.Seed.ToString(),
                    info.Manifest.GameMode,
                    info.Manifest.Difficulty,
                    dayCount: (int)(info.WorldTimeTicks / WorldConstants.TicksPerDay) + 1,
                    lastPlayedUtc: ParseUtc(info.Manifest.ModifiedAtUtc),
                    createdUtc: ParseUtc(info.Manifest.CreatedAtUtc)));
            }

            menuController?.SetSaveList(summaries);
            menuController?.SetSaveAvailability(summaries.Count > 0, summaries.Count > 0);
        }

        static DateTime ParseUtc(string isoTimestamp)
        {
            return DateTime.TryParse(
                isoTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
        }

        // Allocates a unique save directory for a world name (existing names get " (2)", " (3)"…)
        // and returns both the path and the uniquified display name.
        static (string path, string worldName) AllocateSavePath(string worldName)
        {
            Directory.CreateDirectory(SavesRoot);
            string baseName = SanitizeFileName(worldName);

            for (int suffix = 1; ; suffix++)
            {
                string candidateName = suffix == 1 ? baseName : $"{baseName} ({suffix})";
                string candidate = Path.Combine(SavesRoot, candidateName + SaveDirectoryExtension);
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return (candidate, candidateName);
            }
        }

        static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char character in name)
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);

            string sanitized = builder.ToString().Trim();
            return string.IsNullOrEmpty(sanitized) ? "world" : sanitized;
        }
    }
}
