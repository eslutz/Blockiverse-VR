using System;
using System.Collections;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Persistence;
using Blockiverse.UI;
using Blockiverse.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class MenuFlowPlayModeTests
    {
        private BlockiverseMenuController menu;
        private BlockiverseWorldSessionController session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Load Boot scene to get a clean rig
            SceneManager.LoadScene("Assets/Blockiverse/Scenes/Boot.unity");
            yield return null;

            menu = UnityEngine.Object.FindAnyObjectByType<BlockiverseMenuController>();
            session = UnityEngine.Object.FindAnyObjectByType<BlockiverseWorldSessionController>();
            
            Assert.That(menu, Is.Not.Null);
            Assert.That(session, Is.Not.Null);

            // Ensure we are at Title
            menu.ShowTitleScreen();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Flow_Title_NewWorld_Gameplay()
        {
            Assert.That(menu.IsActiveScreen(MenuActions.TitleScreen), Is.True);
            
            // 1. Title -> New World
            InvokeHandleAction(MenuActions.TitleNewWorld);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.NewWorldScreen), Is.True);

            // 2. Configure and Create
            var config = menu.PendingNewWorldConfig;
            config.SetName("PlayMode Test World");
            config.CycleWorldPreset(true); // From SurvivalTerrain to FlatBuilder
            
            InvokeHandleAction(MenuActions.NewWorldCreate);
            
            // 3. Wait for loading -> Gameplay
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!menu.IsActiveScreen(MenuActions.GameplayHudScreen) && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(menu.IsActiveScreen(MenuActions.GameplayHudScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [UnityTest]
        public IEnumerator Flow_Pause_Settings_Return()
        {
            // Start a world first
            yield return Flow_Title_NewWorld_Gameplay();

            // 1. Pause
            menu.OnMenuPressed();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.PauseScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            // 2. Settings
            InvokeHandleAction(MenuActions.PauseSettings);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.SettingsScreen), Is.True);

            // 3. Audio Subpanel
            InvokeHandleAction(MenuActions.SettingsOpenAudio);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.AudioSettingsScreen), Is.True);

            // 4. Return to Settings
            menu.CloseAudioSettingsScreen();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.SettingsScreen), Is.True);

            // 5. Return to Pause
            menu.CloseSettingsScreen();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.PauseScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            // 6. Resume
            InvokeHandleAction(MenuActions.PauseResume);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.GameplayHudScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [UnityTest]
        public IEnumerator Flow_Pause_CreativeTools()
        {
            yield return Flow_Title_NewWorld_Gameplay();

            menu.OnMenuPressed();
            yield return null;

            InvokeHandleAction(MenuActions.PauseCreativeTools);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.CreativeToolsScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            menu.CloseCreativeToolsScreen();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.PauseScreen), Is.True);
        }

        [UnityTest]
        public IEnumerator Flow_Title_LoadWorld_Details_Gameplay()
        {
            Assert.That(menu.IsActiveScreen(MenuActions.TitleScreen), Is.True);

            // 1. Create a world first so there's something to load
            yield return Flow_Title_NewWorld_Gameplay();
            InvokeHandleAction(MenuActions.PauseReturnToTitle);
            yield return null;
            InvokeHandleAction(MenuActions.ConfirmAccept); // Confirm quit
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.TitleScreen), Is.True);

            // 2. Title -> Load World
            InvokeHandleAction(MenuActions.TitleLoadWorld);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.LoadWorldScreen), Is.True);

            // 3. Load World -> Details
            InvokeHandleAction(MenuActions.LoadWorldDetails);
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.WorldDetailsScreen), Is.True);

            // 4. Details -> Gameplay
            InvokeHandleAction(MenuActions.WorldDetailsPlay);
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!menu.IsActiveScreen(MenuActions.GameplayHudScreen) && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(menu.IsActiveScreen(MenuActions.GameplayHudScreen), Is.True);
        }

        [UnityTest]
        public IEnumerator Flow_LAN_Reconnect_Close()
        {
            Assert.That(menu.IsActiveScreen(MenuActions.TitleScreen), Is.True);

            var lanMenu = UnityEngine.Object.FindAnyObjectByType<BlockiverseMultiplayerSessionMenu>();
            Assert.That(lanMenu, Is.Not.Null);

            var sessionObj = lanMenu.Session;
            Assert.That(sessionObj, Is.Not.Null);

            // 1. Simulate that we connected as client previously
            var hasConnectedProp = typeof(BlockiverseNetworkSession).GetProperty("HasConnectedAsClient", 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            hasConnectedProp.GetSetMethod(nonPublic: true).Invoke(sessionObj, new object[] { true });

            var currentStateProp = typeof(BlockiverseNetworkSession).GetProperty("CurrentState", 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            currentStateProp.GetSetMethod(nonPublic: true).Invoke(sessionObj, new object[] { BlockiverseConnectionState.Disconnected });

            var lastJoinProp = typeof(BlockiverseMultiplayerSessionMenu).GetProperty("LastJoinAddress", 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            lastJoinProp.GetSetMethod(nonPublic: true).Invoke(lanMenu, new object[] { "127.0.0.1" });

            // 2. Wait for Update() to route to LAN menu
            yield return null;

            Assert.That(menu.IsActiveScreen(MenuActions.LanMultiplayerScreen), Is.True);
            Assert.That(lanMenu.IsShowingSessionEndedMessage, Is.True);

            // World input must stay suppressed while the host-left surface owns input.
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            // 3. Close LAN menu
            menu.CloseLanMultiplayerScreen();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.TitleScreen), Is.True);
        }

        [UnityTest]
        public IEnumerator UI_Suppression_BlocksInput()
        {
            yield return Flow_Title_NewWorld_Gameplay();
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);

            // Open Inventory
            menu.OpenInventoryScreen();
            yield return null;
            Assert.That(menu.IsActiveScreen(MenuActions.InventoryScreen), Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            menu.CloseInventoryScreen();
            yield return null;
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        private void InvokeHandleAction(string actionId)
        {
            var method = typeof(BlockiverseMenuController).GetMethod("HandleAction", 
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(menu, new object[] { actionId });
        }
    }
}
