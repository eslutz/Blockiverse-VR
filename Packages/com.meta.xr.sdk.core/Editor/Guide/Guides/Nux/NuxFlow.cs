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

using System.Collections.Generic;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using Meta.XR.Guides.Editor.Welcome;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using UIStyles = Meta.XR.Editor.UserInterface.Styles;
using RLDSColors = Meta.XR.Editor.UserInterface.RLDS.Styles.Colors;
using Label = UnityEngine.UIElements.Label;

namespace Meta.XR.Guides.Editor.Nux
{
    [GuideItems]
    internal class NuxFlow : GuidedSetup
    {
        private static GuideWindow _window;
        private static NuxFlow _instance;

        #region Settings

        private UserString _selectedSkillLevel;

        private UserString SelectedSkillLevel => _selectedSkillLevel ??= new UserString()
        {
            Default = null,
            Label = "SkillLevel",
            Uid = "NuxSkillLevel",
            SendTelemetry = true,
            Owner = this
        };

        private UserString _selectedRole;

        private UserString SelectedRole => _selectedRole ??= new UserString()
        {
            Default = null,
            Label = "Role",
            Uid = "NuxRole",
            SendTelemetry = true,
            Owner = this
        };

        private static UserBool _nuxCompleted;

        private static UserBool NuxCompleted => _nuxCompleted ??= new UserBool()
        {
            Default = false,
            Label = "NuxCompleted",
            Uid = "NuxFlowCompleted",
            SendTelemetry = true,
            Owner = null
        };

        private static readonly SessionInt _currentStepIndex = new()
        {
            Owner = null,
            Uid = "NuxCurrentStep",
            SendTelemetry = false,
            Default = 0
        };

        #endregion

        #region UI State

        private const float RoleCardWidth = 185;
        private const float RoleCardHeight = 83;
        private const int RoleGridColumns = 3;

        private ProgressStepper _progressStepper;
        private VisualElement _stepContainer;
        private VisualElement _backButtonElement;
        private VisualElement _nextButtonElement;
        private readonly Dictionary<string, RoleCard> _roleCards = new();

        private int CurrentStep
        {
            get => _currentStepIndex.Value;
            set => _currentStepIndex.SetValue(value);
        }

        #endregion

        #region Window Creation

        internal override GuideWindow CreateWindow()
        {
            if (_window != null) return _window;

            var options = new GuideWindow.GuideOptions(GuideWindow.DefaultOptions)
            {
                ShowCloseButton = false,
                ShowDontShowAgainOption = false,
                ShowAsUtility = true,
                UseUIToolkit = true,
                MinWindowWidth = NuxSettings.WindowWidth,
                MaxWindowWidth = NuxSettings.WindowWidth,
                MinWindowHeight = NuxSettings.WindowHeight,
                MaxWindowHeight = NuxSettings.WindowHeight,
            };
            _window = Guide.Create("Meta XR SDK", null, this, options);
            return _window;
        }

        [Init]
        private void InitializeWindow(GuideWindow window)
        {
            _window = window;
            _instance = this;
            _window.DrawBefore = OnDrawBefore;
            _window.DrawHeader = () => { };
            _window.DrawAfter = OnDrawAfter;
        }

        #endregion

        #region UIToolkit Layout

        private void OnDrawBefore()
        {
            var root = _window.RootContainer;

            _progressStepper = new ProgressStepper(NuxSettings.StepCount, CurrentStep);
            var stepperElement = _progressStepper.Build();
            stepperElement.style.width = 540;
            stepperElement.style.alignSelf = Align.Center;
            stepperElement.style.marginTop = RLDSConstants.Spacing.SizeXL;
            stepperElement.style.marginBottom = 0;
            root.Add(stepperElement);
        }

        private void OnDrawAfter()
        {
            var root = _window.RootContainer;
            // Tag the wizard so skill/role selectors report the NUX surface as their navigation path.
            RLDSTelemetry.SetScope(root, Origins.GuidedSetup, ((IIdentified)this).Id);

            _stepContainer = _window.ItemContainer;
            _stepContainer.style.paddingLeft = RLDSConstants.Spacing.Size5XL;
            _stepContainer.style.paddingRight = RLDSConstants.Spacing.Size5XL;
            _stepContainer.style.paddingBottom = 0;

            var footer = BuildFooter();
            root.Add(footer);

            GoToStep(CurrentStep);
        }

        [GuideItems]
        private List<IUserInterfaceItem> GetItems()
        {
            return new List<IUserInterfaceItem>();
        }

        #endregion

        #region Step Navigation

        private void GoToStep(int stepIndex)
        {
            stepIndex = Mathf.Clamp(stepIndex, 0, NuxSettings.StepCount - 1);
            CurrentStep = stepIndex;

            _progressStepper?.SetCurrentStep(stepIndex);

            if (_stepContainer == null) return;
            _stepContainer.style.paddingTop = stepIndex == 0
                ? RLDSConstants.Spacing.Size4XL
                : 0;
            _stepContainer.Clear();

            var stepContent = BuildStepContent((NuxSettings.StepId)stepIndex);
            if (stepContent != null)
            {
                _stepContainer.Add(stepContent);
            }

            UpdateFooter(stepIndex);
        }

        private void OnNext()
        {
            if (CurrentStep >= NuxSettings.StepCount - 1)
            {
                OnComplete();
                return;
            }

            GoToStep(CurrentStep + 1);
        }

        private void OnBack()
        {
            if (CurrentStep > 0)
            {
                GoToStep(CurrentStep - 1);
            }
        }

        private void OnComplete()
        {
            _window.DontShowAgain.SetValue(true);
            NuxCompleted.SetValue(true);
            _currentStepIndex.SetValue(0);
            _window.Close();

            EditorApplication.delayCall += () =>
                WelcomeWindow.Show(Origins.GuidedSetup);
        }

        #endregion

        #region Step Content

        private VisualElement BuildStepContent(NuxSettings.StepId stepId)
        {
            return stepId switch
            {
                NuxSettings.StepId.Intro => BuildIntroStep(),
                NuxSettings.StepId.SkillLevel => BuildSkillLevelStep(),
                NuxSettings.StepId.Role => BuildRoleStep(),
                _ => new VisualElement()
            };
        }

        private VisualElement BuildIntroStep()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.Flexbox.Column);
            container.AddToClassList(RLDSConstants.Flexbox.AlignCenter);

            var cover = new CoverImage
            {
                CoverIcon = UIStyles.Contents.SdkCoverIcon
            };
            var coverElement = cover.Build();
            coverElement.style.width = 896;
            UIStyles.Contents.CoverBg.RegisterToImageLoaded(tex =>
                coverElement.style.backgroundImage = new StyleBackground(tex as Texture2D));
            container.Add(coverElement);

            var coverTitle = new Label(NuxSettings.Content.IntroTitle);
            coverTitle.AddToClassList(RLDSConstants.Typography.Heading1);
            cover.ContentArea.Add(coverTitle);

            var coverSubtitle = new Label(NuxSettings.Content.IntroSubtitle);
            coverSubtitle.AddToClassList(RLDSConstants.Typography.Body1Text);
            coverSubtitle.AddToClassList(RLDSConstants.Utilities.MarginTopXS);
            cover.ContentArea.Add(coverSubtitle);

            // What's included section
            var section = new VisualElement();
            section.style.alignSelf = Align.Stretch;
            section.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            section.style.paddingRight = RLDSConstants.Spacing.Size3XL;

            var sectionHeader = new Label(NuxSettings.Content.WhatsIncludedHeader);
            sectionHeader.AddToClassList(RLDSConstants.Typography.Heading2);
            sectionHeader.style.marginTop = RLDSConstants.Spacing.Size4XL;
            sectionHeader.style.marginBottom = RLDSConstants.Spacing.SizeXL;
            section.Add(sectionHeader);

            foreach (var item in NuxSettings.Content.WhatsIncludedItems)
            {
                var bulletItem = new VisualElement();
                bulletItem.AddToClassList(RLDSConstants.Flexbox.Row);
                bulletItem.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
                bulletItem.style.marginBottom = RLDSConstants.Spacing.SizeMD;

                var icon = new VisualElement();
                icon.style.width = 24;
                icon.style.height = 24;
                icon.style.flexShrink = 0;
                icon.style.marginRight = RLDSConstants.Spacing.SizeSM;
                var featureIcon = GetFeatureIcon(item.IconName);
                if (featureIcon != null)
                {
                    featureIcon.RegisterToImageLoaded(tex =>
                    {
                        icon.style.backgroundImage = new StyleBackground(tex as Texture2D);
                        // Theme-adaptive RLDS icon tint (dark-grey in light theme, white in dark).
                        icon.style.unityBackgroundImageTintColor = EditorGUIUtility.isProSkin
                            ? RLDSColors.IconSecondary
                            : RLDSColors.LightIconSecondary;
                    });
                }
                bulletItem.Add(icon);

                var text = new Label(item.Text);
                text.AddToClassList(RLDSConstants.Typography.Body1Text);
                text.style.flexShrink = 1;
                text.style.whiteSpace = WhiteSpace.Normal;
                bulletItem.Add(text);

                section.Add(bulletItem);
            }

            container.Add(section);

            return container;
        }

        private VisualElement BuildSkillLevelStep()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.Flexbox.Column);
            container.AddToClassList(RLDSConstants.Flexbox.AlignCenter);

            // Title lockup
            var titleLockup = BuildTitleLockup(
                NuxSettings.Content.SkillLevelTitle,
                NuxSettings.Content.SkillLevelSubtitle);
            container.Add(titleLockup);

            // Skill level selectors
            var selectors = new List<SkillLevelSelector>();
            foreach (var def in NuxSettings.SkillLevels.All)
            {
                selectors.Add(new SkillLevelSelector(def.Id, def.Label, def.Description));
            }

            var group = new SkillLevelSelectorGroup(selectors, OnSkillLevelSelected, RLDSConstants.Spacing.SizeSM);
            var groupElement = group.Build();
            groupElement.style.marginTop = RLDSConstants.Spacing.Size5XL;
            groupElement.style.width = 379;
            groupElement.style.alignSelf = Align.Center;
            container.Add(groupElement);

            // Restore previous selection
            if (!string.IsNullOrEmpty(SelectedSkillLevel.Value))
            {
                group.SetSelection(SelectedSkillLevel.Value);
            }

            return container;
        }

        private VisualElement BuildRoleStep()
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.Flexbox.Column);
            container.AddToClassList(RLDSConstants.Flexbox.AlignCenter);

            // Title lockup
            var titleLockup = BuildTitleLockup(
                NuxSettings.Content.RoleTitle,
                NuxSettings.Content.RoleSubtitle);
            container.Add(titleLockup);

            // Role card grid
            var grid = new VisualElement();
            grid.AddToClassList(RLDSConstants.Flexbox.Row);
            grid.AddToClassList(RLDSConstants.Flexbox.Wrap);
            grid.style.marginTop = RLDSConstants.Spacing.Size5XL;
            grid.style.justifyContent = Justify.Center;
            grid.style.maxWidth = (RoleCardWidth * RoleGridColumns)
                + (RLDSConstants.Spacing.SizeSM * (RoleGridColumns - 1));

            _roleCards.Clear();
            string selectedRoleId = SelectedRole.Value;

            for (var i = 0; i < NuxSettings.Roles.All.Length; i++)
            {
                var def = NuxSettings.Roles.All[i];
                var card = new RoleCard(def.Label, GetRoleIcon(def.IconName), def.Id);
                _roleCards[def.Id] = card;

                if (def.Id == selectedRoleId)
                {
                    card.Selected = true;
                }

                var roleId = def.Id;
                card.Clicked += () => OnRoleSelected(roleId);

                var cardElement = card.Build();
                cardElement.style.width = RoleCardWidth;
                cardElement.style.height = RoleCardHeight;
                cardElement.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                if ((i + 1) % RoleGridColumns != 0)
                {
                    cardElement.style.marginRight = RLDSConstants.Spacing.SizeSM;
                }
                grid.Add(cardElement);
            }

            container.Add(grid);

            return container;
        }

        private static TextureContent GetFeatureIcon(string iconName)
        {
            return iconName switch
            {
                "default-app" => UIStyles.Contents.FeatureDefaultAppIcon,
                "tools" => UIStyles.Contents.FeatureToolsIcon,
                "ai-agent" => UIStyles.Contents.FeatureAiAgentIcon,
                "list-checked" => UIStyles.Contents.FeatureListCheckedIcon,
                _ => null
            };
        }

        private static TextureContent GetRoleIcon(string iconName)
        {
            return iconName switch
            {
                "ibeam-cursor" => UIStyles.Contents.RoleIbeamCursorIcon,
                "headset-alt" => UIStyles.Contents.RoleHeadsetAltIcon,
                "vr-object" => UIStyles.Contents.RoleVrObjectIcon,
                "gamepad" => UIStyles.Contents.RoleGamepadIcon,
                "media-immersive-photo" => UIStyles.Contents.RoleMediaImmersivePhotoIcon,
                "editor" => UIStyles.Contents.RoleEditorIcon,
                "graphs" => UIStyles.Contents.RoleGraphsIcon,
                "avatar-emote" => UIStyles.Contents.RoleAvatarEmoteIcon,
                "category-basic" => UIStyles.Contents.RoleCategoryBasicIcon,
                _ => null
            };
        }

        private VisualElement BuildTitleLockup(string title, string subtitle)
        {
            var lockup = new VisualElement();
            lockup.AddToClassList(RLDSConstants.Flexbox.Column);
            lockup.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
            lockup.style.marginTop = RLDSConstants.Spacing.Size5XL;

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(RLDSConstants.Typography.Heading2);
            lockup.Add(titleLabel);

            var subtitleLabel = new Label(subtitle);
            subtitleLabel.AddToClassList(RLDSConstants.Typography.Body1Text);
            subtitleLabel.AddToClassList(RLDSConstants.Utilities.MarginTopXS);
            subtitleLabel.style.opacity = 0.7f;
            lockup.Add(subtitleLabel);

            return lockup;
        }

        #endregion

        #region Selection Handlers

        private void OnSkillLevelSelected(string skillLevelId)
        {
            SelectedSkillLevel.SetValue(skillLevelId);
            UpdateFooter(CurrentStep);
        }

        private void OnRoleSelected(string roleId)
        {
            foreach (var kvp in _roleCards)
            {
                kvp.Value.Selected = (kvp.Key == roleId);
            }

            SelectedRole.SetValue(roleId);
            UpdateFooter(CurrentStep);
        }

        private bool HasValidSelectionForStep(int stepIndex)
        {
            return (NuxSettings.StepId)stepIndex switch
            {
                NuxSettings.StepId.Intro => true,
                NuxSettings.StepId.SkillLevel => !string.IsNullOrEmpty(SelectedSkillLevel.Value),
                NuxSettings.StepId.Role => !string.IsNullOrEmpty(SelectedRole.Value),
                _ => true
            };
        }

        #endregion

        #region Footer

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList(RLDSConstants.Flexbox.Row);
            footer.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
            footer.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            footer.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            footer.style.paddingLeft = RLDSConstants.Spacing.SizeXL;
            footer.style.paddingRight = RLDSConstants.Spacing.SizeXL;

            // Left side — flexible spacer that right-aligns the nav buttons
            var leftActions = new VisualElement();
            leftActions.AddToClassList(RLDSConstants.Flexbox.Row);
            leftActions.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
            leftActions.AddToClassList(RLDSConstants.Flexbox.Grow1);

            footer.Add(leftActions);

            // Right side — Back + Next/Finish buttons
            var rightActions = new VisualElement();
            rightActions.AddToClassList(RLDSConstants.Flexbox.Row);
            rightActions.AddToClassList(RLDSConstants.Flexbox.AlignCenter);

            _backButtonElement = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(NuxSettings.Content.BackLabel),
                    Action = OnBack,
                    Id = "NuxBackButton",
                    Origin = Origins.GuidedSetup,
                    OriginData = this
                },
                RLDSConstants.ButtonVariant.Secondary,
                RLDSConstants.ButtonSize.Large).Build();
            _backButtonElement.style.minWidth = 219;
            _backButtonElement.style.marginRight = RLDSConstants.Spacing.SizeSM;
            rightActions.Add(_backButtonElement);

            _nextButtonElement = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(NuxSettings.Content.GetStartedLabel),
                    Action = OnNext,
                    Id = "NuxNextButton",
                    Origin = Origins.GuidedSetup,
                    OriginData = this
                },
                RLDSConstants.ButtonVariant.Primary,
                RLDSConstants.ButtonSize.Large).Build();

            _nextButtonElement.style.minWidth = 219;
            rightActions.Add(_nextButtonElement);

            rightActions.style.width = 450;
            rightActions.style.justifyContent = Justify.FlexEnd;
            footer.Add(rightActions);

            return footer;
        }

        private void UpdateFooter(int stepIndex)
        {
            if (_backButtonElement == null || _nextButtonElement == null) return;

            // Back button: hidden on first step
            _backButtonElement.style.display =
                stepIndex > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // Next button label
            var label = stepIndex switch
            {
                0 => NuxSettings.Content.GetStartedLabel,
                _ when stepIndex >= NuxSettings.StepCount - 1 => NuxSettings.Content.FinishSetupLabel,
                _ => NuxSettings.Content.NextLabel
            };

            if (_nextButtonElement is UnityEngine.UIElements.Button btn)
            {
                btn.text = label;
            }

            // Disable next on last step if no selection
            var enabled = HasValidSelectionForStep(stepIndex);
            _nextButtonElement.SetEnabled(enabled);
        }

        #endregion

        #region Public API

        internal static NuxFlow Instance => _instance ??= new NuxFlow();

        internal static bool IsNuxCompleted => NuxCompleted.Value;

        internal void ShowNux(Origins origin, bool forceShow = false)
        {
            ShowWindow(origin, forceShow);
        }

        internal void ResetNux()
        {
            SelectedSkillLevel.SetValue(null);
            SelectedRole.SetValue(null);
            NuxCompleted.SetValue(false);
            _window?.DontShowAgain.SetValue(false);
            _currentStepIndex.SetValue(0);
        }

        #endregion
    }
}
