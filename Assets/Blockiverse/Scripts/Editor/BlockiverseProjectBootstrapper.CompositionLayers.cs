using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.UIInteraction;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static readonly string[] RoutedCompositionMenuPanelNames =
        {
            TitleMenuName,
            PauseMenuName,
            DeathScreenName,
            NewWorldPanelName,
            LoadWorldPanelName,
            SettingsPanelName,
            ComfortMenuName,
            AudioSettingsPanelName,
            ControlsPanelName,
            WorldDetailsPanelName,
            LanMultiplayerPanelName,
            CreativeToolsPanelName,
            StationPanelName,
            ConfirmDialogName,
            ErrorDialogName,
            InventoryPanelName,
            CraftingPanelName,
            CatalogPanelName,
            CratePanelName,
            ControllerMappingPopupName,
        };

        static readonly string[] WorldSpaceVrUiPanelNames =
        {
            StartupLoadingOverlayName,
            SurvivalHudName,
            BlockMenuName,
        };

        static void EnsureGeneratedVrUiPanels(Transform cameraOffset)
        {
            if (cameraOffset == null)
                return;

            RemoveStaleChild(cameraOffset, "Blockiverse UI Pointer Projection");
            RemoveStaleChild(cameraOffset, XrVisualProjectionRigName);
            
            Transform head = cameraOffset.Find("Main Camera");
            GameObject surface = EnsureMenuCompositionSurface(cameraOffset, head);
            Transform menuSurface = surface.transform;
            Transform menuCanvas = menuSurface.Find(MenuCompositionCanvasName);

            EnsureControllerVisualsUseMainCameraLayer(cameraOffset);

            foreach (string panelName in WorldSpaceVrUiPanelNames)
                EnsureWorldSpaceVrUiPanel(cameraOffset, panelName);

            foreach (string panelName in RoutedCompositionMenuPanelNames)
            {
                RouteMenuPanelToCompositionSurface(cameraOffset, menuCanvas, menuSurface, panelName);
            }
        }

        static GameObject EnsureMenuCompositionSurface(Transform cameraOffset, Transform head)
        {
            GameObject surface = EnsureChild(cameraOffset, MenuCompositionSurfaceName);
            
            // Reorder: Create canvasObject first so that when we add CompositionLayer/InteractableUIMirror,
            // they find the existing canvas child instead of auto-generating a default one named "Canvas".
            GameObject canvasObject = EnsureRectChild(surface.transform, MenuCompositionCanvasName);
            canvasObject.layer = GetCompositionUiLayerIndex();
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * GameMenuScale;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.anchoredPosition = Vector2.zero;
            canvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ComfortMenuSize.x);
            canvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ComfortMenuSize.y);

            Canvas canvas = EnsureComponent<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            canvas.enabled = true;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            TrackedDeviceGraphicRaycaster trackedDeviceRaycaster = EnsureTrackedDeviceRaycaster(canvasObject);
            CanvasGroup canvasGroup = EnsureComponent<CanvasGroup>(canvasObject);
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.ignoreParentGroups = false;

            Camera canvasCamera = EnsureMenuCompositionCanvasCamera(canvasObject.transform);

            // Now configure the surface itself.
            surface.layer = GetInteractionLayerIndex();
            surface.transform.localPosition = GameMenuLocalPosition;
            surface.transform.localRotation = Quaternion.Euler(GameMenuPitchDegrees, 0.0f, 0.0f);
            surface.transform.localScale = Vector3.one;

            CompositionLayer layer = EnsureComponent<CompositionLayer>(surface);
            CompositionOutline outline = EnsureComponent<CompositionOutline>(surface);
            if (layer.LayerData is not QuadLayerData quadLayerData)
            {
                layer.ChangeLayerDataType(typeof(QuadLayerData));
                quadLayerData = layer.LayerData as QuadLayerData;
            }

            if (quadLayerData != null)
            {
                quadLayerData.Size = ComfortMenuCompositionSize;
                quadLayerData.ApplyTransformScale = true;
            }

            layer.Order = CompositionLayerOrderMenu;
            layer.enabled = false;
            TexturesExtension textures = EnsureComponent<TexturesExtension>(surface);

            InteractableUIMirror mirror = EnsureComponent<InteractableUIMirror>(surface);
            mirror.enabled = true;
            XRSimpleInteractable simpleInteractable = EnsureComponent<XRSimpleInteractable>(surface);
            MeshCollider meshCollider = EnsureComponent<MeshCollider>(surface);
            simpleInteractable.colliders.Clear();
            simpleInteractable.colliders.Add(meshCollider);
            EnsureComponent<UIHandle>(surface);
            EnsureComponent<UIFocus>(surface);
            QuadUIScale quadUiScale = EnsureComponent<QuadUIScale>(surface);
            RemoveComponentIfPresent<GraphicRaycaster>(canvasObject);

            // Clean up any default Canvas auto-created by the package components during registration.
            RemoveStaleCompositionLayerDefaultCanvas(surface.transform);

            ConfigureCompositionLayerSerializedReferences(layer, canvas, mirror, outline);
            ConfigureInteractableUIMirrorSerializedReferences(
                mirror,
                layer,
                textures,
                simpleInteractable,
                meshCollider,
                quadUiScale,
                canvasRect,
                canvasGroup,
                canvasCamera,
                trackedDeviceRaycaster);
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.ignoreParentGroups = false;
            BlockiverseCompositionLayerRenderScale renderScale = EnsureComponent<BlockiverseCompositionLayerRenderScale>(surface);
            renderScale.Configure(canvas, layer, textures, canvasCamera, CompositionUiRenderScale);
            EnsureCompositionMenuCursor(surface, canvasRect, cameraOffset);

            // Ensure the canvas and all its routed UI panels are on the composition layer.
            SetLayerRecursively(canvasObject, GetCompositionUiLayerIndex());
            
            // The surface quad itself must stay on the interaction layer to hit rays.
            surface.layer = GetInteractionLayerIndex();

            EditorUtility.SetDirty(canvasGroup);
            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(simpleInteractable);
            EditorUtility.SetDirty(canvasObject);
            EditorUtility.SetDirty(renderScale);
            EditorUtility.SetDirty(surface);
            return surface;
        }

        static void RemoveStaleCompositionLayerDefaultCanvas(Transform surface)
        {
            if (surface == null)
                return;

            Transform defaultCanvas = surface.Find("Canvas");
            if (defaultCanvas != null)
                UnityEngine.Object.DestroyImmediate(defaultCanvas.gameObject);
        }

        static void EnsureCompositionMenuCursor(GameObject surface, RectTransform menuCanvas, Transform cameraOffset)
        {
            if (surface == null || menuCanvas == null)
                return;

            GameObject cursorObject = EnsureRectChild(menuCanvas, MenuCompositionCursorName);
            cursorObject.layer = GetCompositionUiLayerIndex();
            cursorObject.transform.localPosition = Vector3.zero;
            cursorObject.transform.localRotation = Quaternion.Euler(0.0f, 0.0f, 45.0f);
            cursorObject.transform.localScale = Vector3.one;

            RectTransform cursorRect = cursorObject.GetComponent<RectTransform>();
            cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
            cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
            cursorRect.pivot = new Vector2(0.5f, 0.5f);
            cursorRect.anchoredPosition = Vector2.zero;
            cursorRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 18.0f);
            cursorRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 18.0f);

            Image cursorImage = EnsureComponent<Image>(cursorObject);
            cursorImage.color = PointerLineColor;
            cursorImage.raycastTarget = false;
            cursorImage.maskable = false;

            BlockiverseCompositionMenuCursor cursor = EnsureComponent<BlockiverseCompositionMenuCursor>(surface);
            BlockiverseInputRig inputRig = cameraOffset != null ? cameraOffset.GetComponentInParent<BlockiverseInputRig>() : null;
            cursor.Configure(inputRig, menuCanvas, cursorRect, cursorImage);
            cursorObject.SetActive(false);

            EditorUtility.SetDirty(cursorImage);
            EditorUtility.SetDirty(cursor);
            EditorUtility.SetDirty(cursorObject);
        }


        static void ConfigureInteractableUIMirrorSerializedReferences(
            InteractableUIMirror mirror,
            CompositionLayer compositionLayer,
            TexturesExtension texturesExtension,
            XRSimpleInteractable simpleInteractable,
            MeshCollider meshCollider,
            LayerUIScale layerUiScale,
            RectTransform canvasRectTransform,
            CanvasGroup canvasGroup,
            Camera canvasCamera,
            TrackedDeviceGraphicRaycaster trackedDeviceRaycaster)
        {
            if (mirror == null)
                return;

            var serializedMirror = new SerializedObject(mirror);
            SetSerializedObject(serializedMirror, "compositionLayer", compositionLayer);
            SetSerializedObject(serializedMirror, "texturesExtension", texturesExtension);
            SetSerializedObject(serializedMirror, "xrSimpleInteractable", simpleInteractable);
            SetSerializedObject(serializedMirror, "meshCollider", meshCollider);
            SetSerializedObject(serializedMirror, "layerUIScale", layerUiScale);
            SetSerializedObject(serializedMirror, "canvasRectTransform", canvasRectTransform);
            SetSerializedObject(serializedMirror, "canvasGroup", canvasGroup);
            SetSerializedObject(serializedMirror, "canvasCamera", canvasCamera);
            SetSerializedObject(serializedMirror, "trackedDeviceGraphicRaycaster", trackedDeviceRaycaster);
            serializedMirror.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mirror);
        }

        static void ConfigureCompositionLayerSerializedReferences(
            CompositionLayer compositionLayer,
            Canvas canvas,
            Component mirror,
            Component outline)
        {
            if (compositionLayer == null)
                return;

            var serializedLayer = new SerializedObject(compositionLayer);
            SetSerializedObject(serializedLayer, "m_UICanvas", canvas);
            SetSerializedObject(serializedLayer, "m_UIMirrorComponent", mirror);
            SetSerializedObject(serializedLayer, "m_LayerOutline", outline);
            serializedLayer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(compositionLayer);
        }

        static Camera EnsureMenuCompositionCanvasCamera(Transform canvasTransform)
        {
            GameObject cameraObject = EnsureChild(canvasTransform, "CanvasCamera");
            cameraObject.layer = GetCompositionUiLayerIndex();
            cameraObject.transform.localPosition = new Vector3(0.0f, 0.0f, -100.0f);
            cameraObject.transform.localRotation = Quaternion.identity;
            cameraObject.transform.localScale = Vector3.one;

            Camera camera = EnsureComponent<Camera>(cameraObject);
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << GetCompositionUiLayerIndex();
            camera.enabled = false;
            EditorUtility.SetDirty(cameraObject);
            EditorUtility.SetDirty(camera);
            return camera;
        }

        static GameObject EnsureRoutedMenuRectChild(
            Transform cameraOffset,
            Transform menuCanvas,
            Transform legacyParent,
            string name)
        {
            Transform existing = menuCanvas != null ? menuCanvas.Find(name) : null;
            Transform legacy = cameraOffset != null ? cameraOffset.Find(name) : null;
            if (legacy == null && legacyParent != null)
                legacy = legacyParent.Find(name);

            if (existing == null && legacy != null)
            {
                legacy.SetParent(menuCanvas, false);
                legacy.gameObject.layer = menuCanvas.gameObject.layer;
                EditorUtility.SetDirty(legacy.gameObject);
                return legacy.gameObject;
            }

            if (existing != null && legacy != null && legacy != existing)
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);

            return EnsureRectChild(menuCanvas, name);
        }

        static GameObject EnsureWorldSpaceMenuRectChild(
            Transform cameraOffset,
            Transform legacyParent,
            string name)
        {
            Transform existing = cameraOffset != null ? cameraOffset.Find(name) : null;
            Transform legacy = FindLegacyCompositionMenuPanel(cameraOffset, name);
            if (legacy == null && legacyParent != null)
                legacy = legacyParent.Find(name);

            if (existing == null && legacy != null)
            {
                legacy.SetParent(cameraOffset, false);
                legacy.gameObject.layer = GetInteractionLayerIndex();
                legacy.gameObject.SetActive(true);
                EditorUtility.SetDirty(legacy.gameObject);
                return legacy.gameObject;
            }

            if (existing != null && legacy != null && legacy != existing)
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);

            return EnsureRectChild(cameraOffset, name);
        }

        static Transform FindLegacyCompositionMenuPanel(Transform cameraOffset, string name)
        {
            Transform menuSurface = cameraOffset != null ? cameraOffset.Find(MenuCompositionSurfaceName) : null;
            Transform menuCanvas = menuSurface != null ? menuSurface.Find(MenuCompositionCanvasName) : null;
            return menuCanvas != null ? menuCanvas.Find(name) : null;
        }

        static void RouteMenuPanelToCompositionSurface(
            Transform cameraOffset,
            Transform menuCanvas,
            Transform menuSurface,
            string panelName)
        {
            if (cameraOffset == null || menuCanvas == null || menuSurface == null)
                return;

            Transform routedPanel = menuCanvas.Find(panelName);
            Transform legacyPanel = cameraOffset.Find(panelName);

            if (routedPanel == null && legacyPanel != null)
                routedPanel = legacyPanel;
            else if (routedPanel != null && legacyPanel != null && legacyPanel != routedPanel)
                UnityEngine.Object.DestroyImmediate(legacyPanel.gameObject);

            if (routedPanel == null)
                return;

            if (routedPanel.parent != menuCanvas)
                routedPanel.SetParent(menuCanvas, false);

            RectTransform rectTransform = routedPanel.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
            }

            routedPanel.localPosition = Vector3.zero;
            routedPanel.localRotation = Quaternion.identity;
            routedPanel.localScale = Vector3.one;

            RemovePanelCanvasComponents(routedPanel.gameObject);
            RemoveStaleCompositionLayerComponents(routedPanel.gameObject);
            SetLayerRecursively(routedPanel.gameObject, GetCompositionUiLayerIndex());

            BlockiverseWorldSpacePanelPresenter presenter =
                routedPanel.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            if (presenter != null)
            {
                presenter.ConfigureSharedCompositionTarget(routedPanel.gameObject, menuSurface);
                EditorUtility.SetDirty(presenter);
            }

            CreativeHotbar hotbar = routedPanel.GetComponent<CreativeHotbar>();
            if (hotbar != null)
            {
                hotbar.ConfigureVisibilityRoot(routedPanel.gameObject);
                EditorUtility.SetDirty(hotbar);
            }

            Blockiverse.UI.BlockiverseComfortMenu comfortMenu =
                routedPanel.GetComponent<Blockiverse.UI.BlockiverseComfortMenu>();
            if (comfortMenu != null)
            {
                comfortMenu.ConfigureVisibilityRoot(routedPanel.gameObject);
                EditorUtility.SetDirty(comfortMenu);
            }

            routedPanel.gameObject.SetActive(false);
            EditorUtility.SetDirty(routedPanel.gameObject);
        }

        static void EnsureWorldSpaceVrUiPanel(Transform cameraOffset, string panelName)
        {
            Transform panel = cameraOffset.Find(panelName);
            if (panel == null)
                return;

            SetLayerRecursively(panel.gameObject, GetInteractionLayerIndex());
            panel.gameObject.SetActive(true);
            RemoveStaleCompositionLayerComponents(panel.gameObject);
            EditorUtility.SetDirty(panel.gameObject);
        }

        static void RemovePanelCanvasComponents(GameObject panel)
        {
            RemoveComponentIfPresent<GraphicRaycaster>(panel);
            RemoveComponentIfPresent<TrackedDeviceGraphicRaycaster>(panel);
            RemoveComponentIfPresent<CanvasScaler>(panel);
            RemoveComponentIfPresent<Canvas>(panel);
        }

        static void EnsureControllerVisualsUseMainCameraLayer(Transform cameraOffset)
        {
            if (cameraOffset == null)
                return;

            EnsureControllerVisualUsesMainCameraLayer(cameraOffset.Find("Left Controller"));
            EnsureControllerVisualUsesMainCameraLayer(cameraOffset.Find("Right Controller"));
        }

        static void EnsureControllerVisualUsesMainCameraLayer(Transform controller)
        {
            if (controller == null)
                return;

            SetObjectLayer(controller, 0);
            SetObjectLayer(controller.Find(ControllerRayOriginName), 0);
            SetObjectLayer(controller.Find(InteractionRayName), 0);
            SetObjectLayer(controller.Find(TeleportRayName), 0);
        }

        static void SetObjectLayer(Transform target, int layer)
        {
            if (target == null)
                return;

            target.gameObject.layer = layer;
            foreach (Transform child in target)
                SetObjectLayer(child, layer);
            
            EditorUtility.SetDirty(target.gameObject);
        }

        static void RemoveStaleCompositionLayerComponents(GameObject panel)
        {
            if (panel == null)
                return;

            RemoveComponentIfPresent<BlockiverseCompositionLayerRenderScale>(panel);
            RemoveComponentIfPresent<InteractableUIMirror>(panel);
            RemoveComponentIfPresent<UIHandle>(panel);
            RemoveComponentIfPresent<UIFocus>(panel);
            RemoveComponentIfPresent<TexturesExtension>(panel);
            RemoveComponentIfPresent<CompositionOutline>(panel);
            RemoveComponentIfPresent<CompositionLayer>(panel);

            Transform canvasCamera = panel.transform.Find("CanvasCamera");
            if (canvasCamera != null)
                UnityEngine.Object.DestroyImmediate(canvasCamera.gameObject);
        }

        static void RemoveComponentIfPresent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject != null ? gameObject.GetComponent<T>() : null;
            if (component != null)
                UnityEngine.Object.DestroyImmediate(component);
        }

        static int EnsureUnityLayer(string layerName)
        {
            if (layerName == BlockiverseProject.InteractionLayerName)
                return EnsureUnityLayer(layerName, BlockiverseProject.InteractionLayerIndex);

            if (layerName == BlockiverseProject.XrVisualProjectionLayerName)
                return EnsureUnityLayer(layerName, BlockiverseProject.XrVisualProjectionLayerIndex);

            const string tagManagerPath = "ProjectSettings/TagManager.asset";
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath(tagManagerPath);
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                throw new InvalidOperationException("Unity TagManager settings asset could not be loaded.");

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            tagManager.UpdateIfRequiredOrScript();
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int index = 0; index < layers.arraySize; index++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (layer.stringValue == layerName)
                    return index;
            }

            for (int index = 8; index < layers.arraySize; index++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (!string.IsNullOrEmpty(layer.stringValue))
                    continue;

                layer.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tagManager.targetObject);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(tagManagerPath, ImportAssetOptions.ForceUpdate);
                return index;
            }

            throw new InvalidOperationException($"No free Unity layer slot is available for {layerName}.");
        }

        static int EnsureUnityLayer(string layerName, int layerIndex)
        {
            const string tagManagerPath = "ProjectSettings/TagManager.asset";
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath(tagManagerPath);
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                throw new InvalidOperationException("Unity TagManager settings asset could not be loaded.");

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            tagManager.UpdateIfRequiredOrScript();
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layerIndex < 0 || layerIndex >= layers.arraySize)
                throw new InvalidOperationException($"Unity layer index {layerIndex} is outside the TagManager layer array.");

            SerializedProperty targetLayer = layers.GetArrayElementAtIndex(layerIndex);
            if (!string.IsNullOrEmpty(targetLayer.stringValue) &&
                targetLayer.stringValue != layerName &&
                !IsCompositionLayerGeneratedCanvasLayerName(targetLayer.stringValue))
            {
                throw new InvalidOperationException(
                    $"Unity layer {layerIndex} is already assigned to {targetLayer.stringValue}; expected {layerName}.");
            }

            bool changed = false;
            for (int index = 8; index < layers.arraySize; index++)
            {
                if (index == layerIndex)
                    continue;

                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (layer.stringValue != layerName)
                    continue;

                layer.stringValue = string.Empty;
                changed = true;
            }

            if (targetLayer.stringValue != layerName)
            {
                targetLayer.stringValue = layerName;
                changed = true;
            }

            if (changed)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tagManager.targetObject);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(tagManagerPath, ImportAssetOptions.ForceUpdate);
            }

            return layerIndex;
        }

        static bool IsCompositionLayerGeneratedCanvasLayerName(string layerName) =>
            layerName.StartsWith("Canvas_", StringComparison.Ordinal);
    }
}
