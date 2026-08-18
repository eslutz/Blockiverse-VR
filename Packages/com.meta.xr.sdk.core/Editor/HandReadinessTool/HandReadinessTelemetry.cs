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
using Meta.XR.AI.AgentBridge;
using Meta.XR.Telemetry;
using UnityEditor;
using AgentBridgeSettings = Meta.XR.AI.AgentBridge.Settings;

namespace Meta.HandReadinessTool.Editor
{
    /// <summary>
    /// Fire-and-forget Falco telemetry wrapper. Errors are swallowed so
    /// telemetry can never surface to the user.
    /// </summary>
    internal static class HandReadinessTelemetry
    {
        private const string PrefKeySessionId = "HandReadiness_TelemetrySessionId";

        /// <summary>
        /// Per-tool-window session id. Survives domain reloads via EditorPrefs;
        /// reset to a fresh value on every `EnsureSessionId()` call when the
        /// EditorPref is missing. Cleared on `ResetSession()`.
        /// </summary>
        internal static string SessionId { get; private set; }

        /// <summary>
        /// Ensure a session id exists. Called from the tool window's OnEnable.
        /// Returns true iff a fresh id was generated (i.e. the caller should
        /// treat this as the start of a new session and emit `hrt_opened`).
        /// </summary>
        internal static bool EnsureSessionId()
        {
            var existing = EditorPrefs.GetString(PrefKeySessionId, "");
            if (!string.IsNullOrEmpty(existing))
            {
                SessionId = existing;
                return false;
            }
            SessionId = Guid.NewGuid().ToString();
            EditorPrefs.SetString(PrefKeySessionId, SessionId);
            return true;
        }

        /// <summary>
        /// Clear the persisted session id. Called from the tool window's
        /// OnDestroy so the next open starts a new session.
        /// </summary>
        internal static void ResetSession()
        {
            SessionId = null;
            EditorPrefs.DeleteKey(PrefKeySessionId);
        }

        /// <summary>
        /// Attach ambient metadata that every event carries: session id and
        /// AgentBridge state.
        /// </summary>
        internal static UnifiedEventData AddHandReadinessContext(this UnifiedEventData eventData)
        {
            eventData.productType = TelemetryProductType.Editor;

            if (!string.IsNullOrEmpty(SessionId))
            {
                eventData.SetMetadata(HandReadinessTelemetryConstants.AnnotationType.SessionId, SessionId);
            }

            try
            {
                eventData.SetMetadata(
                    HandReadinessTelemetryConstants.AnnotationType.AgentBridgeEnabled,
                    AgentBridgeSettings.IsEnabled);

                if (AgentBridgeSettings.IsEnabled)
                {
                    // Use AgentBridgeAPI.GetCurrentServiceName instead of poking at
                    // AgentBridgeSettings.SelectedServiceId.Value directly — the
                    // latter has type UserString which lives in Meta.XR.Editor.Settings,
                    // an assembly HRT doesn't reference. GetCurrentServiceName is
                    // already the accessor ProjectDescriptionScreen uses for the
                    // "Active service provider" row, so we stay consistent.
                    var providerName = AgentBridgeAPI.GetCurrentServiceName();
                    if (!string.IsNullOrEmpty(providerName) && providerName != "None")
                    {
                        eventData.SetMetadata(
                            HandReadinessTelemetryConstants.AnnotationType.AgentBridgeProvider,
                            providerName);
                    }
                }
            }
            catch
            {
                // AgentBridge settings can throw during early Editor init — never
                // bubble that up to a telemetry caller.
            }

            return eventData;
        }

        /// <summary>
        /// Fire a Falco event with the given name. `configureEvent` runs after
        /// the ambient annotations have been attached. All exceptions caught.
        /// </summary>
        public static void SendEvent(
            string eventName,
            Action<UnifiedEventData> configureEvent = null,
            bool isEssential = false)
        {
            try
            {
                var evt = new UnifiedEventData(eventName)
                {
                    isEssential = isEssential
                };
                evt.AddHandReadinessContext();
                configureEvent?.Invoke(evt);
                evt.Send();
            }
            catch
            {
                // Telemetry errors must never surface to users.
            }
        }
    }
}
