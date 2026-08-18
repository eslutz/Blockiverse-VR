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
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
using System.Runtime.InteropServices;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#endif

namespace Meta.XR.Editor
{
    /// <summary>
    /// Excludes MetaXROperator from Release builds on both Android and Windows.
    /// </summary>
    /// <remarks>
    /// Android: Uses IPreprocessBuildWithReport + SetIncludeInBuildDelegate to exclude the AAR.
    /// Windows: Uses BuildPlayerProcessor (which runs before IPreprocessBuildWithReport) to
    /// disable the OpenXR API layer before the OpenXR plugin's own BuildPlayerProcessor
    /// copies enabled layers into StreamingAssets. The layer is re-enabled after the build.
    /// </remarks>
    internal class MetaXROperatorBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string AarFileName = "XrApiLayer_METAX_operator_unity_android";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android)
            {
                bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
                PreprocessAndroid(isDevelopmentBuild);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
            if (report.summary.platform == BuildTarget.StandaloneWindows64)
                MetaXROperatorBuildPlayerProcessor.RestoreApiLayer();
#endif
            EmitBuiltTelemetry(report);
        }

        // Stage-2 "Built" build-adoption signal: a player build completed on a platform
        // where MetaXROperator ships a binary (Android AAR / Windows OpenXR API layer).
        // MetaXROperator is included only in Development builds (the Android AAR is dev-only,
        // and the Windows API layer is disabled for Release in
        // MetaXROperatorBuildPlayerProcessor), so on these platforms `is_development` is the
        // build-adoption signal. We deliberately do NOT emit on other targets
        // (iOS/macOS/Linux/etc.), where MetaXROperator has no binary, to avoid inflating the
        // metric with builds that could never include the agent. This is a side metric,
        // not an inline funnel stage (the layer loads in-Editor at Play Mode; a player
        // build is off the critical path to first tool call) — see D107596070 sec 3.
        private static void EmitBuiltTelemetry(BuildReport report)
        {
            BuildTarget platform = report.summary.platform;
            if (platform != BuildTarget.Android && platform != BuildTarget.StandaloneWindows64)
            {
                return;
            }

            bool isDevelopmentBuild = (report.summary.options & BuildOptions.Development) != 0;
            // `OnPostprocessBuild` runs before `BuildReport.summary.result` is finalized
            // (it is still `InProgress` at this point — verified via an E2E StandaloneWindows64
            // build), so comparing against `Succeeded` would always report false. Treat the
            // build as successful unless it explicitly failed or was cancelled.
            bool success = report.summary.result != BuildResult.Failed
                && report.summary.result != BuildResult.Cancelled;

            MetaXROperatorTelemetry.SendEvent(
                MetaXROperatorTelemetryConstants.FalcoEventName.Built,
                evt =>
                {
                    evt.SetMetadata(
                        MetaXROperatorTelemetryConstants.AnnotationType.Platform,
                        platform.ToString());
                    evt.SetMetadata(
                        MetaXROperatorTelemetryConstants.AnnotationType.IsDevelopment,
                        isDevelopmentBuild);
                    evt.SetMetadata(
                        MetaXROperatorTelemetryConstants.AnnotationType.Success,
                        success);
                });
        }

        private static void PreprocessAndroid(bool isDevelopmentBuild)
        {
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (!importer.assetPath.Contains(AarFileName))
                    continue;

                if (!importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    continue;

                importer.SetIncludeInBuildDelegate(_ => isDevelopmentBuild);

                if (isDevelopmentBuild)
                    Debug.Log($"[MetaXROperator] Including {AarFileName}.aar in Development build");
                else
                    Debug.Log($"[MetaXROperator] Excluding {AarFileName}.aar from Release build");

                break;
            }
        }
    }

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
    /// <summary>
    /// Disables the MetaXROperator OpenXR API layer before the OpenXR plugin's
    /// BuildPlayerProcessor copies layers into StreamingAssets.
    /// </summary>
    internal class MetaXROperatorBuildPlayerProcessor : BuildPlayerProcessor
    {
        private const string MetaXROperatorLayerName = "XR_APILAYER_METAX_operator";

        // Run before the OpenXR plugin's OpenXRApiLayersBuildProcessor (callbackOrder 0).
        public override int callbackOrder => -1;

        private static bool _disabledLayerForBuild;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            _disabledLayerForBuild = false;

            var target = buildPlayerContext.BuildPlayerOptions.target;
            if (target != BuildTarget.StandaloneWindows64)
                return;

            bool isDevelopmentBuild = (buildPlayerContext.BuildPlayerOptions.options & BuildOptions.Development) != 0;
            if (isDevelopmentBuild)
                return;

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var feature = settings?.GetFeature<ApiLayersFeature>();
            if (feature == null || !feature.enabled)
                return;

            var arch = RuntimeInformation.ProcessArchitecture;
            if (!feature.apiLayers.IsEnabled(MetaXROperatorLayerName, arch))
                return;

            feature.apiLayers.SetEnabled(MetaXROperatorLayerName, arch, false);
            _disabledLayerForBuild = true;
            Debug.Log($"[MetaXROperator] Temporarily disabled {MetaXROperatorLayerName} API layer for Release build");
        }

        internal static void RestoreApiLayer()
        {
            if (!_disabledLayerForBuild)
                return;

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var feature = settings?.GetFeature<ApiLayersFeature>();
            if (feature == null || !feature.enabled)
                return;

            var arch = RuntimeInformation.ProcessArchitecture;
            feature.apiLayers.SetEnabled(MetaXROperatorLayerName, arch, true);
            _disabledLayerForBuild = false;
            Debug.Log($"[MetaXROperator] Re-enabled {MetaXROperatorLayerName} API layer after build");
        }
    }
#endif
}
