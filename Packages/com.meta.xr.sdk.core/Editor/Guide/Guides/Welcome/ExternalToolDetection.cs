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
using Meta.XR.Simulator.Editor;
using UnityEngine;

namespace Meta.XR.Guides.Editor.Welcome
{
    internal static class ExternalToolDetection
    {
        public static bool IsXRSimInstalled()
        {
            return XRSimInstallationDetector.IsXRSim2Installed();
        }

        public static bool IsODHInstalled()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Directory.Exists(Path.Combine(programFiles, "Meta Quest Developer Hub")))
                    return true;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (Directory.Exists(Path.Combine(localAppData, "Programs", "oculus-developer-hub")))
                    return true;
                return false;
            }
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return Directory.Exists("/Applications/Meta Quest Developer Hub.app");
            }
            return false;
        }

        public static bool IsOculusLinkInstalled()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Directory.Exists(Path.Combine(programFiles, "Oculus"));
            }
            return false;
        }

        public static bool IsRenderDocInstalled()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Directory.Exists(Path.Combine(programFiles, "RenderDocForMetaQuest"))
                    || Directory.Exists(Path.Combine(programFiles, "RenderDoc"));
            }
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return Directory.Exists("/Applications/RenderDoc.app");
            }
            return false;
        }

        public static bool IsHapticsStudioInstalled()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (Directory.Exists(Path.Combine(localAppData, "Programs", "meta-haptics-studio")))
                    return true;
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (Directory.Exists(Path.Combine(appData, "odh", "packages", "tools", "meta-haptics-studio-win")))
                    return true;
                return false;
            }
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return Directory.Exists("/Applications/Meta Haptics Studio.app");
            }
            return false;
        }

        public static void TryOpenApp(string windowsExePath, string macAppName, string fallbackUrl)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor && File.Exists(windowsExePath))
            {
                System.Diagnostics.Process.Start(windowsExePath);
                return;
            }
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                System.Diagnostics.Process.Start("open", $"-a \"{macAppName}\"");
                return;
            }
            Application.OpenURL(fallbackUrl);
        }

        public static string PlatformDownloadUrl(string macUrl, string windowsUrl)
        {
            return SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX
                ? macUrl
                : windowsUrl;
        }
    }
}
