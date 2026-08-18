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

using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Semantic variants for the BadgeTag, controlling background, border, and default icon.
    /// </summary>
    internal enum BadgeTagType
    {
        Positive,
        Info,
        Warning,
        Negative,
        Neutral
    }

    /// <summary>
    /// Size variants for the BadgeTag.
    /// </summary>
    internal enum BadgeTagSize
    {
        Normal,
        Small
    }

    /// <summary>
    /// A small status tag with an optional icon and a label, used to surface
    /// semantic state (positive, info, warning, negative, neutral) in menus and lists.
    /// </summary>
    internal class BadgeTag : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly string _label;
        private readonly BadgeTagType _type;
        private readonly BadgeTagSize _size;
        private readonly TextureContent _icon;
        private VisualElement _root;

        public BadgeTag(string label, BadgeTagType type = BadgeTagType.Neutral, BadgeTagSize size = BadgeTagSize.Normal, TextureContent icon = null)
        {
            _label = label;
            _type = type;
            _size = size;
            _icon = icon;
        }

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null)
            {
                return _root;
            }

            _root = new VisualElement();
            var root = _root;
            root.AddToClassList(RLDSConstants.BadgeTag.Base);

            string typeClass = _type switch
            {
                BadgeTagType.Positive => RLDSConstants.BadgeTag.Positive,
                BadgeTagType.Info => RLDSConstants.BadgeTag.Info,
                BadgeTagType.Warning => RLDSConstants.BadgeTag.Warning,
                BadgeTagType.Negative => RLDSConstants.BadgeTag.Negative,
                _ => RLDSConstants.BadgeTag.Neutral
            };
            root.AddToClassList(typeClass);

            if (_size == BadgeTagSize.Small)
            {
                root.AddToClassList(RLDSConstants.BadgeTag.Small);
            }

            var iconContent = _icon ?? GetDefaultIcon(_type);
            if (iconContent != null)
            {
                var iconImage = new VisualElement();
                iconImage.AddToClassList(RLDSConstants.BadgeTag.Icon);
                iconContent.RegisterToImageLoaded(tex => iconImage.style.backgroundImage = tex as UnityEngine.Texture2D);
                root.Add(iconImage);
            }

            var labelElement = new UnityEngine.UIElements.Label(_label);
            labelElement.AddToClassList(RLDSConstants.BadgeTag.Label);
            root.Add(labelElement);

            return root;
        }

        private static TextureContent GetDefaultIcon(BadgeTagType type) => type switch
        {
            _ => null
        };
    }
}

