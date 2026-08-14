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
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Provides helper methods for tracking hover state and creating hover-aware buttons in the editor GUI.
    /// </summary>
    public static class HoverHelper
    {
        private static readonly Dictionary<string, bool> Hovers = new();

        /// <summary>
        /// Clears all tracked hover states.
        /// </summary>
        public static void Reset()
        {
            Hovers.Clear();
        }

        /// <summary>
        /// Determines whether the specified UI element is currently being hovered over by the mouse.
        /// </summary>
        /// <param name="id">A unique identifier for the UI element.</param>
        /// <param name="ev">The current GUI event, used to check mouse position during repaint.</param>
        /// <param name="area">The rectangular area of the UI element to test against.</param>
        /// <returns>True if the mouse is hovering over the element; otherwise, false.</returns>
        public static bool IsHover(string id, Event ev = null, Rect? area = null)
        {
            var hover = false;
            if (area.HasValue && ev?.type == EventType.Repaint)
            {
                hover = area.Value.Contains(ev.mousePosition);
                Hovers[id] = hover;
                return hover;
            }

            Hovers.TryGetValue(id, out hover);
            return hover;
        }

        /// <summary>
        /// Renders a GUILayout button and tracks its hover state.
        /// </summary>
        /// <param name="id">A unique identifier for hover tracking.</param>
        /// <param name="content">The content to display on the button.</param>
        /// <param name="style">The style to apply to the button.</param>
        /// <param name="hover">Outputs whether the button is currently being hovered.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public static bool Button(string id, GUIContent content, GUIStyle style, out bool hover)
        {
            var isClicked = GUILayout.Button(content, style);
            hover = IsHover(id, Event.current, GUILayoutUtility.GetLastRect());
            return isClicked;
        }

        /// <summary>
        /// Renders a GUI button at a specific position and tracks its hover state.
        /// </summary>
        /// <param name="id">A unique identifier for hover tracking.</param>
        /// <param name="rect">The position and size of the button.</param>
        /// <param name="content">The content to display on the button.</param>
        /// <param name="style">The style to apply to the button.</param>
        /// <param name="hover">Outputs whether the button is currently being hovered.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public static bool Button(string id, Rect rect, GUIContent content, GUIStyle style, out bool hover)
        {
            var isClicked = GUI.Button(rect, content, style);
            hover = IsHover(id, Event.current, rect);
            return isClicked;
        }

        /// <summary>
        /// Renders a GUILayout button with an icon overlay and tracks its hover state.
        /// </summary>
        /// <param name="id">A unique identifier for hover tracking.</param>
        /// <param name="label">The label content to display on the button.</param>
        /// <param name="icon">The icon content to overlay on the button.</param>
        /// <param name="buttonStyle">The style to apply to the button.</param>
        /// <param name="iconStyle">The style to apply to the icon overlay.</param>
        /// <param name="hover">Outputs whether the button is currently being hovered.</param>
        /// <returns>True if the button was clicked; otherwise, false.</returns>
        public static bool Button(string id, GUIContent label, GUIContent icon, GUIStyle buttonStyle, GUIStyle iconStyle, out bool hover)
        {
            var isClicked = GUILayout.Button(label, buttonStyle);
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.LabelField(rect, icon, iconStyle);
            hover = IsHover(id, Event.current, rect);
            return isClicked;
        }
    }
}
