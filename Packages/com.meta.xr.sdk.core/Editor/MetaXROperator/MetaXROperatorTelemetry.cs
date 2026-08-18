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
using Meta.XR.Telemetry;

namespace Meta.XR.Editor
{
    internal static class MetaXROperatorTelemetry
    {
        private static string _sessionId;

        internal static string SessionId
        {
            get => _sessionId;
            set => _sessionId = value;
        }

        internal static void GenerateSessionId()
        {
            _sessionId = Guid.NewGuid().ToString();
        }

        public static UnifiedEventData AddMetaXROperatorContext(
            this UnifiedEventData eventData)
        {
            eventData.productType = TelemetryProductType.Editor;
            if (!string.IsNullOrEmpty(_sessionId))
            {
                eventData.SetMetadata(
                    MetaXROperatorTelemetryConstants.AnnotationType.SessionId,
                    _sessionId);
            }
            return eventData;
        }

        public static void SendEvent(string eventName,
            Action<UnifiedEventData> configureEvent = null,
            bool isEssential = false)
        {
            try
            {
                var unifiedEvent = new UnifiedEventData(eventName)
                {
                    isEssential = isEssential
                };
                unifiedEvent.AddMetaXROperatorContext();
                configureEvent?.Invoke(unifiedEvent);
                unifiedEvent.Send();
            }
            catch
            {
                // Telemetry errors should never surface to users
            }
        }
    }
}
