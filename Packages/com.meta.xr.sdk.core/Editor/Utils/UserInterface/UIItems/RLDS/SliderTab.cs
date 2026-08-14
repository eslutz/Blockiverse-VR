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
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A rectangular tab item used within a <see cref="SliderTabGroup"/> for
    /// toggling between content categories. Supports default, hover, pressed,
    /// and selected visual states via RLDS design tokens.
    /// </summary>
    internal class SliderTab : IUserInterfaceItem
    {
        public string Id { get; }
        public string Label => _label;
        public bool Hide { get; set; }
        public bool Selected { get; private set; }

        private readonly string _label;
        private readonly Action<string> _onSelect;
        private VisualElement _root;

        public SliderTab(string id, string label, Action<string> onSelect = null)
        {
            Id = id;
            _label = label;
            _onSelect = onSelect;
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            _root?.EnableInClassList(RLDSConstants.SliderTab.ItemSelected, selected);
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.SliderTab.Item);

            if (Selected)
            {
                _root.AddToClassList(RLDSConstants.SliderTab.ItemSelected);
            }

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.SliderTab.Label);
            _root.Add(label);

            return _root;
        }

        public void Draw()
        {
            if (Hide) return;
            var wasSelected = Selected;
            var isSelected = GUILayout.Toggle(Selected, _label, EditorStyles.toolbarButton);
            if (isSelected && !wasSelected)
            {
                Selected = true;
                _onSelect?.Invoke(Id);
            }
        }
    }
}
