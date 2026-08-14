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

using System.Text;
using Meta.XR.Telemetry;

/// <summary>
/// OVRPlugin-based telemetry backend. Extends <see cref="TelemetryBackend"/>
/// and is instantiated as the default backend via <see cref="TelemetryBackend.CreateDefaultBackend"/>.
/// This class is the sole coupling point between Meta.XR.Telemetry and OVRPlugin
/// for telemetry operations.
/// </summary>
internal class OVRPluginTelemetryBackend : TelemetryBackend
{

    // --- Capabilities ---

    public override bool SupportsMetadataHandle =>
        OVRPlugin.version >= OVRPlugin.TelemetryMetadataHandleMinVersion;

    public override bool SupportsMetadataArray =>
        OVRPlugin.version >= OVRPlugin.TelemetryMetadataArrayMinVersion;

    // --- Metadata Handle ---

    public override bool CreateMetadataHandle(out int metadataHandle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetryCreateMetadataHandle(out metadataHandle);
        return result == OVRPlugin.Result.Success;
    }

    // --- SetMetadata (scalar) ---

    public override bool SetMetadata(string key, string value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadata(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadata(string key, int value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataInt(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadata(string key, float value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataFloat(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadata(string key, double value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataDouble(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadata(string key, bool value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataBool(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadata(string key, long value, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataLong(key, value, handle);
        return result == OVRPlugin.Result.Success;
    }

    // --- SetMetadata (array) ---

    public override unsafe bool SetMetadataArray(string key, int* values, int count, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataIntArray(key, values, count, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override unsafe bool SetMetadataArray(string key, long* values, int count, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataLongArray(key, values, count, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override unsafe bool SetMetadataArray(string key, double* values, int count, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataDoubleArray(key, values, count, handle);
        return result == OVRPlugin.Result.Success;
    }

    public override bool SetMetadataArray(string key, string[] values, int count, int handle)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetrySetMetadataStringArray(key, values, count, handle);
        return result == OVRPlugin.Result.Success;
    }

    // --- GetMetadata ---

    public override bool GetMetadata(int handle, StringBuilder buffer, int bufferSize)
    {
        OVRPlugin.Result result = OVRPlugin.TelemetryGetMetadata(handle, buffer, bufferSize);
        return result == OVRPlugin.Result.Success;
    }

    // --- Type Conversions ---

    static TelemetryResult FromOVRPluginResult(OVRPlugin.Result result)
    {
        return result switch
        {
            OVRPlugin.Result.Success => TelemetryResult.Success,
            OVRPlugin.Result.Failure_NotInitialized => TelemetryResult.Failure_NotInitialized,
            OVRPlugin.Result.Failure_Unsupported => TelemetryResult.Failure_Unsupported,
            _ => TelemetryResult.Failure
        };
    }

    // --- Event Sending ---

    public override TelemetryResult SendEvent(UnifiedEventData eventData)
    {
#if OVRPLUGIN_UNSUPPORTED_PLATFORM
        return TelemetryResult.Failure_Unsupported;
#else
        var result = OVRPlugin.SendUnifiedEvent(eventData);
        return FromOVRPluginResult(result);
#endif
    }

    // --- Settings ---

    public override string GetProjectGuid()
    {
        return OVRRuntimeSettings.Instance?.TelemetryProjectGuid ?? string.Empty;
    }

    public override bool? GetConsent()
    {
        return OVRPlugin.UnifiedConsent.GetUnifiedConsent();
    }

    public override bool IsEditorReady()
    {
#if UNITY_EDITOR
        return Meta.XR.Editor.Callbacks.InitializeOnLoad.EditorReady;
#else
        return true;
#endif
    }

    public override bool IsInternalBuild()
    {
        return false;
    }
}
