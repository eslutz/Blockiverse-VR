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

namespace Meta.XR
{
    /// <summary>
    /// Defines scripting preprocessor constants used by the Meta XR SDK for conditional compilation.
    /// </summary>
    public class ScriptingDefineConstants
    {
        /// <summary>
        /// Scripting define symbol indicating that the Unity Input System package is installed.
        /// </summary>
        public const string InputSystemScriptDefine = "USING_INPUT_SYSTEM_PACKAGE";
        /// <summary>
        /// Scripting define symbol used to disable hand pinch-to-button input mapping in OVRInput.
        /// </summary>
        public const string InputMappingScriptDefine = "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING";
    }
}
