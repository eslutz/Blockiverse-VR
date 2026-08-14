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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Status variants for the SDK Menu Button, controlling status icon color and shape.
    /// </summary>
    internal enum SdkMenuButtonVariant
    {
        Positive,
        Warning,
        Error,
        Info
    }

    /// <summary>
    /// The persistent SDK Menu toolbar button — the entry point for the Meta XR SDK
    /// dropdown menu. Displays a status indicator, status icon, Meta logo, label, and
    /// dropdown arrow. Variants reflect the overall SDK health/status.
    /// </summary>
    internal class SdkMenuButton : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action Clicked;

        private SdkMenuButtonVariant _variant;
        private VisualElement _root;
        private VisualElement _statusIcon;

        public SdkMenuButton(SdkMenuButtonVariant variant = SdkMenuButtonVariant.Positive)
        {
            _variant = variant;
        }

        public SdkMenuButtonVariant Variant
        {
            get => _variant;
            set
            {
                if (_variant == value) return;
                var oldClass = GetVariantClass(_variant);
                _variant = value;
                var newClass = GetVariantClass(_variant);
                _root?.RemoveFromClassList(oldClass);
                _root?.AddToClassList(newClass);
                if (_statusIcon != null)
                {
                    GetStatusIconContent(_variant).RegisterToImageLoaded(tex => _statusIcon.style.backgroundImage = tex as UnityEngine.Texture2D);
                }
            }
        }

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.SdkMenuButton.Root);
            _root.AddToClassList(GetVariantClass(_variant));

            _root.RegisterCallback<PointerEnterEvent>(_ => _root.AddToClassList(RLDSConstants.SdkMenuButton.Hover));
            _root.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _root.RemoveFromClassList(RLDSConstants.SdkMenuButton.Hover);
                _root.RemoveFromClassList(RLDSConstants.SdkMenuButton.Pressed);
            });
            _root.RegisterCallback<PointerDownEvent>(_ => _root.AddToClassList(RLDSConstants.SdkMenuButton.Pressed));
            _root.RegisterCallback<PointerUpEvent>(_ =>
            {
                _root.RemoveFromClassList(RLDSConstants.SdkMenuButton.Pressed);
                RLDSTelemetry.SendInteraction(_root, GetType().Name, "SdkMenuButton", "Meta XR SDK", Origins.Toolbar);
                Clicked?.Invoke();
            });

            var iconLabelGroup = new VisualElement();
            iconLabelGroup.AddToClassList(RLDSConstants.SdkMenuButton.IconLabelGroup);

            _statusIcon = new VisualElement();
            _statusIcon.AddToClassList(RLDSConstants.SdkMenuButton.StatusIcon);
            GetStatusIconContent(_variant).RegisterToImageLoaded(tex => _statusIcon.style.backgroundImage = tex as UnityEngine.Texture2D);
            iconLabelGroup.Add(_statusIcon);

            var metaIcon = new VisualElement();
            metaIcon.AddToClassList(RLDSConstants.SdkMenuButton.MetaIcon);
            Styles.Contents.MetaWhiteIcon.RegisterToImageLoaded(tex => metaIcon.style.backgroundImage = tex as UnityEngine.Texture2D);
            iconLabelGroup.Add(metaIcon);

            var label = new UnityEngine.UIElements.Label("Meta XR SDK");
            label.AddToClassList(RLDSConstants.SdkMenuButton.Label);
            iconLabelGroup.Add(label);

            _root.Add(iconLabelGroup);

            var dropdownArrow = new VisualElement();
            dropdownArrow.AddToClassList(RLDSConstants.SdkMenuButton.DropdownArrow);
            Styles.Contents.DownArrowIcon.RegisterToImageLoaded(tex => dropdownArrow.style.backgroundImage = tex as UnityEngine.Texture2D);
            _root.Add(dropdownArrow);

            return _root;
        }

        private static TextureContent GetStatusIconContent(SdkMenuButtonVariant variant) => variant switch
        {
            SdkMenuButtonVariant.Positive => Styles.Contents.CheckMaskIcon,
            SdkMenuButtonVariant.Warning => Styles.Contents.WarningMaskIcon,
            SdkMenuButtonVariant.Error => Styles.Contents.ErrorMaskIcon,
            SdkMenuButtonVariant.Info => Styles.Contents.InfoMaskIcon,
            _ => Styles.Contents.CheckMaskIcon
        };

        private static string GetVariantClass(SdkMenuButtonVariant variant) => variant switch
        {
            SdkMenuButtonVariant.Positive => RLDSConstants.SdkMenuButton.VariantPositive,
            SdkMenuButtonVariant.Warning => RLDSConstants.SdkMenuButton.VariantWarning,
            SdkMenuButtonVariant.Error => RLDSConstants.SdkMenuButton.VariantError,
            SdkMenuButtonVariant.Info => RLDSConstants.SdkMenuButton.VariantInfo,
            _ => RLDSConstants.SdkMenuButton.VariantPositive
        };
    }
}
