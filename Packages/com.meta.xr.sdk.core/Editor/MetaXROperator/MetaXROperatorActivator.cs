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

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
using System.Runtime.InteropServices;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#endif

namespace Meta.XR.Editor
{
    /// <summary>
    /// Manages the activation state of the MetaXROperator OpenXR API layer.
    /// When activated, the layer is registered in OpenXR API Layers settings, enabled
    /// during Play Mode, and PST rules validate the configuration.
    /// When deactivated, the layer is removed from OpenXR settings and PST rules are hidden.
    /// </summary>
    internal static class MetaXROperatorActivator
    {
        private const string EditorPrefKey = "Meta.XR.MetaXROperator.Activated";
        private const string MetaXROperatorLayerName = "XR_APILAYER_METAX_operator";

        // Legacy activation-state key from before the AgenticXR -> MetaXROperator
        // rename. Migrated forward once (static ctor) so users who activated before
        // the rename stay activated rather than silently reverting to inactive.
        private const string LegacyEditorPrefKey = "Meta.XR.AgenticXR.Activated";

        static MetaXROperatorActivator()
        {
            if (!EditorPrefs.HasKey(EditorPrefKey) && EditorPrefs.HasKey(LegacyEditorPrefKey))
            {
                EditorPrefs.SetBool(EditorPrefKey, EditorPrefs.GetBool(LegacyEditorPrefKey, false));
                EditorPrefs.DeleteKey(LegacyEditorPrefKey);
            }
        }


        /// <summary>
        /// Whether MetaXROperator has been activated by the user.
        /// </summary>
        internal static bool IsActivated
        {
            get => EditorPrefs.GetBool(EditorPrefKey, false);
            private set => EditorPrefs.SetBool(EditorPrefKey, value);
        }

        /// <summary>
        /// Whether MetaXROperator native binaries are present for the current editor platform.
        /// </summary>
        internal static bool AreBinariesPresent()
        {
            var platformDir = MetaXROperatorPaths.GetPlatformLayerDir();
            return !string.IsNullOrEmpty(platformDir) && Directory.Exists(platformDir);
        }

        /// <summary>
        /// Activate MetaXROperator. Registers the API layer in OpenXR settings so it will be
        /// loaded on the next Play Mode entry. PST rules will validate the configuration.
        /// </summary>
        internal static void Activate(string source = null)
        {
            if (!AreBinariesPresent())
            {
                Debug.LogWarning(
                    "[MetaXROperator] Cannot activate: Meta XR Operator native binaries were not found for this platform.\n" +
                    "Use the AI Tools setup (Meta > AI Tools) or the Project Setup Tool to install and configure Meta XR Operator.");

                MetaXROperatorTelemetry.SendEvent(
                    MetaXROperatorTelemetryConstants.FalcoEventName.Activated,
                    evt =>
                    {
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Success, false);
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ActivationResult,
                            MetaXROperatorTelemetryConstants.ActivationResult.NoBinaries);
                        if (source != null)
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source, source);
                    },
                    isEssential: true);
                return;
            }

            if (IsActivated)
            {
                Debug.Log("[MetaXROperator] Already activated.");

                MetaXROperatorTelemetry.SendEvent(
                    MetaXROperatorTelemetryConstants.FalcoEventName.Activated,
                    evt =>
                    {
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Success, false);
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ActivationResult,
                            MetaXROperatorTelemetryConstants.ActivationResult.AlreadyActive);
                        if (source != null)
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source, source);
                    },
                    isEssential: true);
                return;
            }

            MetaXROperatorTelemetry.GenerateSessionId();
            IsActivated = true;
            AddOpenXRLayer();
            Debug.Log("[MetaXROperator] Activated. The API layer will be enabled on next Play Mode entry.");

            MetaXROperatorTelemetry.SendEvent(
                MetaXROperatorTelemetryConstants.FalcoEventName.Activated,
                evt =>
                {
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Success, true);
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ActivationResult,
                        MetaXROperatorTelemetryConstants.ActivationResult.Success);
                    if (source != null)
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source, source);
                },
                isEssential: true);
        }

        /// <summary>
        /// Deactivate MetaXROperator. Removes the API layer from OpenXR settings so it will
        /// no longer be loaded during Play Mode. PST rules will be hidden.
        /// </summary>
        internal static void Deactivate(string source = null)
        {
            if (!IsActivated)
            {
                Debug.Log("[MetaXROperator] Already deactivated.");

                MetaXROperatorTelemetry.SendEvent(
                    MetaXROperatorTelemetryConstants.FalcoEventName.Deactivated,
                    evt =>
                    {
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Success, false);
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ActivationResult,
                            MetaXROperatorTelemetryConstants.ActivationResult.AlreadyInactive);
                        if (source != null)
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source, source);
                    });
                return;
            }

            RemoveOpenXRLayer();
            IsActivated = false;
            Debug.Log("[MetaXROperator] Deactivated. The API layer has been removed from OpenXR settings.");

            MetaXROperatorTelemetry.SendEvent(
                MetaXROperatorTelemetryConstants.FalcoEventName.Deactivated,
                evt =>
                {
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Success, true);
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ActivationResult,
                        MetaXROperatorTelemetryConstants.ActivationResult.Success);
                    if (source != null)
                        evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source, source);
                });

            MetaXROperatorTelemetry.SessionId = null;
        }

#if UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER
        private static void AddOpenXRLayer()
        {
#if UNITY_EDITOR_OSX
            // Unity's API Layers system has no macOS platform support
            // (Standalone hardcodes to WindowsPlatformSupport, which only accepts .dll).
            // Layer activation is handled via environment variables in MetaXROperatorEnabler.
            return;
#else
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                Debug.LogError("[MetaXROperator] OpenXR settings not found for Standalone platform. Use the Project Setup Tool to validate your configuration.");
                return;
            }

            var feature = settings.GetFeature<ApiLayersFeature>();
            if (feature == null)
            {
                Debug.LogError("[MetaXROperator] ApiLayersFeature not found in OpenXR settings. Use the Project Setup Tool to validate your configuration.");
                return;
            }

            if (!feature.enabled)
            {
                feature.enabled = true;
                FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            }

            var arch = RuntimeInformation.ProcessArchitecture;

            if (feature.apiLayers.IsEnabled(MetaXROperatorLayerName, arch))
                return;

            // If the layer is registered but disabled, re-enable it
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
                Debug.LogWarning("[MetaXROperator] Could not locate the API layer manifest. " +
                    "Use the Project Setup Tool to validate your configuration.");
                return;
            }

            if (feature.apiLayers.TryAdd(jsonPath, arch, BuildTargetGroup.Standalone, out _))
            {
                EditorUtility.SetDirty(feature);
            }
            else
            {
                Debug.LogWarning($"[MetaXROperator] Failed to register API layer from {jsonPath}. Use the Project Setup Tool to validate your configuration.");
            }
#endif // !UNITY_EDITOR_OSX
        }

        private static void RemoveOpenXRLayer()
        {
#if UNITY_EDITOR_OSX
            // No layer to remove — macOS uses environment variables, not API Layers.
            return;
#else
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var feature = settings?.GetFeature<ApiLayersFeature>();
            if (feature == null || !feature.enabled)
                return;

            if (feature.apiLayers.TryRemove(MetaXROperatorLayerName, RuntimeInformation.ProcessArchitecture))
            {
                EditorUtility.SetDirty(feature);
            }
#endif // !UNITY_EDITOR_OSX
        }
#else
        private static void AddOpenXRLayer() { }
        private static void RemoveOpenXRLayer() { }
#endif
    }
}
