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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Meta.XR.Util
{
    /// <summary>
    /// Extension methods for <see cref="Assembly"/> that provide safe type loading.
    /// </summary>
    internal static class AssemblyExtensions
    {
        /// <summary>
        /// Returns all types from the assembly, recovering gracefully when some types
        /// cannot be loaded.
        /// </summary>
        /// <remarks>
        /// <see cref="Assembly.Load(byte[])"/> loses file-based binding context, so types
        /// that depend on assemblies not yet loaded (e.g. test mocks) may be unresolvable.
        /// This method catches <see cref="ReflectionTypeLoadException"/> and returns only
        /// the types that loaded successfully.
        /// </remarks>
        /// <param name="assembly">The assembly to retrieve types from.</param>
        /// <returns>An array of all successfully loaded types.</returns>
        public static IEnumerable<Type> TryGetTypes(this Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
        }
    }
}
