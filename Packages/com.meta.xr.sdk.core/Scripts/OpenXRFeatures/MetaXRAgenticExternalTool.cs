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
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Meta.XR
{
    /// <summary>
    /// Callback delegate for external tool invocations.
    /// </summary>
    /// <param name="resultBuffer">Buffer to write the result string</param>
    /// <param name="resultBufferCapacityInput">Capacity of the result buffer</param>
    /// <param name="resultBufferCountOutput">Pointer to receive the actual size written</param>
    /// <param name="parameters">JSON string containing the tool parameters</param>
    /// <param name="userData">User data pointer passed during registration</param>
    /// <returns>XrResult indicating success or failure</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate XrResult PFN_xrAgenticExternalToolCallbackMETAX1(
        byte* resultBuffer,
        uint resultBufferCapacityInput,
        uint* resultBufferCountOutput,
        byte* parameters,
        void* userData);

#if USING_XR_SDK_OPENXR
    partial class MetaXRFeature
    {
        /// <summary>
        /// Registers an external tool with the agentic system.
        /// </summary>
        /// <param name="toolName">Name of the tool</param>
        /// <param name="toolDescription">Description of what the tool does</param>
        /// <param name="parameters">Pointer to parameter definitions</param>
        /// <param name="parameterCount">Number of parameters</param>
        /// <param name="callback">Callback function that will be invoked when the tool is called</param>
        /// <param name="callbackFlags">Flags indicating when the callback can be invoked</param>
        /// <param name="userData">Optional user data to pass to the callback</param>
        /// <returns>Result indicating success or failure</returns>
        public unsafe XrResult RegisterAgenticExternalTool(
            string toolName,
            string toolDescription,
            XrAgenticExternalToolParameterMETAX1* parameters,
            uint parameterCount,
            PFN_xrAgenticExternalToolCallbackMETAX1 callback,
            XrAgenticExternalToolCallbackInfoFlagsMETAX1 callbackFlags,
            GCHandle userData)
        {
            if (Instance == 0)
                return XrResult.ErrorHandleInvalid;

            if (!_agenticExternalToolEnabled)
                return XrResult.ErrorExtensionNotPresent;

            if (Command.xrAgenticRegisterExternalToolMETAX1 == null)
            {
                LogError("xrAgenticRegisterExternalToolMETAX1 command was not loaded.");
                return XrResult.ErrorFunctionUnsupported;
            }

            var registerInfo = new XrAgenticExternalToolRegisterInfoMETAX1
            {
                Type = XrAgenticExternalToolRegisterInfoMETAX1.StructureType,
                Next = null,
                ToolParameterCount = parameterCount,
                ToolParameters = parameters,
                ToolCallback = callback,
                ToolCallbackInfoFlags = callbackFlags,
                ToolUserData = GCHandle.ToIntPtr(userData).ToPointer()
            };

            // Copy tool name (max 64 bytes including null terminator)
            if (!string.IsNullOrEmpty(toolName))
            {
                int byteCount = Encoding.UTF8.GetByteCount(toolName);
                if (byteCount >= XrAgenticExternalToolRegisterInfoMETAX1.MaxNameLength)
                {
                    throw new ArgumentException($"Tool name exceeds maximum length of {XrAgenticExternalToolRegisterInfoMETAX1.MaxNameLength - 1} bytes.", nameof(toolName));
                }
                fixed (char* pToolName = toolName)
                {
                    int bytesWritten = Encoding.UTF8.GetBytes(pToolName, toolName.Length, registerInfo.ToolName, XrAgenticExternalToolRegisterInfoMETAX1.MaxNameLength);
                    registerInfo.ToolName[bytesWritten] = 0;
                }
            }
            else
            {
                registerInfo.ToolName[0] = 0;
            }

            // Copy tool description (max 1024 bytes including null terminator)
            if (!string.IsNullOrEmpty(toolDescription))
            {
                int byteCount = Encoding.UTF8.GetByteCount(toolDescription);
                if (byteCount >= XrAgenticExternalToolRegisterInfoMETAX1.MaxDescriptionLength)
                {
                    throw new ArgumentException($"Tool description exceeds maximum length of {XrAgenticExternalToolRegisterInfoMETAX1.MaxDescriptionLength - 1} bytes.", nameof(toolDescription));
                }
                fixed (char* pToolDesc = toolDescription)
                {
                    int bytesWritten = Encoding.UTF8.GetBytes(pToolDesc, toolDescription.Length, registerInfo.ToolDescription, XrAgenticExternalToolRegisterInfoMETAX1.MaxDescriptionLength);
                    registerInfo.ToolDescription[bytesWritten] = 0;
                }
            }
            else
            {
                registerInfo.ToolDescription[0] = 0;
            }

            return Command.xrAgenticRegisterExternalToolMETAX1(Instance, in registerInfo);
        }
    }
#endif // USING_XR_SDK_OPENXR
}
