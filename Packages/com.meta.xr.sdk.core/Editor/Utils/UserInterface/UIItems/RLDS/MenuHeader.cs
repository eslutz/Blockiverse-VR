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
    internal readonly struct HeaderAction
    {
        public TextureContent Icon { get; }
        public Action OnClick { get; }
        public string Id { get; }

        public HeaderAction(TextureContent icon, Action onClick, string id = null)
        {
            Icon = icon;
            OnClick = onClick;
            Id = id;
        }
    }

    internal class MenuHeader : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action UpdateAvailableClicked;

        public string Title { get; set; } = "Meta XR SDK";
        public string Version { get; set; }
        public string UpdateAvailableText { get; set; } = "· Update available";
        public IReadOnlyList<HeaderAction> Actions { get; set; }

        private bool _updateAvailable;

        private VisualElement _root;
        private UnityEngine.UIElements.Label _versionLabel;
        private VisualElement _updateLink;

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set
            {
                _updateAvailable = value;
                if (_updateLink != null) _updateLink.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void Draw()
        {
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.MenuHeader.Root);

            var leftContent = new VisualElement();
            leftContent.AddToClassList(RLDSConstants.MenuHeader.LeftContent);
            _root.Add(leftContent);

            var textGroup = new VisualElement();
            textGroup.AddToClassList(RLDSConstants.MenuHeader.TextGroup);
            leftContent.Add(textGroup);

            var titleLabel = new UnityEngine.UIElements.Label(Title ?? "");
            titleLabel.AddToClassList(RLDSConstants.MenuHeader.Title);
            textGroup.Add(titleLabel);

            var metaData = new VisualElement();
            metaData.AddToClassList(RLDSConstants.MenuHeader.MetaData);
            textGroup.Add(metaData);

            _versionLabel = new UnityEngine.UIElements.Label(Version ?? "");
            _versionLabel.AddToClassList(RLDSConstants.MenuHeader.Version);
            if (string.IsNullOrEmpty(Version)) _versionLabel.style.display = DisplayStyle.None;
            metaData.Add(_versionLabel);

            _updateLink = BuildUpdateLink();
            _updateLink.style.display = _updateAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            metaData.Add(_updateLink);

            if (Actions != null && Actions.Count > 0)
            {
                var rightContent = new VisualElement();
                rightContent.AddToClassList(RLDSConstants.MenuHeader.RightContent);
                foreach (var action in Actions)
                {
                    rightContent.Add(BuildIconButton(action));
                }
                _root.Add(rightContent);
            }

            return _root;
        }

        public void UpdateContent(string version, bool updateAvailable)
        {
            Version = version;
            if (_versionLabel != null)
            {
                _versionLabel.text = version ?? "";
                _versionLabel.style.display = string.IsNullOrEmpty(version) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            UpdateAvailable = updateAvailable;
        }

        private VisualElement BuildUpdateLink()
        {
            var link = new UnityEngine.UIElements.Label(UpdateAvailableText ?? "");
            link.AddToClassList(RLDSConstants.MenuHeader.UpdateLink);
            link.RegisterCallback<ClickEvent>(_ =>
            {
                RLDSTelemetry.SendInteraction(link, GetType().Name, "MenuHeaderUpdateAvailable", UpdateAvailableText ?? "");
                UpdateAvailableClicked?.Invoke();
            });
            return link;
        }

        private static VisualElement BuildIconButton(HeaderAction action)
        {
            var button = new VisualElement();
            button.AddToClassList(RLDSConstants.MenuHeader.IconButton);
            button.RegisterCallback<ClickEvent>(_ =>
            {
                RLDSTelemetry.SendInteraction(button, nameof(MenuHeader), action.Id ?? "MenuHeaderAction", action.Id ?? "");
                action.OnClick?.Invoke();
            });

            var iconImage = new VisualElement();
            iconImage.AddToClassList(RLDSConstants.MenuHeader.IconButtonIcon);
            action.Icon.RegisterToImageLoaded(tex => iconImage.style.backgroundImage = tex as Texture2D);
            button.Add(iconImage);

            return button;
        }
    }
}
