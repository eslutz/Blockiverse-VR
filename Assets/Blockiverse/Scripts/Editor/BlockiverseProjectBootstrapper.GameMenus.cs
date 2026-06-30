using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        const string ControllerMappingText =
            "Dominant trigger: press UI / break\n" +
            "Dominant grip: place / use\n" +
            "Support grip: blocks menu\n" +
            "Menu: pause\n" +
            "Dominant stick: snap turn\n" +
            "Dominant stick click: crouch\n" +
            "Dominant primary button: jump\n" +
            "Dominant secondary button: toggle block editing\n" +
            "Support stick: move\n" +
            "Support stick click: sprint\n" +
            "Either stick hold up: teleport aim, release to land";

        static void EnsureXrRigGameMenus(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            Transform routedMenuParent = cameraOffset;

            var (titleMenu, titlePresenter) = EnsureActionMenuPanel(
                routedMenuParent, BlockiverseMenuController.TitleMenuName, ActionMenuSize, head, buttonCount: 6, sortOrder: 25);
            var (pauseMenu, pausePresenter) = EnsureActionMenuPanel(
                routedMenuParent, BlockiverseMenuController.PauseMenuName, ActionMenuSize, head, buttonCount: 7, sortOrder: 25);
            var (deathMenu, deathPresenter) = EnsureActionMenuPanel(
                routedMenuParent, BlockiverseMenuController.DeathScreenName, ActionMenuSize, head, buttonCount: 3, sortOrder: 25);
            var (confirmMenu, confirmPresenter) = EnsureActionMenuPanel(
                routedMenuParent, BlockiverseMenuController.ConfirmDialogName, ConfirmDialogSize, head, buttonCount: 2, sortOrder: 30);
            var (errorMenu, errorPresenter) = EnsureActionMenuPanel(
                routedMenuParent, BlockiverseMenuController.ErrorDialogName, ErrorDialogSize, head, buttonCount: 1, sortOrder: 30);

            var (newWorldPanel, newWorldPresenter) = EnsureNewWorldMenuPanel(routedMenuParent, head);
            var (loadWorldPanel, loadWorldPresenter) = EnsureLoadWorldMenuPanel(routedMenuParent, head);
            var (settingsMenu, settingsPresenter) = EnsureSettingsMenuPanel(routedMenuParent, head);
            
            Transform comfortRoot = routedMenuParent.Find(BlockiverseMenuController.ComfortSettingsPanelName) ?? cameraOffset.Find(BlockiverseMenuController.ComfortSettingsPanelName);
            BlockiverseWorldSpacePanelPresenter comfortPresenter = comfortRoot?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Button comfortCloseButton = comfortRoot?.Find("Panel/Close Button")?.GetComponent<Button>();
            
            var (stationPanel, stationPresenter) = EnsureStationMenuPanel(routedMenuParent, head);
            var (lanPresenter, lanCloseButton) = EnsureLanMultiplayerMenuPanel(routedMenuParent, head);
            var (audioPanel, audioPresenter, audioCloseButton) = EnsureAudioSettingsMenuPanel(routedMenuParent, head);
            var (controlsPresenter, controlsCloseButton) = EnsureControlsMenuPanel(routedMenuParent, head);
            var (worldDetailsPanel, worldDetailsMenu, worldDetailsPresenter) = EnsureWorldDetailsMenuPanel(routedMenuParent, head);
            var (creativeToolsPanel, creativeToolsPresenter, creativeToolsCloseButton) = EnsureCreativeToolsMenuPanel(routedMenuParent, head);
            
            BlockiverseItemIconLibrary iconLibrary = AssetDatabase.LoadAssetAtPath<BlockiverseItemIconLibrary>(ItemIconLibraryPath);
            var (inventoryPanel, inventoryPresenter, inventoryCloseButton) = EnsureInventoryMenuPanel(routedMenuParent, head, iconLibrary);
            var (craftingPanel, craftingPresenter, craftingCloseButton) = EnsureCraftingMenuPanel(routedMenuParent, head, iconLibrary);
            var (catalogPanel, catalogPresenter, catalogCloseButton) = EnsureCatalogMenuPanel(routedMenuParent, head, iconLibrary);
            var (cratePanel, cratePresenter, crateCloseButton) = EnsureCrateMenuPanel(routedMenuParent, head);

            BlockiverseWorldSpacePanelPresenter gameplayHudPresenter = cameraOffset.Find(BlockiverseMenuController.SurvivalHudName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Transform controllerMappingRoot = routedMenuParent.Find(BlockiverseMenuController.ControllerMappingPopupName) ?? cameraOffset.Find(BlockiverseMenuController.ControllerMappingPopupName);
            BlockiverseWorldSpacePanelPresenter controllerMappingPresenter = controllerMappingRoot?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            BlockiverseWorldSpacePanelPresenter worldLoadingPresenter = cameraOffset.Find(BlockiverseMenuController.StartupLoadingOverlayName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Button controllerMappingCloseButton = controllerMappingRoot?.Find("Panel/Close Button")?.GetComponent<Button>();

            BlockiverseMenuController controller = EnsureComponent<BlockiverseMenuController>(rig);
            controller.Configure(inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu, errorMenu,
                newWorldPanel, loadWorldPanel, settingsMenu, worldDetailsPanel, worldDetailsMenu,
                inventoryPanel, craftingPanel, catalogPanel, creativeToolsPanel, cratePanel);
            controller.ConfigurePresenters(
                titlePresenter, pausePresenter, deathPresenter, confirmPresenter, errorPresenter,
                newWorldPresenter, loadWorldPresenter, settingsPresenter, stationPresenter,
                lanPresenter, audioPresenter, controlsPresenter, worldDetailsPresenter,
                creativeToolsPresenter, gameplayHudPresenter, comfortPresenter, controllerMappingPresenter,
                worldLoadingPresenter, inventoryPresenter, craftingPresenter, catalogPresenter, cratePresenter);
controller.ConfigureStationPanel(stationPanel);

            if (controllerMappingCloseButton != null) { SetSinglePersistentListener(controllerMappingCloseButton.onClick, controller.CloseControllerMappingScreen); EditorUtility.SetDirty(controllerMappingCloseButton); }
            if (comfortCloseButton != null) { SetSinglePersistentListener(comfortCloseButton.onClick, controller.CloseComfortSettingsScreen); EditorUtility.SetDirty(comfortCloseButton); }
            if (creativeToolsCloseButton != null) { SetSinglePersistentListener(creativeToolsCloseButton.onClick, controller.CloseCreativeToolsScreen); EditorUtility.SetDirty(creativeToolsCloseButton); }
            if (lanCloseButton != null) { SetSinglePersistentListener(lanCloseButton.onClick, controller.CloseLanMultiplayerScreen); EditorUtility.SetDirty(lanCloseButton); }
            if (audioCloseButton != null) { SetSinglePersistentListener(audioCloseButton.onClick, controller.CloseAudioSettingsScreen); EditorUtility.SetDirty(audioCloseButton); }
            if (controlsCloseButton != null) { SetSinglePersistentListener(controlsCloseButton.onClick, controller.CloseControlsScreen); EditorUtility.SetDirty(controlsCloseButton); }
            if (inventoryCloseButton != null) { SetSinglePersistentListener(inventoryCloseButton.onClick, controller.CloseInventoryScreen); EditorUtility.SetDirty(inventoryCloseButton); }
            if (craftingCloseButton != null) { SetSinglePersistentListener(craftingCloseButton.onClick, controller.CloseCraftingScreen); EditorUtility.SetDirty(craftingCloseButton); }
            if (catalogCloseButton != null) { SetSinglePersistentListener(catalogCloseButton.onClick, controller.CloseCatalogScreen); EditorUtility.SetDirty(catalogCloseButton); }
            if (crateCloseButton != null) { SetSinglePersistentListener(crateCloseButton.onClick, controller.CloseStationCrateScreen); EditorUtility.SetDirty(crateCloseButton); }

            BlockiverseWorldSessionController sessionController = EnsureComponent<BlockiverseWorldSessionController>(rig);
            BlockiverseMultiplayerSessionMenu lanSessionMenu = lanPresenter != null ? lanPresenter.GetComponent<BlockiverseMultiplayerSessionMenu>() : null;
            if (lanSessionMenu != null) { lanSessionMenu.ConfigureMenuController(controller); lanSessionMenu.ConfigureWorldSessionController(sessionController); EditorUtility.SetDirty(lanSessionMenu); }
            if (inputRig != null) { RemovePersistentListeners(inputRig.MenuPressed, controller, nameof(BlockiverseMenuController.OnMenuPressed)); EditorUtility.SetDirty(inputRig); }
            EnsureGeneratedVrUiPanels(cameraOffset);
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(rig);
        }

        // Routed menu panels live inside the composition menu canvas at rest.On regeneration of an
        // existing prefab they must be migrated back under the camera offset so this authoring pass
        // can reconfigure the SAME object in place — adding new buttons/content and re-wiring — before
        // routing returns it to the canvas. Plain EnsureRectChild instead spawns a fresh duplicate
        // that RouteMenuPanelToCompositionSurface then discards in favor of the stale routed copy,
        // so authoring changes never reach an already-generated prefab. Using the migrating helper
        // preserves each panel's object identity across regenerations and lets edits take effect.
        static GameObject EnsureRoutedMenuPanelRoot(Transform parent, string name)
        {
            return EnsureWorldSpaceMenuRectChild(parent, null, name);
        }

        static Vector2 TopRightClosePosition(float panelWidth) => new Vector2(panelWidth - MenuPanelInset - MenuCloseButtonSize.x, -MenuPanelInset);
        static Vector2 TitleSizeWithClose(float panelWidth, float height = 52.0f) => new Vector2(panelWidth - (MenuPanelInset * 3.0f) - MenuCloseButtonSize.x, height);

        static void ConfigureRoutedMenuPresenter(BlockiverseWorldSpacePanelPresenter presenter, Canvas canvas, Transform head, float scale = GameMenuScale, bool showWhenStarted = false, string showWhenStartedPlayerPrefsKey = null)
        {
            presenter.Configure(canvas, head, GameMenuDistanceMeters, 0.0f, GameMenuVerticalOffsetMeters, GameMenuPitchDegrees, scale, showWhenStarted: showWhenStarted, showWhenStartedPlayerPrefsKey: showWhenStartedPlayerPrefsKey);
        }

        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureLanMultiplayerMenuPanel(Transform parent, Transform head)
        {
            float width = LanMultiplayerPanelSize.x; float height = LanMultiplayerPanelSize.y;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.LanMultiplayerPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.dynamicPixelsPerUnit = 10.0f;
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "LAN Multiplayer", 30, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -18.0f), TitleSizeWithClose(width, 48.0f));
            TMP_InputField addressInput = EnsureInputFieldControl(bg.transform, "Address Input", BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanJoinAddressPlaceholder), string.Empty, new Vector2(28.0f, -100.0f), new Vector2(width - 56.0f, 58.0f));
            Button hostButton = EnsureButtonControl(bg.transform, "Host Button", "Host", new Vector2(28.0f, -180.0f), new Vector2(120.0f, 54.0f));
            Button joinButton = EnsureButtonControl(bg.transform, "Join Button", "Join", new Vector2(168.0f, -180.0f), new Vector2(120.0f, 54.0f));
            Button reconnectButton = EnsureButtonControl(bg.transform, "Reconnect Button", "Reconnect", new Vector2(308.0f, -180.0f), new Vector2(140.0f, 54.0f));
            Button stopButton = EnsureButtonControl(bg.transform, "Stop Button", "Stop", new Vector2(468.0f, -180.0f), new Vector2(120.0f, 54.0f));
            TextMeshProUGUI statusText = EnsureLabel(bg.transform, "Status", BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStoppedWithDefault), 22, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28.0f, -256.0f), new Vector2(width - 56.0f, 120.0f), TextDimColor);
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);
            GameObject badgeObject = EnsureRectChild(bg.transform, "Status Badge");
            ConfigureTopLeftRect(badgeObject.GetComponent<RectTransform>(), new Vector2(width - MenuPanelInset - MenuCloseButtonSize.x - 44.0f, -22.0f), new Vector2(28.0f, 28.0f));
            Image statusBadge = EnsureComponent<Image>(badgeObject);
            ApplySlicedSprite(statusBadge, GetUiSprite("multiplayer_status_badge"));
            statusBadge.color = new Color(0.55f, 0.58f, 0.62f, 1.0f);
            BlockiverseMultiplayerSessionMenu menu = EnsureComponent<BlockiverseMultiplayerSessionMenu>(panelRoot);
            menu.ConfigureControls(hostButton, joinButton, reconnectButton, stopButton, addressInput, statusText);
            menu.ConfigureStatusBadge(statusBadge);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (presenter, closeButton);
        }

        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureActionMenuPanel(Transform parent, string name, Vector2 size, Transform head, int buttonCount = 5, int sortOrder = 25)
        {
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, name);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = sortOrder; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); ApplySlicedSprite(bgImage, GetUiSprite("settings_panel")); bgImage.color = PanelBaseColor;
            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", name, 30, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28.0f, -18.0f), new Vector2(size.x - 56.0f, 48.0f));
            var buttons = new Button[buttonCount]; var labels = new TMP_Text[buttonCount];
            for (int i = 0; i < buttonCount; i++) { float buttonY = -100.0f - i * 58.0f; Button btn = EnsureButtonControl(bg.transform, $"Action {i + 1}", string.Empty, new Vector2(28.0f, buttonY), new Vector2(size.x - 56.0f, 50.0f)); Transform labelTransform = btn.transform.Find("Label"); buttons[i] = btn; labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null; }
            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot); actionMenu.Configure(titleLabel, buttons, labels);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (actionMenu, presenter);
        }

        static (BlockiverseNewWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureNewWorldMenuPanel(Transform parent, Transform head)
        {
            const float W = 620.0f; const float H = 720.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.NewWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); ApplySlicedSprite(bgImage, GetUiSprite("settings_panel")); bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "New World", 30, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(W - 56, 48));
            TMP_InputField nameInput = EnsureInputFieldControl(bg.transform, "Name Input", "Enter name...","New World", new Vector2(186, -96), new Vector2(W - 214, 48));
            TMP_InputField seedInput = EnsureInputFieldControl(bg.transform, "Seed Input", "0", "0", new Vector2(186, -158), new Vector2(W - 214, 48));
            TMP_Text errorLabel = EnsureLabel(bg.transform, "Error", string.Empty, 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -660), new Vector2(W - 56, 44), new Color(0.95f, 0.40f, 0.30f, 1.0f));
            BlockiverseNewWorldPanel panel = EnsureComponent<BlockiverseNewWorldPanel>(panelRoot);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter);
        }

        static (BlockiverseLoadWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureLoadWorldMenuPanel(Transform parent, Transform head)
        {
            const float W = 620.0f; const float H = 600.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.LoadWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Load World", 30, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(W - 56, 48));
            var entryButtons = new Button[6]; var entryLabels = new TMP_Text[6];
            for (int i = 0; i < 6; i++) { float rowY = -96 - i * 54; entryLabels[i] = EnsureLabel(bg.transform, $"Save {i + 1}", string.Empty, 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, rowY), new Vector2(W - 56, 48)); entryButtons[i] = EnsureTextButton(entryLabels[i]); }
            TMP_Text selectionLabel = EnsureLabel(bg.transform, "Selection", "No save selected", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -420), new Vector2(W - 56, 36));
            Button loadButton = EnsureButtonControl(bg.transform, "Load Button", "Load World", new Vector2(28, -500), new Vector2(150, 52));
            Button cancelButton = EnsureButtonControl(bg.transform, "Cancel Button", "Cancel", new Vector2(196, -500), new Vector2(150, 52));
            BlockiverseLoadWorldPanel panel = EnsureComponent<BlockiverseLoadWorldPanel>(panelRoot);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter);
        }

        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureSettingsMenuPanel(Transform parent, Transform head)
        {
            Transform stale = parent.Find(BlockiverseMenuController.SettingsPanelName);
            if (stale != null && stale.Find("Panel/Placeholder") != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
            (BlockiverseActionMenu settingsMenu, BlockiverseWorldSpacePanelPresenter presenter) = EnsureActionMenuPanel(parent, BlockiverseMenuController.SettingsPanelName, ActionMenuSize, head, buttonCount: 4, sortOrder: 25);
            settingsMenu.SetMenu("Settings", MenuActions.Settings);
            return (settingsMenu, presenter);
        }

        static (BlockiverseAudioSettingsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureAudioSettingsMenuPanel(Transform parent, Transform head)
        {
            const float W = 540.0f; const float H = 956.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.AudioSettingsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); ApplySlicedSprite(bgImage, GetUiSprite("settings_panel")); bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Audio & Feedback", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));
            Slider master = EnsureSettingsSlider(bg.transform, "Master Volume Slider", "Master Volume", 1.0f, new Vector2(28, -96));
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(W), MenuCloseButtonSize);
            BlockiverseAudioSettingsPanel panel = EnsureComponent<BlockiverseAudioSettingsPanel>(panelRoot);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static (BlockiverseCreativeToolsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureCreativeToolsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f; const float H = 820.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.CreativeToolsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Creative Tools", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Aim at blocks to select corners.", 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -78), new Vector2(W - 56, 34), TextDimColor);
            TMP_Text cornersLabel = EnsureLabel(bg.transform, "Corners", "A: —    B: —", 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -116), new Vector2(W - 56, 32));
            
            Button setAButton = EnsureButtonControl(bg.transform, "Set A Button", "Set A", new Vector2(28, -156), new Vector2(150, 48));
            Button setBButton = EnsureButtonControl(bg.transform, "Set B Button", "Set B", new Vector2(190, -156), new Vector2(150, 48));
            
            EnsureLabel(bg.transform, "Region Edit Label", "Region Operations", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -220), new Vector2(200, 32));
            Button fillButton = EnsureButtonControl(bg.transform, "Fill Button", "Fill", new Vector2(28, -260), new Vector2(100, 48));
            Button replaceButton = EnsureButtonControl(bg.transform, "Replace Button", "Replace", new Vector2(136, -260), new Vector2(100, 48));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(244, -260), new Vector2(100, 48));
            Button copyButton = EnsureButtonControl(bg.transform, "Copy Button", "Copy", new Vector2(352, -260), new Vector2(80, 48));
            Button pasteButton = EnsureButtonControl(bg.transform, "Paste Button", "Paste", new Vector2(440, -260), new Vector2(80, 48));
            
            Button undoButton = EnsureButtonControl(bg.transform, "Undo Button", "Undo", new Vector2(28, -316), new Vector2(100, 48));
            Button redoButton = EnsureButtonControl(bg.transform, "Redo Button", "Redo", new Vector2(136, -316), new Vector2(100, 48));

            EnsureLabel(bg.transform, "Environment Label", "Environment", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -380), new Vector2(200, 32));
            Slider timeSlider = EnsureSettingsSlider(bg.transform, "Time Slider", "Time of Day", 0.5f, new Vector2(28, -420), minValue: 0, maxValue: 1);
            Slider scaleSlider = EnsureSettingsSlider(bg.transform, "Time Scale Slider", "Cycle Speed", 1.0f, new Vector2(28, -480), minValue: 0, maxValue: 10);
            Button weatherButton = EnsureButtonControl(bg.transform, "Weather Button", "Cycle Weather", new Vector2(28, -540), new Vector2(W - 56, 48));
            TMP_Text weatherText = EnsureLabel(bg.transform, "Weather Label", "Weather: Clear", 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -600), new Vector2(W - 56, 32));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(W), MenuCloseButtonSize);
            
            BlockiverseCreativeToolsPanel panel = EnsureComponent<BlockiverseCreativeToolsPanel>(panelRoot);
            panel.Configure(null, null, null, cornersLabel, statusLabel, weatherText, timeSlider, scaleSlider);
            
            UnityEventTools.AddPersistentListener(setAButton.onClick, panel.SetCornerA);
            UnityEventTools.AddPersistentListener(setBButton.onClick, panel.SetCornerB);
            UnityEventTools.AddPersistentListener(fillButton.onClick, panel.FillRegion);
            UnityEventTools.AddPersistentListener(replaceButton.onClick, panel.ReplaceRegion);
            UnityEventTools.AddPersistentListener(deleteButton.onClick, panel.DeleteRegion);
            UnityEventTools.AddPersistentListener(copyButton.onClick, panel.CopyRegion);
            UnityEventTools.AddPersistentListener(pasteButton.onClick, panel.PasteRegion);
            UnityEventTools.AddPersistentListener(undoButton.onClick, panel.UndoEdit);
            UnityEventTools.AddPersistentListener(redoButton.onClick, panel.RedoEdit);
            UnityEventTools.AddPersistentListener(weatherButton.onClick, panel.CycleWeather);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static (SurvivalInventoryPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureInventoryMenuPanel(Transform parent, Transform head, BlockiverseItemIconLibrary iconLibrary)
        {
            float width = InventoryPanelSize.x; float height = InventoryPanelSize.y;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.InventoryPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Inventory", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(width));
            TMP_Text selectedHotbarLabel = EnsureLabel(bg.transform, "Selected Hotbar", "Hotbar 1 / 10", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -78), new Vector2(width - 56, 32));
            TMP_Text pageLabel = EnsureLabel(bg.transform, "Page Label", "Slots 1-10 / 44", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 48), new Vector2(200, 32), TextDimColor);
            Button prevPageButton = EnsureButtonControl(bg.transform, "Prev Page Button", "<", new Vector2(28, -height + 68), new Vector2(60, 48));
            Button nextPageButton = EnsureButtonControl(bg.transform, "Next Page Button", ">", new Vector2(width - 88, -height + 68), new Vector2(60, 48));
            TMP_Text[] slotLabels = new TMP_Text[10]; Button[] slotButtons = new Button[10];
            for (int i = 0; i < 10; i++) { slotLabels[i] = EnsureLabel(bg.transform, $"Slot {i + 1}", "Empty", 18, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -120 - i * 42), new Vector2(width - 56, 36)); slotButtons[i] = EnsureTextButton(slotLabels[i]); }
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);
            SurvivalInventoryPanel panel = EnsureComponent<SurvivalInventoryPanel>(panelRoot);
            panel.Configure(slotButtons, slotLabels, selectedHotbarLabel, targetPreviousPageButton: prevPageButton, targetNextPageButton: nextPageButton, targetPageLabel: pageLabel);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static (SurvivalCraftingPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureCraftingMenuPanel(Transform parent, Transform head, BlockiverseItemIconLibrary iconLibrary)
        {
            float width = CraftingPanelSize.x; float height = CraftingPanelSize.y;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.CraftingPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Crafting", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(width));
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Ready", 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -78), new Vector2(width - 56, 32), TextDimColor);
            TMP_Text pageLabel = EnsureLabel(bg.transform, "Page Label", "1/1", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 96), new Vector2(100, 32), TextDimColor);
            Button prevButton = EnsureButtonControl(bg.transform, "Prev Page Button", "<", new Vector2(28, -height + 116), new Vector2(60, 48));
            Button nextButton = EnsureButtonControl(bg.transform, "Next Page Button", ">", new Vector2(width - 88, -height + 116), new Vector2(60, 48));
            TMP_Text[] recipeLabels = new TMP_Text[8]; Button[] recipeButtons = new Button[8];
            for (int i = 0; i < 8; i++) { recipeLabels[i] = EnsureLabel(bg.transform, $"Recipe {i + 1}", "Empty", 18, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -120 - i * 42), new Vector2(width - 56, 36)); recipeButtons[i] = EnsureTextButton(recipeLabels[i]); }
            Button repairButton = EnsureButtonControl(bg.transform, "Repair Button", "Repair Held Tool", new Vector2(28, -height + 48), new Vector2(width - 56, 48));
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);
            SurvivalCraftingPanel panel = EnsureComponent<SurvivalCraftingPanel>(panelRoot);
            panel.Configure(recipeButtons, recipeLabels, statusLabel);
            panel.ConfigurePaging(prevButton, nextButton, pageLabel);
            panel.ConfigureRepairButton(repairButton);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static (BlockiverseCatalogBrowserPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureCatalogMenuPanel(Transform parent, Transform head, BlockiverseItemIconLibrary iconLibrary)
        {
            float width = CatalogPanelSize.x; float height = CatalogPanelSize.y;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.CatalogPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Catalog", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(width));
            Button categoryButton = EnsureButtonControl(bg.transform, "Category Button", "Category", new Vector2(28, -78), new Vector2(150, 48));
            TMP_Text categoryLabel = EnsureLabel(bg.transform, "Category Label", "Terrain", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(196, -78), new Vector2(200, 48));
            TMP_InputField searchField = EnsureInputFieldControl(bg.transform, "Search Field", "Search blocks…", string.Empty, new Vector2(28, -136), new Vector2(width - 56, 48));
            TMP_Text pageLabel = EnsureLabel(bg.transform, "Page Label", "1/1", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 48), new Vector2(100, 32), TextDimColor);
            Button prevPageButton = EnsureButtonControl(bg.transform, "Prev Page Button", "<", new Vector2(28, -height + 68), new Vector2(60, 48));
            Button nextPageButton = EnsureButtonControl(bg.transform, "Next Page Button", ">", new Vector2(width - 88, -height + 68), new Vector2(60, 48));
            TMP_Text[] entryLabels = new TMP_Text[12]; Button[] entryButtons = new Button[12];
            for (int i = 0; i < 12; i++) { int col = i % 3; int row = i / 3; entryButtons[i] = EnsureButtonControl(bg.transform, $"Entry {i + 1}", string.Empty, new Vector2(28 + col * 172, -200 - row * 54), new Vector2(164, 46)); entryLabels[i] = entryButtons[i].transform.Find("Label")?.GetComponent<TMP_Text>(); if (entryLabels[i] != null) entryLabels[i].fontSize = 18; }
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);
            BlockiverseCatalogBrowserPanel panel = EnsureComponent<BlockiverseCatalogBrowserPanel>(panelRoot);
            panel.Configure(null, categoryLabel, pageLabel, searchField, entryButtons, entryLabels);
            WireButton(categoryButton, panel, nameof(BlockiverseCatalogBrowserPanel.CycleCategory), panel.CycleCategory);
            WireButton(prevPageButton, panel, nameof(BlockiverseCatalogBrowserPanel.PreviousPage), panel.PreviousPage);
            WireButton(nextPageButton, panel, nameof(BlockiverseCatalogBrowserPanel.NextPage), panel.NextPage);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static (SurvivalCratePanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureCrateMenuPanel(Transform parent, Transform head)
        {
            float width = CratePanelSize.x; float height = CratePanelSize.y;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.CratePanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Shared Crate", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(width));
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Shared crate", 20, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -78), new Vector2(width - 56, 32), TextDimColor);
            TMP_Text[] slotLabels = new TMP_Text[8]; Button[] slotButtons = new Button[8];
            for (int i = 0; i < 8; i++) { slotLabels[i] = EnsureLabel(bg.transform, $"Slot {i + 1}", "Empty", 18, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -120 - i * 42), new Vector2(width - 56, 36)); slotButtons[i] = EnsureTextButton(slotLabels[i]); }
            Button depositButton = EnsureButtonControl(bg.transform, "Deposit Button", "Deposit Held", new Vector2(28, -height + 48), new Vector2(width - 56, 48));
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);
            SurvivalCratePanel panel = EnsureComponent<SurvivalCratePanel>(panelRoot);
            panel.Configure(slotButtons, slotLabels, statusLabel, depositButton);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            return (panel, presenter, closeButton);
        }

        static void WireButton(Button button, UnityEngine.Object target, string methodName, UnityEngine.Events.UnityAction action)
        {
            RemovePersistentListeners(button.onClick, target, methodName);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureControlsMenuPanel(Transform parent, Transform head)
        {
            const float W = 540.0f; const float H = 480.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.ControlsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.dynamicPixelsPerUnit = 10.0f;
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); ApplySlicedSprite(bgImage, GetUiSprite("settings_panel")); bgImage.color = PanelBaseColor;
            EnsureLabel(bg.transform, "Title", "Controls", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));
            EnsureLabel(bg.transform, "Mapping Text", ControllerMappingText, 22, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -96), new Vector2(W - 56, 290));
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(W), MenuCloseButtonSize);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);
            return (presenter, closeButton);
        }

        static (BlockiverseWorldDetailsPanel panel, BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureWorldDetailsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f; const float H = 620.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.WorldDetailsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>(); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.dynamicPixelsPerUnit = 10.0f;
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>(); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg); Sprite rounded = GetRoundedSprite(); if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; } bgImage.color = PanelBaseColor;
            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "World Details", 32, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(W - 56, 52));
            TMP_Text metadataLabel = EnsureLabel(bg.transform, "Metadata", string.Empty, 22, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -96), new Vector2(W - 56, 120));
            TMP_InputField renameField = EnsureInputFieldControl(bg.transform, "Rename Field", "World name", string.Empty, new Vector2(28, -274), new Vector2(W - 56, 56));
            Button playButton = EnsureButtonControl(bg.transform, "Play Button", "Play", new Vector2(28, -356), new Vector2(150, 52));
            Button renameButton = EnsureButtonControl(bg.transform, "Rename Button", "Rename", new Vector2(196, -356), new Vector2(150, 52));
            Button duplicateButton = EnsureButtonControl(bg.transform, "Duplicate Button", "Duplicate", new Vector2(364, -356), new Vector2(150, 52));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(28, -424), new Vector2(150, 52));
            Button backButton = EnsureButtonControl(bg.transform, "Back Button", "Back", new Vector2(196, -424), new Vector2(150, 52));
            var buttons = new[] { playButton, renameButton, duplicateButton, deleteButton, backButton };
            var labels = new TMP_Text[buttons.Length];
            for (int i = 0; i < buttons.Length; i++) { Transform labelTransform = buttons[i].transform.Find("Label"); labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null; }
            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot);
            actionMenu.Configure(titleLabel, buttons, labels);
            actionMenu.SetMenu("World Details", MenuActions.WorldDetails);
            BlockiverseWorldDetailsPanel panel = EnsureComponent<BlockiverseWorldDetailsPanel>(panelRoot);
            panel.Configure(metadataLabel, renameField);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);
            return (panel, actionMenu, presenter);
        }

        static Slider EnsureSettingsSlider(Transform parent, string name, string label, float value, Vector2 anchoredPosition, float minValue = 0.0f, float maxValue = 1.0f)
        {
            GameObject rowObject = EnsureRectChild(parent, name);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(484.0f, 88.0f));
            EnsureLabel(rowObject.transform, "Label", label, 26, TextAnchor.MiddleLeft, new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 0.0f), new Vector2(300.0f, 40.0f), TextDimColor);
            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -46.0f), new Vector2(484.0f, 36.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight; slider.minValue = minValue; slider.maxValue = maxValue; slider.wholeNumbers = false;
            Sprite roundedSprite = GetRoundedSprite();
            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.30f); backgroundRect.anchorMax = new Vector2(1.0f, 0.70f); backgroundRect.offsetMin = Vector2.zero; backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            if (roundedSprite != null) { background.sprite = roundedSprite; background.type = Image.Type.Sliced; }
            background.color = ControlNormalColor;
            ConfigureUiRaycastBlocker(background);
            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero; fillAreaRect.anchorMax = Vector2.one; fillAreaRect.offsetMin = new Vector2(10.0f, 0.0f); fillAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);
            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.30f); fillRect.anchorMax = new Vector2(1.0f, 0.70f); fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            if (roundedSprite != null) { fill.sprite = roundedSprite; fill.type = Image.Type.Sliced; }
            fill.color = AccentColor; fill.raycastTarget = false;
            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero; handleAreaRect.anchorMax = Vector2.one; handleAreaRect.offsetMin = new Vector2(10.0f, 0.0f); handleAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);
            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f); handleRect.anchorMax = new Vector2(0.0f, 0.5f); handleRect.sizeDelta = new Vector2(36.0f, 36.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            Sprite knobSprite = GetUiControlSprite("slider_knob");
            if (knobSprite != null) handle.sprite = knobSprite;
            handle.color = TextPrimaryColor; ConfigureUiRaycastBlocker(handle);
            slider.fillRect = fillRect; slider.handleRect = handleRect; slider.targetGraphic = handle;
            slider.colors = new ColorBlock { normalColor = TextPrimaryColor, highlightedColor = AccentHighlightColor, pressedColor = AccentColor, selectedColor = AccentColor, disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f), colorMultiplier = 1.0f, fadeDuration = 0.08f };
            slider.value = value; ConfigureSelectableFeedback(slider);
            return slider;
        }

        static (BlockiverseStationPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureStationMenuPanel(Transform parent, Transform head)
        {
            const float W = 540.0f; const float H = 620.0f;
            GameObject panelRoot = EnsureRoutedMenuPanelRoot(parent, BlockiverseMenuController.StationPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition; panelRoot.transform.localRotation = Quaternion.identity; panelRoot.transform.localScale = Vector3.one * GameMenuScale;
            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W); rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);
            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 25; canvas.enabled = false; ConfigureCanvasWorldCamera(canvas, head);
            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;
            EnsureTrackedDeviceRaycaster(panelRoot);
            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1); headerRect.anchorMax = new Vector2(1, 1); headerRect.pivot = new Vector2(0, 1); headerRect.anchoredPosition = Vector2.zero; headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;
            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "Station", 30, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(MenuPanelInset, -18), TitleSizeWithClose(W, 48.0f));
            var inputLabels = new TMP_Text[SmeltingStationModel.MaxInputSlots];
            for (int i = 0; i < inputLabels.Length; i++) { inputLabels[i] = EnsureLabel(bg.transform, $"Input Slot {i + 1}", "—", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(200, -94 - i * 46), new Vector2(W - 228, 40)); }
            TMP_Text fuelLabel = EnsureLabel(bg.transform, "Fuel Slot", "No fuel", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(200, -240), new Vector2(W - 228, 40));
            TMP_Text outputLabel = EnsureLabel(bg.transform, "Output Slot", "—", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(200, -290), new Vector2(W - 228, 40));
            Slider progressSlider = EnsureHudSlider(bg.transform, "Progress", new Vector2(28, -354), new Vector2(W - 56, 20));
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Idle", 22, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -394), new Vector2(W - 56, 36), TextDimColor);
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(W), MenuCloseButtonSize);
            BlockiverseStationPanel stationPanel = EnsureComponent<BlockiverseStationPanel>(panelRoot);
            stationPanel.Configure(titleLabel, inputLabels, fuelLabel, outputLabel, statusLabel, progressSlider, closeButton);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            ConfigureRoutedMenuPresenter(presenter, canvas, head);
            presenter.ConfigureFeedback(BlockiverseAudioCue.ContainerOpen, BlockiverseAudioCue.ContainerClose);
            return (stationPanel, presenter);
        }
    }
}
