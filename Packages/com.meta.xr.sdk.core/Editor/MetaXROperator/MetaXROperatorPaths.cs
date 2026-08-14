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
using System.IO;
using UnityEditor.PackageManager;

namespace Meta.XR.Editor
{
    /// <summary>
    /// Shared path utilities for MetaXROperator editor integration.
    /// </summary>
    internal static class MetaXROperatorPaths
    {
        private const string CoreSdkPackagePath = "Packages/com.meta.xr.sdk.core";
        private const string AssetsEditorBasePath = "Assets/Oculus/VR/Editor";
        private const string OculusInternalMetaXROperatorSubdir = "OculusInternal/MetaXROperator";
        private const string LegacyMetaXROperatorSubdir = "MetaXROperator";

        internal static string GetPlatformLayerDir()
        {
            string platformSubdir;
#if UNITY_EDITOR_WIN
            platformSubdir = "x64";
#elif UNITY_EDITOR_OSX
            platformSubdir = "mac-arm64";
#elif UNITY_EDITOR_LINUX
            platformSubdir = "linux-x64";
#else
            return null;
#endif
            foreach (var editorBase in EnumerateEditorBasePaths())
            {
                // Prefer the OculusInternal layout that ships with the
                // package cache; fall back to the legacy layout.
                var internalDir = Path.Combine(editorBase, OculusInternalMetaXROperatorSubdir, platformSubdir);
                if (Directory.Exists(internalDir))
                    return internalDir;

                var legacyDir = Path.Combine(editorBase, LegacyMetaXROperatorSubdir, platformSubdir);
                if (Directory.Exists(legacyDir))
                    return legacyDir;
            }

            // Nothing found on disk — return a plausible path for messaging.
            return Path.Combine(GetDefaultEditorBasePath(), OculusInternalMetaXROperatorSubdir, platformSubdir);
        }

        private static IEnumerable<string> EnumerateEditorBasePaths()
        {
            // Resolve the physical UPM package location. The virtual
            // "Packages/com.meta.xr.sdk.core" path is not a real directory
            // when Unity serves the package out of Library/PackageCache, so
            // Directory.Exists() against it returns false.
            var resolvedPackagePath = TryGetCorePackageResolvedPath();
            if (!string.IsNullOrEmpty(resolvedPackagePath))
                yield return Path.Combine(resolvedPackagePath, "Editor");

            // Virtual UPM path — useful when AssetDatabase can resolve it
            // (e.g. embedded packages) and harmless otherwise.
            yield return Path.Combine(CoreSdkPackagePath, "Editor");

            // Assets-imported development layout.
            yield return AssetsEditorBasePath;
        }

        private static string GetDefaultEditorBasePath()
        {
            var resolvedPackagePath = TryGetCorePackageResolvedPath();
            if (!string.IsNullOrEmpty(resolvedPackagePath))
                return Path.Combine(resolvedPackagePath, "Editor");
            return Path.Combine(CoreSdkPackagePath, "Editor");
        }

        private static string TryGetCorePackageResolvedPath()
        {
            var packageInfo = PackageInfo.FindForAssetPath(CoreSdkPackagePath);
            return packageInfo?.resolvedPath;
        }

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
        /// <summary>
        /// Returns the absolute path to a layer JSON manifest suitable for
        /// <see cref="UnityEngine.XR.OpenXR.ApiLayers.TryAdd"/>.
        /// </summary>
        /// <returns>Absolute path to the JSON manifest, or <c>null</c> if not available.</returns>
        internal static string GetLayerManifestForRegistration()
        {
            var platformDir = GetPlatformLayerDir();
            if (string.IsNullOrEmpty(platformDir) || !Directory.Exists(platformDir))
                return null;

            string absDir = Path.GetFullPath(platformDir);
            var jsonFiles = Directory.GetFiles(absDir, "*.json");
            if (jsonFiles.Length == 0)
                return null;

            return Path.GetFullPath(jsonFiles[0]);
        }
#endif
    }
}
