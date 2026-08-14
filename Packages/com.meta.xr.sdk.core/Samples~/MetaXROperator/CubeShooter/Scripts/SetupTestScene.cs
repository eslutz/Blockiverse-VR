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

public class SetupTestScene : MonoBehaviour
{
    void Start()
    {
        // Create floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0, 0, 0);
        floor.transform.localScale = new Vector3(5, 1, 5); // 50x50 units

        // Create reference cubes at different positions
        CreateCube("RefCube_Red", new Vector3(-3, 1, 5), Color.red);
        CreateCube("RefCube_Blue", new Vector3(3, 1, 5), Color.blue);
        CreateCube("RefCube_Green", new Vector3(0, 1, 10), Color.green);
        CreateCube("RefCube_Yellow", new Vector3(0, 1, 2), Color.yellow);

    }

    void CreateCube(string name, Vector3 position, Color color)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;

        // Set color
        Renderer renderer = cube.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        renderer.material = mat;

        // Make it a shootable target
        cube.AddComponent<ShootableTarget>();
    }
}
