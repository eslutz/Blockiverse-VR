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

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Final-step confirmation screen. Reached from Results once every
    /// recommendation is resolved and the user clicks "Confirm readiness".
    /// Replaces the report with a hero confirmation block (green check, title,
    /// completion badge), a "What happens next" bullet list, and a Close /
    /// Explore Building Blocks footer.
    /// </summary>
    public static class ConfirmationScreen
    {
        /// <summary>
        /// Fixed hero-card content width. Keeps the card centered inside the outer
        /// 64px-gutter content area rather than stretching to fill it.
        /// </summary>
        private const int HeroContentWidth = 896;
        private const int HeroHeight = 240;

        /// <summary>Creates the confirmation screen UI.</summary>
        /// <param name="totalResolvedCount">Total number of recommendations resolved (rendered as "All {N} recommendations have been resolved").</param>
        /// <param name="onClose">Callback invoked when the Close button is clicked.</param>
        /// <param name="onExploreBuildingBlocks">Callback invoked when the Explore Building Blocks button is clicked.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete confirmation screen.</returns>
        public static VisualElement Create(
            int totalResolvedCount,
            Action onClose,
            Action onExploreBuildingBlocks)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            // 64px gutter on the Container that wraps the hero card + What happens
            // next section. WizardSideGutter (240px) is reserved for narrow
            // single-column wizard screens (Welcome, Scanning).
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.paddingLeft = RLDSConstants.Spacing.Size5XL;
            content.style.paddingRight = RLDSConstants.Spacing.Size5XL;
            content.style.paddingTop = RLDSConstants.Spacing.Size4XL;

            content.Add(BuildHeroPanel(totalResolvedCount));
            content.Add(BuildWhatHappensNext());

            container.Add(content);

            var closeButton = HandReadinessResources.CreateBackButton("Close", onClose);
            var exploreButton = HandReadinessResources.CreateNextButton(
                "Explore Building Blocks", onExploreBuildingBlocks);
            container.Add(HandReadinessResources.CreateFooter(closeButton, exploreButton));

            return container;
        }

        private static VisualElement BuildHeroPanel(int totalResolvedCount)
        {
            // Hero card sits centered inside the outer content area at a fixed
            // width. hrt-card provides the surface-secondary-background (lighter
            // than the surrounding window) + RLDS-sm border-radius. Cover.Root
            // is not suitable here — it forces a fixed 261px height and bottom-
            // aligned content, which is wrong for a centered hero.
            var panel = new VisualElement();
            panel.AddToClassList(HandReadinessStyles.Card.Root);
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.Center;
            panel.style.alignSelf = Align.Center;
            panel.style.width = HeroContentWidth;
            panel.style.maxWidth = HeroContentWidth;
            panel.style.height = HeroHeight;
            panel.style.paddingTop = RLDSConstants.Spacing.Size3XL;
            panel.style.paddingBottom = RLDSConstants.Spacing.Size3XL;
            panel.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            panel.style.paddingRight = RLDSConstants.Spacing.Size3XL;
            panel.style.marginBottom = RLDSConstants.Spacing.Size4XL;

            var check = HandReadinessResources.CreateTintableIcon("icon_check_circle", 48);
            check.AddToClassList(HandReadinessStyles.Icon.ThemedPositive);
            check.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            panel.Add(check);

            var title = new Label("Your project is optimized for hands");
            title.AddToClassList(RLDSConstants.Typography.Heading1);
            title.AddToClassList(HandReadinessStyles.Text.Primary);
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            panel.Add(title);

            var subtitle = new Label($"All {totalResolvedCount} recommendations have been resolved");
            subtitle.AddToClassList(RLDSConstants.Typography.Body1Text);
            subtitle.AddToClassList(HandReadinessStyles.Text.Primary);
            subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.opacity = 0.6f;
            subtitle.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            panel.Add(subtitle);

            panel.Add(BuildOptimizedBadge());

            return panel;
        }

        /// <summary>
        /// "Project optimized" badge-pill: neutral badge shell with a positive
        /// check glyph.
        /// </summary>
        private static VisualElement BuildOptimizedBadge()
        {
            var badge = new VisualElement();
            badge.AddToClassList(RLDSConstants.BadgePill.Base);
            badge.AddToClassList(RLDSConstants.BadgePill.Neutral);

            var icon = HandReadinessResources.CreateTintableIcon(
                "icon_checkbox_check", RLDSConstants.IconSize.SizeXS);
            icon.AddToClassList(RLDSConstants.BadgePill.Icon);
            icon.AddToClassList(HandReadinessStyles.Icon.ThemedPositive);
            badge.Add(icon);

            var label = new Label("Project optimized for hands-only experiences");
            label.AddToClassList(RLDSConstants.BadgePill.Label);
            badge.Add(label);

            return badge;
        }

        private static VisualElement BuildWhatHappensNext()
        {
            var section = new VisualElement();
            section.style.alignSelf = Align.Center;
            section.style.width = HeroContentWidth;
            section.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            section.style.paddingRight = RLDSConstants.Spacing.Size3XL;

            var heading = new Label("What happens next");
            heading.AddToClassList(RLDSConstants.Typography.Heading2);
            heading.style.marginBottom = RLDSConstants.Spacing.SizeXL;
            section.Add(heading);

            AddBullet(
                section,
                "icon_hand_tracking",
                "Hands interactions and input methods are now optimized for this project.",
                addBottomMargin: true);
            AddBullet(
                section,
                "icon_touch_3_left",
                "Your project remains compatible with other input methods like controllers.",
                addBottomMargin: true);
            AddBullet(
                section,
                "icon_default_app",
                "Explore Building Blocks related to hand tracking.",
                addBottomMargin: false);

            return section;
        }

        private static void AddBullet(
            VisualElement parent,
            string iconResourceName,
            string text,
            bool addBottomMargin)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            if (addBottomMargin)
            {
                row.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            }

            // Bullet icons use the brand/icon-primary tint (white in dark mode).
            // Themed (not ThemedSecondary) maps to that token.
            var icon = HandReadinessResources.CreateTintableIcon(
                iconResourceName, RLDSConstants.IconSize.SizeLG);
            icon.AddToClassList(HandReadinessStyles.Icon.Themed);
            icon.style.marginRight = RLDSConstants.Spacing.SizeSM;
            row.Add(icon);

            var label = new Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body1Text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1;
            row.Add(label);

            parent.Add(row);
        }
    }
}
