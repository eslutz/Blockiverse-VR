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
using UnityEngine;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;
using Button = UnityEngine.UIElements.Button;
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using ActionLinkDescription = Meta.XR.Editor.UserInterface.ActionLinkDescription;
using TextureContent = Meta.XR.Editor.UserInterface.TextureContent;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Helper for loading Hand Readiness Tool resources and constructing shared RLDS-styled UI elements.
    /// </summary>
    internal static class HandReadinessResources
    {
        /// <summary>
        /// Horizontal gutter used on wizard-style screens (ProjectDescription, Scanning)
        /// to center their single-column content. Shared here so both screens stay in
        /// sync if the layout is ever re-tuned.
        /// </summary>
        public const int WizardSideGutter = 240;

        /// <summary>
        /// Primary ("Next") button styled via RLDS Button.Primary + Hand Readiness footer sizing class.
        /// </summary>
        public static Button CreateNextButton(string text, Action onClick)
        {
            var btn = (Button)new RLDSButton(
                new ActionLinkDescription { Content = new GUIContent(text), Action = onClick },
                RLDSConstants.ButtonVariant.Primary, RLDSConstants.ButtonSize.Large).Build();
            btn.AddToClassList(HandReadinessStyles.FooterButton.Next);
            return btn;
        }

        /// <summary>
        /// Secondary ("Back") button styled via RLDS Button.Secondary + Hand Readiness footer sizing class.
        /// </summary>
        public static Button CreateBackButton(string text, Action onClick)
        {
            var btn = (Button)new RLDSButton(
                new ActionLinkDescription { Content = new GUIContent(text), Action = onClick },
                RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Large).Build();
            btn.AddToClassList(HandReadinessStyles.FooterButton.Back);
            return btn;
        }

        /// <summary>
        /// Enable/disable the Next button while preserving RLDS styling (SetEnabled routes through
        /// the .rlds-button-primary:disabled selector).
        /// </summary>
        public static void SetButtonEnabled(Button button, bool enabled)
        {
            button.SetEnabled(enabled);
        }

        /// <summary>
        /// Build a themed icon using `backgroundImage` (not `Image.image`) so USS
        /// `-unity-background-image-tint-color` can apply the RLDS icon tint variables
        /// (e.g. `--rlds-icon-primary`). Callers add a class that sets the tint.
        /// </summary>
        public static VisualElement CreateTintableIcon(string resourceName, int size)
        {
            var icon = new VisualElement();
            var tex = Resources.Load<Texture2D>(resourceName);
            if (tex != null)
            {
                icon.style.backgroundImage = new StyleBackground(tex);
            }
            icon.style.width = size;
            icon.style.height = size;
            icon.style.flexShrink = 0;
            return icon;
        }

        /// <summary>
        /// Cover banner component: a dark image-backed surface hosting a title +
        /// supporting subtitle on the left and a watermark icon on the right.
        /// Text lives inside a fixed-max-width content container so it never
        /// overruns the watermark icon. Every consumer of the cover image should
        /// call this factory; do not rebuild the cover inline.
        /// </summary>
        /// <param name="title">Heading line (RLDS Heading1, on-media primary).</param>
        /// <param name="subtitle">Supporting line (RLDS Body1, on-media secondary).</param>
        /// <param name="iconResourceName">
        /// `Resources/`-relative texture name for the watermark icon (e.g. `icon_hand_tracking`).
        /// When null the icon is omitted.
        /// </param>
        /// <param name="backgroundResourceName">
        /// `Resources/`-relative texture name for the gradient background. When the
        /// texture can't be loaded the cover falls back to the tertiary surface color.
        /// </param>
        public static VisualElement CreateCoverBanner(
            string title,
            string subtitle,
            string iconResourceName,
            string backgroundResourceName = "cover_hands_readiness")
        {
            var cover = new VisualElement();
            cover.AddToClassList(HandReadinessStyles.Cover.Root);

            if (!string.IsNullOrEmpty(backgroundResourceName))
            {
                var coverTexture = Resources.Load<Texture2D>(backgroundResourceName);
                if (coverTexture != null)
                {
                    cover.style.backgroundImage = new StyleBackground(coverTexture);
                }
                else
                {
                    cover.AddToClassList(HandReadinessStyles.Cover.FallbackBg);
                }
            }
            else
            {
                cover.AddToClassList(HandReadinessStyles.Cover.FallbackBg);
            }

            // Watermark icon — absolutely positioned at right via .hrt-cover-headset.
            if (!string.IsNullOrEmpty(iconResourceName))
            {
                var icon = CreateTintableIcon(iconResourceName, 220);
                icon.AddToClassList(HandReadinessStyles.Cover.Headset);
                cover.Add(icon);
            }

            // Text content — wrapped so the max-width constraint applies at the
            // component level. Anything added here is guaranteed to be bounded
            // to the 522px cover-content rect.
            var contentColumn = new VisualElement();
            contentColumn.AddToClassList(HandReadinessStyles.Cover.Content);

            if (!string.IsNullOrEmpty(title))
            {
                var heading = new Label(title);
                heading.AddToClassList(RLDSConstants.Typography.Heading1);
                heading.AddToClassList(HandReadinessStyles.OnMediaText.Primary);
                heading.style.marginBottom = RLDSConstants.Spacing.SizeXS;
                heading.style.whiteSpace = WhiteSpace.Normal;
                contentColumn.Add(heading);
            }

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList(RLDSConstants.Typography.Body1Text);
                subtitleLabel.AddToClassList(HandReadinessStyles.OnMediaText.Secondary);
                subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
                contentColumn.Add(subtitleLabel);
            }

            cover.Add(contentColumn);
            return cover;
        }

        /// <summary>
        /// Standard footer bar with Back + Next buttons, right-aligned.
        /// </summary>
        public static VisualElement CreateFooter(Button backButton, Button nextButton)
        {
            var bottomBar = new VisualElement();
            bottomBar.style.flexDirection = FlexDirection.Row;
            bottomBar.style.justifyContent = Justify.FlexEnd;
            bottomBar.style.flexShrink = 0;
            bottomBar.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
            bottomBar.style.paddingRight = RLDSConstants.Spacing.SizeXL;
            bottomBar.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            bottomBar.style.paddingBottom = RLDSConstants.Spacing.SizeMD;

            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.width = 450;

            buttonContainer.Add(backButton);
            buttonContainer.Add(nextButton);

            bottomBar.Add(buttonContainer);
            return bottomBar;
        }
    }

    /// <summary>
    /// Hand Readiness Tool icon set exposed as RLDS <see cref="TextureContent"/>s
    /// (loaded from the tool's Resources folder) for use with the RLDS Icon and
    /// Button components.
    /// </summary>
    internal static class HandReadinessIcons
    {
        private static readonly TextureContent.Category Category =
            new("Resources", false, "Meta.XR.HandReadinessTool.Editor");

        public static readonly TextureContent Export =
            TextureContent.CreateContent("icon_export.png", Category);
        public static readonly TextureContent CheckReadiness =
            TextureContent.CreateContent("icon_check_readiness.png", Category);
        public static readonly TextureContent Upload =
            TextureContent.CreateContent("icon_upload.png", Category);
        public static readonly TextureContent Copy =
            TextureContent.CreateContent("icon_copy.png", Category);
        public static readonly TextureContent ApplyAutomated =
            TextureContent.CreateContent("icon_apply_automated.png", Category);
    }
}
