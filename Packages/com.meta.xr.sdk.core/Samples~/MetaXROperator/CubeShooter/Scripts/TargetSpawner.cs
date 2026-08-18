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
using System.Collections.Generic;

public class TargetSpawner : MonoBehaviour
{
    public int targetCount = 16;
    public float minDistBetweenTargets = 2.5f;
    public float minDistFromRig = 4f;
    public float maxDistFromRig = 18f;
    public Vector2 yRange = new Vector2(0.5f, 10f);

    private List<GameObject> activeTargets = new List<GameObject>();
    private Transform rigTransform;

    private Shader neonBorderShader;
    private Shader neonGridShader;
    private Shader jazzCupShader;
    private Shader vaporwaveGradientShader;

    private int targetCounter;

    private static readonly Color[] neonColors = {
        Color.cyan, Color.magenta, Color.green, Color.yellow,
        new Color(1f, 0.3f, 0f), new Color(0.5f, 0f, 1f),
        new Color(1f, 0f, 0.5f), new Color(0f, 1f, 0.5f)
    };

    private static readonly string[] neonColorNames = {
        "Cyan", "Magenta", "Green", "Yellow",
        "Orange", "Purple", "HotPink", "Mint"
    };

    private static readonly string[] styleNames = {
        "NeonBorder", "NeonGrid", "JazzCup", "Vaporwave"
    };

    void Start()
    {
        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig != null)
            rigTransform = rig.transform;

        neonBorderShader = Shader.Find("Custom/NeonBorder");
        neonGridShader = Shader.Find("Custom/NeonGrid");
        jazzCupShader = Shader.Find("Custom/JazzCup");
        vaporwaveGradientShader = Shader.Find("Custom/VaporwaveGradient");

        for (int i = 0; i < targetCount; i++)
            SpawnTarget();
    }

    public void OnTargetDestroyed(GameObject target)
    {
        activeTargets.Remove(target);
        SpawnTarget();
    }

    void SpawnTarget()
    {
        Vector3 pos;
        int attempts = 0;
        Vector3 rigPos = rigTransform != null ? rigTransform.position : Vector3.zero;

        do
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(minDistFromRig, maxDistFromRig);
            pos = new Vector3(
                rigPos.x + Mathf.Cos(angle) * dist,
                Random.Range(yRange.x, yRange.y),
                rigPos.z + Mathf.Sin(angle) * dist
            );
            attempts++;
        } while (!IsValidPosition(pos) && attempts < 50);

        int style = Random.Range(0, 4);
        int colorIndex = Random.Range(0, neonColors.Length);

        targetCounter++;
        string colorSuffix = style <= 1 ? "_" + neonColorNames[colorIndex] : "";
        string name = $"Target_{styleNames[style]}{colorSuffix}_{targetCounter}";

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = pos;

        Renderer renderer = cube.GetComponent<Renderer>();
        renderer.material = CreateRandomMaterial(style, colorIndex);

        var target = cube.AddComponent<ShootableTarget>();
        target.pointValue = style switch { 2 => 3, 3 => 2, _ => 1 };
        activeTargets.Add(cube);
    }

    Material CreateRandomMaterial(int style, int colorIndex)
    {
        Color neon = neonColors[colorIndex];

        switch (style)
        {
            case 0: // Neon border
            {
                var mat = new Material(neonBorderShader != null ? neonBorderShader : Shader.Find("Universal Render Pipeline/Lit"));
                if (neonBorderShader != null)
                {
                    mat.SetColor("_BorderColor", neon);
                    mat.SetFloat("_BorderWidth", Random.Range(0.04f, 0.1f));
                    mat.SetFloat("_EmissionIntensity", Random.Range(3f, 6f));
                }
                return mat;
            }
            case 1: // Neon grid
            {
                var mat = new Material(neonGridShader != null ? neonGridShader : Shader.Find("Universal Render Pipeline/Lit"));
                if (neonGridShader != null)
                {
                    mat.SetColor("_GridColor", neon);
                    mat.SetFloat("_GridSize", (float)Random.Range(3, 8));
                    mat.SetFloat("_LineWidth", Random.Range(0.03f, 0.08f));
                    mat.SetFloat("_EmissionIntensity", Random.Range(2f, 5f));
                }
                return mat;
            }
            case 2: // Jazz cup
            {
                var mat = new Material(jazzCupShader != null ? jazzCupShader : Shader.Find("Universal Render Pipeline/Lit"));
                if (jazzCupShader != null)
                {
                    mat.SetFloat("_WaveScale", Random.Range(1f, 2.5f));
                    mat.SetFloat("_WaveOffset", Random.Range(0f, 1f));
                }
                return mat;
            }
            case 3: // Vaporwave gradient
            {
                var mat = new Material(vaporwaveGradientShader != null ? vaporwaveGradientShader : Shader.Find("Universal Render Pipeline/Lit"));
                if (vaporwaveGradientShader != null)
                {
                    // Pick two contrasting neon colors
                    Color top = neonColors[Random.Range(0, neonColors.Length)];
                    Color bottom = neonColors[Random.Range(0, neonColors.Length)];
                    mat.SetColor("_TopColor", top);
                    mat.SetColor("_BottomColor", bottom);
                    mat.SetFloat("_EmissionIntensity", Random.Range(1f, 2.5f));
                }
                return mat;
            }
        }

        // Fallback
        var fallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        fallback.color = neon;
        return fallback;
    }

    bool IsValidPosition(Vector3 pos)
    {
        if (rigTransform != null)
        {
            float distToRig = Vector3.Distance(pos, rigTransform.position);
            if (distToRig < minDistFromRig)
                return false;
        }

        foreach (var target in activeTargets)
        {
            if (target == null) continue;
            if (Vector3.Distance(pos, target.transform.position) < minDistBetweenTargets)
                return false;
        }

        return true;
    }
}
