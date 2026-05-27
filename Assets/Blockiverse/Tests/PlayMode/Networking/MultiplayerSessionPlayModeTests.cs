using System;
using System.Collections;
using System.IO;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.Networking.PlayMode
{
    public sealed class MultiplayerSessionPlayModeTests
    {
        static ushort nextPort = 7810;

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

            AssertNoSpawnedObjects(hostSession.NetworkManager);
            AssertNoSpawnedObjects(clientSession.NetworkManager);
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
        }

        static IEnumerator LoadMultiplayerTestScene()
        {
            string sceneName = Path.GetFileNameWithoutExtension(BlockiverseProject.MultiplayerTestScenePath);
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            Assert.That(operation, Is.Not.Null);

            while (!operation.isDone)
                yield return null;
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

        static ushort NextPort()
        {
            return nextPort++;
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
    }
}
