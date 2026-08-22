using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using UnityEngine;

namespace Blockiverse.Server
{
    // Entry point for the headless dedicated server. Lives on the server scene's network root and
    // is the only thing in the build that starts a session without a menu.
    //
    // Order matters here and is not incidental:
    //   1. logging first, so a configuration failure is visible
    //   2. configuration next, and fatal on any problem
    //   3. save-root registration BEFORE persistence is configured, since the policy seals later
    //   4. session start last
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-8000)]
    public sealed class BlockiverseDedicatedServerBootstrap : MonoBehaviour
    {
        public const int ExitConfigurationError = 78; // EX_CONFIG
        public const string DefaultConfigFileName = "blockiverse-server.properties";
        public const string CleanShutdownMarkerName = ".clean-shutdown";
        public const string HeartbeatFileName = ".heartbeat";
        const float HeartbeatIntervalSeconds = 10.0f;

        [SerializeField] BlockiverseNetworkSession session;
        [SerializeField] MultiplayerWorldPersistence persistence;
        [SerializeField] MultiplayerSurvivalSync survivalSync;
        [SerializeField] bool startOnAwake = true;

        BlockiverseServerOptions options;
        BlockiverseServerAdminConsole adminConsole;
        string worldDirectory;
        float lastHeartbeat;
        bool stopping;

        public BlockiverseServerOptions Options => options;
        public bool IsRunning { get; private set; }

        void Awake()
        {
            if (!startOnAwake)
                return;

            if (!Application.isBatchMode)
            {
                // The server scene should never be entered from the editor's Play button by
                // accident; that would bind a port and start writing to a world directory.
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    "Dedicated server bootstrap is present outside batch mode; not starting automatically.",
                    this);
                return;
            }

            StartServerFromEnvironment();
        }

        public void StartServerFromEnvironment()
        {
            string[] commandLine = Environment.GetCommandLineArgs();
            var arguments = new List<string>(commandLine.Length);
            for (int index = 1; index < commandLine.Length; index++)
                arguments.Add(commandLine[index]);

            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                string key = Convert.ToString(entry.Key);
                if (key != null && key.StartsWith(BlockiverseServerOptionsResolver.EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
                    environment[key] = Convert.ToString(entry.Value);
            }

            Run(arguments, environment);
        }

        // Split from StartServerFromEnvironment so tests can drive it without a process.
        public void Run(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
        {
            var problems = new List<string>();
            Dictionary<string, string> fileValues = ReadConfigFile(arguments, environment, problems);

            BlockiverseServerOptionsResolver.Resolution resolution =
                BlockiverseServerOptionsResolver.Resolve(arguments, environment, fileValues);
            problems.AddRange(resolution.Problems);

            options = resolution.Options;
            ConfigureLogging(options);

            if (problems.Count > 0)
            {
                foreach (string problem in problems)
                    BlockiverseLog.Error(BlockiverseLogCategory.Bootstrap, $"configuration: {problem}");

                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"Refusing to start with {problems.Count} configuration problem(s). Nothing was written.");
                Quit(ExitConfigurationError);
                return;
            }

            foreach (string line in options.Describe().Split('\n'))
            {
                if (line.Length > 0)
                    BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, line);
            }

            foreach (string advisory in options.Advisories())
                BlockiverseLog.Warning(BlockiverseLogCategory.Bootstrap, advisory);

            if (!PrepareWorldDirectory())
                return;

            ApplyRuntimeSettings();
            ApplySessionConfiguration();

            if (!StartSession())
                return;

            adminConsole = new BlockiverseServerAdminConsole(this, options);
            adminConsole.Start();
            IsRunning = true;
        }

        static Dictionary<string, string> ReadConfigFile(
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment,
            List<string> problems)
        {
            string path = null;

            for (int index = 0; index < arguments.Count; index++)
            {
                if (!string.Equals(arguments[index], "--config", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (index + 1 < arguments.Count)
                    path = arguments[index + 1];
            }

            if (path == null && environment != null &&
                environment.TryGetValue(BlockiverseServerOptionsResolver.EnvironmentPrefix + "CONFIG", out string fromEnvironment))
            {
                path = fromEnvironment;
            }

            // An explicitly named config file that is missing is a mistake worth failing on. The
            // default one being absent is normal -- everything has a default.
            bool explicitlyRequested = path != null;
            path ??= DefaultConfigFileName;

            try
            {
                if (!File.Exists(path))
                {
                    if (explicitlyRequested)
                        problems.Add($"config file '{path}' not found");

                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                return BlockiverseServerOptionsResolver.ParseConfigText(File.ReadAllText(path), problems);
            }
            catch (Exception exception)
            {
                problems.Add($"could not read config file '{path}': {exception.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        static void ConfigureLogging(BlockiverseServerOptions options)
        {
            // Info is suppressed in batch mode by default, which would hide every lifecycle and
            // persistence line the operator needs. The server decides its own level.
            BlockiverseLog.DevelopmentInfoEnabled = options.LogLevel >= BlockiverseServerLogLevel.Info;
            BlockiverseLog.SetSink(new BlockiverseServerLogSink(options.LogLevel, options.LogFormat));
        }

        bool PrepareWorldDirectory()
        {
            try
            {
                worldDirectory = Path.GetFullPath(options.WorldDirectory);
                Directory.CreateDirectory(worldDirectory);
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"Could not create world directory '{options.WorldDirectory}': {exception.Message}");
                Quit(ExitConfigurationError);
                return false;
            }

            // Must happen before persistence is configured: the policy seals once a session is
            // listening, and a save path outside a registered root is refused.
            if (!BlockiverseSavePathPolicy.TryRegisterAdditionalRoot(worldDirectory, out string failureReason))
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"World directory '{worldDirectory}' is not usable as a save root: {failureReason}");
                Quit(ExitConfigurationError);
                return false;
            }

            string marker = Path.Combine(worldDirectory, CleanShutdownMarkerName);
            if (!File.Exists(marker))
            {
                // Either this is a first run, or the previous one did not stop cleanly. Say so:
                // an operator who does not know a stop was unclean cannot judge what was lost.
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"No clean-shutdown marker in '{worldDirectory}'. Either this world is new, or the last run " +
                    "ended without saving. Up to one autosave interval of progress may be missing.");
            }
            else
            {
                TryDelete(marker);
            }

            return true;
        }

        void ApplyRuntimeSettings()
        {
            // Without a frame cap a headless loop spins as fast as it can and burns a core for
            // nothing. Tick counts are frame-rate independent -- WorldTimeClock accumulates and
            // emits whole ticks -- so this only bounds wasted work.
            Application.targetFrameRate = options.FrameRate;
            QualitySettings.vSyncCount = 0;
            // WorldTimeClock hard-returns while paused, and nothing on a server ever unpauses it.
            BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
        }

        void ApplySessionConfiguration()
        {
            ResolveReferences();

            if (session != null)
            {
                var networkConfig = new BlockiverseNetworkConfig(
                    address: options.ListenAddress,
                    listenAddress: options.ListenAddress,
                    port: options.Port,
                    maxPlayers: options.MaxPlayers,
                    joinCode: string.IsNullOrEmpty(options.Secret) ? null : options.Secret);

                session.Configure(networkConfig);

                if (options.TlsEnabled)
                    ApplyTransportSecurity();
            }

            if (survivalSync != null)
                survivalSync.ConfigureMaxStashedPlayers(options.MaxStashedPlayers);

            // Set before persistence subscribes so the first autosave already uses the operator's
            // cadence rather than the five-minute in-headset default.
            WorldSaveService.AutoSaveIntervalSeconds = options.AutoSaveSeconds;

            if (persistence != null)
            {
                string savePath = Path.Combine(worldDirectory, MultiplayerWorldPersistence.DefaultSaveFileName);
                // worldManager stays null: MultiplayerWorldPersistence resolves the scene's
                // IMultiplayerWorldContext itself, and the server scene has exactly one.
                persistence.Configure(session, targetWorldManager: null, targetSavePath: savePath, targetWorldName: options.WorldName);
            }
        }

        void ApplyTransportSecurity()
        {
            try
            {
                session.ConfigureTransportSecurity(
                    enabled: true,
                    serverCertificate: File.ReadAllText(options.TlsCertificatePath),
                    serverPrivateKey: File.ReadAllText(options.TlsKeyPath),
                    serverCommonName: options.TlsServerName,
                    clientCaCertificate: File.ReadAllText(options.TlsCertificatePath));
            }
            catch (Exception exception)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"Could not load TLS material: {exception.Message}");
                Quit(ExitConfigurationError);
            }
        }

        bool StartSession()
        {
            if (session == null)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    "No BlockiverseNetworkSession on the server scene; nothing to start.");
                Quit(ExitConfigurationError);
                return false;
            }

            if (!session.StartServer())
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"Dedicated server failed to start: {session.LastDisconnectReason}");
                Quit(1);
                return false;
            }

            BlockiverseSavePathPolicy.SealForSession();
            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                $"Listening on {options.ListenAddress}:{options.Port}/udp for up to {options.MaxPlayers} player(s).");
            return true;
        }

        void ResolveReferences()
        {
            session ??= FindFirstObjectByType<BlockiverseNetworkSession>(FindObjectsInactive.Include);
            persistence ??= FindFirstObjectByType<MultiplayerWorldPersistence>(FindObjectsInactive.Include);
            survivalSync ??= FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);
        }

        void Update()
        {
            adminConsole?.DrainPendingCommands();

            // A file whose mtime advances is something a container healthcheck can read without a
            // client, which a UDP game port cannot offer.
            if (IsRunning && Time.unscaledTime - lastHeartbeat >= HeartbeatIntervalSeconds)
            {
                lastHeartbeat = Time.unscaledTime;
                TouchHeartbeat();
            }
        }

        void TouchHeartbeat()
        {
            if (string.IsNullOrEmpty(worldDirectory))
                return;

            try
            {
                File.WriteAllText(Path.Combine(worldDirectory, HeartbeatFileName), DateTime.UtcNow.ToString("O"));
            }
            catch (Exception)
            {
                // A healthcheck that cannot be written is not worth failing the server over.
            }
        }

        // The clean way down: save, disconnect, mark, exit. Callable from the admin console.
        public void RequestStop()
        {
            if (stopping)
                return;

            stopping = true;
            StartCoroutine(StopSequence());
        }

        IEnumerator StopSequence()
        {
            BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, "Stopping: saving world and disconnecting players.");

            if (session != null)
            {
                // StopSession runs shutdown preparation, which is what writes the world.
                session.StopSession();

                float deadline = Time.realtimeSinceStartup + 30.0f;
                while (!session.LastStopRequestSucceeded && Time.realtimeSinceStartup < deadline)
                {
                    session.StopSession();
                    yield return null;
                }

                if (!session.LastStopRequestSucceeded)
                {
                    BlockiverseLog.Error(
                        BlockiverseLogCategory.Persistence,
                        "Shutdown preparation did not succeed within 30s; stopping anyway. The world may be stale.");
                }
            }

            WriteCleanShutdownMarker();
            IsRunning = false;
            adminConsole?.Stop();
            Quit(0);
        }

        void WriteCleanShutdownMarker()
        {
            if (string.IsNullOrEmpty(worldDirectory))
                return;

            try
            {
                File.WriteAllText(
                    Path.Combine(worldDirectory, CleanShutdownMarkerName),
                    DateTime.UtcNow.ToString("O"));
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Persistence,
                    $"Could not write the clean-shutdown marker: {exception.Message}");
            }
        }

        void OnApplicationQuit()
        {
            // Covers the paths that do not go through RequestStop: a container SIGTERM Unity does
            // surface, or an editor stop. Persistence saves on this signal already; the marker
            // records that the stop was orderly.
            adminConsole?.Stop();
            if (IsRunning)
                WriteCleanShutdownMarker();
        }

        static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort; a stale marker only weakens a warning.
            }
        }

        static void Quit(int exitCode)
        {
            if (Application.isBatchMode)
                Application.Quit(exitCode);
        }
    }
}
