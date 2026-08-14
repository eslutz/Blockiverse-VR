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
    /// Badge pill type variants following the RLDS design system.
    /// </summary>
    internal enum BadgePillType
    {
        Positive,
        Info,
        Warning,
        Negative,
        Neutral
    }

    /// <summary>
    /// Badge pill size variants following the RLDS design system.
    /// </summary>
    internal enum BadgePillSize
    {
        Normal,
        Small
    }

    /// <summary>
    /// A badge pill component following the RLDS design system.
    /// Used to label and tag UI elements with contextual metadata such as "New", "Experimental", or status indicators.
    /// Supports optional leading icon.
    /// </summary>
    internal class BadgePill : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly string _label;
        private readonly BadgePillType _type;
        private readonly BadgePillSize _size;
        private readonly TextureContent _icon;
        private VisualElement _pill;

        /// <summary>
        /// Constructor for UIToolkit-based Badge Pill.
        /// </summary>
        /// <param name="label">The text label to display (e.g., "New", "Experimental", "Beta")</param>
        /// <param name="type">The type variant (Positive, Info, Warning, Negative, Neutral)</param>
        /// <param name="size">The size variant (Normal, Small)</param>
        /// <param name="icon">Optional leading icon</param>
        public BadgePill(string label, BadgePillType type = BadgePillType.Neutral, BadgePillSize size = BadgePillSize.Normal, TextureContent icon = null)
        {
            _label = label;
            _type = type;
            _size = size;
            _icon = icon;
        }

        /// <summary>
        /// Draw method for IMGUI - not implemented for BadgePill.
        /// </summary>
        public void Draw()
        {
            // Not implemented - BadgePill is UIToolkit only
        }

        /// <summary>
        /// Creates a UIToolkit Badge Pill element with RLDS styling applied.
        /// </summary>
        /// <returns>A VisualElement containing the styled badge pill</returns>
        internal VisualElement Pill => _pill;

        public VisualElement Build()
        {
            if (_pill != null)
            {
                return _pill;
            }

            _pill = new VisualElement();
            var pill = _pill;
            pill.AddToClassList(RLDSConstants.BadgePill.Base);

            // Add type-specific class
            string typeClass = _type switch
            {
                BadgePillType.Positive => RLDSConstants.BadgePill.Positive,
                BadgePillType.Info => RLDSConstants.BadgePill.Info,
                BadgePillType.Warning => RLDSConstants.BadgePill.Warning,
                BadgePillType.Negative => RLDSConstants.BadgePill.Negative,
                _ => RLDSConstants.BadgePill.Neutral
            };
            pill.AddToClassList(typeClass);

            // Add size-specific class
            if (_size == BadgePillSize.Small)
            {
                pill.AddToClassList(RLDSConstants.BadgePill.Small);
            }

            // Add optional leading icon
            if (_icon != null)
            {
                var iconImage = new VisualElement();
                iconImage.AddToClassList(RLDSConstants.BadgePill.Icon);
                _icon.RegisterToImageLoaded(tex => iconImage.style.backgroundImage = tex as UnityEngine.Texture2D);
                pill.Add(iconImage);
            }

            // Add label
            var labelElement = new UnityEngine.UIElements.Label(_label);
            labelElement.AddToClassList(RLDSConstants.BadgePill.Label);
            pill.Add(labelElement);

            return pill;
        }
    }
}
