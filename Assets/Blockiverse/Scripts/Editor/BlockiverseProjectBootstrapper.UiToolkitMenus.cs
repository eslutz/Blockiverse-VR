using System;
using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using Blockiverse.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Blockiverse.Editor
{
    // Production UI Toolkit menu generation (ADR 0010 Phases 2–5).
    //
    // Table-driven from [UiToolkitScreen] attributes: every screen controller declares its
    // screen id, document path, panel size and placement profile, and this partial generates
    // one world-space UIDocument panel per declaration under a single scene root — so adding
    // a screen never edits this file. Boot scene only; the rig prefab is never touched
    // (PR #324's unreviewable 76k-line prefab rewrite is the reason).
    public static partial class BlockiverseProjectBootstrapper
    {
        public const string UiToolkitMenusRootName = "Blockiverse UI Toolkit Menus";
        public const string UiToolkitMenuHostName = "Menu Host";
        public const string UiToolkitScreenStylesFolderPath = UiToolkitStylesFolderPath + "/Screens";

        // Panels are parked here until the placement controller poses them at runtime; the
        // value only matters for the editor scene view.
        static readonly Vector3 UiToolkitMenuPanelRestPosition = new(0f, 1.4f, 1.2f);

        // Targeted generation into the committed Boot scene, without the full Run() (which
        // regenerates the rig prefab and switches the build target). EnsureBootScene also
        // calls EnsureUiToolkitMenus, so a full bootstrap stays equivalent.
        [MenuItem("Blockiverse/UI Toolkit/Generate Menu Screens")]
        public static void BootstrapUiToolkitMenus()
        {
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                BlockiverseProject.BootScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

            EnsureUiToolkitMenus(scene);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);
            AssetDatabase.SaveAssets();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                "UI Toolkit menu screens generated into the Boot scene.");
        }

        public static void EnsureUiToolkitMenus(Scene scene)
        {
            EnsureUiToolkitFolders();
            EnsureFolder(UiToolkitScreenStylesFolderPath);
            PanelSettings panelSettings = EnsureMenuWorldSpacePanelSettings();

            GameObject root = EnsureSingleRootGameObject(scene, UiToolkitMenusRootName);

            if (root == null)
            {
                root = new GameObject(UiToolkitMenusRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            // Scene-wide XRI/UI Toolkit input requirements live under the menus root. The
            // Phase 1 proof root carries its own copies; the two must not coexist in a
            // committed scene (the configuration validator flags duplicated managers), which
            // is one more reason the proof is removed via its menu item before shipping.
            EnsureUiToolkitInfrastructure(root);

            GameObject hostObject = EnsureChild(root.transform, UiToolkitMenuHostName);
            UiToolkitMenuHost host = EnsureComponent<UiToolkitMenuHost>(hostObject);
            host.Configure(FindSceneComponent<BlockiverseMenuController>(scene));
            EditorUtility.SetDirty(host);

            List<UiToolkitScreenDeclaration> declarations = EnumerateUiToolkitScreenDeclarations();

            foreach (UiToolkitScreenDeclaration declaration in declarations)
                EnsureUiToolkitScreenPanel(hostObject.transform, panelSettings, declaration);

            PruneRetiredScreenPanels(hostObject.transform, declarations);
        }

        // A retired [UiToolkitScreen] controller (deleted class, or the attribute removed) leaves
        // its panel GameObject behind forever: EnsureUiToolkitScreenPanel only ensures what IS
        // declared, so a screen that no longer exists in code keeps serializing a missing
        // MonoBehaviour and an orphaned UXML/UIDocument reference into every future regen. Prune
        // any host child whose name doesn't match a current declaration's "<document> Panel".
        static void PruneRetiredScreenPanels(
            Transform hostTransform,
            List<UiToolkitScreenDeclaration> declarations)
        {
            var expectedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (UiToolkitScreenDeclaration declaration in declarations)
            {
                string documentName = Path.GetFileNameWithoutExtension(declaration.Attribute.DocumentAssetPath);
                expectedNames.Add(documentName + " Panel");
            }

            for (int i = hostTransform.childCount - 1; i >= 0; i--)
            {
                Transform child = hostTransform.GetChild(i);

                if (expectedNames.Contains(child.name))
                    continue;

                BlockiverseLog.Info(
                    BlockiverseLogCategory.Bootstrap,
                    $"Pruning retired UI Toolkit screen panel '{child.name}' — no declaration claims it.");
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        public readonly struct UiToolkitScreenDeclaration
        {
            public UiToolkitScreenDeclaration(Type controllerType, UiToolkitScreenAttribute attribute)
            {
                ControllerType = controllerType;
                Attribute = attribute;
            }

            public Type ControllerType { get; }
            public UiToolkitScreenAttribute Attribute { get; }
        }

        public static List<UiToolkitScreenDeclaration> EnumerateUiToolkitScreenDeclarations()
        {
            var declarations = new List<UiToolkitScreenDeclaration>();

            foreach (Type type in TypeCache.GetTypesWithAttribute<UiToolkitScreenAttribute>())
            {
                if (type.IsAbstract || !typeof(UiToolkitScreenController).IsAssignableFrom(type))
                    continue;

                var attribute = (UiToolkitScreenAttribute)Attribute.GetCustomAttribute(
                    type, typeof(UiToolkitScreenAttribute));

                if (attribute != null)
                    declarations.Add(new UiToolkitScreenDeclaration(type, attribute));
            }

            // Deterministic generation order keeps reruns diff-free.
            declarations.Sort(static (a, b) =>
                string.CompareOrdinal(a.Attribute.ScreenId, b.Attribute.ScreenId));
            return declarations;
        }

        static void EnsureUiToolkitScreenPanel(
            Transform hostTransform,
            PanelSettings panelSettings,
            UiToolkitScreenDeclaration declaration)
        {
            UiToolkitScreenAttribute attribute = declaration.Attribute;
            string documentName = Path.GetFileNameWithoutExtension(attribute.DocumentAssetPath);
            GameObject panelObject = EnsureChild(hostTransform, documentName + " Panel");

            // BlockiverseInteractable, never Unity layer 5 — the rays raycast VrUiRaycastLayerMask only.
            panelObject.layer = BlockiverseProject.InteractionLayerIndex;
            panelObject.transform.localPosition = UiToolkitMenuPanelRestPosition;
            panelObject.transform.localRotation = Quaternion.identity;
            panelObject.transform.localScale = Vector3.one * UiToolkitPanelScale;

            UIDocument document = EnsureComponent<UIDocument>(panelObject);
            document.panelSettings = panelSettings;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(attribute.DocumentAssetPath);

            if (tree == null)
            {
                // A null VisualTreeAsset still builds a rootVisualElement: the screen would
                // attach, report healthy, and render a blank rectangle.
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"UI Toolkit document missing at {attribute.DocumentAssetPath} for " +
                    $"{declaration.ControllerType.Name}. The screen will render blank.");
            }

            document.visualTreeAsset = tree;
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = new Vector2(attribute.WidthPixels, attribute.HeightPixels);
            document.pivot = Pivot.Center;

            BoxCollider collider = null;

            if (attribute.NonInteractive)
            {
                // A read-only strip (mining bar, status toast) must never intercept rays: it
                // shares the routed gameplay screen, so its collider would be enabled the
                // whole session, floating in front of the player's face on the interaction
                // layer. No collider at all is the correct shape.
                var stale = panelObject.GetComponent<BoxCollider>();

                if (stale != null)
                    UnityEngine.Object.DestroyImmediate(stale);
            }
            else
            {
                collider = EnsureComponent<BoxCollider>(panelObject);
                collider.size = new Vector3(
                    attribute.WidthPixels / UiToolkitPixelsPerUnit,
                    attribute.HeightPixels / UiToolkitPixelsPerUnit,
                    UiToolkitPanelColliderDepth);
                collider.center = Vector3.zero;
                collider.isTrigger = true;
                // Routed screens start hidden; the controller enables the collider with input.
                collider.enabled = false;
            }

            WorldSpaceUiPlacementController placement =
                EnsureComponent<WorldSpaceUiPlacementController>(panelObject);
            ConfigurePlacementForProfile(placement, attribute.PlacementProfile);

            var controller = (UiToolkitScreenController)EnsureComponent(panelObject, declaration.ControllerType);
            ConfigureScreenControllerReferences(controller, collider, documentName);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(placement);
            EditorUtility.SetDirty(controller);
        }

        static void ConfigurePlacementForProfile(
            WorldSpaceUiPlacementController placement,
            UiToolkitPlacementProfile profile)
        {
            switch (profile)
            {
                case UiToolkitPlacementProfile.Hud:
                    placement.Configure(
                        null,
                        WorldSpaceUiPlacementController.HudDistanceMeters,
                        0f,
                        WorldSpaceUiPlacementController.HudVerticalOffsetMeters,
                        WorldSpaceUiPlacementController.HudPitchDegrees);
                    break;
                default:
                    placement.Configure(
                        null,
                        WorldSpaceUiPlacementController.MenuDistanceMeters,
                        0f,
                        WorldSpaceUiPlacementController.MenuVerticalOffsetMeters,
                        WorldSpaceUiPlacementController.MenuPitchDegrees);
                    break;
            }
        }

        // Stylesheet lists are serialized references assigned here (a UXML <Style src> bakes
        // GUID+fileID that don't exist pre-import). Order is load-bearing: Tokens before Base
        // before the screen's own sheet.
        static void ConfigureScreenControllerReferences(
            UiToolkitScreenController controller,
            Collider panelCollider,
            string documentName)
        {
            var serialized = new SerializedObject(controller);
            SerializedProperty sheets = serialized.FindProperty("styleSheets");

            if (sheets != null)
            {
                var resolved = new List<StyleSheet>(3);

                if (TryLoadStyleSheet(TokensStyleSheetPath, out StyleSheet tokens))
                    resolved.Add(tokens);

                if (TryLoadStyleSheet(BaseStyleSheetPath, out StyleSheet baseSheet))
                    resolved.Add(baseSheet);

                // The per-screen sheet is optional by design.
                string screenSheetPath = UiToolkitScreenStylesFolderPath + "/" + documentName + ".uss";
                var screenSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(screenSheetPath);

                if (screenSheet != null)
                    resolved.Add(screenSheet);

                if (resolved.Count >= 2)
                {
                    sheets.ClearArray();

                    for (int i = 0; i < resolved.Count; i++)
                    {
                        sheets.InsertArrayElementAtIndex(i);
                        sheets.GetArrayElementAtIndex(i).objectReferenceValue = resolved[i];
                    }
                }
                else
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Bootstrap,
                        $"Only {resolved.Count} of 2 required UI Toolkit stylesheets loaded for " +
                        $"{controller.GetType().Name}; leaving the existing serialized list untouched.");
                }
            }

            SerializedProperty colliderProperty = serialized.FindProperty("panelCollider");

            if (colliderProperty != null)
                colliderProperty.objectReferenceValue = panelCollider;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Component EnsureComponent(GameObject target, Type componentType)
        {
            Component component = target.GetComponent(componentType);

            if (component == null)
                component = target.AddComponent(componentType);

            return component;
        }

        static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                T component = rootObject.GetComponentInChildren<T>(true);

                if (component != null)
                    return component;
            }

            return null;
        }
    }
}
