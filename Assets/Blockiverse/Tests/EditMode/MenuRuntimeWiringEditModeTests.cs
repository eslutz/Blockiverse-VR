using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.Voxel;
using Blockiverse.VR;
using Blockiverse.WorldGen;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blockiverse.Tests.EditMode
{
    public sealed class MenuRuntimeWiringEditModeTests
    {
        readonly List<UnityEngine.Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object target in objectsToDestroy)
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void MenuControllerDiscoversGeneratedMenusAndRoutesTitleActionsAtRuntime()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseNewWorldPanel newWorldPanel = CreateGeneratedNewWorldPanel(rig.transform);

            StartMenuController(controller);

            Assert.That(titleMenu.ActionIds[0], Is.EqualTo(MenuActions.TitleNewWorld));

            GetChildComponent<Button>(titleMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.NewWorldScreen));
            Assert.That(newWorldPanel.Config, Is.Not.Null);
        }

        [Test]
        public void DeathWhilePausedRoutesToDeathScreen()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.IsGamePaused, Is.True);
        }

        [Test]
        public void MenuRouterPublishesPauseAndWorldInputState()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);

            StartMenuController(controller);

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.EnterGameplay();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.False);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);

            controller.OnMenuPressed();

            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
        }

        [Test]
        public void WorldLoadingRoutePausesAndKeepsOverlayNonInteractive()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            GameObject loading = CreateChild(rig.transform, "Startup Loading Overlay");
            Canvas loadingCanvas = loading.AddComponent<Canvas>();
            loadingCanvas.enabled = false;
            BlockiverseStartupOverlay startupOverlay = loading.AddComponent<BlockiverseStartupOverlay>();
            BlockiverseWorldSpacePanelPresenter loadingPresenter = loading.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            loadingPresenter.Configure(
                loadingCanvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);
            startupOverlay.Configure(loadingCanvas, loadingPresenter);

            StartMenuController(controller);
            controller.ShowWorldLoadingScreen();

            CanvasGroup loadingInput = loading.GetComponent<CanvasGroup>();
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.WorldLoadingScreen));
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);
            Assert.That(loadingCanvas.enabled, Is.True);
            Assert.That(loadingInput.interactable, Is.False);
            Assert.That(loadingInput.blocksRaycasts, Is.False);
            Assert.That(startupOverlay.HideAutomatically, Is.False);

            controller.EnterGameplay();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(loadingCanvas.enabled, Is.False);
            Assert.That(startupOverlay.HideAutomatically, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void SurvivalHudPresenterFollowsGameplayRoute()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);

            GameObject hud = CreateChild(rig.transform, "Survival HUD");
            Canvas hudCanvas = hud.AddComponent<Canvas>();
            hudCanvas.enabled = true;

            StartMenuController(controller);

            Assert.That(hud.GetComponent<BlockiverseWorldSpacePanelPresenter>(), Is.Not.Null);
            Assert.That(hudCanvas.enabled, Is.False, "Title routing must hide the gameplay HUD.");

            controller.EnterGameplay();

            Assert.That(hudCanvas.enabled, Is.True);

            controller.OnMenuPressed();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.PauseScreen));
            Assert.That(hudCanvas.enabled, Is.False, "Pause routing must hide the gameplay HUD.");
        }

        [Test]
        public void ControllerMappingPresenterShowsOnlyUntilDismissed()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                BlockiverseWorldSpacePanelPresenter firstPresenter = CreatePresenterWithStartGate(
                    "Controller Mapping Popup First",
                    key,
                    out Canvas firstCanvas);

                StartBehaviour(firstPresenter);

                Assert.That(firstCanvas.enabled, Is.True);

                firstPresenter.Hide();

                Assert.That(firstCanvas.enabled, Is.False);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));

                BlockiverseWorldSpacePanelPresenter secondPresenter = CreatePresenterWithStartGate(
                    "Controller Mapping Popup Second",
                    key,
                    out Canvas secondCanvas);

                StartBehaviour(secondPresenter);

                Assert.That(secondCanvas.enabled, Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingRouteOwnsFirstLaunchBeforeTitleMenu()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                AddPresenter(titleMenu.gameObject);
                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                StartMenuController(controller);

                Canvas titleCanvas = titleMenu.GetComponent<Canvas>();
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ControllerMappingScreen));
                Assert.That(mappingCanvas.enabled, Is.True);

                StartBehaviour(mappingPresenter);

                Assert.That(mappingCanvas.enabled, Is.True,
                    "Presenter Start must not hide a controller-mapping route already activated by the menu router.");
                Assert.That(mappingPresenter.GetComponent<CanvasGroup>().interactable, Is.True);
                Assert.That(mappingPresenter.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
                Assert.That(titleCanvas.enabled, Is.False, "The title menu must not sit in front of first-run controls.");

                controller.CloseControllerMappingScreen();

                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(mappingCanvas.enabled, Is.False);
                Assert.That(titleCanvas.enabled, Is.True);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ControllerMappingSeenLaunchesTitleMenuDirectly()
        {
            string key = BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.SetInt(key, 1);

            try
            {
                GameObject rig = CreateRoot("Rig");
                BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
                BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
                BlockiverseWorldSpacePanelPresenter titlePresenter = AddPresenter(titleMenu.gameObject);
                GameObject controllerMapping = CreateChild(rig.transform, "Controller Mapping Popup");
                Canvas mappingCanvas = controllerMapping.AddComponent<Canvas>();
                mappingCanvas.enabled = false;
                BlockiverseWorldSpacePanelPresenter mappingPresenter =
                    controllerMapping.AddComponent<BlockiverseWorldSpacePanelPresenter>();
                mappingPresenter.Configure(
                    mappingCanvas,
                    targetHeadset: null,
                    distance: 1.2f,
                    horizontalOffset: 0.0f,
                    verticalOffset: 0.0f,
                    pitch: 0.0f,
                    showWhenStarted: false,
                    showWhenStartedPlayerPrefsKey: key);

                StartMenuController(controller);

                Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
                Assert.That(mappingCanvas.enabled, Is.False);
                Assert.That(titleMenu.GetComponent<Canvas>().enabled, Is.True);

                StartBehaviour(titlePresenter);

                Assert.That(titleMenu.GetComponent<Canvas>().enabled, Is.True,
                    "Presenter Start must not hide a title route already activated by the menu router.");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void LanSessionEndedRouteClosesThroughRouter()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            GameObject lanPanel = CreateChild(rig.transform, "LAN Multiplayer Panel");
            AddPresenter(lanPanel);
            lanPanel.AddComponent<BlockiverseMultiplayerSessionMenu>();

            StartMenuController(controller);
            controller.EnterGameplay();

            controller.ShowLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.LanMultiplayerScreen));

            controller.CloseLanMultiplayerScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.GameplayHudScreen));
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.True);
        }

        [Test]
        public void SettingsComfortPanelRoutesAndClosesThroughRouter()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu settingsMenu = CreateGeneratedActionMenu(rig.transform, "Settings Panel", 4);
            AddPresenter(settingsMenu.gameObject);

            GameObject comfortObject = CreateChild(rig.transform, "Comfort Settings Menu");
            Canvas comfortCanvas = comfortObject.AddComponent<Canvas>();
            comfortCanvas.enabled = false;
            BlockiverseComfortMenu comfortMenu = comfortObject.AddComponent<BlockiverseComfortMenu>();
            comfortMenu.Configure(comfortCanvas, rig.AddComponent<BlockiverseComfortSettings>());
            BlockiverseWorldSpacePanelPresenter comfortPresenter =
                comfortObject.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            comfortPresenter.Configure(
                comfortCanvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);

            StartMenuController(controller);
            controller.Router.PushScreen(new ScreenRoute(MenuActions.SettingsScreen, pauseGame: true));

            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(comfortCanvas.enabled, Is.False);

            GetChildComponent<Button>(settingsMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.ComfortSettingsScreen));
            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(comfortCanvas.enabled, Is.True);
            Assert.That(BlockiverseRuntimeState.IsGamePaused, Is.True);
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False);

            controller.CloseComfortSettingsScreen();

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.SettingsScreen));
            Assert.That(settingsMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(comfortCanvas.enabled, Is.False);
        }

        [Test]
        public void DeathWhileModalIsOpenClearsModalAndRoutesToDeathScreen()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            Assert.That(controller.Router.HasModal, Is.True);

            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.DeathScreen));
        }

        [Test]
        public void ConfirmModalDisablesUnderlyingScreenInput()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu pauseMenu = CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);
            AddPresenter(pauseMenu.gameObject);
            AddPresenter(confirmMenu.gameObject);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();
            controller.RequestConfirm("Quit?", "Quit", "Cancel", _ => { });

            CanvasGroup pauseInput = pauseMenu.GetComponent<CanvasGroup>();
            CanvasGroup confirmInput = confirmMenu.GetComponent<CanvasGroup>();

            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(pauseMenu.GetComponent<Canvas>().enabled, Is.True, "The underlying screen remains visible.");
            Assert.That(confirmMenu.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(pauseInput.interactable, Is.False);
            Assert.That(pauseInput.blocksRaycasts, Is.False);
            Assert.That(confirmInput.interactable, Is.True);
            Assert.That(confirmInput.blocksRaycasts, Is.True);
        }

        [Test]
        public void TitleQuitOpensConfirmAndCancelSuppressesQuitAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            BlockiverseActionMenu titleMenu = CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            controller.SetSaveAvailability(latestSaveExists: true, anySaveExists: true);
            StartMenuController(controller);

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            int quitIndex = IndexOfAction(titleMenu.ActionIds, MenuActions.TitleQuit);
            Assert.That(quitIndex, Is.GreaterThanOrEqualTo(0));
            GetChildComponent<Button>(titleMenu.transform, $"Panel/Action {quitIndex + 1}").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetChildComponent<TMP_Text>(confirmMenu.transform, "Panel/Title").text,
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void PauseQuitOpensConfirmAndCancelSuppressesSaveAction()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            BlockiverseActionMenu pauseMenu = CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            controller.OnMenuPressed();

            var requestedActions = new List<string>();
            controller.ActionRequested += requestedActions.Add;

            int quitIndex = IndexOfAction(pauseMenu.ActionIds, MenuActions.PauseQuit);
            Assert.That(quitIndex, Is.GreaterThanOrEqualTo(0));
            GetChildComponent<Button>(pauseMenu.transform, $"Panel/Action {quitIndex + 1}").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.True);
            Assert.That(controller.Router.InputTarget, Is.EqualTo(MenuActions.ConfirmModal));
            Assert.That(
                GetChildComponent<TMP_Text>(confirmMenu.transform, "Panel/Title").text,
                Is.EqualTo(BlockiverseLocalization.Text(BlockiverseLocalization.Keys.ConfirmQuitGame)));
            Assert.That(requestedActions, Is.Empty);

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(requestedActions, Is.Empty);
        }

        [Test]
        public void ClearingModalStackClearsPendingConfirmCallback()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu confirmMenu = CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);

            bool accepted = false;
            controller.RequestConfirm("Quit?", "Quit", "Cancel", value => accepted = value);
            controller.EnterGameplay();

            GetChildComponent<Button>(confirmMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(controller.Router.HasModal, Is.False);
            Assert.That(accepted, Is.False, "A confirm callback from a cleared modal must not survive behind hidden UI.");
        }

        [Test]
        public void DeathReturnToTitleRespawnsBeforeSaveActionIsRaised()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            SurvivalVitalsRuntime vitals = rig.AddComponent<SurvivalVitalsRuntime>();
            EnableBehaviour(vitals);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseActionMenu deathMenu = CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);

            StartMenuController(controller);
            controller.EnterGameplay();
            vitals.Vitals.ApplyDamage(vitals.Vitals.MaxHealth);

            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.DeathScreen));
            Assert.That(vitals.Vitals.IsDead, Is.True);

            string requestedAction = null;
            bool wasAliveWhenActionRaised = false;
            controller.ActionRequested += actionId =>
            {
                requestedAction = actionId;
                wasAliveWhenActionRaised = !vitals.Vitals.IsDead;
            };

            GetChildComponent<Button>(deathMenu.transform, "Panel/Action 2").onClick.Invoke();

            Assert.That(requestedAction, Is.EqualTo(MenuActions.DeathReturnToTitle));
            Assert.That(wasAliveWhenActionRaised, Is.True,
                "The session save handler must observe post-respawn vitals.");
            Assert.That(vitals.Vitals.IsDead, Is.False);
            Assert.That(controller.Router.ActiveScreen.ScreenId, Is.EqualTo(MenuActions.TitleScreen));
        }

        [Test]
        public void NewWorldPanelDiscoversGeneratedControlsAndEmitsActionsAtRuntime()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_Text gameModeLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Row Game Mode/Value");
            Assert.That(gameModeLabel.text, Is.EqualTo("Survival"));

            GetChildComponent<Button>(panel.transform, "Panel/Row Game Mode/Next").onClick.Invoke();
            Assert.That(gameModeLabel.text, Is.EqualTo("Creative"));

            TMP_Text textureSetLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Row Texture Set/Value");
            Assert.That(textureSetLabel.text, Is.EqualTo("Enhanced"));

            GetChildComponent<Button>(panel.transform, "Panel/Row Texture Set/Next").onClick.Invoke();
            Assert.That(textureSetLabel.text, Is.EqualTo("AI Simplified"));

            string invoked = null;
            panel.ActionRequested += id => invoked = id;

            GetChildComponent<Button>(panel.transform, "Panel/Create Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.NewWorldCreate));

            GetChildComponent<Button>(panel.transform, "Panel/Cancel Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.NewWorldCancel));
        }

        [Test]
        public void NewWorldPanelConfiguresSeedInputForEditableTextEntry()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            Assert.That(seedInput.interactable, Is.True);
            Assert.That(seedInput.readOnly, Is.False);
            Assert.That(seedInput.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            Assert.That(seedInput.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
            Assert.That(seedInput.characterValidation, Is.EqualTo(TMP_InputField.CharacterValidation.None));
            Assert.That(seedInput.keyboardType, Is.EqualTo(TouchScreenKeyboardType.Default));
            Assert.That(seedInput.GetComponent<BlockiverseSystemKeyboardField>().KeyboardType,
                Is.EqualTo(TouchScreenKeyboardType.Default));

            seedInput.text = "meadow-home";
            seedInput.onValueChanged.Invoke(seedInput.text);

            Assert.That(panel.Config.SeedText, Is.EqualTo("meadow-home"));
            Assert.That(panel.Config.Seed, Is.EqualTo(NewWorldConfig.HashSeed("meadow-home")));
        }

        [Test]
        public void NewWorldTextFieldsAcceptXrPressAndSubmitKeyboardEvents()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            panel.ResolveRuntimeReferences();
            panel.ResetForNewWorld();

            TMP_InputField nameInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Name Input");
            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            AssertKeyboardFieldAcceptsXrPressEvents(nameInput);
            AssertKeyboardFieldAcceptsXrPressEvents(seedInput);
        }

        [Test]
        public void SystemKeyboardFieldOwnsOnlyOneActiveFieldAtATime()
        {
            Type keyboardFieldType = typeof(BlockiverseSystemKeyboardField);

            Assert.That(keyboardFieldType.GetProperty("ActiveField"), Is.Not.Null,
                "The Quest keyboard bridge must expose the single field that owns streamed keyboard text.");
            Assert.That(keyboardFieldType.GetProperty("AnyKeyboardVisible"), Is.Not.Null,
                "Keyboard visibility must be observable so first-person hand visuals can hide while the system keyboard is open.");
            Assert.That(keyboardFieldType.GetEvent("KeyboardVisibilityChanged"), Is.Not.Null,
                "Keyboard visibility changes must be broadcast without coupling the keyboard bridge to avatar internals.");
            Assert.That(typeof(IDeselectHandler).IsAssignableFrom(keyboardFieldType), Is.True,
                "Losing focus must release ownership so a stale field cannot keep streaming keyboard text.");
        }

        [Test]
        public void NewWorldTextFieldsUseInputBackgroundAsOnlyRaycastTarget()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            TMP_InputField nameInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Name Input");
            TMP_InputField seedInput = GetChildComponent<TMP_InputField>(panel.transform, "Panel/Seed Input");

            Assert.That(nameInput.textComponent.raycastTarget, Is.True,
                "Test setup should reproduce generated TMP text that can steal ray hits.");
            Assert.That(seedInput.textComponent.raycastTarget, Is.True,
                "Test setup should reproduce generated TMP text that can steal ray hits.");

            panel.ResolveRuntimeReferences();

            AssertInputFieldRaycastSetup(nameInput);
            AssertInputFieldRaycastSetup(seedInput);
        }

        [Test]
        public void NewWorldPanelCycleButtonsUseWholeButtonAsRaycastTarget()
        {
            BlockiverseNewWorldPanel panel = CreateGeneratedNewWorldPanel(null);
            Button backButton = GetChildComponent<Button>(panel.transform, "Panel/Row Game Mode/Back");
            TMP_Text backLabel = GetChildComponent<TMP_Text>(backButton.transform, "Label");

            Assert.That(backLabel.raycastTarget, Is.True, "Test setup should reproduce generated labels that can steal ray hits.");

            panel.ResolveRuntimeReferences();

            Image buttonImage = backButton.GetComponent<Image>();
            Assert.That(backButton.targetGraphic, Is.SameAs(buttonImage));
            Assert.That(buttonImage.raycastTarget, Is.True);
            Assert.That(backLabel.raycastTarget, Is.False);
        }

        static void AssertKeyboardFieldAcceptsXrPressEvents(TMP_InputField input)
        {
            BlockiverseSystemKeyboardField keyboardField = input.GetComponent<BlockiverseSystemKeyboardField>();
            Assert.That(keyboardField, Is.Not.Null);
            Assert.That(keyboardField, Is.InstanceOf<IPointerDownHandler>(),
                "XR UI sends press feedback through pointer-down; text fields must open the system keyboard on that event.");
            Assert.That(keyboardField, Is.InstanceOf<ISubmitHandler>(),
                "Submit events should also open the system keyboard for controller-driven UI activation.");
        }

        static void AssertInputFieldRaycastSetup(TMP_InputField input)
        {
            Image background = input.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(input.targetGraphic, Is.SameAs(background));
            Assert.That(background.raycastTarget, Is.True);
            Assert.That(input.textComponent.raycastTarget, Is.False);
            Assert.That(((TMP_Text)input.placeholder).raycastTarget, Is.False);
        }

        [Test]
        public void LoadWorldPanelDiscoversGeneratedControlsAndEmitsActionsAtRuntime()
        {
            BlockiverseLoadWorldPanel panel = CreateGeneratedLoadWorldPanel(null);
            panel.ResolveRuntimeReferences();

            var save = new WorldSaveSummary(
                "Meadow Home",
                "918273645",
                "survival",
                "normal",
                12,
                new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

            panel.SetSaves(new[] { save });

            TMP_Text firstEntry = GetChildComponent<TMP_Text>(panel.transform, "Panel/Save 1");
            Assert.That(firstEntry.text, Does.Contain("Meadow Home"));
            Assert.That(firstEntry.text, Does.Contain("Day 12"));
            Assert.That(panel.SelectedSave?.Name, Is.EqualTo("Meadow Home"));

            string invoked = null;
            panel.ActionRequested += id => invoked = id;

            GetChildComponent<Button>(panel.transform, "Panel/Details Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldDetails));

            GetChildComponent<Button>(panel.transform, "Panel/Load Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldLoad));

            GetChildComponent<Button>(panel.transform, "Panel/Cancel Button").onClick.Invoke();
            Assert.That(invoked, Is.EqualTo(MenuActions.LoadWorldCancel));
        }

        [Test]
        public void LoadWorldPanelPagesBeyondGeneratedRows()
        {
            BlockiverseLoadWorldPanel panel = CreateGeneratedLoadWorldPanel(null);
            panel.ResolveRuntimeReferences();

            DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var saves = new List<WorldSaveSummary>();
            for (int i = 1; i <= 8; i++)
            {
                saves.Add(new WorldSaveSummary(
                    $"World {i}",
                    i.ToString(),
                    "survival",
                    "normal",
                    i,
                    created.AddDays(i),
                    created));
            }

            panel.SetSaves(saves);

            TMP_Text pageLabel = GetChildComponent<TMP_Text>(panel.transform, "Panel/Page");
            TMP_Text firstEntry = GetChildComponent<TMP_Text>(panel.transform, "Panel/Save 1");
            Button previousPage = GetChildComponent<Button>(panel.transform, "Panel/Previous Page Button");
            Button nextPage = GetChildComponent<Button>(panel.transform, "Panel/Next Page Button");

            Assert.That(panel.PageCount, Is.EqualTo(2));
            Assert.That(pageLabel.text, Is.EqualTo("Page 1 / 2"));
            Assert.That(firstEntry.text, Does.Contain("World 8"));
            Assert.That(previousPage.interactable, Is.False);
            Assert.That(nextPage.interactable, Is.True);

            nextPage.onClick.Invoke();

            Assert.That(panel.PageIndex, Is.EqualTo(1));
            Assert.That(pageLabel.text, Is.EqualTo("Page 2 / 2"));
            Assert.That(firstEntry.text, Does.Contain("World 2"));
            Assert.That(panel.SelectedSave?.Name, Is.EqualTo("World 2"));
            Assert.That(previousPage.interactable, Is.True);
            Assert.That(nextPage.interactable, Is.False);
        }

        [Test]
        public void StationPanelDiscoversGeneratedControlsAndRoutesButtonsAtRuntime()
        {
            BlockiverseStationPanel panel = CreateGeneratedStationPanel(null);
            GameObject syncObject = CreateRoot("Station Survival Sync");
            MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
            survivalSync.Configure(null, null, null);
            survivalSync.LocalInventory.SetSlot(0, new ItemStack(ItemId.ClayLump, 1));
            panel.ConfigureSurvivalSync(survivalSync);
            panel.ResolveRuntimeReferences();

            var station = new SmeltingStationModel(CraftingStation.ClayKiln, inputSlotCount: 1);
            station.TryDepositInput(new ItemStack(ItemId.ClayLump, 2));
            panel.Open(station, new BlockPosition(1, 2, 3));

            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Title").text, Is.EqualTo("Clay Kiln"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Input Slot 1").text, Is.EqualTo("Clay Lump ×2"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Fuel Slot").text, Is.EqualTo("No fuel"));
            Assert.That(GetChildComponent<TMP_Text>(panel.transform, "Panel/Output Slot").text, Is.EqualTo("—"));

            GetChildComponent<Button>(panel.transform, "Panel/Deposit Input Button").onClick.Invoke();
            Assert.That(
                GetChildComponent<TMP_Text>(panel.transform, "Panel/Status").text,
                Is.EqualTo("Cannot deposit: Not a Station"));

            GetChildComponent<Button>(panel.transform, "Panel/Withdraw Input Button").onClick.Invoke();
            Assert.That(
                GetChildComponent<TMP_Text>(panel.transform, "Panel/Status").text,
                Is.EqualTo("Cannot withdraw: Not a Station"));

            bool closed = false;
            panel.CloseRequested += () => closed = true;
            GetChildComponent<Button>(panel.transform, "Panel/Close Button").onClick.Invoke();
            Assert.That(closed, Is.True);
        }

        [Test]
        public void StationPanelClosesWhenOpenStationIsRemoved()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreativeWorldManager worldManager = rig.AddComponent<CreativeWorldManager>();
            MultiplayerSurvivalSync survivalSync = rig.AddComponent<MultiplayerSurvivalSync>();
            worldManager.Configure(CreateTestChunkMaterial(), BlockiverseProject.InteractionLayerIndex);
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            BlockiverseStationPanel panel = CreateGeneratedStationPanel(rig.transform);
            var stationPosition = new BlockPosition(2, 1, 2);

            BlockRegistry registry = BlockRegistry.CreateDefault();
            var settings = new WorldGenerationSettings(
                width: 6,
                height: 4,
                depth: 6,
                chunkSize: 2,
                seed: 47,
                groundHeight: 1);
            var world = new VoxelWorld(settings.Bounds, settings.ChunkSize, settings.Seed);
            world.SetBlock(stationPosition, BlockRegistry.ClayKiln, trackChange: false);
            worldManager.InitializeGeneratedWorld(new GeneratedCreativeWorld(registry, settings, world, CreativeWorldGenerationPreset.FlatCreative));
            worldManager.SetGameMode(WorldGameMode.Survival);
            survivalSync.Configure(null, null, worldManager);

            StartMenuController(controller);
            controller.EnterGameplay();
            panel.Open(survivalSync.GetOrCreateStationModel(stationPosition, CraftingStation.ClayKiln), stationPosition);

            world.SetBlock(stationPosition, BlockRegistry.Air, trackChange: false);
            survivalSync.TickStations(1);

            Assert.That(panel.IsOpen, Is.False);
        }

        [Test]
        public void WorldDetailsMenuDiscoversGeneratedActionIdsAtRuntime()
        {
            GameObject rig = CreateRoot("Rig");
            BlockiverseMenuController controller = rig.AddComponent<BlockiverseMenuController>();
            CreateGeneratedActionMenu(rig.transform, "Title Menu", 6);
            CreateGeneratedActionMenu(rig.transform, "Pause Menu", 8);
            CreateGeneratedActionMenu(rig.transform, "Death Screen", 3);
            CreateGeneratedActionMenu(rig.transform, "Confirm Dialog", 2);
            (BlockiverseWorldDetailsPanel detailsPanel, BlockiverseActionMenu detailsMenu) =
                CreateGeneratedWorldDetailsPanel(rig.transform);

            StartMenuController(controller);

            detailsPanel.ShowSave(new WorldSaveSummary(
                "Meadow Home",
                "1234",
                "survival",
                "normal",
                4,
                new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)));

            Assert.That(detailsMenu.ActionIds[0], Is.EqualTo(MenuActions.WorldDetailsPlay));
            Assert.That(GetChildComponent<TMP_Text>(detailsMenu.transform, "Panel/Action 1/Label").text, Is.EqualTo("Play"));

            string invoked = null;
            controller.ActionRequested += id => invoked = id;
            GetChildComponent<Button>(detailsMenu.transform, "Panel/Action 1").onClick.Invoke();

            Assert.That(invoked, Is.EqualTo(MenuActions.WorldDetailsPlay));
        }

        [Test]
        public void WorldDetailsMetadataFormatsDatesWithCurrentCulture()
        {
            CultureInfo previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            try
            {
                DateTime created = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
                DateTime lastPlayed = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
                var save = new WorldSaveSummary(
                    "Meadow Home",
                    "1234",
                    "survival",
                    "normal",
                    4,
                    lastPlayed,
                    created);

                string metadata = BlockiverseWorldDetailsPanel.BuildMetadataText(save);

                Assert.That(metadata, Does.Contain(created.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Contain(lastPlayed.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
                Assert.That(metadata, Does.Not.Contain("2026-06-01"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        BlockiverseActionMenu CreateGeneratedActionMenu(Transform parent, string name, int actionCount)
        {
            GameObject root = CreateChild(parent, name);
            BlockiverseActionMenu menu = root.AddComponent<BlockiverseActionMenu>();
            GameObject panel = CreateChild(root.transform, "Panel");
            AddText(panel.transform, "Title");
            AddText(panel.transform, "Status");

            for (int i = 0; i < actionCount; i++)
            {
                GameObject button = CreateChild(panel.transform, $"Action {i + 1}");
                button.AddComponent<Button>();
                AddText(button.transform, "Label");
            }

            return menu;
        }

        BlockiverseNewWorldPanel CreateGeneratedNewWorldPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "New World Panel");
            BlockiverseNewWorldPanel panel = root.AddComponent<BlockiverseNewWorldPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            AddInput(bg.transform, "Name Input");
            AddInput(bg.transform, "Seed Input");

            string[] rows = { "Game Mode", "Difficulty", "World Size", "World Preset", "Starting Biome", "Texture Set" };
            foreach (string row in rows)
            {
                GameObject rowRoot = CreateChild(bg.transform, $"Row {row}");
                AddButton(rowRoot.transform, "Back");
                AddText(rowRoot.transform, "Value");
                AddButton(rowRoot.transform, "Next");
            }

            AddButton(bg.transform, "Create Button");
            AddButton(bg.transform, "Cancel Button");
            AddText(bg.transform, "Error");

            return panel;
        }

        BlockiverseLoadWorldPanel CreateGeneratedLoadWorldPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "Load World Panel");
            BlockiverseLoadWorldPanel panel = root.AddComponent<BlockiverseLoadWorldPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            for (int i = 0; i < 6; i++)
                AddTextButton(bg.transform, $"Save {i + 1}");

            AddText(bg.transform, "Selection");
            AddButton(bg.transform, "Previous Page Button");
            AddText(bg.transform, "Page");
            AddButton(bg.transform, "Next Page Button");
            AddButton(bg.transform, "Load Button");
            AddButton(bg.transform, "Details Button");
            AddButton(bg.transform, "Cancel Button");

            return panel;
        }

        (BlockiverseWorldDetailsPanel, BlockiverseActionMenu) CreateGeneratedWorldDetailsPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "World Details Panel");
            BlockiverseActionMenu menu = root.AddComponent<BlockiverseActionMenu>();
            BlockiverseWorldDetailsPanel detailsPanel = root.AddComponent<BlockiverseWorldDetailsPanel>();
            GameObject panel = CreateChild(root.transform, "Panel");

            AddText(panel.transform, "Title");
            AddText(panel.transform, "Status");
            TMP_Text metadata = AddText(panel.transform, "Metadata");
            TMP_InputField rename = AddInput(panel.transform, "Rename Input");

            for (int i = 0; i < 5; i++)
            {
                GameObject button = CreateChild(panel.transform, $"Action {i + 1}");
                button.AddComponent<Button>();
                AddText(button.transform, "Label");
            }

            detailsPanel.Configure(metadata, rename);
            return (detailsPanel, menu);
        }

        BlockiverseStationPanel CreateGeneratedStationPanel(Transform parent)
        {
            GameObject root = CreateChild(parent, "Station Panel");
            BlockiverseStationPanel panel = root.AddComponent<BlockiverseStationPanel>();
            GameObject bg = CreateChild(root.transform, "Panel");

            AddText(bg.transform, "Title");
            AddText(bg.transform, "Input Slot 1");
            AddText(bg.transform, "Input Slot 2");
            AddText(bg.transform, "Input Slot 3");
            AddText(bg.transform, "Fuel Slot");
            AddText(bg.transform, "Output Slot");
            AddText(bg.transform, "Status");
            CreateChild(bg.transform, "Progress").AddComponent<Slider>();
            AddButton(bg.transform, "Deposit Input Button");
            AddButton(bg.transform, "Deposit Fuel Button");
            AddButton(bg.transform, "Collect Output Button");
            AddButton(bg.transform, "Withdraw Input Button");
            AddButton(bg.transform, "Withdraw Fuel Button");
            AddButton(bg.transform, "Close Button");

            return panel;
        }

        Button AddButton(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            Image image = target.AddComponent<Image>();
            image.raycastTarget = false;
            Button button = target.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text label = AddText(target.transform, "Label");
            label.raycastTarget = true;
            return button;
        }

        TMP_InputField AddInput(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            Image image = target.AddComponent<Image>();
            image.raycastTarget = true;
            var input = target.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textComponent = AddText(target.transform, "Text");
            input.placeholder = AddText(target.transform, "Placeholder");
            target.AddComponent<BlockiverseSystemKeyboardField>().Configure(input);
            return input;
        }

        TMP_Text AddText(Transform parent, string name)
        {
            GameObject target = CreateChild(parent, name);
            return target.AddComponent<TextMeshProUGUI>();
        }

        Button AddTextButton(Transform parent, string name)
        {
            TMP_Text label = AddText(parent, name);
            return label.gameObject.AddComponent<Button>();
        }

        BlockiverseWorldSpacePanelPresenter AddPresenter(GameObject target)
        {
            Canvas canvas = target.AddComponent<Canvas>();
            var presenter = target.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                canvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: false);
            return presenter;
        }

        BlockiverseWorldSpacePanelPresenter CreatePresenterWithStartGate(string name, string key, out Canvas canvas)
        {
            GameObject target = CreateRoot(name);
            canvas = target.AddComponent<Canvas>();
            canvas.enabled = false;
            var presenter = target.AddComponent<BlockiverseWorldSpacePanelPresenter>();
            presenter.Configure(
                canvas,
                targetHeadset: null,
                distance: 1.2f,
                horizontalOffset: 0.0f,
                verticalOffset: 0.0f,
                pitch: 0.0f,
                showWhenStarted: true,
                showWhenStartedPlayerPrefsKey: key);
            return presenter;
        }

        GameObject CreateRoot(string name)
        {
            var target = new GameObject(name);
            objectsToDestroy.Add(target);
            return target;
        }

        GameObject CreateChild(Transform parent, string name)
        {
            var target = new GameObject(name);
            if (parent == null)
                objectsToDestroy.Add(target);
            else
                target.transform.SetParent(parent, false);
            return target;
        }

        T GetChildComponent<T>(Transform root, string path) where T : Component
        {
            Transform target = root.Find(path);
            Assert.That(target, Is.Not.Null, $"Missing generated child '{path}'.");
            T component = target.GetComponent<T>();
            Assert.That(component, Is.Not.Null, $"Missing {typeof(T).Name} on '{path}'.");
            return component;
        }

        static void StartMenuController(BlockiverseMenuController controller)
        {
            StartBehaviour(controller);
        }

        static void StartBehaviour(MonoBehaviour behaviour)
        {
            MethodInfo start = behaviour
                .GetType()
                .GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(start, Is.Not.Null, $"{behaviour.GetType().Name} must expose a Start method for this wiring test.");
            start.Invoke(behaviour, null);
        }

        static void EnableBehaviour(MonoBehaviour behaviour)
        {
            MethodInfo onEnable = behaviour
                .GetType()
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(onEnable, Is.Not.Null, $"{behaviour.GetType().Name} must expose an OnEnable method for this wiring test.");
            onEnable.Invoke(behaviour, null);
        }

        Material CreateTestChunkMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Sprites/Default");
            Assert.That(shader, Is.Not.Null, "EditMode test requires a basic renderable shader.");

            var texture = new Texture2D(BlockVisualAtlas.AtlasWidthPixels, BlockVisualAtlas.AtlasHeightPixels);
            texture.name = BlockVisualAtlas.AuthoredAtlasName;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            var material = new Material(shader)
            {
                mainTexture = texture
            };
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            objectsToDestroy.Add(texture);
            objectsToDestroy.Add(material);
            return material;
        }

        static int IndexOfAction(IReadOnlyList<string> actionIds, string actionId)
        {
            for (int i = 0; i < actionIds.Count; i++)
                if (string.Equals(actionIds[i], actionId, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
