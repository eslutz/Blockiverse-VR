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
using System.Reflection;

namespace Meta.XR.Editor.Reflection
{
    /// <summary>
    /// Utility for filtering assemblies during reflection-based discovery operations.
    /// Provides a consolidated check to skip well-known system and platform assemblies
    /// that will never contain user-defined attributes or types.
    /// </summary>
    public static class AssemblyFilter
    {
        /// <summary>
        /// Determines whether the specified assembly is a system or platform assembly
        /// that should be skipped during reflection-based type/attribute discovery.
        /// </summary>
        /// <remarks>
        /// This covers:
        /// - .NET runtime assemblies (System.*, Microsoft.*, mscorlib, netstandard)
        /// - Unity engine assemblies (UnityEngine.*)
        /// - Unity editor assemblies (UnityEditor.*)
        /// - Unity package framework assemblies (Unity.*)
        ///
        /// Skipping these assemblies during reflection scanning is a performance optimization
        /// that avoids scanning thousands of types that will never contain user-defined attributes.
        /// </remarks>
        /// <param name="assembly">The assembly to check</param>
        /// <returns>True if the assembly is a system/platform assembly and should be skipped</returns>
        public static bool IsSystemAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            return name.StartsWith("System", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                   name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                   name.StartsWith("netstandard", StringComparison.Ordinal) ||
                   name.StartsWith("Unity.", StringComparison.Ordinal) ||
                   name.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   name.StartsWith("UnityEditor", StringComparison.Ordinal);
        }
    }
}
