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
using System.Linq;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;

/// <summary>Provides editor utility methods for configuring OVR overlay canvas layers and displaying messages.</summary>
public static class OVROverlayEditorHelper
{
    public const string DefaultCanvasRenderLayerName = "OVROverlayCanvas Rendering";
    public static int CanvasRenderLayer
    {
        get
        {
            var settings = OVROverlayCanvasSettings.Instance;
            if (settings.CanvasRenderLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(settings.CanvasRenderLayer)))
                return settings.CanvasRenderLayer;

            if (LayerMask.NameToLayer(DefaultCanvasRenderLayerName) is var layer and not -1)
                settings.CanvasRenderLayer = layer;

            return settings.CanvasRenderLayer;
        }
    }

    public const string DefaultHiddenCanvasLayerName = "Overlay UI";
    public static int HiddenCanvasLayer
    {
        get
        {
            var settings = OVROverlayCanvasSettings.Instance;
            if (settings.HiddenCanvasLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(settings.HiddenCanvasLayer)))
                return settings.HiddenCanvasLayer;

            if (LayerMask.NameToLayer(DefaultHiddenCanvasLayerName) is var layer and not -1)
                settings.HiddenCanvasLayer = layer;

            return settings.HiddenCanvasLayer;
        }
    }

    /// <summary>
    /// Returns true if a valid hidden canvas layer has been assigned.
    /// </summary>
    public static bool HiddenCanvasLayerSelected => HiddenCanvasLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(HiddenCanvasLayer));

    /// <summary>Sets the name of a Unity layer in the Tag Manager project settings.</summary>
    /// <param name="layer">The layer index to rename.</param>
    /// <param name="name">The new name to assign to the layer.</param>
    public static void SetLayerName(int layer, string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
        var it = tagManager.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.name == "layers")
            {
                it.GetArrayElementAtIndex(layer).stringValue = name;
                break;
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    /// <summary>Draws a layer selection field and marks the target object as dirty if the layer changes.</summary>
    /// <param name="dirtyObject">The object to mark as dirty when the layer value changes.</param>
    /// <param name="currentLayer">The currently selected layer index.</param>
    /// <param name="label">The label to display for the field.</param>
    /// <param name="tooltip">The tooltip to display for the field.</param>
    /// <returns>The newly selected layer index.</returns>
    public static int DirtyLayerField(UnityEngine.Object dirtyObject, int currentLayer, string label, string tooltip)
    {
        var newLayer = EditorGUILayout.LayerField(new GUIContent(label, tooltip), currentLayer);
        if (currentLayer != newLayer)
        {
            EditorUtility.SetDirty(dirtyObject);
        }
        return newLayer;
    }

    /// <summary>Defines the visual severity types for editor display messages.</summary>
    public enum DisplayMessageType
    {
        Notice,
        Warning,
        Error,
        Check
    }

    /// <summary>Draws a styled message box in the editor with an icon, optional header, and message text.</summary>
    /// <param name="messageType">The type of message that determines the icon and default header.</param>
    /// <param name="messageText">The body text of the message.</param>
    /// <param name="headerOverride">An optional header string that overrides the default header for the message type.</param>
    public static void DisplayMessage(DisplayMessageType messageType, string messageText, string headerOverride = null)
    {
        var (iconUri, header) = messageType switch
        {
            DisplayMessageType.Check => ("TestPassed", ""),
            DisplayMessageType.Error => ("console.erroricon.sml", "Error"),
            DisplayMessageType.Warning => ("console.warnicon.sml", "Warning"),
            _ => ("console.infoicon.sml", "Notice"),
        };
        header = headerOverride ?? header;

        EditorGUILayout.BeginHorizontal(Styles.GUIStyles.DialogBox); //2-column wrapper
        EditorGUILayout.BeginVertical(GUILayout.Width(20)); //column 1: icon
        EditorGUILayout.LabelField(EditorGUIUtility.IconContent(iconUri), GUILayout.Width(20));
        EditorGUILayout.EndVertical(); //end column 1: icon
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true)); //column 2: label, message
        if (!string.IsNullOrEmpty(header))
        {
            GUILayout.Label(header, EditorStyles.boldLabel);
        }
        GUILayout.Label(messageText, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical(); //end column 2: label, message, objects
        EditorGUILayout.EndHorizontal(); //end 2-column wrapper
    }

    /// <summary>Draws the UI for selecting or creating the hidden canvas layer used by OVR overlay canvases.</summary>
    /// <param name="canvasLayer">The currently assigned canvas layer index.</param>
    /// <param name="setCanvasLayer">A callback invoked to set the new canvas layer index.</param>
    /// <param name="setMask">A callback invoked to set the camera culling mask.</param>
    public static void CanvasLayerSelectionUI(int canvasLayer, Action<int> setCanvasLayer, Action<int> setMask)
    {
        if (HiddenCanvasLayer != -1)
        {
            var layerName = LayerMask.LayerToName(HiddenCanvasLayer);
            if (canvasLayer != HiddenCanvasLayer && GUILayout.Button($"Set Layer to \"{layerName}\""))
            {
                setCanvasLayer(HiddenCanvasLayer);
                setMask(~LayerMask.GetMask(layerName));
                var evt = new UnifiedEventData("canvas_set_layer_clicked");
                evt.Send();
            }
        }
        else if (FindUnusedLayer() is not { } unusedLayer)
        {
            IssueTracker.TrackError(IssueTracker.SDK.Core, "ovr-overlay-all-layers-used",
                "All Layers are Used!");
        }
        else if (GUILayout.Button($"Set Layer {unusedLayer} to \"{DefaultHiddenCanvasLayerName}\""))
        {
            SetLayerName(unusedLayer, DefaultHiddenCanvasLayerName);
            setCanvasLayer(unusedLayer);
            setMask(~(1 << unusedLayer));
            var evt = new UnifiedEventData("canvas_set_default_layer_clicked");
            evt.Send();
        }
    }

    /// <summary>Finds an unused Unity layer index.</summary>
    /// <param name="lowest">If true, returns the lowest unused layer; otherwise returns the highest.</param>
    /// <returns>The index of an unused layer, or null if all layers are in use.</returns>
    public static int? FindUnusedLayer(bool lowest = true)
    {
        var layers = Enumerable.Range(0, 32);
        if (!lowest)
            layers = layers.Reverse();

        foreach (var i in layers)
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
            {
                return i;
            }
        }
        return null;
    }
}
