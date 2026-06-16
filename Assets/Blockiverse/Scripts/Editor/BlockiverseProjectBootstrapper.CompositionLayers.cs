using System;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.XR;
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
            ControllerMappingPopupName,
            ConfirmDialogName,
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

            Transform head = cameraOffset.Find("Main Camera");
            GameObject menuSurface = EnsureMenuCompositionSurface(cameraOffset, head);
            Transform menuCanvas = menuSurface.transform.Find(MenuCompositionCanvasName);

            foreach (string panelName in RoutedCompositionMenuPanelNames)
                RouteMenuPanelToCompositionSurface(cameraOffset, menuCanvas, menuSurface.transform, panelName);

            foreach (string panelName in WorldSpaceVrUiPanelNames)
                EnsureWorldSpaceVrUiPanel(cameraOffset, panelName);

            EnsureXrVisualProjectionRig(cameraOffset, head);
        }

        static GameObject EnsureMenuCompositionSurface(Transform cameraOffset, Transform head)
        {
            GameObject surface = EnsureChild(cameraOffset, MenuCompositionSurfaceName);
            surface.layer = GetCompositionUiLayerIndex();
            surface.transform.localPosition = GameMenuLocalPosition;
            surface.transform.localRotation = Quaternion.identity;
            surface.transform.localScale = Vector3.one * GameMenuScale;

            CompositionLayer layer = EnsureComponent<CompositionLayer>(surface);
            if (layer.LayerData is not QuadLayerData quadLayerData)
            {
                layer.ChangeLayerDataType(typeof(QuadLayerData));
                quadLayerData = layer.LayerData as QuadLayerData;
            }

            if (quadLayerData != null)
            {
                quadLayerData.Size = ComfortMenuSize;
                quadLayerData.ApplyTransformScale = true;
            }

            layer.Order = CompositionLayerOrderMenu;
            TexturesExtension textures = EnsureComponent<TexturesExtension>(surface);
            InteractableUIMirror mirror = EnsureComponent<InteractableUIMirror>(surface);
            mirror.enabled = true;
            XRSimpleInteractable simpleInteractable = EnsureComponent<XRSimpleInteractable>(surface);
            MeshCollider meshCollider = EnsureComponent<MeshCollider>(surface);
            EnsureComponent<UIHandle>(surface);
            EnsureComponent<UIFocus>(surface);
            QuadUIScale quadUiScale = EnsureComponent<QuadUIScale>(surface);

            GameObject canvasObject = EnsureRectChild(surface.transform, MenuCompositionCanvasName);
            canvasObject.layer = GetCompositionUiLayerIndex();
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one;

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
            ConfigureInteractableUIMirrorSerializedReferences(
                mirror,
                layer,
                textures,
                simpleInteractable,
                meshCollider,
                quadUiScale,
                canvasCamera,
                trackedDeviceRaycaster);
            BlockiverseCompositionLayerRenderScale renderScale = EnsureComponent<BlockiverseCompositionLayerRenderScale>(surface);
            renderScale.Configure(canvas, layer, textures, canvasCamera, CompositionUiRenderScale);

            SetLayerRecursively(surface, GetCompositionUiLayerIndex());
            EditorUtility.SetDirty(canvasGroup);
            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(canvasObject);
            EditorUtility.SetDirty(renderScale);
            EditorUtility.SetDirty(surface);
            return surface;
        }


        static void ConfigureInteractableUIMirrorSerializedReferences(
            InteractableUIMirror mirror,
            CompositionLayer compositionLayer,
            TexturesExtension texturesExtension,
            XRSimpleInteractable simpleInteractable,
            MeshCollider meshCollider,
            LayerUIScale layerUiScale,
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
            SetSerializedObject(serializedMirror, "canvasCamera", canvasCamera);
            SetSerializedObject(serializedMirror, "trackedDeviceGraphicRaycaster", trackedDeviceRaycaster);
            serializedMirror.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mirror);
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
            camera.nearClipPlane = 0.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.001f);
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

        static void EnsureXrVisualProjectionRig(Transform cameraOffset, Transform head)
        {
            int visualLayer = EnsureXrVisualProjectionLayer();
            GameObject projectionRig = EnsureChild(cameraOffset, XrVisualProjectionRigName);
            projectionRig.layer = visualLayer;
            projectionRig.transform.localPosition = Vector3.zero;
            projectionRig.transform.localRotation = Quaternion.identity;
            projectionRig.transform.localScale = Vector3.one;

            CompositionLayer layer = EnsureComponent<CompositionLayer>(projectionRig);
            if (layer.LayerData is not ProjectionLayerRigData)
                layer.ChangeLayerDataType(typeof(ProjectionLayerRigData));
            layer.Order = CompositionLayerOrderMenu + 5;

            TexturesExtension textures = EnsureComponent<TexturesExtension>(projectionRig);
            textures.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;

            Camera mainCamera = head != null ? head.GetComponent<Camera>() : null;
            if (mainCamera != null)
            {
                mainCamera.cullingMask &= ~BlockiverseProject.XrVisualProjectionLayerMask;
                EditorUtility.SetDirty(mainCamera);
            }

            EnsureProjectionEyeCamera(projectionRig.transform, "Left Camera", visualLayer,
                BlockiverseInputActionNames.LeftEyePosition, BlockiverseInputActionNames.LeftEyeRotation, mainCamera);
            EnsureProjectionEyeCamera(projectionRig.transform, "Right Camera", visualLayer,
                BlockiverseInputActionNames.RightEyePosition, BlockiverseInputActionNames.RightEyeRotation, mainCamera);

            SetControllerVisualLayer(cameraOffset.Find("Left Controller"), visualLayer);
            SetControllerVisualLayer(cameraOffset.Find("Right Controller"), visualLayer);

            EditorUtility.SetDirty(layer);
            EditorUtility.SetDirty(projectionRig);
        }

        static void EnsureProjectionEyeCamera(
            Transform parent,
            string name,
            int visualLayer,
            string positionActionName,
            string rotationActionName,
            Camera mainCamera)
        {
            GameObject cameraObject = EnsureChild(parent, name);
            cameraObject.layer = visualLayer;
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
            cameraObject.transform.localScale = Vector3.one;

            Camera camera = EnsureComponent<Camera>(cameraObject);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << visualLayer;
            camera.depth = mainCamera != null ? mainCamera.depth - 1.0f : -1.0f;
            camera.enabled = true;

            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(cameraObject);
            ConfigureProjectionEyePoseDriverReferenceActions(poseDriver, positionActionName, rotationActionName);

            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(cameraObject);
        }

        static void ConfigureProjectionEyePoseDriverReferenceActions(
            TrackedPoseDriver poseDriver,
            string positionActionName,
            string rotationActionName)
        {
            BlockiverseInputRig.ConfigurePoseDriverActionReferences(
                poseDriver,
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, positionActionName),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, rotationActionName),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState));
        }

        static void SetControllerVisualLayer(Transform controller, int visualLayer)
        {
            if (controller == null)
                return;

            SetLayerRecursively(controller.gameObject, visualLayer);
            EditorUtility.SetDirty(controller.gameObject);
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
            if (!string.IsNullOrEmpty(targetLayer.stringValue) && targetLayer.stringValue != layerName)
                throw new InvalidOperationException(
                    $"Unity layer {layerIndex} is already assigned to {targetLayer.stringValue}; expected {layerName}.");

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
    }
}
