using System;
using System.IO;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class MultiplayerWorldPersistence : MonoBehaviour
    {
        const string DefaultSaveFileName = "multiplayer-world.json";
        const string DefaultWorldName = "Multiplayer World";

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] string saveFileName = DefaultSaveFileName;
        [SerializeField] string worldName = DefaultWorldName;

        string configuredSavePath;
        bool subscribed;

        public bool LastHostLoadAttempted { get; private set; }
        public bool LastHostLoadSucceeded { get; private set; }
        public bool LastShutdownSaveAttempted { get; private set; }
        public bool LastShutdownSaveSucceeded { get; private set; }
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
            configuredSavePath = targetSavePath;

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

        bool RestoreSavedWorldBeforeHostStart(out string failureReason)
        {
            failureReason = string.Empty;
            LastHostLoadAttempted = false;
            LastHostLoadSucceeded = false;
            LastFailureReason = string.Empty;

            string path = ResolveSavePath();

            if (!File.Exists(path))
                return true;

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

            WorldLoadResult result = new WorldSaveService(new WorldSaveMigrationRegistry()).Load(path);

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

            if (!SavedWorldMatchesInitializedWorld(result.Data, worldManager.World))
            {
                failureReason = "Unable to load saved multiplayer world because the save metadata does not match the initialized host world.";
                LastFailureReason = failureReason;
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Failed to load multiplayer host world before start file={SanitizeSavePath(path)} reason=world-metadata-mismatch",
                    this);
                return false;
            }

            result.ApplyTo(worldManager.World, preserveLoadedBlockChanges: true);
            worldManager.Renderer?.RebuildAll();
            LastHostLoadSucceeded = true;
            BlockiverseLog.Info(
                BlockiverseLogCategory.Persistence,
                $"Loaded multiplayer host world before start file={SanitizeSavePath(path)}");
            return true;
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
                new WorldSaveService(new WorldSaveMigrationRegistry()).Save(path, ResolveWorldName(), worldManager.World);
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

        void ResolveReferences()
        {
            if (session == null)
                session = GetComponent<BlockiverseNetworkSession>();

            if (worldManager == null)
                worldManager = FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (chunkAuthoritySync == null)
                chunkAuthoritySync = GetComponent<MultiplayerChunkAuthoritySync>();
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

        static bool SavedWorldMatchesInitializedWorld(WorldSaveData data, VoxelWorld world)
        {
            if (data == null || world == null)
                return false;

            return data.Width == world.Bounds.Width &&
                   data.Height == world.Bounds.Height &&
                   data.Depth == world.Bounds.Depth &&
                   data.ChunkSize == world.ChunkSize &&
                   data.Seed == world.Seed;
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
