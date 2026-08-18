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
using UnityEngine;

namespace Meta.XR.Editor
{
    /// <summary>
    /// Unity menu items for MetaXROperator activation and status.
    /// </summary>
    internal static class MetaXROperatorMenu
    {
        private const string MenuPath = "Meta/Meta XR Operator";
        private const string ActivateMenuPath = MenuPath + "/Activate";
        private const string DeactivateMenuPath = MenuPath + "/Deactivate";
        private const string StatusMenuPath = MenuPath + "/Status";

        [MenuItem(ActivateMenuPath, false, 1)]
        public static void Activate()
        {
            MetaXROperatorActivator.Activate(MetaXROperatorTelemetryConstants.Source.Menu);
        }

        [MenuItem(ActivateMenuPath, true)]
        public static bool ValidateActivate()
        {
            return MetaXROperatorActivator.AreBinariesPresent() && !MetaXROperatorActivator.IsActivated;
        }

        [MenuItem(DeactivateMenuPath, false, 2)]
        public static void Deactivate()
        {
            MetaXROperatorActivator.Deactivate(MetaXROperatorTelemetryConstants.Source.Menu);
        }

        [MenuItem(DeactivateMenuPath, true)]
        public static bool ValidateDeactivate()
        {
            return MetaXROperatorActivator.AreBinariesPresent() && MetaXROperatorActivator.IsActivated;
        }

        [MenuItem(StatusMenuPath, false, 4)]
        public static void ShowStatus()
        {
            bool binariesPresent = MetaXROperatorActivator.AreBinariesPresent();
            bool isActive = MetaXROperatorActivator.IsActivated;
            string layerDir = MetaXROperatorPaths.GetPlatformLayerDir();

            string status = "Meta XR Operator Status:\n" +
                            $"\u2022 Binaries Present: {(binariesPresent ? "Yes" : "No")}\n" +
                            $"\u2022 Activated: {(isActive ? "Yes" : "No")}\n" +
                            $"\u2022 Layer Path: {layerDir}";

            Debug.Log($"[MetaXROperator] {status}");

            MetaXROperatorTelemetry.SendEvent(
                MetaXROperatorTelemetryConstants.FalcoEventName.Discovered,
                evt =>
                {
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.Source,
                        MetaXROperatorTelemetryConstants.Source.Menu);
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.BinariesPresent,
                        binariesPresent);
                    evt.SetMetadata(MetaXROperatorTelemetryConstants.AnnotationType.IsActivated,
                        isActive);
                },
                isEssential: true);

            EditorUtility.DisplayDialog("Meta XR Operator Status", status, "OK");
        }
    }
}
