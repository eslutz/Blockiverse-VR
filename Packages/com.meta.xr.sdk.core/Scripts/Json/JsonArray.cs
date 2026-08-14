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
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meta.XR.Json
{
    /// <summary>
    /// Mutable JSON array backed by a list.
    /// Replaces Newtonsoft.Json.Linq.JArray.
    /// </summary>
    public class JsonArray : JsonNode, IEnumerable<JsonNode>
    {
        private readonly List<JsonNode> _items = new();

        public JsonArray() { }

        public JsonArray(IEnumerable<string> items)
        {
            foreach (var item in items)
                _items.Add(item == null ? JsonValue.Null : new JsonValue(item));
        }

        public JsonArray(IEnumerable<JsonNode> items)
        {
            _items.AddRange(items);
        }

        public override JsonNodeType Type => JsonNodeType.Array;

        public override JsonNode this[int index]
        {
            get => _items[index];
        }

        public int Count => _items.Count;

        public void Add(JsonNode item)
        {
            _items.Add(item ?? JsonValue.Null);
        }

        public void Add(string item)
        {
            _items.Add(item == null ? JsonValue.Null : new JsonValue(item));
        }

        public IEnumerable<T> Select<T>(System.Func<JsonNode, T> selector)
        {
            return _items.Select(selector);
        }

        public IEnumerator<JsonNode> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToJsonString(bool indented = false, int depth = 0)
        {
            var sb = new StringBuilder();
            sb.Append('[');

            bool first = true;
            foreach (var item in _items)
            {
                if (!first) sb.Append(',');
                first = false;

                if (indented)
                {
                    sb.AppendLine();
                    sb.Append(new string(' ', (depth + 1) * 2));
                }

                sb.Append(item?.ToJsonString(indented, depth + 1) ?? "null");
            }

            if (indented && _items.Count > 0)
            {
                sb.AppendLine();
                sb.Append(new string(' ', depth * 2));
            }

            sb.Append(']');
            return sb.ToString();
        }
    }
}
