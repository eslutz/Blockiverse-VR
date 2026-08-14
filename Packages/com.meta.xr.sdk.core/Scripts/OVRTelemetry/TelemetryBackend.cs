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

namespace Meta.XR.Telemetry
{
    /// <summary>
    /// Abstract base class for telemetry backend implementations.
    /// The default backend is wired at compile time via the partial method
    /// <see cref="CreateDefaultBackend"/> (defined in a separate per-consumer file).
    /// Override at runtime by calling <see cref="Register"/>.
    /// </summary>
    public abstract partial class TelemetryBackend
    {
        /// <summary>
        /// The active backend instance. Wired at static init via <see cref="CreateDefaultBackend"/>.
        /// Override at runtime via <see cref="Register"/>.
        /// </summary>
        public static TelemetryBackend Instance { get; private set; } = CreateDefaultBackend();

        /// <summary>
        /// Creates the default backend for this assembly. Implemented in a separate
        /// partial file so each consumer (OVR, ISDK, etc.) can provide its own.
        /// </summary>
        private static partial TelemetryBackend CreateDefaultBackend();

        // --- Metadata Handle ---

        /// <summary>
        /// Creates a new metadata handle for attaching key-value pairs to a telemetry event.
        /// </summary>
        public abstract bool CreateMetadataHandle(out int metadataHandle);

        // --- SetMetadata (scalar) ---

        /// <summary>Sets a string metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, string value, int handle);

        /// <summary>Sets an int metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, int value, int handle);

        /// <summary>Sets a float metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, float value, int handle);

        /// <summary>Sets a double metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, double value, int handle);

        /// <summary>Sets a bool metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, bool value, int handle);

        /// <summary>Sets a long metadata value on the given handle.</summary>
        public abstract bool SetMetadata(string key, long value, int handle);

        // --- SetMetadata (array) ---

        /// <summary>Sets an int array metadata value on the given handle.</summary>
        public abstract unsafe bool SetMetadataArray(string key, int* values, int count, int handle);

        /// <summary>Sets a long array metadata value on the given handle.</summary>
        public abstract unsafe bool SetMetadataArray(string key, long* values, int count, int handle);

        /// <summary>Sets a double array metadata value on the given handle.</summary>
        public abstract unsafe bool SetMetadataArray(string key, double* values, int count, int handle);

        /// <summary>Sets a string array metadata value on the given handle.</summary>
        public abstract bool SetMetadataArray(string key, string[] values, int count, int handle);

        // --- GetMetadata ---

        /// <summary>Reads metadata JSON from the given handle into a buffer.</summary>
        public abstract bool GetMetadata(int handle, StringBuilder buffer, int bufferSize);

        // --- Capabilities ---

        /// <summary>Whether the backend supports native metadata handles.</summary>
        public abstract bool SupportsMetadataHandle { get; }

        /// <summary>Whether the backend supports native array metadata.</summary>
        public abstract bool SupportsMetadataArray { get; }

        // --- Event Sending ---

        /// <summary>Sends a telemetry event through the backend.</summary>
        public abstract TelemetryResult SendEvent(UnifiedEventData eventData);

        // --- Settings ---

        /// <summary>Returns the project GUID for telemetry identification.</summary>
        public abstract string GetProjectGuid();

        /// <summary>Returns the user's telemetry consent status.</summary>
        public abstract bool? GetConsent();

        /// <summary>Whether the editor is ready (e.g., domain reload complete). Returns true at runtime.</summary>
        public abstract bool IsEditorReady();

        /// <summary>Whether this is an internal (non-public) build.</summary>
        public abstract bool IsInternalBuild();

        /// <summary>
        /// Registers a backend instance. First registration wins — subsequent
        /// calls are ignored unless <see cref="Unregister"/> is called first.
        /// </summary>
        public static void Register(TelemetryBackend backend)
        {
            if (backend == null)
                throw new System.ArgumentNullException(nameof(backend));
            if (Instance != null)
                return;
            Instance = backend;
        }

    }
}
