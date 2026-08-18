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
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// Provides a persistent session-scoped queue backed by Unity's SessionState for storing value-type items.
    /// </summary>
    public static class SessionQueue
    {
        /// <summary>
        /// Adds an item to the end of the session queue identified by the specified key.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        /// <param name="queueKey">The session state key identifying the queue.</param>
        /// <typeparam name="T">The value type of the items in the queue.</typeparam>
        public static void Enqueue<T>(T item, string queueKey) where T : struct
        {
            var queue = LoadQueue<T>(queueKey);
            queue.Enqueue(item);
            SaveQueue(queue, queueKey);
        }

        /// <summary>
        /// Removes and returns the item at the front of the session queue.
        /// </summary>
        /// <param name="queueKey">The session state key identifying the queue.</param>
        /// <typeparam name="T">The value type of the items in the queue.</typeparam>
        /// <returns>The dequeued item, or null if the queue is empty.</returns>
        public static T? Dequeue<T>(string queueKey) where T : struct
        {
            var queue = LoadQueue<T>(queueKey);
            if (queue.Count <= 0)
            {
                return null;
            }

            var item = queue.Dequeue();
            SaveQueue(queue, queueKey);
            return item;
        }

        /// <summary>
        /// Returns the number of items in the session queue.
        /// </summary>
        /// <param name="queueKey">The session state key identifying the queue.</param>
        /// <typeparam name="T">The value type of the items in the queue.</typeparam>
        /// <returns>The number of items in the queue.</returns>
        public static int Count<T>(string queueKey) where T : struct
        {
            var queue = LoadQueue<T>(queueKey);
            return queue.Count;
        }

        /// <summary>
        /// Clears all items from the session queue identified by the specified key.
        /// </summary>
        /// <param name="queueKey">The session state key identifying the queue to clear.</param>
        public static void Clear(string queueKey)
        {
            SessionState.EraseString(queueKey);
        }

        private static Queue<T> LoadQueue<T>(string key) where T : struct
        {
            var json = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new Queue<T>();
            }

            var helper = JsonUtility.FromJson<SerializationHelper<T>>(json);
            return new Queue<T>(helper.list);
        }

        private static void SaveQueue<T>(IEnumerable<T> queue, string key) where T : struct
        {
            var list = new List<T>(queue);
            var json = JsonUtility.ToJson(new SerializationHelper<T>(list));
            SessionState.SetString(key, json);
        }

        [Serializable]
        private class SerializationHelper<TU> where TU : struct
        {
            public List<TU> list;

            public SerializationHelper(List<TU> list)
            {
                this.list = list;
            }
        }
    }
}
