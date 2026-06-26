using Blockiverse.Gameplay;
using Blockiverse.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureXrRigSurvivalHud(GameObject rig)
        {
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            if (cameraOffset == null)
                return;

            GameObject hudObject = EnsureChild(cameraOffset, SurvivalHudName);
            hudObject.transform.localPosition = new Vector3(0.0f, 1.16f, 1.25f);
            hudObject.transform.localRotation = Quaternion.Euler(12.0f, 0.0f, 0.0f);
            hudObject.transform.localScale = Vector3.one * SurvivalHudScale;
            SetLayerRecursively(hudObject, GetInteractionLayerIndex());

            UIDocument document = EnsureComponent<UIDocument>(hudObject);
            document.panelSettings = EnsureUiToolkitMenuPanelSettings();
            SetFixedWorldSpaceSizeMode(document);
            document.worldSpaceSize = new Vector2(640.0f, 360.0f);

            BlockiverseHudToolkitSurface hudSurface = EnsureComponent<BlockiverseHudToolkitSurface>(hudObject);
            hudSurface.Configure(document);

            SurvivalHudController controller = EnsureComponent<SurvivalHudController>(hudObject);
            controller.Configure(targetHudSurface: hudSurface);

            BlockiverseSubtitleToastPanel toastPanel = EnsureComponent<BlockiverseSubtitleToastPanel>(rig);
            toastPanel.Configure(hudSurface);

            SurvivalFeedbackBridge feedbackBridge = EnsureComponent<SurvivalFeedbackBridge>(rig);
            feedbackBridge.ConfigureToastPanel(toastPanel);

            CreativeHotbar hotbar = rig.GetComponentInChildren<CreativeHotbar>(includeInactive: true);
            hotbar?.ConfigureHudSurface(hudSurface);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(hudSurface);
            EditorUtility.SetDirty(hudObject);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(toastPanel);
            EditorUtility.SetDirty(feedbackBridge);
            if (hotbar != null)
                EditorUtility.SetDirty(hotbar);
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

        static T EnsureComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();

            if (component == null)
                component = gameObject.AddComponent<T>();

            return component;
        }

        static Sprite GetVfxSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Blockiverse/Art/Sprites/VFX/{name}.png");

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
