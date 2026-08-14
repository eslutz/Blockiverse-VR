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

using Meta.XR.Editor.Utils;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Registers hand-readiness compatibility checks using the existing OVRProjectSetup task system.
    /// These tasks will appear in both the Project Setup Tool and the Hand Readiness Tool.
    /// </summary>
    [InitializeOnLoad]
    internal static class HandReadinessSetupTasks
    {
        /// <summary>
        /// Task group for hand-readiness tasks.
        /// </summary>
        internal const OVRProjectSetup.TaskGroup HandReadinessTaskGroup = OVRProjectSetup.TaskGroup.HandReadiness;

        /// <summary>
        /// Interaction SDK package name.
        /// </summary>
        private const string InteractionSDKPackageName = "com.meta.xr.sdk.interaction";

        /// <summary>
        /// Minimum required version for Interaction SDK.
        /// </summary>
        private const string MinInteractionSDKVersion = "205.0.0";

        // Per-project, per-user opt-in flag stored in Library/EditorUserSettings.asset
        // (not version-controlled). Flipped to "true" when the user runs their first
        // scan via HandReadinessToolWindow.RunScan(). When true, the hand-tracking
        // task escalates to Required severity for this project; otherwise it stays
        // Recommended so it doesn't render as a red error for controller-only
        // projects that never engaged with the tool.
        private const string OptInConfigKey = "HandReadiness.UserOptedIn";

        internal static void MarkUserOptedIn()
        {
            EditorUserSettings.SetConfigValue(OptInConfigKey, "true");
        }

        private static bool HasUserOptedIn()
        {
            return EditorUserSettings.GetConfigValue(OptInConfigKey) == "true";
        }

        static HandReadinessSetupTasks()
        {
            // Hand tracking only needs OVRProjectConfig (OVR core), not ISDK,
            // so it registers unconditionally.
            OVRProjectSetup.AddTask(
                group: HandReadinessTaskGroup,
                conditionalLevel: _ => HasUserOptedIn()
                    ? OVRProjectSetup.TaskLevel.Required
                    : OVRProjectSetup.TaskLevel.Recommended,
                conditionalValidity: _ => true,
                isDone: _ =>
                {
                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    return projectConfig != null &&
                           projectConfig.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersOnly;
                },
                fix: _ =>
                {
                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    if (projectConfig != null)
                    {
                        projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                        EditorUtility.SetDirty(projectConfig);
                        AssetDatabase.SaveAssets();
                    }
                },
                message: $"Hand Tracking must be enabled. {HandReadinessConstants.DeviceName} requires hand tracking support " +
                         "for the best user experience. Enable 'Controllers and Hands' or 'Hands Only' in Project Settings.",
                fixMessage: "Enable hand tracking (Controllers and Hands)"
            );

#if USING_META_XR_INTERACTION_SDK
            // Register Interaction SDK version requirement
            OVRProjectSetup.AddTask(
                group: HandReadinessTaskGroup,
                level: OVRProjectSetup.TaskLevel.Recommended,
                conditionalValidity: _ => PackageList.PackageManagerListAvailable,
                isDone: _ => PackageList.IsPackageInstalledWithValidVersion(
                    $"{InteractionSDKPackageName}@>={MinInteractionSDKVersion}"),
                fix: _ =>
                {
                    if (OVRSilentMode.IsEnabled)
                    {
                        IssueTracker.TrackWarning(IssueTracker.SDK.ProjectSetupTool, "ovr-project-setup-fix-skipped-silent",
                            $"Skipping Package Manager UI (silent mode). Install {InteractionSDKPackageName} manually or via the command line.");
                        return;
                    }
                    // Open Package Manager to the Interaction SDK package
                    UnityEditor.PackageManager.UI.Window.Open(InteractionSDKPackageName);
                },
                fixAutomatic: false, // Can't auto-update packages, just open Package Manager
                message: $"Interaction SDK version {MinInteractionSDKVersion} or higher is required. " +
                         $"{HandReadinessConstants.DeviceName} requires the latest Meta Interaction SDK ({InteractionSDKPackageName}) for full " +
                         $"compatibility with {HandReadinessConstants.DeviceName} input and interaction features.",
                fixMessage: "Open Package Manager to update Interaction SDK"
            );
#endif
        }

        /// <summary>
        /// Checks if a task belongs to the hand-readiness task group.
        /// </summary>
        internal static bool IsHandReadinessTask(OVRConfigurationTask task, BuildTargetGroup buildTarget)
        {
            return task.Group == HandReadinessTaskGroup;
        }
    }
}
