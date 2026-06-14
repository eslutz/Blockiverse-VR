using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using Oculus.Avatar2;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureXrRigGameMenus(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            var (titleMenu, titlePresenter) = EnsureActionMenuPanel(
                cameraOffset, TitleMenuName, ActionMenuSize, head, buttonCount: 6, sortOrder: 25);
            var (pauseMenu, pausePresenter) = EnsureActionMenuPanel(
                cameraOffset, PauseMenuName, ActionMenuSize, head, buttonCount: 7, sortOrder: 25);
            var (deathMenu, deathPresenter) = EnsureActionMenuPanel(
                cameraOffset, DeathScreenName, ActionMenuSize, head, buttonCount: 3, sortOrder: 25);
            var (confirmMenu, confirmPresenter) = EnsureActionMenuPanel(
                cameraOffset, ConfirmDialogName, ConfirmDialogSize, head, buttonCount: 2, sortOrder: 30);

            var (newWorldPanel, newWorldPresenter) = EnsureNewWorldMenuPanel(cameraOffset, head);
            var (loadWorldPanel, loadWorldPresenter) = EnsureLoadWorldMenuPanel(cameraOffset, head);
            var (settingsMenu, settingsPresenter) = EnsureSettingsMenuPanel(cameraOffset, head);
            BlockiverseWorldSpacePanelPresenter comfortPresenter =
                cameraOffset.Find(ComfortMenuName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Button comfortCloseButton =
                cameraOffset.Find($"{ComfortMenuName}/Panel/Close Button")?.GetComponent<Button>();
            var (stationPanel, stationPresenter) = EnsureStationMenuPanel(cameraOffset, head);
            var (lanPresenter, lanCloseButton) = EnsureLanMultiplayerMenuPanel(cameraOffset, head);
            var (audioPanel, audioPresenter, audioCloseButton) = EnsureAudioSettingsMenuPanel(cameraOffset, head);
            var (controlsPresenter, controlsCloseButton) = EnsureControlsMenuPanel(cameraOffset, head);
            var (worldDetailsPanel, worldDetailsMenu, worldDetailsPresenter) = EnsureWorldDetailsMenuPanel(cameraOffset, head);
            var (creativeToolsPanel, creativeToolsPresenter, creativeToolsCloseButton) = EnsureCreativeToolsMenuPanel(cameraOffset, head);
            BlockiverseWorldSpacePanelPresenter gameplayHudPresenter =
                cameraOffset.Find(SurvivalHudName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            BlockiverseWorldSpacePanelPresenter controllerMappingPresenter =
                cameraOffset.Find(ControllerMappingPopupName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            BlockiverseWorldSpacePanelPresenter worldLoadingPresenter =
                cameraOffset.Find(StartupLoadingOverlayName)?.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            Button controllerMappingCloseButton =
                cameraOffset.Find($"{ControllerMappingPopupName}/Panel/Close Button")?.GetComponent<Button>();

            BlockiverseMenuController controller = EnsureComponent<BlockiverseMenuController>(rig);
            controller.Configure(inputRig, titleMenu, pauseMenu, deathMenu, confirmMenu,
                newWorldPanel, loadWorldPanel, settingsMenu, worldDetailsPanel, worldDetailsMenu);
            controller.ConfigurePresenters(
                titlePresenter, pausePresenter, deathPresenter, confirmPresenter,
                newWorldPresenter, loadWorldPresenter, settingsPresenter, stationPresenter,
                lanPresenter, audioPresenter, controlsPresenter, worldDetailsPresenter,
                creativeToolsPresenter, gameplayHudPresenter, comfortPresenter, controllerMappingPresenter,
                worldLoadingPresenter);
            controller.ConfigureStationPanel(stationPanel);

            if (controllerMappingCloseButton != null)
            {
                if (controllerMappingPresenter != null)
                    RemovePersistentListeners(
                        controllerMappingCloseButton.onClick,
                        controllerMappingPresenter,
                        nameof(BlockiverseWorldSpacePanelPresenter.Hide));
                RemovePersistentListeners(
                    controllerMappingCloseButton.onClick,
                    controller,
                    nameof(BlockiverseMenuController.CloseControllerMappingScreen));
                UnityEventTools.AddPersistentListener(
                    controllerMappingCloseButton.onClick,
                    controller.CloseControllerMappingScreen);
                EditorUtility.SetDirty(controllerMappingCloseButton);
            }

            if (comfortCloseButton != null)
            {
                RemovePersistentListeners(comfortCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseComfortSettingsScreen));
                UnityEventTools.AddPersistentListener(comfortCloseButton.onClick, controller.CloseComfortSettingsScreen);
                EditorUtility.SetDirty(comfortCloseButton);
            }

            if (creativeToolsCloseButton != null)
            {
                RemovePersistentListeners(creativeToolsCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseCreativeToolsScreen));
                UnityEventTools.AddPersistentListener(creativeToolsCloseButton.onClick, controller.CloseCreativeToolsScreen);
                EditorUtility.SetDirty(creativeToolsCloseButton);
            }

            EditorUtility.SetDirty(creativeToolsPanel);

            if (lanCloseButton != null)
            {
                RemovePersistentListeners(lanCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseLanMultiplayerScreen));
                UnityEventTools.AddPersistentListener(lanCloseButton.onClick, controller.CloseLanMultiplayerScreen);
                EditorUtility.SetDirty(lanCloseButton);
            }

            if (audioCloseButton != null)
            {
                RemovePersistentListeners(audioCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseAudioSettingsScreen));
                UnityEventTools.AddPersistentListener(audioCloseButton.onClick, controller.CloseAudioSettingsScreen);
                EditorUtility.SetDirty(audioCloseButton);
            }

            if (controlsCloseButton != null)
            {
                RemovePersistentListeners(controlsCloseButton.onClick, controller, nameof(BlockiverseMenuController.CloseControlsScreen));
                UnityEventTools.AddPersistentListener(controlsCloseButton.onClick, controller.CloseControlsScreen);
                EditorUtility.SetDirty(controlsCloseButton);
            }

            EditorUtility.SetDirty(audioPanel);

            // The session coordinator implements the menu's save/load/new-world/continue verbs.
            BlockiverseWorldSessionController sessionController = EnsureComponent<BlockiverseWorldSessionController>(rig);
            EditorUtility.SetDirty(sessionController);

            BlockiverseMultiplayerSessionMenu lanSessionMenu =
                lanPresenter != null ? lanPresenter.GetComponent<BlockiverseMultiplayerSessionMenu>() : null;
            if (lanSessionMenu != null)
            {
                lanSessionMenu.ConfigureMenuController(controller);
                lanSessionMenu.ConfigureWorldSessionController(sessionController);
                EditorUtility.SetDirty(lanSessionMenu);
            }

            if (inputRig != null)
            {
                // The controller subscribes to MenuPressed at runtime (Start → AddListener), so a
                // persistent listener here would double-fire the pause toggle. Only scrub any stale
                // persistent listener a previous bootstrap left on the prefab.
                RemovePersistentListeners(inputRig.MenuPressed, controller, nameof(BlockiverseMenuController.OnMenuPressed));
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(rig);
        }

        static Vector2 TopRightClosePosition(float panelWidth)
        {
            return new Vector2(panelWidth - MenuPanelInset - MenuCloseButtonSize.x, -MenuPanelInset);
        }

        static Vector2 TitleSizeWithClose(float panelWidth, float height = 52.0f)
        {
            return new Vector2(panelWidth - (MenuPanelInset * 3.0f) - MenuCloseButtonSize.x, height);
        }

        // Builds the LAN multiplayer panel on the rig: host/join/stop controls plus a close
        // button, presented through the same world-space presenter stack as the other menus.
        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureLanMultiplayerMenuPanel(
            Transform parent,
            Transform head)
        {
            float width = LanMultiplayerPanelSize.x;
            float height = LanMultiplayerPanelSize.y;

            GameObject panelRoot = EnsureRectChild(parent, LanMultiplayerPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(
                bg.transform, "Title", "LAN Multiplayer", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(MenuPanelInset, -18.0f), TitleSizeWithClose(width, 48.0f));

            TMP_InputField addressInput = EnsureInputFieldControl(
                bg.transform,
                "Address Input",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanJoinAddressPlaceholder),
                string.Empty,
                new Vector2(28.0f, -100.0f),
                new Vector2(width - 56.0f, 58.0f));

            Button hostButton = EnsureButtonControl(
                bg.transform, "Host Button", "Host", new Vector2(28.0f, -180.0f), new Vector2(164.0f, 54.0f));
            Button joinButton = EnsureButtonControl(
                bg.transform, "Join Button", "Join", new Vector2(228.0f, -180.0f), new Vector2(164.0f, 54.0f));
            Button stopButton = EnsureButtonControl(
                bg.transform, "Stop Button", "Stop", new Vector2(428.0f, -180.0f), new Vector2(164.0f, 54.0f));

            GameObject statusBadge = EnsureRectChild(bg.transform, "Status Badge");
            RectTransform statusBadgeRect = statusBadge.GetComponent<RectTransform>();
            ConfigureTopLeftRect(statusBadgeRect, new Vector2(22.0f, -248.0f), new Vector2(width - 44.0f, 132.0f));
            Image statusBadgeImage = EnsureComponent<Image>(statusBadge);
            ApplySlicedSprite(statusBadgeImage, GetUiSprite("multiplayer_status_badge"));
            statusBadgeImage.color = new Color(0.3f, 0.68f, 0.9f, 0.22f);
            statusBadgeImage.raycastTarget = false;

            TextMeshProUGUI statusText = EnsureLabel(
                bg.transform, "Status",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LanStoppedWithDefault),
                22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -256.0f), new Vector2(width - 56.0f, 120.0f),
                TextDimColor);

            Button closeButton = EnsureButtonControl(
                bg.transform, "Close Button", "Close", TopRightClosePosition(width), MenuCloseButtonSize);

            BlockiverseMultiplayerSessionMenu menu = EnsureComponent<BlockiverseMultiplayerSessionMenu>(panelRoot);
            menu.ConfigureControls(hostButton, joinButton, stopButton, addressInput, statusText);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (presenter, closeButton);
        }

        // Builds a world-space panel canvas with a background, title label, N full-width action
        // buttons (text-button style), and a status label. Returns the wired BlockiverseActionMenu
        // and its presenter so callers can chain further configuration.
        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureActionMenuPanel(
            Transform parent,
            string name,
            Vector2 size,
            Transform head,
            int buttonCount = 5,
            int sortOrder = 25)
        {
            GameObject panelRoot = EnsureRectChild(parent, name);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortOrder;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            ApplySlicedSprite(bgImage, GetUiSprite("settings_panel"));
            bgImage.color = PanelBaseColor;

            // Header divider strip
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.0f, 1.0f);
            headerRect.anchorMax = new Vector2(1.0f, 1.0f);
            headerRect.pivot = new Vector2(0.0f, 1.0f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0.0f, 72.0f);
            Image headerImage = EnsureComponent<Image>(header);
            headerImage.color = PanelHeaderColor;

            TMP_Text titleLabel = EnsureLabel(
                bg.transform, "Title", name, 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -18.0f), new Vector2(size.x - 56.0f, 48.0f));

            var buttons = new Button[buttonCount];
            var labels = new TMP_Text[buttonCount];
            for (int i = 0; i < buttonCount; i++)
            {
                float buttonY = -100.0f - i * 58.0f;
                Button btn = EnsureButtonControl(
                    bg.transform,
                    $"Action {i + 1}",
                    string.Empty,
                    new Vector2(28.0f, buttonY),
                    new Vector2(size.x - 56.0f, 50.0f));
                Transform labelTransform = btn.transform.Find("Label");
                buttons[i] = btn;
                labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            }

            TMP_Text statusLabel = EnsureLabel(
                bg.transform, "Status", string.Empty, 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28.0f, -100.0f - buttonCount * 58.0f), new Vector2(size.x - 56.0f, 36.0f),
                TextDimColor);

            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot);
            actionMenu.Configure(titleLabel, buttons, labels, statusLabel);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(actionMenu);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (actionMenu, presenter);
        }

        // Builds the New World config panel: name/seed text inputs + 5 cycle-selector rows
        // (GameMode, Difficulty, WorldSize, WorldPreset, StartingBiome) + Create/Cancel buttons.
        static (BlockiverseNewWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureNewWorldMenuPanel(
            Transform parent,
            Transform head)
        {
            const float W = 620.0f;
            const float H = 720.0f;

            GameObject panelRoot = EnsureRectChild(parent, NewWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            ApplySlicedSprite(bgImage, GetUiSprite("settings_panel"));
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            EnsureLabel(bg.transform, "Title", "New World", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -18), new Vector2(W - 56, 48));

            // Name input row
            EnsureLabel(bg.transform, "Name Label", "World Name", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(150, 44), TextDimColor);
            TMP_InputField nameInput = EnsureInputFieldControl(
                bg.transform, "Name Input", "Enter name...", NewWorldConfig.DefaultName,
                new Vector2(186, -96), new Vector2(W - 214, 48));

            // Seed input row
            EnsureLabel(bg.transform, "Seed Label", "Seed", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -158), new Vector2(150, 44), TextDimColor);
            TMP_InputField seedInput = EnsureInputFieldControl(
                bg.transform, "Seed Input", "0", "0",
                new Vector2(186, -158),
                new Vector2(W - 214, 48));

            // 5 cycle rows: GameMode, Difficulty, WorldSize, WorldPreset, StartingBiome
            string[] rowLabels = { "Game Mode", "Difficulty", "World Size", "World Preset", "Starting Biome" };
            string[] defaultValues = { "survival", "normal", "small", WorldPresetIds.SurvivalTerrain, "balanced" };
            const float rowStartY = -230;
            const float rowH = 56;
            var backButtons = new Button[rowLabels.Length];
            var nextButtons = new Button[rowLabels.Length];
            var valueLabels = new TMP_Text[rowLabels.Length];

            for (int i = 0; i < rowLabels.Length; i++)
            {
                float rowY = rowStartY - i * rowH;

                // Row background
                GameObject rowBg = EnsureRectChild(bg.transform, $"Row {rowLabels[i]}");
                RectTransform rowRect = rowBg.GetComponent<RectTransform>();
                ConfigureTopLeftRect(rowRect, new Vector2(28, rowY), new Vector2(W - 56, rowH - 4));
                EnsureComponent<Image>(rowBg).color = PanelHeaderColor;

                // Field name
                EnsureLabel(rowBg.transform, "Label", rowLabels[i], 20, TextAnchor.MiddleLeft,
                    Vector2.zero, Vector2.one, new Vector2(0, 0.5f),
                    new Vector2(8, 0), new Vector2(160, rowH - 4), TextDimColor);

                // Back button ◀
                backButtons[i] = EnsureButtonControl(rowBg.transform, "Back",
                    "<", new Vector2(172, -(rowH - 4) * 0.5f + (rowH - 4) * 0.5f - 22), new Vector2(44, 44));
                ConfigureTopLeftRect(
                    backButtons[i].GetComponent<RectTransform>(),
                    new Vector2(172, -((rowH - 4) / 2 - 22)), new Vector2(44, 44));

                // Value label (center)
                valueLabels[i] = EnsureLabel(rowBg.transform, "Value", defaultValues[i], 22, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    new Vector2(0, 0), new Vector2(0, 0));
                // Position value label between the two buttons
                RectTransform valRect = valueLabels[i].GetComponent<RectTransform>();
                valRect.anchorMin = new Vector2(0, 0);
                valRect.anchorMax = new Vector2(1, 1);
                valRect.offsetMin = new Vector2(224, 0);
                valRect.offsetMax = new Vector2(-52, 0);

                // Next button ▶
                nextButtons[i] = EnsureButtonControl(rowBg.transform, "Next",
                    ">", Vector2.zero, new Vector2(44, 44));
                ConfigureTopLeftRect(
                    nextButtons[i].GetComponent<RectTransform>(),
                    new Vector2(W - 56 - 52, -((rowH - 4) / 2 - 22)), new Vector2(44, 44));
            }

            // Create / Cancel buttons
            float actionRowY = rowStartY - rowLabels.Length * rowH - 32;
            Button createButton = EnsureButtonControl(bg.transform, "Create Button", "Create World",
                new Vector2(28, actionRowY), new Vector2((W - 84) / 2, 52));
            Button cancelButton = EnsureButtonControl(bg.transform, "Cancel Button", "Cancel",
                new Vector2(28 + (W - 84) / 2 + 28, actionRowY), new Vector2((W - 84) / 2, 52));

            // Error label
            TMP_Text errorLabel = EnsureLabel(bg.transform, "Error", string.Empty, 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, actionRowY - 60), new Vector2(W - 56, 44),
                new Color(0.95f, 0.40f, 0.30f, 1.0f));

            BlockiverseNewWorldPanel panel = EnsureComponent<BlockiverseNewWorldPanel>(panelRoot);
            panel.Configure(nameInput, seedInput, backButtons, nextButtons, valueLabels,
                createButton, cancelButton, errorLabel);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (panel, presenter);
        }

        // Builds the Load World panel: up to 6 save-entry buttons + Load/Cancel footer buttons.
        static (BlockiverseLoadWorldPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureLoadWorldMenuPanel(
            Transform parent,
            Transform head)
        {
            const float W = 620.0f;
            const float H = 600.0f;
            const int MaxEntries = 6;

            GameObject panelRoot = EnsureRectChild(parent, LoadWorldPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            EnsureLabel(bg.transform, "Title", "Load World", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -18), new Vector2(W - 56, 48));

            // Save entry rows
            var entryButtons = new Button[MaxEntries];
            var entryLabels = new TMP_Text[MaxEntries];
            for (int i = 0; i < MaxEntries; i++)
            {
                float rowY = -96 - i * 54;
                entryLabels[i] = EnsureLabel(bg.transform, $"Save {i + 1}", string.Empty, 20,
                    TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(28, rowY), new Vector2(W - 56, 48));
                entryButtons[i] = EnsureTextButton(entryLabels[i]);
            }

            // Selection label
            TMP_Text selectionLabel = EnsureLabel(bg.transform, "Selection", "No save selected", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96 - MaxEntries * 54 - 12), new Vector2(W - 56, 36));

            float pageY = -96 - MaxEntries * 54 - 56;
            Button previousPageButton = EnsureButtonControl(
                bg.transform,
                "Previous Page Button",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldPreviousPage),
                new Vector2(28, pageY),
                new Vector2(112, 40));
            TMP_Text pageLabel = EnsureLabel(
                bg.transform,
                "Page",
                BlockiverseLocalization.Format(BlockiverseLocalization.Keys.LoadWorldPage, 1, 1),
                18,
                TextAnchor.MiddleCenter,
                new Vector2(0, 1),
                new Vector2(0, 1),
                new Vector2(0, 1),
                new Vector2(154, pageY),
                new Vector2(W - 308, 40));
            Button nextPageButton = EnsureButtonControl(
                bg.transform,
                "Next Page Button",
                BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldNextPage),
                new Vector2(W - 140, pageY),
                new Vector2(112, 40));

            // Load / Details / Cancel buttons (Details opens the §6.5 World Details screen).
            float footerY = -96 - MaxEntries * 54 - 104;
            float footerButtonWidth = (W - 112) / 3;
            Button loadButton = EnsureButtonControl(bg.transform, "Load Button", "Load World",
                new Vector2(28, footerY), new Vector2(footerButtonWidth, 52));
            Button detailsButton = EnsureButtonControl(bg.transform, "Details Button", "Details",
                new Vector2(28 + footerButtonWidth + 28, footerY), new Vector2(footerButtonWidth, 52));
            Button cancelButton = EnsureButtonControl(bg.transform, "Cancel Button", "Cancel",
                new Vector2(28 + (footerButtonWidth + 28) * 2, footerY), new Vector2(footerButtonWidth, 52));

            BlockiverseLoadWorldPanel panel = EnsureComponent<BlockiverseLoadWorldPanel>(panelRoot);
            panel.Configure(
                entryButtons,
                entryLabels,
                loadButton,
                cancelButton,
                selectionLabel,
                detailsButton,
                previousPageButton,
                nextPageButton,
                pageLabel);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (panel, presenter);
        }

        // Builds the Settings hub as a four-entry action menu (Comfort / Audio / Controls / Close).
        static (BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter) EnsureSettingsMenuPanel(
            Transform parent, Transform head)
        {
            // The settings hub is a plain four-entry action menu (Comfort / Audio / Controls /
            // Close, set by the menu controller at Start). Rebuild the panel from scratch so the
            // pre-hub placeholder layout (title + placeholder + lone close button) never lingers
            // in regenerated rigs.
            Transform stale = parent.Find(SettingsPanelName);
            if (stale != null && stale.Find("Panel/Placeholder") != null)
                UnityEngine.Object.DestroyImmediate(stale.gameObject);

            (BlockiverseActionMenu settingsMenu, BlockiverseWorldSpacePanelPresenter presenter) =
                EnsureActionMenuPanel(parent, SettingsPanelName, ActionMenuSize, head, buttonCount: 4, sortOrder: 25);
            settingsMenu.SetMenu("Settings", MenuActions.Settings);

            EditorUtility.SetDirty(settingsMenu);
            return (settingsMenu, presenter);
        }

        // Canonical controller mapping description, shared by the first-launch mapping popup and
        // the Settings → Controls reference screen so the two can never drift apart.
        const string ControllerMappingText =
            "Support stick: move\n" +
            "Support stick click: sprint\n" +
            "Dominant stick: snap turn\n" +
            "Either stick hold up: teleport aim, release to land\n" +
            "Dominant trigger: press UI / break\n" +
            "Dominant grip: place / use\n" +
            "Support grip: blocks menu\n" +
            "Dominant primary button: jump\n" +
            "Dominant secondary button: toggle block editing\n" +
            "Menu: pause";

        // Builds the audio/feedback settings screen: volume sliders, feedback toggles, and a
        // Close button (wired by the caller to the menu controller's close hook).
        static (BlockiverseAudioSettingsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton)
            EnsureAudioSettingsMenuPanel(Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 956.0f;

            GameObject panelRoot = EnsureRectChild(parent, AudioSettingsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            ApplySlicedSprite(bgImage, GetUiSprite("settings_panel"));
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Audio & Feedback", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));

            Slider master = EnsureSettingsSlider(bg.transform, "Master Volume Slider", "Master Volume", 1.0f, new Vector2(28, -96));
            Slider effects = EnsureSettingsSlider(bg.transform, "Effects Volume Slider", "Effects Volume", 1.0f, new Vector2(28, -192));
            Slider ui = EnsureSettingsSlider(bg.transform, "UI Volume Slider", "UI Volume", 1.0f, new Vector2(28, -288));
            Slider weather = EnsureSettingsSlider(bg.transform, "Weather Volume Slider", "Weather Volume", 1.0f, new Vector2(28, -384));
            Slider music = EnsureSettingsSlider(bg.transform, "Music Volume Slider", "Music Volume", 0.5f, new Vector2(28, -480));
            Slider hapticStrength = EnsureSettingsSlider(bg.transform, "Haptic Strength Slider", "Haptic Strength", 1.0f, new Vector2(28, -576));

            Toggle mute = EnsureToggleControl(bg.transform, "Mute All Toggle", "Mute All", false, new Vector2(28, -672));
            Toggle haptics = EnsureToggleControl(bg.transform, "Haptics Toggle", "Haptics", true, new Vector2(28, -728));
            Toggle reducedFlash = EnsureToggleControl(bg.transform, "Reduced Flash Toggle", "Reduced Flash", false, new Vector2(28, -784));
            Toggle reducedParticles = EnsureToggleControl(bg.transform, "Reduced Particles Toggle", "Reduced Particles", false, new Vector2(28, -840));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                TopRightClosePosition(W), MenuCloseButtonSize);

            BlockiverseAudioSettingsPanel panel = EnsureComponent<BlockiverseAudioSettingsPanel>(panelRoot);
            panel.Configure(
                parent.GetComponentInParent<BlockiverseFeedbackSettings>(),
                master, effects, ui, weather, music, hapticStrength,
                mute, haptics, reducedFlash, reducedParticles);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, presenter, closeButton);
        }

        // Builds the Creative Tools screen (§12): corner A/B region selection, fill/replace/
        // delete/copy/paste with region undo/redo, tree/ruin spawners, pick-block, and the
        // environment controls (time-of-day, day/night cycle pause, day speed, weather cycle).
        static (BlockiverseCreativeToolsPanel panel, BlockiverseWorldSpacePanelPresenter presenter, Button closeButton)
            EnsureCreativeToolsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f;
            const float H = 820.0f;

            GameObject panelRoot = EnsureRectChild(parent, CreativeToolsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Creative Tools", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));

            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Aim at blocks to select corners.", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -78), new Vector2(W - 56, 34), TextDimColor);

            TMP_Text cornersLabel = EnsureLabel(bg.transform, "Corners", "A: —    B: —", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -116), new Vector2(W - 56, 32));

            Button setAButton = EnsureButtonControl(bg.transform, "Set A Button", "Set A", new Vector2(28, -156), new Vector2(150, 48));
            Button setBButton = EnsureButtonControl(bg.transform, "Set B Button", "Set B", new Vector2(196, -156), new Vector2(150, 48));
            Button pickButton = EnsureButtonControl(bg.transform, "Pick Block Button", "Pick Block", new Vector2(364, -156), new Vector2(150, 48));

            Button fillButton = EnsureButtonControl(bg.transform, "Fill Button", "Fill", new Vector2(28, -216), new Vector2(150, 48));
            Button replaceButton = EnsureButtonControl(bg.transform, "Replace Button", "Replace", new Vector2(196, -216), new Vector2(150, 48));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(364, -216), new Vector2(150, 48));

            Button copyButton = EnsureButtonControl(bg.transform, "Copy Button", "Copy", new Vector2(28, -276), new Vector2(150, 48));
            Button pasteButton = EnsureButtonControl(bg.transform, "Paste Button", "Paste", new Vector2(196, -276), new Vector2(150, 48));

            Button undoButton = EnsureButtonControl(bg.transform, "Undo Button", "Undo Edit", new Vector2(28, -336), new Vector2(150, 48));
            Button redoButton = EnsureButtonControl(bg.transform, "Redo Button", "Redo Edit", new Vector2(196, -336), new Vector2(150, 48));

            Button treeButton = EnsureButtonControl(bg.transform, "Spawn Tree Button", "Spawn Tree", new Vector2(28, -396), new Vector2(150, 48));
            Button ruinButton = EnsureButtonControl(bg.transform, "Spawn Ruin Button", "Spawn Ruin", new Vector2(196, -396), new Vector2(150, 48));

            Slider timeSlider = EnsureSettingsSlider(bg.transform, "Time Of Day Slider", "Time of Day", 0.25f, new Vector2(28, -462));
            Slider speedSlider = EnsureSettingsSlider(bg.transform, "Day Speed Slider", "Day Speed", 1.0f, new Vector2(28, -558), minValue: 0.0f, maxValue: 4.0f);
            Button toggleCycleButton = EnsureButtonControl(bg.transform, "Toggle Cycle Button", "Pause / Resume Cycle", new Vector2(28, -638), new Vector2(246, 48));

            TMP_Text weatherLabel = EnsureLabel(bg.transform, "Weather Label", "Weather: Clear", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -714), new Vector2(300, 40));
            Button weatherButton = EnsureButtonControl(bg.transform, "Cycle Weather Button", "Cycle Weather", new Vector2(346, -710), new Vector2(186, 48));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close", TopRightClosePosition(W), MenuCloseButtonSize);

            BlockiverseCreativeToolsPanel panel = EnsureComponent<BlockiverseCreativeToolsPanel>(panelRoot);
            panel.Configure(null, null, null, cornersLabel, statusLabel, weatherLabel, timeSlider, speedSlider);

            WireButton(setAButton, panel, nameof(BlockiverseCreativeToolsPanel.SetCornerA), panel.SetCornerA);
            WireButton(setBButton, panel, nameof(BlockiverseCreativeToolsPanel.SetCornerB), panel.SetCornerB);
            WireButton(pickButton, panel, nameof(BlockiverseCreativeToolsPanel.PickBlock), panel.PickBlock);
            WireButton(fillButton, panel, nameof(BlockiverseCreativeToolsPanel.FillRegion), panel.FillRegion);
            WireButton(replaceButton, panel, nameof(BlockiverseCreativeToolsPanel.ReplaceRegion), panel.ReplaceRegion);
            WireButton(deleteButton, panel, nameof(BlockiverseCreativeToolsPanel.DeleteRegion), panel.DeleteRegion);
            WireButton(copyButton, panel, nameof(BlockiverseCreativeToolsPanel.CopyRegion), panel.CopyRegion);
            WireButton(pasteButton, panel, nameof(BlockiverseCreativeToolsPanel.PasteRegion), panel.PasteRegion);
            WireButton(undoButton, panel, nameof(BlockiverseCreativeToolsPanel.UndoEdit), panel.UndoEdit);
            WireButton(redoButton, panel, nameof(BlockiverseCreativeToolsPanel.RedoEdit), panel.RedoEdit);
            WireButton(treeButton, panel, nameof(BlockiverseCreativeToolsPanel.SpawnTree), panel.SpawnTree);
            WireButton(ruinButton, panel, nameof(BlockiverseCreativeToolsPanel.SpawnRuin), panel.SpawnRuin);
            WireButton(toggleCycleButton, panel, nameof(BlockiverseCreativeToolsPanel.ToggleDayNightCycle), panel.ToggleDayNightCycle);
            WireButton(weatherButton, panel, nameof(BlockiverseCreativeToolsPanel.CycleWeather), panel.CycleWeather);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, presenter, closeButton);
        }

        // Replaces any previous persistent listener for the same target/method, then adds it.
        static void WireButton(Button button, UnityEngine.Object target, string methodName, UnityEngine.Events.UnityAction action)
        {
            RemovePersistentListeners(button.onClick, target, methodName);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        // Builds the read-only controls reference screen (Settings → Controls).
        static (BlockiverseWorldSpacePanelPresenter presenter, Button closeButton) EnsureControlsMenuPanel(
            Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 480.0f;

            GameObject panelRoot = EnsureRectChild(parent, ControlsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            ApplySlicedSprite(bgImage, GetUiSprite("settings_panel"));
            bgImage.color = PanelBaseColor;

            EnsureLabel(bg.transform, "Title", "Controls", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(MenuPanelInset, -MenuPanelInset), TitleSizeWithClose(W));

            EnsureLabel(bg.transform, "Mapping Text", ControllerMappingText, 22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(W - 56, 290));

            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                TopRightClosePosition(W), MenuCloseButtonSize);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (presenter, closeButton);
        }

        // Builds the World Details screen (§6.5): save metadata, a rename field, and the
        // Play/Rename/Duplicate/Delete/Back management actions.
        static (BlockiverseWorldDetailsPanel panel, BlockiverseActionMenu menu, BlockiverseWorldSpacePanelPresenter presenter)
            EnsureWorldDetailsMenuPanel(Transform parent, Transform head)
        {
            const float W = 560.0f;
            const float H = 620.0f;

            GameObject panelRoot = EnsureRectChild(parent, WorldDetailsPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "World Details", 32, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -28), new Vector2(W - 56, 52));

            TMP_Text metadataLabel = EnsureLabel(bg.transform, "Metadata", string.Empty, 22, TextAnchor.UpperLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -96), new Vector2(W - 56, 120));

            EnsureLabel(bg.transform, "Rename Label", "Name", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -232), new Vector2(120, 40), TextDimColor);

            TMP_InputField renameField = EnsureInputFieldControl(bg.transform, "Rename Field",
                "World name", string.Empty, new Vector2(28, -274), new Vector2(W - 56, 56));

            // §6.5 management actions in two rows.
            Button playButton = EnsureButtonControl(bg.transform, "Play Button", "Play", new Vector2(28, -356), new Vector2(150, 52));
            Button renameButton = EnsureButtonControl(bg.transform, "Rename Button", "Rename", new Vector2(196, -356), new Vector2(150, 52));
            Button duplicateButton = EnsureButtonControl(bg.transform, "Duplicate Button", "Duplicate", new Vector2(364, -356), new Vector2(150, 52));
            Button deleteButton = EnsureButtonControl(bg.transform, "Delete Button", "Delete", new Vector2(28, -424), new Vector2(150, 52));
            Button backButton = EnsureButtonControl(bg.transform, "Back Button", "Back", new Vector2(196, -424), new Vector2(150, 52));

            var buttons = new[] { playButton, renameButton, duplicateButton, deleteButton, backButton };
            var labels = new TMP_Text[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                Transform labelTransform = buttons[i].transform.Find("Label");
                labels[i] = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            }

            BlockiverseActionMenu actionMenu = EnsureComponent<BlockiverseActionMenu>(panelRoot);
            actionMenu.Configure(titleLabel, buttons, labels);
            actionMenu.SetMenu("World Details", MenuActions.WorldDetails);

            BlockiverseWorldDetailsPanel panel = EnsureComponent<BlockiverseWorldDetailsPanel>(panelRoot);
            panel.Configure(metadataLabel, renameField);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.UiConfirm, BlockiverseAudioCue.UiCancel);

            EditorUtility.SetDirty(actionMenu);
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);
            return (panel, actionMenu, presenter);
        }

        // Generic labeled slider row for settings panels (same construction as the comfort
        // sliders, parameterized by name/label/range/position; defaults to 0–1).
        static Slider EnsureSettingsSlider(Transform parent, string name, string label, float value, Vector2 anchoredPosition, float minValue = 0.0f, float maxValue = 1.0f)
        {
            GameObject rowObject = EnsureRectChild(parent, name);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(484.0f, 88.0f));

            EnsureLabel(
                rowObject.transform,
                "Label",
                label,
                26,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(300.0f, 40.0f),
                TextDimColor);

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -46.0f), new Vector2(484.0f, 36.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = false;

            Sprite roundedSprite = GetRoundedSprite();

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.30f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.70f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            if (roundedSprite != null)
            {
                background.sprite = roundedSprite;
                background.type = Image.Type.Sliced;
            }
            background.color = ControlNormalColor;
            ConfigureUiRaycastBlocker(background);

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.30f);
            fillRect.anchorMax = new Vector2(1.0f, 0.70f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            if (roundedSprite != null)
            {
                fill.sprite = roundedSprite;
                fill.type = Image.Type.Sliced;
            }
            fill.color = AccentColor;
            fill.raycastTarget = false;

            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-10.0f, 0.0f);

            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f);
            handleRect.anchorMax = new Vector2(0.0f, 0.5f);
            handleRect.sizeDelta = new Vector2(36.0f, 36.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            Sprite knobSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (knobSprite != null)
                handle.sprite = knobSprite;
            handle.color = TextPrimaryColor;
            ConfigureUiRaycastBlocker(handle);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.colors = new ColorBlock
            {
                normalColor      = TextPrimaryColor,
                highlightedColor = AccentHighlightColor,
                pressedColor     = AccentColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            slider.value = value;
            ConfigureSelectableFeedback(slider);
            return slider;
        }

        // Builds the smelting-station panel: up to 3 input slots, 1 fuel slot, output, progress
        // slider, deposit/collect transfer buttons, and a Close button. Used for both Clay Kiln
        // (1 input) and Bellows Forge (3).
        static (BlockiverseStationPanel panel, BlockiverseWorldSpacePanelPresenter presenter) EnsureStationMenuPanel(
            Transform parent, Transform head)
        {
            const float W = 540.0f;
            const float H = 620.0f;

            GameObject panelRoot = EnsureRectChild(parent, StationPanelName);
            panelRoot.transform.localPosition = GameMenuLocalPosition;
            panelRoot.transform.localRotation = Quaternion.identity;
            panelRoot.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, W);
            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, H);

            Canvas canvas = EnsureComponent<Canvas>(panelRoot);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(panelRoot);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(panelRoot);

            GameObject bg = EnsureRectChild(panelRoot.transform, "Panel");
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = EnsureComponent<Image>(bg);
            Sprite rounded = GetRoundedSprite();
            if (rounded != null) { bgImage.sprite = rounded; bgImage.type = Image.Type.Sliced; }
            bgImage.color = PanelBaseColor;

            // Header
            GameObject header = EnsureRectChild(bg.transform, "Header");
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0, 1);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0, 72);
            EnsureComponent<Image>(header).color = PanelHeaderColor;

            TMP_Text titleLabel = EnsureLabel(bg.transform, "Title", "Station", 30, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(MenuPanelInset, -18), TitleSizeWithClose(W, 48.0f));

            // Input slots (forge maximum; unused ones stay empty when model has fewer)
            var inputLabels = new TMP_Text[SmeltingStationModel.MaxInputSlots];
            string[] inputNames = { "Input Slot 1", "Input Slot 2", "Input Slot 3" };
            for (int i = 0; i < inputLabels.Length; i++)
            {
                EnsureLabel(bg.transform, $"Input Label {i + 1}", $"Input {i + 1}", 20, TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(28, -94 - i * 46), new Vector2(160, 40), TextDimColor);
                inputLabels[i] = EnsureLabel(bg.transform, inputNames[i], "—", 22, TextAnchor.MiddleLeft,
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(200, -94 - i * 46), new Vector2(W - 228, 40));
            }

            // Fuel slot
            EnsureLabel(bg.transform, "Fuel Label", "Fuel", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -240), new Vector2(160, 40), TextDimColor);
            TMP_Text fuelLabel = EnsureLabel(bg.transform, "Fuel Slot", "No fuel", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(200, -240), new Vector2(W - 228, 40));

            // Output slot
            EnsureLabel(bg.transform, "Output Label", "Output", 20, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -290), new Vector2(160, 40), TextDimColor);
            TMP_Text outputLabel = EnsureLabel(bg.transform, "Output Slot", "—", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(200, -290), new Vector2(W - 228, 40));

            // Progress slider
            Slider progressSlider = EnsureHudSlider(bg.transform, "Progress", new Vector2(28, -354), new Vector2(W - 56, 20));

            // Status label
            TMP_Text statusLabel = EnsureLabel(bg.transform, "Status", "Idle", 22, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -394), new Vector2(W - 56, 36), TextDimColor);

            // Transfer buttons: deposit the held hotbar item as input/fuel, collect the output,
            // or withdraw loaded input/fuel back into the player's inventory.
            Button depositInputButton = EnsureButtonControl(bg.transform, "Deposit Input Button", "Add Input",
                new Vector2(28, -440), new Vector2(150, 52));
            Button depositFuelButton = EnsureButtonControl(bg.transform, "Deposit Fuel Button", "Add Fuel",
                new Vector2(188, -440), new Vector2(150, 52));
            Button collectOutputButton = EnsureButtonControl(bg.transform, "Collect Output Button", "Collect",
                new Vector2(348, -440), new Vector2(150, 52));
            Button withdrawInputButton = EnsureButtonControl(bg.transform, "Withdraw Input Button", "Take Input",
                new Vector2(28, -504), new Vector2(150, 52));
            Button withdrawFuelButton = EnsureButtonControl(bg.transform, "Withdraw Fuel Button", "Take Fuel",
                new Vector2(188, -504), new Vector2(150, 52));

            // Close button
            Button closeButton = EnsureButtonControl(bg.transform, "Close Button", "Close",
                TopRightClosePosition(W), MenuCloseButtonSize);

            BlockiverseStationPanel stationPanel = EnsureComponent<BlockiverseStationPanel>(panelRoot);
            stationPanel.Configure(titleLabel, inputLabels, fuelLabel, outputLabel, statusLabel,
                progressSlider, closeButton);
            stationPanel.ConfigureTransferControls(
                depositInputButton,
                depositFuelButton,
                collectOutputButton,
                withdrawInputButton,
                withdrawFuelButton);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(panelRoot);
            presenter.Configure(canvas, head, 1.1f, 0.0f, -0.06f, 0.0f, GameMenuScale);
            presenter.ConfigureFeedback(BlockiverseAudioCue.ContainerOpen, BlockiverseAudioCue.ContainerClose);

            EditorUtility.SetDirty(stationPanel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panelRoot);

            return (stationPanel, presenter);
        }
    }
}
