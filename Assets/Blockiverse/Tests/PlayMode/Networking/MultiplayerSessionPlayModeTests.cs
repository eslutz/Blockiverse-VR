using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using Blockiverse.UI;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TMPro;
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
            AssertFallbackAvatarExists(hostSession.NetworkManager, hostSession.NetworkManager.LocalClientId);
            AssertFallbackAvatarExists(hostSession.NetworkManager, clientSession.NetworkManager.LocalClientId);
            AssertFallbackAvatarExists(clientSession.NetworkManager, hostSession.NetworkManager.LocalClientId);
            AssertFallbackAvatarExists(clientSession.NetworkManager, clientSession.NetworkManager.LocalClientId);

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
        public IEnumerator FallbackAvatarPoseSyncsBetweenOwnersAndRemoteCopies()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

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

            BlockiverseNetworkAvatarRig hostOwnerAvatar = GetPlayerObject(
                hostSession.NetworkManager,
                hostSession.NetworkManager.LocalClientId).GetComponent<BlockiverseNetworkAvatarRig>();
            BlockiverseNetworkAvatarRig clientCopyOfHostAvatar = GetPlayerObject(
                clientSession.NetworkManager,
                hostSession.NetworkManager.LocalClientId).GetComponent<BlockiverseNetworkAvatarRig>();
            BlockiverseNetworkAvatarRig clientOwnerAvatar = GetPlayerObject(
                clientSession.NetworkManager,
                clientSession.NetworkManager.LocalClientId).GetComponent<BlockiverseNetworkAvatarRig>();
            BlockiverseNetworkAvatarRig hostCopyOfClientAvatar = GetPlayerObject(
                hostSession.NetworkManager,
                clientSession.NetworkManager.LocalClientId).GetComponent<BlockiverseNetworkAvatarRig>();

            Assert.That(hostOwnerAvatar, Is.Not.Null);
            Assert.That(clientCopyOfHostAvatar, Is.Not.Null);
            Assert.That(clientOwnerAvatar, Is.Not.Null);
            Assert.That(hostCopyOfClientAvatar, Is.Not.Null);

            GameObject clientHeadSource = CreateTrackingSource(
                "Client Tracked Head",
                new Vector3(1.25f, 1.68f, -0.5f),
                Quaternion.Euler(0.0f, 35.0f, 0.0f));
            GameObject clientLeftSource = CreateTrackingSource(
                "Client Tracked Left Hand",
                new Vector3(0.85f, 1.12f, -0.25f),
                Quaternion.Euler(0.0f, -15.0f, -20.0f));
            GameObject clientRightSource = CreateTrackingSource(
                "Client Tracked Right Hand",
                new Vector3(1.62f, 1.15f, -0.18f),
                Quaternion.Euler(0.0f, 15.0f, 20.0f));
            GameObject hostHeadSource = CreateTrackingSource(
                "Host Tracked Head",
                new Vector3(-0.7f, 1.72f, 0.45f),
                Quaternion.Euler(4.0f, -28.0f, 0.0f));
            GameObject hostLeftSource = CreateTrackingSource(
                "Host Tracked Left Hand",
                new Vector3(-1.08f, 1.18f, 0.62f),
                Quaternion.Euler(2.0f, -44.0f, -18.0f));
            GameObject hostRightSource = CreateTrackingSource(
                "Host Tracked Right Hand",
                new Vector3(-0.35f, 1.14f, 0.64f),
                Quaternion.Euler(2.0f, 6.0f, 18.0f));
            var clientRootPose = new Pose(new Vector3(1.0f, 0.0f, -0.6f), Quaternion.Euler(0.0f, 20.0f, 0.0f));
            var hostRootPose = new Pose(new Vector3(-0.8f, 0.0f, 0.5f), Quaternion.Euler(0.0f, -15.0f, 0.0f));

            try
            {
                clientOwnerAvatar.transform.SetPositionAndRotation(clientRootPose.position, clientRootPose.rotation);
                hostOwnerAvatar.transform.SetPositionAndRotation(hostRootPose.position, hostRootPose.rotation);
                clientOwnerAvatar.ConfigureTrackingSources(
                    clientHeadSource.transform,
                    clientLeftSource.transform,
                    clientRightSource.transform);
                hostOwnerAvatar.ConfigureTrackingSources(
                    hostHeadSource.transform,
                    hostLeftSource.transform,
                    hostRightSource.transform);

                Pose expectedClientHead = ExpectedLocalPose(clientOwnerAvatar.transform, clientHeadSource.transform);
                Pose expectedClientLeftHand = ExpectedLocalPose(clientOwnerAvatar.transform, clientLeftSource.transform);
                Pose expectedClientRightHand = ExpectedLocalPose(clientOwnerAvatar.transform, clientRightSource.transform);
                Pose expectedHostHead = ExpectedLocalPose(hostOwnerAvatar.transform, hostHeadSource.transform);
                Pose expectedHostLeftHand = ExpectedLocalPose(hostOwnerAvatar.transform, hostLeftSource.transform);
                Pose expectedHostRightHand = ExpectedLocalPose(hostOwnerAvatar.transform, hostRightSource.transform);

                yield return WaitFor(
                    () => AvatarPoseMatches(
                              hostCopyOfClientAvatar,
                              clientRootPose,
                              expectedClientHead,
                              expectedClientLeftHand,
                              expectedClientRightHand) &&
                          AvatarPoseMatches(
                              clientCopyOfHostAvatar,
                              hostRootPose,
                              expectedHostHead,
                              expectedHostLeftHand,
                              expectedHostRightHand),
                    "Fallback proxy avatar poses did not synchronize between owners and remote copies.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clientHeadSource);
                UnityEngine.Object.DestroyImmediate(clientLeftSource);
                UnityEngine.Object.DestroyImmediate(clientRightSource);
                UnityEngine.Object.DestroyImmediate(hostHeadSource);
                UnityEngine.Object.DestroyImmediate(hostLeftSource);
                UnityEngine.Object.DestroyImmediate(hostRightSource);
            }
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

                worldManager.World.SetBlock(editPosition, BlockRegistry.LumenQuartzCluster);
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

                Assert.That(Directory.Exists(savePath), Is.True);

                worldManager.World.SetBlock(editPosition, BlockRegistry.Air);

                Assert.That(hostSession.StartHost(), Is.True);
                yield return WaitFor(
                    () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                    "Host did not restart.");

                Assert.That(persistence.LastHostLoadAttempted, Is.True);
                Assert.That(persistence.LastHostLoadSucceeded, Is.True);
                Assert.That(worldManager.World.GetBlock(editPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));

                worldManager.World.SetBlock(restartEditPosition, BlockRegistry.LooseLoam);
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

                Assert.That(worldManager.World.GetBlock(editPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
                Assert.That(worldManager.World.GetBlock(restartEditPosition), Is.EqualTo(BlockRegistry.LooseLoam));
            }
            finally
            {
                hostSession.HostShutdownPreparing -= CaptureShutdownPreparation;
                DeleteIfExists(savePath);
            }
        }

        [UnityTest]
        public IEnumerator HostStartRejectsSavedWorldThatDoesNotMatchInitializedWorld()
        {
            yield return LoadMultiplayerTestScene();

            string savePath = CreateTempSavePath();
            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            CreativeWorldManager worldManager = CreateCreativeWorldManager("Host Mismatched Save World");
            MultiplayerWorldPersistence persistence = ConfigurePersistence(hostSession, worldManager, savePath);
            VoxelWorld savedWorld = new VoxelWorld(new WorldBounds(4, 4, 4), chunkSize: 4, seed: 2026);
            savedWorld.SetBlock(new BlockPosition(3, 3, 3), BlockRegistry.LumenQuartzCluster);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            try
            {
                new WorldSaveService().Save(savePath, "mismatched-save", savedWorld);
                hostSession.Configure(testConfig);

                Assert.That(hostSession.StartHost(), Is.False);
                Assert.That(persistence.LastHostLoadAttempted, Is.True);
                Assert.That(persistence.LastHostLoadSucceeded, Is.False);
                Assert.That(persistence.LastFailureReason, Does.Contain("does not match the initialized host world"));
                Assert.That(worldManager.World.Bounds.Width, Is.EqualTo(16));
                Assert.That(worldManager.World.Bounds.Height, Is.EqualTo(8));
                Assert.That(worldManager.World.Bounds.Depth, Is.EqualTo(16));
            }
            finally
            {
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

        [UnityTest]
        public IEnumerator ClientBlockMutationRequestsAreHostValidatedBroadcastAndLateJoinSynced()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseNetworkSession observerClientSession = CreateClientSession(hostSession);
            BlockiverseNetworkSession lateJoinClientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Chunk Authority World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Chunk Authority World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 1220, groundHeight: 2));
            CreativeWorldManager observerWorldManager = CreateCreativeWorldManager(
                "Observer Chunk Authority World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 1219, groundHeight: 2));
            CreativeWorldManager lateJoinWorldManager = CreateCreativeWorldManager(
                "Late Join Chunk Authority World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 1221, groundHeight: 2));
            MultiplayerChunkAuthoritySync hostSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerChunkAuthoritySync observerSync = ConfigureChunkSync(observerClientSession, observerWorldManager);
            MultiplayerChunkAuthoritySync lateJoinSync = ConfigureChunkSync(lateJoinClientSession, lateJoinWorldManager);
            var editPosition = new BlockPosition(2, 2, 2);
            var stalePosition = new BlockPosition(3, 2, 2);
            var postLateJoinPosition = new BlockPosition(4, 2, 2);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);
            observerClientSession.Configure(testConfig);
            lateJoinClientSession.Configure(testConfig);
            hostWorldManager.World.SetBlock(stalePosition, BlockRegistry.LooseLoam);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for chunk authority sync.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for chunk authority sync.");

            Assert.That(observerClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => observerClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 3,
                "Observer client did not connect for chunk authority sync.");

            yield return WaitFor(
                () => clientSync.AppliedGenerationSnapshotCount >= 1 &&
                      clientSync.HasHostGenerationSnapshotForSession &&
                      clientWorldManager.World.Bounds == hostWorldManager.World.Bounds &&
                      clientWorldManager.World.Seed == hostWorldManager.World.Seed &&
                      clientWorldManager.World.GetBlock(stalePosition) == BlockRegistry.LooseLoam &&
                      observerSync.AppliedGenerationSnapshotCount >= 1 &&
                      observerSync.HasHostGenerationSnapshotForSession &&
                      observerWorldManager.World.Bounds == hostWorldManager.World.Bounds &&
                      observerWorldManager.World.Seed == hostWorldManager.World.Seed &&
                      observerWorldManager.World.GetBlock(stalePosition) == BlockRegistry.LooseLoam,
                "Connected clients did not replace local generation with the host-owned world snapshot.");

            BlockMutationResult requestResult = clientSync.TrySubmitMutation(
                editPosition,
                BlockRegistry.LumenQuartzCluster,
                out SetBlockCommand clientCommand,
                out bool requestSentToHost);

            Assert.That(requestSentToHost, Is.True);
            Assert.That(requestResult.PendingHostValidation, Is.True);
            Assert.That(requestResult.RpcRequestId, Is.EqualTo(1));
            Assert.That(clientCommand, Is.Null);
            Assert.That(clientSync.LastSentMutationRequestId, Is.EqualTo(1));
            Assert.That(clientSync.PendingMutationRequestCount, Is.EqualTo(1));
            Assert.That(clientWorldManager.World.GetBlock(editPosition), Is.EqualTo(BlockRegistry.Air));

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster &&
                      clientWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster &&
                      observerWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster,
                "Host did not validate and broadcast the client block mutation.");

            Assert.That(hostSync.CurrentBoundary.OwnsMutationValidation, Is.True);
            Assert.That(hostSync.CurrentBoundary.CanBroadcastDeltas, Is.True);
            Assert.That(clientSync.CurrentBoundary.MustRequestMutations, Is.True);
            Assert.That(clientSync.CurrentBoundary.CanBroadcastDeltas, Is.False);
            Assert.That(hostSync.ReceivedMutationRequestCount, Is.EqualTo(1));
            Assert.That(hostSync.LastReceivedMutationRequestId, Is.EqualTo(1));
            Assert.That(hostSync.BroadcastDeltaCount, Is.EqualTo(1));
            Assert.That(hostSync.RecordedChunkDeltas, Has.Count.EqualTo(1));
            Assert.That(hostSync.LastBroadcastChunkDeltaSequence, Is.EqualTo(1));
            Assert.That(hostSync.RecordedChunkDeltas[0].SequenceId, Is.EqualTo(1));
            Assert.That(hostSync.RecordedChunkDeltas[0].Chunk, Is.EqualTo(new ChunkCoordinate(0, 0, 0)));
            Assert.That(hostSync.RecordedChunkDeltas[0].Change.Position, Is.EqualTo(editPosition));
            Assert.That(hostSync.RecordedChunkDeltas[0].Change.NewBlock, Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(clientSync.SentMutationRequestCount, Is.EqualTo(1));
            Assert.That(clientSync.AppliedRemoteDeltaCount, Is.EqualTo(1));
            Assert.That(clientSync.AppliedChunkDeltaCount, Is.EqualTo(1));
            Assert.That(clientSync.LastAppliedChunkDeltaSequence, Is.EqualTo(1));
            Assert.That(clientSync.AcceptedMutationResponseCount, Is.EqualTo(1));
            Assert.That(clientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(clientSync.LastCompletedMutationRequestId, Is.EqualTo(1));
            Assert.That(clientSync.LastMutationResult.RpcRequestId, Is.EqualTo(1));
            Assert.That(observerSync.SentMutationRequestCount, Is.Zero);
            Assert.That(observerSync.AppliedRemoteDeltaCount, Is.EqualTo(1));
            Assert.That(observerSync.AppliedChunkDeltaCount, Is.EqualTo(1));
            Assert.That(observerSync.LastAppliedChunkDeltaSequence, Is.EqualTo(1));
            Assert.That(observerSync.AcceptedMutationResponseCount, Is.Zero);
            Assert.That(observerSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(observerSync.LastMutationResult.RpcRequestId, Is.Zero);

            BlockMutationResult rejectedRequest = clientSync.TrySubmitMutation(
                new BlockPosition(-1, 2, 2),
                BlockRegistry.Graystone,
                out SetBlockCommand rejectedClientCommand,
                out bool rejectedRequestSentToHost);

            Assert.That(rejectedRequestSentToHost, Is.True);
            Assert.That(rejectedRequest.PendingHostValidation, Is.True);
            Assert.That(rejectedRequest.RpcRequestId, Is.EqualTo(2));
            Assert.That(rejectedClientCommand, Is.Null);
            Assert.That(clientSync.PendingMutationRequestCount, Is.EqualTo(1));

            yield return WaitFor(
                () => clientSync.LastMutationResult.RejectionReason == BlockMutationRejectionReason.PositionOutOfBounds,
                "Host did not report deterministic rejection for an invalid client mutation request.");

            Assert.That(clientSync.ReceivedMutationRejectionCount, Is.EqualTo(1));
            Assert.That(clientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(clientSync.LastCompletedMutationRequestId, Is.EqualTo(2));
            Assert.That(clientSync.LastMutationResult.RpcRequestId, Is.EqualTo(2));

            clientWorldManager.World.SetBlock(stalePosition, BlockRegistry.Air, trackChange: false);
            BlockMutationResult staleRequest = clientSync.TrySubmitMutation(
                stalePosition,
                BlockRegistry.Graystone,
                out SetBlockCommand staleClientCommand,
                out bool staleRequestSentToHost);

            Assert.That(staleRequestSentToHost, Is.True);
            Assert.That(staleRequest.PendingHostValidation, Is.True);
            Assert.That(staleRequest.RpcRequestId, Is.EqualTo(3));
            Assert.That(staleClientCommand, Is.Null);

            yield return WaitFor(
                () => clientSync.LastMutationResult.RejectionReason == BlockMutationRejectionReason.ExpectedBlockMismatch &&
                      clientWorldManager.World.GetBlock(stalePosition) == BlockRegistry.LooseLoam,
                "Host did not reject and correct a stale client mutation request.");

            Assert.That(clientSync.ReceivedMutationRejectionCount, Is.EqualTo(2));
            Assert.That(hostSync.LastReceivedMutationRequestId, Is.EqualTo(3));
            Assert.That(clientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(clientSync.LastCompletedMutationRequestId, Is.EqualTo(3));
            Assert.That(clientSync.LastMutationResult.RpcRequestId, Is.EqualTo(3));

            Assert.That(lateJoinClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => lateJoinClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 4,
                "Late join client did not connect for chunk authority sync.");

            yield return WaitFor(
                () => lateJoinWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster,
                "Late join client did not receive the host chunk snapshot.");

            Assert.That(hostSync.CurrentBoundary.CanServeLateJoinSync, Is.True);
            Assert.That(hostSync.SentLateJoinSnapshotCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(lateJoinSync.HasHostGenerationSnapshotForSession, Is.True);
            Assert.That(lateJoinSync.AppliedGenerationSnapshotCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(lateJoinSync.AppliedSnapshotBlockCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(lateJoinSync.LastAppliedChunkDeltaSequence, Is.EqualTo(hostSync.LastBroadcastChunkDeltaSequence));
            Assert.That(lateJoinWorldManager.World.Bounds, Is.EqualTo(hostWorldManager.World.Bounds));
            Assert.That(lateJoinWorldManager.World.Seed, Is.EqualTo(hostWorldManager.World.Seed));
            Assert.That(lateJoinWorldManager.GenerationPreset, Is.EqualTo(hostWorldManager.GenerationPreset));

            BlockMutationResult postLateJoinRequest = clientSync.TrySubmitMutation(
                postLateJoinPosition,
                BlockRegistry.Graystone,
                out SetBlockCommand postLateJoinClientCommand,
                out bool postLateJoinRequestSentToHost);

            Assert.That(postLateJoinRequestSentToHost, Is.True);
            Assert.That(postLateJoinRequest.PendingHostValidation, Is.True);
            Assert.That(postLateJoinRequest.RpcRequestId, Is.EqualTo(4));
            Assert.That(postLateJoinClientCommand, Is.Null);
            Assert.That(clientSync.PendingMutationRequestCount, Is.EqualTo(1));

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(postLateJoinPosition) == BlockRegistry.Graystone &&
                      clientWorldManager.World.GetBlock(postLateJoinPosition) == BlockRegistry.Graystone &&
                      observerWorldManager.World.GetBlock(postLateJoinPosition) == BlockRegistry.Graystone &&
                      lateJoinWorldManager.World.GetBlock(postLateJoinPosition) == BlockRegistry.Graystone,
                "Late join client did not remain synchronized with subsequent host chunk deltas.");

            Assert.That(hostSync.BroadcastDeltaCount, Is.EqualTo(2));
            Assert.That(hostSync.RecordedChunkDeltas, Has.Count.EqualTo(2));
            Assert.That(hostSync.LastBroadcastChunkDeltaSequence, Is.EqualTo(2));
            Assert.That(hostSync.RecordedChunkDeltas[1].SequenceId, Is.EqualTo(2));
            Assert.That(
                hostSync.RecordedChunkDeltas[1].Chunk,
                Is.EqualTo(ChunkCoordinate.FromBlockPosition(postLateJoinPosition, hostWorldManager.World.ChunkSize)));
            Assert.That(hostSync.RecordedChunkDeltas[1].Change.Position, Is.EqualTo(postLateJoinPosition));
            Assert.That(hostSync.RecordedChunkDeltas[1].Change.NewBlock, Is.EqualTo(BlockRegistry.Graystone));
            Assert.That(clientSync.AppliedRemoteDeltaCount, Is.EqualTo(2));
            Assert.That(clientSync.AppliedChunkDeltaCount, Is.EqualTo(2));
            Assert.That(clientSync.AcceptedMutationResponseCount, Is.EqualTo(2));
            Assert.That(clientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(clientSync.LastCompletedMutationRequestId, Is.EqualTo(4));
            Assert.That(clientSync.LastAppliedChunkDeltaSequence, Is.EqualTo(2));
            Assert.That(observerSync.AppliedRemoteDeltaCount, Is.EqualTo(2));
            Assert.That(observerSync.AppliedChunkDeltaCount, Is.EqualTo(2));
            Assert.That(observerSync.LastAppliedChunkDeltaSequence, Is.EqualTo(2));
            Assert.That(lateJoinSync.AppliedRemoteDeltaCount, Is.EqualTo(1));
            Assert.That(lateJoinSync.AppliedChunkDeltaCount, Is.EqualTo(1));
            Assert.That(lateJoinSync.AcceptedMutationResponseCount, Is.Zero);
            Assert.That(lateJoinSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(lateJoinSync.LastAppliedChunkDeltaSequence, Is.EqualTo(2));

            clientSession.StopSession();
            yield return WaitFor(
                () => !clientSession.NetworkManager.IsListening,
                "Client did not stop after chunk authority sync validation.");
            Assert.That(clientSync.HasHostGenerationSnapshotForSession, Is.False);
            Assert.That(clientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(clientSync.LastSentMutationRequestId, Is.Zero);
            Assert.That(clientSync.LastCompletedMutationRequestId, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CompetingClientBlockMutationsRejectStaleRequestAndPreserveAuthoritativeWinner()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession firstClientSession = CreateClientSession(hostSession);
            BlockiverseNetworkSession competingClientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Conflict World");
            CreativeWorldManager firstClientWorldManager = CreateCreativeWorldManager(
                "First Client Conflict World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 1222, groundHeight: 2));
            CreativeWorldManager competingClientWorldManager = CreateCreativeWorldManager(
                "Competing Client Conflict World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 1223, groundHeight: 2));
            MultiplayerChunkAuthoritySync hostSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync firstClientSync = ConfigureChunkSync(firstClientSession, firstClientWorldManager);
            MultiplayerChunkAuthoritySync competingClientSync = ConfigureChunkSync(competingClientSession, competingClientWorldManager);
            var conflictPosition = new BlockPosition(2, 2, 2);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            firstClientSession.Configure(testConfig);
            competingClientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for conflict handling.");

            Assert.That(firstClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => firstClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "First client did not connect for conflict handling.");

            Assert.That(competingClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => competingClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 3,
                "Competing client did not connect for conflict handling.");

            yield return WaitFor(
                () => firstClientSync.HasHostGenerationSnapshotForSession &&
                      competingClientSync.HasHostGenerationSnapshotForSession,
                "Clients did not receive host generation snapshots for conflict handling.");

            BlockMutationResult winningRequest = firstClientSync.TrySubmitMutation(
                new BlockMutationRequest(
                    firstClientSync.CurrentBoundary.LocalClientId,
                    conflictPosition,
                    BlockRegistry.LumenQuartzCluster,
                    BlockRegistry.Air),
                out SetBlockCommand firstClientCommand,
                out bool winningRequestSentToHost);

            Assert.That(winningRequestSentToHost, Is.True);
            Assert.That(winningRequest.PendingHostValidation, Is.True);
            Assert.That(firstClientCommand, Is.Null);

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(conflictPosition) == BlockRegistry.LumenQuartzCluster &&
                      firstClientWorldManager.World.GetBlock(conflictPosition) == BlockRegistry.LumenQuartzCluster &&
                      competingClientWorldManager.World.GetBlock(conflictPosition) == BlockRegistry.LumenQuartzCluster,
                "Winning competing mutation did not converge before stale conflict request.");

            BlockMutationResult staleCompetingRequest = competingClientSync.TrySubmitMutation(
                new BlockMutationRequest(
                    competingClientSync.CurrentBoundary.LocalClientId,
                    conflictPosition,
                    BlockRegistry.Graystone,
                    BlockRegistry.Air),
                out SetBlockCommand competingClientCommand,
                out bool staleRequestSentToHost);

            Assert.That(staleRequestSentToHost, Is.True);
            Assert.That(staleCompetingRequest.PendingHostValidation, Is.True);
            Assert.That(competingClientCommand, Is.Null);

            yield return WaitFor(
                () => competingClientSync.LastMutationResult.RejectionReason == BlockMutationRejectionReason.ExpectedBlockMismatch,
                "Host did not reject stale competing mutation deterministically.");

            Assert.That(hostSync.ReceivedMutationRequestCount, Is.EqualTo(2));
            Assert.That(hostSync.BroadcastDeltaCount, Is.EqualTo(1));
            Assert.That(hostSync.ConflictRejectedMutationCount, Is.EqualTo(1));
            Assert.That(competingClientSync.ReceivedMutationRejectionCount, Is.EqualTo(1));
            Assert.That(competingClientSync.PendingMutationRequestCount, Is.Zero);
            Assert.That(hostWorldManager.World.GetBlock(conflictPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(firstClientWorldManager.World.GetBlock(conflictPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
            Assert.That(competingClientWorldManager.World.GetBlock(conflictPosition), Is.EqualTo(BlockRegistry.LumenQuartzCluster));
        }

        [UnityTest]
        public IEnumerator ActiveBlockEditingConvergesUnderSimulated100MsLatency()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Latency World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Latency World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 5100, groundHeight: 2));
            MultiplayerChunkAuthoritySync hostSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientSync = ConfigureChunkSync(clientSession, clientWorldManager);
            var editPosition = new BlockPosition(2, 2, 2);
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
                "Host did not start for latency simulation.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for latency simulation.");

            yield return WaitFor(
                () => clientSync.HasHostGenerationSnapshotForSession,
                "Client did not receive host generation snapshot before latency simulation.");

            SimulatorUtility.Parameters hostSimulator = ApplyTransportSimulator(hostSession, packetDelayMs: 100, packetDropInterval: 0, randomSeed: 5100);
            SimulatorUtility.Parameters clientSimulator = ApplyTransportSimulator(clientSession, packetDelayMs: 100, packetDropInterval: 0, randomSeed: 5101);

            Assert.That(hostSimulator.PacketDelayMs, Is.EqualTo(100));
            Assert.That(clientSimulator.PacketDelayMs, Is.EqualTo(100));

            BlockMutationResult requestResult = clientSync.TrySubmitMutation(
                new BlockMutationRequest(
                    clientSync.CurrentBoundary.LocalClientId,
                    editPosition,
                    BlockRegistry.LumenQuartzCluster,
                    BlockRegistry.Air),
                out SetBlockCommand clientCommand,
                out bool requestSentToHost);

            Assert.That(requestSentToHost, Is.True);
            Assert.That(requestResult.PendingHostValidation, Is.True);
            Assert.That(clientCommand, Is.Null);

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster &&
                      clientWorldManager.World.GetBlock(editPosition) == BlockRegistry.LumenQuartzCluster &&
                      clientSync.PendingMutationRequestCount == 0,
                "Active block edit did not converge under simulated 100ms latency.",
                timeoutSeconds: 8.0f);

            Assert.That(hostSync.ReceivedMutationRequestCount, Is.EqualTo(1));
            Assert.That(hostSync.BroadcastDeltaCount, Is.EqualTo(1));
            Assert.That(clientSync.AcceptedMutationResponseCount, Is.EqualTo(1));
            Assert.That(clientSync.LastAppliedChunkDeltaSequence, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ChunkDeltasConvergeUnderSimulatedPacketLoss()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            BlockiverseNetworkSession observerSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Packet Loss World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Packet Loss World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6200, groundHeight: 2));
            CreativeWorldManager observerWorldManager = CreateCreativeWorldManager(
                "Observer Packet Loss World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6201, groundHeight: 2));
            MultiplayerChunkAuthoritySync hostSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerChunkAuthoritySync observerSync = ConfigureChunkSync(observerSession, observerWorldManager);
            var editPositions = new[]
            {
                new BlockPosition(2, 2, 2),
                new BlockPosition(3, 2, 2),
                new BlockPosition(4, 2, 2)
            };
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);
            observerSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for packet-loss simulation.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for packet-loss simulation.");

            Assert.That(observerSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => observerSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 3,
                "Observer client did not connect for packet-loss simulation.");

            yield return WaitFor(
                () => clientSync.HasHostGenerationSnapshotForSession &&
                      observerSync.HasHostGenerationSnapshotForSession,
                "Clients did not receive host generation snapshots before packet-loss simulation.");

            SimulatorUtility.Parameters hostSimulator = ApplyTransportSimulator(hostSession, packetDelayMs: 0, packetDropInterval: 5, randomSeed: 6200);
            SimulatorUtility.Parameters clientSimulator = ApplyTransportSimulator(clientSession, packetDelayMs: 0, packetDropInterval: 5, randomSeed: 6201);
            SimulatorUtility.Parameters observerSimulator = ApplyTransportSimulator(observerSession, packetDelayMs: 0, packetDropInterval: 5, randomSeed: 6202);

            Assert.That(hostSimulator.PacketDropInterval, Is.EqualTo(5));
            Assert.That(clientSimulator.PacketDropInterval, Is.EqualTo(5));
            Assert.That(observerSimulator.PacketDropInterval, Is.EqualTo(5));

            for (int index = 0; index < editPositions.Length; index++)
            {
                BlockMutationResult requestResult = clientSync.TrySubmitMutation(
                    new BlockMutationRequest(
                        clientSync.CurrentBoundary.LocalClientId,
                        editPositions[index],
                        index == 1 ? BlockRegistry.Graystone : BlockRegistry.LumenQuartzCluster,
                        BlockRegistry.Air),
                    out SetBlockCommand clientCommand,
                    out bool requestSentToHost);

                Assert.That(requestSentToHost, Is.True);
                Assert.That(requestResult.PendingHostValidation, Is.True);
                Assert.That(clientCommand, Is.Null);
            }

            yield return WaitFor(
                () => editPositions.All(position =>
                          hostWorldManager.World.GetBlock(position) != BlockRegistry.Air &&
                          clientWorldManager.World.GetBlock(position) == hostWorldManager.World.GetBlock(position) &&
                          observerWorldManager.World.GetBlock(position) == hostWorldManager.World.GetBlock(position)) &&
                      clientSync.PendingMutationRequestCount == 0 &&
                      clientSync.LastAppliedChunkDeltaSequence == 3 &&
                      observerSync.LastAppliedChunkDeltaSequence == 3,
                "Chunk deltas did not converge under simulated packet loss.",
                timeoutSeconds: 10.0f);

            Assert.That(hostSync.ReceivedMutationRequestCount, Is.EqualTo(3));
            Assert.That(hostSync.BroadcastDeltaCount, Is.EqualTo(3));
            Assert.That(clientSync.AcceptedMutationResponseCount, Is.EqualTo(3));
            Assert.That(observerSync.AppliedChunkDeltaCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator NetworkedSurvivalLiteActionsStayHostAuthoritativeAndPerPlayer()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession firstClientSession = CreateClientSession(hostSession);
            BlockiverseNetworkSession secondClientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Survival Sync World");
            CreativeWorldManager firstClientWorldManager = CreateCreativeWorldManager(
                "First Client Survival Sync World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 3112, groundHeight: 2));
            CreativeWorldManager secondClientWorldManager = CreateCreativeWorldManager(
                "Second Client Survival Sync World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 4112, groundHeight: 2));
            var timberPosition = new BlockPosition(2, 2, 2);
            var coalstonePosition = new BlockPosition(3, 2, 2);
            var crateTimberPosition = new BlockPosition(4, 2, 2);
            hostWorldManager.World.SetBlock(timberPosition, BlockRegistry.BranchwoodLog);
            hostWorldManager.World.SetBlock(coalstonePosition, BlockRegistry.EmbercoalSeam);
            hostWorldManager.World.SetBlock(crateTimberPosition, BlockRegistry.BranchwoodLog);

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync firstClientChunkSync = ConfigureChunkSync(firstClientSession, firstClientWorldManager);
            MultiplayerChunkAuthoritySync secondClientChunkSync = ConfigureChunkSync(secondClientSession, secondClientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync firstClientSurvivalSync = ConfigureSurvivalSync(firstClientSession, firstClientChunkSync, firstClientWorldManager);
            MultiplayerSurvivalSync secondClientSurvivalSync = ConfigureSurvivalSync(secondClientSession, secondClientChunkSync, secondClientWorldManager);
            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultAddress,
                port);

            hostSession.Configure(testConfig);
            firstClientSession.Configure(testConfig);
            secondClientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for survival sync.");

            Assert.That(firstClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => firstClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "First client did not connect for survival sync.");

            Assert.That(secondClientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => secondClientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 3,
                "Second client did not connect for survival sync.");

            yield return WaitFor(
                () => firstClientChunkSync.HasHostGenerationSnapshotForSession &&
                      secondClientChunkSync.HasHostGenerationSnapshotForSession &&
                      firstClientSurvivalSync.ReceivedInventorySnapshotCount > 0 &&
                      secondClientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Clients did not receive host-owned survival and world snapshots.");

            SurvivalCommandResult timberHarvest = firstClientSurvivalSync.TrySubmitHarvest(
                timberPosition,
                ItemStack.Empty,
                out bool timberHarvestSentToHost);

            Assert.That(timberHarvestSentToHost, Is.True);
            Assert.That(timberHarvest.PendingHostValidation, Is.True);
            Assert.That(timberHarvest.CommandKind, Is.EqualTo(SurvivalCommandKind.HarvestResource));

            yield return WaitFor(
                () => firstClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 1 &&
                      hostSurvivalSync.GetInventory(firstClientChunkSync.CurrentBoundary.LocalClientId).CountOf(ItemId.BranchwoodLog) == 1 &&
                      secondClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 0 &&
                      hostWorldManager.World.GetBlock(timberPosition) == BlockRegistry.Air &&
                      firstClientWorldManager.World.GetBlock(timberPosition) == BlockRegistry.Air &&
                      secondClientWorldManager.World.GetBlock(timberPosition) == BlockRegistry.Air,
                "Host did not grant harvested timber only to the requesting client.");

            // Embercoal Seam requires a tier-2 Delver (Flint); a tier-1 Reedwood tool cannot mine
            // ores per the survival ruleset (§3, §7.1). Harvest validation is server-authoritative:
            // the host reads the equipped tool from its own copy of the client's inventory at the
            // supplied slot, so the client's claimed stack is ignored. Seed the host-authoritative
            // inventory with the tool and reference it by slot — the client passes no claimed item.
            ulong firstClientId = firstClientChunkSync.CurrentBoundary.LocalClientId;
            const int coalToolSlotIndex = 9;
            hostSurvivalSync.GetInventory(firstClientId).SetSlot(
                coalToolSlotIndex, new ItemStack(ItemId.FlintDelver, 1).WithDurability(35));

            SurvivalCommandResult coalHarvest = firstClientSurvivalSync.TrySubmitHarvest(
                coalstonePosition,
                ItemStack.Empty,
                out bool coalHarvestSentToHost,
                equippedSlotIndex: coalToolSlotIndex);

            Assert.That(coalHarvestSentToHost, Is.True);
            Assert.That(coalHarvest.PendingHostValidation, Is.True);

            yield return WaitFor(
                () => firstClientSurvivalSync.LocalInventory.CountOf(ItemId.Embercoal) == 1,
                "Host did not grant harvested coalstone to the requesting client.");

            // Craft is server-authoritative: Work Plank is the canonical handcraft recipe
            // (branchwood_log ×1 → work_plank ×6, §9.1) and is achievable from the harvested log.
            SurvivalCommandResult craftPlanks = firstClientSurvivalSync.TrySubmitCraft(
                ItemId.WorkPlank,
                CraftingStation.BuildTable,
                out bool craftSentToHost);

            Assert.That(craftSentToHost, Is.True);
            Assert.That(craftPlanks.PendingHostValidation, Is.True);

            yield return WaitFor(
                () => firstClientSurvivalSync.LocalInventory.CountOf(ItemId.WorkPlank) == 6 &&
                      firstClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 0 &&
                      hostSurvivalSync.GetInventory(firstClientChunkSync.CurrentBoundary.LocalClientId).CountOf(ItemId.WorkPlank) == 6,
                "Host did not validate crafting consistently for the requesting client.");

            SurvivalCommandResult crateTimberHarvest = firstClientSurvivalSync.TrySubmitHarvest(
                crateTimberPosition,
                ItemStack.Empty,
                out bool crateTimberHarvestSentToHost);

            Assert.That(crateTimberHarvestSentToHost, Is.True);
            Assert.That(crateTimberHarvest.PendingHostValidation, Is.True);

            yield return WaitFor(
                () => firstClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 1,
                "Host did not grant timber before crate transfer.");

            SurvivalCommandResult depositTimber = firstClientSurvivalSync.TrySubmitCrateDeposit(
                ItemId.BranchwoodLog,
                1,
                out bool depositSentToHost);

            Assert.That(depositSentToHost, Is.True);
            Assert.That(depositTimber.PendingHostValidation, Is.True);

            yield return WaitFor(
                () => firstClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 0 &&
                      firstClientSurvivalSync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog) == 1 &&
                      secondClientSurvivalSync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog) == 1,
                "Shared crate deposit did not sync to both clients.");

            SurvivalCommandResult withdrawTimber = secondClientSurvivalSync.TrySubmitCrateWithdraw(
                ItemId.BranchwoodLog,
                1,
                out bool withdrawSentToHost);

            Assert.That(withdrawSentToHost, Is.True);
            Assert.That(withdrawTimber.PendingHostValidation, Is.True);

            yield return WaitFor(
                () => secondClientSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog) == 1 &&
                      firstClientSurvivalSync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog) == 0 &&
                      secondClientSurvivalSync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog) == 0,
                "Shared crate withdrawal did not update the withdrawing client and crate mirrors.");

            Assert.That(hostSurvivalSync.AcceptedHarvestCount, Is.EqualTo(3));
            Assert.That(hostSurvivalSync.AcceptedCraftCount, Is.EqualTo(1));
            Assert.That(hostSurvivalSync.AcceptedCrateTransferCount, Is.EqualTo(2));
            Assert.That(firstClientSurvivalSync.PendingCommandRequestCount, Is.Zero);
            Assert.That(secondClientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NetworkedSurvivalPlaceConsumesHeldBlockAuthoritatively()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Place World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Place World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 5112, groundHeight: 2));

            var placePosition = new BlockPosition(2, 4, 2); // an air cell above the ground band
            Assert.That(hostWorldManager.World.GetBlock(placePosition), Is.EqualTo(BlockRegistry.Air));

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientChunkSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync clientSurvivalSync = ConfigureSurvivalSync(clientSession, clientChunkSync, clientWorldManager);

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
                "Host did not start for survival place.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for survival place.");

            yield return WaitFor(
                () => clientChunkSync.HasHostGenerationSnapshotForSession &&
                      clientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Client did not receive host-owned survival and world snapshots.");

            // Seed the host-authoritative copy of the client's inventory with a placeable block item.
            ulong clientId = clientChunkSync.CurrentBoundary.LocalClientId;
            const int blockSlotIndex = 0;
            hostSurvivalSync.GetInventory(clientId).SetSlot(blockSlotIndex, new ItemStack(ItemId.BranchwoodLog, 2));

            SurvivalCommandResult place = clientSurvivalSync.TrySubmitPlace(
                placePosition,
                out bool placeSentToHost,
                blockSlotIndex);

            Assert.That(placeSentToHost, Is.True);
            Assert.That(place.PendingHostValidation, Is.True);
            Assert.That(place.CommandKind, Is.EqualTo(SurvivalCommandKind.PlaceBlock));

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(placePosition) == BlockRegistry.BranchwoodLog &&
                      clientWorldManager.World.GetBlock(placePosition) == BlockRegistry.BranchwoodLog &&
                      hostSurvivalSync.GetInventory(clientId).CountOf(ItemId.BranchwoodLog) == 1,
                "Host did not place the held block authoritatively and consume one item.");

            Assert.That(hostSurvivalSync.AcceptedPlaceCount, Is.EqualTo(1));
            Assert.That(clientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NetworkedFellerStripLogConvertsBranchwoodAuthoritatively()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Strip World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Strip World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6112, groundHeight: 2));

            var logPosition = new BlockPosition(2, 4, 2);
            hostWorldManager.World.SetBlock(logPosition, BlockRegistry.BranchwoodLog);

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientChunkSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync clientSurvivalSync = ConfigureSurvivalSync(clientSession, clientChunkSync, clientWorldManager);

            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress, BlockiverseNetworkConfig.DefaultAddress, port);
            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for strip-log.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for strip-log.");

            yield return WaitFor(
                () => clientChunkSync.HasHostGenerationSnapshotForSession &&
                      clientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Client did not receive host-owned survival and world snapshots.");

            // Seed the host-authoritative copy of the client's inventory with a Feller in a slot.
            ulong clientId = clientChunkSync.CurrentBoundary.LocalClientId;
            const int fellerSlotIndex = 0;
            hostSurvivalSync.GetInventory(clientId).SetSlot(fellerSlotIndex, new ItemStack(ItemId.ReedwoodFeller, 1).WithDurability(10));

            SurvivalCommandResult strip = clientSurvivalSync.TrySubmitStripLog(
                logPosition,
                out bool stripSentToHost,
                fellerSlotIndex);

            Assert.That(stripSentToHost, Is.True);
            Assert.That(strip.CommandKind, Is.EqualTo(SurvivalCommandKind.StripLog));

            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(logPosition) == BlockRegistry.SmoothBranchwood &&
                      clientWorldManager.World.GetBlock(logPosition) == BlockRegistry.SmoothBranchwood &&
                      hostSurvivalSync.GetInventory(clientId).GetSlot(fellerSlotIndex).Durability == 9,
                "Feller strip-log did not convert the log to smooth_branchwood and spend durability.");

            Assert.That(hostSurvivalSync.AcceptedStripLogCount, Is.EqualTo(1));
            Assert.That(clientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NetworkedStationSmeltingStaysHostAuthoritative()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Station World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Station World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6113, groundHeight: 2));

            var kilnPosition = new BlockPosition(2, 4, 2);
            hostWorldManager.World.SetBlock(kilnPosition, BlockRegistry.ClayKiln);

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientChunkSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync clientSurvivalSync = ConfigureSurvivalSync(clientSession, clientChunkSync, clientWorldManager);

            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress, BlockiverseNetworkConfig.DefaultAddress, port);
            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for station smelting.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for station smelting.");

            yield return WaitFor(
                () => clientChunkSync.HasHostGenerationSnapshotForSession &&
                      clientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Client did not receive host-owned survival and world snapshots.");

            // Seed the host-authoritative copy of the client's inventory with smelting materials.
            ulong clientId = clientChunkSync.CurrentBoundary.LocalClientId;
            Inventory clientInventoryOnHost = hostSurvivalSync.GetInventory(clientId);
            clientInventoryOnHost.SetSlot(0, new ItemStack(ItemId.ClayLump, 2));
            clientInventoryOnHost.SetSlot(1, new ItemStack(ItemId.Embercoal, 1));

            // Client deposits input + fuel; the host derives the station from the world block and
            // moves the items out of its authoritative copy of the inventory.
            clientSurvivalSync.TrySubmitStationDepositInput(kilnPosition, ItemId.ClayLump, 2, out bool inputSent);
            Assert.That(inputSent, Is.True);
            yield return WaitFor(
                () => hostSurvivalSync.AcceptedStationCommandCount == 1 &&
                      clientInventoryOnHost.CountOf(ItemId.ClayLump) == 0,
                "Station input deposit was not host-validated.");

            clientSurvivalSync.TrySubmitStationDepositFuel(kilnPosition, ItemId.Embercoal, 1, out bool fuelSent);
            Assert.That(fuelSent, Is.True);
            yield return WaitFor(
                () => hostSurvivalSync.AcceptedStationCommandCount == 2 &&
                      clientInventoryOnHost.CountOf(ItemId.Embercoal) == 0,
                "Station fuel deposit was not host-validated.");

            // Host ticks the station through one full kiln craft (fired brick, 8 s).
            hostSurvivalSync.TickStations(8 * SmeltingModel.TicksPerSecond);
            SmeltingStationModel hostStation = hostSurvivalSync.GetOrCreateStationModel(kilnPosition, CraftingStation.ClayKiln);
            Assert.That(hostStation.Output, Is.EqualTo(new ItemStack(ItemId.FiredBrick, 1)));

            // Client collects the output into its inventory via the host.
            clientSurvivalSync.TrySubmitStationCollect(kilnPosition, out bool collectSent);
            Assert.That(collectSent, Is.True);
            yield return WaitFor(
                () => hostSurvivalSync.AcceptedStationCommandCount == 3 &&
                      clientInventoryOnHost.CountOf(ItemId.FiredBrick) == 1 &&
                      clientSurvivalSync.LocalInventory.CountOf(ItemId.FiredBrick) == 1,
                "Station collect did not deliver the output to the client inventory.");

            Assert.That(hostStation.Output.IsEmpty, Is.True);
            Assert.That(clientSurvivalSync.ReceivedStationSnapshotCount, Is.GreaterThan(0),
                "Client should mirror station state from host snapshots.");
            Assert.That(clientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NetworkedConsumableUseDecrementsInventoryAuthoritatively()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Consumable World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Consumable World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6114, groundHeight: 2));

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientChunkSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync clientSurvivalSync = ConfigureSurvivalSync(clientSession, clientChunkSync, clientWorldManager);

            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress, BlockiverseNetworkConfig.DefaultAddress, port);
            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for consumable use.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for consumable use.");

            yield return WaitFor(
                () => clientChunkSync.HasHostGenerationSnapshotForSession &&
                      clientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Client did not receive host-owned survival snapshots.");

            // Seed the host-authoritative copy of the client's inventory with two bandages.
            ulong clientId = clientChunkSync.CurrentBoundary.LocalClientId;
            const int bandageSlot = 0;
            Inventory clientInventoryOnHost = hostSurvivalSync.GetInventory(clientId);
            clientInventoryOnHost.SetSlot(bandageSlot, new ItemStack(ItemId.FieldBandage, 2));

            // The consuming peer applies the vitals effect when the host confirms the command.
            ItemStack consumed = ItemStack.Empty;
            clientSurvivalSync.ConsumableConsumed += item => consumed = item;

            SurvivalCommandResult use = clientSurvivalSync.TrySubmitUseConsumable(out bool useSent, bandageSlot);
            Assert.That(useSent, Is.True);
            Assert.That(use.CommandKind, Is.EqualTo(SurvivalCommandKind.UseConsumable));

            yield return WaitFor(
                () => hostSurvivalSync.AcceptedConsumableCount == 1 &&
                      clientInventoryOnHost.GetSlot(bandageSlot).Count == 1 &&
                      clientSurvivalSync.LocalInventory.GetSlot(bandageSlot).Count == 1 &&
                      !consumed.IsEmpty,
                "Consumable use was not host-validated and mirrored to the client.");

            Assert.That(consumed, Is.EqualTo(new ItemStack(ItemId.FieldBandage, 1)));
            Assert.That(clientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NetworkedTillPlantAndRepairStayHostAuthoritative()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            BlockiverseNetworkSession clientSession = CreateClientSession(hostSession);
            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Farming World");
            CreativeWorldManager clientWorldManager = CreateCreativeWorldManager(
                "Client Farming World",
                new WorldGenerationSettings(width: 8, height: 8, depth: 8, chunkSize: 4, seed: 6115, groundHeight: 2));

            var soilPosition = new BlockPosition(2, 4, 2);
            var cropPosition = new BlockPosition(2, 5, 2);
            hostWorldManager.World.SetBlock(soilPosition, BlockRegistry.LooseLoam);
            // §11.1: tilling requires freshwater within reach (or a clean water flask). Place a
            // source beside the soil on the host (the till is validated against the host world).
            hostWorldManager.World.SetBlock(new BlockPosition(3, 4, 2), BlockRegistry.Freshwater);

            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerChunkAuthoritySync clientChunkSync = ConfigureChunkSync(clientSession, clientWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);
            MultiplayerSurvivalSync clientSurvivalSync = ConfigureSurvivalSync(clientSession, clientChunkSync, clientWorldManager);

            ushort port = NextPort();
            var testConfig = new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress, BlockiverseNetworkConfig.DefaultAddress, port);
            hostSession.Configure(testConfig);
            clientSession.Configure(testConfig);

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for till/plant/repair.");

            Assert.That(clientSession.StartClient(BlockiverseNetworkConfig.DefaultAddress), Is.True);
            yield return WaitFor(
                () => clientSession.NetworkManager.IsConnectedClient &&
                      hostSession.NetworkManager.ConnectedClientsIds.Count == 2,
                "Client did not connect for till/plant/repair.");

            yield return WaitFor(
                () => clientChunkSync.HasHostGenerationSnapshotForSession &&
                      clientSurvivalSync.ReceivedInventorySnapshotCount > 0,
                "Client did not receive host-owned survival and world snapshots.");

            // Seed the host-authoritative copy of the client's inventory: tiller, seed, worn tool
            // and its repair material.
            ulong clientId = clientChunkSync.CurrentBoundary.LocalClientId;
            Inventory clientInventoryOnHost = hostSurvivalSync.GetInventory(clientId);
            clientInventoryOnHost.SetSlot(0, new ItemStack(ItemId.ReedwoodTiller, 1).WithDurability(10));
            clientInventoryOnHost.SetSlot(1, new ItemStack(ItemId.MeadowSeed, 2));
            clientInventoryOnHost.SetSlot(2, new ItemStack(ItemId.FlintDelver, 1).WithDurability(30));
            clientInventoryOnHost.SetSlot(3, new ItemStack(ItemId.FlintyShingle, 2));

            // Till: loose_loam → tended_soil, host-validated against the held Tiller (§11.1).
            clientSurvivalSync.TrySubmitTill(soilPosition, out bool tillSent, equippedSlotIndex: 0);
            Assert.That(tillSent, Is.True);
            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(soilPosition) == BlockRegistry.TendedSoil &&
                      clientWorldManager.World.GetBlock(soilPosition) == BlockRegistry.TendedSoil &&
                      clientInventoryOnHost.GetSlot(0).Durability == 9,
                "Till did not convert the soil authoritatively and spend tiller durability.");
            Assert.That(hostSurvivalSync.AcceptedTillCount, Is.EqualTo(1));

            // Plant: meadow_seed → grain_stalk above the tended soil, consuming one seed (§11.2).
            clientSurvivalSync.TrySubmitPlantSeed(soilPosition, out bool plantSent, equippedSlotIndex: 1);
            Assert.That(plantSent, Is.True);
            yield return WaitFor(
                () => hostWorldManager.World.GetBlock(cropPosition) == BlockRegistry.GrainStalk &&
                      clientWorldManager.World.GetBlock(cropPosition) == BlockRegistry.GrainStalk &&
                      clientInventoryOnHost.CountOf(ItemId.MeadowSeed) == 1,
                "Plant did not place the crop authoritatively and consume a seed.");
            Assert.That(hostSurvivalSync.AcceptedPlantCount, Is.EqualTo(1));

            // Repair requires standing at a Mend Bench (§10.7). The client's avatar is spawned, so
            // the host resolves its position and validates the claim against the world — place a
            // Mend Bench at that resolved block position so the proximity check passes.
            NetworkObject clientAvatarOnHost = GetPlayerObject(hostSession.NetworkManager, clientId);
            Vector3 avatarPosition = clientAvatarOnHost.transform.position;
            WorldBounds hostBounds = hostWorldManager.World.Bounds;
            var benchPosition = new BlockPosition(
                Mathf.Clamp(Mathf.FloorToInt(avatarPosition.x), 0, hostBounds.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(avatarPosition.y), 0, hostBounds.Height - 1),
                Mathf.Clamp(Mathf.FloorToInt(avatarPosition.z), 0, hostBounds.Depth - 1));
            hostWorldManager.World.SetBlock(benchPosition, BlockRegistry.MendBench);

            // Repair: +25% of the Flint Delver's 90 max (23), consuming one flinty_shingle (§10.7).
            clientSurvivalSync.TrySubmitRepair(out bool repairSent, toolSlotIndex: 2);
            Assert.That(repairSent, Is.True);
            yield return WaitFor(
                () => clientInventoryOnHost.GetSlot(2).Durability == 53 &&
                      clientInventoryOnHost.CountOf(ItemId.FlintyShingle) == 1 &&
                      clientSurvivalSync.LocalInventory.GetSlot(2).Durability == 53,
                "Repair did not restore durability authoritatively and consume the material.");
            Assert.That(hostSurvivalSync.AcceptedRepairCount, Is.EqualTo(1));
            Assert.That(clientSurvivalSync.PendingCommandRequestCount, Is.Zero);
        }

        [Test]
        public void HostRejectsNonConsumableUseWithoutThrowing()
        {
            GameObject syncObject = new("Non-Consumable Use Survival Sync");
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, null, null);
            bool requestSentToHost = true;
            SurvivalCommandResult result = default;

            try
            {
                // Offline host-local commands resolve the server client id's inventory.
                Inventory hostInventory = survivalSync.GetInventory(NetworkManager.ServerClientId);
                hostInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 1));

                Assert.DoesNotThrow(
                    () => result = survivalSync.TrySubmitUseConsumable(out requestSentToHost, slotIndex: 0));
                Assert.That(requestSentToHost, Is.False);
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.NotConsumable));
                Assert.That(survivalSync.AcceptedConsumableCount, Is.Zero);
                Assert.That(hostInventory.GetSlot(0).Count, Is.EqualTo(1),
                    "A rejected consumable use must not consume the held item.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(syncObject);
            }
        }

        [UnityTest]
        public IEnumerator SurvivalHudPanelsRouteCraftAndCrateThroughAuthoritativeSync()
        {
            yield return LoadMultiplayerTestScene();

            BlockiverseNetworkSession hostSession = UnityEngine.Object.FindFirstObjectByType<BlockiverseNetworkSession>();
            Assert.That(hostSession, Is.Not.Null);

            CreativeWorldManager hostWorldManager = CreateCreativeWorldManager("Host Panel Routing World");
            MultiplayerChunkAuthoritySync hostChunkSync = ConfigureChunkSync(hostSession, hostWorldManager);
            MultiplayerSurvivalSync hostSurvivalSync = ConfigureSurvivalSync(hostSession, hostChunkSync, hostWorldManager);

            ushort port = NextPort();
            hostSession.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress, BlockiverseNetworkConfig.DefaultAddress, port));

            Assert.That(hostSession.StartHost(), Is.True);
            yield return WaitFor(
                () => hostSession.NetworkManager.IsHost && hostSession.CurrentState == BlockiverseConnectionState.Hosting,
                "Host did not start for panel routing.");

            // After StartHost the host's LocalInventory is the authoritative local-player inventory.
            ItemRegistry itemRegistry = ItemRegistry.CreateDefault();
            hostSurvivalSync.LocalInventory.SetSlot(0, new ItemStack(ItemId.BranchwoodLog, 2));
            hostSurvivalSync.SelectedHotbarSlotIndex = 0;

            // Crafting panel → authoritative TrySubmitCraft (Work Plank: branchwood_log ×1 → ×6, §9.1).
            var craftingObject = new GameObject("Test Crafting Panel");
            SurvivalCraftingPanel craftingPanel = craftingObject.AddComponent<SurvivalCraftingPanel>();
            craftingPanel.ConfigureSurvivalSync(hostSurvivalSync);
            craftingPanel.Bind(CraftingRecipeBook.CreateDefault(itemRegistry), hostSurvivalSync.LocalInventory, itemRegistry, CraftingStation.BuildTable);
            craftingPanel.TryCraftByOutput(ItemId.WorkPlank);

            Assert.That(hostSurvivalSync.LocalInventory.CountOf(ItemId.WorkPlank), Is.EqualTo(6),
                "Crafting panel should craft through the authoritative sync into the shared inventory.");
            Assert.That(hostSurvivalSync.AcceptedCraftCount, Is.EqualTo(1));

            // Crate panel → authoritative deposit/withdraw of the held item (the remaining branchwood_log).
            var crateObject = new GameObject("Test Crate Panel");
            SurvivalCratePanel cratePanel = crateObject.AddComponent<SurvivalCratePanel>();
            cratePanel.Bind(hostSurvivalSync, itemRegistry);

            cratePanel.DepositHeld();
            Assert.That(hostSurvivalSync.SharedCrateInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(1),
                "Crate panel deposit should move the held item into the shared crate.");
            Assert.That(hostSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(0));

            int crateSlot = FindSlotWith(hostSurvivalSync.SharedCrateInventory, ItemId.BranchwoodLog);
            Assert.That(crateSlot, Is.GreaterThanOrEqualTo(0));
            cratePanel.WithdrawSlot(crateSlot);
            Assert.That(hostSurvivalSync.LocalInventory.CountOf(ItemId.BranchwoodLog), Is.EqualTo(1),
                "Crate panel withdraw should return the item to the player inventory.");
            Assert.That(hostSurvivalSync.AcceptedCrateTransferCount, Is.EqualTo(2));

            UnityEngine.Object.Destroy(craftingObject);
            UnityEngine.Object.Destroy(crateObject);
        }

        static int FindSlotWith(Inventory inventory, ItemId itemId)
        {
            for (int i = 0; i < inventory.SlotCount; i++)
                if (inventory.GetSlot(i).ItemId.Equals(itemId) && !inventory.GetSlot(i).IsEmpty)
                    return i;
            return -1;
        }

        [Test]
        public void HostRejectsUnknownCrateTransferItemWithoutThrowing()
        {
            GameObject syncObject = new("Malformed Crate Transfer Survival Sync");
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, null, null);
            bool requestSentToHost = true;
            SurvivalCommandResult result = default;

            try
            {
                Assert.DoesNotThrow(
                    () => result = survivalSync.TrySubmitCrateDeposit(new ItemId("unknown_item_test_9999"), 1, out requestSentToHost));
                Assert.That(requestSentToHost, Is.False);
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(SurvivalCommandFailureReason.InvalidTransfer));
                Assert.That(survivalSync.AcceptedCrateTransferCount, Is.Zero);
                Assert.That(survivalSync.RejectedCommandCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(syncObject);
            }
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

        static MultiplayerChunkAuthoritySync ConfigureChunkSync(
            BlockiverseNetworkSession session,
            CreativeWorldManager worldManager)
        {
            MultiplayerChunkAuthoritySync sync = session.GetComponent<MultiplayerChunkAuthoritySync>();

            if (sync == null)
                sync = session.gameObject.AddComponent<MultiplayerChunkAuthoritySync>();

            sync.Configure(session, worldManager);
            return sync;
        }

        static MultiplayerSurvivalSync ConfigureSurvivalSync(
            BlockiverseNetworkSession session,
            MultiplayerChunkAuthoritySync chunkSync,
            CreativeWorldManager worldManager)
        {
            MultiplayerSurvivalSync sync = session.GetComponent<MultiplayerSurvivalSync>();

            if (sync == null)
                sync = session.gameObject.AddComponent<MultiplayerSurvivalSync>();

            sync.Configure(session, chunkSync, worldManager);
            return sync;
        }

        static CreativeWorldManager CreateCreativeWorldManager(string name, WorldGenerationSettings settings = null)
        {
            GameObject worldObject = new(name);
            worldObject.SetActive(false);
            CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
            manager.Configure(CreateBlockAtlasMaterial(), -1);
            BlockRegistry registry = BlockRegistry.CreateDefault();
            settings ??= new WorldGenerationSettings(
                    width: 16,
                    height: 8,
                    depth: 16,
                    chunkSize: 16,
                    seed: 9901,
                    groundHeight: 2);
            VoxelWorld world = new FlatCreativeWorldPreset(registry, settings).Generate();
            manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                registry,
                settings,
                world,
                CreativeWorldGenerationPreset.FlatCreative));
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
            TMP_InputField addressInput = CreateInputField("Address Input", menuObject.transform);
            TMP_Text statusText = CreateText("Status", menuObject.transform);

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

        static TMP_InputField CreateInputField(string name, Transform parent)
        {
            GameObject inputObject = new(name, typeof(RectTransform));
            inputObject.transform.SetParent(parent, false);
            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
            input.textComponent = CreateText("Text", inputObject.transform);
            return input;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent)
        {
            GameObject textObject = new(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            return textObject.AddComponent<TextMeshProUGUI>();
        }

        static ushort NextPort()
        {
            return nextPort++;
        }

        static string CreateTempSavePath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"blockiverse-multiplayer-{Guid.NewGuid():N}.vxlworld");
            TempSavePaths.Add(path);
            return path;
        }

        static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }

        static IEnumerator WaitFor(Func<bool> condition, string failureMessage, float timeoutSeconds = 5.0f)
        {
            float timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;

            while (!condition() && Time.realtimeSinceStartup < timeoutAt)
                yield return null;

            Assert.That(condition(), Is.True, failureMessage);
        }

        static SimulatorUtility.Parameters ApplyTransportSimulator(
            BlockiverseNetworkSession session,
            int packetDelayMs,
            int packetDropInterval,
            uint randomSeed)
        {
            UnityTransport transport = session.NetworkManager.NetworkConfig.NetworkTransport as UnityTransport;
            Assert.That(transport, Is.Not.Null);

            ref NetworkDriver driver = ref transport.GetNetworkDriver();
            Assert.That(driver.IsCreated, Is.True, "Unity Transport driver must be created before applying simulator settings.");

            NetworkSettings settings = driver.CurrentSettings;
            SimulatorUtility.Parameters parameters = settings.GetSimulatorStageParameters();
            Assert.That(parameters.MaxPacketCount, Is.GreaterThan(0), "Unity Transport simulator stage was not initialized.");

            parameters.Mode = ApplyMode.AllPackets;
            parameters.PacketDelayMs = packetDelayMs;
            parameters.PacketJitterMs = 0;
            parameters.PacketDropInterval = packetDropInterval;
            parameters.PacketDropPercentage = 0;
            parameters.PacketDuplicationPercentage = 0;
            parameters.FuzzFactor = 0;
            parameters.FuzzOffset = 0;
            parameters.RandomSeed = randomSeed;
            driver.ModifySimulatorStageParameters(parameters);
            return parameters;
        }

        static void AssertPlayerObjectExists(NetworkManager networkManager, ulong clientId)
        {
            NetworkObject playerObject = GetPlayerObject(networkManager, clientId);

            Assert.That(playerObject, Is.Not.Null);
            Assert.That(playerObject.OwnerClientId, Is.EqualTo(clientId));
        }

        static void AssertFallbackAvatarExists(NetworkManager networkManager, ulong clientId)
        {
            NetworkObject playerObject = GetPlayerObject(networkManager, clientId);
            BlockiverseNetworkAvatarRig avatarRig = playerObject.GetComponent<BlockiverseNetworkAvatarRig>();

            Assert.That(avatarRig, Is.Not.Null);
            Assert.That(avatarRig.IsUsingFallbackProxy, Is.True);
            Assert.That(avatarRig.FallbackRoot, Is.Not.Null);
            Assert.That(avatarRig.FallbackRoot.gameObject.activeSelf, Is.True);
            Assert.That(avatarRig.HeadAnchor, Is.Not.Null);
            Assert.That(avatarRig.LeftHandAnchor, Is.Not.Null);
            Assert.That(avatarRig.RightHandAnchor, Is.Not.Null);
        }

        static NetworkObject GetPlayerObject(NetworkManager networkManager, ulong clientId)
        {
            if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                Assert.That(client.PlayerObject, Is.Not.Null);
                return client.PlayerObject;
            }

            if (networkManager.SpawnManager != null)
            {
                NetworkObject remotePlayer = networkManager.SpawnManager.SpawnedObjectsList
                    .FirstOrDefault(spawnedObject => spawnedObject != null &&
                                                     spawnedObject.IsPlayerObject &&
                                                     spawnedObject.OwnerClientId == clientId);

                if (remotePlayer != null)
                    return remotePlayer;
            }

            Assert.That(networkManager.LocalClientId, Is.EqualTo(clientId));
            Assert.That(networkManager.LocalClient, Is.Not.Null);
            Assert.That(networkManager.LocalClient.PlayerObject, Is.Not.Null);
            return networkManager.LocalClient.PlayerObject;
        }

        static GameObject CreateTrackingSource(string name, Vector3 position, Quaternion rotation)
        {
            GameObject source = new(name);
            source.transform.SetPositionAndRotation(position, rotation);
            return source;
        }

        static bool IsClose(Vector3 actual, Vector3 expected)
        {
            return (actual - expected).sqrMagnitude <= 0.0025f;
        }

        static bool IsClose(Quaternion actual, Quaternion expected)
        {
            return Quaternion.Angle(actual, expected) <= 0.5f;
        }

        static Pose ExpectedLocalPose(Transform root, Transform source)
        {
            return new Pose(
                root.InverseTransformPoint(source.position),
                Quaternion.Inverse(root.rotation) * source.rotation);
        }

        static bool AvatarPoseMatches(
            BlockiverseNetworkAvatarRig avatarRig,
            Pose rootPose,
            Pose headPose,
            Pose leftHandPose,
            Pose rightHandPose)
        {
            return IsClose(avatarRig.transform.position, rootPose.position) &&
                   IsClose(avatarRig.transform.rotation, rootPose.rotation) &&
                   IsClose(avatarRig.HeadAnchor.localPosition, headPose.position) &&
                   IsClose(avatarRig.HeadAnchor.localRotation, headPose.rotation) &&
                   IsClose(avatarRig.LeftHandAnchor.localPosition, leftHandPose.position) &&
                   IsClose(avatarRig.LeftHandAnchor.localRotation, leftHandPose.rotation) &&
                   IsClose(avatarRig.RightHandAnchor.localPosition, rightHandPose.position) &&
                   IsClose(avatarRig.RightHandAnchor.localRotation, rightHandPose.rotation);
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
            // VR UI now uses XRI's tracked-device input module (a BaseInputModule) instead of the
            // screen-space InputSystemUIInputModule. Assert without an XRI dependency in this asmdef.
            Assert.That(eventSystems[0].GetComponent<BaseInputModule>(), Is.Not.Null);
            Assert.That(eventSystems[0].GetComponent<InputSystemUIInputModule>(), Is.Null);
        }
    }
}
