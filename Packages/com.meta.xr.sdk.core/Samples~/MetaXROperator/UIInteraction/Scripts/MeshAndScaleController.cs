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
using TMPro;
using UnityEngine.UI;

public class MeshAndScaleController : MonoBehaviour
{
    public GameObject targetObject;
    public TMP_Dropdown meshDropdown;
    public Slider scaleSlider;
    public Toggle textureToggle;
    public Material targetMaterial;
    public float minScale = 0.1f;
    public float maxScale = 1.0f;

    public Mesh sphereMesh;
    public Mesh cubeMesh;
    public Mesh cylinderMesh;
    public Texture meshTexture;

    private void Start()
    {
        if (meshDropdown != null)
        {
            meshDropdown.onValueChanged.AddListener(OnMeshChanged);
        }

        if (scaleSlider != null)
        {
            scaleSlider.minValue = minScale;
            scaleSlider.maxValue = maxScale;
            scaleSlider.value = targetObject != null ? targetObject.transform.localScale.x : 1f;
            scaleSlider.onValueChanged.AddListener(OnScaleChanged);
        }

        if (textureToggle != null)
        {
            textureToggle.isOn = meshTexture != null;
            textureToggle.onValueChanged.AddListener(OnTextureToggled);
        }
    }

    private void OnMeshChanged(int index)
    {
        if (targetObject == null) return;

        var meshFilter = targetObject.GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        switch (index)
        {
            case 0: meshFilter.mesh = sphereMesh; break;
            case 1: meshFilter.mesh = cubeMesh; break;
            case 2: meshFilter.mesh = cylinderMesh; break;
        }
    }

    private void OnTextureToggled(bool isOn)
    {
        if (targetMaterial == null) return;
        targetMaterial.SetTexture("_BaseMap", isOn ? meshTexture : null);
    }

    private void OnScaleChanged(float value)
    {
        if (targetObject == null) return;
        targetObject.transform.localScale = Vector3.one * value;
    }
}
