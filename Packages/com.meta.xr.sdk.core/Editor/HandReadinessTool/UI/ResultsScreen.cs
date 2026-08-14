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
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using ActionLinkDescription = Meta.XR.Editor.UserInterface.ActionLinkDescription;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Issue category for the results screen.
    /// </summary>
    public enum IssueCategory
    {
        Manual,
        AI,
        Automation
    }

    /// <summary>
    /// Results screen — "Hand optimization report." Header with device tag
    /// + progress bar; table with Recommendation / Priority / Type columns;
    /// footer with Export + Check optimization buttons.
    /// </summary>
    public static class ResultsScreen
    {

        /// <summary>Creates the hand optimization results screen UI with a progress header and issue table.</summary>
        /// <param name="selectedDeviceName">The name of the selected target device.</param>
        /// <param name="issues">The list of issues to display in the results table.</param>
        /// <param name="onViewDetails">Callback invoked when the user requests details for a non-automated issue.</param>
        /// <param name="onApplyFix">Callback invoked when the user applies an automated fix for an issue.</param>
        /// <param name="onExport">Callback invoked when the Export as Markdown button is clicked.</param>
        /// <param name="onCheckReadiness">Callback invoked when the Check readiness button is clicked.</param>
        /// <param name="onReanalyze">Callback invoked when the Reanalyze project link is clicked.</param>
        /// <param name="onApplyAllAutomated">Callback invoked when the Apply all automated fixes button is clicked.</param>
        /// <param name="onCopyToClipboard">Callback invoked when an issue's overflow "Copy to clipboard" action is chosen.</param>
        /// <param name="onMarkComplete">Callback invoked when an issue's overflow "Mark as complete" action is chosen.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete results screen.</returns>
        public static VisualElement Create(
            string selectedDeviceName,
            List<IssueData> issues,
            Action<IssueData> onViewDetails,
            Action<IssueData> onApplyFix,
            Action onExport,
            Action onCheckReadiness,
            Action onReanalyze = null,
            Action onApplyAllAutomated = null,
            Action<IssueData> onCopyToClipboard = null,
            Action<IssueData> onMarkComplete = null)
        {
            var allIssues = issues ?? new List<IssueData>();
            int totalCount = allIssues.Count;
            int fixedCount = allIssues.Count(i => i.IsFixed);
            var unfixedIssues = allIssues.Where(i => !i.IsFixed).ToList();
            bool allComplete = totalCount > 0 && unfixedIssues.Count == 0;

            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            // Content area: pinned header at top, scrollable table below.
            var contentArea = new VisualElement();
            contentArea.style.flexGrow = 1;
            contentArea.style.paddingLeft = RLDSConstants.Spacing.Size4XL;
            contentArea.style.paddingRight = RLDSConstants.Spacing.Size4XL;
            contentArea.style.paddingTop = RLDSConstants.Spacing.SizeXL;

            // Page header — stays fixed at the top while the table scrolls.
            contentArea.Add(CreateHeader(totalCount, fixedCount, allComplete, onApplyAllAutomated));

            // Column header — also stays fixed so the column titles are always visible.
            contentArea.Add(CreateTableHeader());

            // Table rows — unfixed first (sorted High→Medium→Low), then fixed (same).
            // IssuePriority enum is declared High=0, Medium=1, Low=2 so plain
            // ascending order gives the desired severity ranking.
            var orderedIssues = allIssues.Where(i => !i.IsFixed).OrderBy(i => i.Priority)
                .Concat(allIssues.Where(i => i.IsFixed).OrderBy(i => i.Priority))
                .ToList();

            var tableScrollView = new ScrollView();
            tableScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            tableScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            tableScrollView.style.flexGrow = 1;
            tableScrollView.style.paddingBottom = RLDSConstants.Spacing.SizeXL;
            tableScrollView.Add(CreateTableRows(orderedIssues, onViewDetails, onApplyFix, onCopyToClipboard, onMarkComplete));
            contentArea.Add(tableScrollView);

            container.Add(contentArea);

            // Footer
            container.Add(CreateFooter(onExport, onCheckReadiness, onReanalyze, allComplete));

            return container;
        }

        // ----------------------------------------------------------------
        // Header
        // ----------------------------------------------------------------

        private static VisualElement CreateHeader(
            int totalCount, int fixedCount, bool allComplete, Action onApplyAll)
        {
            var header = new VisualElement();
            header.style.marginBottom = RLDSConstants.Spacing.SizeMD;

            // Top row: "Apply all automated fixes" button when work remains, or a
            // disabled "Automated fixes completed" badge when everything is resolved.
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.FlexEnd;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = RLDSConstants.Spacing.Size3XS;

            if (allComplete)
            {
                topRow.Add(CreateCompletedBadge("Automated fixes completed"));
            }
            else if (onApplyAll != null)
            {
                var applyAllBtn = new RLDSButton(
                    new ActionLinkDescription
                    {
                        Content = new GUIContent("Apply all automated fixes"),
                        Action = onApplyAll,
                    },
                    RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Small)
                {
                    LeftIcon = HandReadinessIcons.ApplyAutomated,
                }.Build();
                topRow.Add(applyAllBtn);
            }

            header.Add(topRow);

            var title = new Label("Hands optimization report");
            title.AddToClassList(RLDSConstants.Typography.Heading2);
            title.style.marginBottom = RLDSConstants.Spacing.Size2XS;
            header.Add(title);

            // Subtitle — kept unchanged in both states so the screen continues to
            // describe its purpose; the completed status moves to a separate row below.
            int unfixedCount = totalCount - fixedCount;
            int subtitleCount = allComplete ? totalCount : unfixedCount;
            var subtitle = new Label(
                $"Resolve these {subtitleCount} recommendations to ensure your app works without controllers");
            subtitle.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            subtitle.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            header.Add(subtitle);

            // Progress row: progress bar + count while work remains, or a green
            // check + status line once everything is resolved.
            if (allComplete)
            {
                header.Add(CreateCompletedStatusRow(
                    "Recommendations are completed. Click \"Confirm\" to verify."));
            }
            else
            {
                var progressRow = new VisualElement();
                progressRow.style.flexDirection = FlexDirection.Row;
                progressRow.style.alignItems = Align.Center;
                progressRow.style.maxWidth = 320;

                var progressTrack = new VisualElement();
                progressTrack.AddToClassList(RLDSConstants.ProgressBar.Container);
                progressTrack.style.flexGrow = 1;
                progressTrack.style.marginRight = RLDSConstants.Spacing.SizeSM;

                float progress = totalCount > 0 ? (float)fixedCount / totalCount : 0f;
                var progressFill = new VisualElement();
                progressFill.AddToClassList(RLDSConstants.ProgressBar.Bar);
                progressFill.style.width = new Length(progress * 100f, LengthUnit.Percent);
                progressTrack.Add(progressFill);

                progressRow.Add(progressTrack);

                var progressLabel = new Label($"{fixedCount}/{totalCount}");
                progressLabel.AddToClassList(RLDSConstants.Typography.Meta);
                progressLabel.style.flexShrink = 0;
                progressRow.Add(progressLabel);

                header.Add(progressRow);
            }

            return header;
        }

        /// <summary>
        /// "Automated fixes completed" pill used in the top-right when everything is
        /// resolved. Positive BadgeTag (green check + green-tinted text + outline).
        /// </summary>
        private static VisualElement CreateCompletedBadge(string text)
        {
            var badge = new VisualElement();
            badge.AddToClassList(RLDSConstants.BadgeTag.Base);
            badge.AddToClassList(RLDSConstants.BadgeTag.Positive);
            badge.style.flexDirection = FlexDirection.Row;
            badge.style.alignItems = Align.Center;
            badge.style.alignSelf = Align.FlexStart;

            var icon = HandReadinessResources.CreateTintableIcon(
                "icon_check_circle", RLDSConstants.IconSize.SizeXS);
            icon.AddToClassList(HandReadinessStyles.Icon.ThemedPositive);
            icon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            badge.Add(icon);

            var label = new Label(text);
            label.AddToClassList(RLDSConstants.BadgeTag.Label);
            badge.Add(label);

            return badge;
        }

        /// <summary>
        /// In-flow status row (green check icon + label) that replaces the progress
        /// bar once every recommendation is resolved. Plain icon + text — not a
        /// BadgeTag.
        /// </summary>
        private static VisualElement CreateCompletedStatusRow(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var icon = HandReadinessResources.CreateTintableIcon(
                "icon_check_circle", RLDSConstants.IconSize.SizeXS);
            icon.AddToClassList(HandReadinessStyles.Icon.ThemedPositive);
            icon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            row.Add(icon);

            var label = new Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            row.Add(label);

            return row;
        }

        // ----------------------------------------------------------------
        // Table
        // ----------------------------------------------------------------

        /// <summary>
        /// Column-header row + divider. Rendered outside the ScrollView so the
        /// column titles stay visible while the list scrolls beneath them.
        /// </summary>
        private static VisualElement CreateTableHeader()
        {
            var wrapper = new VisualElement();
            // 16px gap between the column-label divider and the first table row,
            // on top of the rows wrapper's own 8px top padding (= 24px total).
            wrapper.style.marginBottom = RLDSConstants.Spacing.SizeMD;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.paddingTop = RLDSConstants.Spacing.SizeXS;
            headerRow.style.paddingBottom = RLDSConstants.Spacing.SizeXS;

            AddColumnHeader(headerRow, "Recommendation", 0, true);
            AddColumnHeader(headerRow, "Priority", 110, false);
            AddColumnHeader(headerRow, "Type", 110, false);
            var actionSpacer = new VisualElement();
            actionSpacer.style.width = 160;
            headerRow.Add(actionSpacer);

            wrapper.Add(headerRow);

            var headerDivider = new VisualElement();
            headerDivider.AddToClassList(RLDSConstants.Divider.Base);
            wrapper.Add(headerDivider);

            return wrapper;
        }

        private static VisualElement CreateTableRows(
            List<IssueData> issues, Action<IssueData> onViewDetails, Action<IssueData> onApplyFix,
            Action<IssueData> onCopyToClipboard, Action<IssueData> onMarkComplete)
        {
            // Table content: 8px padding top/bottom, 12px gap between rows.
            var rows = new VisualElement();
            rows.style.paddingTop = RLDSConstants.Spacing.SizeXS;
            rows.style.paddingBottom = RLDSConstants.Spacing.SizeXS;

            for (int i = 0; i < issues.Count; i++)
            {
                var rowEl = CreateIssueRow(issues[i], onViewDetails, onApplyFix, onCopyToClipboard, onMarkComplete);
                if (i < issues.Count - 1)
                {
                    rowEl.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                }
                rows.Add(rowEl);
            }
            return rows;
        }

        private static void AddColumnHeader(
            VisualElement parent, string text, int width, bool grow)
        {
            var label = new Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            if (grow)
            {
                label.style.flexGrow = 1;
                label.style.flexBasis = 0;
            }
            else
            {
                label.style.width = width;
                label.style.flexShrink = 0;
            }
            parent.Add(label);
        }

        private static VisualElement CreateIssueRow(
            IssueData issue, Action<IssueData> onViewDetails, Action<IssueData> onApplyFix,
            Action<IssueData> onCopyToClipboard, Action<IssueData> onMarkComplete)
        {
            bool isFixed = issue.IsFixed;
            float textAlpha = isFixed ? 0.4f : 1f;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            // Row 32px tall, content 24px → 4px padding each side.
            row.style.paddingTop = RLDSConstants.Spacing.Size3XS;
            row.style.paddingBottom = RLDSConstants.Spacing.Size3XS;

            // Circle indicator — green checkmark if fixed, empty circle if not
            row.Add(isFixed ? CreateCheckmark() : CreateCircle());

            // The base body2-small-label resolves to text-secondary (~70% white);
            // .hrt-text-primary flips the col-1 label to pure white.
            var titleLabel = new Label(issue.Title);
            titleLabel.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            titleLabel.AddToClassList(HandReadinessStyles.Text.Primary);
            titleLabel.style.flexGrow = 1;
            titleLabel.style.flexBasis = 0;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.opacity = textAlpha;
            row.Add(titleLabel);

            // Priority: icon + text
            row.Add(CreatePriorityCell(issue.Priority, textAlpha));

            // Type: badge
            row.Add(CreateTypeCell(issue.Category, textAlpha));

            // Action: primary button + overflow (⋮) context menu
            row.Add(CreateActionCell(issue, onViewDetails, onApplyFix, onCopyToClipboard, onMarkComplete));

            return row;
        }

        private static VisualElement CreateCheckmark()
        {
            var container = new VisualElement();
            container.AddToClassList(HandReadinessStyles.PhaseIcon.Root);
            container.AddToClassList(HandReadinessStyles.PhaseIcon.Small);
            container.AddToClassList(HandReadinessStyles.PhaseIcon.Complete);
            container.style.marginRight = RLDSConstants.Spacing.SizeSM;
            container.style.flexShrink = 0;

            var check = new Label("\u2713");
            check.AddToClassList(HandReadinessStyles.PhaseIcon.CheckGlyph);
            container.Add(check);

            return container;
        }

        private static VisualElement CreateCircle()
        {
            var circle = HandReadinessResources.CreateTintableIcon("icon_check_box_circle", 16);
            circle.AddToClassList(HandReadinessStyles.Icon.ThemedSecondary);
            circle.style.marginRight = RLDSConstants.Spacing.SizeSM;
            return circle;
        }

        /// <summary>
        /// Priority cell — semantic-tinted glyph (red filled circle for High, orange
        /// triangle for Medium, neutral for Low) followed by the priority label. The
        /// tint color comes from the RLDS semantic icon palette via `.hrt-priority-*`.
        /// </summary>
        private static VisualElement CreatePriorityCell(IssuePriority priority, float alpha = 1f)
        {
            var cell = new VisualElement();
            cell.style.width = 110;
            cell.style.flexShrink = 0;
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.Center;
            cell.style.opacity = alpha;

            (string iconName, string tintClass) = priority switch
            {
                IssuePriority.High => ("icon_priority_high", HandReadinessStyles.Priority.High),
                IssuePriority.Medium => ("icon_priority_medium", HandReadinessStyles.Priority.Medium),
                _ => ("icon_priority_low", HandReadinessStyles.Priority.Low),
            };

            var icon = HandReadinessResources.CreateTintableIcon(iconName, RLDSConstants.IconSize.SizeXS);
            icon.AddToClassList(tintClass);
            icon.style.marginRight = RLDSConstants.Spacing.SizeXS;
            cell.Add(icon);

            var label = new Label(priority.ToString());
            label.AddToClassList(RLDSConstants.Typography.Meta);
            label.AddToClassList(HandReadinessStyles.Text.Primary);
            cell.Add(label);

            return cell;
        }

        private static VisualElement CreateTypeCell(IssueCategory category, float alpha = 1f)
        {
            var cell = new VisualElement();
            cell.style.width = 110;
            cell.style.flexShrink = 0;
            cell.style.alignItems = Align.FlexStart;
            cell.style.opacity = alpha;

            string text;
            string variantClass;
            switch (category)
            {
                case IssueCategory.Manual:
                    text = "Manual";
                    variantClass = RLDSConstants.BadgePill.Neutral;
                    break;
                case IssueCategory.AI:
                    text = "AI assisted";
                    variantClass = RLDSConstants.BadgePill.Info;
                    break;
                default:
                    text = "Automated";
                    variantClass = RLDSConstants.BadgePill.Positive;
                    break;
            }

            var badge = new VisualElement();
            badge.AddToClassList(RLDSConstants.BadgePill.Base);
            badge.AddToClassList(variantClass);

            var badgeLabel = new Label(text);
            badgeLabel.AddToClassList(RLDSConstants.BadgePill.Label);
            badge.Add(badgeLabel);

            cell.Add(badge);
            return cell;
        }

        private const int ContextMenuWidth = 192;
        private const int OverflowButtonSize = 24;

        private static VisualElement CreateActionCell(
            IssueData issue, Action<IssueData> onViewDetails, Action<IssueData> onApplyFix,
            Action<IssueData> onCopyToClipboard, Action<IssueData> onMarkComplete)
        {
            var cell = new VisualElement();
            cell.style.width = 160;
            cell.style.flexShrink = 0;
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.FlexEnd;

            bool isAutomated = issue.Category == IssueCategory.Automation;
            string buttonText = isAutomated ? "Apply automated fix" : "View details";

            if (issue.IsFixed)
            {
                var fixedBtn = new RLDSButton(
                    new ActionLinkDescription { Content = new GUIContent(buttonText), Action = () => { } },
                    RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.XSmall)
                {
                    Disable = true,
                }.Build();
                cell.Add(fixedBtn);
                return cell;
            }

            // Primary button + overflow kebab read as one split control. RLDS
            // buttons carry no default margin, so a 1px left margin on the kebab
            // is the only gap between them.
            var actionBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(buttonText),
                    Action = () =>
                    {
                        if (isAutomated)
                            onApplyFix?.Invoke(issue);
                        else
                            onViewDetails?.Invoke(issue);
                    },
                },
                RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.XSmall).Build();
            actionBtn.style.marginRight = 0;
            actionBtn.style.borderTopRightRadius = 0;
            actionBtn.style.borderBottomRightRadius = 0;
            cell.Add(actionBtn);

            VisualElement overflowEl = null;
            overflowEl = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent("⋮"),
                    Action = () => ShowContextMenu(overflowEl, BuildOverflowItems(
                        issue, onViewDetails, onCopyToClipboard, onMarkComplete)),
                },
                RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.XSmall).Build();
            overflowEl.style.minWidth = OverflowButtonSize;
            overflowEl.style.marginLeft = 1;
            overflowEl.style.paddingLeft = 6;
            overflowEl.style.paddingRight = 6;
            overflowEl.style.borderTopLeftRadius = 0;
            overflowEl.style.borderBottomLeftRadius = 0;
            cell.Add(overflowEl);

            return cell;
        }

        // Secondary actions per issue type, mirroring the Figma context-menu variants.
        private static List<(string label, Action onClick)> BuildOverflowItems(
            IssueData issue, Action<IssueData> onViewDetails,
            Action<IssueData> onCopyToClipboard, Action<IssueData> onMarkComplete)
        {
            var items = new List<(string label, Action onClick)>();
            switch (issue.Category)
            {
                case IssueCategory.Automation:
                    items.Add(("View details", () => onViewDetails?.Invoke(issue)));
                    break;
                case IssueCategory.AI:
                    items.Add(("Apply AI recommended fix", () => onViewDetails?.Invoke(issue)));
                    items.Add(("Mark as complete", () => onMarkComplete?.Invoke(issue)));
                    break;
                default:
                    items.Add(("Copy to clipboard", () => onCopyToClipboard?.Invoke(issue)));
                    items.Add(("Mark as complete", () => onMarkComplete?.Invoke(issue)));
                    break;
            }
            return items;
        }

        // Floating RLDS context menu anchored under and right-aligned to the trigger.
        // Mirrors Meta.XR.Editor.UserInterface.DropdownMenu, which is internal to its
        // own assembly and so can't be referenced from here.
        private static void ShowContextMenu(VisualElement anchor, List<(string label, Action onClick)> items)
        {
            var styledRoot = FindStyledRoot(anchor);
            if (styledRoot == null) return;

            var overlay = new VisualElement();
            overlay.AddToClassList(RLDSConstants.DropdownMenu.Overlay);
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                overlay.RemoveFromHierarchy();
            });

            var menu = new VisualElement();
            menu.AddToClassList(RLDSConstants.DropdownMenu.Container);
            menu.style.position = Position.Absolute;
            menu.style.width = ContextMenuWidth;

            foreach (var item in items)
            {
                var row = new VisualElement();
                row.AddToClassList(RLDSConstants.DropdownMenu.Item);
                var label = new Label(item.label);
                label.AddToClassList(RLDSConstants.DropdownMenu.ItemLabel);
                row.Add(label);
                var captured = item.onClick;
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    overlay.RemoveFromHierarchy();
                    captured?.Invoke();
                });
                menu.Add(row);
            }

            overlay.Add(menu);
            styledRoot.Add(overlay);

            menu.schedule.Execute(() =>
            {
                var anchorBound = anchor.worldBound;
                var rootBound = styledRoot.worldBound;
                menu.style.top = anchorBound.yMax - rootBound.y + RLDSConstants.Spacing.Size3XS;
                menu.style.left = anchorBound.xMax - rootBound.x - ContextMenuWidth;
            });
        }

        private static VisualElement FindStyledRoot(VisualElement element)
        {
            VisualElement styledRoot = null;
            var current = element;
            while (current != null)
            {
                if (current.styleSheets.count > 0)
                    styledRoot = current;
                current = current.parent;
            }
            return styledRoot;
        }

        // ----------------------------------------------------------------
        // Footer
        // ----------------------------------------------------------------

        private static VisualElement CreateFooter(
            Action onExport, Action onCheckReadiness, Action onReanalyze, bool allComplete)
        {
            var footer = new VisualElement();

            // Divider
            var divider = new VisualElement();
            divider.AddToClassList(RLDSConstants.Divider.Base);
            divider.style.height = RLDSConstants.BorderWidth.SizeSM;
            footer.Add(divider);

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.justifyContent = Justify.SpaceBetween;
            bar.style.paddingLeft = RLDSConstants.Spacing.Size4XL;
            bar.style.paddingRight = RLDSConstants.Spacing.Size4XL;
            bar.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            bar.style.paddingBottom = RLDSConstants.Spacing.SizeMD;

            // Left: "Made changes outside this tool? Reanalyze project"
            var leftSection = new VisualElement();
            leftSection.style.flexDirection = FlexDirection.Row;
            leftSection.style.alignItems = Align.Center;

            var madeChangesLabel = new Label("Made changes outside this tool?");
            madeChangesLabel.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            madeChangesLabel.style.marginRight = RLDSConstants.Spacing.Size3XS;
            leftSection.Add(madeChangesLabel);

            // Render as a text-link styled Label rather than a TertiarySmall
            // button — same typography as the leading "Made changes outside
            // this tool?" label, only color + click affordance differ.
            var reanalyzeLink = new Label("Reanalyze project");
            reanalyzeLink.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            reanalyzeLink.AddToClassList(HandReadinessStyles.Text.Link);
            reanalyzeLink.RegisterCallback<ClickEvent>(_ => onReanalyze?.Invoke());
            leftSection.Add(reanalyzeLink);

            bar.Add(leftSection);

            // Right: Export + Check readiness buttons
            var rightSection = new VisualElement();
            rightSection.style.flexDirection = FlexDirection.Row;
            rightSection.style.alignItems = Align.Center;

            var exportBtn = new RLDSButton(
                new ActionLinkDescription { Content = new GUIContent("Export as Markdown"), Action = onExport },
                RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Large)
            {
                LeftIcon = HandReadinessIcons.Export,
            }.Build();
            exportBtn.style.marginRight = RLDSConstants.Spacing.SizeSM;
            rightSection.Add(exportBtn);

            // Primary action while work remains is a Secondary "Check readiness" with
            // a refresh icon; it swaps to a Primary "Confirm readiness" once every
            // recommendation is resolved.
            VisualElement checkBtn;
            if (allComplete)
            {
                checkBtn = new RLDSButton(
                    new ActionLinkDescription { Content = new GUIContent("Confirm readiness"), Action = onCheckReadiness },
                    RLDSConstants.ButtonVariant.Primary, RLDSConstants.ButtonSize.Large).Build();
            }
            else
            {
                checkBtn = new RLDSButton(
                    new ActionLinkDescription { Content = new GUIContent("Check readiness"), Action = onCheckReadiness },
                    RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Large)
                {
                    LeftIcon = HandReadinessIcons.CheckReadiness,
                }.Build();
            }
            rightSection.Add(checkBtn);

            bar.Add(rightSection);
            footer.Add(bar);

            return footer;
        }
    }

    /// <summary>
    /// Issue priority levels.
    /// </summary>
    public enum IssuePriority
    {
        High,
        Medium,
        Low
    }

    /// <summary>
    /// Issue complexity levels for AI suggestions.
    /// </summary>
    public enum IssueComplexity
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Data for a single issue.
    /// </summary>
    [System.Serializable]
    public class IssueData
    {
        // Public fields (not properties) so JsonUtility can serialize them
        public string Title;
        public string Description;
        public IssuePriority Priority;
        public bool RequiresAI;
        public bool IsFixed;
        public string TaskUid;
        public IssueCategory Category;

        // Cross-scan persistence: when the AI maps this fresh recommendation to one
        // from the previous scan, this carries the old TaskUid. The merge step uses
        // it to inherit the user's prior IsFixed state.
        public string PreviousTaskUid;

        // AI suggestion-specific fields
        public bool IsAISuggestion;
        public string InitialPrompt;
        public string CurrentImplementation;
        public string HandTrackingAdaptation;
        public List<string> ImplementationSteps;
        public IssueComplexity Complexity;
    }
}
