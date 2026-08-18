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

#if USING_XR_SDK_OPENXR

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace Meta.XR
{
    public delegate string AgenticToolCallback(string parameters);

    public struct AgenticToolParameter
    {
        /// <summary>Name of the parameter (max 63 UTF-8 bytes).</summary>
        public string Name;

        /// <summary>Description of the parameter (max 1023 UTF-8 bytes).</summary>
        public string Description;

        /// <summary>The data type of the parameter.</summary>
        public XrAgenticExternalToolParameterTypeMETAX1 ParamType;

        /// <summary>Whether this parameter is required.</summary>
        public bool IsRequired;

        /// <summary>When ParamType is Array, the element type of the array items.</summary>
        public XrAgenticExternalToolParameterTypeMETAX1 ArrayItemType;

        /// <summary>When ParamType is Object, a JSON schema string describing the object properties (max 4095 UTF-8 bytes).</summary>
        public string ObjectProperties;
    }
    /// <summary>
    /// Stores the managed callback and tool name so the static native trampoline
    /// can retrieve them via the userDataPtr (GCHandle).
    /// </summary>
    internal class AgenticToolCallbackData
    {
        public AgenticToolCallback Callback;
        public string ToolName;
    }

    public static class MetaXROperatorExternalTool
    {
        private static Dictionary<string, string> _callbackResults = new Dictionary<string, string>();

        private static Dictionary<string, PFN_xrAgenticExternalToolCallbackMETAX1> _nativeCallbacks = new();

        private static List<GCHandle> _callbackHandles = new();

        /// <summary>
        /// Static native callback trampoline compatible with IL2CPP. Retrieves the
        /// managed callback and tool name from the GCHandle passed via userDataPtr.
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(PFN_xrAgenticExternalToolCallbackMETAX1))]
        private static unsafe XrResult NativeCallbackTrampoline(
            byte* resultBuffer, uint resultBufferCapacityInput,
            uint* resultBufferCountOutput, byte* parametersPtr, void* userDataPtr)
        {
            string toolName = null;
            try
            {
                if (userDataPtr == null)
                {
                    Debug.LogError("[MetaXROperatorExternalTools] Pointer to managed callback is null");
                    return XrResult.ErrorRuntimeFailure;
                }

                GCHandle callbackDataHandle = GCHandle.FromIntPtr((IntPtr)userDataPtr);
                if (!callbackDataHandle.IsAllocated)
                {
                    Debug.LogError("[MetaXROperatorExternalTools] Managed callback is not allocated");
                    return XrResult.ErrorRuntimeFailure;
                }

                AgenticToolCallbackData callbackData = (AgenticToolCallbackData)callbackDataHandle.Target;
                toolName = callbackData.ToolName;

                string paramsJson = parametersPtr != null
                    ? Marshal.PtrToStringUTF8((IntPtr)parametersPtr) ?? "{}"
                    : "{}";

                string result;
                if (!_callbackResults.TryGetValue(paramsJson, out result))
                {
                    result = callbackData.Callback(paramsJson);

                    if (result == null)
                    {
                        Debug.LogError($"[MetaXROperatorExternalTools] Callback returned null for tool: '{toolName}'");
                        return XrResult.ErrorRuntimeFailure;
                    }

                    _callbackResults[paramsJson] = result;
                }

                var writeResult = WriteResultToBuffer(result, resultBuffer,
                    resultBufferCapacityInput, resultBufferCountOutput);
                if (writeResult == XrResult.Success)
                    _callbackResults.Remove(paramsJson);

                return writeResult;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaXROperatorExternalTools] Exception during callback execution for '{toolName ?? "unknown"}': {e}");
                return XrResult.ErrorRuntimeFailure;
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                foreach (var handle in _callbackHandles)
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
                _callbackHandles.Clear();
                _nativeCallbacks.Clear();
                _callbackResults.Clear();
            }
        }
#endif

        /// <summary>
        /// Registers an external tool with the agentic system.
        /// </summary>
        /// <param name="toolName">Name of the tool (max 63 UTF-8 bytes)</param>
        /// <param name="toolDescription">Description of what the tool does (max 1023 UTF-8 bytes)</param>
        /// <param name="parameters">Parameter definitions for the tool (may be null or empty)</param>
        /// <param name="callback">
        /// Managed callback invoked when the agentic system calls the tool. Receives the JSON
        /// parameters and user data, and should return a result string (or null on failure).
        /// </param>
        /// <param name="callbackFlags">Flags indicating when the callback can be invoked</param>
        /// <param name="userData">Optional user data forwarded to the callback</param>
        /// <returns>True if registration succeeded, false otherwise</returns>
        public static unsafe XrResult RegisterAgenticTool(
            string toolName,
            string toolDescription,
            AgenticToolParameter[] parameters,
            AgenticToolCallback callback,
            XrAgenticExternalToolCallbackInfoFlagsMETAX1 callbackFlags =
                XrAgenticExternalToolCallbackInfoFlagsMETAX1.ApplicationMainThreadBit
            )
        {
            if (!MetaXRFeature.TryGet(out var feature))
            {
                Debug.LogError("[MetaXRFeature] Feature is unavailable.");
                return XrResult.ErrorFeatureUnsupported;
            }

            // Use the static NativeCallbackTrampoline method instead of a lambda
            // closure — IL2CPP cannot marshal closures to native function pointers.
            PFN_xrAgenticExternalToolCallbackMETAX1 nativeCallback = NativeCallbackTrampoline;

            // Prevent the delegates from being garbage-collected while the runtime holds native pointers.
            if (_nativeCallbacks.TryGetValue(toolName, out PFN_xrAgenticExternalToolCallbackMETAX1 storedCallback))
            {
                Debug.LogError("[MetaXROperatorExternalTools] Tool with same name has already been registered.");
                return XrResult.ErrorValidationFailure;
            }
            else
            {
                _nativeCallbacks[toolName] = nativeCallback;
            }

            // Bundle the managed callback and tool name into a single GCHandle
            // so the static trampoline can retrieve them via userDataPtr.
            var callbackData = new AgenticToolCallbackData { Callback = callback, ToolName = toolName };
            GCHandle managedCallback = GCHandle.Alloc(callbackData);
            _callbackHandles.Add(managedCallback);
            int paramCount = parameters != null ? parameters.Length : 0;

            using var nativeParams = new OVRNativeList<XrAgenticExternalToolParameterMETAX1>(paramCount, Allocator.Temp);

            for (int i = 0; i < paramCount; i++)
            {
                ref var src = ref parameters[i];
                var param = new XrAgenticExternalToolParameterMETAX1
                {
                    Type = XrAgenticExternalToolParameterMETAX1.StructureType,
                    Next = null,
                    ParamType = src.ParamType,
                    IsRequired = (XrBool32)src.IsRequired,
                    ArrayItemType = src.ArrayItemType,
                };

                if (!string.IsNullOrEmpty(src.Name))
                {
                    int byteCount = Encoding.UTF8.GetByteCount(src.Name);
                    if (byteCount >= XrAgenticExternalToolParameterMETAX1.MaxNameLength)
                    {
                        throw new ArgumentException($"Parameter name exceeds maximum length of {XrAgenticExternalToolParameterMETAX1.MaxNameLength - 1} bytes.", nameof(src.Name));
                    }
                    fixed (char* pName = src.Name)
                    {
                        int bytesWritten = Encoding.UTF8.GetBytes(pName, src.Name.Length, param.ParamName, XrAgenticExternalToolParameterMETAX1.MaxNameLength);
                        param.ParamName[bytesWritten] = 0;
                    }
                }
                else
                {
                    param.ParamName[0] = 0;
                }

                if (!string.IsNullOrEmpty(src.Description))
                {
                    int byteCount = Encoding.UTF8.GetByteCount(src.Description);
                    if (byteCount >= XrAgenticExternalToolParameterMETAX1.MaxDescriptionLength)
                    {
                        throw new ArgumentException($"Parameter description exceeds maximum length of {XrAgenticExternalToolParameterMETAX1.MaxDescriptionLength - 1} bytes.", nameof(src.Description));
                    }
                    fixed (char* pDesc = src.Description)
                    {
                        int bytesWritten = Encoding.UTF8.GetBytes(pDesc, src.Description.Length, param.ParamDescription, XrAgenticExternalToolParameterMETAX1.MaxDescriptionLength);
                        param.ParamDescription[bytesWritten] = 0;
                    }
                }
                else
                {
                    param.ParamDescription[0] = 0;
                }

                if (!string.IsNullOrEmpty(src.ObjectProperties))
                {
                    int byteCount = Encoding.UTF8.GetByteCount(src.ObjectProperties);
                    if (byteCount >= XrAgenticExternalToolParameterMETAX1.MaxObjectPropertiesLength)
                    {
                        throw new ArgumentException($"Object properties exceeds maximum length of {XrAgenticExternalToolParameterMETAX1.MaxObjectPropertiesLength - 1} bytes.", nameof(src.ObjectProperties));
                    }
                    fixed (char* pObjProp = src.ObjectProperties)
                    {
                        int bytesWritten = Encoding.UTF8.GetBytes(pObjProp, src.ObjectProperties.Length, param.ObjectProperties, XrAgenticExternalToolParameterMETAX1.MaxObjectPropertiesLength);
                        param.ObjectProperties[bytesWritten] = 0;
                    }
                }
                else
                {
                    param.ObjectProperties[0] = 0;
                }

                nativeParams.Add(param);
            }

            return feature.RegisterAgenticExternalTool(
                toolName,
                toolDescription,
                nativeParams.Data,
                (uint)paramCount,
                nativeCallback,
                callbackFlags,
                managedCallback);
        }

        /// <summary>
        /// Helper to write a UTF-8 result string into a pre-allocated native buffer.
        /// Follows the OpenXR two-call idiom: returns ErrorSizeInsufficient when the buffer is too small.
        /// </summary>
        public static unsafe XrResult WriteResultToBuffer(
            string content,
            byte* resultBuffer,
            uint resultBufferCapacityInput,
            uint* resultBufferCountOutput)
        {
            int byteCount = Encoding.UTF8.GetByteCount(content);
            uint requiredSize = (uint)(byteCount + 1);

            if (resultBufferCountOutput != null)
            {
                *resultBufferCountOutput = requiredSize;
            }

            if (requiredSize > resultBufferCapacityInput)
            {
                return XrResult.ErrorSizeInsufficient;
            }

            fixed (char* pContent = content)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(pContent, content.Length, resultBuffer, (int)resultBufferCapacityInput);
                resultBuffer[bytesWritten] = 0;
            }

            return XrResult.Success;
        }
    }
}

#endif // USING_XR_SDK_OPENXR
