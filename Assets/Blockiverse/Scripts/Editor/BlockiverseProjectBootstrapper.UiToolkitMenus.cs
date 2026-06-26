using Blockiverse.Core;
using Blockiverse.UI;
using Blockiverse.VR;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        const string UiToolkitMenuSurfaceName = "UI Toolkit Menu Surface";

        static BlockiverseUiToolkitMenuSurface EnsureUiToolkitMenuSurface(Transform parent, Transform head)
        {
            EnsureUiToolkitMenuAssets();

            GameObject surfaceObject = EnsureChild(parent, UiToolkitMenuSurfaceName);
            surfaceObject.transform.localPosition = GameMenuLocalPosition;
            surfaceObject.transform.localRotation = Quaternion.Euler(GameMenuPitchDegrees, 0.0f, 0.0f);
            surfaceObject.transform.localScale = Vector3.one * UiToolkitMenuScale;
            SetLayerRecursively(surfaceObject, GetVrUiLayerIndex());

            UIDocument document = EnsureComponent<UIDocument>(surfaceObject);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(BlockiverseProject.UiToolkitMenuShellPath);
            document.panelSettings = EnsureUiToolkitMenuPanelSettings();
            SetFixedWorldSpaceSizeMode(document);
            document.worldSpaceSize = UiToolkitMenuWorldSpaceSize;

            BoxCollider worldSpaceCollider = EnsureComponent<BoxCollider>(surfaceObject);
            ConfigureUiToolkitWorldSpaceCollider(worldSpaceCollider);
            AssignUiToolkitWorldSpaceCollider(document, worldSpaceCollider);

            XRUIToolkitManager toolkitManager = EnsureComponent<XRUIToolkitManager>(surfaceObject);
            BlockiverseUiToolkitMenuPresenter presenter = EnsureComponent<BlockiverseUiToolkitMenuPresenter>(surfaceObject);
            presenter.ConfigureWorldSpaceTarget(
                surfaceObject,
                head,
                GameMenuDistanceMeters,
                0.0f,
                GameMenuVerticalOffsetMeters,
                GameMenuPitchDegrees,
                UiToolkitMenuScale,
                recenterWhenShown: false);
            BlockiverseUiToolkitMenuSurface surface = EnsureComponent<BlockiverseUiToolkitMenuSurface>(surfaceObject);
            surface.Configure(document);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(worldSpaceCollider);
            EditorUtility.SetDirty(toolkitManager);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(surfaceObject);
            return surface;
        }

        static void SetFixedWorldSpaceSizeMode(UIDocument document)
        {
#if UNITY_6000_5_OR_NEWER
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
#else
            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
#endif
        }

        static void EnsureUiToolkitMenuAssets()
        {
            EnsureFolder(BlockiverseProject.UiFolderPath);
            EnsureFolder(BlockiverseProject.UiMenuFolderPath);
            EnsureUiToolkitMenuPanelSettings();
        }

        static PanelSettings EnsureUiToolkitMenuPanelSettings()
        {
            PanelSettings panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(BlockiverseProject.UiToolkitMenuPanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, BlockiverseProject.UiToolkitMenuPanelSettingsPath);
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1280, 720);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = 40;
            ConfigureWorldSpacePanelSettings(panelSettings);
            EditorUtility.SetDirty(panelSettings);
            AssetDatabase.SaveAssetIfDirty(panelSettings);
            return panelSettings;
        }

        static void ConfigureWorldSpacePanelSettings(PanelSettings panelSettings)
        {
            var serialized = new SerializedObject(panelSettings);
            serialized.Update();

            SerializedProperty renderMode = serialized.FindProperty("m_RenderMode");
            if (renderMode != null)
                renderMode.intValue = 1;

            SerializedProperty colliderUpdateMode = serialized.FindProperty("m_ColliderUpdateMode");
            if (colliderUpdateMode != null)
                colliderUpdateMode.intValue = 1;

            SerializedProperty colliderIsTrigger = serialized.FindProperty("m_ColliderIsTrigger");
            if (colliderIsTrigger != null)
                colliderIsTrigger.boolValue = true;

            serialized.ApplyModifiedProperties();
        }

        static void ConfigureUiToolkitWorldSpaceCollider(BoxCollider worldSpaceCollider)
        {
            if (worldSpaceCollider == null)
                return;

            worldSpaceCollider.isTrigger = true;
            worldSpaceCollider.center = Vector3.zero;
            worldSpaceCollider.size = BlockiverseUiToolkitMenuSurface.ReadableWorldSpaceColliderSize;
        }

        static void AssignUiToolkitWorldSpaceCollider(UIDocument document, Collider worldSpaceCollider)
        {
            if (document == null || worldSpaceCollider == null)
                return;

            var serialized = new SerializedObject(document);
            SerializedProperty colliderProperty = serialized.FindProperty("m_WorldSpaceCollider");
            if (colliderProperty != null)
                colliderProperty.objectReferenceValue = worldSpaceCollider;
            serialized.ApplyModifiedProperties();
        }
    }
}
