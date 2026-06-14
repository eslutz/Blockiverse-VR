using System;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.UIInteraction;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureGeneratedCompositionLayerPanels(Transform cameraOffset, Transform head)
        {
            if (cameraOffset == null)
                return;

            EnsureCompositionPanel(cameraOffset, StartupLoadingOverlayName, CompositionLayerOrderHud);
            EnsureCompositionPanel(cameraOffset, SurvivalHudName, CompositionLayerOrderHud);

            EnsureCompositionPanel(cameraOffset, TitleMenuName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, PauseMenuName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, DeathScreenName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, NewWorldPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, LoadWorldPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, SettingsPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, ComfortMenuName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, AudioSettingsPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, ControlsPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, WorldDetailsPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, LanMultiplayerPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, CreativeToolsPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, StationPanelName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, ControllerMappingPopupName, CompositionLayerOrderMenu);
            EnsureCompositionPanel(cameraOffset, BlockMenuName, CompositionLayerOrderMenu);

            EnsureCompositionPanel(cameraOffset, ConfirmDialogName, CompositionLayerOrderModal);
            EnsurePointerProjectionLayer(cameraOffset, head);
        }

        static void EnsureCompositionPanel(Transform cameraOffset, string panelName, int layerOrder)
        {
            Transform panel = cameraOffset.Find(panelName);
            if (panel == null)
                return;

            Canvas canvas = panel.GetComponent<Canvas>();
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            if (canvas == null || rectTransform == null)
                return;

            CompositionLayer compositionLayer = EnsureComponent<CompositionLayer>(panel.gameObject);
            SetCompositionLayerOrder(compositionLayer, layerOrder);

            QuadLayerData quadLayerData = compositionLayer.LayerData as QuadLayerData;
            if (quadLayerData == null)
            {
                quadLayerData = new QuadLayerData();
                compositionLayer.ChangeLayerDataType(quadLayerData);
            }

            quadLayerData.Size = rectTransform.rect.size;
            quadLayerData.ApplyTransformScale = true;

            TexturesExtension texturesExtension = EnsureComponent<TexturesExtension>(panel.gameObject);
            InteractableUIMirror uiMirror = EnsureComponent<InteractableUIMirror>(panel.gameObject);
            Camera canvasCamera = EnsureCompositionCanvasCamera(canvas.transform, panelName);
            ConfigureCompositionLayerSerializedReferences(compositionLayer, canvas, uiMirror);
            ConfigureInteractableMirrorSerializedReferences(
                uiMirror,
                compositionLayer,
                texturesExtension,
                canvas,
                canvasCamera);

            BlockiverseCompositionLayerRenderScale renderScale =
                EnsureComponent<BlockiverseCompositionLayerRenderScale>(panel.gameObject);
            renderScale.Configure(canvas, compositionLayer, texturesExtension, canvasCamera, CompositionUiRenderScale);

            BlockiverseWorldSpacePanelPresenter presenter =
                panel.GetComponent<BlockiverseWorldSpacePanelPresenter>();
            if (presenter != null)
                presenter.ConfigureCompositionLayer(compositionLayer);

            compositionLayer.enabled = canvas.enabled;
            EditorUtility.SetDirty(compositionLayer);
            EditorUtility.SetDirty(texturesExtension);
            EditorUtility.SetDirty(uiMirror);
            EditorUtility.SetDirty(canvasCamera);
            EditorUtility.SetDirty(renderScale);
            if (presenter != null)
                EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panel.gameObject);
        }

        static void EnsurePointerProjectionLayer(Transform cameraOffset, Transform head)
        {
            EnsureUnityLayer(BlockiverseProject.InteractionLayerName);
            int layerIndex = EnsureUnityLayer(CompositionPointerProjectionLayerName);
            GameObject projectionObject = EnsureChild(cameraOffset, CompositionPointerProjectionLayerName);
            projectionObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            projectionObject.transform.localScale = Vector3.one;
            projectionObject.layer = layerIndex;

            CompositionLayer compositionLayer = EnsureComponent<CompositionLayer>(projectionObject);
            if (!(compositionLayer.LayerData is ProjectionLayerRigData))
                compositionLayer.ChangeLayerDataType(new ProjectionLayerRigData());
            SetCompositionLayerOrder(compositionLayer, CompositionLayerOrderPointerProjection);
            compositionLayer.enabled = true;

            TexturesExtension texturesExtension = EnsureComponent<TexturesExtension>(projectionObject);
            ClearProjectionLayerTextures(texturesExtension);

            Camera mainCamera = head != null ? head.GetComponent<Camera>() : null;
            if (mainCamera != null)
                mainCamera.cullingMask &= ~(1 << layerIndex);

            RemoveLegacyProjectionEyeCamera(projectionObject.transform, "Left Eye");
            RemoveLegacyProjectionEyeCamera(projectionObject.transform, "Right Eye");
            EnsureProjectionEyeCamera(
                projectionObject.transform,
                "Left Camera",
                layerIndex,
                mainCamera,
                BlockiverseInputActionNames.LeftEyePosition,
                BlockiverseInputActionNames.LeftEyeRotation);
            EnsureProjectionEyeCamera(
                projectionObject.transform,
                "Right Camera",
                layerIndex,
                mainCamera,
                BlockiverseInputActionNames.RightEyePosition,
                BlockiverseInputActionNames.RightEyeRotation);
            texturesExtension.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;

            AssignPointerProjectionLayer(cameraOffset.Find("Left Controller"), layerIndex);
            AssignPointerProjectionLayer(cameraOffset.Find("Right Controller"), layerIndex);
            AssignPointerProjectionLayer(cameraOffset.Find(LeftAimPoseName), layerIndex);
            AssignPointerProjectionLayer(cameraOffset.Find(RightAimPoseName), layerIndex);
            RemoveProjectionTexturePrefabInstanceOverrides(projectionObject);

            EditorUtility.SetDirty(compositionLayer);
            EditorUtility.SetDirty(texturesExtension);
            EditorUtility.SetDirty(projectionObject);
            if (mainCamera != null)
                EditorUtility.SetDirty(mainCamera);
        }

        static Camera EnsureProjectionEyeCamera(
            Transform parent,
            string name,
            int layerIndex,
            Camera mainCamera,
            string positionActionName,
            string rotationActionName)
        {
            GameObject cameraObject = EnsureChild(parent, name);
            cameraObject.layer = layerIndex;
            cameraObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            Camera camera = EnsureComponent<Camera>(cameraObject);
            camera.enabled = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << layerIndex;
            camera.nearClipPlane = mainCamera != null ? mainCamera.nearClipPlane : 0.05f;
            camera.farClipPlane = mainCamera != null ? mainCamera.farClipPlane : 500.0f;
            camera.fieldOfView = mainCamera != null ? mainCamera.fieldOfView : 60.0f;
            camera.depth = mainCamera != null ? mainCamera.depth - 1.0f : -1.0f;
            ClearCameraTargetTexture(camera);

            TrackedPoseDriver poseDriver = EnsureComponent<TrackedPoseDriver>(cameraObject);
            BlockiverseInputRig.ConfigurePoseDriverActionReferences(
                poseDriver,
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, positionActionName),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, rotationActionName),
                LoadInputActionReference(BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState));

            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(poseDriver);
            EditorUtility.SetDirty(cameraObject);
            return camera;
        }

        static void ClearProjectionLayerTextures(TexturesExtension texturesExtension)
        {
            if (texturesExtension == null)
                return;

            texturesExtension.LeftTexture = null;
            texturesExtension.RightTexture = null;
            RevertPrefabObjectReferenceOverride(texturesExtension, "m_LeftTexture");
            RevertPrefabObjectReferenceOverride(texturesExtension, "m_RightTexture");
            EditorUtility.SetDirty(texturesExtension);
        }

        static void ClearCameraTargetTexture(Camera camera)
        {
            if (camera == null)
                return;

            camera.targetTexture = null;
            RevertPrefabObjectReferenceOverride(camera, "m_TargetTexture");
            EditorUtility.SetDirty(camera);
        }

        static void RevertPrefabObjectReferenceOverride(UnityEngine.Object target, string propertyPath)
        {
            if (target == null || !PrefabUtility.IsPartOfPrefabInstance(target))
                return;

            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
        }

        static void RemoveProjectionTexturePrefabInstanceOverrides(GameObject projectionObject)
        {
            if (projectionObject == null)
                return;

            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(projectionObject);
            if (instanceRoot == null)
                return;

            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
            if (modifications == null || modifications.Length == 0)
                return;

            PropertyModification[] filtered = modifications
                .Where(modification => !IsProjectionTexturePrefabOverride(modification))
                .ToArray();
            if (filtered.Length == modifications.Length)
                return;

            PrefabUtility.SetPropertyModifications(instanceRoot, filtered);
            EditorUtility.SetDirty(instanceRoot);
        }

        static bool IsProjectionTexturePrefabOverride(PropertyModification modification)
        {
            if (modification == null)
                return false;

            string propertyPath = modification.propertyPath;
            if (modification.target == null)
                return propertyPath == "m_TargetTexture"
                       || propertyPath == "m_LeftTexture"
                       || propertyPath == "m_RightTexture";

            if (propertyPath == "m_TargetTexture")
                return modification.target is Camera camera
                       && (camera.name == "Left Camera" || camera.name == "Right Camera")
                       && camera.transform.parent != null
                       && camera.transform.parent.name == CompositionPointerProjectionLayerName;

            if (propertyPath == "m_LeftTexture" || propertyPath == "m_RightTexture")
                return modification.target is TexturesExtension texturesExtension
                       && texturesExtension.name == CompositionPointerProjectionLayerName;

            return false;
        }

        static void RemoveLegacyProjectionEyeCamera(Transform parent, string name)
        {
            Transform legacy = parent.Find(name);
            if (legacy == null)
                return;

            UnityEngine.Object.DestroyImmediate(legacy.gameObject);
        }

        static void AssignPointerProjectionLayer(Transform root, int layerIndex)
        {
            if (root == null)
                return;

            root.gameObject.layer = layerIndex;
            foreach (Transform child in root)
                AssignPointerProjectionLayer(child, layerIndex);
            EditorUtility.SetDirty(root.gameObject);
        }

        static int EnsureUnityLayer(string layerName)
        {
            if (layerName == BlockiverseProject.InteractionLayerName)
                return EnsureUnityLayer(layerName, BlockiverseProject.InteractionLayerIndex);

            if (layerName == CompositionPointerProjectionLayerName)
                return EnsureUnityLayer(layerName, BlockiverseProject.CompositionPointerProjectionLayerIndex);

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

        static void SetCompositionLayerOrder(CompositionLayer compositionLayer, int layerOrder)
        {
            if (compositionLayer == null)
                return;

            var serializedLayer = new SerializedObject(compositionLayer);
            SetSerializedInt(serializedLayer, "m_Order", layerOrder);
            serializedLayer.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureCompositionLayerSerializedReferences(
            CompositionLayer compositionLayer,
            Canvas canvas,
            InteractableUIMirror uiMirror)
        {
            if (compositionLayer == null)
                return;

            var serializedLayer = new SerializedObject(compositionLayer);
            SetSerializedObject(serializedLayer, "m_UICanvas", canvas);
            SetSerializedObject(serializedLayer, "m_UIMirrorComponent", uiMirror);
            serializedLayer.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureInteractableMirrorSerializedReferences(
            InteractableUIMirror uiMirror,
            CompositionLayer compositionLayer,
            TexturesExtension texturesExtension,
            Canvas canvas,
            Camera canvasCamera)
        {
            if (uiMirror == null)
                return;

            var serializedMirror = new SerializedObject(uiMirror);
            SetSerializedObject(serializedMirror, "texturesExtension", texturesExtension);
            SetSerializedObject(serializedMirror, "compositionLayer", compositionLayer);
            SetSerializedObject(serializedMirror, "canvasRectTransform", canvas != null ? canvas.GetComponent<RectTransform>() : null);
            SetSerializedObject(serializedMirror, "canvasCamera", canvasCamera);
            serializedMirror.ApplyModifiedPropertiesWithoutUndo();
        }

        static Camera EnsureCompositionCanvasCamera(Transform canvasTransform, string panelName)
        {
            Transform existing = canvasTransform != null ? canvasTransform.Find("CanvasCamera") : null;
            GameObject cameraObject = existing != null
                ? existing.gameObject
                : new GameObject("CanvasCamera");

            if (canvasTransform != null && cameraObject.transform.parent != canvasTransform)
                cameraObject.transform.SetParent(canvasTransform, false);

            cameraObject.transform.localScale = Vector3.one;
            cameraObject.transform.localPosition = new Vector3(0.0f, 0.0f, -100.0f);
            cameraObject.transform.localRotation = Quaternion.identity;
            if (canvasTransform != null)
                cameraObject.layer = canvasTransform.gameObject.layer;

            Camera camera = EnsureComponent<Camera>(cameraObject);
            camera.enabled = false;
            camera.orthographic = true;
            camera.nearClipPlane = 0.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.001f);
            camera.name = "CanvasCamera";

            EditorUtility.SetDirty(cameraObject);
            EditorUtility.SetDirty(camera);
            return camera;
        }
    }
}
