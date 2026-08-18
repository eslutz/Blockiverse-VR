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
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;
using Meta.HandReadinessTool.Editor.UI;
using TelemetryConstants = Meta.HandReadinessTool.Editor.HandReadinessTelemetryConstants;
using Debug = UnityEngine.Debug;
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using ActionLinkDescription = Meta.XR.Editor.UserInterface.ActionLinkDescription;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Issue details popup. Shows problem, current implementation, recommendation
    /// card, steps, and action buttons.
    /// </summary>
    public class IssueDetailsPopup : EditorWindow
    {
        private const string PrefKeyIssue = "HandReadiness_PopupIssue";

        private IssueData _issue;
        private List<IssueData> _orderedIssues;
        private Action _onDismiss;

        // Telemetry — popup-lifetime tracking so OnDestroy can emit duration + markedComplete.
        private Stopwatch _openStopwatch;
        private bool _markedComplete;

        /// <summary>
        /// Opens the issue details popup window for the specified issue.
        /// </summary>
        /// <param name="issue">The issue data to display in the popup.</param>
        /// <param name="orderedIssues">
        /// All issues in the ResultsScreen's display order. When supplied (and the issue is found
        /// in it) the popup adds Back / Next-style navigation between issues so the user can move
        /// through the list without closing and reopening. Null disables the nav row.
        /// </param>
        /// <param name="onDismiss">Optional callback invoked when the popup window is closed.</param>
        public static void Show(IssueData issue, List<IssueData> orderedIssues = null, Action onDismiss = null)
        {
            // Open as a normal (dockable) EditorWindow so the user can dock/tab it
            // into their layout.
            var tabTitle = Truncate(issue.Title, 200);
            var window = GetWindow<IssueDetailsPopup>(utility: false, title: tabTitle, focus: true);
            window.titleContent = new GUIContent(tabTitle);
            // Roughly matches the Figma detail view (~776pt tall).
            window.minSize = new Vector2(650, 760);
            window.maxSize = new Vector2(900, 880);
            window._issue = issue;
            window._orderedIssues = orderedIssues;
            window._onDismiss = onDismiss;
            window._openStopwatch = Stopwatch.StartNew();
            window._markedComplete = false;

            try { EditorPrefs.SetString(PrefKeyIssue, JsonUtility.ToJson(issue)); }
            catch { }

            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.IssueDetailsOpened,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.IssueUid, issue.TaskUid ?? "");
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Priority, PriorityName(issue.Priority));
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Category, CategoryName(issue.Category));
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Source, TelemetryConstants.Source.RowButton);
                });

            window.Show();
            window.CreateContent();
        }

        private static string PriorityName(IssuePriority p) => p switch
        {
            IssuePriority.High => TelemetryConstants.Priority.High,
            IssuePriority.Medium => TelemetryConstants.Priority.Medium,
            _ => TelemetryConstants.Priority.Low,
        };

        private static string CategoryName(IssueCategory c) => c switch
        {
            IssueCategory.AI => TelemetryConstants.IssueCategoryName.Ai,
            IssueCategory.Manual => TelemetryConstants.IssueCategoryName.Manual,
            _ => TelemetryConstants.IssueCategoryName.Automation,
        };

        /// <summary>
        /// Navigates the popup to a sibling issue in `_orderedIssues`. `direction` is
        /// -1 for Back, +1 for Next. Refreshes the title bar and re-renders.
        /// </summary>
        private void Navigate(int direction)
        {
            if (_orderedIssues == null || _orderedIssues.Count == 0) return;
            int idx = _orderedIssues.IndexOf(_issue);
            if (idx < 0) return;
            int target = idx + direction;
            if (target < 0 || target >= _orderedIssues.Count) return;
            var fromUid = _issue?.TaskUid ?? "";
            _issue = _orderedIssues[target];
            // Tab label tracks current issue, truncated per spec.
            titleContent = new GUIContent(Truncate(_issue.Title, 200));
            try { EditorPrefs.SetString(PrefKeyIssue, JsonUtility.ToJson(_issue)); }
            catch { }

            var toUid = _issue?.TaskUid ?? "";
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.IssueDetailsNavigated,
                evt =>
                {
                    evt.SetMetadata(
                        TelemetryConstants.AnnotationType.Direction,
                        direction > 0 ? TelemetryConstants.Direction.Next : TelemetryConstants.Direction.Back);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.FromIssueUid, fromUid);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.ToIssueUid, toUid);
                });

            CreateContent();
        }

        private void CreateGUI()
        {
            if (_issue == null)
            {
                var json = EditorPrefs.GetString(PrefKeyIssue, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { _issue = JsonUtility.FromJson<IssueData>(json); }
                    catch { }
                }
            }
            if (_issue != null) CreateContent();
        }

        private void CreateContent()
        {
            var root = rootVisualElement;
            root.Clear();

            var lightMode = !EditorGUIUtility.isProSkin;
            var styleSheet = RLDSUtils.LoadStyleSheet(lightMode);
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                root.styleSheets.Add(styleSheet);

            var hrtStyleSheet = UI.HandReadinessStyles.LoadStyleSheet();
            if (hrtStyleSheet != null && !root.styleSheets.Contains(hrtStyleSheet))
                root.styleSheets.Add(hrtStyleSheet);

            root.AddToClassList(RLDSConstants.Surface.Secondary);
            // Sticky footer: the scroll body grows to fill and the footer is a
            // non-shrinking bar pinned to the bottom of the window.
            root.style.flexGrow = 1;

            // ---- Scrollable content ----
            var scrollView = new ScrollView();
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.style.flexGrow = 1;
            scrollView.style.paddingLeft = RLDSConstants.Spacing.SizeLG;
            scrollView.style.paddingRight = RLDSConstants.Spacing.SizeLG;
            scrollView.style.paddingTop = RLDSConstants.Spacing.SizeLG;

            var title = new Label(_issue.Title);
            title.AddToClassList(RLDSConstants.Typography.Heading2);
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            scrollView.Add(title);

            // Badges row — priority dot + type badge
            var badgesRow = new VisualElement();
            badgesRow.style.flexDirection = FlexDirection.Row;
            badgesRow.style.alignItems = Align.Center;
            badgesRow.style.marginBottom = RLDSConstants.Spacing.SizeLG;

            // Priority — wrapped in RLDS BadgeTag matching the row in ResultsScreen.
            string prioVariant = _issue.Priority switch
            {
                IssuePriority.High => RLDSConstants.BadgeTag.Negative,
                IssuePriority.Medium => RLDSConstants.BadgeTag.Warning,
                _ => RLDSConstants.BadgeTag.Neutral,
            };
            var prioBadge = new VisualElement();
            prioBadge.AddToClassList(RLDSConstants.BadgeTag.Base);
            prioBadge.AddToClassList(prioVariant);
            prioBadge.style.marginRight = RLDSConstants.Spacing.SizeXS;
            prioBadge.style.flexDirection = FlexDirection.Row;
            prioBadge.style.alignItems = Align.Center;

            (string prioIconName, string prioIconClass) = _issue.Priority switch
            {
                IssuePriority.High => ("icon_priority_high", UI.HandReadinessStyles.Priority.High),
                IssuePriority.Medium => ("icon_priority_medium", UI.HandReadinessStyles.Priority.Medium),
                _ => ("icon_priority_low", UI.HandReadinessStyles.Priority.Low),
            };
            var prioIcon = UI.HandReadinessResources.CreateTintableIcon(prioIconName, RLDSConstants.IconSize.SizeXS);
            prioIcon.AddToClassList(prioIconClass);
            prioIcon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            prioBadge.Add(prioIcon);

            var prioBadgeLabel = new Label(_issue.Priority.ToString());
            prioBadgeLabel.AddToClassList(RLDSConstants.BadgeTag.Label);
            prioBadge.Add(prioBadgeLabel);
            badgesRow.Add(prioBadge);

            // Vertical 1x20 divider between BadgeTag and BadgePill. Reuses
            // .rlds-divider for the theme-aware background; .hrt-vertical-divider
            // overrides the geometry to a vertical rule.
            var verticalDivider = new VisualElement();
            verticalDivider.AddToClassList(RLDSConstants.Divider.Base);
            verticalDivider.AddToClassList(UI.HandReadinessStyles.Divider.Vertical);
            verticalDivider.style.marginRight = RLDSConstants.Spacing.SizeXS;
            badgesRow.Add(verticalDivider);

            // Type badge — shared styling with ResultsScreen (RLDS BadgePill variants).
            string typeText;
            string typeVariant;
            switch (_issue.Category)
            {
                case IssueCategory.AI:
                    typeText = "AI assisted";
                    typeVariant = RLDSConstants.BadgePill.Info;
                    break;
                case IssueCategory.Automation:
                    typeText = "Automated";
                    typeVariant = RLDSConstants.BadgePill.Positive;
                    break;
                default:
                    typeText = "Manual";
                    typeVariant = RLDSConstants.BadgePill.Warning;
                    break;
            }
            var typeBadge = new VisualElement();
            typeBadge.AddToClassList(RLDSConstants.BadgePill.Base);
            typeBadge.AddToClassList(typeVariant);

            var typeBadgeLabel = new Label(typeText);
            typeBadgeLabel.AddToClassList(RLDSConstants.BadgePill.Label);
            typeBadge.Add(typeBadgeLabel);
            badgesRow.Add(typeBadge);

            scrollView.Add(badgesRow);

            // ---- Problem section ----
            if (!string.IsNullOrEmpty(_issue.Description))
            {
                scrollView.Add(CreateProblemSectionHeader("Problem"));
                scrollView.Add(CreateBodyText(_issue.Description));
            }

            // ---- Current implementation section ----
            if (!string.IsNullOrEmpty(_issue.CurrentImplementation))
            {
                scrollView.Add(CreateProblemSectionHeader("Current implementation"));
                scrollView.Add(CreateBodyText(_issue.CurrentImplementation));
            }

            // ---- Divider before recommendation ----
            if (!string.IsNullOrEmpty(_issue.HandTrackingAdaptation))
            {
                var divider = new VisualElement();
                divider.AddToClassList(RLDSConstants.Divider.Base);
                divider.style.marginTop = RLDSConstants.Spacing.SizeSM;
                divider.style.marginBottom = RLDSConstants.Spacing.SizeMD;
                scrollView.Add(divider);

                var recoHeader = new VisualElement();
                recoHeader.style.flexDirection = FlexDirection.Row;
                recoHeader.style.alignItems = Align.Center;
                recoHeader.style.marginBottom = RLDSConstants.Spacing.Size3XS;

                var recoIcon = UI.HandReadinessResources.CreateTintableIcon("icon_editor",
                    RLDSConstants.IconSize.SizeSM);
                recoIcon.AddToClassList(UI.HandReadinessStyles.Icon.Themed);
                recoIcon.style.marginRight = RLDSConstants.Spacing.SizeXS;
                recoHeader.Add(recoIcon);

                var recoTitle = new Label("Recommendation");
                recoTitle.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
                recoTitle.AddToClassList(UI.HandReadinessStyles.Text.Primary);
                recoHeader.Add(recoTitle);
                scrollView.Add(recoHeader);

                var recoBody = new Label(_issue.HandTrackingAdaptation);
                recoBody.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
                recoBody.AddToClassList(UI.HandReadinessStyles.Text.Primary);
                recoBody.style.whiteSpace = WhiteSpace.Normal;
                recoBody.style.marginBottom = RLDSConstants.Spacing.SizeLG;
                scrollView.Add(recoBody);

                // Steps section
                if (_issue.ImplementationSteps != null && _issue.ImplementationSteps.Count > 0)
                {
                    var stepsHeader = new VisualElement();
                    stepsHeader.style.flexDirection = FlexDirection.Row;
                    stepsHeader.style.alignItems = Align.Center;
                    stepsHeader.style.marginBottom = RLDSConstants.Spacing.Size3XS;

                    var stepsIcon = UI.HandReadinessResources.CreateTintableIcon("icon_bullet_list",
                        RLDSConstants.IconSize.SizeSM);
                    stepsIcon.AddToClassList(UI.HandReadinessStyles.Icon.Themed);
                    stepsIcon.style.marginRight = RLDSConstants.Spacing.Size3XS;
                    stepsHeader.Add(stepsIcon);

                    var stepsTitle = new Label("Steps");
                    stepsTitle.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
                    stepsTitle.AddToClassList(UI.HandReadinessStyles.Text.Primary);
                    stepsHeader.Add(stepsTitle);
                    scrollView.Add(stepsHeader);

                    for (int i = 0; i < _issue.ImplementationSteps.Count; i++)
                    {
                        var stepRow = new VisualElement();
                        stepRow.style.flexDirection = FlexDirection.Row;
                        stepRow.style.alignItems = Align.FlexStart;
                        stepRow.style.marginBottom = 2;

                        var stepNum = new Label($"{i + 1}.");
                        stepNum.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
                        stepNum.AddToClassList(UI.HandReadinessStyles.Text.Primary);
                        stepNum.style.width = 18;
                        stepNum.style.flexShrink = 0;
                        stepRow.Add(stepNum);

                        var stepText = new Label(_issue.ImplementationSteps[i]);
                        stepText.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
                        stepText.AddToClassList(UI.HandReadinessStyles.Text.Primary);
                        stepText.style.whiteSpace = WhiteSpace.Normal;
                        stepText.style.flexGrow = 1;
                        stepRow.Add(stepText);

                        scrollView.Add(stepRow);
                    }
                }
            }

            root.Add(scrollView);

            // Back / Next navigation between issues, anchored below the
            // scrollable body and above the action footer.
            if (_orderedIssues != null && _orderedIssues.Count > 1)
            {
                int currentIdx = _orderedIssues.IndexOf(_issue);
                if (currentIdx >= 0)
                {
                    var navWrap = new VisualElement();
                    // Padding matches the action footer below (SizeXL = 24px),
                    // so the Back/Next nav row aligns with the action buttons.
                    navWrap.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
                    navWrap.style.paddingRight = RLDSConstants.Spacing.SizeXL;
                    // Explicit gap above the nav row so short-content popups don't
                    // crowd the nav-footer up against the last body section.
                    navWrap.style.marginTop = RLDSConstants.Spacing.SizeMD;

                    var navRow = new VisualElement();
                    navRow.style.flexDirection = FlexDirection.Row;
                    navRow.style.justifyContent = Justify.SpaceBetween;
                    navRow.style.alignItems = Align.Center;
                    navRow.style.marginTop = RLDSConstants.Spacing.SizeSM;
                    navRow.style.marginBottom = RLDSConstants.Spacing.SizeSM;

                    var backNavBtn = new UnityEngine.UIElements.Button(() => Navigate(-1));
                    backNavBtn.text = "← Back";
                    backNavBtn.AddToClassList(RLDSConstants.Button.TertiarySmall);
                    backNavBtn.SetEnabled(currentIdx > 0);
                    navRow.Add(backNavBtn);

                    string nextTitle = currentIdx + 1 < _orderedIssues.Count
                        ? _orderedIssues[currentIdx + 1].Title
                        : null;
                    var nextNavBtn = new UnityEngine.UIElements.Button(() => Navigate(1));
                    nextNavBtn.text = nextTitle != null
                        ? $"Next: {Truncate(nextTitle, 40)} →"
                        : "Next →";
                    nextNavBtn.AddToClassList(RLDSConstants.Button.TertiarySmall);
                    nextNavBtn.SetEnabled(nextTitle != null);
                    navRow.Add(nextNavBtn);

                    navWrap.Add(navRow);
                    root.Add(navWrap);
                }
            }

            // ---- Footer ----
            var footer = new VisualElement();
            footer.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
            footer.style.paddingRight = RLDSConstants.Spacing.SizeXL;
            footer.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            footer.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            // Fixed bottom bar: never let the scroll body's flex-grow compress it
            // (compression clipped the wrapped helper text).
            footer.style.flexShrink = 0;

            // Divider at top of footer
            var footerDivider = new VisualElement();
            footerDivider.AddToClassList(RLDSConstants.Divider.Base);
            footerDivider.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            footer.Add(footerDivider);

            // Helper text — secondary supporting style.
            var helperText = new Label(
                "Copy these details to share with your AI assistant. When you're done implementing, " +
                "mark this complete and we'll verify your changes.");
            helperText.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            helperText.style.whiteSpace = WhiteSpace.Normal;
            helperText.style.marginTop = RLDSConstants.Spacing.SizeXS;
            // Reserve the wrapped two-line height so the footer doesn't clip the
            // closing line (UIElements under-measures a freshly-added wrapping Label).
            helperText.style.minHeight = 40;

            // Button row
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginBottom = RLDSConstants.Spacing.SizeXS;

            var copyBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent("Copy to clipboard"),
                    Action = CopyToClipboard,
                },
                RLDSConstants.ButtonVariant.Secondary,
                RLDSConstants.ButtonSize.Large)
            {
                LeftIcon = Meta.XR.Editor.UserInterface.UIStyles.Contents.CopyIcon,
            }.Build();
            copyBtn.style.flexGrow = 1;
            copyBtn.style.flexBasis = 0;
            copyBtn.style.justifyContent = Justify.Center;
            copyBtn.style.marginRight = RLDSConstants.Spacing.SizeSM;
            buttonRow.Add(copyBtn);

            var completeBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent("\u2713 Mark as complete"),
                    Action = OnMarkComplete,
                },
                RLDSConstants.ButtonVariant.Primary,
                RLDSConstants.ButtonSize.Large).Build();
            completeBtn.style.flexGrow = 1;
            completeBtn.style.flexBasis = 0;
            completeBtn.style.justifyContent = Justify.Center;
            buttonRow.Add(completeBtn);

            footer.Add(buttonRow);
            footer.Add(helperText);
            root.Add(footer);
        }

        private void OnMarkComplete()
        {
            _issue.IsFixed = true;
            _markedComplete = true;
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.IssueMarkedComplete,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.IssueUid, _issue.TaskUid ?? "");
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Priority, PriorityName(_issue.Priority));
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Category, CategoryName(_issue.Category));
                },
                isEssential: true);
            Close();
        }

        /// <summary>Truncate a string to `max` characters with an ellipsis.</summary>
        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private static VisualElement CreateProblemSectionHeader(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = RLDSConstants.Spacing.SizeMD;
            row.style.marginBottom = RLDSConstants.Spacing.Size3XS;

            var label = new Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            row.Add(label);
            return row;
        }

        // Used by Problem and Current implementation bodies — Recommendation and
        // Steps bodies override the body2-supporting-text colour to primary inline.
        private static Label CreateBodyText(string text)
        {
            var label = new Label(text);
            label.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = RLDSConstants.Spacing.SizeXS;
            return label;
        }

        private void CopyToClipboard()
        {
            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.IssueCopiedToClipboard,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.IssueUid, _issue.TaskUid ?? "");
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Priority, PriorityName(_issue.Priority));
                    evt.SetMetadata(TelemetryConstants.AnnotationType.Category, CategoryName(_issue.Category));
                });

            EditorGUIUtility.systemCopyBuffer = BuildClipboardPayload(_issue);
            ShowNotification(new GUIContent("Copied to clipboard!"), 1.5f);
        }

        /// <summary>
        /// Builds the Markdown clipboard payload for an issue. Shared by the details
        /// popup and the results-row "Copy to clipboard" overflow action.
        /// </summary>
        internal static string BuildClipboardPayload(IssueData issue)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {issue.Title}");
            sb.AppendLine();
            sb.AppendLine($"**Priority:** {issue.Priority}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(issue.Description))
            {
                sb.AppendLine("## Problem");
                sb.AppendLine(issue.Description);
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(issue.CurrentImplementation))
            {
                sb.AppendLine("## Current Implementation");
                sb.AppendLine(issue.CurrentImplementation);
                sb.AppendLine();
            }
            if (!string.IsNullOrEmpty(issue.HandTrackingAdaptation))
            {
                sb.AppendLine("## Recommendation");
                sb.AppendLine(issue.HandTrackingAdaptation);
                sb.AppendLine();
            }
            if (issue.ImplementationSteps != null && issue.ImplementationSteps.Count > 0)
            {
                sb.AppendLine("## Steps");
                for (int i = 0; i < issue.ImplementationSteps.Count; i++)
                    sb.AppendLine($"{i + 1}. {issue.ImplementationSteps[i]}");
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine($"Please help me implement this change for {HandReadinessConstants.DeviceName}.");
            return sb.ToString();
        }

        private void OnDestroy()
        {
            long duration = _openStopwatch?.ElapsedMilliseconds ?? 0;
            _openStopwatch?.Stop();
            var issueUid = _issue?.TaskUid ?? "";
            var marked = _markedComplete;

            HandReadinessTelemetry.SendEvent(
                TelemetryConstants.FalcoEventName.IssueDetailsClosed,
                evt =>
                {
                    evt.SetMetadata(TelemetryConstants.AnnotationType.IssueUid, issueUid);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.MarkedComplete, marked);
                    evt.SetMetadata(TelemetryConstants.AnnotationType.DurationMs, duration);
                });

            EditorPrefs.DeleteKey(PrefKeyIssue);
            _onDismiss?.Invoke();
        }
    }
}
