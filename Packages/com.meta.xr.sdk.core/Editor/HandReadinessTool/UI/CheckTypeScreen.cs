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
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Check-type selection screen ("How should we check your project?"). Two
    /// mutually exclusive radio cards:
    ///
    /// - Standard check (regex-only) — runs the heuristic config + code-pattern scan.
    /// - AI-powered check — adds AI analysis, requires an AI service provider.
    ///
    /// Next is disabled until the user picks one. The choice flows out via
    /// <paramref name="useAI"/> on the `onNext` callback so the parent window
    /// can route AI users to the Project Description screen and Standard users
    /// straight to Scanning.
    /// </summary>
    public static class CheckTypeScreen
    {
        // Fixed width of the two-card column so the cards align consistently
        // with the title/subtitle lockup above and the footer below.
        private const int CardColumnWidth = 497;

        /// <summary>Creates the check-type selection screen with two radio cards and a footer.</summary>
        /// <param name="initialUseAI">
        /// Pre-selected card on entry. When null, neither card is selected and Next is disabled.
        /// When non-null, the matching card starts selected (used when the user steps Back from a later screen).
        /// </param>
        /// <param name="onBack">Invoked when the Back button is clicked.</param>
        /// <param name="onNext">Invoked when Next is clicked, with the selected `useAI` flag.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete check-type screen.</returns>
        public static VisualElement Create(
            bool? initialUseAI,
            Action onBack,
            Action<bool> onNext)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            // ---- Main content ----
            var content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.alignItems = Align.Center;
            content.style.paddingLeft = RLDSConstants.Spacing.Size5XL;
            content.style.paddingRight = RLDSConstants.Spacing.Size5XL;
            content.style.paddingTop = RLDSConstants.Spacing.Size5XL;

            // Title lockup — centered.
            var titleLockup = new VisualElement();
            titleLockup.style.alignItems = Align.Center;
            titleLockup.style.marginBottom = RLDSConstants.Spacing.Size5XL;

            var heading = new Label("How should we check your project?");
            heading.AddToClassList(RLDSConstants.Typography.Heading2);
            heading.style.marginBottom = RLDSConstants.Spacing.Size3XS;
            heading.style.whiteSpace = WhiteSpace.Normal;
            titleLockup.Add(heading);

            var subtitle = new Label("Choose how you'd like us to scan for issues.");
            subtitle.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            titleLockup.Add(subtitle);

            content.Add(titleLockup);

            var cardList = new VisualElement();
            cardList.style.width = CardColumnWidth;

            bool? selected = initialUseAI;
            VisualElement standardCard = null;
            VisualElement aiCard = null;
            UnityEngine.UIElements.Button nextButton = null;

            void Refresh()
            {
                ApplySelection(standardCard, selected == false);
                ApplySelection(aiCard, selected == true);
                if (nextButton != null)
                {
                    HandReadinessResources.SetButtonEnabled(nextButton, selected.HasValue);
                }
            }

            standardCard = CreateOptionCard(
                iconResourceName: "icon_list_checked",
                title: "Standard check",
                description:
                    "Scans your project for common configuration issues and " +
                    "controller-only input patterns that may prevent hand tracking " +
                    "from working.",
                onClick: () => { selected = false; Refresh(); });
            standardCard.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            cardList.Add(standardCard);

            aiCard = CreateOptionCard(
                iconResourceName: "icon_ai_agent",
                title: "AI-powered check",
                description:
                    "Includes everything in the standard check, plus AI analysis of " +
                    "your code and shaders for targeted, project-specific recommendations. " +
                    "Requires connecting an AI service provider.",
                onClick: () => { selected = true; Refresh(); });
            cardList.Add(aiCard);

            content.Add(cardList);
            container.Add(content);

            // ---- Footer ----
            var backButton = HandReadinessResources.CreateBackButton("Back", onBack);
            nextButton = HandReadinessResources.CreateNextButton(
                "Next",
                () => onNext(selected ?? false));
            container.Add(HandReadinessResources.CreateFooter(backButton, nextButton));

            Refresh();

            return container;
        }

        private static VisualElement CreateOptionCard(
            string iconResourceName,
            string title,
            string description,
            Action onClick)
        {
            var card = new VisualElement();
            card.AddToClassList(HandReadinessStyles.OptionCard.Root);
            if (onClick != null)
            {
                // Make the whole card clickable (not just the label) so the user can
                // tap anywhere in the row to pick it.
                card.AddManipulator(new Clickable(onClick));
            }

            var icon = HandReadinessResources.CreateTintableIcon(
                iconResourceName, RLDSConstants.IconSize.SizeLG);
            icon.AddToClassList(HandReadinessStyles.OptionCard.Icon);
            card.Add(icon);

            var labelDescription = new VisualElement();
            labelDescription.style.flexGrow = 1;
            labelDescription.style.flexBasis = 0;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(RLDSConstants.Typography.Heading3);
            titleLabel.AddToClassList(HandReadinessStyles.OptionCard.Title);
            titleLabel.style.marginBottom = RLDSConstants.Spacing.Size2XS;
            labelDescription.Add(titleLabel);

            var descLabel = new Label(description);
            descLabel.AddToClassList(RLDSConstants.Typography.Body1Text);
            descLabel.AddToClassList(HandReadinessStyles.OptionCard.Description);
            labelDescription.Add(descLabel);

            card.Add(labelDescription);

            return card;
        }

        // Selected state is communicated via the `.hrt-option-card--selected`
        // modifier class — a filled background plus border accent. No separate
        // check-icon affordance.
        private static void ApplySelection(VisualElement card, bool selected)
        {
            if (card == null) return;
            if (selected)
            {
                card.AddToClassList(HandReadinessStyles.OptionCard.Selected);
            }
            else
            {
                card.RemoveFromClassList(HandReadinessStyles.OptionCard.Selected);
            }
        }
    }
}
