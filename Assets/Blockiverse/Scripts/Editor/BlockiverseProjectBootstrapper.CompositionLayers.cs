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
        static readonly string[] GeneratedGameplayMenuPanelNames =
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

            EnsureControllerVisualsUseMainCameraLayer(cameraOffset);
            RemoveStaleChild(cameraOffset, MenuCompositionSurfaceName);

            foreach (string panelName in WorldSpaceVrUiPanelNames)
                EnsureWorldSpaceVrUiPanel(cameraOffset, panelName);

            foreach (string panelName in GeneratedGameplayMenuPanelNames)
                EnsureWorldSpaceVrUiPanel(cameraOffset, panelName);
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

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(panel);
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
