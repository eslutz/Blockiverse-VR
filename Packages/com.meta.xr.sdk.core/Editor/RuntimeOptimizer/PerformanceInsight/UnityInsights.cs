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
using UnityEditor.Rendering;
using UnityEngine;

namespace Meta.XR.RuntimeOptimizer.Editor.PerformanceInsight
{
    /// <summary>Represents a performance insight with a category, description, and links to relevant documentation.</summary>
    [System.Serializable]
    public class UnityInsight
    {
        /// <summary>
        /// Gets the classification category of this performance insight.
        /// </summary>
        public string Category { get; }
        /// <summary>
        /// Gets the human-readable description of the performance issue.
        /// </summary>
        public string Description { get; }
        /// <summary>
        /// Gets the actionable recommendation text for addressing the performance issue.
        /// </summary>
        public string Insight { get; }
        /// <summary>
        /// Gets the URLs to relevant documentation for this insight.
        /// </summary>
        public string[] LinksToDoc { get; }

        public UnityInsight(string category, string description, string insight, string[] links)
        {
            Category = category;
            Description = description;
            Insight = insight;
            LinksToDoc = links;
        }
    }

    /// <summary>Stores runtime data for a texture including its format, dimensions, and memory usage.</summary>
    [System.Serializable]
    public class TextureRuntimeData
    {
        public string format;
        public string filterMode;
        public long runtimeSize;
        public int width;
        public int height;
        public int anisoLevel;
        public int depth;
        public int mipCount;
        public int usedByCount;
        public bool isReadable;
    }

    /// <summary>Stores runtime data for a mesh including its scene path and draw call count.</summary>
    [System.Serializable]
    public class MeshRuntimeData
    {
        public string shortPath = "";
        public int drawCount = 0;

        public int dynamicVertexCount = 0;
    }

    /// <summary>Stores shader analysis data including compiled stats, usage count, and associated textures.</summary>
    public class ShaderData
    {
        /// <summary>
        /// Gets or sets the list of compiled shader statistics from the Adreno offline compiler.
        /// </summary>
        //TODO: Refactor to get the data from PIL
        public List<AdrenoOfflineCompilerUtilityRO.ShaderStatInfo> shaderStats { get; set; }
        /// <summary>
        /// Gets or sets the number of materials in the scene that reference this shader.
        /// </summary>
        public int usedByCount { get; set; }
        /// <summary>
        /// Gets or sets the Unity Shader asset reference.
        /// </summary>
        public Shader shader { get; set; }
        /// <summary>
        /// Gets or sets the display name of the shader.
        /// </summary>
        public string shaderName { get; set; }
        /// <summary>
        /// Gets or sets the number of sub-shaders defined in this shader.
        /// </summary>
        public int subshaderCount { get; set; }
        /// <summary>
        /// Gets or sets the list of textures referenced by materials using this shader.
        /// </summary>
        public List<Texture> textureList { get; set; }
    }

    /// <summary>Represents a trigger condition for a performance insight based on min/max metric thresholds.</summary>
    [System.Serializable]
    public class InsightTrigger
    {
        /// <summary>
        /// Gets or sets the display title of this insight trigger rule.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Gets or sets the insight categories this trigger is associated with.
        /// </summary>
        public string[] Category { get; set; }
        /// <summary>
        /// Gets or sets the description text explaining what this trigger detects.
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// Gets or sets the minimum threshold value that activates this trigger.
        /// </summary>
        public float Min { get; set; }
        /// <summary>
        /// Gets or sets the maximum threshold value for this trigger.
        /// </summary>
        public float Max { get; set; }
    }

    /// <summary>Stores event data for insights including target devices and frozen state.</summary>
    [Serializable]
    public class InsightEventData
    {
        public string[] Devices;
        public bool IsForzen;
    }

    /// <summary>Wraps a string value as JSON-serializable insight event data.</summary>
    [Serializable]
    public class InsightEventDataStr
    {
        public string data;
        InsightEventDataStr(string src)
        {
            data = src;
        }

        /// <summary>
        /// Serializes a string value to JSON format wrapped in an InsightEventDataStr container.
        /// </summary>
        /// <param name="src">The string value to serialize.</param>
        /// <returns>A JSON string representation of the wrapped value.</returns>
        static public string ToJsonStr(string src)
        {
            InsightEventDataStr item = new InsightEventDataStr(src);
            return JsonUtility.ToJson(item);
        }
    }
}
