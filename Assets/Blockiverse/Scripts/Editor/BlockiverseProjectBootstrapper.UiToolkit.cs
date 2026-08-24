using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.UI.Toolkit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Editor
{
    // UI Toolkit scene generation (ADR 0010).
    //
    // Two halves with different lifetimes, which is why the file reads as mixed:
    //
    //  * The INFRASTRUCTURE half — folders, the PanelSettings asset, XRUIToolkitManager and
    //    PanelInputConfiguration, the stylesheet loaders and the panel-geometry constants — is
    //    shipped. EnsureUiToolkitMenus (the menus partial) calls into it, and that runs from
    //    Run() via EnsureBootScene.
    //  * The PHASE 1 PROOF half — BootstrapUiToolkitProof / RemoveUiToolkitProof and everything
    //    they build — is scaffolding reachable only from its own menu items. It has never been in
    //    the committed Boot scene. It is kept for now because nine tests in
    //    UiToolkitBootstrapEditModeTests hang off EnsureUiToolkitProofPanel, and one of them
    //    (GeneratedConfigurationSatisfiesTheValidator) is the only place real generated objects
    //    are fed through XrUiToolkitConfigurationValidator. Retiring the proof means re-pointing
    //    those at EnsureUiToolkitMenus first.
    //
    // Constants used by BOTH halves live above the proof section, not inside it.
    public static partial class BlockiverseProjectBootstrapper
    {
        public const string UiToolkitFolderPath = "Assets/Blockiverse/UI";
        public const string UiToolkitDocumentsFolderPath = UiToolkitFolderPath + "/Documents";
        public const string UiToolkitStylesFolderPath = UiToolkitFolderPath + "/Styles";
        public const string UiToolkitSettingsFolderPath = UiToolkitFolderPath + "/Settings";

        public const string MenuPanelSettingsPath =
            UiToolkitSettingsFolderPath + "/MenuWorldSpacePanelSettings.asset";
        public const string RuntimeThemePath =
            UiToolkitStylesFolderPath + "/BlockiverseRuntimeTheme.tss";
        public const string TokensStyleSheetPath = UiToolkitStylesFolderPath + "/Tokens.uss";
        public const string BaseStyleSheetPath = UiToolkitStylesFolderPath + "/Base.uss";
        public const string ProofDocumentPath =
            UiToolkitDocumentsFolderPath + "/UiToolkitProofScreen.uxml";

        public const string UiToolkitRootName = "Blockiverse UI Toolkit";
        public const string UiToolkitManagerName = "XR UI Toolkit Manager";
        public const string PanelInputConfigurationName = "Panel Input Configuration";
        public const string UiToolkitProofPanelName = "UI Toolkit Proof Panel";

        // Physical size is derived, not authored: metres = worldSpaceSize / pixelsPerUnit * scale.
        // 1000 x 700 px at 100 ppu and 0.1 scale is 1.00 m x 0.70 m, inside the 0.85-1.1 m target
        // in the migration spec. Do NOT carry the uGUI Canvas scale constant (0.0013) across; it
        // describes a different unit system and reproducing it here produces a panel of the wrong
        // physical size that looks plausible in the editor.
        public const float UiToolkitPixelsPerUnit = 100f;
        public const float UiToolkitPanelScale = 0.1f;
        public const float UiToolkitPanelWidthPixels = 1000f;
        public const float UiToolkitPanelHeightPixels = 700f;

        // 0.01 local units — one millimetre at the 0.1 panel scale. Every generated menu panel's
        // box collider uses this (EnsureUiToolkitScreenPanel in the menus partial), so it is
        // shipped geometry, not proof scaffolding, and must not move back down into the proof
        // section below. The reasoning is from the proof: the XRI sample ships a zero-depth
        // collider, but its collider backs a poke filter rather than a ray, and a degenerate PhysX
        // box was an unnecessary risk to take on the one thing the proof existed to establish — if
        // zero depth works, a millimetre works too.
        const float UiToolkitPanelColliderDepth = 0.01f;

        // Quest is memory-constrained and this project's UI iconography is small. 2048 is a
        // starting point reasoned from Unity's mobile guidance, not measured — the XRI sample ships
        // 4096. Revisit with the UI Toolkit Debugger's atlas view once real screens exist.
        public const int UiToolkitMaxAtlasSize = 2048;

        // ---- Phase 1 proof scaffolding (see the file header) ----

        // Placement mirrored the routed uGUI menus closely enough to be comparable in headset:
        // 1.2 m forward, centre 0.2 m below a 1.6 m standing eye height.
        static readonly Vector3 UiToolkitProofPanelPosition = new(0f, 1.4f, 1.2f);

        [MenuItem("Blockiverse/UI Toolkit/Bootstrap Phase 1 Proof")]
        public static void BootstrapUiToolkitProof()
        {
            EnsureUiToolkitFolders();
            PanelSettings panelSettings = EnsureMenuWorldSpacePanelSettings();

            Scene scene = EditorSceneManager.OpenScene(
                BlockiverseProject.BootScenePath, OpenSceneMode.Single);

            GameObject root = EnsureUiToolkitRoot(scene);
            EnsureUiToolkitInfrastructure(root);
            EnsureUiToolkitProofPanel(root, panelSettings);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);
            AssetDatabase.SaveAssets();

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                "UI Toolkit Phase 1 proof scaffolding written to the Boot scene. " +
                "This is scaffolding: remove it with Blockiverse/UI Toolkit/Remove Phase 1 Proof " +
                "before shipping.");
        }

        [MenuItem("Blockiverse/UI Toolkit/Remove Phase 1 Proof")]
        public static void RemoveUiToolkitProof()
        {
            Scene scene = EditorSceneManager.OpenScene(
                BlockiverseProject.BootScenePath, OpenSceneMode.Single);

            GameObject root = FindRootGameObject(scene, UiToolkitRootName);

            if (root != null)
                Object.DestroyImmediate(root);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BlockiverseProject.BootScenePath);

            BlockiverseLog.Info(
                BlockiverseLogCategory.Bootstrap,
                "UI Toolkit Phase 1 proof scaffolding removed from the Boot scene.");
        }

        public static void EnsureUiToolkitFolders()
        {
            EnsureFolder(UiToolkitFolderPath);
            EnsureFolder(UiToolkitDocumentsFolderPath);
            EnsureFolder(UiToolkitStylesFolderPath);
            EnsureFolder(UiToolkitSettingsFolderPath);
        }

        // The PanelSettings asset is generated rather than hand-authored for the same reason the URP
        // assets are: several of its fields decide whether the panel works at all, and an asset
        // edited by hand in the inspector drifts silently.
        public static PanelSettings EnsureMenuWorldSpacePanelSettings()
        {
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(MenuPanelSettingsPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, MenuPanelSettingsPath);
            }

            ConfigureMenuWorldSpacePanelSettings(settings);
            EditorUtility.SetDirty(settings);
            return settings;
        }

        // Split out from the asset-writing path so tests can configure an in-memory PanelSettings
        // and assert the result without creating a committed asset as a side effect.
        public static void ConfigureMenuWorldSpacePanelSettings(PanelSettings settings)
        {
            // Public API where one exists.
            settings.renderMode = PanelRenderMode.WorldSpace;
            settings.referenceSpritePixelsPerUnit = UiToolkitPixelsPerUnit;
            settings.scale = 1f;
            // A null theme is not an error Unity raises usefully — the panel renders with unstyled
            // controls and logs a generic "no theme" warning, which on a headset reads as "UI
            // Toolkit looks broken" rather than "an asset did not import".
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RuntimeThemePath);

            if (theme == null)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"UI Toolkit runtime theme missing at {RuntimeThemePath}. Every control will " +
                    "render unstyled. Check the .tss imported before judging the proof panel.");
            }

            settings.themeStyleSheet = theme;

            // scaleMode is inert in World Space and set only so the asset does not read as
            // accidentally unconfigured. Verified in the IL of PanelSettings.ApplyScale
            // (6000.5.8f1): when renderMode == WorldSpace it stores m_ResolvedScale = 1 directly
            // and never calls ResolveScale, so scaleMode, referenceDpi, fallbackDpi,
            // referenceResolution, screenMatchMode and match have no effect on a world-space panel.
            // ConstantPixelSize is chosen because it is the mode whose name describes what actually
            // happens; the XRI sample ships ConstantPhysicalSize, which would be DPI-dependent if
            // it were consulted at all.
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;

            DynamicAtlasSettings atlas = settings.dynamicAtlasSettings;
            atlas.maxAtlasSize = UiToolkitMaxAtlasSize;
            settings.dynamicAtlasSettings = atlas;

            // colliderUpdateMode and colliderIsTrigger have `assembly`-internal setters and
            // ColliderUpdateMode is a private enum, so they are unreachable from user code and must
            // go through SerializedObject. Verified against the IL of UnityEngine.UIElementsModule
            // in 6000.5.8f1; if a future Unity makes them public this block can collapse to two
            // property assignments.
            //
            // Keep = 1 ("Keep existing colliders (if any)"): the bootstrapper sizes the collider to
            // the document itself. MatchDocumentRect would generate a second collider alongside it.
            var serialized = new SerializedObject(settings);
            SetSerializedIntOrWarn(serialized, "m_ColliderUpdateMode", 1);

            // Written for explicitness only. It already defaults to true, and under
            // ColliderUpdateMode.Keep it is never consulted — UIDocument reads it only when it
            // creates a collider itself. The trigger flag that actually matters is the one set on
            // the BoxCollider in EnsureUiToolkitProofPanel.
            SetSerializedBoolOrWarn(serialized, "m_ColliderIsTrigger", true);
            // m_PixelsPerUnit is declared `float32` in PanelSettings, so writing it through
            // SerializedProperty.intValue does not land. Today the constant happens to equal the
            // constructor default of 100, so the panel would be the right physical size by
            // coincidence — and changing the constant would silently move the collider, the
            // document size and both size tests while leaving the panel settings behind.
            SetSerializedFloatOrWarn(serialized, "m_PixelsPerUnit", UiToolkitPixelsPerUnit);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject EnsureUiToolkitRoot(Scene scene)
        {
            GameObject root = EnsureSingleRootGameObject(scene, UiToolkitRootName);

            if (root == null)
            {
                root = new GameObject(UiToolkitRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            return root;
        }

        // The two scene-wide requirements from XRI's world-space UI Toolkit documentation. Both are
        // inert without a UIDocument, which is why they are safe to add beside the existing uGUI
        // menus — but "safe" here is reasoned, and confirming it is part of what the proof is for.
        public static void EnsureUiToolkitInfrastructure(GameObject root)
        {
            GameObject managerObject = EnsureChild(root.transform, UiToolkitManagerName);
            EnsureComponent<XRUIToolkitManager>(managerObject);

            GameObject configurationObject = EnsureChild(root.transform, PanelInputConfigurationName);
            PanelInputConfiguration configuration =
                EnsureComponent<PanelInputConfiguration>(configurationObject);

            // Never ("No input redirection"). XRI states the EventSystem otherwise interferes with
            // UI Toolkit input, and the shipped sample scene serializes this same value.
            configuration.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;
            configuration.processWorldSpaceInput = true;

            EditorUtility.SetDirty(configuration);
        }

        public static UiToolkitProofPanel EnsureUiToolkitProofPanel(
            GameObject root,
            PanelSettings panelSettings)
        {
            GameObject panelObject = EnsureChild(root.transform, UiToolkitProofPanelName);

            // The collider must sit on BlockiverseInteractable. The XRI sample uses Unity layer 5
            // (UI), but this project's ray interactors raycast against
            // BlockiverseProject.VrUiRaycastLayerMask only — a panel on layer 5 renders perfectly
            // and cannot be pointed at.
            panelObject.layer = BlockiverseProject.InteractionLayerIndex;
            panelObject.transform.localPosition = UiToolkitProofPanelPosition;
            panelObject.transform.localRotation = Quaternion.identity;
            panelObject.transform.localScale = Vector3.one * UiToolkitPanelScale;

            UIDocument document = EnsureComponent<UIDocument>(panelObject);
            document.panelSettings = panelSettings;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ProofDocumentPath);

            if (tree == null)
            {
                // Silence here is the worst outcome: UIDocument still builds a rootVisualElement
                // from a null source asset, so the panel attaches, reports "ready" through every
                // counter, and renders a blank rectangle with nothing logged anywhere.
                BlockiverseLog.Error(
                    BlockiverseLogCategory.Bootstrap,
                    $"UI Toolkit proof document missing at {ProofDocumentPath}. The panel will " +
                    "render blank while reporting itself healthy. Check the .uxml imported.");
            }

            document.visualTreeAsset = tree;
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = new Vector2(UiToolkitPanelWidthPixels, UiToolkitPanelHeightPixels);

            // Pivot.Center rather than the sample's TopLeft, so the panel is centred on its
            // transform and the collider needs no compensating offset. Unlike PanelSettings'
            // collider fields, this one has a public setter.
            document.pivot = Pivot.Center;

            // Local units, because the transform scale is applied on top:
            // 1000 px / 100 ppu = 10 local units -> 1.0 m at 0.1 scale.
            var collider = EnsureComponent<BoxCollider>(panelObject);
            collider.size = new Vector3(
                UiToolkitPanelWidthPixels / UiToolkitPixelsPerUnit,
                UiToolkitPanelHeightPixels / UiToolkitPixelsPerUnit,
                UiToolkitPanelColliderDepth);
            collider.center = Vector3.zero;

            // Trigger, so XRI's "collect colliders from children" behaviour on any future parent
            // interactable does not adopt this one as a grab surface.
            collider.isTrigger = true;

            UiToolkitProofPanel proof = EnsureComponent<UiToolkitProofPanel>(panelObject);
            ConfigureProofPanelReferences(proof, collider);

            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(proof);
            return proof;
        }

        // Stylesheets are assigned here rather than through a UXML <Style> element: that element
        // encodes the target asset's GUID and fileID, neither of which exists before Unity imports
        // the sheet, so a hand-authored reference cannot be written ahead of the first import.
        // Tokens must precede Base — Base reads Tokens' custom properties.
        static void ConfigureProofPanelReferences(UiToolkitProofPanel proof, Collider panelCollider)
        {
            var serialized = new SerializedObject(proof);

            SerializedProperty sheets = serialized.FindProperty("styleSheets");

            if (sheets != null)
            {
                // Resolve everything BEFORE clearing. Clearing first and then bailing on a missing
                // asset destroys a previously good list and commits the damage to the scene — and a
                // re-run at a moment when the .uss is mid-import is exactly when that happens.
                var resolved = new List<StyleSheet>(2);

                if (TryLoadStyleSheet(TokensStyleSheetPath, out StyleSheet tokens))
                    resolved.Add(tokens);

                if (TryLoadStyleSheet(BaseStyleSheetPath, out StyleSheet baseSheet))
                    resolved.Add(baseSheet);

                if (resolved.Count == 2)
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
                        $"Only {resolved.Count} of 2 UI Toolkit stylesheets loaded; leaving the " +
                        "existing serialized list untouched rather than replacing it with a short one.");
                }
            }

            SerializedProperty colliderProperty = serialized.FindProperty("panelCollider");

            if (colliderProperty != null)
                colliderProperty.objectReferenceValue = panelCollider;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool TryLoadStyleSheet(string path, out StyleSheet sheet)
        {
            sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);

            if (sheet != null)
                return true;

            BlockiverseLog.Warning(
                BlockiverseLogCategory.Bootstrap,
                $"UI Toolkit stylesheet missing at {path}; the proof panel would render unstyled.");
            return false;
        }

        // The `OrWarn` suffix is not decoration: BlockiverseProjectBootstrapper.cs already declares
        // SetSerializedFloat/Int/Bool with these exact signatures in this same partial class, so
        // these cannot share their names. They differ in behaviour too — the originals no-op
        // silently when FindProperty returns null, and every property written here is one whose
        // absence would leave the panel mis-configured in a way nothing else reports.
        static void SetSerializedFloatOrWarn(SerializedObject serialized, string propertyPath, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);

            if (property == null)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Serialized property '{propertyPath}' not found on {serialized.targetObject.GetType().Name}; " +
                    "the UI Toolkit configuration is incomplete and the configuration test will fail.");
                return;
            }

            property.floatValue = value;
        }

        static void SetSerializedIntOrWarn(SerializedObject serialized, string propertyPath, int value)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);

            if (property == null)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Serialized property '{propertyPath}' not found on {serialized.targetObject.GetType().Name}; " +
                    "the UI Toolkit configuration is incomplete and the configuration test will fail.");
                return;
            }

            property.intValue = value;
        }

        static void SetSerializedBoolOrWarn(SerializedObject serialized, string propertyPath, bool value)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);

            if (property == null)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Bootstrap,
                    $"Serialized property '{propertyPath}' not found on {serialized.targetObject.GetType().Name}; " +
                    "the UI Toolkit configuration is incomplete and the configuration test will fail.");
                return;
            }

            property.boolValue = value;
        }
    }
}
