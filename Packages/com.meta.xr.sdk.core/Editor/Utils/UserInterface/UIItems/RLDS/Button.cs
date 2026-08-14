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

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal class Button : IUserInterfaceItem
    {
        public bool Hide { get; set; }
        public bool Disable { get; set; }
        public bool Invisible { get; set; }
        public TextureContent LeftIcon { get; set; }
        public TextureContent RightIcon { get; set; }

        private readonly GUIStyle _containerStyle;
        private readonly ButtonStyle _buttonStyle;
        private readonly GUIStyle _textStyle;
        private readonly ActionLinkDescription _action;
        private readonly RLDSConstants.ButtonVariant _variant;
        private readonly RLDSConstants.ButtonSize _size;
        private UnityEngine.UIElements.Button _button;

        public Button(ActionLinkDescription action, ButtonStyle buttonStyle, int fixedWidth = 0)
        {
            _action = action;
            _buttonStyle = buttonStyle;
            var style = _buttonStyle.ToGUIStyle();
            _containerStyle = new GUIStyle
            {
                fixedHeight = _buttonStyle.Height,
                stretchHeight = false,
                stretchWidth = true,
                padding = new RectOffset(0, 0, _buttonStyle.Height / 2, _buttonStyle.Height / 2),
                margin = style.margin
            };
            if (fixedWidth != 0)
            {
                _containerStyle.fixedWidth = fixedWidth;
                _containerStyle.stretchWidth = false;
                _containerStyle.padding.left = fixedWidth / 2;
                _containerStyle.padding.right = fixedWidth / 2;
            }

            _textStyle = _buttonStyle.TextStyle;
            _textStyle.alignment = style.alignment;
            var clearTexture = Color.clear.ToTexture();
            _textStyle.normal = new()
            {
                background = clearTexture,
                textColor = _buttonStyle.TextColorNormal
            };
            _textStyle.hover = new()
            {
                background = clearTexture,
                textColor = _buttonStyle.TextColorNormal
            };
            _textStyle.active.textColor = _buttonStyle.TextColorHover;
            _textStyle.padding = new RectOffset(RLDS.Styles.Spacing.Space3XS, RLDS.Styles.Spacing.Space3XS,
                RLDS.Styles.Spacing.Space3XS, RLDS.Styles.Spacing.Space3XS);
        }

        public Button(ActionLinkDescription action, RLDSConstants.ButtonVariant variant, RLDSConstants.ButtonSize size)
        {
            _action = action;
            _variant = variant;
            _size = size;
        }

        public void Draw()
        {
            EditorGUI.BeginDisabledGroup(Disable);
            var rect = EditorGUILayout.BeginVertical(_containerStyle);
            var hover = HoverHelper.IsHover(GetType() + "RLDS_BUTTON", Event.current, rect);

            if (_buttonStyle.BorderWidth > 0)
            {
                DrawBorder(rect, _buttonStyle.BorderWidth, _buttonStyle.CornerRadius, _buttonStyle.BorderColor);
            }

            var expectedColor = hover ? _buttonStyle.BackgroundColorHover : _buttonStyle.BackgroundColorNormal;
            GUI.DrawTexture(rect, expectedColor.ToTexture(), ScaleMode.ScaleAndCrop, false, 1, GUI.color,
                Vector4.zero, _buttonStyle.CornerRadius);
            if (GUI.Button(rect, _action.Content, _textStyle))
            {
                SendClickTelemetry();
                _action.Action?.Invoke();
            }

            EditorGUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Creates a UIToolkit Button element with RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A UnityEngine.UIElements.Button configured with RLDS styling</returns>
        public VisualElement Build()
        {
            if (_button != null)
            {
                return _button;
            }

            _button = new UnityEngine.UIElements.Button(() =>
            {
                SendClickTelemetry();
                _action.Action?.Invoke();
            });

            var cssClass = GetRLDSStyleClass();
            _button.AddToClassList(cssClass);

            var hasLeftIcon = LeftIcon != null;
            var hasRightIcon = RightIcon != null;

            if (hasLeftIcon || hasRightIcon)
            {
                _button.AddToClassList(RLDSConstants.Button.WithIcon);

                if (hasLeftIcon)
                {
                    AddIconElement(_button, LeftIcon, RLDSConstants.Button.IconLeft);
                }

                if (!string.IsNullOrEmpty(_action.Content.text))
                {
                    var label = new UnityEngine.UIElements.Label(_action.Content.text);
                    _button.Add(label);
                }

                if (hasRightIcon)
                {
                    AddIconElement(_button, RightIcon, RLDSConstants.Button.IconRight);
                }
            }
            else
            {
                _button.text = _action.Content.text;
            }

            if (Disable)
            {
                _button.SetEnabled(false);
            }

            return _button;
        }

        // Emits the generic RLDS click event. The legacy IMGUI path bypassed LinkDescription.Click(),
        // so without this the UIToolkit Button produced no EDITOR_LINK_CLICKED telemetry at all.
        private void SendClickTelemetry()
        {
            RLDSTelemetry.SendInteraction(
                _button,
                GetType().Name,
                _action.Id,
                _action.Label,
                _action.Origin,
                _action.OriginData?.Id,
                actionData: _action.ActionData?.Id);
        }

        private void AddIconElement(VisualElement parent, TextureContent icon, string positionClass)
        {
            var iconElement = new VisualElement();
            iconElement.AddToClassList(positionClass);
            icon.RegisterToImageLoaded(tex => iconElement.style.backgroundImage = tex as Texture2D);
            parent.Add(iconElement);
        }

        /// <summary>
        /// Maps the ButtonStyle to the appropriate RLDS CSS class name.
        /// Determines the button variant (Primary, Secondary, Tertiary, OnMedia) and size (Large, Small, XSmall)
        /// based on the current ButtonStyle properties.
        /// </summary>
        /// <returns>The RLDS CSS class name (e.g., "rlds-button-primary", "rlds-button-secondary-small")</returns>
        private string GetRLDSStyleClass() => RLDSUtils.GetButtonStyleClass(_variant, _size);

        private void DrawBorder(Rect contentRect, int borderWidth, int cornerRadius, Color borderColor)
        {
            var bottomRect = new Rect(
                new Vector2(contentRect.position.x - borderWidth, contentRect.position.y - borderWidth),
                new Vector2(contentRect.width + (2 * borderWidth), contentRect.height + (2 * borderWidth)));
            GUI.DrawTexture(bottomRect, borderColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, borderWidth, cornerRadius);
        }
    }
}
