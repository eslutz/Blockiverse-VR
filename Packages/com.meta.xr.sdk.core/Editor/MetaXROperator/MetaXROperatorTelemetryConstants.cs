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

namespace Meta.XR.Editor
{
    internal static partial class MetaXROperatorTelemetryConstants
    {
        internal static partial class FalcoEventName
        {
            // Discovery
            public const string Discovered = "agentic_xr_discovered";

            // Activation
            public const string Activated = "agentic_xr_activated";
            public const string Deactivated = "agentic_xr_deactivated";

            // Play Mode
            public const string PlayModeEntered = "agentic_xr_play_mode_entered";

            // Build — emitted from MetaXROperatorBuildProcessor.OnPostprocessBuild when a
            // player build completes (MetaXROperator ships only in Development builds).
            public const string Built = "agentic_xr_built";
        }

        internal static partial class AnnotationType
        {
            public const string Source = "source";
            public const string Success = "success";
            public const string ErrorMessage = "error_message";
            public const string BinariesPresent = "binaries_present";
            public const string IsActivated = "is_activated";
            public const string LayerLoaded = "layer_loaded";
            public const string ActivationResult = "activation_result";
            public const string SessionId = "session_id";

            // Build telemetry (agentic_xr_built). MetaXROperator is included only in
            // Development builds on supported targets (Android/Windows), so
            // `is_development` is the build-adoption signal; a dedicated
            // `agentic_included` column was dropped as a verbatim alias of it.
            public const string Platform = "platform";
            public const string IsDevelopment = "is_development";
        }

        internal static partial class Source
        {
            public const string Menu = "menu";
            public const string Pst = "pst";
        }

        internal static class ActivationResult
        {
            public const string Success = "success";
            public const string NoBinaries = "no_binaries";
            public const string AlreadyActive = "already_active";
            public const string AlreadyInactive = "already_inactive";
        }
    }
}
