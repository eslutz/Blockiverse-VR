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

using UnityEditor;
using UnityEngine.UIElements;
using Meta.XR.Editor.UserInterface;

namespace Meta.HandReadinessTool.Editor.UI
{
    /// <summary>
    /// Class-name constants mirroring HandReadinessStyles.uss, plus the loader that resolves the
    /// stylesheet path against package vs in-repo layout (mirrors RLDSUtils.LoadStyleSheet).
    /// </summary>
    internal static class HandReadinessStyles
    {
        private const string PackageName = "com.meta.xr.sdk.core";
        private const string RelativePath =
            "Editor/HandReadinessTool/UI/HandReadinessStyles.uss";

        private static string StyleSheetPath => Utils.IsInsidePackageDistribution()
            ? $"Packages/{PackageName}/{RelativePath}"
            : $"Assets/Oculus/VR/{RelativePath}";

        public static StyleSheet LoadStyleSheet()
        {
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
        }

        public static class Cover
        {
            public const string Root = "hrt-cover";
            public const string FallbackBg = "hrt-cover-fallback-bg";
            public const string Headset = "hrt-cover-headset";
            public const string Content = "hrt-cover-content";
        }

        public static class Description
        {
            public const string Textarea = "hrt-description-textarea";
            public const string TextareaBox = "hrt-description-textarea-box";
        }

        public static class PhaseIcon
        {
            public const string Root = "hrt-phase-icon";
            public const string Small = "hrt-phase-icon--sm";
            public const string Pending = "hrt-phase-icon--pending";
            public const string Active = "hrt-phase-icon--active";
            public const string Complete = "hrt-phase-icon--complete";
            public const string Error = "hrt-phase-icon--error";
            public const string CheckGlyph = "hrt-phase-icon__check-glyph";
        }

        public static class Progress
        {
            public const string Track = "hrt-progress-track";
            public const string Fill = "hrt-progress-fill";
        }

        public static class Icon
        {
            public const string Themed = "hrt-themed-icon";
            public const string ThemedSecondary = "hrt-themed-icon--secondary";
            public const string ThemedPositive = "hrt-themed-icon--positive";
        }

        public static class Priority
        {
            public const string High = "hrt-priority-high";
            public const string Medium = "hrt-priority-medium";
            public const string Low = "hrt-priority-low";
        }

        public static class OnMediaText
        {
            public const string Primary = "hrt-on-media-text";
            public const string Secondary = "hrt-on-media-text-secondary";
        }



        public static class FooterButton
        {
            public const string Next = "hrt-footer-button";
            public const string Back = "hrt-footer-back";
        }

        public static class ErrorBanner
        {
            public const string Root = "hrt-error-banner";
            public const string Title = "hrt-error-banner__title";
        }

        public static class Card
        {
            public const string Root = "hrt-card";
            public const string Divider = "hrt-card__divider";
        }

        public static class WarningText
        {
            public const string Root = "hrt-warning-text";
        }

        public static class InlineLinkButton
        {
            public const string Root = "hrt-inline-link-button";
        }

        public static class Checkbox
        {
            public const string Root = "hrt-checkbox";
            public const string Glyph = "hrt-checkbox__glyph";
        }

        public static class UploadButton
        {
            public const string Root = "hrt-upload-button";
            public const string Icon = "hrt-upload-button__icon";
        }

        public static class FileCard
        {
            public const string Root = "hrt-file-card";
            public const string Icon = "hrt-file-card__icon";
            public const string RemoveButton = "hrt-file-card__remove-button";
        }

        public static class OptionCard
        {
            public const string Root = "hrt-option-card";
            public const string Selected = "hrt-option-card--selected";
            public const string Icon = "hrt-option-card__icon";
            public const string Title = "hrt-option-card__title";
            public const string Description = "hrt-option-card__description";
        }

        public static class Text
        {
            public const string Primary = "hrt-text-primary";
            public const string Link = "hrt-text-link";
        }

        public static class Divider
        {
            public const string Vertical = "hrt-vertical-divider";
        }
    }
}
