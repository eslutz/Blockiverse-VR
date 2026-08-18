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

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// Wraps a value that can be temporarily overridden while preserving the original.
    /// </summary>
    /// <typeparam name="T">The type of the value being wrapped.</typeparam>
    public class Overridable<T>
    {
        private readonly T _originalValue;
        private T _overrideValue;

        /// <summary>
        /// Gets whether the value is currently overridden.
        /// </summary>
        public bool IsOverriden { get; private set; }

        public Overridable(T originalValue)
        {
            _originalValue = originalValue;
        }

        /// <summary>
        /// Gets the current value, returning the override if set or the original otherwise.
        /// </summary>
        public T Value => IsOverriden ? _overrideValue : _originalValue;

        /// <summary>
        /// Sets an override value, replacing the original value until the override is removed.
        /// </summary>
        /// <param name="overrideValue">The value to use as the override.</param>
        public void SetOverride(T overrideValue)
        {
            if (overrideValue is null)
            {
                RemoveOverride();
                return;
            }

            _overrideValue = overrideValue;
            IsOverriden = true;
        }

        /// <summary>
        /// Removes the current override, restoring the original value.
        /// </summary>
        public void RemoveOverride()
        {
            IsOverriden = false;
        }

        /// <summary>
        /// Implicitly converts an Overridable to its underlying value.
        /// </summary>
        /// <param name="overridable">The overridable instance to convert.</param>
        /// <returns>The current value of the overridable.</returns>
        public static implicit operator T(Overridable<T> overridable) => overridable.Value;
        /// <summary>
        /// Implicitly wraps a value in a new Overridable instance.
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        /// <returns>A new Overridable wrapping the given value.</returns>
        public static implicit operator Overridable<T>(T value) => new(value);
        /// <summary>
        /// Returns the string representation of the current value.
        /// </summary>
        /// <returns>The string representation, or an empty string if the value is null.</returns>
        public override string ToString() => Value?.ToString() ?? string.Empty;
    }
}
