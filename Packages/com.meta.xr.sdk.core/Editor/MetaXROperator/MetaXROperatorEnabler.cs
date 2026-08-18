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

// This enabler handles layer activation via environment variables.
// On OpenXR Plugin < 1.17.0, this is the only path (API Layers feature doesn't exist).
// On macOS with 1.17.0+, this is still required because Unity's API Layers system
// has no macOS platform support (Standalone hardcodes to WindowsPlatformSupport).
#if !UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER || UNITY_EDITOR_OSX

using UnityEditor;
using UnityEngine;
using System;
using System.IO;

namespace Meta.XR.Editor
{
    /// <summary>
    /// Sets OpenXR environment variables to enable the MetaXROperator API layer
    /// when entering Play Mode, but only if MetaXROperator has been activated by the user.
    /// Active on OpenXR Plugin versions before 1.17.0 (API Layers feature doesn't exist)
    /// and on macOS with any version (Unity's API Layers system has no macOS platform support).
    /// </summary>
    [InitializeOnLoad]
    internal static class MetaXROperatorEnabler
    {
        static MetaXROperatorEnabler()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (!MetaXROperatorActivator.IsActivated)
                    return;

                var layerDir = GetPlatformLayerDir();
                if (string.IsNullOrEmpty(layerDir))
                {
                    Debug.LogWarning("[MetaXROperator] Unsupported editor platform for Meta XR Operator layer.");
                    return;
                }

                string absoluteApiLayerPath = Path.GetFullPath(layerDir);
                if (Directory.Exists(absoluteApiLayerPath))
                {
                    Environment.SetEnvironmentVariable("XR_API_LAYER_PATH", absoluteApiLayerPath);
                    Environment.SetEnvironmentVariable("XR_ENABLE_API_LAYERS", "XR_APILAYER_METAX_operator");
                    Debug.Log("[MetaXROperator] XR_APILAYER_METAX_operator enabled for Play Mode.");

                    MetaXROperatorTelemetry.SendEvent(
                        MetaXROperatorTelemetryConstants.FalcoEventName.PlayModeEntered,
                        evt =>
                        {
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.LayerLoaded, true);
                        },
                        isEssential: true);
                }
                else
                {
                    Debug.LogWarning(
                        $"[MetaXROperator] API layer path does not exist: {absoluteApiLayerPath}\n" +
                        "Use the AI Tools setup (Meta > AI Tools) or the Project Setup Tool to install and configure Meta XR Operator.");

                    MetaXROperatorTelemetry.SendEvent(
                        MetaXROperatorTelemetryConstants.FalcoEventName.PlayModeEntered,
                        evt =>
                        {
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.LayerLoaded, false);
                            evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.ErrorMessage,
                                "layer_path_missing");
                        },
                        isEssential: true);
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Environment.SetEnvironmentVariable("XR_API_LAYER_PATH", null);
                Environment.SetEnvironmentVariable("XR_ENABLE_API_LAYERS", null);
                Debug.Log("[MetaXROperator] XR_APILAYER_METAX_operator disabled after Play Mode.");
            }
        }

        private static string GetPlatformLayerDir() => MetaXROperatorPaths.GetPlatformLayerDir();
    }
}

#endif // !UNITY_OPENXR_PLUGIN_1_17_0_OR_NEWER || UNITY_EDITOR_OSX
