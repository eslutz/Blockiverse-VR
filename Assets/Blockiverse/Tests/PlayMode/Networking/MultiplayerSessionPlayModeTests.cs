using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Blockiverse.Tests.Networking.PlayMode
{
    public sealed class MultiplayerSessionPlayModeTests
    {
        static ushort nextPort = 7810;
        static readonly List<string> TempSavePaths = new();

        [UnityTest]
        public IEnumerator MultiplayerTestSceneSessionMenuHostsAndJoinsLocalClient()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            BlockiverseMultiplayerSessionMenu hostMenu = UnityEngine.Object.FindFirstObjectByType<BlockiverseMultiplayerSessionMenu>();
            Assert.That(hostSession, Is.Not.Null);
            Assert.That(hostMenu, Is.Not.Null);
            Assert.That(hostMenu.Session, Is.SameAs(hostSession));
            Assert.That(hostMenu.HostButton, Is.Not.Null);
            Assert.That(hostMenu.JoinButton, Is.Not.Null);
            Assert.That(hostMenu.StopButton, Is.Not.Null);
            Assert.That(hostMenu.AddressInput, Is.Not.Null);
            Assert.That(hostMenu.StatusText, Is.Not.Null);
            AssertSceneHasUiInputSystem();

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseMultiplayerSessionMenu clientMenu = CreateSessionMenu("Client Session Menu", clientSession);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            hostMenu.HostButton.onClick.Invoke();
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host menu did not start host.");

            hostMenu.RefreshStatus();
            StringAssert.Contains("Hosting LAN session", hostMenu.StatusText.text);

            clientMenu.AddressInput.text = string.Empty;
            clientMenu.JoinButton.onClick.Invoke();
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client menu did not connect to host.");

            clientMenu.RefreshStatus();
            Assert.That(clientMenu.ResolveJoinAddress(), Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
            StringAssert.Contains("Connected to LAN session", clientMenu.StatusText.text);

            hostMenu.StopButton.onClick.Invoke();
            yield return WaitFor(
                () => !hostSession.NetworkManager.IsListening && !clientSession.NetworkManager.IsListening,
                "Host menu shutdown did not stop all local session managers.");

            hostMenu.RefreshStatus();
            StringAssert.Contains("LAN session stopped", hostMenu.StatusText.text);
        }

        [UnityTest]
        public IEnumerator MultiplayerTestSceneStartsHostAndConnectsLocalClient()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);
            Assert.That(hostSession.NetworkManager.NetworkConfig.PlayerPrefab, Is.Not.Null);
            Assert.That(hostSession.NetworkManager.NetworkConfig.NetworkTransport, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect to host.");

            ulong[] connectedClientIds = hostSession.NetworkManager.ConnectedClientsIds.OrderBy(id => id).ToArray();
            Assert.That(connectedClientIds, Has.Length.EqualTo(2));
            Assert.That(connectedClientIds.Distinct().Count(), Is.EqualTo(2));
            Assert.That(clientSession.NetworkManager.LocalClientId, Is.Not.EqualTo(hostSession.NetworkManager.LocalClientId));

            AssertPlayerObjectExists(hostSession.NetworkManager, hostSession.NetworkManager.LocalClientId);
            AssertPlayerObjectExists(hostSession.NetworkManager, clientSession.NetworkManager.LocalClientId);

            hostSession.StopSession();
            yield return WaitFor(
                () => !hostSession.NetworkManager.IsListening && !clientSession.NetworkManager.IsListening,
                "Host shutdown did not stop all local session managers.");

            Assert.That(hostSession.CurrentState, Is.EqualTo(BlockiverseConnectionState.Stopped));
            Assert.That(clientSession.CurrentState, Is.EqualTo(BlockiverseConnectionState.Disconnected));
            AssertNoSpawnedObjects(hostSession.NetworkManager);
            AssertNoSpawnedObjects(clientSession.NetworkManager);
        }

        [UnityTest]
        public IEnumerator ClientMenuShowsSessionEndedAndReconnectsAfterLanHostRestarts()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            BlockiverseMultiplayerSessionMenu hostMenu = UnityEngine.Object.FindFirstObjectByType<BlockiverseMultiplayerSessionMenu>();
            Assert.That(hostSession, Is.Not.Null);
            Assert.That(hostMenu, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseMultiplayerSessionMenu clientMenu = CreateSessionMenu("Client Session Menu", clientSession);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);
            clientMenu.AddressInput.text = BlockiverseNetworkConfig.DefaultAddress;

            hostMenu.HostButton.onClick.Invoke();
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host menu did not start host.");

            clientMenu.JoinButton.onClick.Invoke();
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client menu did not connect before host shutdown.");

            GameObject hiddenMenuRoot = CreateDisabledMenuRoot(clientMenu.transform);
            Canvas recoveryCanvas = hiddenMenuRoot.GetComponent<Canvas>();
            GraphicRaycaster recoveryRaycaster = hiddenMenuRoot.GetComponent<GraphicRaycaster>();

            hostMenu.StopButton.onClick.Invoke();
            yield return WaitFor(
                () => hostSession.CurrentState == BlockiverseConnectionState.Stopped &&
                      clientSession.CurrentState == BlockiverseConnectionState.Disconnected &&
                      !hostSession.NetworkManager.IsListening &&
                      !clientSession.NetworkManager.IsListening &&
                      !hostSession.NetworkManager.ShutdownInProgress &&
                      !clientSession.NetworkManager.ShutdownInProgress,
                "Host shutdown did not return the client to a disconnected menu state.");

            clientMenu.RefreshStatus();
            Assert.That(clientMenu.IsShowingSessionEndedMessage, Is.True);
            Assert.That(clientMenu.gameObject.activeInHierarchy, Is.True);
            Assert.That(recoveryCanvas.enabled, Is.True);
            Assert.That(recoveryRaycaster.enabled, Is.True);
            StringAssert.Contains("LAN session ended because the host disconnected", clientMenu.StatusText.text);
            StringAssert.Contains($"Use Join to reconnect to {BlockiverseNetworkConfig.DefaultAddress}:{port}", clientMenu.StatusText.text);
            string lowerStatusText = clientMenu.StatusText.text.ToLowerInvariant();
            Assert.That(lowerStatusText, Does.Not.Contain("matchmaking"));
            Assert.That(lowerStatusText, Does.Not.Contain("relay"));
            Assert.That(lowerStatusText, Does.Not.Contain("lobby"));
            Assert.That(clientMenu.JoinButton.interactable, Is.True);
            Assert.That(clientMenu.StopButton.interactable, Is.False);

            hostMenu.HostButton.onClick.Invoke();
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host menu did not restart host.");

            clientMenu.JoinButton.onClick.Invoke();
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      clientSession.CurrentState == BlockiverseConnectionState.ConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client menu did not reconnect after the LAN host restarted.");

            clientMenu.RefreshStatus();
            Assert.That(clientMenu.IsShowingSessionEndedMessage, Is.False);
            StringAssert.Contains("Connected to LAN session", clientMenu.StatusText.text);
        }

        [UnityTest]
        public IEnumerator ClientMenuShowsJoinFailedInsteadOfSessionEndedWhenHostUnavailable()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseMultiplayerSessionMenu clientMenu = CreateSessionMenu("Client Session Menu", clientSession);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            clientSession.Configure(testConfig);
            clientSession.UnityTransport.ConnectTimeoutMS = 50;
            clientSession.UnityTransport.MaxConnectAttempts = 1;
            clientMenu.AddressInput.text = BlockiverseNetworkConfig.DefaultAddress;

            LogAssert.Expect(LogType.Error, "Failed to connect to server.");
            clientMenu.JoinButton.onClick.Invoke();
            yield return WaitFor(
                () => clientSession.CurrentState == BlockiverseConnectionState.Disconnected &&
                      !clientSession.NetworkManager.IsListening &&
                      !clientSession.NetworkManager.ShutdownInProgress,
                "Client did not return to a disconnected state after joining an unavailable LAN host.",
                timeoutSeconds: 5.0f);

            clientMenu.RefreshStatus();
            Assert.That(clientMenu.IsShowingSessionEndedMessage, Is.False);
            StringAssert.Contains("Unable to reach LAN session", clientMenu.StatusText.text);
            Assert.That(clientMenu.StatusText.text, Does.Not.Contain("host disconnected"));
            Assert.That(clientMenu.JoinButton.interactable, Is.True);
            Assert.That(clientMenu.StopButton.interactable, Is.False);
        }

        [UnityTest]
        public IEnumerator ClientStopSessionDoesNotShowHostDisconnectedUx()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseMultiplayerSessionMenu clientMenu = CreateSessionMenu("Client Session Menu", clientSession);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect to host.");

            clientMenu.StopButton.onClick.Invoke();
            yield return WaitFor(
                () => clientSession.CurrentState == BlockiverseConnectionState.Stopped &&
                      !clientSession.NetworkManager.IsListening &&
                      !clientSession.NetworkManager.ShutdownInProgress,
                "Client stop did not finish as a local stopped session.");

            clientMenu.RefreshStatus();
            Assert.That(clientMenu.IsShowingSessionEndedMessage, Is.False);
            StringAssert.Contains("LAN session stopped", clientMenu.StatusText.text);
            Assert.That(clientMenu.StatusText.text, Does.Not.Contain("host disconnected"));
        }

        [UnityTest]
        public IEnumerator HostShutdownPersistsWorldBeforeClientDisconnectAndRestoresOnRestart()
        {
            yield return LoadMultiplayerTestScene();

            string savePath = CreateTempSavePath();
            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager worldManager = CreateCreativeWorldManager("Host Save World");
            MultiplayerWorldPersistence persistence = ConfigurePersistence(hostSession, worldManager, savePath);
            var editPosition = new BlockPosition(2, 2, 2);
            var restartEditPosition = new BlockPosition(3, 2, 2);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);
            bool clientWasListeningDuringShutdownPreparation = false;

            bool CaptureShutdownPreparation(out string failureReason)
            {
                failureReason = string.Empty;
                clientWasListeningDuringShutdownPreparation = clientSession.NetworkManager.IsListening;
                return true;
            }

            try
            {
                hostSession.Configure(testConfig);
                clientSession.Configure(testConfig);
                hostSession.HostShutdownPreparing += CaptureShutdownPreparation;

                Assert.That(hostSession.StartHost(), Is.True);
                yield return WaitFor(
                    () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                    "Host did not start.");

                Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
                yield return WaitFor(
                    () => clientSession.NetworkManager.IsConnectedClient &&
                          hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                    "Client did not connect to host.");

                worldManager.World.SetBlock(editPosition, BlockRegistry.Clearstone);
                hostSession.StopSession();

                Assert.That(hostSession.LastStopRequestSucceeded, Is.True);
                Assert.That(persistence.LastShutdownSaveAttempted, Is.True);
                Assert.That(persistence.LastShutdownSaveSucceeded, Is.True);
                Assert.That(clientWasListeningDuringShutdownPreparation, Is.True);

                yield return WaitFor(
                    () => hostSession.CurrentState == BlockiverseConnectionState.Stopped &&
                          clientSession.CurrentState == BlockiverseConnectionState.Disconnected &&
                          !hostSession.NetworkManager.IsListening &&
                          !clientSession.NetworkManager.IsListening,
                    "Host shutdown did not stop after saving the world.");

                Assert.That(File.Exists(savePath), Is.True);

                worldManager.World.SetBlock(editPosition, BlockRegistry.Air);

                Assert.That(hostSession.StartHost(), Is.True);
                yield return WaitFor(
                    () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                    "Host did not restart.");

                Assert.That(persistence.LastHostLoadAttempted, Is.True);
                Assert.That(persistence.LastHostLoadSucceeded, Is.True);
                Assert.That(worldManager.World.GetBlock(editPosition), Is.EqualTo(BlockRegistry.Clearstone));

                worldManager.World.SetBlock(restartEditPosition, BlockRegistry.Loam);
                hostSession.StopSession();

                Assert.That(hostSession.LastStopRequestSucceeded, Is.True);
                Assert.That(persistence.LastShutdownSaveAttempted, Is.True);
                Assert.That(persistence.LastShutdownSaveSucceeded, Is.True);

                yield return WaitFor(
                    () => hostSession.CurrentState == BlockiverseConnectionState.Stopped &&
                          !hostSession.NetworkManager.IsListening,
                    "Restarted host did not stop after saving the world.");

                worldManager.World.SetBlock(editPosition, BlockRegistry.Air);
                worldManager.World.SetBlock(restartEditPosition, BlockRegistry.Air);

                Assert.That(hostSession.StartHost(), Is.True);
                yield return WaitFor(
                    () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                    "Host did not restart after the second shutdown save.");

                Assert.That(worldManager.World.GetBlock(editPosition), Is.EqualTo(BlockRegistry.Clearstone));
                Assert.That(worldManager.World.GetBlock(restartEditPosition), Is.EqualTo(BlockRegistry.Loam));
            }
            finally
            {
                hostSession.HostShutdownPreparing -= CaptureShutdownPreparation;
                DeleteIfExists(savePath);
            }
        }

        [UnityTest]
        public IEnumerator HostShutdownSaveFailureAbortsShutdownAndKeepsClientsConnected()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseMultiplayerSessionMenu hostMenu = UnityEngine.Object.FindFirstObjectByType<BlockiverseMultiplayerSessionMenu>();
            Assert.That(hostMenu, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager worldManager = CreateCreativeWorldManager("Host Save Failure World");
            MultiplayerWorldPersistence persistence = ConfigurePersistence(hostSession, worldManager, "invalid\0save");
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect to host.");

            LogAssert.Expect(
                LogType.Error,
                new Regex(@"\[Blockiverse\]\[Persistence\] Failed to save multiplayer host world before shutdown.*exception=(ArgumentException|NotSupportedException)"));

            hostMenu.StopButton.onClick.Invoke();
            yield return null;

            Assert.That(hostSession.LastStopRequestSucceeded, Is.False);
            Assert.That(hostSession.CurrentState, Is.EqualTo(BlockiverseConnectionState.Hosting));
            Assert.That(hostSession.NetworkManager.IsListening, Is.True);
            Assert.That(clientSession.NetworkManager.IsListening, Is.True);
            Assert.That(clientSession.NetworkManager.IsConnectedClient, Is.True);
            Assert.That(persistence.LastShutdownSaveAttempted, Is.True);
            Assert.That(persistence.LastShutdownSaveSucceeded, Is.False);
            StringAssert.Contains("Unable to save multiplayer world before host shutdown", hostSession.LastDisconnectReason);
            hostMenu.RefreshStatus();
            StringAssert.Contains("Unable to stop LAN session", hostMenu.StatusText.text);
            StringAssert.Contains("Unable to save multiplayer world before host shutdown", hostMenu.StatusText.text);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManager[] managers = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);

            foreach (NetworkManager manager in managers)
            {
                if (manager != null && (manager.IsListening || manager.ShutdownInProgress))
                    manager.Shutdown();
            }

            yield return WaitFor(
                () => UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)
                    .All(manager => manager == null || !manager.IsListening),
                "Network managers did not stop during test cleanup.",
                timeoutSeconds: 3.0f);

            foreach (NetworkManager manager in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None))
            {
                if (manager != null)
                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }

            foreach (string tempSavePath in TempSavePaths)
                DeleteIfExists(tempSavePath);

            TempSavePaths.Clear();
        }

        static IEnumerator LoadMultiplayerTestScene()
        {
            string sceneName = Path.GetFileNameWithoutExtension(BlockiverseProject.MultiplayerTestScenePath);
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            CreativeWorldManager worldManager = UnityEngine.Object.FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);

            if (hostSession != null && worldManager != null)
                ConfigurePersistence(hostSession, worldManager, CreateTempSavePath());
        }

        static BlockiverseNetworkSession CreateClientSession(BlockiverseNetworkSession hostSession)
        {
            GameObject clientObject = UnityEngine.Object.Instantiate(hostSession.gameObject);
            clientObject.name = "Blockiverse Network Client";
            BlockiverseNetworkSession clientSession = clientObject.GetComponent<BlockiverseNetworkSession>();

            Assert.That(clientSession, Is.Not.Null);
            Assert.That(clientSession.NetworkManager, Is.Not.SameAs(hostSession.NetworkManager));
            return clientSession;
        }

        static MultiplayerWorldPersistence ConfigurePersistence(
            BlockiverseNetworkSession session,
            CreativeWorldManager worldManager,
            string savePath)
        {
            MultiplayerWorldPersistence persistence = session.GetComponent<MultiplayerWorldPersistence>();

            if (persistence == null)
                persistence = session.gameObject.AddComponent<MultiplayerWorldPersistence>();

            persistence.Configure(session, worldManager, savePath, "playmode-multiplayer");
            return persistence;
        }

        static CreativeWorldManager CreateCreativeWorldManager(string name)
        {
            GameObject worldObject = new(name);
            worldObject.SetActive(false);
            CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
            manager.Configure(CreateBlockAtlasMaterial(), -1);
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 16,
                height: 8,
                depth: 16,
                chunkSize: 16,
                seed: 9901,
                groundHeight: 2);
            VoxelWorld world = new FlatCreativeWorldPreset(registry, settings).Generate();
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world));
            return manager;
        }

        static Material CreateBlockAtlasMaterial()
        {
            var atlasTexture = new Texture2D(
                BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels,
                BlockVisualAtlas.Rows * BlockVisualAtlas.TilePixels,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = BlockVisualAtlas.AuthoredAtlasName
            };

            Material material = new(Shader.Find("Sprites/Default"));
            material.mainTexture = atlasTexture;
            return material;
        }

        static BlockiverseMultiplayerSessionMenu CreateSessionMenu(string name, BlockiverseNetworkSession session)
        {
            GameObject menuObject = new(name);
            BlockiverseMultiplayerSessionMenu menu = menuObject.AddComponent<BlockiverseMultiplayerSessionMenu>();
            Button hostButton = CreateButton("Host Button", menuObject.transform);
            Button joinButton = CreateButton("Join Button", menuObject.transform);
            Button stopButton = CreateButton("Stop Button", menuObject.transform);
            InputField addressInput = CreateInputField("Address Input", menuObject.transform);
            Text statusText = CreateText("Status", menuObject.transform);

            menu.Configure(session);
            menu.ConfigureControls(hostButton, joinButton, stopButton, addressInput, statusText);
            return menu;
        }

        static GameObject CreateDisabledMenuRoot(Transform child)
        {
            GameObject rootObject = new("Disabled Menu Root", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = rootObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.enabled = false;
            rootObject.GetComponent<GraphicRaycaster>().enabled = false;
            child.SetParent(rootObject.transform, false);
            return rootObject;
        }

        static Button CreateButton(string name, Transform parent)
        {
            GameObject buttonObject = new(name, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.AddComponent<Button>();
        }

        static InputField CreateInputField(string name, Transform parent)
        {
            GameObject inputObject = new(name, typeof(RectTransform));
            inputObject.transform.SetParent(parent, false);
            InputField input = inputObject.AddComponent<InputField>();
            input.textComponent = CreateText("Text", inputObject.transform);
            return input;
        }

        static Text CreateText(string name, Transform parent)
        {
            GameObject textObject = new(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            return textObject.AddComponent<Text>();
        }

        static ushort NextPort()
        {
            return nextPort++;
        }

        static string CreateTempSavePath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"blockiverse-multiplayer-{Guid.NewGuid():N}.json");
            TempSavePaths.Add(path);
            return path;
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        static IEnumerator WaitFor(Func<bool> condition, string failureMessage, float timeoutSeconds = 5.0f)
        {
            float timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;

            while (!condition() && Time.realtimeSinceStartup < timeoutAt)
                yield return null;

            Assert.That(condition(), Is.True, failureMessage);
        }

        static void AssertPlayerObjectExists(NetworkManager networkManager, ulong clientId)
        {
            Assert.That(networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client), Is.True);
            Assert.That(client.PlayerObject, Is.Not.Null);
            Assert.That(client.PlayerObject.OwnerClientId, Is.EqualTo(clientId));
        }

        static void AssertNoSpawnedObjects(NetworkManager networkManager)
        {
            if (networkManager.SpawnManager == null)
                return;

            Assert.That(networkManager.SpawnManager.SpawnedObjectsList.Count, Is.Zero);
        }

        static void AssertSceneHasUiInputSystem()
        {
            EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

            Assert.That(eventSystems, Has.Length.EqualTo(1));
            Assert.That(eventSystems[0].GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
        }
    }
}
