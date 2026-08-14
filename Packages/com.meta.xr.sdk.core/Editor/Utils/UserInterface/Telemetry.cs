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

using System.Runtime.CompilerServices;
using Meta.XR.Editor.Id;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static OVRTelemetry;

namespace Meta.XR.Editor.UserInterface
{
    internal static class Telemetry
    {
        [Markers]
        public static class MarkerId
        {
            public const int LinkClick = 163057622;
            public const int PageOpen = 163063708;
            public const int PageClose = 163065149;
        }

        public static class FalcoEventName
        {
            public const string LinkClick = "EDITOR_LINK_CLICKED";
            public const string PageOpen = "EDITOR_PAGE_OPEN";
            public const string PageClose = "EDITOR_PAGE_CLOSE";
        }

        public static class AnnotationType
        {
            public const string Label = "label";
            public const string Url = "url";
            public const string Type = "type";
            public const string Origin = "origin";
            public const string OriginData = "origin_data";
            public const string Action = "action";
            public const string ActionData = "action_data";
            public const string ActionType = "action_type";
            public const string Value = "value";
            public const string SubOrigin = "sub_origin";
        }
    }

    /// <summary>
    /// Generic interaction and impression telemetry for RLDS UI components. Mirrors the legacy
    /// <see cref="LinkDescription"/> Falco contract so any RLDS widget emits the same
    /// EDITOR_LINK_CLICKED / EDITOR_PAGE_OPEN / EDITOR_PAGE_CLOSE schema the IMGUI path produced,
    /// keeping the editor-engagement funnels populated without per-feature telemetry calls.
    /// </summary>
    /// <remarks>
    /// A widget reports an interaction from its own click handler via <see cref="SendInteraction"/>;
    /// every event carries component identity (action_type + action + label). The owning surface tags
    /// one container with <see cref="SetScope"/>, and descendant widgets that do not carry their own
    /// origin resolve the surface and navigation path (origin + origin_data) from it automatically.
    /// </remarks>
    internal static class RLDSTelemetry
    {
        private sealed class Scope
        {
            public Origins Origin;
            public string PathId;
        }

        // Weakly keyed so a scoped container is never kept alive by the telemetry layer.
        private static readonly ConditionalWeakTable<VisualElement, Scope> Scopes = new();

        /// <summary>
        /// Tags <paramref name="element"/> as a telemetry scope identifying a surface and navigation
        /// path. Descendant RLDS widgets without their own origin inherit these values.
        /// </summary>
        public static void SetScope(VisualElement element, Origins origin, string pathId)
        {
            if (element == null)
            {
                return;
            }

            Scopes.Remove(element);
            Scopes.Add(element, new Scope { Origin = origin, PathId = pathId });
        }

        /// <summary>
        /// Emits an EDITOR_LINK_CLICKED event for a generic RLDS interaction (click, selection or
        /// toggle). <paramref name="origin"/> and <paramref name="originData"/> fall back to the
        /// nearest ambient scope when not supplied, so the event always carries both component
        /// identity and the navigation path it occurred on.
        /// </summary>
        public static void SendInteraction(
            VisualElement source,
            string actionType,
            string action,
            string label,
            Origins? origin = null,
            string originData = null,
            string value = null,
            string actionData = null)
        {
            if ((origin == null || string.IsNullOrEmpty(originData)) && TryResolveScope(source, out var scope))
            {
                origin ??= scope.Origin;
                if (string.IsNullOrEmpty(originData))
                {
                    originData = scope.PathId;
                }
            }

            var unifiedEvent = new Meta.XR.Telemetry.UnifiedEventData(Telemetry.FalcoEventName.LinkClick)
            {
                isEssential = false,
                productType = Meta.XR.Telemetry.TelemetryProductType.Editor
            };
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.Label, label);
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.Action, string.IsNullOrEmpty(action) ? label : action);
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.ActionType, actionType);
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.Origin, (origin ?? Origins.Unknown).ToString());

            if (!string.IsNullOrEmpty(originData))
            {
                unifiedEvent.SetMetadata(Telemetry.AnnotationType.OriginData, originData);
            }

            if (!string.IsNullOrEmpty(actionData))
            {
                unifiedEvent.SetMetadata(Telemetry.AnnotationType.ActionData, actionData);
            }

            if (value != null)
            {
                unifiedEvent.SetMetadata(Telemetry.AnnotationType.Value, value);
            }

            unifiedEvent.Send();
        }

        /// <summary>
        /// Emits an EDITOR_PAGE_OPEN impression for an RLDS surface, mirroring the GuideWindow
        /// page-open contract so plain EditorWindow surfaces are counted in the same funnel.
        /// </summary>
        public static void SendPageOpen(Origins origin, string id, string actionType, string label = null)
        {
            SendPageEvent(Telemetry.FalcoEventName.PageOpen, true, origin, id, actionType, label);
        }

        /// <summary>
        /// Emits an EDITOR_PAGE_CLOSE impression for an RLDS surface.
        /// </summary>
        public static void SendPageClose(Origins origin, string id, string actionType, string label = null)
        {
            SendPageEvent(Telemetry.FalcoEventName.PageClose, false, origin, id, actionType, label);
        }

        private static void SendPageEvent(
            string eventName,
            bool essential,
            Origins origin,
            string id,
            string actionType,
            string label)
        {
            var unifiedEvent = new Meta.XR.Telemetry.UnifiedEventData(eventName)
            {
                isEssential = essential,
                productType = Meta.XR.Telemetry.TelemetryProductType.Editor
            };
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.Origin, origin.ToString());
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.Action, origin.ToString());
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.ActionData, id);
            unifiedEvent.SetMetadata(Telemetry.AnnotationType.ActionType, actionType);

            if (!string.IsNullOrEmpty(label))
            {
                unifiedEvent.SetMetadata(Telemetry.AnnotationType.Label, label);
            }

            unifiedEvent.Send();
        }

        private static bool TryResolveScope(VisualElement element, out Scope scope)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (Scopes.TryGetValue(current, out scope))
                {
                    return true;
                }
            }

            scope = null;
            return false;
        }
    }

    /// <summary>
    /// Base class for RLDS editor windows that emits impression telemetry — EDITOR_PAGE_OPEN when the
    /// window first opens and EDITOR_PAGE_CLOSE when it is destroyed — so plain EditorWindow surfaces
    /// participate in the page funnel the same way <c>GuideWindow</c> does.
    /// </summary>
    internal abstract class RLDSEditorWindow : EditorWindow
    {
        // Serialized so a single user-initiated open is not re-counted on every domain reload, where
        // OnEnable runs again for the still-open window.
        [SerializeField] private bool _impressionLogged;

        /// <summary>Stable identifier reported as the page event's origin_data / action_data.</summary>
        protected abstract string TelemetryId { get; }

        /// <summary>Origin reported for this surface's page-open event.</summary>
        protected virtual Origins TelemetryOrigin => Origins.GuidedSetup;

        protected virtual void OnEnable()
        {
            if (_impressionLogged)
            {
                return;
            }

            _impressionLogged = true;
            RLDSTelemetry.SendPageOpen(TelemetryOrigin, TelemetryId, GetType().Name, titleContent?.text);
        }

        protected virtual void OnDestroy()
        {
            if (!_impressionLogged)
            {
                return;
            }

            RLDSTelemetry.SendPageClose(TelemetryOrigin, TelemetryId, GetType().Name, titleContent?.text);
        }
    }
}
