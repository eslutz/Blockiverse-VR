using System.Reflection;
using Blockiverse.MetaPlatform;
using Blockiverse.Networking;
using Blockiverse.UI;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Tests.EditMode
{
    // UI Toolkit mirror of BlockiverseMultiplayerSessionMenuEditModeTests: same behaviours
    // (address parsing, status copy including the host-left vs join-failed distinction,
    // discovered-port adoption, discovery-follows-visibility) asserted against the real
    // LanMultiplayerScreen.uxml through the controller's AttachForTest seam. UIDocument never
    // builds rootVisualElement in EditMode, so the tree is instantiated directly from the asset.
    public sealed class LanMultiplayerScreenEditModeTests
    {
        const string DocumentPath = "Assets/Blockiverse/UI/Documents/LanMultiplayerScreen.uxml";

        GameObject screenObject;
        GameObject sessionObject;
        LanMultiplayerScreenController controller;
        VisualElement root;

        [SetUp]
        public void SetUp()
        {
            screenObject = new GameObject("LAN Multiplayer Screen");
            controller = screenObject.AddComponent<LanMultiplayerScreenController>();

            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DocumentPath);
            Assert.That(tree, Is.Not.Null, "LanMultiplayerScreen.uxml did not load — document path drifted.");
            root = tree.Instantiate();
            controller.AttachForTest(root);
        }

        [TearDown]
        public void TearDown()
        {
            BlockiverseUserAgeCategoryService.ResetForTests();

            if (screenObject != null)
                Object.DestroyImmediate(screenObject);

            if (sessionObject != null)
                Object.DestroyImmediate(sessionObject);
        }

        // Positive control for every other test here: a controller that failed to find its
        // elements would no-op its way through the assertions below.
        [Test]
        public void DocumentBindsEveryNamedElement()
        {
            Assert.That(controller.IsBound, Is.True,
                "OnAttach reported missing elements — UXML names drifted from the controller.");
            Assert.That(root.Q<Button>("bv-lan-host"), Is.Not.Null);
            Assert.That(root.Q<Label>("bv-lan-status"), Is.Not.Null);
            Assert.That(root.Q<Button>("bv-lan-discovery-slot-4"), Is.Not.Null);
        }

        [Test]
        public void BlankAddressUsesDefaultLanAddress()
        {
            root.Q<TextField>("bv-lan-address").value = "   ";

            Assert.That(controller.ResolveJoinAddress(), Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
        }

        [Test]
        public void AddressFieldTrimsPlayerEnteredAddress()
        {
            root.Q<TextField>("bv-lan-address").value = " 192.168.1.42 ";

            Assert.That(controller.ResolveJoinAddress(), Is.EqualTo("192.168.1.42"));
        }

        [Test]
        public void AddressFieldStartsBlankWithHostIpPlaceholder()
        {
            TextField addressField = root.Q<TextField>("bv-lan-address");
            TextField secretField = root.Q<TextField>("bv-lan-secret");

            Assert.That(addressField.value, Is.Empty);
            Assert.That(addressField.textEdition.placeholder, Is.EqualTo("Host LAN IP"));
            Assert.That(secretField.textEdition.placeholder, Is.EqualTo("Server password (if any)"));
            Assert.That(controller.ResolveJoinAddress(), Is.EqualTo(BlockiverseNetworkConfig.DefaultAddress));
        }

        [Test]
        public void HostingStatusShowsJoinableAddressInsteadOfWildcardListenAddress()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Hosting);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentMode), NetworkSessionMode.Host);

            controller.Configure(session);
            controller.RefreshStatus();

            Label status = root.Q<Label>("bv-lan-status");
            StringAssert.Contains("Hosting LAN session", status.text);
            StringAssert.Contains("Join at", status.text);
            Assert.That(status.text, Does.Not.Contain(BlockiverseNetworkConfig.DefaultListenAddress));
            Assert.That(status.ClassListContains("hs-status--confirmed"), Is.True,
                "A live session should carry the confirmed status tone.");
        }

        [Test]
        public void ChildAccountStatusMentionsFallbackIdentityBehavior()
        {
            BlockiverseUserAgeCategoryService.SetCurrentForTests(new BlockiverseUserAgeCategoryState(
                BlockiverseUserAgeCategory.Child,
                BlockiverseUserAgeCategorySource.LiveApi,
                1,
                "child"));
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Hosting);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentMode), NetworkSessionMode.Host);

            controller.Configure(session);
            controller.RefreshStatus();

            Assert.That(root.Q<Label>("bv-lan-status").text.ToLowerInvariant(), Does.Contain("fallback identity"));
        }

        [Test]
        public void HostLeftStatusSurfacesHostDisconnectedCopyWithDisconnectReason()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            // Host-left signature: previously connected as client, then dropped involuntarily.
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.HasConnectedAsClient), true);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Disconnected);
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.LastDisconnectReason), "host shut down");

            controller.Configure(session);
            controller.RefreshStatus();

            Label status = root.Q<Label>("bv-lan-status");
            Assert.That(controller.IsShowingSessionEndedMessage, Is.True);
            StringAssert.Contains("host disconnected", status.text);
            StringAssert.Contains("host shut down", status.text);
            Assert.That(status.ClassListContains("hs-status--rejected"), Is.True,
                "A session-ended disconnect should carry the rejected status tone.");
        }

        [Test]
        public void JoinFailureStatusIsDistinctFromHostLeftStatus()
        {
            BlockiverseNetworkSession session = CreateSession();
            session.Configure(new BlockiverseNetworkConfig(
                BlockiverseNetworkConfig.DefaultAddress,
                BlockiverseNetworkConfig.DefaultListenAddress,
                BlockiverseNetworkConfig.DefaultPort));
            // Join failure: never connected, so HasConnectedAsClient stays false.
            SetAutoProperty(session, nameof(BlockiverseNetworkSession.CurrentState), BlockiverseConnectionState.Disconnected);

            controller.Configure(session);
            controller.RefreshStatus();

            Label status = root.Q<Label>("bv-lan-status");
            Assert.That(controller.IsShowingSessionEndedMessage, Is.False);
            StringAssert.Contains("Unable to reach", status.text);
            Assert.That(status.text, Does.Not.Contain("host disconnected"));
            Assert.That(status.ClassListContains("hs-status--rejected"), Is.False,
                "A plain unable-to-reach disconnect reads as idle, not session-ended.");
        }

        [Test]
        public void MissingSessionShowsUnavailableStatusAndDisablesActions()
        {
            controller.Configure(null);
            controller.RefreshStatus();

            StringAssert.Contains("unavailable", root.Q<Label>("bv-lan-status").text);
            Assert.That(root.Q<Button>("bv-lan-host").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("bv-lan-join").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("bv-lan-stop").enabledSelf, Is.False);
        }

        [Test]
        public void ReconnectStaysDisabledUntilAJoinAddressIsRemembered()
        {
            BlockiverseNetworkSession session = CreateSession();
            controller.Configure(session);
            controller.RefreshStatus();

            // Positive control first: an idle session can start, so a do-nothing RefreshControls
            // (which leaves every element enabled) cannot pass the reconnect assertion by luck.
            Assert.That(root.Q<Button>("bv-lan-host").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("bv-lan-join").enabledSelf, Is.True);
            Assert.That(controller.LastJoinAddress, Is.Null.Or.Empty);
            Assert.That(root.Q<Button>("bv-lan-reconnect").enabledSelf, Is.False,
                "Reconnect requires a previously attempted join address.");
        }

        [Test]
        public void JoiningADiscoveredHostAdoptsItsAdvertisedPort()
        {
            BlockiverseNetworkSession session = CreateSession();
            BlockiverseLanDiscovery discovery = session.gameObject.AddComponent<BlockiverseLanDiscovery>();
            discovery.Configure(session);
            controller.Configure(session);
            controller.ConfigureDiscovery(discovery);

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

            Assert.That(controller.DiscoveredSessions, Has.Count.EqualTo(1));
            Assert.That(session.Config.Port, Is.Not.EqualTo(advertisedPort));

            // The port adoption is exercised on its own rather than through
            // JoinDiscoveredSession, which would start a real Netcode client in EditMode.
            Assert.That(AdoptDiscoveredPort(controller, controller.DiscoveredSessions[0]), Is.True);

            Assert.That(session.Config.Port, Is.EqualTo(advertisedPort));
        }

        [Test]
        public void DiscoveredHostFillsASlotButtonWithItsEntry()
        {
            BlockiverseNetworkSession session = CreateSession();
            BlockiverseLanDiscovery discovery = session.gameObject.AddComponent<BlockiverseLanDiscovery>();
            discovery.Configure(session);
            controller.Configure(session);
            controller.ConfigureDiscovery(discovery);

            Button firstSlot = root.Q<Button>("bv-lan-discovery-slot-1");
            Assert.That(firstSlot.style.display.value, Is.EqualTo(DisplayStyle.None),
                "An empty slot must stay hidden.");

            discovery.ApplyBeacon(
                BlockiverseLanDiscoveryBeacon.Encode(
                    7777,
                    playerCount: 1,
                    maxPlayers: 2,
                    hostName: "Camp Host",
                    joinCode: session.Config.JoinCode),
                "192.168.1.50");

            Assert.That(firstSlot.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(firstSlot.enabledSelf, Is.True, "A session with room should be joinable.");
            StringAssert.Contains("Camp Host", firstSlot.text);
            StringAssert.Contains("192.168.1.50:7777", firstSlot.text);
            Assert.That(root.Q<Button>("bv-lan-discovery-slot-2").style.display.value,
                Is.EqualTo(DisplayStyle.None), "Slots beyond the discovered count stay hidden.");
        }

        [Test]
        public void BrowsingFollowsRoutedVisibilityRatherThanComponentLifecycle()
        {
            BlockiverseNetworkSession session = CreateSession();
            BlockiverseLanDiscovery discovery = session.gameObject.AddComponent<BlockiverseLanDiscovery>();
            controller.Configure(session);
            controller.ConfigureDiscovery(discovery);

            // A UI Toolkit screen hides by collapsing its root; the component stays enabled the
            // whole time, so OnShown/OnHidden are the only honest browse signal (the uGUI-era
            // bug this guards against left a UDP socket open for an entire headset session).
            Assert.That(discovery.ListenRequested, Is.False,
                "An attached but never-shown screen must not be browsing.");

            controller.SetVisible(true, true);
            Assert.That(discovery.ListenRequested, Is.True, "A routed-visible screen should browse.");

            controller.SetVisible(false, false);
            Assert.That(discovery.ListenRequested, Is.False, "Hiding the screen must stop browsing.");

            controller.SetVisible(true, true);
            Assert.That(discovery.ListenRequested, Is.True,
                "Re-showing should reopen browsing (the socket-failure latch resets on open).");

            // End hidden: EditMode never calls OnDestroy for components whose Awake never ran,
            // so this is what actually closes the browse socket the test opened.
            controller.SetVisible(false, false);
            Assert.That(discovery.ListenRequested, Is.False);
        }

        static bool AdoptDiscoveredPort(LanMultiplayerScreenController target, BlockiverseDiscoveredSession discovered)
        {
            MethodInfo adopt = typeof(LanMultiplayerScreenController).GetMethod(
                "TryAdoptDiscoveredPort",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(adopt, Is.Not.Null, "The discovered-port adoption should remain present.");
            return (bool)adopt.Invoke(target, new object[] { discovered });
        }

        BlockiverseNetworkSession CreateSession()
        {
            sessionObject = new GameObject("Network Session");
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
