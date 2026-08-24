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
        // Sizes and names for the multiplayer join/leave toast; see EnsureXrRigSubtitleToast.
        const string SubtitleToastName = "Multiplayer Toast";
        static readonly Vector2 SubtitleToastSize = new(760.0f, 96.0f);

        // The Block Menu. Its uGUI catalog browser is gone — the UI Toolkit catalog screen and
        // CreativeHotbarController own block picking now — but this stays because it is the only
        // generator of the scene CreativeHotbar, which is gameplay state rather than menu state:
        // Scenes.cs resolves it by the literal path "Camera Offset/Block Menu" and feeds it to
        // BlockiverseWorldPresentation, which is how a placed block is chosen, and
        // CreativeHotbarController mirrors every Toolkit selection back into it.
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

            // CreativeHotbar writes the selected block's display name here, so the label is not
            // decoration: ConfigureFromDefaultCatalog takes it and nothing else supplies one.
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

            // This pass re-authors an existing prefab rather than rebuilding it, so a control the
            // bootstrapper stops generating survives every rerun unless it is destroyed by name.
            // The swatches predate the catalog browser; everything after them IS the browser,
            // replaced by the UI Toolkit catalog screen.
            var staleControls = new List<string>
            {
                "Swatch A",
                "Swatch B",
                "Swatch C",
                "Category Button",
                "Category Label",
                "Prev Page Button",
                "Page Label",
                "Next Page Button",
                "Search Field",
            };

            for (int index = 0; index < 12; index++)
                staleControls.Add($"Entry Button {index}");

            foreach (string stale in staleControls)
            {
                Transform staleControl = panelObject.transform.Find(stale);
                if (staleControl != null)
                    UnityEngine.Object.DestroyImmediate(staleControl.gameObject);
            }

            CreativeHotbar menu = EnsureComponent<CreativeHotbar>(menuObject);
            menu.ConfigureFromDefaultCatalog(selectedLabel);
            menu.ConfigureCanvas(canvas);

            BlockiverseWorldSpacePanelPresenter presenter = EnsureComponent<BlockiverseWorldSpacePanelPresenter>(menuObject);
            presenter.Configure(canvas, head, 1.12f, -0.34f, -0.18f, 0.0f);
            presenter.ConfigureFeedback(BlockiverseAudioCue.InventoryOpen, BlockiverseAudioCue.InventoryClose);

            // Display-only now that the browser controls are gone. A panel that still raycasts but
            // has nothing to click would swallow controller rays aimed past it at a UI Toolkit
            // panel behind, and this also strips the TrackedDeviceGraphicRaycaster from rigs
            // generated before the cut.
            EnsureDecorativeCanvasDoesNotReceiveUi(menuObject);

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
            artworkImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPlainPath);
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

        // The multiplayer join/leave toast (shipped in 94bec837). Not a menu, and not ported: the
        // Toolkit StatusToastController covers only FINAL harvest rejections and early-returns on
        // anything else, so it is not a replacement. The label this panel writes into used to be
        // the uGUI survival HUD's status line, which means deleting that HUD without re-homing the
        // toast here would have turned SurvivalFeedbackBridge.ShowToast into a silent no-op —
        // no compile error, no failing test, just "Player joined." never appearing on device.
        //
        // It keeps a small uGUI canvas for the same reason the startup splash does: it is a
        // passive label with no controls and no UI Toolkit owner. The dark HUD panel that used to
        // sit behind the text is gone with the HUD; the label stands on its own.
        static void EnsureXrRigSubtitleToast(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            if (cameraOffset == null)
                return;

            GameObject toastObject = EnsureRectChild(cameraOffset, SubtitleToastName);
            toastObject.transform.localPosition = new Vector3(0.0f, 1.06f, 1.25f);
            toastObject.transform.localRotation = Quaternion.Euler(12.0f, 0.0f, 0.0f);
            toastObject.transform.localScale = Vector3.one * 0.0013f;

            RectTransform toastRect = toastObject.GetComponent<RectTransform>();
            toastRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, SubtitleToastSize.x);
            toastRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SubtitleToastSize.y);

            Canvas canvas = EnsureComponent<Canvas>(toastObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 35;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(toastObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            // The canvas stays enabled and the panel shows and hides the label's own GameObject,
            // so nothing renders between toasts. Anything else on this canvas would be permanently
            // visible, which is why there is only the one child.
            TMP_Text messageLabel = EnsureLabel(
                toastObject.transform,
                "Message",
                string.Empty,
                30,
                TextAnchor.MiddleCenter,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            EnsureDecorativeCanvasDoesNotReceiveUi(toastObject);

            BlockiverseSubtitleToastPanel toastPanel = EnsureComponent<BlockiverseSubtitleToastPanel>(rig);
            toastPanel.Configure(messageLabel);
            SurvivalFeedbackBridge feedbackBridge = EnsureComponent<SurvivalFeedbackBridge>(rig);
            feedbackBridge.ConfigureToastPanel(toastPanel);

            EditorUtility.SetDirty(toastObject);
            EditorUtility.SetDirty(toastPanel);
            EditorUtility.SetDirty(feedbackBridge);
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
            tmp.textWrappingMode = TextWrappingModes.Normal;
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

            SetLayerRecursively(canvasObject, GetInteractionLayerIndex());

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
                graphic.gameObject.layer = GetInteractionLayerIndex();
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

        static Sprite GetRoundedSprite() => GetUiControlSprite("settings_panel") ?? GetUiControlSprite("hotbar_frame");

        static Sprite GetUiSprite(string name)
        {
            Sprite sprite = GetUiControlSprite(name);
            return sprite != null ? sprite : GetRoundedSprite();
        }

        static Sprite GetUiControlSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Blockiverse/Art/Sprites/UI/{name}.png");

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
