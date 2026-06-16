using System;
using Blockiverse.Core;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.UIInteraction;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureGeneratedVrUiPanels(Transform cameraOffset)
        {
            if (cameraOffset == null)
                return;

            RemoveStaleChild(cameraOffset, "Blockiverse UI Pointer Projection");

            EnsureVrUiPanel(cameraOffset, StartupLoadingOverlayName);
            EnsureVrUiPanel(cameraOffset, SurvivalHudName);

            EnsureVrUiPanel(cameraOffset, TitleMenuName);
            EnsureVrUiPanel(cameraOffset, PauseMenuName);
            EnsureVrUiPanel(cameraOffset, DeathScreenName);
            EnsureVrUiPanel(cameraOffset, NewWorldPanelName);
            EnsureVrUiPanel(cameraOffset, LoadWorldPanelName);
            EnsureVrUiPanel(cameraOffset, SettingsPanelName);
            EnsureVrUiPanel(cameraOffset, ComfortMenuName);
            EnsureVrUiPanel(cameraOffset, AudioSettingsPanelName);
            EnsureVrUiPanel(cameraOffset, ControlsPanelName);
            EnsureVrUiPanel(cameraOffset, WorldDetailsPanelName);
            EnsureVrUiPanel(cameraOffset, LanMultiplayerPanelName);
            EnsureVrUiPanel(cameraOffset, CreativeToolsPanelName);
            EnsureVrUiPanel(cameraOffset, StationPanelName);
            EnsureVrUiPanel(cameraOffset, ControllerMappingPopupName);
            EnsureVrUiPanel(cameraOffset, BlockMenuName);

            EnsureVrUiPanel(cameraOffset, ConfirmDialogName);
        }

        static void EnsureVrUiPanel(Transform cameraOffset, string panelName)
        {
            Transform panel = cameraOffset.Find(panelName);
            if (panel == null)
                return;

            Canvas canvas = panel.GetComponent<Canvas>();
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            if (canvas == null || rectTransform == null)
                return;

            SetLayerRecursively(panel.gameObject, GetCompositionUiLayerIndex());
            RemoveStaleCompositionLayerComponents(panel.gameObject);
            EditorUtility.SetDirty(panel.gameObject);
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
