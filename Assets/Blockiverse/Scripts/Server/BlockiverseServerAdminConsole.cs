using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Blockiverse.Core;
using Blockiverse.Networking;
using Unity.Netcode;
using UnityEngine;

namespace Blockiverse.Server
{
    // Operator control surface: standard input, plus a Unix domain socket.
    //
    // Deliberately NOT a TCP or HTTP port. A network admin port needs an authentication story this
    // project does not have, and it is the classic way a self-hosted game server gets taken over. A
    // Unix socket takes its authorization from filesystem permissions, which the operator already
    // controls, and cannot be reached from off the machine at all.
    //
    // stdin covers `docker run -it` and a foreground shell; the socket covers `docker run -d` and
    // `docker exec`, where stdin is not attached.
    public sealed class BlockiverseServerAdminConsole
    {
        readonly BlockiverseDedicatedServerBootstrap bootstrap;
        readonly BlockiverseServerOptions options;
        readonly ConcurrentQueue<PendingCommand> pending = new();
        readonly List<Thread> readers = new();
        readonly BlockiverseServerAccessControl accessControl;

        Socket listener;
        volatile bool running;

        sealed class PendingCommand
        {
            public string Line;
            public Socket Reply;
        }

        public BlockiverseServerAdminConsole(
            BlockiverseDedicatedServerBootstrap bootstrap,
            BlockiverseServerOptions options)
        {
            this.bootstrap = bootstrap;
            this.options = options;
            accessControl = new BlockiverseServerAccessControl(options);
        }

        public BlockiverseServerAccessControl AccessControl => accessControl;

        public void Start()
        {
            running = true;

            if (options.AdminStdinEnabled)
                StartReader(ReadStandardInput, "blockiverse-admin-stdin");

            StartSocketListener();
        }

        void StartReader(ThreadStart body, string name)
        {
            var thread = new Thread(body) { IsBackground = true, Name = name };
            thread.Start();
            readers.Add(thread);
        }

        void ReadStandardInput()
        {
            try
            {
                while (running)
                {
                    string line = Console.In.ReadLine();
                    if (line == null)
                        return; // stdin closed: normal under `docker run -d`.

                    if (line.Trim().Length > 0)
                        pending.Enqueue(new PendingCommand { Line = line.Trim() });
                }
            }
            catch (Exception)
            {
                // A closed or unreadable stdin must never take the server down.
            }
        }

        const string NoSocketConsequence =
            "Admin commands are then only available on stdin, which is NOT attached under systemd " +
            "or `docker run -d` -- meaning no `save`, no `stop`, and no clean shutdown. The world " +
            "still autosaves, so at most one autosave interval is at risk, but fix this before " +
            "relying on the server.";

        void StartSocketListener()
        {
            string path = ResolveSocketPath();
            if (string.IsNullOrEmpty(path))
                return;

            // AF_UNIX caps the socket path at ~104 bytes -- far shorter than any filesystem path
            // limit, so a perfectly legal world directory can simply be too deep to hold a socket.
            // Check it explicitly: the raw OS error sends operators to look at permissions, which
            // is the wrong place, and the consequence is severe (see the catch below).
            const int MaxUnixSocketPathBytes = 104;
            if (Encoding.UTF8.GetByteCount(path) > MaxUnixSocketPathBytes)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Admin socket path '{path}' is {Encoding.UTF8.GetByteCount(path)} bytes; the " +
                    $"operating system limit for a Unix socket is {MaxUnixSocketPathBytes}. Set " +
                    "admin.socket_path to somewhere shorter (for example /run/blockiverse.sock), or " +
                    "use a shallower world.dir. " + NoSocketConsequence);
                return;
            }

            try
            {
                // A leftover socket file from an unclean stop would block the bind.
                if (File.Exists(path))
                    File.Delete(path);

                listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(path));
                listener.Listen(4);
                StartReader(AcceptSocketClients, "blockiverse-admin-socket");

                BlockiverseLog.Info(
                    BlockiverseLogCategory.Bootstrap,
                    $"Admin socket listening at {path}. Its file permissions are its access control; do not " +
                    "place the world directory on a share other users can write to.");
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Could not open the admin socket at '{path}': {exception.Message}. " +
                    NoSocketConsequence);
            }
        }

        string ResolveSocketPath()
        {
            if (!string.IsNullOrEmpty(options.AdminSocketPath))
                return options.AdminSocketPath;

            try
            {
                return Path.Combine(Path.GetFullPath(options.WorldDirectory), "admin.sock");
            }
            catch (Exception)
            {
                return null;
            }
        }

        void AcceptSocketClients()
        {
            while (running)
            {
                Socket client = null;
                try
                {
                    client = listener.Accept();
                    var buffer = new byte[4096];
                    int read = client.Receive(buffer);
                    if (read <= 0)
                    {
                        client.Dispose();
                        continue;
                    }

                    string line = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    if (line.Length == 0)
                    {
                        client.Dispose();
                        continue;
                    }

                    // The socket stays open until the main thread has replied.
                    pending.Enqueue(new PendingCommand { Line = line, Reply = client });
                }
                catch (Exception)
                {
                    client?.Dispose();
                    if (!running)
                        return;

                    // A persistent error -- descriptor exhaustion, say -- would otherwise retry
                    // instantly forever and burn a core while the server looks healthy.
                    Thread.Sleep(200);
                }
            }
        }

        // Called from Update: every command touches Unity API, which is main-thread only.
        public void DrainPendingCommands()
        {
            while (pending.TryDequeue(out PendingCommand command))
            {
                string response;
                try
                {
                    response = Execute(command.Line);
                }
                catch (Exception exception)
                {
                    response = $"error: {exception.Message}";
                }

                if (command.Reply != null)
                {
                    TrySend(command.Reply, response);
                    command.Reply.Dispose();
                }
                else
                {
                    Console.Out.WriteLine(response);
                }
            }
        }

        static void TrySend(Socket socket, string response)
        {
            try
            {
                socket.Send(Encoding.UTF8.GetBytes(response + "\n"));
            }
            catch (Exception)
            {
                // The caller may have hung up; nothing to do about it.
            }
        }

        string Execute(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;

            switch (parts[0].ToLowerInvariant())
            {
                case "help": return Help();
                case "status": return Status();
                case "list": return ListPlayers();
                case "save": return SaveNow();
                case "stop": bootstrap.RequestStop(); return "stopping: saving world and disconnecting players";
                case "kick": return Kick(parts);
                case "ban": return BanAndDisconnect(parts.Length > 1 ? parts[1] : null);
                case "unban": return accessControl.Unban(parts.Length > 1 ? parts[1] : null);
                default: return $"unknown command '{parts[0]}'. Try 'help'.";
            }
        }

        static string Help() =>
            "help                  list commands\n" +
            "status                uptime, world, player count\n" +
            "list                  connected players\n" +
            "save                  save the world now\n" +
            "stop                  save, disconnect everyone, and exit cleanly\n" +
            "kick <clientId>       disconnect one player\n" +
            "ban <playerId>        add to the ban list and disconnect\n" +
            "unban <playerId>      remove from the ban list";

        string Status()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            int players = networkManager != null ? CountRemote(networkManager) : 0;
            var text = new StringBuilder();
            text.Append("name:     ").Append(options.ServerName).Append('\n');
            text.Append("running:  ").Append(bootstrap.IsRunning).Append('\n');
            text.Append("uptime:   ").Append(TimeSpan.FromSeconds(Time.realtimeSinceStartup).ToString(@"hh\:mm\:ss")).Append('\n');
            text.Append("players:  ").Append(players).Append('/').Append(options.MaxPlayers).Append('\n');
            text.Append("world:    ").Append(options.WorldName).Append(" (").Append(options.WorldDirectory).Append(')');
            return text.ToString();
        }

        static int CountRemote(NetworkManager networkManager)
        {
            int count = 0;
            foreach (ulong id in networkManager.ConnectedClientsIds)
            {
                if (id != networkManager.LocalClientId)
                    count++;
            }

            return count;
        }

        static string ListPlayers()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
                return "server is not listening";

            MultiplayerSurvivalSync survivalSync =
                UnityEngine.Object.FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            var text = new StringBuilder();
            foreach (ulong id in networkManager.ConnectedClientsIds)
            {
                if (id == networkManager.LocalClientId)
                    continue;

                // The player id is what `ban` takes. Printing only the numeric client id made the
                // ban command impossible to use: nothing else ever revealed an id it would match.
                string playerId = survivalSync != null && survivalSync.TryGetPlayerIdForClient(id, out string resolved)
                    ? resolved
                    : "(identity not yet received)";

                text.Append("client ").Append(id.ToString(CultureInfo.InvariantCulture))
                    .Append("  player ").Append(playerId).Append('\n');
            }

            return text.Length == 0 ? "no players connected" : text.ToString().TrimEnd('\n');
        }

        static string SaveNow()
        {
            MultiplayerWorldPersistence persistence =
                UnityEngine.Object.FindFirstObjectByType<MultiplayerWorldPersistence>(FindObjectsInactive.Include);

            if (persistence == null)
                return "no persistence component in the scene";

            return persistence.SaveCurrentMultiplayerWorld()
                ? "world saved"
                : "save refused: this process does not hold save authority";
        }

        // Adding an id to the ban file is only half of a ban: without the disconnect the player
        // keeps playing until they choose to leave, which is not what the operator asked for.
        string BanAndDisconnect(string playerId)
        {
            string result = accessControl.Ban(playerId);
            if (string.IsNullOrWhiteSpace(playerId))
                return result;

            MultiplayerSurvivalSync survivalSync =
                UnityEngine.Object.FindFirstObjectByType<MultiplayerSurvivalSync>(FindObjectsInactive.Include);

            int disconnected = survivalSync != null
                ? survivalSync.DisconnectPlayer(playerId, "Banned by the server operator.")
                : 0;

            return disconnected > 0
                ? $"{result}; disconnected {disconnected} connected session(s)"
                : $"{result}; not currently connected";
        }

        static string Kick(string[] parts)
        {
            if (parts.Length < 2 || !ulong.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong clientId))
                return "usage: kick <clientId>";

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
                return "server is not listening";

            networkManager.DisconnectClient(clientId, "Kicked by the server operator.");
            return $"kicked client {clientId}";
        }

        public void Stop()
        {
            running = false;

            // Answer and close anything queued but not yet drained: Stop() can run between an
            // enqueue and the next Update, and a caller left waiting on a socket that will never
            // be read is worse than a refusal.
            while (pending.TryDequeue(out PendingCommand abandoned))
            {
                if (abandoned.Reply == null)
                    continue;

                TrySend(abandoned.Reply, "server is shutting down");
                abandoned.Reply.Dispose();
            }

            try
            {
                listener?.Dispose();
            }
            catch (Exception)
            {
                // Disposing a listener mid-accept throws; that is the intended wakeup.
            }

            string path = ResolveSocketPath();
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception)
                {
                    // A leftover socket file is cleaned up on the next start.
                }
            }
        }
    }
}
