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
    /// Groups multiple <see cref="SliderTab"/> items in a horizontal rectangular
    /// container with single-selection (radio) behavior. Selecting one tab
    /// deselects all others.
    /// </summary>
    internal class SliderTabGroup : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly SliderTab[] _items;
        private readonly Action<string> _onSelectionChanged;
        private readonly Dictionary<string, SliderTab> _itemMap;
        private readonly bool _hasDuplicateId;
        private string _selectedId;

        public SliderTabGroup(
            IEnumerable<SliderTab> items,
            Action<string> onSelectionChanged = null)
        {
            _items = items.ToArray();
            _onSelectionChanged = onSelectionChanged;
            _itemMap = new Dictionary<string, SliderTab>();

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
            container.AddToClassList(RLDSConstants.SliderTab.Group);

            foreach (var item in _items)
            {
                var element = item.Build();

                var capturedId = item.Id;
                element.RegisterCallback<ClickEvent>(evt =>
                {
                    RLDSTelemetry.SendInteraction(element, nameof(SliderTab), capturedId, item.Label, value: capturedId);
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
