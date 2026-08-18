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
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;
using RLDSBadge = Meta.XR.Editor.UserInterface.Badge;
using BadgeVariant = Meta.XR.Editor.UserInterface.BadgeVariant;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>Builds the todo list screen with an execution overview card and a list of pending changes.</summary>
    public static class TodoListScreen
    {
        /// <summary>Creates the todo list screen UI with an execution summary and a categorized list of changes.</summary>
        /// <param name="issues">The list of issues representing changes to be made.</param>
        /// <param name="selectedDeviceName">The name of the selected target device.</param>
        /// <param name="isUpdating">Whether an update is currently in progress.</param>
        /// <param name="onStartUpdate">Callback invoked when the Execute button is clicked.</param>
        /// <param name="onBack">Callback invoked when the Back button is clicked.</param>
        /// <param name="onRestart">Callback invoked when the Restart button is clicked.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete todo list screen.</returns>
        public static VisualElement Create(
            List<IssueData> issues,
            string selectedDeviceName,
            bool isUpdating,
            Action onStartUpdate,
            Action onBack,
            Action onRestart = null)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            var contentRow = new VisualElement();
            contentRow.style.flexDirection = FlexDirection.Row;
            contentRow.style.flexGrow = 1;
            contentRow.style.paddingLeft = RLDSConstants.Spacing.Size4XL;
            contentRow.style.paddingRight = RLDSConstants.Spacing.Size4XL;
            contentRow.style.paddingTop = RLDSConstants.Spacing.Size2XL;
            contentRow.style.paddingBottom = RLDSConstants.Spacing.SizeXL;

            issues = issues ?? new List<IssueData>();

            int totalItems = issues.Count;
            int autoCount = issues.Count(i => i.Category == IssueCategory.Automation);
            int aiItems = issues.Count(i => i.Category == IssueCategory.AI || i.IsAISuggestion || i.RequiresAI);
            int manualItems = issues.Count(i => i.Category == IssueCategory.Manual && !i.RequiresAI && !i.IsAISuggestion);
            int autoDone = issues.Count(i => i.Category == IssueCategory.Automation && i.IsFixed);
            bool allDone = issues.All(i => i.IsFixed);
            float automationPct = totalItems > 0 ? (float)autoCount / totalItems * 100f : 0f;

            // ---- Left column: Execute overview card ----
            var leftCol = new VisualElement();
            leftCol.style.width = new Length(35, LengthUnit.Percent);
            leftCol.style.paddingRight = RLDSConstants.Spacing.SizeXL;

            var summaryCard = CreateBorderedCard();
            summaryCard.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
            summaryCard.style.paddingRight = RLDSConstants.Spacing.SizeXL;
            summaryCard.style.paddingTop = RLDSConstants.Spacing.SizeXL;
            summaryCard.style.paddingBottom = RLDSConstants.Spacing.SizeXL;

            var cardTitle = new Label("Execute overview");
            cardTitle.AddToClassList(RLDSConstants.Typography.Heading2);
            cardTitle.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            summaryCard.Add(cardTitle);

            var cardDesc = new Label(
                $"Here's your prioritized list of changes to make your project ready for {selectedDeviceName ?? "the target device"}.");
            cardDesc.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            cardDesc.style.whiteSpace = WhiteSpace.Normal;
            cardDesc.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            summaryCard.Add(cardDesc);

            AddFullWidthDivider(summaryCard);

            int estimatedMinutes = manualItems * 15 + aiItems * 5 + autoCount * 1;
            AddStatRow(summaryCard, "Estimated time", $"~{estimatedMinutes} min");
            AddStatRow(summaryCard, "Rollback", "Available");
            AddStatRow(summaryCard, "AI recommendations", aiItems.ToString());
            AddStatRow(summaryCard, "Manual items", manualItems.ToString());
            AddStatRow(summaryCard, "Automated updates", autoCount.ToString());

            AddFullWidthDivider(summaryCard);

            var coverageLabel = new Label($"Automation coverage: {automationPct:F0}%");
            coverageLabel.AddToClassList(RLDSConstants.Typography.Body1Label);
            coverageLabel.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            summaryCard.Add(coverageLabel);

            var progressBar = new Meta.XR.Editor.UserInterface.ProgressBar(automationPct).Build();
            progressBar.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            summaryCard.Add(progressBar);

            if (isUpdating)
            {
                var warningText = new Label("Update in progress... Do not close this window.");
                warningText.AddToClassList(RLDSConstants.Typography.Body1Label);
                warningText.AddToClassList(HandReadinessStyles.WarningText.Root);
                summaryCard.Add(warningText);
            }
            else if (onStartUpdate != null)
            {
                var startButton = HandReadinessResources.CreateNextButton("Execute", onStartUpdate);
                startButton.style.width = StyleKeyword.Auto;
                summaryCard.Add(startButton);
            }

            leftCol.Add(summaryCard);
            contentRow.Add(leftCol);

            // ---- Right: What's changing panel ----
            var rightCol = new VisualElement();
            rightCol.style.flexGrow = 1;

            var todoCard = CreateBorderedCard();

            var todoHeaderRow = new VisualElement();
            todoHeaderRow.style.flexDirection = FlexDirection.Row;
            todoHeaderRow.style.justifyContent = Justify.SpaceBetween;
            todoHeaderRow.style.alignItems = Align.Center;
            todoHeaderRow.style.paddingLeft = RLDSConstants.Spacing.SizeLG;
            todoHeaderRow.style.paddingRight = RLDSConstants.Spacing.SizeSM;
            todoHeaderRow.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            todoHeaderRow.style.paddingBottom = RLDSConstants.Spacing.SizeSM;

            var todoTitle = new Label("What's changing");
            todoTitle.AddToClassList(RLDSConstants.Typography.Body1Label);
            todoHeaderRow.Add(todoTitle);

            var headerActions = new VisualElement();
            headerActions.style.flexDirection = FlexDirection.Row;
            headerActions.style.alignItems = Align.Center;

            var acceptBtn = new UnityEngine.UIElements.Button();
            acceptBtn.text = "Accept all AI recommendation";
            acceptBtn.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            acceptBtn.AddToClassList(HandReadinessStyles.InlineLinkButton.Root);
            headerActions.Add(acceptBtn);

            var kebab = new Label("\u22EE");
            kebab.AddToClassList(RLDSConstants.Typography.Body1Label);
            kebab.style.marginLeft = RLDSConstants.Spacing.Size3XS;
            headerActions.Add(kebab);

            todoHeaderRow.Add(headerActions);
            todoCard.Add(todoHeaderRow);

            var todoSeparator = new VisualElement();
            todoSeparator.AddToClassList(RLDSConstants.Divider.Base);
            todoCard.Add(todoSeparator);

            var scroll = new ScrollView();
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = RLDSConstants.Spacing.SizeLG;
            scroll.style.paddingRight = RLDSConstants.Spacing.SizeLG;
            scroll.style.paddingBottom = RLDSConstants.Spacing.SizeMD;

            foreach (var issue in issues.Where(i => i.Category != IssueCategory.Automation))
            {
                string badge = issue.Category == IssueCategory.Manual ? "Manual" : "AI";
                var variant = issue.Category == IssueCategory.Manual
                    ? BadgeVariant.Warning : BadgeVariant.Default;
                scroll.Add(CreateTodoItem(issue.Title, issue.IsFixed, badge, variant));
            }

            bool allAutosDone = isUpdating && autoDone >= autoCount;
            string autoLabel = allDone ? $"{autoCount} automated updates" : $"{autoCount} automated items";
            scroll.Add(CreateTodoItem(autoLabel, allAutosDone,
                "Automated", BadgeVariant.Positive));

            todoCard.Add(scroll);
            rightCol.Add(todoCard);
            contentRow.Add(rightCol);
            container.Add(contentRow);

            // ---- Footer ----
            var footerDivider = new VisualElement();
            footerDivider.AddToClassList(RLDSConstants.Divider.Base);
            container.Add(footerDivider);

            var backButton = HandReadinessResources.CreateBackButton("Back", onBack);
            HandReadinessResources.SetButtonEnabled(backButton, !isUpdating);

            UnityEngine.UIElements.Button nextButton;
            if (onRestart != null)
            {
                nextButton = HandReadinessResources.CreateNextButton("Restart", onRestart);
            }
            else
            {
                nextButton = HandReadinessResources.CreateNextButton("", null);
                nextButton.style.display = DisplayStyle.None;
            }

            container.Add(HandReadinessResources.CreateFooter(backButton, nextButton));

            return container;
        }

        private static VisualElement CreateBorderedCard()
        {
            var card = new VisualElement();
            card.AddToClassList(HandReadinessStyles.Card.Root);
            return card;
        }

        private static void AddFullWidthDivider(VisualElement parent)
        {
            var d = new VisualElement();
            d.AddToClassList(HandReadinessStyles.Card.Divider);
            d.style.marginLeft = -RLDSConstants.Spacing.SizeXL;
            d.style.marginRight = -RLDSConstants.Spacing.SizeXL;
            parent.Add(d);
        }

        private static void AddStatRow(VisualElement parent, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginBottom = RLDSConstants.Spacing.SizeXS;

            var labelElement = new Label(label);
            labelElement.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            row.Add(labelElement);

            var valueElement = new Label(value);
            valueElement.AddToClassList(RLDSConstants.Typography.Body1Label);
            row.Add(valueElement);

            parent.Add(row);
        }

        private static VisualElement CreateTodoItem(
            string title, bool isDone, string badgeText, BadgeVariant badgeVariant)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = RLDSConstants.Spacing.Size2XS + RLDSConstants.Spacing.Size4XS;
            row.style.paddingBottom = RLDSConstants.Spacing.Size2XS + RLDSConstants.Spacing.Size4XS;

            var iconElement = new Image();
            iconElement.style.width = RLDSConstants.IconSize.SizeMD;
            iconElement.style.height = RLDSConstants.IconSize.SizeMD;
            iconElement.style.marginRight = RLDSConstants.Spacing.SizeSM;
            iconElement.style.flexShrink = 0;
            iconElement.scaleMode = ScaleMode.ScaleToFit;

            var iconResource = isDone ? "icon_check_circle" : "icon_check_box_circle";
            var tex = Resources.Load<Texture2D>(iconResource);
            if (tex != null) iconElement.image = tex;
            row.Add(iconElement);

            var lbl = new Label(title);
            lbl.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            if (isDone) lbl.style.opacity = 0.6f;
            lbl.style.flexGrow = 1;
            lbl.style.overflow = Overflow.Hidden;
            lbl.style.textOverflow = TextOverflow.Ellipsis;
            lbl.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(lbl);

            var badge = new RLDSBadge(badgeText, badgeVariant).Build();
            badge.style.marginLeft = RLDSConstants.Spacing.SizeXS;
            badge.style.flexShrink = 0;
            row.Add(badge);

            var chevron = new Label("\u2304");
            chevron.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            chevron.style.marginLeft = RLDSConstants.Spacing.Size3XS;
            row.Add(chevron);

            return row;
        }
    }
}
