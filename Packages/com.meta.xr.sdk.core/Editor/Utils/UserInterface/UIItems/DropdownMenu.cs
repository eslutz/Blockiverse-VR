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
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Represents a single item in a dropdown menu.
    /// </summary>
    internal class DropdownMenuItem
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public Texture2D Icon { get; set; }
        public Action OnClick { get; set; }
        public bool Disabled { get; set; }
        public bool Destructive { get; set; }
        public bool IsDivider { get; set; }
        public bool IsSectionHeader { get; set; }
    }

    /// <summary>
    /// A dropdown menu component following the RLDS design system.
    /// Supports selection mode (with checkmarks) and action mode.
    /// Menu items can have optional left icons, disabled, destructive, divider, and section header states.
    /// </summary>
    internal class DropdownMenu : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly List<DropdownMenuItem> _items;
        private readonly Action<string> _onSelectionChanged;
        private string _selectedId;
        private VisualElement _menuContainer;
        private VisualElement _overlay;
        private VisualElement _root;
        private VisualElement _anchor;
        private bool _matchWidth = true;
        private UnityEngine.UIElements.Button _trigger;
        private UnityEngine.UIElements.Label _triggerLabel;

        /// <param name="items">The menu items to display</param>
        /// <param name="selectedId">The ID of the initially selected item (null for action mode)</param>
        /// <param name="onSelectionChanged">Callback when selection changes. Receives selected item ID. Null for action-only mode.</param>
        public DropdownMenu(List<DropdownMenuItem> items, string selectedId = null, Action<string> onSelectionChanged = null)
        {
            _items = items ?? new List<DropdownMenuItem>();
            _selectedId = selectedId;
            _onSelectionChanged = onSelectionChanged;
        }

        public void Draw()
        {
            // Not implemented - DropdownMenu is UIToolkit only
        }

        public string GetSelectedId() => _selectedId;

        /// <summary>
        /// Attaches the dropdown as a context menu to an external element.
        /// Clicking the element toggles the floating menu. No built-in trigger button is created.
        /// </summary>
        /// <param name="anchor">The element that triggers and anchors the menu (e.g., user's own button)</param>
        /// <param name="matchWidth">If true, menu width matches the anchor width. Defaults to true.</param>
        public void AttachTo(VisualElement anchor, bool matchWidth = true)
        {
            _anchor = anchor;
            _matchWidth = matchWidth;
            anchor.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                if (IsOpen)
                    Close();
                else
                    Show(anchor);
            });
        }

        /// <summary>
        /// Shows the dropdown menu floating below the given anchor element.
        /// </summary>
        public void Show(VisualElement anchor)
        {
            if (IsOpen) return;

            var styledRoot = FindStyledRoot(anchor);
            if (styledRoot == null) return;

            _anchor = anchor;

            _overlay = new VisualElement();
            _overlay.AddToClassList(RLDSConstants.DropdownMenu.Overlay);
            _overlay.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                Close();
            });

            _menuContainer = BuildMenuContent();
            _menuContainer.style.position = Position.Absolute;

            _overlay.Add(_menuContainer);
            styledRoot.Add(_overlay);

            // Position after layout pass so worldBound is valid
            _overlay.schedule.Execute(() =>
            {
                if (_menuContainer == null)
                {
                    return;
                }
                var anchorBound = anchor.worldBound;
                var rootBound = styledRoot.worldBound;
                _menuContainer.style.top = anchorBound.yMax - rootBound.y + RLDSConstants.Spacing.Size3XS;
                _menuContainer.style.left = anchorBound.xMin - rootBound.x;
                if (_matchWidth)
                    _menuContainer.style.width = anchorBound.width;
            });
        }

        /// <summary>
        /// Closes the dropdown menu.
        /// </summary>
        public void Close()
        {
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
            _menuContainer = null;

            UpdateTriggerLabel();
        }

        public bool IsOpen => _overlay != null;

        /// <summary>
        /// Builds the dropdown with a built-in trigger button that opens a floating menu.
        /// For context menu usage, use AttachTo() instead.
        /// </summary>
        public VisualElement Build()
        {
            _root = new VisualElement();
            _root.style.flexDirection = FlexDirection.Column;

            _trigger = new UnityEngine.UIElements.Button(() =>
            {
                if (IsOpen)
                    Close();
                else
                    Show(_trigger);
            });
            _trigger.AddToClassList(RLDSConstants.Button.SecondarySmall);
            _trigger.style.flexDirection = FlexDirection.Row;
            _trigger.style.justifyContent = Justify.SpaceBetween;
            _trigger.style.alignItems = Align.Center;

            _triggerLabel = new UnityEngine.UIElements.Label(GetTriggerLabel());
            _triggerLabel.style.flexGrow = 1;
            _triggerLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _triggerLabel.style.overflow = Overflow.Hidden;
            _trigger.Add(_triggerLabel);

            var arrow = new UnityEngine.UIElements.Image
            {
                style =
                {
                    width = RLDSConstants.IconSize.SizeSM,
                    height = RLDSConstants.IconSize.SizeSM,
                    flexShrink = 0
                }
            };
            Styles.Contents.DownArrowIcon.RegisterToImageLoaded(image =>
            {
                arrow.image = image;
            });
            if (Styles.Contents.DownArrowIcon.Valid)
            {
                arrow.image = Styles.Contents.DownArrowIcon.Image;
            }
            _trigger.Add(arrow);
            _root.Add(_trigger);

            return _root;
        }

        private string GetTriggerLabel()
        {
            if (_selectedId != null)
            {
                foreach (var item in _items)
                {
                    if (item.Id == _selectedId)
                        return item.Label;
                }
            }
            return "Select...";
        }

        private void UpdateTriggerLabel()
        {
            if (_triggerLabel != null)
                _triggerLabel.text = GetTriggerLabel();
        }

        /// <summary>
        /// Walks up the hierarchy to find the highest ancestor that has stylesheets applied.
        /// This ensures the overlay inherits RLDS styles.
        /// </summary>
        private static VisualElement FindStyledRoot(VisualElement element)
        {
            VisualElement styledRoot = null;
            var current = element;
            while (current != null)
            {
                if (current.styleSheets.count > 0)
                    styledRoot = current;
                current = current.parent;
            }
            return styledRoot;
        }

        private VisualElement BuildMenuContent()
        {
            var menu = new VisualElement();
            menu.AddToClassList(RLDSConstants.DropdownMenu.Container);

            var isSelectionMode = _onSelectionChanged != null;

            foreach (var item in _items)
            {
                if (item.IsDivider)
                {
                    var divider = new VisualElement();
                    divider.AddToClassList(RLDSConstants.DropdownMenu.Divider);
                    menu.Add(divider);
                    continue;
                }

                if (item.IsSectionHeader)
                {
                    var header = new UnityEngine.UIElements.Label(item.Label);
                    header.AddToClassList(RLDSConstants.DropdownMenu.SectionHeader);
                    menu.Add(header);
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList(RLDSConstants.DropdownMenu.Item);

                var isSelected = isSelectionMode && item.Id == _selectedId;

                if (isSelected)
                    row.AddToClassList(RLDSConstants.DropdownMenu.ItemSelected);

                if (item.Disabled)
                    row.AddToClassList(RLDSConstants.DropdownMenu.ItemDisabled);

                if (item.Destructive)
                    row.AddToClassList(RLDSConstants.DropdownMenu.ItemDestructive);

                var leftGroup = new VisualElement();
                leftGroup.style.flexDirection = FlexDirection.Row;
                leftGroup.style.alignItems = Align.Center;
                leftGroup.style.flexGrow = 1;

                if (item.Icon != null)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList(RLDSConstants.DropdownMenu.ItemIcon);
                    icon.style.backgroundImage = item.Icon;
                    leftGroup.Add(icon);
                }

                var textGroup = new VisualElement();
                textGroup.style.flexDirection = FlexDirection.Column;
                textGroup.style.flexGrow = 1;

                var label = new UnityEngine.UIElements.Label(item.Label);
                label.AddToClassList(RLDSConstants.DropdownMenu.ItemLabel);
                textGroup.Add(label);

                if (!string.IsNullOrEmpty(item.Description))
                {
                    var desc = new UnityEngine.UIElements.Label(item.Description);
                    desc.AddToClassList(RLDSConstants.DropdownMenu.ItemDescription);
                    textGroup.Add(desc);
                }

                leftGroup.Add(textGroup);

                row.Add(leftGroup);

                if (isSelected)
                {
                    var checkmark = new UnityEngine.UIElements.Label("\u2713");
                    checkmark.AddToClassList(RLDSConstants.DropdownMenu.ItemCheckmark);
                    row.Add(checkmark);
                }

                if (!item.Disabled)
                {
                    var capturedItem = item;
                    row.RegisterCallback<ClickEvent>(evt =>
                    {
                        evt.StopPropagation();
                        capturedItem.OnClick?.Invoke();

                        if (isSelectionMode)
                        {
                            _selectedId = capturedItem.Id;
                            _onSelectionChanged?.Invoke(_selectedId);
                        }

                        Close();
                    });
                }

                menu.Add(row);
            }

            return menu;
        }
    }
}
