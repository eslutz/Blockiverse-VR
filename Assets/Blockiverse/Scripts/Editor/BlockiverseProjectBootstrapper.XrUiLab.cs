using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.VR;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        const string XrUiInteractionLabName = "XR UI Interaction Lab";

        static readonly Vector2 XrUiLabPanelSize = new(520.0f, 360.0f);
        static readonly Vector2 XrUiLabStatusSize = new(960.0f, 170.0f);
        static readonly Color XrUiLabPanelColor = new(0.07f, 0.08f, 0.09f, 0.98f);
        static readonly Color XrUiLabAltPanelColor = new(0.09f, 0.11f, 0.14f, 0.98f);

        static void EnsureXrUiInteractionLab(Scene scene)
        {
            GameObject labObject = FindRootGameObject(scene, XrUiInteractionLabName);
            if (labObject != null)
                UnityEngine.Object.DestroyImmediate(labObject);

            labObject = new GameObject(XrUiInteractionLabName);
            SceneManager.MoveGameObjectToScene(labObject, scene);

            labObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            labObject.transform.localScale = Vector3.one;

            EnsureXrUiLabStubFloor(labObject.transform);

            GameObject rig = FindRootGameObject(scene, BlockiverseProject.XrRigRootName);
            Transform cameraOffset = rig != null ? rig.transform.Find("Camera Offset") : null;
            Transform head = cameraOffset != null ? cameraOffset.Find("Main Camera") : null;

            var labCanvases = new List<Canvas>();
            var labButtons = new List<Button>();
            var labStatusLabels = new List<TMP_Text>();
            var labOptionNames = new List<string>();
            TMP_Text globalStatus = EnsureXrUiLabStatusCanvas(labObject.transform, head, labCanvases);

            EnsureXrUiLabPanel(
                labObject.transform,
                head,
                labCanvases,
                labButtons,
                labStatusLabels,
                labOptionNames,
                "A Current",
                "A: Current Canvas",
                "World-space Canvas + TrackedDeviceGraphicRaycaster + Main Camera",
                new Vector3(-1.05f, 1.45f, 2.05f),
                configureCanvas: canvas => ConfigureCanvasWorldCamera(canvas, head));

            EnsureXrUiLabPanel(
                labObject.transform,
                head,
                labCanvases,
                labButtons,
                labStatusLabels,
                labOptionNames,
                "B No Camera",
                "B: No Canvas Camera",
                "World-space Canvas + TrackedDeviceGraphicRaycaster + null worldCamera",
                new Vector3(-0.35f, 1.45f, 2.05f),
                configureCanvas: canvas => canvas.worldCamera = null);

            EnsureXrUiLabPanel(
                labObject.transform,
                head,
                labCanvases,
                labButtons,
                labStatusLabels,
                labOptionNames,
                "C Both Raycasters",
                "C: Both Raycasters",
                "TrackedDeviceGraphicRaycaster plus legacy GraphicRaycaster",
                new Vector3(0.35f, 1.45f, 2.05f),
                configureCanvas: canvas => ConfigureCanvasWorldCamera(canvas, head));

            EnsureXrUiLabPanel(
                labObject.transform,
                head,
                labCanvases,
                labButtons,
                labStatusLabels,
                labOptionNames,
                "D Large Target",
                "D: Large Target",
                "Same tracked canvas, oversized full-card button target",
                new Vector3(1.05f, 1.45f, 2.05f),
                configureCanvas: canvas => ConfigureCanvasWorldCamera(canvas, head),
                largeButton: true);

            BlockiverseXrUiInteractionLab lab = EnsureComponent<BlockiverseXrUiInteractionLab>(labObject);
            lab.Configure(
                labCanvases.ToArray(),
                globalStatus,
                labButtons.ToArray(),
                labStatusLabels.ToArray(),
                labOptionNames.ToArray());
            RemoveSerializedXrUiLabProbeComponents(labObject);

            EditorUtility.SetDirty(lab);
            EditorUtility.SetDirty(labObject);
        }

        static void RemoveSerializedXrUiLabProbeComponents(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);

                foreach (MonoBehaviour behaviour in child.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null)
                        continue;

                    if (behaviour.GetType().FullName != typeof(BlockiverseXrUiInteractionProbe).FullName)
                        continue;

                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
        }

        static TMP_Text EnsureXrUiLabStatusCanvas(Transform parent, Transform head, List<Canvas> labCanvases)
        {
            GameObject canvasObject = EnsureRectChild(parent, "Status Canvas");
            ConfigureXrUiLabCanvas(canvasObject, XrUiLabStatusSize, new Vector3(0.0f, 2.18f, 2.08f), 140, head);
            EnsureDecorativeCanvasDoesNotReceiveUi(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            labCanvases.Add(canvas);

            Image background = EnsureComponent<Image>(EnsureRectChild(canvasObject.transform, "Background"));
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            ApplySlicedSprite(background, GetUiSprite("feedback_toast"));
            background.color = XrUiLabPanelColor;
            background.raycastTarget = false;

            TMP_Text status = EnsureLabel(
                canvasObject.transform,
                "Status",
                "XR UI Lab\nTry each button with the right trigger.",
                28,
                TextAnchor.UpperLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.0f, 1.0f),
                new Vector2(28.0f, -22.0f),
                new Vector2(900.0f, 130.0f));
            status.raycastTarget = false;
            return status;
        }

        static void EnsureXrUiLabPanel(
            Transform parent,
            Transform head,
            List<Canvas> labCanvases,
            List<Button> labButtons,
            List<TMP_Text> labStatusLabels,
            List<string> labOptionNames,
            string optionName,
            string title,
            string description,
            Vector3 localPosition,
            System.Action<Canvas> configureCanvas,
            bool largeButton = false)
        {
            GameObject canvasObject = EnsureRectChild(parent, $"{optionName} Panel");
            ConfigureXrUiLabCanvas(canvasObject, XrUiLabPanelSize, localPosition, 150 + labCanvases.Count, head);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            configureCanvas?.Invoke(canvas);
            EnsureTrackedDeviceRaycaster(canvasObject);
            if (optionName == "C Both Raycasters")
                EnsureComponent<GraphicRaycaster>(canvasObject);
            labCanvases.Add(canvas);

            Image background = EnsureComponent<Image>(EnsureRectChild(canvasObject.transform, "Background"));
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            ApplySlicedSprite(background, GetUiSprite("settings_panel"));
            background.color = optionName.EndsWith("Camera") ? XrUiLabAltPanelColor : XrUiLabPanelColor;
            background.raycastTarget = false;

            TMP_Text titleLabel = EnsureLabel(
                canvasObject.transform,
                "Title",
                title,
                30,
                TextAnchor.MiddleLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -26.0f),
                new Vector2(472.0f, 44.0f));
            titleLabel.raycastTarget = false;

            TMP_Text descriptionLabel = EnsureLabel(
                canvasObject.transform,
                "Description",
                description,
                18,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -76.0f),
                new Vector2(472.0f, 60.0f),
                TextDimColor);
            descriptionLabel.raycastTarget = false;

            Vector2 buttonPosition = largeButton ? new Vector2(24.0f, -148.0f) : new Vector2(24.0f, -150.0f);
            Vector2 buttonSize = largeButton ? new Vector2(472.0f, 96.0f) : new Vector2(220.0f, 58.0f);
            Button button = EnsureButtonControl(canvasObject.transform, "Test Button", "Press", buttonPosition, buttonSize);
            foreach (BlockiverseXrUiInteractionProbe staleProbe in button.GetComponents<BlockiverseXrUiInteractionProbe>())
                UnityEngine.Object.DestroyImmediate(staleProbe);

            TMP_Text statusLabel = EnsureLabel(
                canvasObject.transform,
                "Local Status",
                $"{optionName}\nReady",
                18,
                TextAnchor.UpperLeft,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(24.0f, -252.0f),
                new Vector2(472.0f, 92.0f),
                TextDimColor);
            statusLabel.raycastTarget = false;
            labButtons.Add(button);
            labStatusLabels.Add(statusLabel);
            labOptionNames.Add(optionName);

            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(background);
            EditorUtility.SetDirty(titleLabel);
            EditorUtility.SetDirty(descriptionLabel);
            EditorUtility.SetDirty(statusLabel);
        }

        static void ConfigureXrUiLabCanvas(GameObject canvasObject, Vector2 size, Vector3 localPosition, int sortingOrder, Transform head)
        {
            canvasObject.transform.localPosition = localPosition;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.00125f;

            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);

            Canvas canvas = EnsureComponent<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;
            canvas.enabled = true;
            ConfigureCanvasWorldCamera(canvas, head);

            CanvasScaler scaler = EnsureComponent<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10.0f;

            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(scaler);
            EditorUtility.SetDirty(canvasObject);
        }

        static void EnsureXrUiLabStubFloor(Transform parent)
        {
            Transform existing = parent.Find("Stub Floor");
            GameObject floor = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Stub Floor";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0.0f, -0.04f, 1.2f);
            floor.transform.localRotation = Quaternion.identity;
            floor.transform.localScale = new Vector3(6.0f, 0.08f, 5.0f);

            int interactionLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);
            if (interactionLayer >= 0)
                floor.layer = interactionLayer;

            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material material = renderer.sharedMaterial;
                if (material == null || material.name == "Default-Material")
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = new Color(0.18f, 0.19f, 0.20f, 1.0f);
                renderer.sharedMaterial = material;
            }

            EditorUtility.SetDirty(floor);
        }
    }
}
