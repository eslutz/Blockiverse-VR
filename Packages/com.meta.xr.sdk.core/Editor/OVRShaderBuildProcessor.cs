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

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shader build preprocessor that strips unnecessary shader graphics tiers (Tier1 and Tier3) for Android Quest builds
/// when BiRP is used and skipUnneededShaders is enabled in OVRProjectConfig. Quest only uses Tier2 shaders.
/// </summary>
public class OVRShaderBuildProcessor : IPreprocessShaders
{
    public int callbackOrder
    {
        get { return 0; }
    }

    /// <summary>
    /// Strips shader variants targeting unused graphics tiers (Tier1, Tier3) from Android Quest builds to reduce build size and compile time.
    /// </summary>
    /// <param name="shader">The shader being processed.</param>
    /// <param name="snippet">Metadata about the shader snippet (pass type, stage).</param>
    /// <param name="shaderCompilerData">The list of shader variants to compile. Variants targeting stripped tiers are removed from this list.</param>
    public void OnProcessShader(
        Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> shaderCompilerData)
    {
        var projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
        {
            return;
        }

        if (!projectConfig.skipUnneededShaders)
        {
            return;
        }

        // Don't strip if we are using SRP, only BiRP
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            return;
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            return;
        }

        var strippedGraphicsTiers = new HashSet<GraphicsTier>();

        // Unity only uses shader Tier2 on Quest and Go (regardless of graphics API)
        if (projectConfig.targetDeviceTypes.Count != 0)
        {
            strippedGraphicsTiers.Add(GraphicsTier.Tier1);
            strippedGraphicsTiers.Add(GraphicsTier.Tier3);
        }

        if (strippedGraphicsTiers.Count == 0)
        {
            return;
        }

        for (int i = shaderCompilerData.Count - 1; i >= 0; --i)
        {
            if (strippedGraphicsTiers.Contains(shaderCompilerData[i].graphicsTier))
            {
                shaderCompilerData.RemoveAt(i);
            }
        }
    }
}
