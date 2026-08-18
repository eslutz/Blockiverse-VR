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

using System.Collections;
using UnityEditor;

namespace Meta.XR.Editor.EditorCoroutine
{
    /// <summary>Provides a mechanism for running coroutines in the Unity Editor outside of play mode.</summary>
    public class EditorCoroutine
    {
        /// <summary>Starts a new editor coroutine from the given enumerator.</summary>
        /// <param name="routine">The enumerator to execute as a coroutine.</param>
        /// <returns>The started editor coroutine instance.</returns>
        public static EditorCoroutine Start(IEnumerator routine)
        {
            EditorCoroutine coroutine = new EditorCoroutine(routine);
            coroutine.Start();
            return coroutine;
        }

        readonly IEnumerator routine;
        bool completed;

        EditorCoroutine(IEnumerator _routine)
        {
            routine = _routine;
            completed = false;
        }

        void Start()
        {
            EditorApplication.update += Update;
        }

        /// <summary>Stops the coroutine and unregisters it from editor update callbacks.</summary>
        public void Stop()
        {
            EditorApplication.update -= Update;
            completed = true;
        }

        /// <summary>Gets whether the coroutine has completed execution.</summary>
        /// <returns>True if the coroutine has completed, false otherwise.</returns>
        public bool GetCompleted()
        {
            return completed;
        }

        void Update()
        {
            if (!routine.MoveNext())
            {
                Stop();
            }
        }
    }
}
