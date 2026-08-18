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
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A promotional banner for the SDK menu, displaying a version number, feature
    /// description, optional background image, and a call-to-action button.
    /// </summary>
    internal class MenuBanner : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action CtaClicked;

        public string VersionNumber { get; set; }
        public string FeatureDescription { get; set; }
        public string CtaText { get; set; } = "Review";
        public Texture2D BackgroundImage { get; set; }

        private VisualElement _root;
        private UnityEngine.UIElements.Label _labelElement;
        private UnityEngine.UIElements.Label _descriptionElement;
        private UnityEngine.UIElements.Label _ctaLabelElement;

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.MenuBanner.Root);

            if (BackgroundImage != null)
            {
                _root.style.backgroundImage = new StyleBackground(BackgroundImage);
            }

            var leftContent = new VisualElement();
            leftContent.AddToClassList(RLDSConstants.MenuBanner.LeftContent);

            var labelDescGroup = new VisualElement();
            labelDescGroup.AddToClassList(RLDSConstants.MenuBanner.LabelDescriptionGroup);

            _labelElement = new UnityEngine.UIElements.Label(VersionNumber ?? "");
            _labelElement.AddToClassList(RLDSConstants.MenuBanner.Label);
            labelDescGroup.Add(_labelElement);

            _descriptionElement = new UnityEngine.UIElements.Label(FeatureDescription ?? "");
            _descriptionElement.AddToClassList(RLDSConstants.MenuBanner.Description);
            labelDescGroup.Add(_descriptionElement);

            leftContent.Add(labelDescGroup);
            _root.Add(leftContent);

            var rightContent = new VisualElement();
            rightContent.AddToClassList(RLDSConstants.MenuBanner.RightContent);

            var ctaButton = new VisualElement();
            ctaButton.AddToClassList(RLDSConstants.MenuBanner.CtaButton);
            ctaButton.RegisterCallback<ClickEvent>(_ =>
            {
                RLDSTelemetry.SendInteraction(ctaButton, GetType().Name, "MenuBannerCta", CtaText ?? "");
                CtaClicked?.Invoke();
            });

            _ctaLabelElement = new UnityEngine.UIElements.Label(CtaText ?? "");
            _ctaLabelElement.AddToClassList(RLDSConstants.MenuBanner.CtaLabel);
            ctaButton.Add(_ctaLabelElement);

            rightContent.Add(ctaButton);
            _root.Add(rightContent);

            return _root;
        }

        public void UpdateContent(string versionNumber, string featureDescription, string ctaText = null)
        {
            VersionNumber = versionNumber;
            FeatureDescription = featureDescription;
            if (ctaText != null) CtaText = ctaText;
            if (_labelElement != null) _labelElement.text = versionNumber ?? "";
            if (_descriptionElement != null) _descriptionElement.text = featureDescription ?? "";
            if (ctaText != null && _ctaLabelElement != null) _ctaLabelElement.text = ctaText;
        }
    }
}
