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

#if UNITY_EDITOR_OSX

using System;
using System.Diagnostics;
using System.IO;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// macOS-specific implementation for detecting Meta XR Simulator installation.
    /// Uses mdfind (Spotlight) to locate the app regardless of install location.
    /// </summary>
    internal class MacOSXRSimInstallationDetector : IXRSimInstallationDetector
    {
        private const string ApplicationsPath = "/Applications";
        private const string MetaXRSimulatorAppName = "MetaXRSimulator.app";
        private static readonly string DefaultMetaXRSimulatorPath = Path.Combine(ApplicationsPath, MetaXRSimulatorAppName);

        private static bool _cachedAppPathResolved;
        private static string _cachedAppPath;

        public bool IsInstalled()
        {
            return !string.IsNullOrEmpty(GetOpenXRRuntimeDirectory());
        }

        /// <summary>
        /// Gets the installation directory of Meta XR Simulator on macOS
        /// </summary>
        /// <returns>The installation directory path, or null if not found</returns>
        public string GetOpenXRRuntimeDirectory()
        {
            var appPath = GetAppPath();
            if (appPath != null && Directory.Exists(appPath))
            {
                // We need to get in Contents/Resources/MetaXRSimulator as the calling methods are expecting to find
                // meta_openxr_simulator.json in the root directory after calling this method
                return Path.Combine(appPath, "Contents", "Resources", "MetaXRSimulator");
            }

            return null;
        }

        private static string GetAppPath()
        {
            if (_cachedAppPathResolved)
            {
                return _cachedAppPath;
            }

            if (Directory.Exists(DefaultMetaXRSimulatorPath))
            {
                _cachedAppPath = DefaultMetaXRSimulatorPath;
                _cachedAppPathResolved = true;
                return _cachedAppPath;
            }

            _cachedAppPath = FindAppWithMdfind();
            _cachedAppPathResolved = true;
            return _cachedAppPath;
        }

        private static string FindAppWithMdfind()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/mdfind",
                        Arguments = $"-name '{MetaXRSimulatorAppName}' kind:App",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (string.IsNullOrEmpty(output))
                    {
                        return null;
                    }

                    // mdfind returns one path per line; use the first match
                    string firstResult = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    return string.IsNullOrEmpty(firstResult) ? null : firstResult;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

#endif
