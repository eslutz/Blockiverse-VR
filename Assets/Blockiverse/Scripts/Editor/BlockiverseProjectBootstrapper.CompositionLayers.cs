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

            RemoveStaleChild(cameraOffset, "Blockiverse UI Pointer Projection");

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
            InteractableUIMirror uiMirror = panel.GetComponent<InteractableUIMirror>();
            if (uiMirror != null)
            {
                uiMirror.enabled = false;
                EditorUtility.SetDirty(uiMirror);
            }

            Camera canvasCamera = EnsureCompositionCanvasCamera(canvas.transform, panelName);
            ConfigureCompositionLayerSerializedReferences(compositionLayer, canvas, null);

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
            EditorUtility.SetDirty(canvasCamera);
            EditorUtility.SetDirty(renderScale);
            if (presenter != null)
                EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(panel.gameObject);
        }

        static int EnsureUnityLayer(string layerName)
        {
            if (layerName == BlockiverseProject.InteractionLayerName)
                return EnsureUnityLayer(layerName, BlockiverseProject.InteractionLayerIndex);

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
