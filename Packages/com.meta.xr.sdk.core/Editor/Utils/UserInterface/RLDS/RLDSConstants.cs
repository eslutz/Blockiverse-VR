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

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal static class RLDSConstants
    {
        /// <summary>
        /// Button variant types following the RLDS design system.
        /// </summary>
        public enum ButtonVariant
        {
            Primary,
            Secondary,
            Tertiary,
            OnMedia
        }

        /// <summary>
        /// Button size types following the RLDS design system.
        /// </summary>
        public enum ButtonSize
        {
            Large,
            Small,
            XSmall
        }

        /// <summary>
        /// Typography CSS class names for RLDS design system.
        /// </summary>
        public static class Typography
        {
            public const string Title = "rlds-title";
            public const string Heading1 = "rlds-heading1";
            public const string Heading2 = "rlds-heading2";
            public const string Heading3 = "rlds-heading3";
            public const string Heading4 = "rlds-heading4";
            public const string Body1Label = "rlds-body1-label";
            public const string Body1Text = "rlds-body1-text";
            public const string Body2SmallLabel = "rlds-body2-small-label";
            public const string Body2SupportingText = "rlds-body2-supporting-text";
            public const string Meta = "rlds-meta";
            public const string EyebrowLabel = "rlds-eyebrow-label";
            public const string BodyNav = "rlds-body-nav";
            public const string BodyCode = "rlds-body-code";
            public const string BodySmallCode = "rlds-body-small-code";
            public const string Tiny = "rlds-tiny";
        }

        /// <summary>
        /// Button CSS class names for RLDS design system.
        /// </summary>
        public static class Button
        {
            // Primary buttons
            public const string Primary = "rlds-button-primary";
            public const string PrimarySmall = "rlds-button-primary-small";
            public const string PrimaryXSmall = "rlds-button-primary-xsmall";

            // Secondary buttons
            public const string Secondary = "rlds-button-secondary";
            public const string SecondarySmall = "rlds-button-secondary-small";
            public const string SecondarySmallDanger = "rlds-button-secondary-small--danger";
            public const string SecondaryXSmall = "rlds-button-secondary-xsmall";

            // Tertiary buttons
            public const string Tertiary = "rlds-button-tertiary";
            public const string TertiarySmall = "rlds-button-tertiary-small";
            public const string TertiaryXSmall = "rlds-button-tertiary-xsmall";

            // OnMedia buttons
            public const string OnMedia = "rlds-button-onmedia";
            public const string OnMediaSmall = "rlds-button-onmedia-small";
            public const string OnMediaXSmall = "rlds-button-onmedia-xsmall";

            // Modifier for buttons with icons
            public const string WithIcon = "rlds-button--with-icon";
            public const string IconLeft = "rlds-button-icon--left";
            public const string IconRight = "rlds-button-icon--right";
        }

        /// <summary>
        /// TextField CSS class names for RLDS design system.
        /// </summary>
        public static class TextField
        {
            // Base and variants
            public const string Base = "rlds-textfield";
            public const string Small = "rlds-textfield-small";
            public const string Error = "rlds-textfield-error";
            public const string ReadOnly = "rlds-textfield-readonly";
            public const string TextArea = "rlds-textfield-textarea";

            // With icons
            public const string WithLeftIcon = "rlds-textfield-with-left-icon";
            public const string WithRightIcon = "rlds-textfield-with-right-icon";

            // Helper elements
            public const string HelperContainer = "rlds-textfield__helper-container";
            public const string HasError = "has-error";
            public const string ErrorMessage = "rlds-textfield__error-message";
            public const string CharacterCount = "rlds-textfield__character-count";

            // Icon containers
            public const string IconLeft = "rlds-textfield__icon-left";
            public const string IconRight = "rlds-textfield__icon-right";

            // Action buttons
            public const string ClearButton = "rlds-textfield__clear-button";
            public const string PasswordToggle = "rlds-textfield__password-toggle";
        }

        /// <summary>
        /// Toggle CSS class names for RLDS design system.
        /// </summary>
        public static class Toggle
        {
            public const string Base = "rlds-toggle";
        }

        /// <summary>
        /// Slider CSS class names for RLDS design system.
        /// </summary>
        public static class Slider
        {
            public const string Base = "rlds-slider";
        }

        /// <summary>
        /// Radio button CSS class names for RLDS design system.
        /// </summary>
        public static class Radio
        {
            public const string Group = "rlds-radio-group";
            public const string Item = "rlds-radio-item";
            public const string InputRow = "rlds-radio-item__input-row";
            public const string Label = "rlds-radio-item__label";
            public const string LabelDisabled = "rlds-radio-item__label--disabled";
            public const string SelectedIndicator = "rlds-radio-item__selected-indicator";
            public const string ChildrenContainer = "rlds-radio-item__children-container";
            public const string Description = "rlds-radio-item__description";
            public const string DescriptionDisabled = "rlds-radio-item__description--disabled";
        }

        /// <summary>
        /// Indicator CSS class names for RLDS design system.
        /// </summary>
        public static class Indicator
        {
            public const string Base = "rlds-indicator";
            public const string Positive = "rlds-indicator--positive";
            public const string Negative = "rlds-indicator--negative";
            public const string Warning = "rlds-indicator--warning";
            public const string Disabled = "rlds-indicator--disabled";
            public const string Default = "rlds-indicator--default";
            public const string Privacy = "rlds-indicator--privacy";
        }

        /// <summary>
        /// Badge CSS class names for RLDS design system.
        /// </summary>
        public static class Badge
        {
            public const string Base = "rlds-badge";
            public const string Label = "rlds-badge__label";
            public const string Positive = "rlds-badge--positive";
            public const string Negative = "rlds-badge--negative";
            public const string Warning = "rlds-badge--warning";
            public const string Disabled = "rlds-badge--disabled";
            public const string Default = "rlds-badge--default";
            public const string Privacy = "rlds-badge--privacy";
            public const string DefaultLight = "rlds-badge--default-light";
            public const string PositiveLight = "rlds-badge--positive-light";
            public const string WarningLight = "rlds-badge--warning-light";
            public const string NegativeLight = "rlds-badge--negative-light";
            public const string NotificationLight = "rlds-badge--notification-light";
            public const string PrivacyLight = "rlds-badge--privacy-light";
        }

        /// <summary>
        /// Badge Pill CSS class names for RLDS design system.
        /// </summary>
        public static class BadgePill
        {
            public const string Base = "rlds-badge-pill";
            public const string Label = "rlds-badge-pill__label";
            public const string Icon = "rlds-badge-pill__icon";
            public const string Small = "rlds-badge-pill--small";
            public const string Positive = "rlds-badge-pill--positive";
            public const string Info = "rlds-badge-pill--info";
            public const string Warning = "rlds-badge-pill--warning";
            public const string Negative = "rlds-badge-pill--negative";
            public const string Neutral = "rlds-badge-pill--neutral";
        }

        /// <summary>
        /// Status Notice CSS class names for RLDS design system.
        /// A block-level banner with colored background, border, icon, label, and optional action button.
        /// </summary>
        public static class StatusNotice
        {
            public const string Base = "rlds-status-notice";
            public const string Left = "rlds-status-notice__left";
            public const string Icon = "rlds-status-notice__icon";
            public const string Label = "rlds-status-notice__label";
            public const string Positive = "rlds-status-notice--positive";
            public const string Negative = "rlds-status-notice--negative";
            public const string Processing = "rlds-status-notice--processing";
        }

        /// <summary>
        /// Badge Tag CSS class names for RLDS design system.
        /// </summary>
        public static class BadgeTag
        {
            public const string Base = "rlds-badge-tag";
            public const string Label = "rlds-badge-tag__label";
            public const string Icon = "rlds-badge-tag__icon";
            public const string Small = "rlds-badge-tag--small";
            public const string Positive = "rlds-badge-tag--positive";
            public const string Info = "rlds-badge-tag--info";
            public const string Warning = "rlds-badge-tag--warning";
            public const string Negative = "rlds-badge-tag--negative";
            public const string Neutral = "rlds-badge-tag--neutral";
        }

        /// <summary>
        /// Progress bar CSS class names for RLDS design system.
        /// </summary>
        public static class ProgressBar
        {
            public const string Container = "rlds-progress-bar-container";
            public const string Bar = "rlds-progress-bar";
            public const string Error = "rlds-progress-bar--error";
        }

        public static class ProgressStepper
        {
            public const string Container = "rlds-progress-stepper";
            public const string Step = "rlds-progress-stepper__step";
            public const string StepCompleted = "rlds-progress-stepper__step--completed";
            public const string StepCurrent = "rlds-progress-stepper__step--current";
            public const string StepClickable = "rlds-progress-stepper__step--clickable";
        }

        /// <summary>
        /// Ring Spinner CSS class names for RLDS design system (animated progress ring).
        /// </summary>
        public static class RingSpinner
        {
            public const string Root = "rlds-ring-spinner-root";
            public const string Ring = "rlds-ring-spinner";
            public const string Size12 = "rlds-ring-spinner--size-12";
            public const string Size16 = "rlds-ring-spinner--size-16";
            public const string Size24 = "rlds-ring-spinner--size-24";
            public const string Size32 = "rlds-ring-spinner--size-32";
            public const string Default = "rlds-ring-spinner--default";
            public const string Accent = "rlds-ring-spinner--accent";
            public const string Disabled = "rlds-ring-spinner--disabled";
            public const string Dark = "rlds-ring-spinner--dark";
            public const string Light = "rlds-ring-spinner--light";
        }

        /// <summary>
        /// Surface CSS class names for RLDS design system.
        /// </summary>
        public static class Surface
        {
            public const string Primary = "rlds-surface-primary";
            public const string Secondary = "rlds-surface-secondary";
            public const string Tertiary = "rlds-surface-tertiary";
            public const string Overlay = "rlds-surface-overlay";
        }

        /// <summary>
        /// Divider CSS class names for RLDS design system.
        /// </summary>
        public static class Divider
        {
            public const string Base = "rlds-divider";
            public const string Section = "rlds-divider--section";
            public const string Highlight = "rlds-divider__highlight";
        }

        public static class BeveledDropdown
        {
            public const string Base = "rlds-beveled-dropdown";
        }

        public static class Chip
        {
            public const string Base = "rlds-chip";
            public const string Selected = "rlds-chip--selected";
            public const string Label = "rlds-chip__label";
        }

        /// <summary>
        /// Slider tab CSS class names for RLDS design system.
        /// A rectangular tab control for toggling between content categories.
        /// </summary>
        public static class SliderTab
        {
            public const string Group = "rlds-slider-tab-group";
            public const string Item = "rlds-slider-tab";
            public const string ItemSelected = "rlds-slider-tab--selected";
            public const string Label = "rlds-slider-tab__label";
        }

        public static class DropdownMenu
        {
            public const string Overlay = "rlds-dropdown-menu__overlay";
            public const string Container = "rlds-dropdown-menu";
            public const string Item = "rlds-dropdown-menu__item";
            public const string ItemSelected = "rlds-dropdown-menu__item--selected";
            public const string ItemDisabled = "rlds-dropdown-menu__item--disabled";
            public const string ItemDestructive = "rlds-dropdown-menu__item--destructive";
            public const string ItemIcon = "rlds-dropdown-menu__item-icon";
            public const string ItemLabel = "rlds-dropdown-menu__item-label";
            public const string ItemDescription = "rlds-dropdown-menu__item-description";
            public const string ItemCheckmark = "rlds-dropdown-menu__item-checkmark";
            public const string Divider = "rlds-dropdown-menu__divider";
            public const string SectionHeader = "rlds-dropdown-menu__section-header";
        }

        /// <summary>
        /// Skill level selector CSS class names for RLDS design system.
        /// A card-style selection component with radio-button behavior.
        /// </summary>
        public static class SkillLevelSelector
        {
            public const string Group = "rlds-skill-level-selector-group";
            public const string Item = "rlds-skill-level-selector";
            public const string ItemSelected = "rlds-skill-level-selector--selected";
            public const string LabelContainer = "rlds-skill-level-selector__label-container";
            public const string Label = "rlds-skill-level-selector__label";
            public const string Description = "rlds-skill-level-selector__description";
        }

        /// <summary>
        /// Cover image CSS class names for RLDS design system.
        /// A hero/banner card with background image, watermark icon, and content area.
        /// </summary>
        public static class CoverImage
        {
            public const string Root = "rlds-cover-image";
            public const string RootFullBleed = "rlds-cover-image--full-bleed";
            public const string Icon = "rlds-cover-image__icon";
            public const string IconImage = "rlds-cover-image__icon-image";
            public const string Content = "rlds-cover-image__content";
        }

        /// <summary>
        /// SDK Menu Button CSS class names for RLDS design system.
        /// Persistent toolbar entry point for the Meta XR SDK dropdown menu.
        /// </summary>
        public static class SdkMenuButton
        {
            public const string Root = "rlds-sdk-menu-button";
            public const string Hover = "rlds-sdk-menu-button--hover";
            public const string Pressed = "rlds-sdk-menu-button--pressed";
            public const string VariantPositive = "rlds-sdk-menu-button--positive";
            public const string VariantWarning = "rlds-sdk-menu-button--warning";
            public const string VariantError = "rlds-sdk-menu-button--error";
            public const string VariantInfo = "rlds-sdk-menu-button--info";
            public const string IconLabelGroup = "rlds-sdk-menu-button__icon-label";
            public const string StatusIcon = "rlds-sdk-menu-button__status-icon";
            public const string MetaIcon = "rlds-sdk-menu-button__meta-icon";
            public const string Label = "rlds-sdk-menu-button__label";
            public const string DropdownArrow = "rlds-sdk-menu-button__dropdown";
        }

        /// <summary>
        /// Menu Banner CSS class names for RLDS design system.
        /// Promotional banner shown inside the SDK menu when an update is available.
        /// </summary>
        public static class MenuBanner
        {
            public const string Root = "rlds-menu-banner";
            public const string LeftContent = "rlds-menu-banner__left-content";
            public const string LabelDescriptionGroup = "rlds-menu-banner__label-description";
            public const string Label = "rlds-menu-banner__label";
            public const string Description = "rlds-menu-banner__description";
            public const string RightContent = "rlds-menu-banner__right-content";
            public const string CtaButton = "rlds-menu-banner__cta";
            public const string CtaLabel = "rlds-menu-banner__cta-label";
        }

        /// <summary>
        /// Category label CSS class names for RLDS design system.
        /// A non-interactive typographic section divider used to group menu items.
        /// </summary>
        public static class CategoryLabel
        {
            public const string Root = "rlds-category-label";
            public const string Label = "rlds-category-label__label";
        }

        /// <summary>
        /// Code block CSS class names for RLDS design system.
        /// </summary>
        public static class CodeBlock
        {
            public const string Container = "rlds-code-block";
            public const string Label = "rlds-code-block__label";
            public const string Language = "rlds-code-block__language";
        }

        /// <summary>
        /// Toast CSS class names for RLDS design system.
        /// </summary>
        public static class Toast
        {
            public const string Root = "rlds-toast";
        }

        /// <summary>
        /// Icon button CSS class names for RLDS design system.
        /// </summary>
        public static class IconButton
        {
            public const string Root = "rlds-icon-button";
            public const string Absolute = "rlds-icon-button--absolute";
        }

        /// <summary>
        /// Feature card CSS class names for RLDS design system.
        /// An interactive card component for the Welcome Board, supporting selection states
        /// and a CTA variant with an action button.
        /// </summary>
        public static class FeatureCard
        {
            public const string Root = "rlds-feature-card";
            public const string Selected = "rlds-feature-card--selected";
            public const string Cta = "rlds-feature-card--cta";
            public const string Container = "rlds-feature-card__container";
            public const string Icon = "rlds-feature-card__icon";
            public const string Eyebrow = "rlds-feature-card__eyebrow";
            public const string EyebrowIcon = "rlds-feature-card__eyebrow-icon";
            public const string EyebrowLabel = "rlds-feature-card__eyebrow-label";
            public const string Label = "rlds-feature-card__label";
            public const string Description = "rlds-feature-card__description";
            public const string Link = "rlds-feature-card__link";
            public const string LinkLabel = "rlds-feature-card__link-label";
            public const string LinkIcon = "rlds-feature-card__link-icon";
            public const string CtaButton = "rlds-feature-card__cta-button";
            public const string CtaButtonSecondary = "rlds-feature-card__cta-button--secondary";
            public const string CtaButtonIcon = "rlds-feature-card__cta-button-icon";
            public const string CtaButtonLabel = "rlds-feature-card__cta-button-label";
        }

        /// <summary>
        /// Role card CSS class names for RLDS design system.
        /// An interactive card for NUX role selection, supporting Default, Hover, Pressed, and Selected states.
        /// </summary>
        public static class RoleCard
        {
            public const string Root = "rlds-role-card";
            public const string Selected = "rlds-role-card--selected";
            public const string Icon = "rlds-role-card__icon";
            public const string Label = "rlds-role-card__label";
        }

        /// <summary>
        /// Menu item CSS class names for RLDS design system.
        /// A row component for navigation menus, supporting icon/avatar attributes,
        /// optional subtitle, and trailing action items (toggle, label+icon).
        /// </summary>
        public static class MenuItem
        {
            public const string Root = "rlds-menu-item";
            public const string Hover = "rlds-menu-item--hover";
            public const string Pressed = "rlds-menu-item--pressed";
            public const string Selected = "rlds-menu-item--selected";
            public const string WithSubtitle = "rlds-menu-item--with-subtitle";
            public const string Icon = "rlds-menu-item__icon";
            public const string TextGroup = "rlds-menu-item__text-group";
            public const string Title = "rlds-menu-item__title";
            public const string Subtitle = "rlds-menu-item__subtitle";
            public const string Spacer = "rlds-menu-item__spacer";
            public const string ActionArea = "rlds-menu-item__action-area";
            public const string ActionLabel = "rlds-menu-item__action-label";
            public const string ActionIcon = "rlds-menu-item__action-icon";
            public const string Disabled = "rlds-menu-item--disabled";
            public const string EnablementText = "rlds-menu-item__enablement-text";
            public const string EnablementLine = "rlds-menu-item__enablement-line";
            public const string EnablementPrefix = "rlds-menu-item__enablement-prefix";
            public const string EnablementLink = "rlds-menu-item__enablement-link";
        }

        /// <summary>
        /// Menu header CSS class names for RLDS design system.
        /// The persistent SDK Menu header bar with title, version, optional inline
        /// "Update available" link, settings/welcome/profile icon buttons, and a
        /// Logged-In variant.
        /// </summary>
        public static class MenuHeader
        {
            public const string Root = "rlds-menu-header";
            public const string LoggedIn = "rlds-menu-header--logged-in";
            public const string LeftContent = "rlds-menu-header__left-content";
            public const string TextGroup = "rlds-menu-header__text-group";
            public const string Title = "rlds-menu-header__title";
            public const string MetaData = "rlds-menu-header__meta-data";
            public const string Version = "rlds-menu-header__version";
            public const string UpdateLink = "rlds-menu-header__update-link";
            public const string RightContent = "rlds-menu-header__right-content";
            public const string IconButton = "rlds-menu-header__icon-button";
            public const string IconButtonIcon = "rlds-menu-header__icon-button-icon";
            public const string ProfileButton = "rlds-menu-header__profile-button";
        }

        public static class FontSize
        {
            public const int Size4XL = 56;
            public const int Size3XL = 32;
            public const int Size2XL = 24;
            public const int SizeXL = 20;
            public const int SizeLG = 16;
            public const int SizeMD = 14;
            public const int SizeSM = 12;
            public const int SizeXS = 11;
            public const int Size2XS = 10;
        }

        public static class LineHeight
        {
            public const int Size8XL = 60;
            public const int Size7XL = 52;
            public const int Size6XL = 44;
            public const int Size5XL = 40;
            public const int Size4XL = 36;
            public const int Size3XL = 28;
            public const int Size2XL = 26;
            public const int SizeXL = 24;
            public const int SizeLG = 20;
            public const int SizeMD = 18;
            public const int SizeSM = 16;
            public const int SizeXS = 13;
        }

        public static class LetterSpacing
        {
            public const float Title = -1f;
            public const float Heading = -0.5f;
            public const float Eyebrow = 0.3f;
            public const float Normal = 0f;
        }

        public static class Spacing
        {
            public const int None = 0;
            public const int Size4XS = 2;
            public const int Size3XS = 4;
            public const int Size2XS = 6;
            public const int SizeXS = 8;
            public const int SizeSM = 12;
            public const int SizeMD = 16;
            public const int Size2xMD = 32;
            public const int SizeLG = 20;
            public const int SizeXL = 24;
            public const int Size2XL = 32;
            public const int Size3XL = 40;
            public const int Size4XL = 48;
            public const int Size5XL = 64;
        }

        public static class Radius
        {
            public const int None = 0;
            public const int SizeXS = 4;
            public const int SizeSM = 8;
            public const int SizeMD = 14;
            public const int SizeLG = 20;
            public const int SizeXL = 32;
            public const int Full = 1000;
        }

        public static class IconSize
        {
            public const int Size2XS = 8;
            public const int SizeXS = 12;
            public const int SizeSM = 16;
            public const int SizeMD = 18;
            public const int SizeLG = 24;
            public const int SizeXL = 32;
            public const int Size2XL = 48;
            public const int Size3XL = 72;
        }

        public static class ThumbnailSize
        {
            public const int SizeXS = 24;
            public const int SizeSM = 32;
            public const int SizeMD = 40;
            public const int SizeLG = 48;
            public const int SizeXL = 56;
            public const int Size2XL = 64;
            public const int Size3XL = 72;
            public const int Size4XL = 96;
            public const int Size5XL = 120;
        }

        public static class BorderWidth
        {
            public const int None = 0;
            public const int SizeSM = 1;
            public const int SizeMD = 2;
            public const int SizeLG = 4;
        }

        /// <summary>
        /// CSS variable names for spinner colors (from RLDSBaseTokens).
        /// Use with Unity CustomStyleProperty to read values from USS.
        /// </summary>
        public static class Spinner
        {
            public const string FillBackground = "--rlds-spinner-fill-background";
            public const string TrackBackground = "--rlds-spinner-track-background";
        }

        /// <summary>
        /// CSS variable names for state overlay colors (from RLDSBaseTokens).
        /// Use with Unity CustomStyleProperty to read values from USS.
        /// </summary>
        public static class StateOverlay
        {
            public const string Hover = "--rlds-state-overlay-hover";
            public const string Pressed = "--rlds-state-overlay-pressed";
            public const string Selected = "--rlds-state-overlay-selected";
            public const string PrimaryButtonHover = "--rlds-state-overlay-primary-button-hover";
            public const string PrimaryButtonPressed = "--rlds-state-overlay-primary-button-pressed";
            public const string AuxiliaryHover = "--rlds-state-overlay-auxiliary-hover";
            public const string AuxiliaryPressed = "--rlds-state-overlay-auxiliary-pressed";
            public const string OnMediaHover = "--rlds-state-overlay-on-media-hover";
            public const string OnMediaPressed = "--rlds-state-overlay-on-media-pressed";
        }

        /// <summary>
        /// CSS variable names for semantic status colors (from RLDSBaseTokens).
        /// </summary>
        public static class SemanticStatus
        {
            public const string TextOnline = "--rlds-text-online";
            public const string TextScreenshare = "--rlds-text-screenshare";
            public const string IconScreenshare = "--rlds-icon-screenshare";
        }

        /// <summary>
        /// Utility CSS class names for RLDS design system.
        /// </summary>
        public static class Utilities
        {
            // Padding
            public const string PaddingMD = "rlds-padding-md";
            public static string Padding2xMD = "rlds-padding-2x-md";
            public const string PaddingSM = "rlds-padding-sm";
            public const string PaddingTopLG = "rlds-padding-top-lg";

            // Margin
            public const string MarginTopSM = "rlds-margin-top-sm";
            public const string MarginTopXS = "rlds-margin-top-xs";
            public const string MarginTop3XS = "rlds-margin-top-3xs";
            public const string MarginLG = "rlds-margin-lg";
            public const string NoMargin = "rlds-no-margin";

            // Position
            public const string AbsoluteFill = "rlds-absolute-fill";

            // Curson
            public const string CursorLink = "rlds-cursor-link";
        }

        /// <summary>
        /// Flexbox utility CSS class names for RLDS design system.
        /// </summary>
        public static class Flexbox
        {
            // Direction
            public const string Row = "rlds-flex-row";
            public const string Column = "rlds-flex-column";
            public const string RowReverse = "rlds-flex-row-reverse";
            public const string ColumnReverse = "rlds-flex-column-reverse";

            // Justify Content
            public const string JustifyStart = "rlds-justify-start";
            public const string JustifyEnd = "rlds-justify-end";
            public const string JustifyCenter = "rlds-justify-center";
            public const string JustifySpaceBetween = "rlds-justify-space-between";
            public const string JustifySpaceAround = "rlds-justify-space-around";

            // Align Items
            public const string AlignStart = "rlds-align-start";
            public const string AlignEnd = "rlds-align-end";
            public const string AlignCenter = "rlds-align-center";
            public const string AlignStretch = "rlds-align-stretch";

            // Align Self
            public const string SelfStart = "rlds-self-start";
            public const string SelfEnd = "rlds-self-end";
            public const string SelfCenter = "rlds-self-center";
            public const string SelfStretch = "rlds-self-stretch";

            // Grow & Shrink
            public const string Grow0 = "rlds-flex-grow-0";
            public const string Grow1 = "rlds-flex-grow-1";
            public const string Shrink0 = "rlds-flex-shrink-0";
            public const string Shrink1 = "rlds-flex-shrink-1";

            // Wrap
            public const string NoWrap = "rlds-flex-nowrap";
            public const string Wrap = "rlds-flex-wrap";
            public const string WrapReverse = "rlds-flex-wrap-reverse";

            // Common Patterns
            public const string RowCenter = "rlds-flex-row-center";
            public const string ColumnCenter = "rlds-flex-column-center";
            public const string RowSpaceBetween = "rlds-flex-row-space-between";
            public const string ColumnSpaceBetween = "rlds-flex-column-space-between";
            public const string RowStart = "rlds-flex-row-start";
            public const string RowEnd = "rlds-flex-row-end";
            public const string ColumnStart = "rlds-flex-column-start";
            public const string ColumnEnd = "rlds-flex-column-end";
        }
    }
}
