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
        static void EnsureBlockMenuPlaceholder(GameObject rig, BlockiverseInputRig inputRig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform leftController = cameraOffset != null ? cameraOffset.Find("Left Controller") : null;
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject menuObject = EnsureRectChildMigrated(cameraOffset, leftController, BlockMenuName);
            menuObject.transform.localPosition = new Vector3(-0.34f, 1.32f, 1.12f);
            menuObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
            menuObject.transform.localScale = Vector3.one * 0.002f;

            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BlockMenuSize.x);
            menuRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, BlockMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(menuObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 12;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(menuObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(menuObject);

            GameObject panelObject = EnsureRectChild(menuObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            ApplySlicedSprite(panelImage, GetUiSprite("hotbar_frame"));
            panelImage.color = BlockMenuPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Blocks",
                34,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -32.0f),
                new Vector2(300.0f, 48.0f));

            TMP_Text selectedLabel = EnsureLabel(
                panelObject.transform,
                "Selected Block",
                "Meadow Turf",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -82.0f),
                new Vector2(300.0f, 34.0f));

            // The browser replaced the decorative swatches; clear them out of older rigs.
            foreach (string stale in new[] { "Swatch A", "Swatch B", "Swatch C" })
            {
                Transform staleSwatch = panelObject.transform.Find(stale);
                if (staleSwatch != null)
                    UnityEngine.Object.DestroyImmediate(staleSwatch.gameObject);
            }

            // Catalog browser controls: category cycle + page label/buttons + search field.
            Button categoryButton = EnsureButtonControl(panelObject.transform, "Category Button", "Category",
                new Vector2(24.0f, -124.0f), new Vector2(150.0f, 44.0f));
            TMP_Text categoryLabel = EnsureLabel(panelObject.transform, "Category Label", "Terrain", 22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(190.0f, -128.0f), new Vector2(170.0f, 36.0f));

            Button prevPageButton = EnsureButtonControl(panelObject.transform, "Prev Page Button", "<",
                new Vector2(368.0f, -124.0f), new Vector2(52.0f, 44.0f));
            TMP_Text pageLabel = EnsureLabel(panelObject.transform, "Page Label", "1/1", 22,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(424.0f, -128.0f), new Vector2(56.0f, 36.0f));
            Button nextPageButton = EnsureButtonControl(panelObject.transform, "Next Page Button", ">",
                new Vector2(484.0f, -124.0f), new Vector2(52.0f, 44.0f));

            TMP_InputField searchField = EnsureInputFieldControl(panelObject.transform, "Search Field",
                "Search blocks…", string.Empty, new Vector2(24.0f, -176.0f), new Vector2(512.0f, 48.0f));

            // 12-entry grid (3 columns × 4 rows) of pick buttons.
            const int gridColumns = 3;
            const int gridEntries = 12;
            var entryButtons = new Button[gridEntries];
            var entryLabels = new TMP_Text[gridEntries];
            for (int i = 0; i < gridEntries; i++)
            {
                int column = i % gridColumns;
                int row = i / gridColumns;
                var position = new Vector2(24.0f + column * 172.0f, -236.0f - row * 54.0f);
                entryButtons[i] = EnsureButtonControl(panelObject.transform, $"Entry Button {i}", string.Empty,
                    position, new Vector2(164.0f, 46.0f));
                Transform entryLabelTransform = entryButtons[i].transform.Find("Label");
                entryLabels[i] = entryLabelTransform != null ? entryLabelTransform.GetComponent<TMP_Text>() : null;
                if (entryLabels[i] != null)
                    entryLabels[i].fontSize = 18;
            }

            CreativeHotbar menu = EnsureComponent<CreativeHotbar>(menuObject);
            menu.ConfigureFromDefaultCatalog(selectedLabel);
            menu.ConfigureCanvas(canvas);

            BlockiverseCatalogBrowserPanel browser = EnsureComponent<BlockiverseCatalogBrowserPanel>(menuObject);
            browser.Configure(menu, categoryLabel, pageLabel, searchField, entryButtons, entryLabels);
            WireButton(categoryButton, browser, nameof(BlockiverseCatalogBrowserPanel.CycleCategory), browser.CycleCategory);
            WireButton(prevPageButton, browser, nameof(BlockiverseCatalogBrowserPanel.PreviousPage), browser.PreviousPage);
            WireButton(nextPageButton, browser, nameof(BlockiverseCatalogBrowserPanel.NextPage), browser.NextPage);
            EditorUtility.SetDirty(browser);
            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.12f, -0.34f, -0.18f, 0.0f);
            presenter.ConfigureFeedback(BlockiverseAudioCue.InventoryOpen, BlockiverseAudioCue.InventoryClose);

            if (inputRig != null)
            {
                RemovePersistentListeners(
                    inputRig.QuickMenuPressed,
                    menu,
                    nameof(CreativeHotbar.ToggleVisible));
                RemovePersistentListeners(
                    inputRig.QuickMenuPressed,
                    presenter,
                    nameof(BlockiverseWorldSpacePanelPresenter.ToggleVisible));
                UnityEventTools.AddPersistentListener(inputRig.QuickMenuPressed, presenter.ToggleVisible);
                EditorUtility.SetDirty(inputRig);
            }

            EditorUtility.SetDirty(menuObject);
            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(presenter);
        }

        static void EnsureXrRigStartupLoadingOverlay(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject overlayObject = EnsureRectChild(cameraOffset, StartupLoadingOverlayName);
            overlayObject.transform.localPosition = new Vector3(0.0f, 1.46f, 1.0f);
            overlayObject.transform.localRotation = Quaternion.identity;
            overlayObject.transform.localScale = Vector3.one * 0.00165f;

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, StartupLoadingOverlaySize.x);
            overlayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StartupLoadingOverlaySize.y);

            Canvas canvas = EnsureComponent<Canvas>(overlayObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(overlayObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            GameObject artworkObject = EnsureRectChild(overlayObject.transform, "Artwork");
            RectTransform artworkRect = artworkObject.GetComponent<RectTransform>();
            artworkRect.anchorMin = Vector2.zero;
            artworkRect.anchorMax = Vector2.one;
            artworkRect.offsetMin = Vector2.zero;
            artworkRect.offsetMax = Vector2.zero;

            RawImage artworkImage = EnsureComponent<RawImage>(artworkObject);
            artworkImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPath);
            artworkImage.color = Color.white;
            artworkImage.raycastTarget = false;

            GameObject tintObject = EnsureRectChild(overlayObject.transform, "Title Tint");
            RectTransform tintRect = tintObject.GetComponent<RectTransform>();
            tintRect.anchorMin = new Vector2(0.0f, 0.0f);
            tintRect.anchorMax = new Vector2(1.0f, 0.38f);
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            Image tintImage = EnsureComponent<Image>(tintObject);
            ApplySlicedSprite(tintImage, GetUiSprite("feedback_toast"));
            tintImage.color = StartupOverlayPanelColor;
            tintImage.raycastTarget = false;

            TMP_Text titleLabel = EnsureLabel(
                overlayObject.transform,
                "Title",
                BlockiverseProject.ProductName,
                72,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(58.0f, 118.0f),
                new Vector2(720.0f, 92.0f));
            titleLabel.raycastTarget = false;

            TMP_Text subtitleLabel = EnsureLabel(
                overlayObject.transform,
                "Subtitle",
                "Survive, craft, and shape the world.",
                30,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(62.0f, 72.0f),
                new Vector2(720.0f, 48.0f));
            subtitleLabel.raycastTarget = false;

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(overlayObject);
            presenter.Configure(
                canvas,
                head,
                1.0f,
                0.0f,
                -0.14f,
                0.0f,
                0.00165f,
                showWhenStarted: false);

            BlockiverseStartupOverlay startupOverlay = EnsureComponent<BlockiverseStartupOverlay>(overlayObject);
            startupOverlay.Configure(canvas, presenter, 2.25f, automaticHide: true);
            EnsureDecorativeCanvasDoesNotReceiveUi(overlayObject);

            EditorUtility.SetDirty(artworkImage);
            EditorUtility.SetDirty(tintImage);
            EditorUtility.SetDirty(titleLabel);
            EditorUtility.SetDirty(subtitleLabel);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(startupOverlay);
            EditorUtility.SetDirty(overlayObject);
        }

        static void EnsureXrRigControllerMappingPopup(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            Transform routedMenuParent = EnsureMenuCompositionSurface(cameraOffset, head).transform.Find(MenuCompositionCanvasName);
            GameObject popupObject = EnsureRoutedMenuRectChild(cameraOffset, routedMenuParent, null, ControllerMappingPopupName);
            popupObject.transform.localPosition = new Vector3(0.0f, 1.42f, 1.06f);
            popupObject.transform.localRotation = Quaternion.identity;
            popupObject.transform.localScale = Vector3.one * 0.0013f;

            RectTransform popupRect = popupObject.GetComponent<RectTransform>();
            popupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ControllerMappingPopupSize.x);
            popupRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ControllerMappingPopupSize.y);

            Canvas canvas = EnsureComponent<Canvas>(popupObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 40;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(popupObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(popupObject);

            GameObject panelObject = EnsureRectChild(popupObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            ApplySlicedSprite(panelImage, GetUiSprite("feedback_toast"));
            panelImage.color = SurvivalHudPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Controller Map",
                32,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(34.0f, -28.0f),
                TitleSizeWithClose(ControllerMappingPopupSize.x, 58.0f));

            EnsureLabel(
                panelObject.transform,
                "Mapping Text",
                ControllerMappingText,
                20,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(34.0f, -102.0f),
                new Vector2(552.0f, 220.0f));

            Button closeButton = EnsureButtonControl(
                panelObject.transform,
                "Close Button",
                "Close",
                TopRightClosePosition(ControllerMappingPopupSize.x),
                MenuCloseButtonSize);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(popupObject);
            presenter.Configure(
                canvas,
                head,
                1.06f,
                0.0f,
                -0.14f,
                0.0f,
                0.0013f,
                showWhenStarted: false,
                showWhenStartedPlayerPrefsKey: BlockiverseWorldSpacePanelPresenter.ControllerMappingPopupSeenPrefKey);

            RemovePersistentListeners(
                closeButton.onClick,
                presenter,
                nameof(BlockiverseWorldSpacePanelPresenter.Hide));
            UnityEventTools.AddPersistentListener(closeButton.onClick, presenter.Hide);

            EditorUtility.SetDirty(closeButton);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(popupObject);
        }

        static void EnsureXrRigSurvivalHud(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject hudObject = EnsureRectChild(cameraOffset, SurvivalHudName);
            hudObject.transform.localPosition = new Vector3(0.0f, 1.16f, 1.25f);
            hudObject.transform.localRotation = Quaternion.Euler(12.0f, 0.0f, 0.0f);
            hudObject.transform.localScale = Vector3.one * SurvivalHudScale;

            RectTransform hudRect = hudObject.GetComponent<RectTransform>();
            hudRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SurvivalHudSize.x);
            hudRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SurvivalHudSize.y);

            Canvas canvas = EnsureComponent<Canvas>(hudObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 9;
            canvas.enabled = false;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(hudObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EnsureTrackedDeviceRaycaster(hudObject);

            GameObject panelObject = EnsureRectChild(hudObject.transform, "Panel");
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = EnsureComponent<Image>(panelObject);
            panelImage.color = SurvivalHudPanelColor;

            EnsureLabel(
                panelObject.transform,
                "Title",
                "Survival",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(18.0f, -14.0f),
                new Vector2(130.0f, 34.0f));

            TMP_Text statusLabel = EnsureLabel(
                panelObject.transform,
                "Status",
                string.Empty,
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(160.0f, -16.0f),
                new Vector2(350.0f, 32.0f),
                TextDimColor);
            statusLabel.gameObject.SetActive(false);

            Slider miningProgressSlider = EnsureHudSlider(
                panelObject.transform,
                "Mining Progress",
                new Vector2(304.0f, -60.0f),
                new Vector2(220.0f, 14.0f));
            miningProgressSlider.gameObject.SetActive(false);

            BlockiverseItemIconLibrary iconLibrary = EnsureItemIconLibrary(rig);
            SurvivalHealthPanel healthPanel = EnsureSurvivalHealthSection(panelObject.transform);
            SurvivalInventoryPanel inventoryPanel = EnsureSurvivalInventorySection(panelObject.transform, iconLibrary);
            SurvivalCraftingPanel craftingPanel = EnsureSurvivalCraftingSection(panelObject.transform, iconLibrary);
            SurvivalCratePanel cratePanel = EnsureSurvivalCrateSection(panelObject.transform);
            SetGameplayHudBackingSectionVisible(inventoryPanel, false);
            SetGameplayHudBackingSectionVisible(craftingPanel, false);
            SetGameplayHudBackingSectionVisible(cratePanel, false);

            SurvivalHudController controller = EnsureComponent<SurvivalHudController>(hudObject);
            controller.Configure(
                inventoryPanel,
                craftingPanel,
                healthPanel,
                cratePanel,
                targetStatusLabel: statusLabel,
                targetMiningProgressSlider: miningProgressSlider);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(hudObject);
            presenter.Configure(
                canvas,
                head,
                1.15f,
                0.0f,
                -0.30f,
                12.0f,
                SurvivalHudScale,
                recenterWhenShown: false);

            BlockiverseSubtitleToastPanel toastPanel = EnsureComponent<BlockiverseSubtitleToastPanel>(rig);
            toastPanel.Configure(statusLabel);
            SurvivalFeedbackBridge feedbackBridge = EnsureComponent<SurvivalFeedbackBridge>(rig);
            feedbackBridge.ConfigureToastPanel(toastPanel);

            EditorUtility.SetDirty(hudObject);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(toastPanel);
            EditorUtility.SetDirty(feedbackBridge);
        }

        static SurvivalHealthPanel EnsureSurvivalHealthSection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Health", new Vector2(18.0f, -56.0f), new Vector2(240.0f, 104.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Health",
                18,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(14.0f, -6.0f),
                new Vector2(200.0f, 24.0f));

            TMP_Text valueLabel = EnsureLabel(
                sectionObject.transform,
                "Value",
                "100 / 100",
                22,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(14.0f, -32.0f),
                new Vector2(200.0f, 28.0f));

            Slider slider = EnsureHudSlider(sectionObject.transform, "Health Slider", new Vector2(14.0f, -66.0f), new Vector2(200.0f, 14.0f));

            TMP_Text stateLabel = EnsureLabel(
                sectionObject.transform,
                "State",
                "Stable",
                16,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(14.0f, -80.0f),
                new Vector2(200.0f, 20.0f));

            SurvivalHealthPanel panel = EnsureComponent<SurvivalHealthPanel>(sectionObject);
            panel.Configure(valueLabel, slider, stateLabel);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static void SetGameplayHudBackingSectionVisible(Component section, bool visible)
        {
            if (section != null)
                section.gameObject.SetActive(visible);
        }

        // Populates the rig's item icon library from the committed sprites for registered items.
        // Future-tier art can live in the folder without becoming a runtime lookup entry early.
        static BlockiverseItemIconLibrary EnsureItemIconLibrary(GameObject rig)
        {
            const string itemsDir = "Assets/Blockiverse/Art/Textures/Items";

            var ids = new List<string>();
            var sprites = new List<Sprite>();
            var registeredItemIds = new HashSet<string>(
                ItemRegistry.Default.All.Select(item => item.Id.Value),
                StringComparer.OrdinalIgnoreCase);

            if (AssetDatabase.IsValidFolder(itemsDir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { itemsDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                        importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.SaveAndReimport();
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null)
                        continue;

                    string id = Path.GetFileNameWithoutExtension(path);
                    if (!registeredItemIds.Contains(id))
                        continue;

                    ids.Add(id);
                    sprites.Add(sprite);
                }
            }

            BlockiverseItemIconLibrary library = EnsureComponent<BlockiverseItemIconLibrary>(rig);
            library.Configure(ids.ToArray(), sprites.ToArray());
            EditorUtility.SetDirty(library);
            return library;
        }

        static SurvivalInventoryPanel EnsureSurvivalInventorySection(Transform parent, BlockiverseItemIconLibrary iconLibrary)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Inventory", new Vector2(250.0f, -82.0f), new Vector2(206.0f, 380.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Inventory",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(170.0f, 34.0f));

            TMP_Text selectedHotbarLabel = EnsureLabel(
                sectionObject.transform,
                "Selected Hotbar",
                $"Hotbar 1 / {Inventory.DefaultHotbarSlotCount}",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(170.0f, 28.0f));
            GameObject selectedFrameObject = EnsureRectChild(sectionObject.transform, "Selected Hotbar Frame");
            RectTransform selectedFrameRect = selectedFrameObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(selectedFrameRect, new Vector2(12.0f, -42.0f), new Vector2(178.0f, 32.0f));
            Image selectedFrameImage = EnsureComponent<Image>(selectedFrameObject);
            ApplySlicedSprite(selectedFrameImage, GetUiSprite("selected_slot"));
            selectedFrameImage.color = new Color(1.0f, 0.82f, 0.25f, 0.18f);
            selectedFrameImage.raycastTarget = false;
            selectedHotbarLabel.transform.SetAsLastSibling();

            TMP_Text pageLabel = EnsureLabel(
                sectionObject.transform,
                "Page",
                $"Slots 1-{Inventory.DefaultHotbarSlotCount} / {Inventory.DefaultSlotCount}",
                15,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -70.0f),
                new Vector2(96.0f, 24.0f));

            Button prevPageButton = EnsureButtonControl(
                sectionObject.transform,
                "Previous Page Button",
                "<",
                new Vector2(116.0f, -68.0f),
                new Vector2(32.0f, 24.0f));

            Button nextPageButton = EnsureButtonControl(
                sectionObject.transform,
                "Next Page Button",
                ">",
                new Vector2(154.0f, -68.0f),
                new Vector2(32.0f, 24.0f));
            SetButtonLabelFontSize(prevPageButton, 18.0f);
            SetButtonLabelFontSize(nextPageButton, 18.0f);

            TMP_Text[] slotLabels = new TMP_Text[Inventory.DefaultHotbarSlotCount];
            Button[] slotButtons = new Button[slotLabels.Length];
            Image[] slotIcons = new Image[slotLabels.Length];

            for (int index = 0; index < slotLabels.Length; index++)
            {
                float rowY = -104.0f - index * 26.0f;
                slotIcons[index] = EnsureItemIconImage(
                    sectionObject.transform,
                    $"Slot Icon {index + 1}",
                    new Vector2(16.0f, rowY),
                    22.0f);

                slotLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Slot {index + 1}",
                    "Empty",
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(42.0f, rowY),
                    new Vector2(146.0f, 24.0f));
                slotButtons[index] = EnsureTextButton(slotLabels[index]);
            }

            SurvivalInventoryPanel panel = EnsureComponent<SurvivalInventoryPanel>(sectionObject);
            panel.Configure(
                slotButtons,
                slotLabels,
                selectedHotbarLabel,
                slotIcons,
                iconLibrary,
                prevPageButton,
                nextPageButton,
                pageLabel);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        // Small square icon image used by inventory slots and crafting rows; hidden until a
        // sprite is assigned at runtime.
        static Image EnsureItemIconImage(Transform parent, string name, Vector2 anchoredPosition, float size)
        {
            GameObject iconObject = EnsureRectChild(parent, name);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(iconRect, anchoredPosition, new Vector2(size, size));

            Image icon = EnsureComponent<Image>(iconObject);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            return icon;
        }

        static SurvivalCraftingPanel EnsureSurvivalCraftingSection(Transform parent, BlockiverseItemIconLibrary iconLibrary)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Crafting", new Vector2(480.0f, -82.0f), new Vector2(216.0f, 380.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Crafting",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(180.0f, 34.0f));

            TMP_Text statusLabel = EnsureLabel(
                sectionObject.transform,
                "Status",
                "Ready",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(180.0f, 28.0f),
                TextDimColor);

            TMP_Text[] recipeLabels = new TMP_Text[5];
            Button[] recipeButtons = new Button[recipeLabels.Length];
            Image[] recipeIcons = new Image[recipeLabels.Length];

            for (int index = 0; index < recipeLabels.Length; index++)
            {
                recipeIcons[index] = EnsureItemIconImage(
                    sectionObject.transform,
                    $"Recipe Icon {index + 1}",
                    new Vector2(16.0f, -84.0f - index * 40.0f),
                    28.0f);

                recipeLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Recipe {index + 1}",
                    string.Empty,
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(50.0f, -82.0f - index * 40.0f),
                    new Vector2(146.0f, 36.0f));
                recipeButtons[index] = EnsureTextButton(recipeLabels[index]);
            }

            TMP_Text previousPageLabel = EnsureLabel(
                sectionObject.transform,
                "Previous Recipes",
                "<",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -82.0f - recipeLabels.Length * 40.0f),
                new Vector2(40.0f, 32.0f));
            Button previousPageButton = EnsureTextButton(previousPageLabel);

            TMP_Text recipePageLabel = EnsureLabel(
                sectionObject.transform,
                "Recipe Page",
                "1/1",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(62.0f, -82.0f - recipeLabels.Length * 40.0f),
                new Vector2(88.0f, 32.0f),
                TextDimColor);

            TMP_Text nextPageLabel = EnsureLabel(
                sectionObject.transform,
                "Next Recipes",
                ">",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(156.0f, -82.0f - recipeLabels.Length * 40.0f),
                new Vector2(40.0f, 32.0f));
            Button nextPageButton = EnsureTextButton(nextPageLabel);

            // Mend Bench repair of the held tool (§10.7) — gated at runtime by station proximity.
            TMP_Text repairLabel = EnsureLabel(
                sectionObject.transform,
                "Repair",
                "Repair Held Tool",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -122.0f - recipeLabels.Length * 40.0f),
                new Vector2(180.0f, 36.0f));
            Button repairButton = EnsureTextButton(repairLabel);

            SurvivalCraftingPanel panel = EnsureComponent<SurvivalCraftingPanel>(sectionObject);
            panel.Configure(recipeButtons, recipeLabels, statusLabel, recipeIcons, iconLibrary);
            panel.ConfigurePaging(previousPageButton, nextPageButton, recipePageLabel);
            panel.ConfigureRepairButton(repairButton);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static SurvivalCratePanel EnsureSurvivalCrateSection(Transform parent)
        {
            GameObject sectionObject = EnsureHudSection(parent, "Shared Crate", new Vector2(706.0f, -82.0f), new Vector2(216.0f, 300.0f));

            EnsureLabel(
                sectionObject.transform,
                "Label",
                "Shared Crate",
                24,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -10.0f),
                new Vector2(180.0f, 34.0f));

            TMP_Text statusLabel = EnsureLabel(
                sectionObject.transform,
                "Status",
                "Shared crate",
                20,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -44.0f),
                new Vector2(180.0f, 28.0f),
                TextDimColor);

            TMP_Text[] slotLabels = new TMP_Text[4];
            Button[] slotButtons = new Button[slotLabels.Length];
            for (int index = 0; index < slotLabels.Length; index++)
            {
                slotLabels[index] = EnsureLabel(
                    sectionObject.transform,
                    $"Slot {index + 1}",
                    "Empty",
                    15,
                    TextAnchor.MiddleLeft,
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(16.0f, -82.0f - index * 40.0f),
                    new Vector2(180.0f, 36.0f));
                slotButtons[index] = EnsureTextButton(slotLabels[index]);
            }

            TMP_Text depositLabel = EnsureLabel(
                sectionObject.transform,
                "Deposit",
                "Deposit Held",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(16.0f, -82.0f - slotLabels.Length * 40.0f),
                new Vector2(180.0f, 36.0f));
            Button depositButton = EnsureTextButton(depositLabel);

            SurvivalCratePanel panel = EnsureComponent<SurvivalCratePanel>(sectionObject);
            panel.Configure(slotButtons, slotLabels, statusLabel, depositButton);
            EditorUtility.SetDirty(panel);
            return panel;
        }

        static GameObject EnsureHudSection(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject sectionObject = EnsureRectChild(parent, name);
            RectTransform sectionRect = sectionObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sectionRect, anchoredPosition, size);
            Image sectionImage = EnsureComponent<Image>(sectionObject);
            ApplySlicedSprite(sectionImage, GetUiSprite(UiSpriteForHudSection(name)));
            sectionImage.color = SurvivalHudSectionColor;
            return sectionObject;
        }

        static string UiSpriteForHudSection(string name)
        {
            return name switch
            {
                "Inventory" => "inventory_panel",
                "Crafting" => "crafting_panel",
                _ => "hotbar_frame"
            };
        }

        static Slider EnsureHudSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject sliderObject = EnsureRectChild(parent, name);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, anchoredPosition, size);

            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0.0f;
            slider.maxValue = 100.0f;
            slider.value = 100.0f;

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            background.color = ComfortMenuControlColor;

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            Sprite healthPipSprite = GetUiSprite("health_pip");
            if (healthPipSprite != null)
            {
                fill.sprite = healthPipSprite;
                fill.type = Image.Type.Tiled;
            }
            fill.color = SurvivalHudAccentColor;

            slider.fillRect = fillRect;
            slider.targetGraphic = background;
            return slider;
        }

        static Toggle EnsureToggleControl(
            Transform parent,
            string name,
            string label,
            bool isOn,
            Vector2 anchoredPosition)
        {
            GameObject toggleObject = EnsureRectChild(parent, name);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(toggleRect, anchoredPosition, new Vector2(456.0f, 64.0f));

            Toggle toggle = EnsureComponent<Toggle>(toggleObject);

            GameObject backgroundObject = EnsureRectChild(toggleObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(backgroundRect, new Vector2(0.0f, -10.0f), new Vector2(44.0f, 44.0f));
            Image background = EnsureComponent<Image>(backgroundObject);
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                background.sprite = roundedSprite;
                background.type = Image.Type.Sliced;
            }
            background.color = ControlNormalColor;
            ConfigureUiRaycastBlocker(background);

            GameObject checkmarkObject = EnsureRectChild(backgroundObject.transform, "Checkmark");
            RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.offsetMin = new Vector2(7.0f, 7.0f);
            checkmarkRect.offsetMax = new Vector2(-7.0f, -7.0f);
            Image checkmark = EnsureComponent<Image>(checkmarkObject);
            Sprite checkmarkSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Checkmark.psd");
            if (checkmarkSprite != null)
                checkmark.sprite = checkmarkSprite;
            checkmark.color = AccentColor;
            checkmark.raycastTarget = false;

            EnsureLabel(
                toggleObject.transform,
                "Label",
                label,
                30,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(60.0f, -4.0f),
                new Vector2(380.0f, 56.0f));

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.colors = new ColorBlock
            {
                normalColor      = ControlNormalColor,
                highlightedColor = ControlHighlightColor,
                pressedColor     = ControlPressedColor,
                selectedColor    = ControlSelectedColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            toggle.isOn = isOn;
            ConfigureSelectableFeedback(toggle);
            return toggle;
        }

        static Slider EnsureSnapTurnSlider(Transform parent, float value, Vector2 anchoredPosition)
        {
            GameObject rowObject = EnsureRectChild(parent, "Snap Turn Slider");
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(456.0f, 88.0f));

            EnsureLabel(
                rowObject.transform,
                "Label",
                "Snap Turn",
                28,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f),
                new Vector2(220.0f, 40.0f),
                TextDimColor);

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -48.0f), new Vector2(420.0f, 36.0f));
            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 15.0f;
            slider.maxValue = 90.0f;
            slider.wholeNumbers = true;

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

        static Slider EnsureVignetteSlider(Transform parent, float value, Vector2 anchoredPosition)
        {
            GameObject rowObject = EnsureRectChild(parent, "Vignette Slider");
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(rowRect, anchoredPosition, new Vector2(456.0f, 88.0f));

            EnsureLabel(rowObject.transform, "Label", "Strength", 32, TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f), new Vector2(0.0f, 1.0f),
                new Vector2(0.0f, 0.0f), new Vector2(220.0f, 40.0f));

            GameObject sliderObject = EnsureRectChild(rowObject.transform, "Slider");
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(sliderRect, new Vector2(0.0f, -48.0f), new Vector2(420.0f, 32.0f));

            Slider slider = EnsureComponent<Slider>(sliderObject);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0.0f;
            slider.maxValue = 1.0f;
            slider.wholeNumbers = false;

            GameObject backgroundObject = EnsureRectChild(sliderObject.transform, "Background");
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.0f, 0.35f);
            backgroundRect.anchorMax = new Vector2(1.0f, 0.65f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = EnsureComponent<Image>(backgroundObject);
            background.color = ComfortMenuControlColor;
            ConfigureUiRaycastBlocker(background);

            GameObject fillAreaObject = EnsureRectChild(sliderObject.transform, "Fill Area");
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(8.0f, 0.0f);
            fillAreaRect.offsetMax = new Vector2(-8.0f, 0.0f);

            GameObject fillObject = EnsureRectChild(fillAreaObject.transform, "Fill");
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0.0f, 0.35f);
            fillRect.anchorMax = new Vector2(1.0f, 0.65f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = EnsureComponent<Image>(fillObject);
            fill.color = ComfortMenuAccentColor;
            fill.raycastTarget = false;

            GameObject handleAreaObject = EnsureRectChild(sliderObject.transform, "Handle Slide Area");
            RectTransform handleAreaRect = handleAreaObject.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8.0f, 0.0f);
            handleAreaRect.offsetMax = new Vector2(-8.0f, 0.0f);

            GameObject handleObject = EnsureRectChild(handleAreaObject.transform, "Handle");
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0.0f, 0.5f);
            handleRect.anchorMax = new Vector2(0.0f, 0.5f);
            handleRect.sizeDelta = new Vector2(32.0f, 32.0f);
            Image handle = EnsureComponent<Image>(handleObject);
            handle.color = Color.white;
            ConfigureUiRaycastBlocker(handle);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.value = value;
            ConfigureSelectableFeedback(slider);
            return slider;
        }

        static Button EnsureButtonControl(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            return EnsureButtonControl(parent, name, label, anchoredPosition, new Vector2(220.0f, 54.0f));
        }

        static Button EnsureButtonControl(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject buttonObject = EnsureRectChild(parent, name);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(buttonRect, anchoredPosition, size);

            // Rounded 9-slice background using the Unity built-in UI sprite.
            Image image = EnsureComponent<Image>(buttonObject);
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                image.sprite = roundedSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = ControlNormalColor;
            ConfigureUiRaycastBlocker(image);

            Button button = EnsureComponent<Button>(buttonObject);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = ControlNormalColor,
                highlightedColor = AccentHighlightColor,
                pressedColor     = ControlPressedColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            ConfigureSelectableFeedback(button);

            EnsureLabel(
                buttonObject.transform,
                "Label",
                label,
                26,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            TextMeshProUGUI buttonLabel = buttonObject.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            buttonLabel.raycastTarget = false;
            RectTransform labelRect = buttonLabel.GetComponent<RectTransform>();
            labelRect.offsetMin = new Vector2(8.0f, 4.0f);
            labelRect.offsetMax = new Vector2(-8.0f, -4.0f);
            return button;
        }

        static void SetButtonLabelFontSize(Button button, float fontSize)
        {
            if (button == null)
                return;

            TMP_Text label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null)
                ConfigureGeneratedTextSizing(label, fontSize);
        }

        static Button EnsureTextButton(TMP_Text label)
        {
            Button button = EnsureComponent<Button>(label.gameObject);
            label.raycastTarget = true;
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = Color.white,
                highlightedColor = AccentHighlightColor,
                pressedColor     = AccentColor,
                selectedColor    = AccentColor,
                disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f),
                colorMultiplier  = 1.0f,
                fadeDuration     = 0.08f
            };
            ConfigureSelectableFeedback(button);
            return button;
        }

        static TMP_InputField EnsureInputFieldControl(
            Transform parent,
            string name,
            string placeholder,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard,
            TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default)
        {
            GameObject inputObject = EnsureRectChild(parent, name);
            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            ConfigureTopLeftRect(inputRect, anchoredPosition, size);

            // Remove legacy InputField if present (migration).
            InputField legacyInput = inputObject.GetComponent<InputField>();
            if (legacyInput != null)
                UnityEngine.Object.DestroyImmediate(legacyInput);

            Image image = EnsureComponent<Image>(inputObject);
            Sprite roundedSprite = GetRoundedSprite();
            if (roundedSprite != null)
            {
                image.sprite = roundedSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = ControlNormalColor;
            ConfigureUiRaycastBlocker(image);

            TMP_InputField input = EnsureComponent<TMP_InputField>(inputObject);
            input.targetGraphic = image;
            input.text = value;
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.keyboardType = TouchScreenKeyboardType.Default;
            input.characterValidation = contentType == TMP_InputField.ContentType.IntegerNumber
                ? TMP_InputField.CharacterValidation.Integer
                : TMP_InputField.CharacterValidation.None;
            input.interactable = true;
            input.readOnly = false;

            TextMeshProUGUI textComp = EnsureLabel(
                inputObject.transform,
                "Text",
                value,
                24,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 0.5f),
                new Vector2(18.0f, 0.0f),
                new Vector2(-36.0f, 0.0f));
            textComp.richText = false;
            textComp.raycastTarget = false;
            ClearLocalizedTextBinding(textComp);

            TextMeshProUGUI placeholderText = EnsureLabel(
                inputObject.transform,
                "Placeholder",
                placeholder,
                24,
                TextAnchor.MiddleLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 0.5f),
                new Vector2(18.0f, 0.0f),
                new Vector2(-36.0f, 0.0f),
                new Color(0.65f, 0.70f, 0.75f, 0.60f));
            placeholderText.raycastTarget = false;

            input.textComponent = textComp;
            input.placeholder = placeholderText;

            // Native VR text entry: open the Quest system keyboard when the field is selected.
            BlockiverseSystemKeyboardField keyboardField = EnsureComponent<BlockiverseSystemKeyboardField>(inputObject);
            keyboardField.Configure(input, TouchScreenKeyboardType.Default);

            ConfigureSelectableFeedback(input);
            return input;
        }

        // Returns a TextMeshProUGUI label so the caller can set .text; also removes any legacy
        // UnityEngine.UI.Text on the same object to avoid double-rendering during migration.
        static TextMeshProUGUI EnsureLabel(
            Transform parent,
            string name,
            string label,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color? colorOverride = null)
        {
            GameObject labelObject = EnsureRectChild(parent, name);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = anchorMin;
            labelRect.anchorMax = anchorMax;
            labelRect.pivot = pivot;
            labelRect.anchoredPosition = anchoredPosition;
            labelRect.sizeDelta = size;

            // Remove legacy Text if present (idempotent migration).
            Text legacyText = labelObject.GetComponent<Text>();
            if (legacyText != null)
                UnityEngine.Object.DestroyImmediate(legacyText);

            TextMeshProUGUI tmp = EnsureComponent<TextMeshProUGUI>(labelObject);
            tmp.text = label;
            tmp.color = colorOverride ?? TextPrimaryColor;
            tmp.enableWordWrapping = true;
            ConfigureGeneratedTextSizing(tmp, fontSize);

            // Map TextAnchor to TMP alignment.
            tmp.alignment = alignment switch
            {
                TextAnchor.UpperLeft    => TextAlignmentOptions.TopLeft,
                TextAnchor.UpperCenter  => TextAlignmentOptions.Top,
                TextAnchor.UpperRight   => TextAlignmentOptions.TopRight,
                TextAnchor.MiddleLeft   => TextAlignmentOptions.MidlineLeft,
                TextAnchor.MiddleCenter => TextAlignmentOptions.Midline,
                TextAnchor.MiddleRight  => TextAlignmentOptions.MidlineRight,
                TextAnchor.LowerLeft    => TextAlignmentOptions.BottomLeft,
                TextAnchor.LowerCenter  => TextAlignmentOptions.Bottom,
                TextAnchor.LowerRight   => TextAlignmentOptions.BottomRight,
                _                       => TextAlignmentOptions.MidlineLeft,
            };

            // Use the TMP default font if available. TMP_Settings.defaultFontAsset throws a
            // NullReferenceException on first run before Essential Resources are imported, so
            // guard it. The label still renders (TMP uses an internal fallback).
            try
            {
                TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
                if (defaultFont != null)
                    tmp.font = defaultFont;
            }
            catch
            {
                // TMP_Settings not yet initialized — font will be assigned on next bootstrap.
            }

            ConfigureLocalizedTextBinding(tmp, label);
            return tmp;
        }

        static void ConfigureGeneratedTextSizing(TMP_Text text, float preferredFontSize)
        {
            if (text == null)
                return;

            text.enableAutoSizing = true;
            text.fontSize = preferredFontSize;
            text.fontSizeMax = preferredFontSize;
            text.fontSizeMin = Mathf.Max(14.0f, Mathf.Min(18.0f, preferredFontSize * 0.72f));
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        static void ConfigureLocalizedTextBinding(TextMeshProUGUI tmp, string fallbackText)
        {
            if (tmp == null)
                return;

            BlockiverseLocalizedText localizedText = tmp.GetComponent<BlockiverseLocalizedText>();
            if (!BlockiverseLocalization.TryGetKnownKeyForDefaultText(fallbackText, out string key))
            {
                if (localizedText != null)
                    UnityEngine.Object.DestroyImmediate(localizedText);
                return;
            }

            localizedText = localizedText != null
                ? localizedText
                : EnsureComponent<BlockiverseLocalizedText>(tmp.gameObject);
            localizedText.Configure(key, fallbackText);
            EditorUtility.SetDirty(localizedText);
        }

        static void ClearLocalizedTextBinding(TextMeshProUGUI tmp)
        {
            if (tmp == null)
                return;

            BlockiverseLocalizedText localizedText = tmp.GetComponent<BlockiverseLocalizedText>();
            if (localizedText != null)
                UnityEngine.Object.DestroyImmediate(localizedText);
        }

        static GameObject EnsureRectChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
            {
                existing.gameObject.layer = parent.gameObject.layer;
                EditorUtility.SetDirty(existing.gameObject);
                return existing.gameObject;
            }

            GameObject child = new(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            child.layer = parent.gameObject.layer;
            EditorUtility.SetDirty(child);
            return child;
        }

        static GameObject EnsureRectChildMigrated(Transform parent, Transform legacyParent, string name)
        {
            Transform existing = parent.Find(name);
            Transform legacy = legacyParent != null ? legacyParent.Find(name) : null;

            if (existing == null && legacy != null)
            {
                legacy.SetParent(parent, false);
                legacy.gameObject.layer = parent.gameObject.layer;
                EditorUtility.SetDirty(legacy.gameObject);
                return legacy.gameObject;
            }

            if (existing != null && legacy != null && legacy != existing)
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);

            return EnsureRectChild(parent, name);
        }

        static void ConfigureCanvasWorldCamera(Canvas canvas, Transform head)
        {
            if (canvas == null)
                return;

            canvas.worldCamera = head != null ? head.GetComponent<Camera>() : null;
        }

        // World-space VR canvases must be raycast by tracked-device rays, not the screen-space
        // GraphicRaycaster. Swap in XRI's TrackedDeviceGraphicRaycaster so XRRayInteractors can
        // drive buttons, toggles, sliders, and scrolling.
        static TrackedDeviceGraphicRaycaster EnsureTrackedDeviceRaycaster(GameObject canvasObject)
        {
            GraphicRaycaster legacyRaycaster = canvasObject.GetComponent<GraphicRaycaster>();

            if (legacyRaycaster != null)
                UnityEngine.Object.DestroyImmediate(legacyRaycaster);

            SetLayerRecursively(canvasObject, GetCompositionUiLayerIndex());

            CanvasGroup inputGate = EnsureComponent<CanvasGroup>(canvasObject);
            inputGate.interactable = true;
            inputGate.blocksRaycasts = true;
            inputGate.ignoreParentGroups = false;

            TrackedDeviceGraphicRaycaster raycaster = EnsureComponent<TrackedDeviceGraphicRaycaster>(canvasObject);
            EditorUtility.SetDirty(inputGate);
            EditorUtility.SetDirty(canvasObject);
            return raycaster;
        }

        static void EnsureDecorativeCanvasDoesNotReceiveUi(GameObject canvasObject)
        {
            if (canvasObject == null)
                return;

            GraphicRaycaster legacyRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
            if (legacyRaycaster != null)
                UnityEngine.Object.DestroyImmediate(legacyRaycaster);

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvasObject.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster != null)
                UnityEngine.Object.DestroyImmediate(trackedRaycaster);

            CanvasGroup inputGate = EnsureComponent<CanvasGroup>(canvasObject);
            inputGate.interactable = false;
            inputGate.blocksRaycasts = false;
            inputGate.ignoreParentGroups = false;

            foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>(true))
            {
                graphic.gameObject.layer = GetCompositionUiLayerIndex();
                graphic.raycastTarget = false;
                EditorUtility.SetDirty(graphic);
            }

            EditorUtility.SetDirty(inputGate);
            EditorUtility.SetDirty(canvasObject);
        }

        static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);

            if (existing != null)
                return existing.gameObject;

            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0)
                return;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                child.gameObject.layer = layer;
                EditorUtility.SetDirty(child.gameObject);
            }
        }

        static void ConfigureUiRaycastBlocker(Graphic graphic)
        {
            if (graphic == null)
                return;

            graphic.raycastTarget = true;
            EditorUtility.SetDirty(graphic);
        }

        static void ConfigureSelectableFeedback(Selectable selectable)
        {
            if (selectable == null)
                return;

            BlockiverseUiSelectableFeedback feedback =
                EnsureComponent<BlockiverseUiSelectableFeedback>(selectable.gameObject);
            feedback.Configure();
            EditorUtility.SetDirty(feedback);
        }

        static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();

            if (component == null)
                component = gameObject.AddComponent<T>();

            return component;
        }

        // Returns Unity's built-in 9-slice rounded-rectangle sprite ("Background.psd").
        // When set on an Image with Image.Type.Sliced it produces rounded corners at any size.
        // Returns null when running without the UISprite built-ins (very rare; handled gracefully by callers).
        static Sprite GetRoundedSprite() => Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");

        static Sprite GetUiSprite(string name)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Blockiverse/Art/Sprites/UI/{name}.png");
            return sprite != null ? sprite : GetRoundedSprite();
        }

        static Sprite GetVfxSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Blockiverse/Art/Sprites/VFX/{name}.png");

        static void ApplySlicedSprite(Image image, Sprite sprite)
        {
            if (image == null || sprite == null)
                return;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            ConfigureUiRaycastBlocker(image);
        }

        static void ConfigureTopLeftRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
        {
            rectTransform.anchorMin = new Vector2(0.0f, 1.0f);
            rectTransform.anchorMax = new Vector2(0.0f, 1.0f);
            rectTransform.pivot = new Vector2(0.0f, 1.0f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        static void RemovePersistentListeners(UnityEvent unityEvent, UnityEngine.Object target, string methodName)
        {
            for (int index = unityEvent.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                if (unityEvent.GetPersistentTarget(index) == target &&
                    unityEvent.GetPersistentMethodName(index) == methodName)
                {
                    UnityEventTools.RemovePersistentListener(unityEvent, index);
                }
            }
        }
    }
}
