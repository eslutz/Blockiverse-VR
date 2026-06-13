using System;
using System.IO;
using System.Threading.Tasks;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class MultiplayerWorldPersistence : MonoBehaviour
    {
        const string DefaultSaveFileName = "multiplayer-world.vxlworld";
        const string DefaultWorldName = "Multiplayer World";
        const string DefaultWorldPreset = WorldPresetIds.SurvivalTerrain;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] SurvivalVitalsRuntime vitalsRuntime;
        [SerializeField] string saveFileName = DefaultSaveFileName;
        [SerializeField] string worldName = DefaultWorldName;

        string configuredSavePath;
        bool subscribed;
        float lastAutoSaveTime;
        Task autoSaveTask;
        string autoSavePath;

        // Mirrors the single-player defaults in BlockiverseWorldSessionController so fresh
        // multiplayer worlds save the same difficulty/preset metadata.
        string worldDifficulty = string.Empty;
        string worldPreset = DefaultWorldPreset;

        public bool LastHostLoadAttempted { get; private set; }
        public bool LastHostLoadSucceeded { get; private set; }
        public bool LastShutdownSaveAttempted { get; private set; }
        public bool LastShutdownSaveSucceeded { get; private set; }
        public bool LastApplicationPauseSaveAttempted { get; private set; }
        public bool LastApplicationPauseSaveSucceeded { get; private set; }
        public string LastFailureReason { get; private set; } = string.Empty;
        public string SavePath => ResolveSavePath();

        public void Configure(
            BlockiverseNetworkSession targetSession,
            CreativeWorldManager targetWorldManager,
            string targetSavePath = null,
            string targetWorldName = null)
        {
            Unsubscribe();
            session = targetSession;
            worldManager = targetWorldManager;

            if (string.IsNullOrWhiteSpace(targetSavePath))
            {
                configuredSavePath = null;
            }
            else if (TryResolveTrustedSavePath(targetSavePath, out string trustedSavePath))
            {
                configuredSavePath = trustedSavePath;
            }
            else
            {
                // Keep the previous/default path so a bad configuration cannot redirect saves.
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Rejected untrusted multiplayer save path file={SanitizeSavePath(targetSavePath)} reason=path-outside-trusted-roots",
                    context: this);
            }

            if (!string.IsNullOrWhiteSpace(targetWorldName))
                worldName = targetWorldName;

            Subscribe();
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveWorldForApplicationPause();
        }

        void OnApplicationQuit()
        {
            SaveWorldForApplicationPause();
        }

        bool RestoreSavedWorldBeforeHostStart(out string failureReason)
        {
            failureReason = string.Empty;
            LastHostLoadAttempted = false;
            LastHostLoadSucceeded = false;
            LastFailureReason = string.Empty;

            string path = ResolveSavePath();

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return InitializeFreshMultiplayerWorldBeforeHostStart(path, out failureReason);
            }

            LastHostLoadAttempted = true;

            if (!TryEnsureHostSaveAuthority(out failureReason, "load saved multiplayer world before hosting"))
            {
                LastFailureReason = failureReason;
                return false;
            }

            if (!TryResolveWorldManager(out failureReason, "load saved multiplayer world before hosting"))
            {
                LastFailureReason = failureReason;
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to load multiplayer host world before start file={SanitizeSavePath(path)} reason=world-unavailable",
                    context: this);
                return false;
            }

            WorldLoadResult result = new WorldSaveService().Load(path);

            if (!result.Success)
            {
                failureReason = "Unable to load saved multiplayer world before hosting.";
                LastFailureReason = failureReason;
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to load multiplayer host world before start file={SanitizeSavePath(path)} reason=load-failed",
                    this);
                return false;
            }

            try
            {
                GeneratedCreativeWorld generated = WorldSaveGeneration.Regenerate(result.Data);
                worldManager.InitializeGeneratedWorld(
                    generated,
                    chunkAuthoritySync,
                    deferInitialRendererRebuild: Application.isPlaying);
            }
            catch (Exception exception)
            {
                failureReason = "Unable to load saved multiplayer world because the save metadata could not regenerate the host world.";
                LastFailureReason = failureReason;
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to load multiplayer host world before start file={SanitizeSavePath(path)} reason=world-regeneration-failed exception={exception.GetType().Name}",
                    this);
                return false;
            }

            // Suppress container auto-loot while applying the save: loaded block deltas that remove
            // crates must not dump their (generated) loot into the player. The authoritative container
            // contents are restored explicitly below.
            worldManager.SuppressContainerAutoLoot = true;
            try
            {
                result.ApplyTo(worldManager.World, preserveLoadedBlockChanges: true);
                worldManager.RestoreSimulationState(result.Data);
                worldManager.RestoreWorldTimeTicks(result.Data.WorldTimeTicks);
                worldManager.SetGameMode(CreativeWorldManager.ParseGameMode(result.Data.GameMode));
                RestoreContainers(result.Data.Containers);

                // Always restore (an empty list clears any prior session's station models), and
                // a save without player state resets vitals to a fresh spawn inside the restore.
                if (survivalSync != null)
                {
                    survivalSync.RestoreStationStates(WorldSaveStateMapper.FromSavedStations(result.Data.Stations));
                    survivalSync.RestoreLocalInventory(
                        result.CreateInventory(),
                        result.Data.PlayerInventory != null ? result.Data.PlayerInventory.SelectedHotbarSlotIndex : 0);
                    survivalSync.RestoreSharedCrateInventory(result.Data.SharedCrateInventory);
                    survivalSync.RestorePersistedRemoteInventories(result.Data.MultiplayerPlayerInventories);
                }

                vitalsRuntime?.RestorePlayerSaveState(result.Data.PlayerState);
            }
            finally
            {
                worldManager.SuppressContainerAutoLoot = false;
            }
            worldDifficulty = result.Data.Difficulty ?? string.Empty;
            vitalsRuntime?.ConfigureDifficulty(worldDifficulty);
            worldPreset = string.IsNullOrWhiteSpace(result.Data.WorldPreset) ? DefaultWorldPreset : result.Data.WorldPreset;
            if (!Application.isPlaying)
                worldManager.Renderer?.RebuildAll();
            LastHostLoadSucceeded = true;
            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Loaded multiplayer host world before start file={SanitizeSavePath(path)}");
            return true;
        }

        bool InitializeFreshMultiplayerWorldBeforeHostStart(string path, out string failureReason)
        {
            failureReason = string.Empty;

            if (!TryEnsureHostSaveAuthority(out failureReason, "initialize fresh multiplayer world before hosting"))
            {
                LastFailureReason = failureReason;
                return false;
            }

            if (!TryResolveWorldManager(out failureReason, "initialize fresh multiplayer world before hosting"))
            {
                LastFailureReason = failureReason;
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to initialize fresh multiplayer host world before start file={SanitizeSavePath(path)} reason=world-unavailable",
                    context: this);
                return false;
            }

            try
            {
                GeneratedCreativeWorld generated = CreativeWorldManager.CreateDefaultGeneratedWorld();
                worldManager.InitializeGeneratedWorld(
                    generated,
                    chunkAuthoritySync,
                    deferInitialRendererRebuild: Application.isPlaying);
                worldManager.SetGameMode(WorldGameMode.Survival);
                ResetFreshMultiplayerSurvivalState();
            }
            catch (Exception exception)
            {
                failureReason = "Unable to initialize a fresh multiplayer world before hosting.";
                LastFailureReason = failureReason;
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to initialize fresh multiplayer host world before start file={SanitizeSavePath(path)} reason=fresh-world-failed exception={exception.GetType().Name}",
                    this);
                return false;
            }

            worldDifficulty = string.Empty;
            vitalsRuntime?.ConfigureDifficulty(worldDifficulty);
            vitalsRuntime?.ResetVitalsToFull();
            worldPreset = DefaultWorldPreset;
            if (!Application.isPlaying)
                worldManager.Renderer?.RebuildAll();
            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Initialized fresh multiplayer host world before start file={SanitizeSavePath(path)}");
            return true;
        }

        void ResetFreshMultiplayerSurvivalState()
        {
            if (survivalSync == null)
                return;

            survivalSync.SetMode(PlayerModeState.Survival);
            survivalSync.RestoreStationStates(null);
            survivalSync.RestoreLocalInventory(new Inventory(ItemRegistry.Default), selectedHotbarSlot: 0);
            survivalSync.RestoreSharedCrateInventory(null);
            survivalSync.RestorePersistedRemoteInventories(null);
        }

        bool SaveWorldBeforeHostShutdown(out string failureReason)
        {
            failureReason = string.Empty;
            LastShutdownSaveAttempted = true;
            LastShutdownSaveSucceeded = false;
            LastFailureReason = string.Empty;

            if (!TryEnsureHostSaveAuthority(out failureReason, "save multiplayer world before host shutdown"))
            {
                LastFailureReason = failureReason;
                return false;
            }

            if (!TryResolveWorldManager(out failureReason, "save multiplayer world before host shutdown"))
            {
                LastFailureReason = failureReason;
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to save multiplayer host world before shutdown file={SanitizeSavePath(ResolveSavePath())} reason=world-unavailable",
                    context: this);
                return false;
            }

            string path = ResolveSavePath();

            try
            {
                SaveCurrentMultiplayerWorld(path);
                LastShutdownSaveSucceeded = true;
                BlockiverseLog.Info(
                    BlockiverseLogCategory.Persistence,
                    $"Saved multiplayer host world before shutdown file={SanitizeSavePath(path)}");
                return true;
            }
            catch (Exception exception)
            {
                failureReason = "Unable to save multiplayer world before host shutdown.";
                LastFailureReason = failureReason;
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to save multiplayer host world before shutdown file={SanitizeSavePath(path)} exception={exception.GetType().Name}",
                    context: this);
                return false;
            }
        }

        void SaveWorldForApplicationPause()
        {
            LastApplicationPauseSaveAttempted = false;
            LastApplicationPauseSaveSucceeded = false;
            LastFailureReason = string.Empty;

            if (session == null ||
                session.NetworkManager == null ||
                !session.NetworkManager.IsListening)
            {
                return;
            }

            LastApplicationPauseSaveAttempted = true;

            if (!TryEnsureHostSaveAuthority(out string failureReason, "save multiplayer world on application pause"))
            {
                LastFailureReason = failureReason;
                return;
            }

            if (!TryResolveWorldManager(out failureReason, "save multiplayer world on application pause"))
            {
                LastFailureReason = failureReason;
                return;
            }

            try
            {
                SaveCurrentMultiplayerWorld(ResolveSavePath());
                LastApplicationPauseSaveSucceeded = true;
            }
            catch (Exception exception)
            {
                LastFailureReason = "Unable to save multiplayer world on application pause.";
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to save multiplayer host world on application pause file={SanitizeSavePath(ResolveSavePath())} exception={exception.GetType().Name}",
                    context: this);
            }
        }

        // World-sim state beyond blocks/containers: weather machine, vegetation/farming queues,
        // timed-station contents, and the hosting player's presence/vitals.
        WorldSaveExtras BuildSaveExtras()
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

            extras.PlayerState = vitalsRuntime != null ? vitalsRuntime.BuildPlayerSaveState() : null;
            return extras;
        }

        // Host autosave: while hosting with save authority, persist the world on the save-service
        // cadence so a crash or battery death loses at most one interval (§6.7).
        void Update()
        {
            CompleteAutoSaveIfReady();

            if (!ShouldStartHostAutoSave(session != null, subscribed, Time.unscaledTime, lastAutoSaveTime))
                return;

            lastAutoSaveTime = Time.unscaledTime;

            if (session.NetworkManager == null || !session.NetworkManager.IsListening)
                return;

            if (!ResolveAuthorityBoundary().CanSaveMultiplayerWorld || worldManager == null || worldManager.World == null)
                return;

            StartAutoSave(ResolveSavePath());
        }

        public static bool ShouldStartHostAutoSave(
            bool hasSession,
            bool isSubscribed,
            float currentUnscaledTime,
            float lastAutoSaveTime) =>
            hasSession &&
            isSubscribed &&
            WorldSaveService.ShouldAutoSave(currentUnscaledTime - lastAutoSaveTime);

        void StartAutoSave(string path)
        {
            if (autoSaveTask != null)
                return;

            try
            {
                WorldSaveService.WorldSaveSnapshot snapshot = CaptureCurrentMultiplayerWorldSnapshot(path);
                autoSavePath = path;
                autoSaveTask = Task.Run(() => new WorldSaveService().Save(snapshot));
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to start autosave multiplayer host world file={SanitizeSavePath(path)} exception={exception.GetType().Name}",
                    context: this);
            }
        }

        void SaveCurrentMultiplayerWorld(string path)
        {
            ResolveReferences();
            WaitForAutoSave();

            new WorldSaveService().Save(CaptureCurrentMultiplayerWorldSnapshot(path));
        }

        WorldSaveService.WorldSaveSnapshot CaptureCurrentMultiplayerWorldSnapshot(string path)
        {
            Inventory inventory = survivalSync != null
                ? survivalSync.BuildPersistedInventory()
                : new Inventory(ItemRegistry.Default);
            int selectedHotbarSlotIndex = survivalSync != null ? survivalSync.SelectedHotbarSlotIndex : 0;

            return new WorldSaveService().CaptureSnapshot(
                path,
                ResolveWorldName(),
                worldManager.World,
                inventory,
                selectedHotbarSlotIndex,
                weatherState: worldManager.CurrentWeatherState,
                gameMode: worldManager.GameModeString,
                worldTimeTicks: worldManager.WorldTimeClock?.TotalElapsedTicks ?? 0L,
                containers: WorldSaveContainerMapper.BuildSavedContainers(worldManager.ContainerStore),
                difficulty: worldDifficulty,
                worldPreset: worldPreset,
                extras: BuildSaveExtras(),
                additionalPlayerInventories: survivalSync?.BuildPersistedPlayerInventories());
        }

        void CompleteAutoSaveIfReady()
        {
            if (autoSaveTask == null || !autoSaveTask.IsCompleted)
                return;

            try
            {
                autoSaveTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to autosave multiplayer host world file={SanitizeSavePath(autoSavePath)} exception={exception.GetType().Name}",
                    context: this);
            }
            finally
            {
                autoSaveTask = null;
                autoSavePath = null;
            }
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
                    $"Failed to finish pending multiplayer autosave file={SanitizeSavePath(autoSavePath)} exception={exception.GetType().Name}",
                    context: this);
            }
            finally
            {
                autoSaveTask = null;
                autoSavePath = null;
            }
        }

        void RestoreContainers(SavedContainer[] saved)
        {
            worldManager.RestoreContainerStore(
                WorldSaveContainerMapper.BuildRestoredContainers(saved, LogInvalidContainerSlot));
        }

        void LogInvalidContainerSlot(SavedContainer container, SavedContainerSlot slot)
        {
            BlockiverseLog.Warning(
                BlockiverseLogCategory.Persistence,
                $"Skipped invalid saved container slot world={ResolveWorldName()} container=({container.X},{container.Y},{container.Z}) id={(slot == null || string.IsNullOrWhiteSpace(slot.CanonicalId) ? "(empty)" : slot.CanonicalId)} count={slot?.Count ?? 0}",
                this);
        }

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            if (worldManager == null)
                worldManager = BlockiverseSceneLookup.Find<CreativeWorldManager>(FindObjectsInactive.Include);

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();

            if (survivalSync == null)
                survivalSync = GetComponent<MultiplayerSurvivalSync>() ??
                    BlockiverseSceneLookup.Find<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            if (vitalsRuntime == null)
                vitalsRuntime = BlockiverseSceneLookup.Find<SurvivalVitalsRuntime>(FindObjectsInactive.Include);
        }

        bool TryResolveWorldManager(out string failureReason, string operation)
        {
            failureReason = string.Empty;
            ResolveReferences();

            if (worldManager == null)
            {
                failureReason = $"Unable to {operation} because the host world is unavailable.";
                return false;
            }

            if (worldManager.World == null)
            {
                try
                {
                    worldManager.InitializeDefaultWorld();
                }
                catch (Exception exception)
                {
                    failureReason = $"Unable to {operation} because the host world could not be initialized. exception={exception.GetType().Name}";
                    return false;
                }
            }

            if (worldManager.World != null)
                return true;

            failureReason = $"Unable to {operation} because the host world is unavailable.";
            return false;
        }

        bool TryEnsureHostSaveAuthority(out string failureReason, string operation)
        {
            failureReason = string.Empty;
            ChunkAuthorityBoundary boundary = ResolveAuthorityBoundary();

            if (boundary.CanSaveMultiplayerWorld)
                return true;

            failureReason = $"Unable to {operation} because only the host owns multiplayer world save state.";
            BlockiverseLog.Error(
                BlockiverseLogCategory.Persistence,
                $"Rejected multiplayer world save operation role={boundary.Role} operation={operation}",
                context: this);
            return false;
        }

        ChunkAuthorityBoundary ResolveAuthorityBoundary()
        {
            ResolveReferences();

            if (chunkAuthoritySync != null)
                return chunkAuthoritySync.CurrentBoundary;

            if (session != null &&
                session.NetworkManager.IsListening &&
                session.CurrentMode == NetworkSessionMode.Client)
            {
                return ChunkAuthorityBoundary.ForClient(
                    session.NetworkManager.LocalClientId,
                    NetworkManager.ServerClientId);
            }

            ulong hostClientId = session != null ? session.NetworkManager.LocalClientId : 0;
            return ChunkAuthorityBoundary.ForHost(hostClientId);
        }

        void Subscribe()
        {
            if (subscribed || session == null)
                return;

            session.HostStartPreparing += RestoreSavedWorldBeforeHostStart;
            session.HostShutdownPreparing += SaveWorldBeforeHostShutdown;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || session == null)
                return;

            session.HostStartPreparing -= RestoreSavedWorldBeforeHostStart;
            session.HostShutdownPreparing -= SaveWorldBeforeHostShutdown;
            subscribed = false;
        }

        // Trust model: configured save paths must be absolute and resolve (after normalization,
        // which collapses ".." segments) under the app's persistent data folder or the OS temp
        // folder (used by trusted local tooling and tests). Everything else — relative paths and
        // paths escaping both roots — is rejected.
        static bool TryResolveTrustedSavePath(string candidate, out string fullPath)
        {
            fullPath = null;

            try
            {
                if (!Path.IsPathRooted(candidate))
                    return false;

                string resolved = Path.GetFullPath(candidate);

                if (!IsUnderRoot(resolved, Application.persistentDataPath) &&
                    !IsUnderRoot(resolved, Path.GetTempPath()))
                {
                    return false;
                }

                fullPath = resolved;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool IsUnderRoot(string fullPath, string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return false;

            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        string ResolveSavePath()
        {
            if (!string.IsNullOrWhiteSpace(configuredSavePath))
                return configuredSavePath;

            string fileName = string.IsNullOrWhiteSpace(saveFileName) ? DefaultSaveFileName : Path.GetFileName(saveFileName);
            return Path.Combine(Application.persistentDataPath, "Saves", fileName);
        }

        string ResolveWorldName()
        {
            return string.IsNullOrWhiteSpace(worldName) ? DefaultWorldName : worldName.Trim();
        }

        static string SanitizeSavePath(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? "(empty)" : Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return "(invalid)";
            }
        }
    }
}
