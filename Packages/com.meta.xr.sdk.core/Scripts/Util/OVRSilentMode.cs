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
using System.Threading;
using UnityEngine;

/// <summary>
/// Thread-safe flag for suppressing interactive UI dialogs
/// (e.g. EditorUtility.DisplayDialog) during automated/programmatic
/// operations. Automation frameworks set this before running operations
/// that may trigger editor dialogs; code that would otherwise show a
/// blocking dialog checks <see cref="IsEnabled"/> and auto-accepts instead.
/// </summary>
internal static class OVRSilentMode
{
    private static int _refCount;

    /// <summary>
    /// True when automated operations are in progress and interactive
    /// dialogs should be auto-accepted or skipped. Also true in batch mode.
    /// </summary>
    public static bool IsEnabled => Application.isBatchMode || _refCount > 0;

    /// <summary>
    /// Enter silent mode. Returns an IDisposable scope that exits
    /// silent mode when disposed. Use with a using statement.
    /// </summary>
    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _refCount);
        return new Scope();
    }

    private class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _refCount);
            }
        }
    }
}
