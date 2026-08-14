using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.VR;
using Blockiverse.WorldGen;
using UnityEngine;
using Unity.Profiling;

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
        [SerializeField] BlockiverseMenuController menuController;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] BlockiverseInputRig inputRig;

        string currentSavePath;
        string currentWorldName;
        string currentDifficulty = string.Empty;
        string currentWorldPreset = WorldPresetIds.SurvivalTerrain;
        string currentTextureSet = BlockTextureSetIds.Default;
        // Save list rows keyed back to their on-disk paths so Load resolves the exact slot even
        // when two saves share a display name (rebuilt by RefreshSaveList).
        readonly Dictionary<string, string> savePathsBySummaryKey = new();
        float lastSaveTime;
        bool worldTransitionInProgress;
        Task autoSaveTask;
        string autoSaveWorldName;

        public string CurrentSavePath => currentSavePath;
        public bool HasActiveSession => !string.IsNullOrEmpty(currentSavePath);

        static string savesRoot;

        // Application.persistentDataPath is fixed for the life of the process — compute once.
        static string SavesRoot => savesRoot ??= Path.Combine(Application.persistentDataPath, "Saves");

        static readonly ProfilerMarker SaveCurrentWorldMarker = new("Blockiverse.WorldSession.SaveCurrentWorld");
        static readonly ProfilerMarker StartAutoSaveMarker = new("Blockiverse.WorldSession.StartAutoSave");
        static readonly ProfilerMarker CompleteAutoSaveMarker = new("Blockiverse.WorldSession.CompleteAutoSave");
        static readonly ProfilerMarker EnterGeneratedWorldMarker = new("Blockiverse.WorldSession.EnterGeneratedWorld");
        static readonly ProfilerMarker LoadSaveMarker = new("Blockiverse.WorldSession.LoadSave");
        static readonly ProfilerMarker ApplyLoadedWorldMarker = new("Blockiverse.WorldSession.ApplyLoadedWorld");

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
                    BlockiverseSceneLookup.Find<BlockiverseMenuController>(FindObjectsInactive.Include);

            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            if (survivalSync == null)
                survivalSync = BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = BlockiverseSceneLookup.Find<MultiplayerChunkAuthoritySync>(FindObjectsInactive.Include);

            if (vitalsRuntime == null)
                vitalsRuntime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);

            if (inputRig == null)
                inputRig = BlockiverseSceneLookup.Find<BlockiverseInputRig>(FindObjectsInactive.Include);
        }

        // Autosave: while a session is active, save on the WorldSaveService cadence (§6.7).
        // Multiplayer-hosted worlds are autosaved by MultiplayerWorldPersistence instead.
        void Update()
        {
            CompleteAutoSaveIfReady();

            if (!ShouldStartAutoSave(HasActiveSession, Time.unscaledTime, lastSaveTime))
                return;

            StartAutoSave();
        }

        public static bool ShouldStartAutoSave(bool hasActiveSession, float currentUnscaledTime, float lastSaveTime) =>
            hasActiveSession && WorldSaveService.ShouldAutoSave(currentUnscaledTime - lastSaveTime);

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveCurrentWorld();
        }

        void OnApplicationQuit()
        {
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
                    ReportManualSaveStatus(SaveCurrentWorld());
                    break;
                case MenuActions.PauseReturnToTitle:
                    SaveCurrentWorld();
                    break;
                case MenuActions.DeathReturnToTitle:
                    SaveCurrentWorld(respawnDeadPlayer: true);
                    break;
                case MenuActions.TitleQuit:
                    SaveCurrentWorld();
                    break;
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────

        public bool TrySuspendActiveSessionForMultiplayer(out string failureReason)
        {
            failureReason = string.Empty;

            if (!HasActiveSession)
                return true;

            if (!SaveCurrentWorld())
            {
                failureReason = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSuspendSinglePlayerFailed);
                return false;
            }

            currentSavePath = null;
            currentWorldName = null;
            currentDifficulty = string.Empty;
            currentWorldPreset = WorldPresetIds.SurvivalTerrain;
            currentTextureSet = BlockTextureSetIds.Default;
            lastSaveTime = 0.0f;
            RefreshSaveList();
            return true;
        }

        public bool SaveCurrentWorld(bool respawnDeadPlayer = false)
        {
            using ProfilerMarker.AutoScope scope = SaveCurrentWorldMarker.Auto();

            ResolveReferences();
            WaitForAutoSave();

            if (worldManager == null || worldManager.World == null || !HasActiveSession)
                return false;

            // Stamp before the attempt so a persistent failure logs once per autosave interval,
            // not once per frame.
            lastSaveTime = Time.unscaledTime;

            try
            {
                WorldSaveService.WorldSaveSnapshot snapshot = CaptureCurrentWorldSaveSnapshot(respawnDeadPlayer);
                new WorldSaveService().Save(snapshot);

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

        void StartAutoSave()
        {
            if (autoSaveTask != null)
                return;

            using ProfilerMarker.AutoScope scope = StartAutoSaveMarker.Auto();

            // Stamp when the snapshot is captured so repeated background failures still wait for
            // the normal autosave cadence instead of retrying every frame.
            lastSaveTime = Time.unscaledTime;

            try
            {
                WorldSaveService.WorldSaveSnapshot snapshot = CaptureCurrentWorldSaveSnapshot();
                autoSaveWorldName = currentWorldName;
                autoSaveTask = Task.Run(() => new WorldSaveService().Save(snapshot));
            }
            catch (Exception exception)
            {
                ReportAutoSaveStatus(success: false);
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to start autosave world session name={currentWorldName} exception={exception.GetType().Name}",
                    context: this);
            }
        }

        void CompleteAutoSaveIfReady()
        {
            if (autoSaveTask == null || !autoSaveTask.IsCompleted)
                return;

            using ProfilerMarker.AutoScope scope = CompleteAutoSaveMarker.Auto();

            try
            {
                autoSaveTask.GetAwaiter().GetResult();
                RefreshSaveList();
                ReportAutoSaveStatus(success: true);
            }
            catch (Exception exception)
            {
                ReportAutoSaveStatus(success: false);
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to autosave world session name={autoSaveWorldName} exception={exception.GetType().Name}",
                    context: this);
            }
            finally
            {
                autoSaveTask = null;
                autoSaveWorldName = null;
            }
        }

        void ReportManualSaveStatus(bool success)
        {
            menuController?.SetPauseStatus(BlockiverseLocalization.Text(success
                ? BlockiverseLocalization.Keys.StatusSaveSucceeded
                : BlockiverseLocalization.Keys.StatusSaveFailed));
        }

        void ReportAutoSaveStatus(bool success)
        {
            menuController?.SetPauseStatus(BlockiverseLocalization.Text(success
                ? BlockiverseLocalization.Keys.StatusAutosaveSucceeded
                : BlockiverseLocalization.Keys.StatusAutosaveFailed));
        }

        void WaitForAutoSave()
        {
            if (autoSaveTask == null)
                return;

            try
            {
                autoSaveTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to finish pending autosave world session name={autoSaveWorldName} exception={exception.GetType().Name}",
                    context: this);
            }
            finally
            {
                autoSaveTask = null;
                autoSaveWorldName = null;
            }
        }

        WorldSaveService.WorldSaveSnapshot CaptureCurrentWorldSaveSnapshot(bool respawnDeadPlayer = false)
        {
            Inventory inventory = survivalSync != null
                ? survivalSync.BuildPersistedInventory()
                : new Inventory(ItemRegistry.Default);
            int selectedHotbarSlot = survivalSync != null ? survivalSync.SelectedHotbarSlotIndex : 0;

            return new WorldSaveService().CaptureSnapshot(
                currentSavePath,
                currentWorldName,
                worldManager.World,
                inventory,
                selectedHotbarSlot,
                weatherState: worldManager.CurrentWeatherState,
                gameMode: worldManager.GameModeString,
                worldTimeTicks: worldManager.WorldTimeClock != null ? worldManager.WorldTimeClock.TotalElapsedTicks : 0L,
                containers: WorldSaveContainerMapper.BuildSavedContainers(worldManager.ContainerStore),
                difficulty: currentDifficulty,
                worldPreset: currentWorldPreset,
                textureSet: currentTextureSet,
                extras: BuildSaveExtras(respawnDeadPlayer));
        }

        // World-sim state beyond blocks/inventory: weather machine, vegetation/farming queues
        // (world manager), timed-station contents (survival sync), and player presence/vitals.
        WorldSaveExtras BuildSaveExtras(bool respawnDeadPlayer = false)
        {
            var extras = new WorldSaveExtras();
            worldManager.FillSaveExtras(extras);

            if (worldManager.Settings != null)
            {
                extras.HasSpawnPosition = true;
                extras.SpawnPosition = worldManager.Settings.SpawnPosition;
            }

            if (survivalSync != null)
            {
                extras.Stations = WorldSaveStateMapper.ToSavedStations(survivalSync.ExportStationStates());
                extras.SharedCrateInventory = survivalSync.BuildPersistedSharedCrateInventory();
            }

            extras.PlayerState = vitalsRuntime != null
                ? respawnDeadPlayer
                    ? vitalsRuntime.BuildRespawnedPlayerSaveState()
                    : vitalsRuntime.BuildPlayerSaveState()
                : null;
            return extras;
        }

        // ── New world ─────────────────────────────────────────────────────────

        void CreateNewWorld()
        {
            ResolveReferences();
            NewWorldConfig config = menuController != null ? menuController.PendingNewWorldConfig : null;

            if (worldTransitionInProgress || worldManager == null || config == null || !config.IsValid(out _))
                return;

            if (Application.isPlaying)
            {
                StartCoroutine(CreateNewWorldRoutine(config));
                return;
            }

            CreateNewWorldSynchronously(config, deferRendererRebuild: false);
        }

        void CreateNewWorldSynchronously(NewWorldConfig config, bool deferRendererRebuild)
        {
            try
            {
                GeneratedCreativeWorld generated = WorldSaveGeneration.GenerateNewWorld(
                    config.WorldPreset,
                    config.Seed,
                    config.WorldSize,
                    config.StartingBiome);

                EnterGeneratedNewWorld(config, generated, deferRendererRebuild);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to create new world name={config.Name} exception={exception.GetType().Name}",
                    context: this);
                menuController?.SetTitleStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusCreateWorldFailed));
            }
        }

        IEnumerator CreateNewWorldRoutine(NewWorldConfig config)
        {
            worldTransitionInProgress = true;
            SetTransitionLocomotionBlocked(true);
            menuController?.ShowWorldLoadingScreen();
            yield return null;

            menuController?.SetTitleStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusCreatingWorld));

            string name = config.Name.Trim();
            string gameMode = config.GameMode;
            string difficulty = config.Difficulty;
            string worldPreset = config.WorldPreset;
            string textureSet = config.TextureSet;
            string startingBiome = config.StartingBiome;
            string worldSize = config.WorldSize;
            ulong seed = config.Seed;
            Task<GeneratedCreativeWorld> generationTask = Task.Run(
                () => WorldSaveGeneration.GenerateNewWorld(worldPreset, seed, worldSize, startingBiome));

            while (!generationTask.IsCompleted)
                yield return null;

            try
            {
                if (generationTask.IsFaulted)
                    throw generationTask.Exception?.GetBaseException() ?? new InvalidOperationException("World generation failed.");

                EnterGeneratedNewWorld(
                    name,
                    gameMode,
                    difficulty,
                    worldPreset,
                    textureSet,
                    generationTask.Result,
                    deferRendererRebuild: false);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to create new world name={name} exception={exception.GetType().Name}",
                    context: this);
                menuController?.ShowTitleScreen();
                menuController?.SetTitleStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusCreateWorldFailed));
            }
            finally
            {
                SetTransitionLocomotionBlocked(false);
                worldTransitionInProgress = false;
            }
        }

        void EnterGeneratedNewWorld(NewWorldConfig config, GeneratedCreativeWorld generated, bool deferRendererRebuild)
        {
            EnterGeneratedNewWorld(
                config.Name.Trim(),
                config.GameMode,
                config.Difficulty,
                config.WorldPreset,
                config.TextureSet,
                generated,
                deferRendererRebuild);
        }

        void EnterGeneratedNewWorld(
            string worldName,
            string gameMode,
            string difficulty,
            string worldPreset,
            string textureSet,
            GeneratedCreativeWorld generated,
            bool deferRendererRebuild)
        {
            using ProfilerMarker.AutoScope scope = EnterGeneratedWorldMarker.Auto();

            currentTextureSet = BlockTextureSetIds.Normalize(textureSet);
            worldManager.SetTextureSet(currentTextureSet);
            worldManager.InitializeGeneratedWorld(
                generated,
                chunkAuthoritySync,
                deferInitialRendererRebuild: deferRendererRebuild);
            worldManager.SetGameMode(CreativeWorldManager.ParseGameMode(gameMode));
            ApplyPlayerMode();

            currentDifficulty = difficulty;
            vitalsRuntime?.ConfigureDifficulty(currentDifficulty);
            currentWorldPreset = worldPreset;
            // The allocated slot may suffix the name ("Camp (2)") — the manifest world name
            // follows it so Load World rows stay unique and selectable.
            (currentSavePath, currentWorldName) = WorldSaveSlotService.AllocateSavePath(SavesRoot, worldName);

            // Write the slot immediately so the world exists on disk (and Continue works)
            // even if the player never opens the pause menu.
            SaveCurrentWorld();
            menuController?.EnterGameplay();
        }

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
                StartLoadSave(latest.Path);
        }

        void LoadSelectedSave()
        {
            WorldSaveSummary? selected = menuController != null ? menuController.PendingLoadSave : null;
            if (!selected.HasValue)
                return;

            if (TryResolveSavePath(selected.Value, out string path))
            {
                StartLoadSave(path);
                return;
            }

            ReportLoadStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSaveNotFound), isFailure: true);
        }

        // Seed + created timestamp disambiguate slots whose display names collide; built from the
        // summary on both sides so lookups see exactly what RefreshSaveList stored.
        static string SummaryKey(WorldSaveSummary summary) =>
            $"{summary.Name}\n{summary.Seed}\n{summary.CreatedUtc.Ticks}";

        bool TryResolveSavePath(WorldSaveSummary summary, out string path)
        {
            // Prefer the path cached by RefreshSaveList — it pins the exact slot even when two
            // saves share a display name. The name scan below covers a stale or missing mapping.
            if (savePathsBySummaryKey.TryGetValue(SummaryKey(summary), out string mappedPath) &&
                Directory.Exists(mappedPath))
            {
                path = mappedPath;
                return true;
            }

            return TryResolveSavePathByName(summary.Name, out path);
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

            if (TryResolveSavePath(save.Value, out string path))
                StartLoadSave(path);
            else
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSaveNotFound));
        }

        void RenameDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            string newName = menuController?.PendingDetailsRenameText?.Trim();
            if (!save.HasValue || string.IsNullOrEmpty(newName))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusEnterWorldNameFirst));
                return;
            }

            if (string.Equals(newName, save.Value.Name, StringComparison.Ordinal))
                return; // unchanged

            if (!TryResolveSavePath(save.Value, out string path))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSaveNotFound));
                return;
            }

            (string destinationPath, string uniqueName) = WorldSaveSlotService.AllocateSavePath(SavesRoot, newName);
            if (!WorldSaveService.TryRenameSave(path, destinationPath, uniqueName))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusRenameFailed));
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

            if (!TryResolveSavePath(save.Value, out string path))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSaveNotFound));
                return;
            }

            (string destinationPath, string uniqueName) = WorldSaveSlotService.AllocateSavePath(SavesRoot, save.Value.Name + " Copy");
            if (!WorldSaveService.TryDuplicateSave(path, destinationPath, uniqueName))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusDuplicateFailed));
                return;
            }

            RefreshSaveList();
        }

        void DeleteDetailsSave()
        {
            WorldSaveSummary? save = menuController?.PendingDetailsSave;
            if (!save.HasValue)
                return;

            if (!TryResolveSavePath(save.Value, out string path))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusSaveNotFound));
                return;
            }

            if (!WorldSaveService.TryDeleteSave(path))
            {
                menuController?.ShowError(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusDeleteFailed));
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

        void StartLoadSave(string path)
        {
            if (worldTransitionInProgress)
                return;

            if (Application.isPlaying)
                StartCoroutine(LoadSaveRoutine(path));
            else
                LoadSave(path);
        }

        IEnumerator LoadSaveRoutine(string path)
        {
            worldTransitionInProgress = true;
            SetTransitionLocomotionBlocked(true);
            ReportLoadStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusLoadingWorld), isFailure: false);

            WorldLoadResult result = new WorldSaveService().Load(path);
            if (!result.Success)
            {
                RefreshSaveList();
                ReportLoadStatus(
                    BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StatusLoadFailed, result.Error),
                    isFailure: true);
                SetTransitionLocomotionBlocked(false);
                worldTransitionInProgress = false;
                yield break;
            }

            menuController?.ShowWorldLoadingScreen();
            yield return null;

            Task<GeneratedCreativeWorld> regenerationTask = Task.Run(() => WorldSaveGeneration.Regenerate(result.Data));
            while (!regenerationTask.IsCompleted)
                yield return null;

            try
            {
                if (regenerationTask.IsFaulted)
                    throw regenerationTask.Exception?.GetBaseException() ?? new InvalidOperationException("World regeneration failed.");

                ApplyLoadedWorld(path, result, regenerationTask.Result, deferRendererRebuild: false);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to enter loaded world file={Path.GetFileName(path)} exception={exception.GetType().Name}",
                    context: this);
                menuController?.ShowTitleScreen();
                ReportLoadStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusLoadWorldFailed), isFailure: true);
            }
            finally
            {
                SetTransitionLocomotionBlocked(false);
                worldTransitionInProgress = false;
            }
        }

        void SetTransitionLocomotionBlocked(bool blocked)
        {
            ResolveReferences();
            if (inputRig != null)
                inputRig.LocomotionSuppressed = blocked;
        }

        public bool LoadSave(string path)
        {
            using ProfilerMarker.AutoScope scope = LoadSaveMarker.Auto();

            ResolveReferences();

            if (worldManager == null)
                return false;

            WorldLoadResult result = new WorldSaveService().Load(path);
            if (!result.Success)
            {
                RefreshSaveList();
                ReportLoadStatus(
                    BlockiverseLocalization.Format(BlockiverseLocalization.Keys.StatusLoadFailed, result.Error),
                    isFailure: true);
                return false;
            }

            try
            {
                return ApplyLoadedWorld(
                    path,
                    result,
                    RegenerateBaseWorld(result.Data),
                    deferRendererRebuild: false);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to enter loaded world file={Path.GetFileName(path)} exception={exception.GetType().Name}",
                    context: this);
                ReportLoadStatus(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.StatusLoadWorldFailed), isFailure: true);
                return false;
            }
        }

        void ReportLoadStatus(string message, bool isFailure)
        {
            if (menuController == null)
                return;

            if (menuController.IsActiveScreen(MenuActions.LoadWorldScreen))
            {
                menuController.SetLoadWorldStatus(message);
                return;
            }

            if (isFailure && menuController.IsActiveScreen(MenuActions.WorldDetailsScreen))
            {
                menuController.ShowError(message);
                return;
            }

            menuController.SetTitleStatus(message);
        }

        bool ApplyLoadedWorld(
            string path,
            WorldLoadResult result,
            GeneratedCreativeWorld generated,
            bool deferRendererRebuild)
        {
            using ProfilerMarker.AutoScope scope = ApplyLoadedWorldMarker.Auto();

            WorldSaveData data = result.Data;
            currentTextureSet = BlockTextureSetIds.Normalize(data.TextureSet);
            worldManager.SetTextureSet(currentTextureSet);
            worldManager.InitializeGeneratedWorld(
                generated,
                chunkAuthoritySync,
                deferInitialRendererRebuild: deferRendererRebuild);

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

            if (!deferRendererRebuild)
                worldManager.Renderer?.RebuildAll();
            RestoreInventory(result, data);
            ApplyPlayerMode();
            RestorePlayer(data.PlayerState);

            currentSavePath = path;
            currentWorldName = data.WorldName;
            currentDifficulty = data.Difficulty ?? string.Empty;
            vitalsRuntime?.ConfigureDifficulty(currentDifficulty);
            currentWorldPreset = data.WorldPreset;
            currentTextureSet = BlockTextureSetIds.Normalize(data.TextureSet);
            lastSaveTime = Time.unscaledTime;
            menuController?.EnterGameplay();
            return true;
        }

        GeneratedCreativeWorld RegenerateBaseWorld(WorldSaveData data)
        {
            return WorldSaveGeneration.Regenerate(data);
        }

        void RestoreContainers(SavedContainer[] saved)
        {
            worldManager.RestoreContainerStore(WorldSaveContainerMapper.BuildRestoredContainers(saved));
        }

        void RestoreInventory(WorldLoadResult result, WorldSaveData data)
        {
            if (survivalSync == null)
                return;

            Inventory loaded = result.CreateInventory();
            int selectedHotbarSlot = data.PlayerInventory != null ? data.PlayerInventory.SelectedHotbarSlotIndex : 0;
            survivalSync.RestoreLocalInventory(loaded, selectedHotbarSlot);
            survivalSync.RestoreSharedCrateInventory(data.SharedCrateInventory);
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
            savePathsBySummaryKey.Clear();

            foreach (WorldSaveService.WorldSaveInfo info in saves)
            {
                var summary = new WorldSaveSummary(
                    info.Manifest.WorldName,
                    info.Manifest.Seed.ToString(),
                    info.Manifest.GameMode,
                    info.Manifest.Difficulty,
                    dayCount: (int)(info.WorldTimeTicks / WorldConstants.TicksPerDay) + 1,
                    lastPlayedUtc: ParseUtc(info.Manifest.ModifiedAtUtc),
                    createdUtc: ParseUtc(info.Manifest.CreatedAtUtc));
                summaries.Add(summary);
                savePathsBySummaryKey[SummaryKey(summary)] = info.Path;
            }

            menuController?.SetSaveList(summaries);
            // Continue and Load World share one gate: Continue opens the most recent save, so
            // "latest exists" and "any exists" are the same predicate today.
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

    }
}
