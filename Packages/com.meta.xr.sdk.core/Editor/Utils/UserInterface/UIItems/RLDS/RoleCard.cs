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
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A selectable card representing a user role (e.g., Developer, Designer)
    /// in the SDK menu's onboarding/role-picker flow. Clicking toggles selection.
    /// </summary>
    internal class RoleCard : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action Clicked;

        private readonly string _label;
        private readonly TextureContent _icon;
        private readonly string _id;

        private bool _selected;
        private VisualElement _root;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                _root?.EnableInClassList(RLDSConstants.RoleCard.Selected, value);
            }
        }

        public RoleCard(string label, TextureContent icon = null, string id = null)
        {
            _label = label;
            _icon = icon;
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
            _root.AddToClassList(RLDSConstants.RoleCard.Root);

            if (_icon != null)
            {
                var iconEl = new VisualElement();
                iconEl.AddToClassList(RLDSConstants.RoleCard.Icon);
                _icon.RegisterToImageLoaded(tex => iconEl.style.backgroundImage = tex as Texture2D);
                _root.Add(iconEl);
            }

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.RoleCard.Label);
            _root.Add(label);

            _root.EnableInClassList(RLDSConstants.RoleCard.Selected, _selected);

            _root.RegisterCallback<ClickEvent>(_ =>
            {
                Selected = !Selected;
                RLDSTelemetry.SendInteraction(_root, GetType().Name, _id ?? _label, _label, value: Selected.ToString());
                Clicked?.Invoke();
            });

            return _root;
        }
    }
}
