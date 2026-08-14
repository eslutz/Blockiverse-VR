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

#if USING_XR_SDK_OPENXR

using System.Text;
using OVRSimpleJSON;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meta.XR
{
    /// <summary>
    /// Provides a set of default agentic tools that can be registered with Meta XR Operator.
    /// </summary>
    public static class MetaXROperatorDefaultTools
    {
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered)
                return;

            RegisterGetSceneRootObjects();
            RegisterGetChildren();
            RegisterFindCanvases();
            RegisterFindInteractables();
            RegisterGetWorldPose();
            RegisterConvertUnityPositionToOpenXR();
            RegisterConvertUnityRotationToOpenXR();
            RegisterGetToggleState();
            RegisterGetSliderInfo();

            _registered = true;
        }

        private static void RegisterGetSceneRootObjects()
        {
            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_get_scene_root_objects",
                "Returns the names and hierarchy paths of all root-level GameObjects in the active Unity scene.",
                null,
                _ =>
                {
                    var scene = SceneManager.GetActiveScene();
                    var rootObjects = scene.GetRootGameObjects();
                    var result = new JSONObject();
                    var arr = new JSONArray();
                    for (int i = 0; i < rootObjects.Length; i++)
                    {
                        var go = rootObjects[i];
                        var obj = new JSONObject();
                        obj["name"] = go.name;
                        obj["active"] = go.activeSelf;
                        obj["childCount"] = go.transform.childCount;
                        arr.Add(obj);
                    }
                    result["rootObjects"] = arr;
                    return result.ToString();
                });
        }
        private static void RegisterGetChildren()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "path",
                    Description = "The hierarchy path of the GameObject (e.g. \"PlayerController/MainCamera\").",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.String,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_get_children",
                "Returns the paths of all direct children of the GameObject at the given hierarchy path.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<PathInput>(paramsJson);
                    if (string.IsNullOrEmpty(input.path))
                        return "Error: 'path' parameter is required.\n" +
                               "Tip: Provide the hierarchy path of the GameObject.";

                    var go = GameObject.Find(input.path);
                    if (go == null)
                        return $"Error: GameObject not found at path '{input.path}'.\n" +
                               "Tip: Use get_scene_root_objects to find valid root paths, then navigate with get_children.";

                    var transform = go.transform;
                    var result = new JSONObject();
                    var arr = new JSONArray();
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        var child = transform.GetChild(i);
                        var obj = new JSONObject();
                        obj["name"] = child.name;
                        obj["path"] = GetHierarchyPath(child);
                        obj["active"] = child.gameObject.activeSelf;
                        obj["childCount"] = child.childCount;
                        arr.Add(obj);
                    }
                    result["children"] = arr;
                    return result.ToString();
                });
        }

        private static void RegisterFindCanvases()
        {
            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_find_canvases",
                "Returns all Canvas components in the scene with their hierarchy paths, render modes, and forward-facing direction vectors. " +
                "The 'forward' vector indicates which direction the front of the UI is facing in world space.",
                null,
                _ =>
                {
                    var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    var result = new JSONObject();
                    var arr = new JSONArray();
                    for (int i = 0; i < canvases.Length; i++)
                    {
                        var canvas = canvases[i];
                        var obj = new JSONObject();
                        obj["path"] = GetHierarchyPath(canvas.transform);
                        obj["active"] = canvas.gameObject.activeInHierarchy;
                        obj["renderMode"] = canvas.renderMode.ToString();

                        // Include forward vector - the direction the front of the canvas faces
                        // Unity canvases face -Z locally, so we negate transform.forward
                        var forward = -canvas.transform.forward;
                        var forwardObj = new JSONObject();
                        forwardObj["x"] = forward.x;
                        forwardObj["y"] = forward.y;
                        forwardObj["z"] = forward.z;
                        obj["forward"] = forwardObj;

                        arr.Add(obj);
                    }
                    result["canvases"] = arr;
                    return result.ToString();
                });
        }

        private static void RegisterFindInteractables()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "path",
                    Description = "The hierarchy path of the Canvas GameObject to search within.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.String,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_find_interactables",
                "Searches all children of the Canvas at the given path and returns the paths of all GameObjects that contain Button, Toggle, Slider, Dropdown, InputField, or Selectable components.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<PathInput>(paramsJson);
                    if (string.IsNullOrEmpty(input.path))
                        return "Error: 'path' parameter is required.\n" +
                               "Tip: Use find_canvases to discover Canvas paths, then pass one here.";

                    var go = GameObject.Find(input.path);
                    if (go == null)
                        return $"Error: GameObject not found at path '{input.path}'.\n" +
                               "Tip: Use find_canvases to discover valid Canvas paths.";

                    var selectables = go.GetComponentsInChildren<Selectable>(true);
                    var result = new JSONObject();
                    var arr = new JSONArray();
                    foreach (var selectable in selectables)
                    {
                        var obj = new JSONObject();
                        obj["path"] = GetHierarchyPath(selectable.transform);
                        obj["type"] = selectable.GetType().Name;
                        obj["interactable"] = selectable.interactable;
                        obj["active"] = selectable.gameObject.activeInHierarchy;
                        arr.Add(obj);
                    }
                    result["interactables"] = arr;
                    return result.ToString();
                });
        }

        private static void RegisterGetWorldPose()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "path",
                    Description = "The hierarchy path of the GameObject to get the world pose of.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.String,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_get_world_pose",
                "Returns the world space position, rotation, and scale for every GameObject or UI element matching the given hierarchy path. " +
                "Multiple GameObjects can share the same hierarchy path; the response contains a 'matches' array with one entry per match, " +
                "each including 'path', 'active', 'position', 'rotation', and 'scale'. " +
                "Also returns the current tracking_origin, which indicates the reference frame used by the XR system to interpret poses. " +
                "The tracking origin determines which OpenXR reference space Unity uses (e.g., EyeLevel → local, FloorLevel → local_floor, " +
                "Stage → stage). When converting between OpenXR and Unity coordinates, you MUST query OpenXR poses using the reference " +
                "space that matches the tracking origin. Using a mismatched reference space will produce incorrect Y-axis offsets or positional errors.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<PathInput>(paramsJson);
                    if (string.IsNullOrEmpty(input.path))
                        return "Error: 'path' parameter is required.\n" +
                               "Tip: Provide the hierarchy path of the GameObject.";

                    var matches = FindAllByHierarchyPath(input.path);
                    if (matches.Count == 0)
                        return $"Error: GameObject not found at path '{input.path}'.\n" +
                               "Tip: Use get_scene_root_objects to find valid root paths, then navigate with get_children.";

                    var result = new JSONObject();
                    var matchesArr = new JSONArray();
                    foreach (var go in matches)
                    {
                        var t = go.transform;
                        var pos = t.position;
                        var rot = t.rotation.eulerAngles;
                        var scale = t.lossyScale;

                        var matchObj = new JSONObject();
                        matchObj["path"] = GetHierarchyPath(t);
                        matchObj["active"] = go.activeInHierarchy;

                        var posObj = new JSONObject();
                        posObj["x"] = pos.x;
                        posObj["y"] = pos.y;
                        posObj["z"] = pos.z;
                        matchObj["position"] = posObj;

                        var rotObj = new JSONObject();
                        rotObj["x"] = rot.x;
                        rotObj["y"] = rot.y;
                        rotObj["z"] = rot.z;
                        matchObj["rotation"] = rotObj;

                        var scaleObj = new JSONObject();
                        scaleObj["x"] = scale.x;
                        scaleObj["y"] = scale.y;
                        scaleObj["z"] = scale.z;
                        matchObj["scale"] = scaleObj;

                        matchesArr.Add(matchObj);
                    }
                    result["matchCount"] = matches.Count;
                    result["matches"] = matchesArr;

                    var trackingOriginObj = new JSONObject();
                    var instance = OVRManager.instance;
                    if (instance != null)
                    {
                        var trackingOrigin = instance.trackingOriginType;
                        trackingOriginObj["OVR"] = trackingOrigin.ToString();

                        string openXrSpace;
                        switch (trackingOrigin)
                        {
                            case OVRManager.TrackingOrigin.EyeLevel:
                                openXrSpace = "local";
                                break;
                            case OVRManager.TrackingOrigin.FloorLevel:
                                openXrSpace = "local_floor";
                                break;
                            case OVRManager.TrackingOrigin.Stage:
                                openXrSpace = "stage";
                                break;
                            case OVRManager.TrackingOrigin.Stationary:
                                openXrSpace = "stationary";
                                break;
                            default:
                                openXrSpace = "unknown";
                                break;
                        }
                        trackingOriginObj["OpenXR"] = openXrSpace;
                    }
                    else
                    {
                        trackingOriginObj["error"] = "OVRManager instance not found in the scene.";
                    }
                    result["tracking_origin"] = trackingOriginObj;

                    return result.ToString();
                });
        }

        private static void RegisterConvertUnityPositionToOpenXR()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "x",
                    Description = "The X component of the Unity world-space position.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                },
                new AgenticToolParameter
                {
                    Name = "y",
                    Description = "The Y component of the Unity world-space position.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                },
                new AgenticToolParameter
                {
                    Name = "z",
                    Description = "The Z component of the Unity world-space position.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "convert_unity_position_to_openxr",
                "Converts a Unity world-space position to OpenXR space by negating the Z axis.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<Vector3Input>(paramsJson);
                    var openXrPos = UnityPositionToOpenXR(new Vector3(input.x, input.y, input.z));
                    var result = new JSONObject();
                    result["x"] = openXrPos.x;
                    result["y"] = openXrPos.y;
                    result["z"] = openXrPos.z;
                    return result.ToString();
                });
        }

        private static void RegisterConvertUnityRotationToOpenXR()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "x",
                    Description = "The X component of the Unity quaternion.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                },
                new AgenticToolParameter
                {
                    Name = "y",
                    Description = "The Y component of the Unity quaternion.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                },
                new AgenticToolParameter
                {
                    Name = "z",
                    Description = "The Z component of the Unity quaternion.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                },
                new AgenticToolParameter
                {
                    Name = "w",
                    Description = "The W component of the Unity quaternion.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.Number,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "convert_unity_rotation_to_openxr",
                "Converts a Unity quaternion to OpenXR space by negating X and Y components.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<QuaternionInput>(paramsJson);
                    var openXrRot = UnityRotationToOpenXR(new Quaternion(input.x, input.y, input.z, input.w));
                    var result = new JSONObject();
                    result["x"] = openXrRot.x;
                    result["y"] = openXrRot.y;
                    result["z"] = openXrRot.z;
                    result["w"] = openXrRot.w;
                    return result.ToString();
                });
        }

        private static void RegisterGetToggleState()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "path",
                    Description = "The hierarchy path of the GameObject that has a Toggle component.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.String,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_get_toggle_state",
                "Returns the isOn state of the Toggle component on the GameObject at the given hierarchy path.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<PathInput>(paramsJson);
                    if (string.IsNullOrEmpty(input.path))
                        return "Error: 'path' parameter is required.\n" +
                               "Tip: Provide the hierarchy path of the GameObject with a Toggle component.";

                    var go = GameObject.Find(input.path);
                    if (go == null)
                        return $"Error: GameObject not found at path '{input.path}'.\n" +
                               "Tip: Use get_scene_root_objects to find valid root paths, then navigate with get_children.";

                    var toggle = go.GetComponent<Toggle>();
                    if (toggle == null)
                        return $"Error: No Toggle component found on GameObject at path '{input.path}'.\n" +
                               "Tip: Use find_interactables to discover GameObjects with Toggle components.";

                    var result = new JSONObject();
                    result["isOn"] = toggle.isOn;
                    result["interactable"] = toggle.interactable;
                    result["active"] = go.activeInHierarchy;
                    return result.ToString();
                });
        }

        private static void RegisterGetSliderInfo()
        {
            var parameters = new[]
            {
                new AgenticToolParameter
                {
                    Name = "path",
                    Description = "The hierarchy path of the GameObject that has a Slider component.",
                    ParamType = XrAgenticExternalToolParameterTypeMETAX1.String,
                    IsRequired = true
                }
            };

            MetaXROperatorExternalTool.RegisterAgenticTool(
                "unity_get_slider_info",
                "Returns information about the Slider component on the GameObject at the given hierarchy path, including direction, min value, max value, and current value.",
                parameters,
                paramsJson =>
                {
                    var input = JsonUtility.FromJson<PathInput>(paramsJson);
                    if (string.IsNullOrEmpty(input.path))
                        return "Error: 'path' parameter is required.\n" +
                               "Tip: Provide the hierarchy path of the GameObject with a Slider component.";

                    var go = GameObject.Find(input.path);
                    if (go == null)
                        return $"Error: GameObject not found at path '{input.path}'.\n" +
                               "Tip: Use get_scene_root_objects to find valid root paths, then navigate with get_children.";

                    var slider = go.GetComponent<Slider>();
                    if (slider == null)
                        return $"Error: No Slider component found on GameObject at path '{input.path}'.\n" +
                               "Tip: Use find_interactables to discover GameObjects with Slider components.";

                    var rt = go.GetComponent<RectTransform>();
                    var result = new JSONObject();
                    result["direction"] = slider.direction.ToString();
                    result["minValue"] = slider.minValue;
                    result["maxValue"] = slider.maxValue;
                    result["value"] = slider.value;
                    result["wholeNumbers"] = slider.wholeNumbers;
                    result["interactable"] = slider.interactable;
                    result["active"] = go.activeInHierarchy;
                    if (rt != null)
                    {
                        var worldWidth = rt.rect.width * rt.lossyScale.x;
                        var worldHeight = rt.rect.height * rt.lossyScale.y;
                        result["worldWidth"] = worldWidth;
                        result["worldHeight"] = worldHeight;
                    }
                    return result.ToString();
                });
        }

        private static Vector3 UnityPositionToOpenXR(Vector3 unityPos)
        {
            return new Vector3(unityPos.x, unityPos.y, -unityPos.z);
        }

        private static Quaternion UnityRotationToOpenXR(Quaternion unityRot)
        {
            return new Quaternion(-unityRot.x, -unityRot.y, unityRot.z, unityRot.w);
        }

        private static System.Collections.Generic.List<GameObject> FindAllByHierarchyPath(string path)
        {
            var results = new System.Collections.Generic.List<GameObject>();
            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (GetHierarchyPath(t) == path)
                {
                    results.Add(t.gameObject);
                }
            }
            return results;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var sb = new StringBuilder(transform.name);
            var parent = transform.parent;
            while (parent != null)
            {
                sb.Insert(0, '/');
                sb.Insert(0, parent.name);
                parent = parent.parent;
            }
            return sb.ToString();
        }

        [System.Serializable]
        private struct PathInput
        {
            public string path;
        }

        [System.Serializable]
        private struct Vector3Input
        {
            public float x;
            public float y;
            public float z;
        }

        [System.Serializable]
        private struct QuaternionInput
        {
            public float x;
            public float y;
            public float z;
            public float w;
        }
    }

}

#endif // USING_XR_SDK_OPENXR
