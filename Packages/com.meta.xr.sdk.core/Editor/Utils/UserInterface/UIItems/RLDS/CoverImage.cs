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
    /// A hero/banner image card used across NUX, Device Readiness, and SDK Update flows.
    /// Displays a background image with an optional decorative watermark icon and a
    /// composable content area for headings, descriptions, and buttons.
    /// Supports FullBleed mode for edge-to-edge layout on welcome screens.
    /// </summary>
    internal class CoverImage : IUserInterfaceItem
    {
        public bool Hide { get; set; }
        public bool FullBleed { get; set; }
        public Texture2D BackgroundImage { get; set; }
        public TextureContent CoverIcon { get; set; }

        /// <summary>
        /// The content area where callers add child elements (headings, descriptions, buttons).
        /// Available after <see cref="Build"/> is called.
        /// </summary>
        public VisualElement ContentArea { get; private set; }

        private VisualElement _root;

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.CoverImage.Root);

            if (FullBleed)
            {
                _root.AddToClassList(RLDSConstants.CoverImage.RootFullBleed);
            }

            if (BackgroundImage != null)
            {
                _root.style.backgroundImage = new StyleBackground(BackgroundImage);
            }

            if (CoverIcon != null)
            {
                var iconContainer = new VisualElement();
                iconContainer.AddToClassList(RLDSConstants.CoverImage.Icon);

                var iconImage = new UnityEngine.UIElements.Image();
                iconImage.AddToClassList(RLDSConstants.CoverImage.IconImage);
                CoverIcon.RegisterToImageLoaded(tex => iconImage.image = tex);
                iconContainer.Add(iconImage);

                _root.Add(iconContainer);
            }

            ContentArea = new VisualElement();
            ContentArea.AddToClassList(RLDSConstants.CoverImage.Content);
            _root.Add(ContentArea);

            return _root;
        }

        public void Draw()
        {
            if (Hide) return;
            GUILayout.BeginVertical("box");
            GUILayout.Label("Cover Image", EditorStyles.boldLabel);
            GUILayout.EndVertical();
        }
    }
}
