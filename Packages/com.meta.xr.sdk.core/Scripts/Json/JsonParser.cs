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
using System.Globalization;
using System.Text;

namespace Meta.XR.Json
{
    /// <summary>
    /// Recursive descent JSON parser. Converts JSON strings to JsonNode trees.
    /// </summary>
    public static class JsonParser
    {
        public static JsonNode Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new McpJsonException("Cannot parse null or empty JSON string");

            int index = 0;
            var result = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index < json.Length)
                throw new McpJsonException($"Unexpected content after JSON value at position {index}");
            return result;
        }

        private static JsonNode ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
                throw new McpJsonException("Unexpected end of JSON");

            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return ParseString(json, ref index);
                case 't':
                case 'f': return ParseBoolean(json, ref index);
                case 'n': return ParseNull(json, ref index);
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber(json, ref index);
                    throw new McpJsonException($"Unexpected character '{c}' at position {index}");
            }
        }

        private static JsonObject ParseObject(string json, ref int index)
        {
            var obj = new JsonObject();
            index++; // skip '{'
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                    throw new McpJsonException("Unterminated JSON object");

                var key = ParseStringValue(json, ref index);
                SkipWhitespace(json, ref index);

                if (index >= json.Length || json[index] != ':')
                    throw new McpJsonException($"Expected ':' at position {index}");
                index++; // skip ':'

                var value = ParseValue(json, ref index);
                obj[key] = value;

                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                    throw new McpJsonException("Unterminated JSON object");

                if (json[index] == '}')
                {
                    index++;
                    return obj;
                }

                if (json[index] != ',')
                    throw new McpJsonException($"Expected ',' or '}}' at position {index}");
                index++; // skip ','
            }
        }

        private static JsonArray ParseArray(string json, ref int index)
        {
            var arr = new JsonArray();
            index++; // skip '['
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return arr;
            }

            while (true)
            {
                var value = ParseValue(json, ref index);
                arr.Add(value);

                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                    throw new McpJsonException("Unterminated JSON array");

                if (json[index] == ']')
                {
                    index++;
                    return arr;
                }

                if (json[index] != ',')
                    throw new McpJsonException($"Expected ',' or ']' at position {index}");
                index++; // skip ','
            }
        }

        private static JsonValue ParseString(string json, ref int index)
        {
            return new JsonValue(ParseStringValue(json, ref index));
        }

        private static string ParseStringValue(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"')
                throw new McpJsonException($"Expected '\"' at position {index}");

            index++; // skip opening quote
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '"')
                {
                    index++; // skip closing quote
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    index++;
                    if (index >= json.Length)
                        throw new McpJsonException("Unterminated string escape");

                    char escaped = json[index];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 >= json.Length)
                                throw new McpJsonException("Unterminated unicode escape");
                            var hex = json.Substring(index + 1, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                            index += 4;
                            break;
                        default:
                            throw new McpJsonException($"Invalid escape character '\\{escaped}'");
                    }
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }

            throw new McpJsonException("Unterminated string");
        }

        private static JsonValue ParseNumber(string json, ref int index)
        {
            int start = index;
            bool isFloat = false;

            if (json[index] == '-') index++;

            while (index < json.Length && json[index] >= '0' && json[index] <= '9')
                index++;

            if (index < json.Length && json[index] == '.')
            {
                isFloat = true;
                index++;
                while (index < json.Length && json[index] >= '0' && json[index] <= '9')
                    index++;
            }

            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    index++;
                while (index < json.Length && json[index] >= '0' && json[index] <= '9')
                    index++;
            }

            var numStr = json.Substring(start, index - start);

            if (isFloat)
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return new JsonValue(d);
                throw new McpJsonException($"Invalid number: {numStr}");
            }

            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                if (l >= int.MinValue && l <= int.MaxValue)
                    return new JsonValue((int)l);
                return new JsonValue(l);
            }

            // Fall back to double for very large integers
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2))
                return new JsonValue(d2);

            throw new McpJsonException($"Invalid number: {numStr}");
        }

        private static JsonValue ParseBoolean(string json, ref int index)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "true")
            {
                index += 4;
                return new JsonValue(true);
            }
            if (json.Length - index >= 5 && json.Substring(index, 5) == "false")
            {
                index += 5;
                return new JsonValue(false);
            }
            throw new McpJsonException($"Invalid boolean at position {index}");
        }

        private static JsonValue ParseNull(string json, ref int index)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "null")
            {
                index += 4;
                return JsonValue.Null;
            }
            throw new McpJsonException($"Expected 'null' at position {index}");
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }

    /// <summary>
    /// JSON string writing utilities.
    /// </summary>
    public static class JsonWriter
    {
        public static string EscapeString(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
