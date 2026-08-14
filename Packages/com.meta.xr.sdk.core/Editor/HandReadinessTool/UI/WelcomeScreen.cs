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
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;
using UIElements = UnityEngine.UIElements;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Welcome screen. Cover banner with gradient + hand icon, title/subtitle
    /// overlaid, "How it works" section, and "Get started" footer button.
    /// </summary>
    public static class WelcomeScreen
    {
        /// <summary>Creates the welcome screen UI with a cover banner, how-it-works section, and a get-started button.</summary>
        /// <param name="onGetStarted">Callback invoked when the Get started button is clicked.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete welcome screen.</returns>
        public static VisualElement Create(Action onGetStarted)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            // ---- Scrollable content ----
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.paddingLeft = RLDSConstants.Spacing.Size5XL;
            content.style.paddingRight = RLDSConstants.Spacing.Size5XL;
            content.style.paddingTop = RLDSConstants.Spacing.Size4XL;

            var cover = HandReadinessResources.CreateCoverBanner(
                title: "Hands optimizer",
                subtitle: "Audit your project for hands-only compatibility. Review recommendations and " +
                          "issues to ensure your app works without controllers.",
                iconResourceName: "icon_hand_tracking");
            cover.style.marginBottom = RLDSConstants.Spacing.Size4XL;
            content.Add(cover);

            var howSection = new VisualElement();
            howSection.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            howSection.style.paddingRight = RLDSConstants.Spacing.Size3XL;

            var sectionHeader = new UIElements.Label("How it works");
            sectionHeader.AddToClassList(RLDSConstants.Typography.Heading2);
            sectionHeader.style.marginBottom = RLDSConstants.Spacing.SizeXL;
            howSection.Add(sectionHeader);

            AddIconBullet(howSection, "icon_file",
                "Share project files or describe what you're building.");
            AddIconBullet(howSection, "icon_hand_tracking",
                "Your project is checked against hands-only tracking requirements.");
            AddIconBullet(howSection, "icon_list_checked",
                "See what needs to change and why.");

            content.Add(howSection);
            container.Add(content);

            // ---- Footer ----
            var bottomBar = new VisualElement();
            bottomBar.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
            bottomBar.style.paddingRight = RLDSConstants.Spacing.SizeXL;
            bottomBar.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            bottomBar.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            bottomBar.style.flexDirection = FlexDirection.Row;
            bottomBar.style.justifyContent = Justify.FlexEnd;

            var checkButton = HandReadinessResources.CreateNextButton("Get started", onGetStarted);
            bottomBar.Add(checkButton);

            container.Add(bottomBar);

            return container;
        }

        private static void AddIconBullet(VisualElement parent, string iconResourceName, string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = RLDSConstants.Spacing.SizeMD;

            var iconContainer = new VisualElement();
            iconContainer.style.width = RLDSConstants.IconSize.SizeLG;
            iconContainer.style.height = RLDSConstants.IconSize.SizeLG;
            iconContainer.style.minWidth = RLDSConstants.IconSize.SizeLG;
            iconContainer.style.marginRight = RLDSConstants.Spacing.SizeSM;

            var iconImage = HandReadinessResources.CreateTintableIcon(iconResourceName, RLDSConstants.IconSize.SizeLG);
            iconImage.AddToClassList(HandReadinessStyles.Icon.ThemedSecondary);
            iconContainer.Add(iconImage);

            row.Add(iconContainer);

            var label = new UIElements.Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body1Text);
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);

            parent.Add(row);
        }
    }
}
