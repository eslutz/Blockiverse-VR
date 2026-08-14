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
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Layout variants for the FeatureCard. Interactive is a selectable card
    /// with eyebrow/label/description/link; Cta pairs an icon, label, and description
    /// with a call-to-action button.
    /// </summary>
    internal enum FeatureCardVariant
    {
        Interactive,
        Cta
    }

    /// <summary>
    /// A card surfacing a feature in the SDK menu. Interactive variant supports
    /// selection and a "learn more" link; Cta variant exposes a primary action button.
    /// </summary>
    internal class FeatureCard : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        public event Action Clicked;
        public event Action CtaClicked;
        public event Action LinkClicked;

        private readonly string _label;
        private readonly string _description;
        private readonly FeatureCardVariant _variant;
        private readonly string _eyebrowText;
        private readonly TextureContent _eyebrowIcon;
        private readonly TextureContent _icon;
        private readonly string _linkText;
        private readonly string _ctaText;
        private readonly TextureContent _ctaIcon;
        private readonly TextureContent _linkIcon;
        private readonly string _badgeText;
        private readonly BadgeTagType _badgeType;
        private readonly bool _isActive;
        private readonly string _id;

        private bool _selected;
        private VisualElement _root;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                _root?.EnableInClassList(RLDSConstants.FeatureCard.Selected, value);
            }
        }

        public FeatureCard(
            string label,
            string description,
            FeatureCardVariant variant = FeatureCardVariant.Interactive,
            string eyebrowText = null,
            TextureContent eyebrowIcon = null,
            TextureContent icon = null,
            string linkText = "Learn more",
            string ctaText = "Install",
            TextureContent ctaIcon = null,
            TextureContent linkIcon = null,
            string badgeText = null,
            BadgeTagType badgeType = BadgeTagType.Neutral,
            bool isActive = false,
            string id = null)
        {
            _label = label;
            _description = description;
            _variant = variant;
            _eyebrowText = eyebrowText;
            _eyebrowIcon = eyebrowIcon;
            _icon = icon;
            _linkText = linkText;
            _ctaText = ctaText;
            _ctaIcon = ctaIcon;
            _linkIcon = linkIcon;
            _badgeText = badgeText;
            _badgeType = badgeType;
            _isActive = isActive;
            _id = id;
        }

        public void Draw()
        {
            // UIToolkit-only component; IMGUI rendering is not supported.
        }

        public VisualElement Build()
        {
            if (_root != null) return _root;

            _root = new VisualElement();
            _root.AddToClassList(RLDSConstants.FeatureCard.Root);

            if (_variant == FeatureCardVariant.Cta)
            {
                _root.AddToClassList(RLDSConstants.FeatureCard.Cta);
                BuildCtaVariant();
            }
            else
            {
                BuildInteractiveVariant();
                _root.RegisterCallback<ClickEvent>(_ =>
                {
                    RLDSTelemetry.SendInteraction(_root, GetType().Name, _id ?? _label, _label, actionData: "card");
                    Clicked?.Invoke();
                });
            }

            _root.EnableInClassList(RLDSConstants.FeatureCard.Selected, _selected);

            return _root;
        }

        private void BuildInteractiveVariant()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.FeatureCard.Container);

            if (_eyebrowText != null)
            {
                var eyebrow = new VisualElement();
                eyebrow.AddToClassList(RLDSConstants.FeatureCard.Eyebrow);

                if (_eyebrowIcon != null)
                {
                    var eyebrowIconEl = new VisualElement();
                    eyebrowIconEl.AddToClassList(RLDSConstants.FeatureCard.EyebrowIcon);
                    _eyebrowIcon.RegisterToImageLoaded(tex => eyebrowIconEl.style.backgroundImage = tex as UnityEngine.Texture2D);
                    eyebrow.Add(eyebrowIconEl);
                }

                var eyebrowLabel = new UnityEngine.UIElements.Label(_eyebrowText);
                eyebrowLabel.AddToClassList(RLDSConstants.FeatureCard.EyebrowLabel);
                eyebrow.Add(eyebrowLabel);

                container.Add(eyebrow);
            }

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.FeatureCard.Label);
            container.Add(label);

            var desc = new UnityEngine.UIElements.Label(_description);
            desc.AddToClassList(RLDSConstants.FeatureCard.Description);
            container.Add(desc);

            if (_linkText != null)
            {
                var link = new VisualElement();
                link.AddToClassList(RLDSConstants.FeatureCard.Link);
                // Anchor the link to the bottom of the card so links line up across the row.
                link.style.marginTop = StyleKeyword.Auto;
                link.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    RLDSTelemetry.SendInteraction(link, GetType().Name, _id ?? _label, _linkText ?? _label, actionData: "link");
                    LinkClicked?.Invoke();
                });

                var linkLabel = new UnityEngine.UIElements.Label(_linkText);
                linkLabel.AddToClassList(RLDSConstants.FeatureCard.LinkLabel);
                link.Add(linkLabel);

                if (_linkIcon != null)
                {
                    var linkIconEl = new VisualElement();
                    linkIconEl.AddToClassList(RLDSConstants.FeatureCard.LinkIcon);
                    _linkIcon.RegisterToImageLoaded(tex => linkIconEl.style.backgroundImage = tex as UnityEngine.Texture2D);
                    link.Add(linkIconEl);
                }

                container.Add(link);
            }

            _root.Add(container);
        }

        private void BuildCtaVariant()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.FeatureCard.Container);

            if (_icon != null)
            {
                var iconEl = new VisualElement();
                iconEl.AddToClassList(RLDSConstants.FeatureCard.Icon);
                _icon.RegisterToImageLoaded(tex => iconEl.style.backgroundImage = tex as UnityEngine.Texture2D);
                container.Add(iconEl);
            }

            var textGroup = new VisualElement();
            // Grow the title/description block so the status badge is pushed to the bottom of the card.
            textGroup.style.flexGrow = 1;

            var label = new UnityEngine.UIElements.Label(_label);
            label.AddToClassList(RLDSConstants.FeatureCard.Label);
            textGroup.Add(label);

            var desc = new UnityEngine.UIElements.Label(_description);
            desc.AddToClassList(RLDSConstants.FeatureCard.Description);
            desc.style.marginTop = RLDSConstants.Spacing.Size3XS;
            textGroup.Add(desc);

            container.Add(textGroup);

            if (_badgeText != null)
            {
                var badge = new BadgeTag(_badgeText, _badgeType, BadgeTagSize.Small);
                container.Add(badge.Build());
            }

            _root.Add(container);

            var ctaButton = new VisualElement();
            ctaButton.AddToClassList(RLDSConstants.FeatureCard.CtaButton);
            if (_isActive)
            {
                ctaButton.AddToClassList(RLDSConstants.FeatureCard.CtaButtonSecondary);
            }
            ctaButton.RegisterCallback<ClickEvent>(_ =>
            {
                RLDSTelemetry.SendInteraction(ctaButton, GetType().Name, _id ?? _label, _ctaText ?? _label, actionData: "cta");
                CtaClicked?.Invoke();
            });

            if (_ctaIcon != null)
            {
                var ctaIconEl = new VisualElement();
                ctaIconEl.AddToClassList(RLDSConstants.FeatureCard.CtaButtonIcon);
                _ctaIcon.RegisterToImageLoaded(tex => ctaIconEl.style.backgroundImage = tex as UnityEngine.Texture2D);
                ctaButton.Add(ctaIconEl);
            }

            var ctaLabel = new UnityEngine.UIElements.Label(_ctaText);
            ctaLabel.AddToClassList(RLDSConstants.FeatureCard.CtaButtonLabel);
            ctaButton.Add(ctaLabel);

            _root.Add(ctaButton);
        }
    }
}
