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
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface.RLDS;
using Meta.XR.AI.AgentBridge;
using Meta.XR.Editor.Id;
using AgentBridgeSettings = Meta.XR.AI.AgentBridge.Settings;
using AgentBridgeUtils = Meta.XR.AI.AgentBridge.Utils;
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using ActionLinkDescription = Meta.XR.Editor.UserInterface.ActionLinkDescription;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Project description input screen — "Tell us about your project."
    /// Only shown on the AI path; the prior CheckType screen routes Standard
    /// users straight to Scanning. Renders an "Active service provider" line
    /// when AgentBridge is on, or the upsell unit when it is off.
    /// </summary>
    public static class ProjectDescriptionScreen
    {
        /// <summary>Creates the project description input screen UI with a text area, file upload, and AI provider state.</summary>
        /// <param name="projectDescription">The initial project description text to display.</param>
        /// <param name="onDescriptionChanged">Callback invoked with the updated text whenever the description changes.</param>
        /// <param name="onUseAIChanged">
        /// Callback invoked with the resolved `useAI` value. Always emits `true` when AgentBridge is
        /// enabled (the user already opted into AI on the prior screen) and `false` when it is
        /// disabled — the upsell unit is shown but the scan falls back to Standard so the user
        /// is not blocked.
        /// </param>
        /// <param name="onBack">Callback invoked when the Back button is clicked.</param>
        /// <param name="onNext">Callback invoked when the Next button is clicked.</param>
        /// <returns>A <see cref="VisualElement"/> containing the complete project description screen.</returns>
        public static VisualElement Create(
            string projectDescription,
            Action<string> onDescriptionChanged,
            Action<bool> onUseAIChanged,
            Action onBack,
            Action onNext)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;
            container.style.justifyContent = Justify.SpaceBetween;

            var scrollView = new ScrollView();
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.style.flexGrow = 1;
            scrollView.style.paddingLeft = HandReadinessResources.WizardSideGutter;
            scrollView.style.paddingRight = HandReadinessResources.WizardSideGutter;

            var innerCol = new VisualElement();
            innerCol.style.flexGrow = 1;
            innerCol.style.justifyContent = Justify.Center;
            innerCol.style.paddingTop = RLDSConstants.Spacing.Size4XL;
            innerCol.style.paddingBottom = RLDSConstants.Spacing.Size4XL;

            var headerSection = new VisualElement();
            headerSection.style.paddingBottom = RLDSConstants.Spacing.Size2XL;

            var heading = new Label("Tell us about your project");
            heading.AddToClassList(RLDSConstants.Typography.Heading2);
            heading.style.marginBottom = RLDSConstants.Spacing.Size4XS;
            heading.style.whiteSpace = WhiteSpace.Normal;
            headerSection.Add(heading);

            var subtitle = new Label(
                "Describe your game or app so the AI can tailor its recommendations to your project.");
            subtitle.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            headerSection.Add(subtitle);

            innerCol.Add(headerSection);

            var textField = new TextField();
            textField.multiline = true;
            textField.value = projectDescription ?? "";
#if UNITY_6000_0_OR_NEWER
            textField.textEdition.placeholder =
                "e.g., \"A multiplayer racing game with dynamic weather and physics\"";
#endif
            textField.AddToClassList(HandReadinessStyles.Description.Textarea);

            textField.RegisterValueChangedCallback(evt =>
            {
                onDescriptionChanged?.Invoke(evt.newValue);
            });

            // Fixed-height scroll box: long descriptions scroll within the field
            // (scrollbar + mouse wheel) rather than growing the whole section.
            var textAreaBox = new ScrollView(ScrollViewMode.Vertical);
            textAreaBox.verticalScrollerVisibility = ScrollerVisibility.Auto;
            textAreaBox.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            textAreaBox.AddToClassList(HandReadinessStyles.Description.TextareaBox);
            // Let the field fill the box height: the scroll content stretches to
            // the viewport when short, and overflows (scrolls) when long.
            textAreaBox.contentContainer.style.flexGrow = 1;
            textAreaBox.Add(textField);

            var contentSection = new VisualElement();
            contentSection.Add(textAreaBox);

            var uploadContainer = new VisualElement();
            uploadContainer.style.marginTop = RLDSConstants.Spacing.SizeXL;

            var uploadHeader = new Label("Upload project files");
            uploadHeader.AddToClassList(RLDSConstants.Typography.Body1Label);
            uploadContainer.Add(uploadHeader);

            var uploadDesc = new Label(
                "Upload .txt, .md, or .json files with technical specs or build info.");
            uploadDesc.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            uploadDesc.style.whiteSpace = WhiteSpace.Normal;
            uploadDesc.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            uploadContainer.Add(uploadDesc);

            var fileCardContainer = new VisualElement();
            fileCardContainer.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            fileCardContainer.style.display = DisplayStyle.None;

            var uploadButton = new UnityEngine.UIElements.Button(() =>
            {
                string path = EditorUtility.OpenFilePanel("Select File", "", "txt,md,json");
                if (string.IsNullOrEmpty(path)) return;

                string fileNameForTelemetry = Path.GetFileName(path);
                string extForTelemetry = (Path.GetExtension(path) ?? "").TrimStart('.').ToLowerInvariant();
                long fileSizeForTelemetry = 0;
                try
                {
                    fileSizeForTelemetry = new FileInfo(path).Length;
                }
                catch { /* file metadata read may fail — that's tracked below as part of the read error */ }

                try
                {
                    string fileContent = File.ReadAllText(path);
                    string fileName = Path.GetFileName(path);
                    long fileSize = new FileInfo(path).Length;

                    string currentText = textField.value;
                    if (!string.IsNullOrEmpty(currentText))
                        currentText += "\n\n--- Uploaded from: " + fileName + " ---\n\n";
                    currentText += fileContent;

                    textField.value = currentText;
                    onDescriptionChanged?.Invoke(currentText);

                    fileCardContainer.Clear();
                    fileCardContainer.Add(CreateFileCard(fileName, fileSize, () =>
                    {
                        textField.value = "";
                        onDescriptionChanged?.Invoke("");
                        fileCardContainer.style.display = DisplayStyle.None;

                        HandReadinessTelemetry.SendEvent(
                            HandReadinessTelemetryConstants.FalcoEventName.ProjectDescriptionFileRemoved,
                            evt =>
                            {
                                evt.SetMetadata(
                                    HandReadinessTelemetryConstants.AnnotationType.FileExtension,
                                    extForTelemetry);
                            });
                    }));
                    fileCardContainer.style.display = DisplayStyle.Flex;

                    HandReadinessTelemetry.SendEvent(
                        HandReadinessTelemetryConstants.FalcoEventName.ProjectDescriptionFileUploaded,
                        evt =>
                        {
                            evt.SetMetadata(HandReadinessTelemetryConstants.AnnotationType.Success, true);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.FileExtension,
                                extForTelemetry);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.FileSizeBytes,
                                fileSize);
                        });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HRT] Failed to read file: {ex.Message}");

                    HandReadinessTelemetry.SendEvent(
                        HandReadinessTelemetryConstants.FalcoEventName.ProjectDescriptionFileUploaded,
                        evt =>
                        {
                            evt.SetMetadata(HandReadinessTelemetryConstants.AnnotationType.Success, false);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.FileExtension,
                                extForTelemetry);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.FileSizeBytes,
                                fileSizeForTelemetry);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.ErrorKind,
                                HandReadinessTelemetryConstants.ErrorKind.ReadFailed);
                            evt.SetMetadata(
                                HandReadinessTelemetryConstants.AnnotationType.ErrorMessage,
                                ex.Message);
                        });
                }
            });
            uploadButton.text = "";
            uploadButton.AddToClassList(HandReadinessStyles.UploadButton.Root);
            uploadButton.style.flexDirection = FlexDirection.Row;
            uploadButton.style.alignSelf = Align.FlexStart;
            uploadButton.style.flexGrow = 0;

            var uploadIcon = HandReadinessResources.CreateTintableIcon("icon_upload",
                RLDSConstants.IconSize.SizeSM - 2);
            uploadIcon.AddToClassList(HandReadinessStyles.UploadButton.Icon);
            uploadIcon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            uploadButton.Add(uploadIcon);
            var uploadBtnLabel = new Label("Upload file");
            uploadBtnLabel.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            uploadButton.Add(uploadBtnLabel);

            uploadContainer.Add(uploadButton);
            uploadContainer.Add(fileCardContainer);
            contentSection.Add(uploadContainer);
            innerCol.Add(contentSection);

            // ---- AI provider section ----
            // When AgentBridge is enabled and a provider is initialized, surface the
            // active provider so the user can confirm which service the scan will
            // hit. When AgentBridge is disabled, replace this slot with the upsell
            // unit (badge + heading + description + "Open project settings" button)
            // and force the scan to fall back to Standard.
            var aiSection = new VisualElement();
            aiSection.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            aiSection.style.paddingBottom = RLDSConstants.Spacing.SizeMD;

            if (AgentBridgeSettings.IsEnabled)
            {
                BuildActiveProviderRow(aiSection);
                onUseAIChanged?.Invoke(true);
            }
            else
            {
                BuildAiUpsellUnit(aiSection, onUseAIChanged);
            }
            innerCol.Add(aiSection);

            scrollView.Add(innerCol);
            container.Add(scrollView);

            var backButton = HandReadinessResources.CreateBackButton("Back", onBack);
            var nextButton = HandReadinessResources.CreateNextButton("Next", onNext);
            HandReadinessResources.SetButtonEnabled(nextButton, !string.IsNullOrWhiteSpace(projectDescription));

            textField.RegisterValueChangedCallback(evt =>
            {
                HandReadinessResources.SetButtonEnabled(nextButton, !string.IsNullOrWhiteSpace(evt.newValue));
            });

            container.Add(HandReadinessResources.CreateFooter(backButton, nextButton));

            return container;
        }

        /// <summary>
        /// Renders an "Active service provider: {name}" line with a green check icon
        /// when a provider is initialized. No-op when no provider is set, since the
        /// AI scan would still fall through to AgentBridge's provider-resolution at
        /// runtime — we just don't have a name to show yet.
        /// </summary>
        private static void BuildActiveProviderRow(VisualElement aiSection)
        {
            AgentBridgeAPI.EnsureServiceInitialized();
            var providerName = AgentBridgeAPI.GetCurrentServiceName();
            if (string.IsNullOrEmpty(providerName) || providerName == "None") return;

            var providerRow = new VisualElement();
            providerRow.style.flexDirection = FlexDirection.Row;
            providerRow.style.alignItems = Align.Center;

            var providerIcon = HandReadinessResources.CreateTintableIcon(
                "icon_check_circle", RLDSConstants.IconSize.SizeXS);
            providerIcon.AddToClassList(HandReadinessStyles.Icon.ThemedPositive);
            providerIcon.style.marginRight = RLDSConstants.Spacing.Size2XS;
            providerRow.Add(providerIcon);

            var providerLabel = new Label($"Active service provider: {providerName}");
            providerLabel.AddToClassList(RLDSConstants.Typography.Tiny);
            providerRow.Add(providerLabel);

            aiSection.Add(providerRow);
        }

        /// <summary>
        /// Renders the AI upsell unit shown when the AgentBridge master toggle
        /// is off: a Notification-pill "New" badge, bold headline, supporting
        /// description, and an "Open project settings" button that jumps to the
        /// AgentBridge preferences. Forces the parent scan to skip the AI phase
        /// via `onUseAIChanged(false)` so a user who proceeds anyway runs
        /// Standard rather than hitting a failed AI scan.
        /// </summary>
        private static void BuildAiUpsellUnit(VisualElement aiSection, Action<bool> onUseAIChanged)
        {
            // The AI scan is unreachable without an enabled AgentBridge — make the
            // downstream scan fall back to Standard so the user isn't blocked.
            onUseAIChanged?.Invoke(false);

            aiSection.style.flexDirection = FlexDirection.Column;
            aiSection.style.alignItems = Align.FlexStart;

            // "New" BadgePill with the star icon and Notification/Info variant.
            var newBadge = new VisualElement();
            newBadge.AddToClassList(RLDSConstants.BadgePill.Base);
            newBadge.AddToClassList(RLDSConstants.BadgePill.Info);
            newBadge.style.marginBottom = RLDSConstants.Spacing.SizeXS;

            var newIcon = HandReadinessResources.CreateTintableIcon(
                "icon_new_star", RLDSConstants.IconSize.SizeXS);
            newIcon.AddToClassList(HandReadinessStyles.Icon.Themed);
            newIcon.AddToClassList(RLDSConstants.BadgePill.Icon);
            newBadge.Add(newIcon);

            var newLabel = new Label("New");
            newLabel.AddToClassList(RLDSConstants.BadgePill.Label);
            newBadge.Add(newLabel);
            aiSection.Add(newBadge);

            var heading = new Label("AI-powered recommendations now available");
            heading.AddToClassList(RLDSConstants.Typography.Body1Label);
            aiSection.Add(heading);

            var description = new Label(
                "Enable AI recommendations to get intelligent suggestions for improving " +
                "your project. Choose your preferred AI provider (Claude Code, ChatGPT, " +
                "and more) in Project Settings.");
            description.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = RLDSConstants.Spacing.SizeSM;
            aiSection.Add(description);

            var openSettingsBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent("Open project settings"),
                    Action = () =>
                    {
                        HandReadinessTelemetry.SendEvent(
                            HandReadinessTelemetryConstants.FalcoEventName.AgentBridgeSetupClicked,
                            isEssential: true);
                        AgentBridgeUtils.ToolDescriptor.OnClickDelegate?.Invoke(Origins.ProjectSettings);
                    },
                },
                RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Small).Build();
            openSettingsBtn.style.alignSelf = Align.FlexStart;
            aiSection.Add(openSettingsBtn);
        }

        private static VisualElement CreateFileCard(string fileName, long fileSizeBytes, Action onRemove)
        {
            var card = new VisualElement();
            card.AddToClassList(HandReadinessStyles.FileCard.Root);
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;

            var fileIcon = HandReadinessResources.CreateTintableIcon("icon_file", 20);
            fileIcon.AddToClassList(HandReadinessStyles.FileCard.Icon);
            fileIcon.style.marginRight = RLDSConstants.Spacing.SizeXS;
            card.Add(fileIcon);

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            var nameLabel = new Label(fileName);
            nameLabel.AddToClassList(RLDSConstants.Typography.Body2SmallLabel);
            textCol.Add(nameLabel);
            var sizeLabel = new Label(FormatFileSize(fileSizeBytes));
            sizeLabel.AddToClassList(RLDSConstants.Typography.Body2SupportingText);
            textCol.Add(sizeLabel);
            card.Add(textCol);

            var removeBtn = new UnityEngine.UIElements.Button(onRemove);
            removeBtn.text = "\u2715";
            removeBtn.AddToClassList(HandReadinessStyles.FileCard.RemoveButton);
            card.Add(removeBtn);

            return card;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_000_000)
                return $"{bytes / 1_000_000.0:F1} MB";
            if (bytes >= 1_000)
                return $"{bytes / 1_000.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
