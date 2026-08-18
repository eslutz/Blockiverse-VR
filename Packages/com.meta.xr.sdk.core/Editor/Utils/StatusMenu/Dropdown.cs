/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#if UNITY_6000_3_OR_NEWER
#define USE_MAINTOOLBAR
#endif

using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Reflection;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using SdkMenuButton = Meta.XR.Editor.UserInterface.SdkMenuButton;
using SdkMenuButtonVariant = Meta.XR.Editor.UserInterface.SdkMenuButtonVariant;
using UIStyles = Meta.XR.Editor.UserInterface.Styles;
using Utils = Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Editor.StatusMenu
{
    [InitializeOnLoad]
    [Reflection]
    internal static class Dropdown
    {
        private const string ElementClass = "unity-editor-toolbar-element";
        private const string Title = "Meta XR Tools";

        private static readonly CustomBool StatusIconEnabled =
            new UserBool()
            {
                Owner = null,
                Uid = "StatusIcon.Enabled",
                Default = true,
                Label = "Shows Meta XR Tools menu",
                Tooltip = "Requires domain reload to refresh",
            };

        private static EditorToolbarButton _editorToolbarButton;
        private static SdkMenuButton _sdkMenuButton;
        private static SdkMenuButtonVariant _currentVariant = SdkMenuButtonVariant.Positive;
        private static Vector2 _rawPosition = Vector2.zero;

        static Dropdown()
        {
            if (!Utils.ShouldRenderEditorUI()) return;

            if (StatusIconEnabled.Value)
            {
                EditorApplication.update += Initialize;
            }
        }

#if USE_MAINTOOLBAR
        private static MainToolbarButton _mainToolbarButton;

        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbars.MainToolbar", Name = "SetDisplayedAll")]
        private static readonly StaticMethodInfoHandleWithWrapperAction<string, bool> SetDisplayedAll = new();

        private const string MainToolbarPath = "MetaXR/StatusMenu";

        /// <summary>
        /// Creates the toolbar button for Unity 6+ using the MainToolbarElement API.
        /// Called automatically by Unity's toolbar system.
        /// </summary>
        /// <returns>The created MainToolbarElement, or null if the status icon is disabled.</returns>
        [MainToolbarElement(MainToolbarPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        public static MainToolbarElement CreateStatusMenuButton()
        {
            if (!StatusIconEnabled.Value) return null;

            var icon = Styles.Contents.MetaIcon.GUIContent.image as Texture2D;
            var content = new MainToolbarContent(icon) { text = Title };

            _mainToolbarButton = new MainToolbarButton(content, ShowDropdown);

            return _mainToolbarButton;
        }

        private static List<VisualElement> FindAllEditorPanelRoots()
        {
            var panelRoots = new List<VisualElement>();

            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            foreach (var window in allWindows)
            {
                if (window == null || window.rootVisualElement == null) continue;

                var element = window.rootVisualElement;
                while (element.parent != null)
                {
                    element = element.parent;
                }

                if (element.GetType().Name == "EditorPanelRootElement")
                {
                    if (!panelRoots.Contains(element))
                    {
                        panelRoots.Add(element);
                    }
                }
            }

            return panelRoots;
        }
#else
        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar")]
        private static readonly TypeHandle ToolbarType = new();

        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar", Name = "m_Root")]
        private static readonly FieldInfoHandle<VisualElement> Root = new();

        private const string PlayModeGroupId = "ToolbarZoneLeftAlign";

        private static UnityEngine.Object _toolbar;

        private static VisualElement FetchParent()
        {
            if (_toolbar == null)
            {
                var toolbars = Resources.FindObjectsOfTypeAll(ToolbarType.Target);
                _toolbar = toolbars.FirstOrDefault();
            }

            if (_toolbar != null)
            {
                var root = Root.Get(_toolbar);
                return root?.Q(PlayModeGroupId);
            }

            return null;
        }

        private static void Attach(EditorToolbarButton button)
        {
            if (button == null) return;

            var parent = FetchParent();

            if (button.parent == parent) return;

            parent?.Add(_editorToolbarButton);
        }
#endif

        private static void CustomizeButton(EditorToolbarButton button)
        {
            button.AddToClassList(ElementClass);
            button.RegisterCallback<GeometryChangedEvent>(evt => RefreshRawPosition());

            button.text = "";
            button.icon = null;

            // Remove the Button's Clickable manipulator so pointer events propagate
            // to SdkMenuButton's root element instead of being captured by the parent.
            if (button.clickable != null)
            {
                button.RemoveManipulator(button.clickable);
            }

            // Strip EditorToolbarButton's visual chrome so SdkMenuButton
            // renders directly without a wrapper appearance.
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = RLDSConstants.BorderWidth.None;
            button.style.borderBottomWidth = RLDSConstants.BorderWidth.None;
            button.style.borderLeftWidth = RLDSConstants.BorderWidth.None;
            button.style.borderRightWidth = RLDSConstants.BorderWidth.None;
            button.style.paddingTop = RLDSConstants.Spacing.None;
            button.style.paddingBottom = RLDSConstants.Spacing.None;
            button.style.paddingLeft = RLDSConstants.Spacing.None;
            button.style.paddingRight = RLDSConstants.Spacing.None;

            var styleSheet = RLDSUtils.LoadStyleSheet(!EditorGUIUtility.isProSkin);
            if (styleSheet != null)
            {
                button.styleSheets.Add(styleSheet);
            }

            _sdkMenuButton = new SdkMenuButton(ComputeCurrentVariant());
            _sdkMenuButton.Clicked += () =>
            {
                RefreshRawPosition();
                ShowDropdown();
            };
            var sdkElement = _sdkMenuButton.Build();
            button.Add(sdkElement);
        }

        private static void Initialize()
        {
#if USE_MAINTOOLBAR
            if (StatusIconEnabled.Value)
            {
                SetDisplayedAll.Invoke?.Invoke(MainToolbarPath, StatusIconEnabled.Value);
            }

            _editorToolbarButton = FindEditorToolbarButton();
#else
            _editorToolbarButton = new EditorToolbarButton()
            {
                text = Title,
            };

            Attach(_editorToolbarButton);
#endif
            EditorApplication.update -= Initialize;
            if (_editorToolbarButton == null) return;

            CustomizeButton(_editorToolbarButton);
            EditorApplication.update += Update;
        }

        private static void Update()
        {
#if USE_MAINTOOLBAR
            if (!IsButtonReferenceValid())
            {
                ReinitializeButton();
            }
#endif
            UpdateVariant();
        }

        private static void UpdateVariant()
        {
            if (_sdkMenuButton == null) return;

            var variant = ComputeCurrentVariant();
            if (variant == _currentVariant) return;
            _currentVariant = variant;
            _sdkMenuButton.Variant = variant;
        }

        private static SdkMenuButtonVariant ComputeCurrentVariant()
        {
            var item = StatusMenu.GetHighestItem();
            if (item?.PillIcon == null)
            {
                return SdkMenuButtonVariant.Positive;
            }

            var (_, color, showNotification) = item.PillIcon();
            return ComputeVariantForColor(color, showNotification);
        }

        // The toolbar button has only three states (Figma node 2567-63486): Positive (all good),
        // Warning, and Error. Anything that is not a warning or error status — including the
        // Optional/Info color — maps to Positive; the button has no Info variant.
        internal static SdkMenuButtonVariant ComputeVariantForColor(Color? color, bool showNotification)
        {
            if (!showNotification || color == null)
            {
                return SdkMenuButtonVariant.Positive;
            }

            var c = color.Value;
            var errorColor = UIStyles.Colors.ErrorColor;
            if (Mathf.Abs(c.r - errorColor.r) < 0.1f && Mathf.Abs(c.g - errorColor.g) < 0.1f)
                return SdkMenuButtonVariant.Error;

            var warningColor = UIStyles.Colors.WarningColor;
            if (Mathf.Abs(c.r - warningColor.r) < 0.1f && Mathf.Abs(c.g - warningColor.g) < 0.1f)
                return SdkMenuButtonVariant.Warning;

            return SdkMenuButtonVariant.Positive;
        }

        /// <summary>
        /// Renders the dropdown settings in the user settings GUI.
        /// </summary>
        public static void OnSettingsGUI()
        {
            StatusIconEnabled.DrawForGUI(Origins.UserSettings, null, UnityEditor.EditorUtility.RequestScriptReload);
        }

        private static void RefreshRawPosition()
        {
            _rawPosition = GUIUtility.GUIToScreenPoint(Vector2.zero);
        }

        private static Rect ComputeCurrentRect()
        {
            if (_editorToolbarButton == null) return Rect.zero;

            var layoutSize = _editorToolbarButton.layout.size;
            if (float.IsNaN(layoutSize.x) || float.IsNaN(layoutSize.y))
            {
                return Rect.zero;
            }

            var position = _rawPosition;

            var parent = _editorToolbarButton as VisualElement;
            while (parent != null)
            {
                position += parent.layout.position;
                parent = parent.parent;
            }

            return new Rect(position, layoutSize);
        }

        internal static void ShowDropdown()
        {
            StatusMenu.ShowDropdown(ComputeCurrentRect());
        }

#if USE_MAINTOOLBAR
        private static bool IsButtonReferenceValid()
        {
            if (_editorToolbarButton == null) return false;

            var element = _editorToolbarButton as VisualElement;
            if (element == null) return false;

            var current = element;
            while (current.parent != null)
            {
                current = current.parent;
            }

            return current.GetType().Name == "EditorPanelRootElement";
        }

        private static EditorToolbarButton FindEditorToolbarButton()
        {
            var panels = FindAllEditorPanelRoots();
            return panels
                .Select(panel => panel.Query<EditorToolbarButton>()
                    .ToList()
                    .FirstOrDefault(b => b.text == Title))
                .FirstOrDefault(button => button != null);
        }

        private static void ReinitializeButton()
        {
            var newButton = FindEditorToolbarButton();

            if (newButton != null && newButton != _editorToolbarButton)
            {
                _editorToolbarButton = newButton;
                _sdkMenuButton = null;
                CustomizeButton(_editorToolbarButton);
                _currentVariant = SdkMenuButtonVariant.Positive;
            }
        }
#endif
    }
}
