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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meta.XR.Json
{
    /// <summary>
    /// Mutable JSON object backed by an ordered dictionary.
    /// Replaces Newtonsoft.Json.Linq.JObject.
    /// </summary>
    public class JsonObject : JsonNode, IEnumerable<KeyValuePair<string, JsonNode>>
    {
        private readonly List<KeyValuePair<string, JsonNode>> _properties = new();
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public JsonObject() { }

        public override JsonNodeType Type => JsonNodeType.Object;

        public override JsonNode this[string key]
        {
            get
            {
                if (key != null && _index.TryGetValue(key, out var idx))
                    return _properties[idx].Value;
                return null;
            }
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                var node = value ?? JsonValue.Null;
                if (_index.TryGetValue(key, out var idx))
                {
                    _properties[idx] = new KeyValuePair<string, JsonNode>(key, node);
                }
                else
                {
                    _index[key] = _properties.Count;
                    _properties.Add(new KeyValuePair<string, JsonNode>(key, node));
                }
            }
        }

        public bool TryGetValue(string key, out JsonNode value)
        {
            if (key != null && _index.TryGetValue(key, out var idx))
            {
                value = _properties[idx].Value;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetValue(string key, StringComparison comparison, out JsonNode value)
        {
            foreach (var kvp in _properties)
            {
                if (string.Equals(kvp.Key, key, comparison))
                {
                    value = kvp.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public IEnumerable<JsonObjectProperty> Properties()
        {
            return _properties.Select(kvp => new JsonObjectProperty(kvp.Key, kvp.Value));
        }

        public bool HasValues => _properties.Count > 0;

        public int Count => _properties.Count;

        public bool ContainsKey(string key) => key != null && _index.ContainsKey(key);

        public void Remove(string key)
        {
            if (key == null || !_index.TryGetValue(key, out var idx)) return;
            _properties.RemoveAt(idx);
            RebuildIndex();
        }

        public IEnumerable<T> Values<T>()
        {
            foreach (var kvp in _properties)
            {
                if (typeof(T) == typeof(object))
                {
                    if (kvp.Value is JsonValue jv)
                        yield return (T)(jv.RawValue ?? (object)null);
                    else
                        yield return (T)(object)kvp.Value;
                }
                else
                {
                    yield return kvp.Value.Value<T>();
                }
            }
        }

        private void RebuildIndex()
        {
            _index.Clear();
            for (int i = 0; i < _properties.Count; i++)
                _index[_properties[i].Key] = i;
        }

        public static new JsonObject Parse(string json)
        {
            var node = JsonParser.Parse(json);
            if (node is JsonObject obj) return obj;
            throw new McpJsonException("JSON string does not represent an object");
        }

        public static JsonObject FromObject(object obj, McpJsonSettings settings = null)
        {
            var node = McpJsonConvert.SerializeToNode(obj, settings);
            if (node is JsonObject jsonObj) return jsonObj;
            throw new InvalidOperationException("Object did not serialize to a JSON object");
        }

        public IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator() => _properties.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToJsonString(bool indented = false, int depth = 0)
        {
            var sb = new StringBuilder();
            sb.Append('{');

            bool first = true;
            foreach (var kvp in _properties)
            {
                if (!first) sb.Append(',');
                first = false;

                if (indented)
                {
                    sb.AppendLine();
                    sb.Append(new string(' ', (depth + 1) * 2));
                }

                sb.Append(JsonWriter.EscapeString(kvp.Key));
                sb.Append(':');
                if (indented) sb.Append(' ');
                sb.Append(kvp.Value?.ToJsonString(indented, depth + 1) ?? "null");
            }

            if (indented && _properties.Count > 0)
            {
                sb.AppendLine();
                sb.Append(new string(' ', depth * 2));
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
