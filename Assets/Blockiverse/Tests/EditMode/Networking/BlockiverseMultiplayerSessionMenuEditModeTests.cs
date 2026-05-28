using Blockiverse.Networking;
using Blockiverse.UI;
using NUnit.Framework;
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

        InputField CreateInputField(string name)
        {
            GameObject inputObject = new(name, typeof(RectTransform));
            inputObject.transform.SetParent(menuObject.transform, false);
            InputField input = inputObject.AddComponent<InputField>();
            input.textComponent = CreateText("Text");
            return input;
        }

        Text CreateText(string name)
        {
            GameObject textObject = new(name, typeof(RectTransform));
            textObject.transform.SetParent(menuObject.transform, false);
            return textObject.AddComponent<Text>();
        }
    }
}
