using System.Reflection;
using Blockiverse.MetaPlatform;
using Blockiverse.Networking;
using Blockiverse.UI;
using NUnit.Framework;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Tests.Networking.EditMode
{
    public sealed class BlockiverseMultiplayerSessionMenuEditModeTests
    {
        GameObject menuObject;

        [TearDown]
        public void TearDown()
        {
            BlockiverseUserAgeCategoryService.ResetForTests();

            if (menuObject != null)
                Object.DestroyImmediate(menuObject);
        }

        [Test]
        public void BlankAddressUsesDefaultLanAddress()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();

            menu.AddressInput.text = "   ";

            Assert.That(menu.ResolveJoinAddress(), Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
        }

        [Test]
        public void AddressInputTrimsPlayerEnteredAddress()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();

            menu.AddressInput.text = " 192.168.1.42 ";

            Assert.That(menu.ResolveJoinAddress(), Is.EqualTo("192.168.1.42"));
        }

        [Test]
        public void AddressInputStartsBlankWithHostIpPlaceholder()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            TMP_Text placeholder = menu.AddressInput.placeholder as TMP_Text;

            Assert.That(menu.AddressInput.text, Is.Empty);
            Assert.That(placeholder, Is.Not.Null);
            Assert.That(placeholder.text, Is.EqualTo("Host LAN IP"));
            Assert.That(menu.ResolveJoinAddress(), Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
        }

        [Test]
        public void HostingStatusShowsJoinableAddressInsteadOfWildcardListenAddress()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Hosting);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentMode), NetworkSessionMode.Host);

            menu.Configure(session);
            menu.RefreshStatus();

            StringAssert.Contains("Hosting LAN session", menu.StatusText.text);
            StringAssert.Contains("Join at", menu.StatusText.text);
            Assert.That(menu.StatusText.text, Does.Not.Contain(BlockiverseNetworkConfig.DefaultListenAddress));
        }

        [Test]
        public void ChildAccountStatusMentionsFallbackIdentityBehavior()
        {
            BlockiverseUserAgeCategoryService.SetCurrentForTests(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Child,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "child"));
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Hosting);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentMode), NetworkSessionMode.Host);

            menu.Configure(session);
            menu.RefreshStatus();

            Assert.That(menu.StatusText.text.ToLowerInvariant(), Does.Contain("fallback identity"));
        }

        [Test]
        public void HostLeftStatusSurfacesHostDisconnectedCopyWithDisconnectReason()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            // Host-left signature: previously connected as client, then dropped involuntarily.
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.HasConnectedAsClient), true);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Disconnected);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.LastDisconnectReason), "host shut down");

            menu.Configure(session);
            menu.RefreshStatus();

            Assert.That(menu.IsShowingSessionEndedMessage, Is.True);
            StringAssert.Contains("host disconnected", menu.StatusText.text);
            StringAssert.Contains("host shut down", menu.StatusText.text);
        }

        [Test]
        public void JoinFailureStatusIsDistinctFromHostLeftStatus()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            // Join failure: never connected, so HasConnectedAsClient stays false.
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Disconnected);

            menu.Configure(session);
            menu.RefreshStatus();

            Assert.That(menu.IsShowingSessionEndedMessage, Is.False);
            StringAssert.Contains("Unable to reach", menu.StatusText.text);
            Assert.That(menu.StatusText.text, Does.Not.Contain("host disconnected"));
        }

        [Test]
        public void MissingSessionShowsUnavailableStatusAndDisablesActions()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();

            menu.Configure(null);
            menu.RefreshStatus();

            StringAssert.Contains("unavailable", menu.StatusText.text);
            Assert.That(menu.HostButton.interactable, Is.False);
            Assert.That(menu.JoinButton.interactable, Is.False);
            Assert.That(menu.StopButton.interactable, Is.False);
        }

        [Test]
        public void JoiningADiscoveredHostAdoptsItsAdvertisedPort()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            BlockiverseLanDiscovery discovery = session.gameObject.AddComponent<BlockiverseLanDiscovery>();
            discovery.Configure(session);
            menu.Configure(session);
            menu.ConfigureDiscovery(discovery, System.Array.Empty<Button>(), System.Array.Empty<TMP_Text>(), null);

            // A host on a non-default port. Without adopting it, StartClient dials the local
            // config port and signs that port into the approval payload, so the host refuses the
            // join it just advertised.
            const ushort advertisedPort = 7999;
            discovery.ApplyBeacon(
                BlockiverseLanDiscoveryBeacon.Encode(
                    advertisedPort,
                    playerCount: 0,
                    maxPlayers: 2,
                    hostName: "Other Port Host",
                    joinCode: session.Config.JoinCode),
                "192.168.1.77");

            Assert.That(menu.DiscoveredSessions, Has.Count.EqualTo(1));
            Assert.That(session.Config.Port, Is.Not.EqualTo(advertisedPort));

            // The port adoption is exercised on its own rather than through
            // JoinDiscoveredSession, which would start a real Netcode client in EditMode.
            Assert.That(AdoptDiscoveredPort(menu, menu.DiscoveredSessions[0]), Is.True);

            Assert.That(session.Config.Port, Is.EqualTo(advertisedPort));
        }

        static bool AdoptDiscoveredPort(BlockiverseMultiplayerSessionMenu menu, BlockiverseDiscoveredSession discovered)
        {
            MethodInfo adopt = typeof(BlockiverseMultiplayerSessionMenu).GetMethod(
                "TryAdoptDiscoveredPort",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(adopt, Is.Not.Null, "The discovered-port adoption should remain present.");
            return (bool)adopt.Invoke(menu, new object[] { discovered });
        }

        [Test]
        public void BrowsingFollowsPanelVisibilityRatherThanComponentLifecycle()
        {
            BlockiverseMultiplayerSessionMenu menu = CreateMenu();
            BlockiverseNetworkSession session = CreateSession();
            BlockiverseLanDiscovery discovery = session.gameObject.AddComponent<BlockiverseLanDiscovery>();
            menu.Configure(session);
            menu.ConfigureDiscovery(discovery, System.Array.Empty<Button>(), System.Array.Empty<TMP_Text>(), null);

            // The world-space presenter hides a panel by disabling its Canvas and leaves the
            // GameObject active, so OnEnable/OnDisable cannot be the signal: keying off them left
            // the browse socket open for the entire session.
            var canvas = menuObject.AddComponent<Canvas>();
            var presenter = menuObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(canvas, null, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);

            presenter.Hide();
            Assert.That(menuObject.activeSelf, Is.True, "Hiding the panel must not deactivate the GameObject for this test to mean anything.");
            Assert.That(presenter.IsVisible, Is.False);

            RefreshDiscoveryListening(menu);
            Assert.That(discovery.ListenRequested, Is.False, "A hidden panel should not be browsing.");

            presenter.Show(recenterPlacement: false);
            RefreshDiscoveryListening(menu);
            Assert.That(discovery.ListenRequested, Is.True, "A visible panel should browse.");

            presenter.Hide();
            RefreshDiscoveryListening(menu);
            Assert.That(discovery.ListenRequested, Is.False, "Closing the panel should stop browsing.");
        }

        // The per-frame visibility check Update() drives; invoked directly so the test does not
        // depend on EditMode frame ticking.
        static void RefreshDiscoveryListening(BlockiverseMultiplayerSessionMenu menu)
        {
            MethodInfo refresh = typeof(BlockiverseMultiplayerSessionMenu).GetMethod(
                "RefreshDiscoveryListening",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refresh, Is.Not.Null, "The panel-visibility check should remain present.");
            refresh.Invoke(menu, null);
        }

        BlockiverseMultiplayerSessionMenu CreateMenu()
        {
            menuObject = new GameObject("Session Menu");
            BlockiverseMultiplayerSessionMenu menu = menuObject.AddComponent<BlockiverseMultiplayerSessionMenu>();
            menu.ConfigureControls(
                CreateButton("Host Button"),
                CreateButton("Join Button"),
                null,
                CreateButton("Stop Button"),
                CreateInputField("Address Input"),
                CreateText("Status"));
            return menu;
        }

        Button CreateButton(string name)
        {
            GameObject buttonObject = new(name, typeof(RectTransform));
            buttonObject.transform.SetParent(menuObject.transform, false);
            return buttonObject.AddComponent<Button>();
        }

        TMP_InputField CreateInputField(string name)
        {
            GameObject inputObject = new(name, typeof(RectTransform));
            inputObject.transform.SetParent(menuObject.transform, false);
            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
            input.textComponent = CreateText("Text");
            input.placeholder = CreateText("Placeholder");
            return input;
        }

        TextMeshProUGUI CreateText(string name)
        {
            GameObject textObject = new(name, typeof(RectTransform));
            textObject.transform.SetParent(menuObject.transform, false);
            return textObject.AddComponent<TextMeshProUGUI>();
        }

        BlockiverseNetworkSession CreateSession()
        {
            GameObject sessionObject = new("Network Session");
            sessionObject.transform.SetParent(menuObject.transform, false);
            sessionObject.AddComponent<UnityTransport>();
            sessionObject.AddComponent<NetworkManager>();
            return sessionObject.AddComponent<BlockiverseNetworkSession>();
        }

        static void SetAutoProperty<T>(object target, string propertyName, T value)
        {
            FieldInfo field = target.GetType().GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{propertyName} backing field should exist.");
            field.SetValue(target, value);
        }
    }
}
