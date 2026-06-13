using System.Reflection;
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

        BlockiverseMultiplayerSessionMenu CreateMenu()
        {
            menuObject = new GameObject("Session Menu");
            BlockiverseMultiplayerSessionMenu menu = menuObject.AddComponent<BlockiverseMultiplayerSessionMenu>();
            menu.ConfigureControls(
                CreateButton("Host Button"),
                CreateButton("Join Button"),
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
