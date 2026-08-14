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

namespace Oculus.VR.Editor
{
    /// <summary>
    /// Interface for querying OVRPlugin activation state. Implementations determine whether the OpenXR or Unity-provided plugin backend is active.
    /// </summary>
    public interface IOVRPluginInfoSupplier
    {
        /// <summary>
        /// Returns whether the OVRPlugin OpenXR backend is currently activated.
        /// </summary>
        /// <returns><c>true</c> if the OpenXR backend is active; otherwise <c>false</c>.</returns>
        bool IsOVRPluginOpenXRActivated();

        /// <summary>
        /// Returns whether the Unity-provided OVRPlugin backend is currently activated.
        /// </summary>
        /// <returns><c>true</c> if the Unity-provided backend is active; otherwise <c>false</c>.</returns>
        bool IsOVRPluginUnityProvidedActivated();
    }

    /// <summary>
    /// Provides utility methods for querying OVRPlugin state and locating the Meta Core SDK file paths.
    /// Use to determine plugin backend (OpenXR vs Unity-provided), find the SDK root/plugin directories, and check if the SDK is modifiable.
    /// </summary>
    public class OVRPluginInfo : ScriptableObject
    {
        public const string PackageName = "com.meta.xr.sdk.core";

        private static readonly IOVRPluginInfoSupplier Supplier = new OVRPluginInfoOpenXR();

        /// <summary>
        /// Returns true if the OVRPlugin OpenXR backend is currently activated.
        /// </summary>
        /// <returns><c>true</c> if the OpenXR backend is active; otherwise <c>false</c>.</returns>
        public static bool IsOVRPluginOpenXRActivated() => Supplier.IsOVRPluginOpenXRActivated();

        /// <summary>
        /// Returns true if the Unity-provided OVRPlugin backend is currently activated.
        /// </summary>
        /// <returns><c>true</c> if the Unity-provided backend is active; otherwise <c>false</c>.</returns>
        public static bool IsOVRPluginUnityProvidedActivated() => Supplier.IsOVRPluginUnityProvidedActivated();

        /// <summary>
        /// Returns the absolute filesystem path to the root of the Oculus VR utilities directory (parent of the Editor folder).
        /// </summary>
        /// <returns>The absolute path to the Oculus VR root directory.</returns>
        public static string GetUtilitiesRootPath()
        {
            var so = ScriptableObject.CreateInstance(typeof(OVRPluginInfo));
            var script = MonoScript.FromScriptableObject(so);
            string assetPath = AssetDatabase.GetAssetPath(script);

            var editorDir = Directory.GetParent(assetPath);
            if (editorDir == null)
            {
                throw new DirectoryNotFoundException($"Unable to find parent directory of {assetPath}");
            }

            string editorPath = editorDir.FullName;

            var ovrDir = Directory.GetParent(editorPath);
            if (ovrDir == null)
            {
                throw new DirectoryNotFoundException($"Unable to find parent directory of {editorPath}");
            }

            return ovrDir.FullName;
        }

        /// <summary>
        /// Returns the Unity PackageManager PackageInfo for the Meta Core SDK package, or null if not found.
        /// </summary>
        /// <returns>The <see cref="UnityEditor.PackageManager.PackageInfo"/> for the SDK, or <c>null</c> if not installed as a package.</returns>
        public static UnityEditor.PackageManager.PackageInfo GetUtilitiesPackageInfo()
        {
            var so = ScriptableObject.CreateInstance(typeof(OVRPluginInfo));
            var script = MonoScript.FromScriptableObject(so);
            string assetPath = AssetDatabase.GetAssetPath(script);
            return UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        }

        /// <summary>
        /// Returns true if the Meta Core SDK is installed as a UPM package (under Packages/) rather than imported into Assets.
        /// </summary>
        /// <returns><c>true</c> if the SDK asset path starts with "Packages/"; otherwise <c>false</c>.</returns>
        public static bool IsInsidePackageDistribution()
        {
            var so = CreateInstance(typeof(OVRPluginInfo));
            var script = MonoScript.FromScriptableObject(so);
            string assetPath = AssetDatabase.GetAssetPath(script);
            return assetPath.StartsWith("Packages\\", StringComparison.InvariantCultureIgnoreCase) ||
                    assetPath.StartsWith("Packages/", StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Returns the relative path to the OVRPlugin native library directory, adapting for package vs asset store distribution.
        /// </summary>
        /// <returns>A relative path such as "Packages/com.meta.xr.sdk.core/Plugins" or "Assets/Oculus/VR/Plugins".</returns>
        public static string GetPluginRootPath()
        {
            if (IsInsidePackageDistribution())
            {
                return Path.Combine("Packages", PackageName, "Plugins");
            }
            else
            {
                return Path.Combine("Assets", "Oculus", "VR", "Plugins");
            }
        }

        /// <summary>
        /// Returns true if the Core SDK files can be modified (either not a package distribution, or a local package).
        /// </summary>
        /// <returns><c>true</c> if plugin files can be written to (Assets import or local UPM package); otherwise <c>false</c>.</returns>
        public static bool IsCoreSDKModifiable()
        {
            return !IsInsidePackageDistribution() ||
                GetUtilitiesPackageInfo().source == UnityEditor.PackageManager.PackageSource.Local;
        }

        private class OVRPluginInfoStub : IOVRPluginInfoSupplier
        {
            public bool IsOVRPluginOpenXRActivated() => true;

            public bool IsOVRPluginUnityProvidedActivated() => false;
        }
    }
}
