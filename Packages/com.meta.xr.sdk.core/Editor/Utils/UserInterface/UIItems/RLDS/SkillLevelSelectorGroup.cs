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
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Groups multiple <see cref="SkillLevelSelector"/> items with single-selection
    /// (radio button) behavior. Selecting one item deselects all others.
    /// </summary>
    internal class SkillLevelSelectorGroup : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly SkillLevelSelector[] _items;
        private readonly Action<string> _onSelectionChanged;
        private readonly Dictionary<string, SkillLevelSelector> _itemMap;
        private readonly float _itemSpacing;
        private readonly bool _hasDuplicateId;
        private string _selectedId;

        public SkillLevelSelectorGroup(
            IEnumerable<SkillLevelSelector> items,
            Action<string> onSelectionChanged = null,
            float itemSpacing = RLDSConstants.Spacing.SizeXS)
        {
            _items = items.ToArray();
            _onSelectionChanged = onSelectionChanged;
            _itemSpacing = itemSpacing;
            _itemMap = new Dictionary<string, SkillLevelSelector>();

            _hasDuplicateId = _items.Select(p => p.Id).Distinct().Count() != _items.Length;

            foreach (var item in _items)
            {
                _itemMap[item.Id] = item;

                if (item.Selected)
                {
                    _selectedId = item.Id;
                }
            }
        }

        public void SetSelection(string id)
        {
            if (_selectedId == id) return;

            if (_selectedId != null && _itemMap.TryGetValue(_selectedId, out var prev))
            {
                prev.SetSelected(false);
            }

            _selectedId = id;

            if (id != null && _itemMap.TryGetValue(id, out var next))
            {
                next.SetSelected(true);
            }

            _onSelectionChanged?.Invoke(id);
        }

        public VisualElement Build()
        {
            if (_hasDuplicateId)
            {
                return new VisualElement();
            }

            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.SkillLevelSelector.Group);

            for (var i = 0; i < _items.Length; i++)
            {
                var item = _items[i];
                var element = item.Build();

                if (i < _items.Length - 1)
                {
                    element.style.marginBottom = _itemSpacing;
                }

                // Wire up selection through the group
                var capturedId = item.Id;
                element.RegisterCallback<ClickEvent>(evt =>
                {
                    RLDSTelemetry.SendInteraction(element, nameof(SkillLevelSelector), capturedId, item.Label, value: capturedId);
                    SetSelection(capturedId);
                });

                container.Add(element);
            }

            return container;
        }

        public void Draw()
        {
            if (Hide) return;
            if (_hasDuplicateId) return;
            foreach (var item in _items)
            {
                item.Draw();
                if (item.Selected && item.Id != _selectedId)
                {
                    SetSelection(item.Id);
                }
            }
        }
    }
}
