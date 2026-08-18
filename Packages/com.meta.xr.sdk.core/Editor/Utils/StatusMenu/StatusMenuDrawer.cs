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
using System.Linq;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.StatusMenu
{
    internal class StatusMenuDrawer : RLDSEditorWindow
    {
        private const string TelemetryWindowId = "SdkMenu";

        protected override string TelemetryId => TelemetryWindowId;
        protected override Origins TelemetryOrigin => Origins.StatusMenu;

        private const float MenuWidth = 432f;
        private const float HeaderHeight = 60f;
        private const float DividerHeight = 6f;
        private const float CategoryLabelHeight = 34f;
        private const float MenuItemHeight = 55f;
        private const float MenuItemWithStatusHeight = 72f;
        private const float BottomPadding = 12f;

        private static readonly TextureContent.Category BuildingBlocksIcons = new("BuildingBlocks/Icons");

        private static readonly TextureContent NewIcon =
            TextureContent.CreateContent("ovr_icon_new.png", BuildingBlocksIcons, null);

        private static readonly TextureContent ExperimentalIcon =
            TextureContent.CreateContent("ovr_icon_experimental.png", TextureContent.Categories.Generic, null);

        private const float BannerHeight = 72f;

        private static StatusMenuDrawer _instance;
        private IReadOnlyList<ToolDescriptor> _items;
        private string _versionText;
        private bool _isUpdateAvailable;
        private string _latestVersionText;

        internal static bool Visible => _instance != null;

        internal static void ShowDropdown(Rect source, IReadOnlyList<ToolDescriptor> items, bool isUpdateAvailable = false, string latestVersion = null)
        {
            if (_instance != null)
            {
                _instance.Close();
            }

            if (items == null || items.Count == 0) return;

            if (float.IsNaN(source.x) || float.IsNaN(source.y) ||
                float.IsNaN(source.width) || float.IsNaN(source.height))
            {
                return;
            }

            var instance = CreateInstance<StatusMenuDrawer>();
            instance._items = items;
            instance._versionText = GetSdkVersion();
            instance._isUpdateAvailable = isUpdateAvailable;
            instance._latestVersionText = latestVersion;
            instance.ShowAsDropDown(source, new Vector2(MenuWidth, instance.ComputeHeight()));
            instance.Focus();
            _instance = instance;
        }

        private float ComputeHeight()
        {
            float height = HeaderHeight + DividerHeight;

            if (_isUpdateAvailable)
            {
                height += BannerHeight;
            }

            var resources = _items.Where(i => i.MenuCategory == MenuCategory.Resources).ToList();
            var tools = _items.Where(i => i.MenuCategory == MenuCategory.Tools).ToList();
            var uncategorized = _items.Where(i => i.MenuCategory == MenuCategory.None).ToList();

            if (resources.Count > 0)
            {
                height += CategoryLabelHeight + resources.Sum(GetItemHeight);
            }

            if (tools.Count > 0)
            {
                height += CategoryLabelHeight + tools.Sum(GetItemHeight);
            }

            height += uncategorized.Sum(GetItemHeight);
            height += BottomPadding;

            return height;
        }

        private static float GetItemHeight(ToolDescriptor descriptor)
        {
            if (descriptor.EnablementDescriptor != null)
            {
                var (enabled, text) = descriptor.EnablementDescriptor();
                if (!enabled && !string.IsNullOrEmpty(text)) return MenuItemWithStatusHeight;
            }

            // InfoText rendered as a trailing badge keeps the row at its base height.
            if (descriptor.ShowInfoTextAsBadge) return MenuItemHeight;

            if (descriptor.StatusBadges != null)
            {
                var badges = descriptor.StatusBadges();
                var hasBadge = false;
                if (badges != null)
                {
                    foreach (var badge in badges)
                    {
                        if (!string.IsNullOrEmpty(badge.text))
                        {
                            hasBadge = true;
                            break;
                        }
                    }
                }

                return hasBadge ? MenuItemWithStatusHeight : MenuItemHeight;
            }

            if (descriptor.InfoTextDelegate == null) return MenuItemHeight;
            var (infoText, _) = descriptor.InfoTextDelegate();
            return string.IsNullOrEmpty(infoText) ? MenuItemHeight : MenuItemWithStatusHeight;
        }

        private void CreateGUI()
        {
            var isLightMode = !EditorGUIUtility.isProSkin;
            var styleSheet = RLDSUtils.LoadStyleSheet(isLightMode);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            BuildUI(rootVisualElement);
        }

        private void BuildUI(VisualElement root)
        {
            root.Clear();
            // Tag the dropdown so every RLDS row/header/banner reports this surface as its path.
            RLDSTelemetry.SetScope(root, Origins.StatusMenu, TelemetryWindowId);

            var container = new VisualElement();
            container.style.flexGrow = 1f;
            container.AddToClassList(RLDSConstants.BeveledDropdown.Base);
            root.Add(container);

            BuildHeader(container);
            BuildDivider(container);
            if (_isUpdateAvailable)
            {
                BuildBanner(container);
            }
            BuildItemSections(container);
        }

        private void BuildHeader(VisualElement root)
        {
            var headerActions = _items
                .Where(i => i.MenuCategory == MenuCategory.Header)
                .OrderBy(i => i.Order)
                .Select(i => new HeaderAction(i.Icon, () =>
                {
                    i.OnClickDelegate?.Invoke(Origins.StatusMenu);
                    Close();
                }, i.Id))
                .ToList();

            var header = new MenuHeader
            {
                Version = _versionText,
                Actions = headerActions,
                UpdateAvailable = _isUpdateAvailable
            };
            header.UpdateAvailableClicked += () =>
            {
                OpenAbout();
                Close();
            };
            root.Add(header.Build());
        }

        private void BuildBanner(VisualElement root)
        {
            var banner = new MenuBanner
            {
                VersionNumber = _latestVersionText != null ? $"v{_latestVersionText} available" : "Update available",
                FeatureDescription = "New features and improvements"
            };
            banner.CtaClicked += () =>
            {
                OpenAbout();
                Close();
            };
            root.Add(banner.Build());
        }

        private static void BuildDivider(VisualElement root)
        {
            var container = new VisualElement();
            container.AddToClassList(RLDSConstants.Divider.Section);

            var divider = new VisualElement();
            divider.AddToClassList(RLDSConstants.Divider.Base);
            container.Add(divider);

            var highlight = new VisualElement();
            highlight.AddToClassList(RLDSConstants.Divider.Highlight);
            container.Add(highlight);

            root.Add(container);
        }

        private void BuildItemSections(VisualElement root)
        {
            var resources = _items
                .Where(i => i.MenuCategory == MenuCategory.Resources)
                .OrderBy(i => SortKeyForItem(i)).ThenBy(i => i.Order).ToList();
            var tools = _items
                .Where(i => i.MenuCategory == MenuCategory.Tools)
                .OrderBy(i => SortKeyForItem(i)).ThenBy(i => i.Order).ToList();
            var uncategorized = _items
                .Where(i => i.MenuCategory == MenuCategory.None)
                .OrderBy(i => SortKeyForItem(i)).ThenBy(i => i.Order).ToList();

            if (resources.Count > 0)
            {
                root.Add(new CategoryLabel("Resources").Build());
                BuildItemList(root, resources);
            }

            if (tools.Count > 0)
            {
                root.Add(new CategoryLabel("Tools").Build());
                BuildItemList(root, tools, addBottomPadding: true);
            }

            if (uncategorized.Count > 0)
            {
                BuildItemList(root, uncategorized);
            }
        }

        private void BuildItemList(VisualElement root, List<ToolDescriptor> items, bool addBottomPadding = false)
        {
            var listContainer = new VisualElement();
            listContainer.style.paddingLeft = RLDSConstants.Spacing.SizeSM;
            listContainer.style.paddingRight = RLDSConstants.Spacing.SizeSM;
            if (addBottomPadding)
            {
                listContainer.style.paddingBottom = RLDSConstants.Spacing.SizeSM;
            }

            foreach (var descriptor in items)
            {
                listContainer.Add(BuildMenuItemRow(descriptor));
            }

            root.Add(listContainer);
        }

        private VisualElement BuildMenuItemRow(ToolDescriptor descriptor)
        {
            // Use the display name (Label = DisplayName ?? Name) so the row matches the
            // sentence-case labels in the design (Figma node 4818-71850).
            var title = descriptor.Label;

            var isDisabled = false;
            string enablementText = null;
            VisualElement enablementContent = null;
            if (descriptor.EnablementDescriptor != null)
            {
                var (enabled, text) = descriptor.EnablementDescriptor();
                isDisabled = !enabled;
                if (isDisabled)
                {
                    if (descriptor.EnablementLink != null)
                    {
                        enablementContent = BuildEnablementLink(descriptor);
                    }
                    else
                    {
                        enablementText = text;
                    }
                }
            }

            var row = new MenuItemRow(
                title: title,
                icon: descriptor.Icon,
                subtitle: descriptor.MenuDescription,
                statusContent: isDisabled ? null : BuildStatusContent(descriptor),
                trailingContent: BuildTrailingContent(descriptor),
                disabled: isDisabled,
                enablementText: enablementText,
                enablementContent: enablementContent,
                id: descriptor.Id);

            row.Clicked += () =>
            {
                descriptor.MarkSeen();
                descriptor.OnClickDelegate?.Invoke(Origins.StatusMenu);
                if (descriptor.CloseOnClick)
                {
                    Close();
                }
                else
                {
                    // Rebuild so status badges re-evaluate after an in-menu toggle
                    // (e.g. the Meta XR Simulator enable/disable from this same dropdown).
                    BuildUI(rootVisualElement);
                    Repaint();
                }
            };

            return row.Build();
        }

        private static VisualElement BuildStatusContent(ToolDescriptor descriptor)
        {
            // InfoText promoted to the trailing badge is not also shown under the description.
            if (descriptor.ShowInfoTextAsBadge) return null;

            if (descriptor.StatusBadges != null)
            {
                return BuildStatusBadges(descriptor.StatusBadges());
            }

            if (descriptor.InfoTextDelegate == null) return null;

            var (text, color) = descriptor.InfoTextDelegate();
            if (string.IsNullOrEmpty(text)) return null;

            var type = MapColorToBadgeType(color);
            return new BadgeTag(text, type, BadgeTagSize.Small).Build();
        }

        // Renders one or more severity badges on a single row (e.g. the Project setup tool's
        // red "outstanding issues" and amber "manually fixable items").
        private static VisualElement BuildStatusBadges(
            System.Collections.Generic.IReadOnlyList<(string text, Color? color)> badges)
        {
            if (badges == null) return null;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            foreach (var (text, color) in badges)
            {
                if (string.IsNullOrEmpty(text)) continue;

                var badge = new BadgeTag(text, MapColorToBadgeType(color), BadgeTagSize.Small).Build();
                if (row.childCount > 0)
                {
                    badge.style.marginLeft = RLDSConstants.Spacing.Size2XS;
                }

                row.Add(badge);
            }

            return row.childCount > 0 ? row : null;
        }

        private VisualElement BuildEnablementLink(ToolDescriptor descriptor)
        {
            var (prefix, linkText, onClick) = descriptor.EnablementLink();

            var line = new VisualElement();
            line.AddToClassList(RLDSConstants.MenuItem.EnablementLine);

            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixLabel = new UnityEngine.UIElements.Label(prefix);
                prefixLabel.AddToClassList(RLDSConstants.MenuItem.EnablementPrefix);
                line.Add(prefixLabel);
            }

            var link = new UnityEngine.UIElements.Label(linkText);
            link.AddToClassList(RLDSConstants.MenuItem.EnablementLink);
            link.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                onClick?.Invoke(Origins.StatusMenu);
                Close();
            });
            line.Add(link);

            return line;
        }

        private static BadgeTagType MapColorToBadgeType(Color? color)
        {
            if (!color.HasValue) return BadgeTagType.Neutral;

            if (color.Value == UserInterface.Styles.Colors.SuccessColor) return BadgeTagType.Positive;
            if (color.Value == UserInterface.Styles.Colors.ErrorColor) return BadgeTagType.Negative;
            if (color.Value == UserInterface.Styles.Colors.WarningColor) return BadgeTagType.Warning;
            if (color.Value == UserInterface.Styles.Colors.InfoColor) return BadgeTagType.Info;

            return BadgeTagType.Neutral;
        }

        internal static VisualElement BuildTrailingContent(ToolDescriptor descriptor)
        {
            if (descriptor.DrawExperimentalInStatusMenu)
            {
                return new BadgePill("Experimental", BadgePillType.Neutral, BadgePillSize.Small, ExperimentalIcon).Build();
            }

            if (descriptor.ShowInfoTextAsBadge && descriptor.InfoTextDelegate != null)
            {
                var (infoText, _) = descriptor.InfoTextDelegate();
                if (!string.IsNullOrEmpty(infoText))
                {
                    return new BadgePill(infoText, BadgePillType.Info, BadgePillSize.Small, NewIcon).Build();
                }
            }

            if (descriptor.IsNew)
            {
                return new BadgePill("New", BadgePillType.Info, BadgePillSize.Small, NewIcon).Build();
            }

            return null;
        }

        private static int SortKeyForItem(ToolDescriptor descriptor)
        {
            // Tools pinned to the bottom sort after both internal and non-internal tools.
            if (descriptor.ShowLastInStatusMenu) return 2;
            return 0;
        }

        private static void OpenAbout()
        {
            EditorApplication.ExecuteMenuItem("Meta/About Meta XR SDK");
        }

        private const string SdkUpdateAssistantName = "SDK update assistant";

        private void OpenUpdateAssistant()
        {
            var descriptor = _items?.FirstOrDefault(i => i.Name == SdkUpdateAssistantName);
            if (descriptor?.OnClickDelegate != null)
            {
                descriptor.OnClickDelegate(Origins.StatusMenu);
                return;
            }

            // Fallback if the SDK Update Assistant is not registered (e.g. external SDK build).
            OpenAbout();
        }

        private static string GetSdkVersion()
        {
            var version = ToolUsage.GetSdkVersion();
            return version.HasValue ? $"Version {version.Value}" : null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_instance != this) return;
            _instance = null;
        }
    }
}
