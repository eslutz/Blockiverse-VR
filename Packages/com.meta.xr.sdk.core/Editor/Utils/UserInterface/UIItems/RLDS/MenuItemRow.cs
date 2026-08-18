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

using System;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Trailing action variants for a MenuItemRow. None renders no trailing UI;
    /// LabelAndIcon renders an action label with chevron; Toggle renders a Toggle control.
    /// </summary>
    internal enum MenuItemActionType
    {
        None,
        LabelAndIcon,
        Toggle
    }

    /// <summary>
    /// A single row in the SDK menu, with optional icon, title, subtitle, and a trailing
    /// action area (label + chevron, or toggle, or arbitrary trailing content).
    /// </summary>
    internal class MenuItemRow : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action Clicked;
        public event Action<bool> Toggled;

        private readonly string _title;
        private readonly TextureContent _icon;
        private readonly string _subtitle;
        private readonly VisualElement _statusContent;
        private readonly MenuItemActionType _actionType;
        private readonly string _actionLabel;
        private readonly TextureContent _actionIcon;
        private readonly VisualElement _trailingContent;
        private readonly bool _disabled;
        private readonly string _enablementText;
        private readonly VisualElement _enablementContent;
        private readonly string _id;

        private bool _selected;
        private bool _toggleValue;
        private VisualElement _root;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                _root?.EnableInClassList(RLDSConstants.MenuItem.Selected, value);
            }
        }

        public bool ToggleValue
        {
            get => _toggleValue;
            set
            {
                _toggleValue = value;
                UpdateToggleVisual();
            }
        }

        public MenuItemRow(
            string title,
            TextureContent icon = null,
            string subtitle = null,
            VisualElement statusContent = null,
            MenuItemActionType actionType = MenuItemActionType.None,
            string actionLabel = null,
            TextureContent actionIcon = null,
            VisualElement trailingContent = null,
            bool disabled = false,
            string enablementText = null,
            VisualElement enablementContent = null,
            string id = null)
        {
            _title = title;
            _icon = icon;
            _subtitle = subtitle;
            _statusContent = statusContent;
            _actionType = actionType;
            _actionLabel = actionLabel;
            _actionIcon = actionIcon;
            _trailingContent = trailingContent;
            _disabled = disabled;
            _enablementText = enablementText;
            _enablementContent = enablementContent;
            _id = id;
        }

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.MenuItem.Root);

            if (_subtitle != null)
            {
                _root.AddToClassList(RLDSConstants.MenuItem.WithSubtitle);
            }

            if (_icon != null)
            {
                var iconImage = new VisualElement();
                iconImage.AddToClassList(RLDSConstants.MenuItem.Icon);
                _icon.RegisterToImageLoaded(tex => iconImage.style.backgroundImage = tex as UnityEngine.Texture2D);
                _root.Add(iconImage);
            }

            BuildTextGroup();
            BuildTrailingContent();

            _root.EnableInClassList(RLDSConstants.MenuItem.Selected, _selected);
            _root.EnableInClassList(RLDSConstants.MenuItem.Disabled, _disabled);

            _root.RegisterCallback<ClickEvent>(evt =>
            {
                if (_actionType == MenuItemActionType.Toggle && _toggleElement != null)
                {
                    if (evt.target is VisualElement target && _toggleElement.Contains(target))
                    {
                        return;
                    }
                    _toggleElement.value = !_toggleElement.value;
                    return;
                }
                RLDSTelemetry.SendInteraction(_root, GetType().Name, _id ?? _title, _title);
                Clicked?.Invoke();
            });

            return _root;
        }

        private void BuildTextGroup()
        {
            if (_subtitle != null || _statusContent != null || _enablementText != null || _enablementContent != null)
            {
                var textGroup = new VisualElement();
                textGroup.AddToClassList(RLDSConstants.MenuItem.TextGroup);

                var titleLabel = new UnityEngine.UIElements.Label(_title);
                titleLabel.AddToClassList(RLDSConstants.MenuItem.Title);
                textGroup.Add(titleLabel);

                if (_subtitle != null)
                {
                    var subtitleLabel = new UnityEngine.UIElements.Label(_subtitle);
                    subtitleLabel.AddToClassList(RLDSConstants.MenuItem.Subtitle);
                    textGroup.Add(subtitleLabel);
                }

                if (_statusContent != null)
                {
                    textGroup.Add(_statusContent);
                }

                if (_enablementContent != null)
                {
                    textGroup.Add(_enablementContent);
                }
                else if (_enablementText != null)
                {
                    var enablementLabel = new UnityEngine.UIElements.Label(_enablementText);
                    enablementLabel.AddToClassList(RLDSConstants.MenuItem.EnablementText);
                    textGroup.Add(enablementLabel);
                }

                _root.Add(textGroup);
            }
            else
            {
                var titleLabel = new UnityEngine.UIElements.Label(_title);
                titleLabel.AddToClassList(RLDSConstants.MenuItem.Title);
                _root.Add(titleLabel);
            }
        }

        private void BuildTrailingContent()
        {
            if (_trailingContent != null)
            {
                var spacer = new VisualElement();
                spacer.AddToClassList(RLDSConstants.MenuItem.Spacer);
                _root.Add(spacer);
                _root.Add(_trailingContent);
                return;
            }

            switch (_actionType)
            {
                case MenuItemActionType.LabelAndIcon:
                    BuildLabelAndIconAction();
                    break;
                case MenuItemActionType.Toggle:
                    BuildToggleAction();
                    break;
            }
        }

        private void BuildLabelAndIconAction()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(RLDSConstants.MenuItem.Spacer);
            _root.Add(spacer);

            var actionArea = new VisualElement();
            actionArea.AddToClassList(RLDSConstants.MenuItem.ActionArea);

            if (_actionLabel != null)
            {
                var label = new UnityEngine.UIElements.Label(_actionLabel);
                label.AddToClassList(RLDSConstants.MenuItem.ActionLabel);
                actionArea.Add(label);
            }

            if (_actionIcon != null)
            {
                var chevron = new VisualElement();
                chevron.AddToClassList(RLDSConstants.MenuItem.ActionIcon);
                _actionIcon.RegisterToImageLoaded(tex => chevron.style.backgroundImage = tex as UnityEngine.Texture2D);
                actionArea.Add(chevron);
            }

            _root.Add(actionArea);
        }

        private UnityEngine.UIElements.Toggle _toggleElement;

        private void BuildToggleAction()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(RLDSConstants.MenuItem.Spacer);
            _root.Add(spacer);

            _toggleElement = new UnityEngine.UIElements.Toggle();
            _toggleElement.AddToClassList(RLDSConstants.Toggle.Base);
            _toggleElement.value = _toggleValue;
            _toggleElement.RegisterValueChangedCallback(evt =>
            {
                _toggleValue = evt.newValue;
                RLDSTelemetry.SendInteraction(_toggleElement, GetType().Name, _id ?? _title, _title, value: _toggleValue.ToString());
                Toggled?.Invoke(_toggleValue);
            });
            _root.Add(_toggleElement);
        }

        private void UpdateToggleVisual()
        {
            _toggleElement?.SetValueWithoutNotify(_toggleValue);
        }
    }
}
