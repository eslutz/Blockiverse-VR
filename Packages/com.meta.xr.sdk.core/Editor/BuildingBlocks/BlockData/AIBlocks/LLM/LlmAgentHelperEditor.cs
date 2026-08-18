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

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Custom inspector editor for the LlmAgentHelper component.
    /// </summary>
    [CustomEditor(typeof(LlmAgentHelper))]
    public class LlmAgentHelperEditor : UnityEditor.Editor
    {
        private SerializedProperty _userInput,
            _selectedPrompt,
            _includeImage,
            _imageSource,
            _promptImage,
            _promptImageUrl,
            _useEditorFakeCameraPreview;

        private void OnEnable()
        {
            EnsureProperties();
        }

        /// <summary>
        /// Draws the custom inspector GUI for the LLM agent helper.
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (target == null) return;

            // SerializedProperty handles cached in OnEnable can become invalid after a
            // domain reload, prefab edit / play-mode swap, or undo of object create.
            // Drawing with a null SerializedProperty throws NullReferenceException from
            // inside Unity's PropertyField (the "null ref on ID" reported when clicking
            // the prompt enum). Re-fetching any null property is cheap and makes the
            // inspector robust to those races.
            EnsureProperties();

            var helper = (LlmAgentHelper)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(_userInput, new GUIContent("User Input"));
            EditorGUILayout.PropertyField(_selectedPrompt, new GUIContent("Default Prompt"));

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(_includeImage, new GUIContent("Include Image"));

            if (_includeImage.boolValue)
            {
                EditorGUILayout.PropertyField(_imageSource, new GUIContent("Image Source"));
                var src = (PromptImageSource)_imageSource.enumValueIndex;
                switch (src)
                {
                    case PromptImageSource.InspectorTexture:
                        EditorGUILayout.PropertyField(_promptImage, new GUIContent("Prompt Image"));
                        break;

                    case PromptImageSource.ImageUrl:
                        EditorGUILayout.PropertyField(_promptImageUrl, new GUIContent("Prompt Image URL"));
                        break;

                    case PromptImageSource.Camera:
                        if (_useEditorFakeCameraPreview != null)
                        {
                            EditorGUILayout.PropertyField(_useEditorFakeCameraPreview, new GUIContent("Use Editor Fake Camera"));
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Send Prompt"))
                {
                    helper.SendPrompt();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        // Re-fetches any SerializedProperty handle that has gone null. Safe to call
        // every OnInspectorGUI; FindProperty on an already-set field is a no-op here
        // because each call only runs when the cached field is null.
        private void EnsureProperties()
        {
            if (serializedObject == null) return;
            if (_userInput == null) _userInput = serializedObject.FindProperty("userInput");
            if (_selectedPrompt == null) _selectedPrompt = serializedObject.FindProperty("selectedPrompt");
            if (_includeImage == null) _includeImage = serializedObject.FindProperty("includeImage");
            if (_imageSource == null) _imageSource = serializedObject.FindProperty("imageSource");
            if (_promptImage == null) _promptImage = serializedObject.FindProperty("promptImage");
            if (_promptImageUrl == null) _promptImageUrl = serializedObject.FindProperty("promptImageUrl");
            if (_useEditorFakeCameraPreview == null) _useEditorFakeCameraPreview = serializedObject.FindProperty("useEditorFakeCameraPreview");
        }
    }
}
#endif
