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

using UnityEngine;

[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class BloomEffect : MonoBehaviour
{
    [Range(0f, 2f)] public float threshold = 0.8f;
    [Range(0f, 1f)] public float softThreshold = 0.5f;
    [Range(0f, 10f)] public float intensity = 1.5f;
    [Range(1, 8)] public int iterations = 4;

    Material _material;

    void OnEnable()
    {
        var shader = Shader.Find("Hidden/Bloom");
        if (shader == null || !shader.isSupported)
        {
            enabled = false;
            return;
        }
        _material = new Material(shader);
        _material.hideFlags = HideFlags.HideAndDontSave;
        GetComponent<Camera>().forceIntoRenderTexture = true;
    }

    void OnDisable()
    {
        if (_material != null)
            DestroyImmediate(_material);
        var cam = GetComponent<Camera>();
        if (cam != null)
            cam.forceIntoRenderTexture = false;
    }

    RenderTexture GetRT(RenderTexture source, int width, int height)
    {
        var desc = source.descriptor;
        desc.width = width;
        desc.height = height;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        return RenderTexture.GetTemporary(desc);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        float knee = threshold * softThreshold;
        _material.SetVector("_Filter", new Vector4(
            threshold, threshold - knee, 2f * knee, 0.25f / (knee + 0.00001f)
        ));
        _material.SetFloat("_Intensity", intensity);

        int w = source.width / 2;
        int h = source.height / 2;

        // Prefilter
        var prefiltered = GetRT(source, w, h);
        Graphics.Blit(source, prefiltered, _material, 0);

        // Downsample chain
        var mips = new RenderTexture[iterations];
        var prev = prefiltered;
        for (int i = 0; i < iterations; i++)
        {
            w = Mathf.Max(1, w / 2);
            h = Mathf.Max(1, h / 2);
            mips[i] = GetRT(source, w, h);
            Graphics.Blit(prev, mips[i], _material, 1);
            prev = mips[i];
        }

        // Upsample chain
        var current = mips[iterations - 1];
        for (int i = iterations - 2; i >= 0; i--)
        {
            _material.SetTexture("_BloomTex", current);
            var temp = GetRT(source, mips[i].width, mips[i].height);
            Graphics.Blit(mips[i], temp, _material, 2);
            if (current != mips[iterations - 1])
                RenderTexture.ReleaseTemporary(current);
            current = temp;
        }

        // Final upsample to prefilter size
        _material.SetTexture("_BloomTex", current);
        var finalBloom = GetRT(source, prefiltered.width, prefiltered.height);
        Graphics.Blit(prefiltered, finalBloom, _material, 2);
        if (current != mips[iterations - 1])
            RenderTexture.ReleaseTemporary(current);

        // Composite
        _material.SetTexture("_BloomTex", finalBloom);
        Graphics.Blit(source, destination, _material, 3);

        // Cleanup
        RenderTexture.ReleaseTemporary(finalBloom);
        RenderTexture.ReleaseTemporary(prefiltered);
        for (int i = 0; i < iterations; i++)
            RenderTexture.ReleaseTemporary(mips[i]);
    }
}
