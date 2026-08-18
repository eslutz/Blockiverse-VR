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

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal static class RLDSUtils
    {
        private const string PackageName = "com.meta.xr.sdk.core";
        private const string StyleSheetLightRelativePath = "Editor/Utils/UserInterface/RLDS/StyleSheet-Light.uss";
        private const string StyleSheetDarkRelativePath = "Editor/Utils/UserInterface/RLDS/StyleSheet-Dark.uss";

        private static string StyleSheetLightPath => Utils.IsInsidePackageDistribution()
            ? $"Packages/{PackageName}/{StyleSheetLightRelativePath}"
            : $"Assets/Oculus/VR/{StyleSheetLightRelativePath}";

        private static string StyleSheetDarkPath => Utils.IsInsidePackageDistribution()
            ? $"Packages/{PackageName}/{StyleSheetDarkRelativePath}"
            : $"Assets/Oculus/VR/{StyleSheetDarkRelativePath}";

        public static StyleSheet LoadStyleSheet(bool isLightMode)
        {
            var styleSheetPath = isLightMode ? StyleSheetLightPath : StyleSheetDarkPath;
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
        }

        /// <summary>
        /// Gets the RLDS CSS class name for a button based on variant and size.
        /// </summary>
        /// <param name="variant">The button variant (Primary, Secondary, Tertiary, OnMedia)</param>
        /// <param name="size">The button size (Large, Small, XSmall)</param>
        /// <returns>The RLDS CSS class name using RLDSConstants.Button constants</returns>
        public static string GetButtonStyleClass(RLDSConstants.ButtonVariant variant, RLDSConstants.ButtonSize size)
        {
            return (variant, size) switch
            {
                // Primary buttons
                (RLDSConstants.ButtonVariant.Primary, RLDSConstants.ButtonSize.Large) => RLDSConstants.Button.Primary,
                (RLDSConstants.ButtonVariant.Primary, RLDSConstants.ButtonSize.Small) => RLDSConstants.Button.PrimarySmall,
                (RLDSConstants.ButtonVariant.Primary, RLDSConstants.ButtonSize.XSmall) => RLDSConstants.Button.PrimaryXSmall,

                // Secondary buttons
                (RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Large) => RLDSConstants.Button.Secondary,
                (RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.Small) => RLDSConstants.Button.SecondarySmall,
                (RLDSConstants.ButtonVariant.Secondary, RLDSConstants.ButtonSize.XSmall) => RLDSConstants.Button.SecondaryXSmall,

                // Tertiary buttons
                (RLDSConstants.ButtonVariant.Tertiary, RLDSConstants.ButtonSize.Large) => RLDSConstants.Button.Tertiary,
                (RLDSConstants.ButtonVariant.Tertiary, RLDSConstants.ButtonSize.Small) => RLDSConstants.Button.TertiarySmall,
                (RLDSConstants.ButtonVariant.Tertiary, RLDSConstants.ButtonSize.XSmall) => RLDSConstants.Button.TertiaryXSmall,

                // OnMedia buttons
                (RLDSConstants.ButtonVariant.OnMedia, RLDSConstants.ButtonSize.Large) => RLDSConstants.Button.OnMedia,
                (RLDSConstants.ButtonVariant.OnMedia, RLDSConstants.ButtonSize.Small) => RLDSConstants.Button.OnMediaSmall,
                (RLDSConstants.ButtonVariant.OnMedia, RLDSConstants.ButtonSize.XSmall) => RLDSConstants.Button.OnMediaXSmall,

                // Default fallback
                _ => RLDSConstants.Button.Primary
            };
        }
    }
}
