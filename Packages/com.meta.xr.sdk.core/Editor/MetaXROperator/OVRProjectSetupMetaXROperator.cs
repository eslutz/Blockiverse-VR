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

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
using System.Runtime.InteropServices;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#endif

namespace Meta.XR.Editor
{
    /// <summary>
    /// PST (Project Setup Tool) rules for MetaXROperator integration.
    /// Validates that the MetaXROperator OpenXR API layer is properly configured.
    /// Rules are only shown when MetaXROperator has been activated by the user via the
    /// Meta/Meta XR Operator menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class OVRProjectSetupMetaXROperator
    {
        private const string MetaXROperatorLayerName = "XR_APILAYER_METAX_operator";

        // TODO(T274404258): Point this at the dedicated public "Meta XR Operator Getting Started"
        // page once it is published. Until then, reuse the already-public learn-more URL so
        // the rule keeps a working external link.
        private const string WikiUrl = AIToolsSetupStrings.Links.MetaXROperatorLearnMoreUrl;

        static OVRProjectSetupMetaXROperator()
        {
#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER || UNITY_EDITOR_OSX
            // OpenXR is fresh enough for the API Layers path, or macOS uses the
            // env-var path in MetaXROperatorEnabler that works on any OpenXR version.
            AddMetaXROperatorRecommendedRule();
#endif
#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER && !UNITY_EDITOR_OSX
            AddApiLayersFeatureEnabledRule();
            AddAgenticLayerRegisteredRule();
#elif !UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER && !UNITY_EDITOR_OSX
            AddOpenXRPluginTooOldRule();
#endif
            // On macOS with 1.17.0+: no API Layers rules needed.
            // Layer activation is handled via environment variables in MetaXROperatorEnabler.
        }

        /// <summary>
        /// Whether MetaXROperator should show PST rules. Requires both:
        /// 1. The user has activated MetaXROperator via the menu toggle.
        /// 2. Native binaries are present for the current editor platform.
        /// </summary>
        private static bool ShouldValidate()
        {
            return MetaXROperatorActivator.IsActivated && MetaXROperatorActivator.AreBinariesPresent();
        }

        /// <summary>
        /// Rule: Recommend enabling MetaXROperator when binaries are available but the
        /// feature has not been activated yet. Uses the Recommended level so it
        /// appears as a non-intrusive suggestion that users can dismiss.
        /// </summary>
        private static void AddMetaXROperatorRecommendedRule()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Features,
                platform: BuildTargetGroup.Standalone,
                conditionalValidity: _ => !MetaXROperatorActivator.IsActivated
                    && MetaXROperatorActivator.AreBinariesPresent(),
                isDone: _ => MetaXROperatorActivator.IsActivated,
                message: "Meta XR Operator is available but not activated. " +
                         "Enable it to allow AI agents to observe and interact with your VR scene during Play Mode. " +
                         "You can activate it via Meta > Meta XR Operator > Activate.",
                fix: _ =>
                {
                    MetaXROperatorTelemetry.SendEvent(
                        MetaXROperatorTelemetryConstants.FalcoEventName.Discovered,
                        evt =>
                        {
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source,
                                MetaXROperatorTelemetryConstants.Source.Pst);
                        },
                        isEssential: true);
                    MetaXROperatorActivator.Activate(MetaXROperatorTelemetryConstants.Source.Pst);
                },
                fixMessage: "Activate Meta XR Operator",
                url: WikiUrl
            );
        }

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER && !UNITY_EDITOR_OSX
        // On macOS these rules are excluded: Unity's OpenXR API Layers system has no macOS
        // platform support (TryAdd hardcodes WindowsPlatformSupport for Standalone).
        // macOS uses the environment-variable approach via MetaXROperatorEnabler instead.

        /// <summary>
        /// Rule: The API Layers feature must be enabled in OpenXR settings.
        /// Required for MetaXROperator layer to be loaded by the OpenXR runtime.
        /// </summary>
        private static void AddApiLayersFeatureEnabledRule()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Features,
                platform: BuildTargetGroup.Standalone,
                conditionalValidity: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    return settings != null;
                },
                isDone: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    var feature = settings?.GetFeature<ApiLayersFeature>();
                    return feature != null && feature.enabled;
                },
                message: "Meta XR Operator requires the API Layers feature to be enabled in OpenXR settings. " +
                         "Go to Project Settings > XR Plug-in Management > OpenXR and enable \"API Layers\".",
                fix: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    var feature = settings?.GetFeature<ApiLayersFeature>();
                    if (feature != null)
                    {
                        feature.enabled = true;
                        FeatureHelpers.RefreshFeatures(buildTargetGroup);
                    }
                },
                fixMessage: "Enable API Layers feature in OpenXR settings"
            );
        }

        /// <summary>
        /// Rule: The MetaXROperator layer JSON must be registered in API Layers settings.
        /// After enabling the feature, the user must add the layer JSON manifest.
        /// </summary>
        private static void AddAgenticLayerRegisteredRule()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Features,
                platform: BuildTargetGroup.Standalone,
                conditionalValidity: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    var feature = settings?.GetFeature<ApiLayersFeature>();
                    return feature != null && feature.enabled;
                },
                isDone: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    var feature = settings?.GetFeature<ApiLayersFeature>();
                    if (feature == null || !feature.enabled)
                        return false;
                    return feature.apiLayers.IsEnabled(
                        MetaXROperatorLayerName,
                        RuntimeInformation.ProcessArchitecture);
                },
                message: "Meta XR Operator API layer is not registered. " +
                         "The layer JSON must be added to OpenXR > API Layers for your platform in order for Meta XR Operator to work.",
                fix: buildTargetGroup =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                    var feature = settings?.GetFeature<ApiLayersFeature>();
                    if (feature == null || !feature.enabled)
                    {
                        Debug.LogWarning("[MetaXROperator] ApiLayersFeature not found or not enabled. " +
                            "Fix the \"API Layers\" rule first.");
                        return;
                    }

                    var arch = RuntimeInformation.ProcessArchitecture;

                    // Re-enable if already registered but disabled
                    feature.apiLayers.SetEnabled(MetaXROperatorLayerName, arch, true);
                    if (feature.apiLayers.IsEnabled(MetaXROperatorLayerName, arch))
                    {
                        EditorUtility.SetDirty(feature);
                        return;
                    }

                    // Locate the JSON manifest for registration
                    string jsonPath = MetaXROperatorPaths.GetLayerManifestForRegistration();
                    if (string.IsNullOrEmpty(jsonPath))
                    {
                        Debug.LogWarning("[MetaXROperator] Could not locate or prepare the API layer manifest.");
                        return;
                    }

                    if (feature.apiLayers.TryAdd(jsonPath, arch, buildTargetGroup, out _))
                    {
                        // Explicitly enable the layer after adding — TryAdd may
                        // register it in a disabled state, which would cause
                        // isDone (IsEnabled) to remain false.
                        feature.apiLayers.SetEnabled(MetaXROperatorLayerName, arch, true);
                        EditorUtility.SetDirty(feature);
                    }
                    else
                    {
                        Debug.LogWarning($"[MetaXROperator] Failed to register API layer from {jsonPath}.");
                    }
                },
                fixMessage: "Register Meta XR Operator API layer in OpenXR settings"
            );
        }
#endif // UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER && !UNITY_EDITOR_OSX

#if !UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
        /// <summary>
        /// Rule: OpenXR Plugin must be version 1.17.0 or newer.
        /// Older versions lack the API Layers feature required by MetaXROperator.
        /// </summary>
        private static void AddOpenXRPluginTooOldRule()
        {
            const string openXRPackageName = "com.unity.xr.openxr";
            const string minVersion = "1.17.0";

            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Packages,
                platform: BuildTargetGroup.Standalone,
                conditionalValidity: _ => PackageList.PackageManagerListAvailable
                    && PackageList.IsPackageInstalled(openXRPackageName),
                isDone: _ => false,
                message: $"Meta XR Operator requires OpenXR Plugin version {minVersion} or newer. " +
                         "Please upgrade the OpenXR Plugin package in the Package Manager.",
                fix: _ =>
                {
                    if (Application.isBatchMode)
                    {
                        IssueTracker.TrackWarning(IssueTracker.SDK.ProjectSetupTool,
                            "ovr-project-setup-fix-skipped-batchmode",
                            $"Skipping Package Manager UI (no graphics device in batch mode). Upgrade {openXRPackageName} manually or via the command line.");
                        return;
                    }
                    UnityEditor.PackageManager.UI.Window.Open(openXRPackageName);
                },
                fixAutomatic: false,
                fixMessage: "Open Package Manager to upgrade the OpenXR Plugin"
            );
        }
#endif

        private static string GetPlatformLayerDir() => MetaXROperatorPaths.GetPlatformLayerDir();
    }
}
