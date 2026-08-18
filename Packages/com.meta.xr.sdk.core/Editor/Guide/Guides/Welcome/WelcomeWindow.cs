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
using System.Linq;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using Meta.XR.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using RLDSButton = Meta.XR.Editor.UserInterface.RLDS.Button;
using Label = UnityEngine.UIElements.Label;
using ScrollView = UnityEngine.UIElements.ScrollView;
using Toggle = Meta.XR.Editor.UserInterface.Toggle;

namespace Meta.XR.Guides.Editor.Welcome
{
    internal class WelcomeWindow : RLDSEditorWindow
    {
        private const string TelemetryWindowId = "WelcomeWindow";

        protected override string TelemetryId => TelemetryWindowId;

        private static readonly TextureContent DownloadIcon =
            TextureContent.CreateContent("download.png", TextureContent.Categories.Generic, null);

        private static readonly TextureContent SetupIcon =
            TextureContent.CreateContent("feature_tools.png", TextureContent.Categories.Generic, null);

        private static readonly TextureContent ExternalLinkIcon =
            TextureContent.CreateContent("external_link.png", TextureContent.Categories.Generic, null);

        private static WelcomeWindow _instance;

        private static UserBool _showOnLaunch;

        private static UserBool ShowOnLaunch => _showOnLaunch ??= new UserBool()
        {
            Default = true,
            Label = "ShowWelcomeOnLaunch",
            Uid = "WelcomeShowOnLaunch",
            SendTelemetry = true,
            Owner = null
        };

        internal static bool ShouldShowOnLaunch => ShowOnLaunch.Value;

        internal static void Show(Origins origin)
        {
            if (_instance != null)
            {
                _instance.Focus();
                return;
            }

            // Open as a dockable tab (utility:false), not a floating utility window. No maxSize
            // lock so it can be docked and resized.
            var window = GetWindow<WelcomeWindow>(false, WelcomeSettings.WindowTitle);
            window.minSize = new Vector2(WelcomeSettings.WindowWidth, WelcomeSettings.WindowHeight);
            _instance = window;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _instance = this;
            EditorApplication.delayCall += RebuildIfEmpty;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _instance = null;
        }

        private void OnBecameVisible() => RebuildIfEmpty();

        private void OnFocus() => RebuildIfEmpty();

        private void CreateGUI()
        {
            BuildUI();
        }

        private void RebuildIfEmpty()
        {
            if (this == null) return;
            if (rootVisualElement.childCount == 0)
            {
                BuildUI();
            }
        }

        internal void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            // Tag the window so every RLDS widget inside reports this surface as its navigation path.
            RLDSTelemetry.SetScope(root, Origins.GuidedSetup, TelemetryWindowId);

            try
            {
                var styleSheet = RLDSUtils.LoadStyleSheet(!EditorGUIUtility.isProSkin);
                if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
                {
                    root.styleSheets.Add(styleSheet);
                }

                var scrollView = new ScrollView(ScrollViewMode.Vertical);
                scrollView.AddToClassList(RLDSConstants.Flexbox.Grow1);
                root.Add(scrollView);

                var content = scrollView.contentContainer;

                BuildCoverSection(content);
                BuildResourcesSection(content);
                BuildXrToolsSection(content);

                // Footer is part of the scrollable page (scrolls away at the top), not pinned.
                var footer = BuildFooter();
                content.Add(footer);
            }
            catch (Exception e)
            {
                // A transient failure (e.g. tool registry not ready on a restored window) must not
                // leave a half-built, blank window. Clear so OnFocus/OnBecameVisible can rebuild
                // cleanly on the next interaction.
                Debug.LogWarning($"[Welcome] Failed to build window, will retry on focus: {e}");
                root.Clear();
            }
        }

        #region Cover

        private void BuildCoverSection(VisualElement parent)
        {
            var cover = new CoverImage
            {
                FullBleed = true,
                CoverIcon = Meta.XR.Editor.UserInterface.Styles.Contents.SdkCoverIcon
            };

            var coverElement = cover.Build();
            Meta.XR.Editor.UserInterface.Styles.Contents.CoverBg.RegisterToImageLoaded(tex =>
                coverElement.style.backgroundImage = new StyleBackground(tex as Texture2D));
            parent.Add(coverElement);

            cover.ContentArea.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            cover.ContentArea.style.paddingRight = RLDSConstants.Spacing.Size3XL;
            cover.ContentArea.style.paddingBottom = RLDSConstants.Spacing.Size3XL;

            // GetSdkVersion() reads OVRPlugin, so the version resolves even when the SDK is
            // in-project source rather than an installed package (where ComputePackageVersion is 0).
            var version = ToolUsage.GetSdkVersion();
            if (version.HasValue)
            {
                var versionBadge = new BadgePill($"Version {version.Value}", BadgePillType.Warning, BadgePillSize.Small).Build();
                cover.ContentArea.Add(versionBadge);
            }

            var title = new Label(WelcomeSettings.Content.CoverTitle);
            title.AddToClassList(RLDSConstants.Typography.Heading1);
            title.AddToClassList(RLDSConstants.Utilities.MarginTopXS);
            cover.ContentArea.Add(title);

            var subtitle = new Label(WelcomeSettings.Content.CoverSubtitle);
            subtitle.AddToClassList(RLDSConstants.Typography.Body1Text);
            subtitle.AddToClassList(RLDSConstants.Utilities.MarginTopXS);
            cover.ContentArea.Add(subtitle);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList(RLDSConstants.Flexbox.Row);
            buttonRow.AddToClassList(RLDSConstants.Utilities.MarginTopSM);

            var openSdkBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(WelcomeSettings.Content.OpenSdkMenuLabel),
                    Action = OnOpenSdkMenu,
                    Id = "WelcomeOpenSdkMenu",
                    Origin = Origins.GuidedSetup,
                    OriginData = null
                },
                RLDSConstants.ButtonVariant.Primary,
                RLDSConstants.ButtonSize.Large).Build();
            buttonRow.Add(openSdkBtn);

            var releaseNotesBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(WelcomeSettings.Content.ViewReleaseNotesLabel),
                    Action = OnViewReleaseNotes,
                    Id = "WelcomeViewReleaseNotes",
                    Origin = Origins.GuidedSetup,
                    OriginData = null
                },
                RLDSConstants.ButtonVariant.OnMedia,
                RLDSConstants.ButtonSize.Large).Build();
            releaseNotesBtn.style.marginLeft = RLDSConstants.Spacing.SizeSM;
            buttonRow.Add(releaseNotesBtn);

            cover.ContentArea.Add(buttonRow);
        }

        #endregion

        #region Resources

        private void BuildResourcesSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList(RLDSConstants.Utilities.Padding2xMD);
            section.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            section.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            section.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            section.style.paddingRight = RLDSConstants.Spacing.Size3XL;

            var header = new Label(WelcomeSettings.Content.ResourcesHeader);
            header.AddToClassList(RLDSConstants.Typography.Heading2);
            header.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            header.style.opacity = 0.7f;
            section.Add(header);

            var grid = new VisualElement();
            grid.AddToClassList(RLDSConstants.Flexbox.Row);
            grid.AddToClassList(RLDSConstants.Flexbox.Wrap);
            grid.style.justifyContent = Justify.FlexStart;

            foreach (var def in WelcomeSettings.Resources.All)
            {
                var card = new FeatureCard(
                    def.Label,
                    def.Description,
                    FeatureCardVariant.Interactive,
                    linkText: def.LinkText,
                    linkIcon: def.OpensUrl ? ExternalLinkIcon : null,
                    id: def.Label);

                if (def.LinkAction != null)
                {
                    card.LinkClicked += def.LinkAction;
                }

                var cardElement = card.Build();
                cardElement.style.flexGrow = 1;
                cardElement.style.flexBasis = 0;
                cardElement.style.marginRight = RLDSConstants.Spacing.SizeSM;
                cardElement.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                grid.Add(cardElement);
            }

            section.Add(grid);
            parent.Add(section);
        }

        #endregion

        #region XR Tools

        private void BuildXrToolsSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList(RLDSConstants.Utilities.Padding2xMD);
            section.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            section.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            section.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            section.style.paddingRight = RLDSConstants.Spacing.Size3XL;

            var header = new Label(WelcomeSettings.Content.XrToolsHeader);
            header.AddToClassList(RLDSConstants.Typography.Heading2);
            header.style.marginBottom = RLDSConstants.Spacing.SizeMD;
            header.style.opacity = 0.7f;
            section.Add(header);

            var grid = new VisualElement();
            grid.AddToClassList(RLDSConstants.Flexbox.Row);
            grid.AddToClassList(RLDSConstants.Flexbox.Wrap);
            grid.style.justifyContent = Justify.FlexStart;

            foreach (var def in WelcomeSettings.XrTools.All)
            {
                // Runtime Optimizer and Meta Quest Link only exist on Windows — hide on other platforms.
                if (def.WindowsOnly && Application.platform != RuntimePlatform.WindowsEditor)
                {
                    continue;
                }

                var descriptor = ToolRegistry.Registry
                    .FirstOrDefault(t => t.Name != null && t.Name.Contains(def.RegistryNameHint));

                var toolIcon = descriptor?.Icon ?? ResolveFallbackIcon(def.RegistryNameHint);
                var active = def.IsActive?.Invoke() == true;
                var ctaText = active ? def.ActiveCtaText : def.InactiveCtaText;
                var ctaAction = active ? def.ActiveAction : def.InactiveAction;
                var ctaIcon = ctaText == "Download" ? DownloadIcon
                    : ctaText == "Setup" ? SetupIcon
                    : null;

                var card = new FeatureCard(
                    def.Label,
                    def.Description,
                    FeatureCardVariant.Cta,
                    icon: toolIcon,
                    ctaText: ctaText,
                    ctaIcon: ctaIcon,
                    badgeText: active ? def.ActiveBadgeText : def.InactiveBadgeText,
                    badgeType: active ? BadgeTagType.Positive : BadgeTagType.Neutral,
                    isActive: active,
                    id: def.RegistryNameHint);

                if (ctaAction != null)
                {
                    card.CtaClicked += ctaAction;
                }

                var cardElement = card.Build();
                cardElement.style.width = 298;
                cardElement.style.marginRight = RLDSConstants.Spacing.SizeSM;
                cardElement.style.marginBottom = RLDSConstants.Spacing.SizeSM;
                grid.Add(cardElement);
            }

            section.Add(grid);
            parent.Add(section);
        }

        /// <summary>
        /// Resolves an icon for XR Tools cards whose backing tool is not present in the
        /// <see cref="ToolRegistry"/> (e.g. external companion apps) and therefore exposes no
        /// descriptor icon. Returns null for tools that already supply an icon via their descriptor,
        /// letting the caller fall back to that descriptor icon.
        /// </summary>
        private static TextureContent ResolveFallbackIcon(string registryNameHint) => registryNameHint switch
        {
            "AI Agent Bridge" => TextureContent.CreateContent("feature_ai_agent.png", TextureContent.Categories.Generic, null),
            "Runtime Optimizer" => TextureContent.CreateContent("ovr_icon_runtime_optimizer.png", TextureContent.Categories.Generic, null),
            "Meta Quest Developer Hub" => TextureContent.CreateContent("ovr_icon_meta.png", TextureContent.Categories.Generic, null),
            "Meta Quest Link" => TextureContent.CreateContent("ovr_icon_link.png", TextureContent.Categories.Generic, null),
            "RenderDoc" => TextureContent.CreateContent("ovr_icon_gpu.png", TextureContent.Categories.Generic, null),
            "Meta Haptics Studio" => TextureContent.CreateContent("ovr_icon_vibration.png", TextureContent.Categories.Generic, null),
            _ => null
        };

        #endregion

        #region Footer

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.AddToClassList(RLDSConstants.Flexbox.Row);
            footer.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
            footer.AddToClassList(RLDSConstants.Utilities.Padding2xMD);
            footer.style.paddingTop = RLDSConstants.Spacing.SizeMD;
            footer.style.paddingBottom = RLDSConstants.Spacing.SizeMD;
            footer.style.paddingLeft = RLDSConstants.Spacing.Size3XL;
            footer.style.paddingRight = RLDSConstants.Spacing.Size3XL;
            footer.AddToClassList("rlds-welcome-footer");

            var leftActions = new VisualElement();
            leftActions.AddToClassList(RLDSConstants.Flexbox.Row);
            leftActions.AddToClassList(RLDSConstants.Flexbox.AlignCenter);
            leftActions.AddToClassList(RLDSConstants.Flexbox.Grow1);

            var toggle = new Toggle(
                WelcomeSettings.Content.ShowOnLaunchLabel,
                ShowOnLaunch.Value,
                RLDSConstants.Typography.Body2SmallLabel,
                value => ShowOnLaunch.SetValue(value));
            leftActions.Add(toggle.Build());

            footer.Add(leftActions);

            var closeBtn = new RLDSButton(
                new ActionLinkDescription
                {
                    Content = new GUIContent(WelcomeSettings.Content.CloseLabel),
                    Action = OnClose,
                    Id = "WelcomeClose",
                    Origin = Origins.GuidedSetup,
                    OriginData = null
                },
                RLDSConstants.ButtonVariant.Secondary,
                RLDSConstants.ButtonSize.Large).Build();
            footer.Add(closeBtn);

            return footer;
        }

        #endregion

        #region Actions

        private void OnOpenSdkMenu()
        {
            Close();
            EditorApplication.delayCall += () =>
                Meta.XR.Editor.StatusMenu.Dropdown.ShowDropdown();
        }

        private void OnViewReleaseNotes()
        {
            Application.OpenURL(WelcomeSettings.ReleaseNotesUrl);
        }

        private void OnClose()
        {
            Close();
        }

        #endregion
    }
}
