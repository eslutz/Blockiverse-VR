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
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Guides.Editor.Welcome
{
    internal static class WelcomeSettings
    {
        public const string PackageName = "com.meta.xr.sdk.core";
        public const int WindowWidth = 1024;
        public const int WindowHeight = 768;
        public const string WindowTitle = "Welcome to Meta XR SDK";
        public const string ReleaseNotesUrl = "https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk";

        internal static class Content
        {
            public const string CoverTitle = "Meta XR SDK";
            public const string CoverSubtitle = "Build immersive experiences for Quest and Meta devices with Unity.";
            public const string OpenSdkMenuLabel = "Open SDK menu";
            public const string ViewReleaseNotesLabel = "View release notes";
            public const string ResourcesHeader = "Resources";
            public const string XrToolsHeader = "XR tools";
            public const string ShowOnLaunchLabel = "Show this window on launch";
            public const string CloseLabel = "Close";
        }

        internal static class Resources
        {
            public static readonly ResourceDef[] All =
            {
                // In-editor action (no external-link icon).
                new("Building Blocks",
                    "Pre-built XR components you can drag and drop into your scene.",
                    "Browse building blocks",
                    () => EditorApplication.ExecuteMenuItem("Meta/Tools/Building Blocks")),
                // Web links (external-link icon appended automatically).
                new("Samples and Showcases",
                    "Working examples and best practices for Meta XR development.",
                    "Browse samples",
                    "https://developers.meta.com/horizon/code-samples/unity"),
                new("Building with Unity",
                    "Guides and tutorials to get started with Meta XR SDK.",
                    "Read documentation",
                    "https://developers.meta.com/horizon/develop/unity"),
                new("API Reference",
                    "Detailed API documentation for all Meta XR SDK modules.",
                    "View API reference",
                    "https://developers.meta.com/horizon/reference/unity"),
            };
        }

        internal readonly struct ResourceDef
        {
            public readonly string Label;
            public readonly string Description;
            public readonly string LinkText;
            public readonly Action LinkAction;

            // True when the link opens an external web page. The card appends an
            // external-link icon for these (and only these) — see BuildResourcesSection.
            public readonly bool OpensUrl;

            // Link that runs a custom in-editor action (no external-link icon).
            public ResourceDef(string label, string description, string linkText, Action linkAction)
            {
                Label = label;
                Description = description;
                LinkText = linkText;
                LinkAction = linkAction;
                OpensUrl = false;
            }

            // Link that opens a web page in the browser (external-link icon appended).
            public ResourceDef(string label, string description, string linkText, string url)
            {
                Label = label;
                Description = description;
                LinkText = linkText;
                LinkAction = () => Application.OpenURL(url);
                OpensUrl = true;
            }
        }

        internal static class XrTools
        {
            public static readonly XrToolDef[] All =
            {
                new("AI Tools",
                    "Connect AI tools to Meta XR SDK to build XR content with prompts and get project optimization recommendations.",
                    registryNameHint: "AI Tools",
                    isActive: () => FindToolInfoText("AI Tools") == "Connected",
                    activeBadgeText: "Connected", inactiveBadgeText: "Not connected",
                    activeCtaText: "View settings", inactiveCtaText: "Setup",
                    activeAction: () => OpenTool("AI Tools"),
                    inactiveAction: () => OpenTool("AI Tools")),
                new("Runtime optimizer",
                    "Identify performance bottlenecks in XR apps and get actionable optimization recommendations. Enable to analyze whether apps are CPU or GPU bound.",
                    registryNameHint: "Runtime Optimizer",
                    isActive: () => FindToolEnabled("Runtime Optimizer"),
                    activeBadgeText: "Enabled", inactiveBadgeText: "Disabled",
                    activeCtaText: "Launch", inactiveCtaText: "Enable",
                    activeAction: () => OpenToolByHint("Runtime Optimizer"),
                    inactiveAction: () => OpenToolByHint("Runtime Optimizer"),
                    windowsOnly: true),
                new("Immersive debugger",
                    "Debug XR apps inside the headset. Enable to activate in the Meta XR SDK menu.",
                    registryNameHint: "Immersive Debugger",
                    isActive: () => FindToolEnabled("Immersive Debugger"),
                    activeBadgeText: "Enabled", inactiveBadgeText: "Disabled",
                    activeCtaText: "Launch", inactiveCtaText: "Enable",
                    activeAction: () => OpenToolByHint("Immersive Debugger"),
                    inactiveAction: () => OpenToolByHint("Immersive Debugger")),
                new("Meta XR Simulator",
                    "Test XR apps on your computer without a headset.",
                    registryNameHint: "Meta XR Simulator",
                    isActive: ExternalToolDetection.IsXRSimInstalled,
                    activeBadgeText: "Installed", inactiveBadgeText: "Not installed",
                    activeCtaText: "Open", inactiveCtaText: "Download",
                    activeAction: () => Application.OpenURL("xrsim://"),
                    inactiveAction: () => Application.OpenURL(ExternalToolDetection.PlatformDownloadUrl(
                        "https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-mac-arm/",
                        "https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-windows/"))),
                new("Meta Quest Developer Hub",
                    "Manage devices, install builds, and view logs.",
                    registryNameHint: "Meta Quest Developer Hub",
                    isActive: ExternalToolDetection.IsODHInstalled,
                    activeBadgeText: "Installed", inactiveBadgeText: "Not installed",
                    activeCtaText: "Open", inactiveCtaText: "Download",
                    activeAction: () =>
                    {
                        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        var v3Exe = System.IO.Path.Combine(programFiles, "Meta Quest Developer Hub", "Meta Quest Developer Hub.exe");
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var v2Exe = System.IO.Path.Combine(localAppData, "Programs", "oculus-developer-hub", "Meta Quest Developer Hub.exe");
                        var exePath = System.IO.File.Exists(v3Exe) ? v3Exe : v2Exe;
                        ExternalToolDetection.TryOpenApp(
                            exePath,
                            "Meta Quest Developer Hub",
                            "https://developers.meta.com/horizon/documentation/unity/ts-odh-getting-started");
                    },
                    inactiveAction: () => Application.OpenURL(ExternalToolDetection.PlatformDownloadUrl(
                        "https://developers.meta.com/horizon/downloads/package/oculus-developer-hub-mac/",
                        "https://developers.meta.com/horizon/downloads/package/oculus-developer-hub-win/"))),
                new("Meta Quest Link",
                    "Connect Quest headset to PC for VR streaming.",
                    registryNameHint: "Meta Quest Link",
                    isActive: ExternalToolDetection.IsOculusLinkInstalled,
                    activeBadgeText: "Installed", inactiveBadgeText: "Not installed",
                    activeCtaText: "Open", inactiveCtaText: "Download",
                    activeAction: () => Application.OpenURL("https://developers.meta.com/horizon/documentation/unity/unity-link/#set-up-meta-horizon-link"),
                    inactiveAction: () => Application.OpenURL("https://developers.meta.com/horizon/documentation/unity/unity-link/#set-up-meta-horizon-link"),
                    windowsOnly: true),
                new("RenderDoc",
                    "Debug and optimize graphics with frame-level analysis.",
                    registryNameHint: "RenderDoc",
                    isActive: ExternalToolDetection.IsRenderDocInstalled,
                    activeBadgeText: "Installed", inactiveBadgeText: "Not installed",
                    activeCtaText: "Open", inactiveCtaText: "Download",
                    activeAction: () =>
                    {
                        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        var metaForkExe = System.IO.Path.Combine(programFiles, "RenderDocForMetaQuest", "qrenderdoc.exe");
                        var standardExe = System.IO.Path.Combine(programFiles, "RenderDoc", "qrenderdoc.exe");
                        var exePath = System.IO.File.Exists(metaForkExe) ? metaForkExe : standardExe;
                        ExternalToolDetection.TryOpenApp(
                            exePath,
                            "RenderDoc",
                            "https://renderdoc.org");
                    },
                    inactiveAction: () => Application.OpenURL(ExternalToolDetection.PlatformDownloadUrl(
                        "https://developers.meta.com/horizon/downloads/package/renderdoc-meta-fork-for-mac-installer/",
                        "https://developers.meta.com/horizon/downloads/package/renderdoc-oculus/"))),
                new("Meta Haptics Studio",
                    "Design and test haptic feedback for controllers.",
                    registryNameHint: "Meta Haptics Studio",
                    isActive: ExternalToolDetection.IsHapticsStudioInstalled,
                    activeBadgeText: "Installed", inactiveBadgeText: "Not installed",
                    activeCtaText: "Open", inactiveCtaText: "Download",
                    activeAction: () =>
                    {
                        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var standaloneExe = System.IO.Path.Combine(localAppData, "Programs", "meta-haptics-studio", "Meta Haptics Studio.exe");
                        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var odhExe = System.IO.Path.Combine(appData, "odh", "packages", "tools", "meta-haptics-studio-win", "Meta Haptics Studio.exe");
                        var exePath = System.IO.File.Exists(standaloneExe) ? standaloneExe : odhExe;
                        ExternalToolDetection.TryOpenApp(
                            exePath,
                            "Meta Haptics Studio",
                            "https://developer.oculus.com/resources/haptics-overview/");
                    },
                    inactiveAction: () => Application.OpenURL(ExternalToolDetection.PlatformDownloadUrl(
                        "https://developers.meta.com/horizon/downloads/package/meta-haptics-studio-macos/",
                        "https://developers.meta.com/horizon/downloads/package/meta-haptics-studio-win/"))),
            };

            private static string FindToolInfoText(string nameHint)
            {
                var descriptor = ToolRegistry.Registry
                    .FirstOrDefault(t => t.Name != null && t.Name.Contains(nameHint));
                return descriptor?.InfoTextDelegate?.Invoke().Item1;
            }

            // Reads the tool's actual enablement state from its ToolDescriptor
            // (the SDK menu's EnablementDescriptor) rather than the InfoText string,
            // which no longer carries an "Enabled" label after the status-menu redesign.
            private static bool FindToolEnabled(string nameHint)
            {
                var descriptor = ToolRegistry.Registry
                    .FirstOrDefault(t => t.Name != null && t.Name.Contains(nameHint));
                return descriptor?.EnablementDescriptor?.Invoke().Item1 ?? false;
            }

            private static void OpenTool(string toolId)
            {
                var descriptor = ToolRegistry.Registry
                    .FirstOrDefault(t => t.Id == toolId);
                descriptor?.OnClickDelegate?.Invoke(Origins.GuidedSetup);
            }

            private static void OpenToolByHint(string nameHint)
            {
                var descriptor = ToolRegistry.Registry
                    .FirstOrDefault(t => t.Name != null && t.Name.Contains(nameHint));
                descriptor?.OnClickDelegate?.Invoke(Origins.GuidedSetup);
            }
        }

        internal readonly struct XrToolDef
        {
            public readonly string Label;
            public readonly string Description;
            public readonly string RegistryNameHint;
            public readonly Func<bool> IsActive;
            public readonly string ActiveBadgeText;
            public readonly string InactiveBadgeText;
            public readonly string ActiveCtaText;
            public readonly string InactiveCtaText;
            public readonly Action ActiveAction;
            public readonly Action InactiveAction;

            // Tools that only exist on Windows (e.g. Runtime Optimizer, Meta Quest Link) —
            // hidden on non-Windows editors. See BuildXrToolsSection.
            public readonly bool WindowsOnly;

            public XrToolDef(
                string label, string description,
                string registryNameHint,
                Func<bool> isActive,
                string activeBadgeText, string inactiveBadgeText,
                string activeCtaText, string inactiveCtaText,
                Action activeAction, Action inactiveAction,
                bool windowsOnly = false)
            {
                Label = label;
                Description = description;
                RegistryNameHint = registryNameHint;
                IsActive = isActive;
                ActiveBadgeText = activeBadgeText;
                InactiveBadgeText = inactiveBadgeText;
                ActiveCtaText = activeCtaText;
                InactiveCtaText = inactiveCtaText;
                ActiveAction = activeAction;
                InactiveAction = inactiveAction;
                WindowsOnly = windowsOnly;
            }
        }
    }
}
