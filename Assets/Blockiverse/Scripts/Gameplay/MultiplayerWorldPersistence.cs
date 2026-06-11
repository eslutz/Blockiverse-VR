using System;
using System.Collections.Generic;
using System.IO;
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
        const string DefaultWorldPreset = "survival_terrain";

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] CreativeWorldManager worldManager;
        [SerializeField] MultiplayerChunkAuthoritySync chunkAuthoritySync;
        [SerializeField] string saveFileName = DefaultSaveFileName;
        [SerializeField] string worldName = DefaultWorldName;

        string configuredSavePath;
        bool subscribed;

        // Mirrors the single-player defaults in BlockiverseWorldSessionController so fresh
        // multiplayer worlds save the same difficulty/preset metadata.
        string worldDifficulty = string.Empty;
        string worldPreset = DefaultWorldPreset;

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

        bool RestoreSavedWorldBeforeHostStart(out string failureReason)
        {
            failureReason = string.Empty;
            LastHostLoadAttempted = false;
            LastHostLoadSucceeded = false;
            LastFailureReason = string.Empty;

            string path = ResolveSavePath();

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                // No save on disk means a fresh world; reset metadata to the defaults.
                worldDifficulty = string.Empty;
                worldPreset = DefaultWorldPreset;
                return true;
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

            // Suppress container auto-loot while applying the save: loaded block deltas that remove
            // crates must not dump their (generated) loot into the player. The authoritative container
            // contents are restored explicitly below.
            worldManager.SuppressContainerAutoLoot = true;
            try
            {
                result.ApplyTo(worldManager.World, preserveLoadedBlockChanges: true);
                worldManager.RestoreWeatherState(result.Data.WeatherState);
                worldManager.WorldTimeClock?.RestoreElapsedTicks(result.Data.WorldTimeTicks);
                worldManager.SetGameMode(CreativeWorldManager.ParseGameMode(result.Data.GameMode));
                RestoreContainers(result.Data.Containers);
            }
            finally
            {
                worldManager.SuppressContainerAutoLoot = false;
            }
            worldDifficulty = result.Data.Difficulty ?? string.Empty;
            worldPreset = string.IsNullOrWhiteSpace(result.Data.WorldPreset) ? DefaultWorldPreset : result.Data.WorldPreset;
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
                new WorldSaveService().Save(path, ResolveWorldName(), worldManager.World, weatherState: worldManager.CurrentWeatherState, gameMode: worldManager.GameModeString, worldTimeTicks: worldManager.WorldTimeClock?.TotalElapsedTicks ?? 0L, containers: BuildSavedContainers(worldManager.ContainerStore), difficulty: worldDifficulty, worldPreset: worldPreset);
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

        // Snapshots the manager's live container contents into the persistence model.
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
                for (int i = 0; i < inventory.SlotCount; i++)
                {
                    ItemStack stack = inventory.GetSlot(i);
                    if (stack.IsEmpty)
                        continue;
                    slots.Add(new SavedContainerSlot { CanonicalId = stack.ItemId.Value, Count = stack.Count });
                }

                // Persist the position even when empty so an emptied crate stays empty across reloads
                // (otherwise the generated loot would refill it).
                result.Add(new SavedContainer
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    Slots = slots.ToArray()
                });
            }

            return result.Count > 0 ? result : null;
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
                {
                    // Corrupted saves can carry blank ids or non-positive counts; restoring them
                    // would throw inside ItemId or corrupt the container inventory.
                    if (string.IsNullOrWhiteSpace(slot.CanonicalId) || slot.Count <= 0)
                    {
                        BlockiverseLog.Warning(
                            BlockiverseLogCategory.Persistence,
                            $"Skipped invalid saved container slot world={ResolveWorldName()} container=({container.X},{container.Y},{container.Z}) id={(string.IsNullOrWhiteSpace(slot.CanonicalId) ? "(empty)" : slot.CanonicalId)} count={slot.Count}",
                            this);
                        continue;
                    }

                    items.Add((slot.CanonicalId, slot.Count));
                }
                restored.Add((new BlockPosition(container.X, container.Y, container.Z), items));
            }

            worldManager.RestoreContainerStore(restored);
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
