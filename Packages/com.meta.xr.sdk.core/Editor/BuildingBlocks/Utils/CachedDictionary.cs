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
using Meta.XR.Editor.Id;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// Provides a lazily-refreshed dictionary that caches ScriptableObject assets keyed by their unique identifier.
    /// </summary>
    /// <typeparam name="T">The ScriptableObject type that implements IIdentified.</typeparam>
    public class CachedIdDictionary<T>
        where T : ScriptableObject, IIdentified
    {
        public event Action<CachedIdDictionary<T>> OnRefresh;

        private readonly Dictionary<string, T> _dictionary = new();
        private readonly List<T> _list = new();
        private bool _dirty = true;


        public CachedIdDictionary()
        {
            EditorApplication.projectChanged -= MarkAsDirty;
            EditorApplication.projectChanged += MarkAsDirty;
        }

        private static IEnumerable<T> FindAssets() =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(id =>
                    AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(id))
                ).Where(value => value != null);

        private void Refresh()
        {
            if (!_dirty)
            {
                return;
            }

            var assets = FindAssets();
            _list.Clear();
            _list.AddRange(assets);


            _dictionary.Clear();
            foreach (var value in _list)
            {
                _dictionary[value.Id] = value;
            }

            _dirty = !_list.Any();

            OnRefresh?.Invoke(this);
        }

        public T this[string key]
        {
            get
            {
                if (key == null)
                {
                    return null;
                }

                Refresh();
                _dictionary.TryGetValue(key, out var value);
                return value;
            }
        }

        /// <summary>
        /// Attempts to retrieve the value associated with the specified key.
        /// </summary>
        /// <param name="key">The unique identifier to look up.</param>
        /// <param name="value">When this method returns, contains the value if the key was found; otherwise, null.</param>
        /// <returns>True if the key was found; otherwise, false.</returns>
        public bool TryGetValue(string key, out T value)
        {
            if (key == null)
            {
                value = null;
                return false;
            }

            Refresh();
            return _dictionary.TryGetValue(key, out value);
        }

        public IReadOnlyList<T> Values
        {
            get
            {
                Refresh();
                return _list;
            }
        }

        /// <summary>
        /// Marks the cache as dirty so it will be refreshed on the next access.
        /// </summary>
        public void MarkAsDirty() => _dirty = true;
    }
}
