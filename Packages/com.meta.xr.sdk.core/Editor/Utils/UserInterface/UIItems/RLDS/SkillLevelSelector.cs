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
    /// A card-style selection component with radio-button behavior.
    /// Each card displays a label and description, with visual states for
    /// default, hover, pressed, and selected.
    /// </summary>
    internal class SkillLevelSelector : IUserInterfaceItem
    {
        public string Id { get; }
        public string Label => _label;
        public bool Hide { get; set; }
        public bool Selected { get; private set; }

        private readonly string _label;
        private readonly string _description;
        private readonly Action<string> _onSelect;
        private VisualElement _root;

        public SkillLevelSelector(string id, string label, string description, Action<string> onSelect = null)
        {
            Id = id;
            _label = label;
            _description = description;
            _onSelect = onSelect;
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            if (_root != null)
            {
                _root.EnableInClassList(RLDSConstants.SkillLevelSelector.ItemSelected, selected);
            }
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.SkillLevelSelector.Item);

            if (Selected)
            {
                _root.AddToClassList(RLDSConstants.SkillLevelSelector.ItemSelected);
            }

            // Label + description container
            var labelContainer = new VisualElement();
            labelContainer.AddToClassList(RLDSConstants.SkillLevelSelector.LabelContainer);

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.SkillLevelSelector.Label);
            labelContainer.Add(label);

            var description = new UnityEngine.UIElements.Label(_description);
            description.AddToClassList(RLDSConstants.SkillLevelSelector.Description);
            labelContainer.Add(description);

            _root.Add(labelContainer);

            return _root;
        }

        public void Draw()
        {
            // IMGUI fallback — minimal implementation
            if (Hide) return;
            GUILayout.BeginHorizontal();
            var wasSelected = Selected;
            var isSelected = GUILayout.Toggle(Selected, "", GUILayout.Width(20));
            GUILayout.BeginVertical();
            GUILayout.Label(_label, EditorStyles.boldLabel);
            GUILayout.Label(_description, EditorStyles.miniLabel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (isSelected && !wasSelected)
            {
                Selected = true;
                _onSelect?.Invoke(Id);
            }
        }
    }
}
