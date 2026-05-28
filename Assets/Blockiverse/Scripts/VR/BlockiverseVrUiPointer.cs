using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    public sealed class BlockiverseVrUiPointer : MonoBehaviour
    {
        [SerializeField] Transform rayOrigin;
        [SerializeField] Camera eventCamera;
        [SerializeField] float maxDistanceMeters = 5.0f;

        readonly List<RaycastResult> raycastResults = new();

        GameObject currentTarget;

        public bool IsPointerOverUi
        {
            get
            {
                Refresh();
                return currentTarget != null;
            }
        }

        public GameObject CurrentTarget => currentTarget;

        public void Configure(Transform origin, Camera camera, float maxDistance)
        {
            rayOrigin = origin;
            eventCamera = camera;
            maxDistanceMeters = maxDistance;
        }

        public bool TryClick()
        {
            if (!TryGetCurrentRaycast(out RaycastResult result))
                return false;

            var pointerEvent = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = result.screenPosition,
                pointerCurrentRaycast = result,
                pointerPressRaycast = result
            };

            GameObject clickTarget = ExecuteEvents.ExecuteHierarchy(
                result.gameObject,
                pointerEvent,
                ExecuteEvents.pointerClickHandler);

            currentTarget = clickTarget != null ? clickTarget : result.gameObject;
            return clickTarget != null;
        }

        public void Refresh()
        {
            currentTarget = TryGetCurrentRaycast(out RaycastResult result)
                ? result.gameObject
                : null;
        }

        bool TryGetCurrentRaycast(out RaycastResult bestResult)
        {
            bestResult = default;

            if (rayOrigin == null || EventSystem.current == null)
                return false;

            Ray pointerRay = new(rayOrigin.position, rayOrigin.forward);
            float bestDistance = maxDistanceMeters;
            bool found = false;

            GraphicRaycaster[] raycasters = FindObjectsByType<GraphicRaycaster>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (GraphicRaycaster raycaster in raycasters)
            {
                if (raycaster == null || !raycaster.isActiveAndEnabled)
                    continue;

                Canvas canvas = raycaster.GetComponent<Canvas>();

                if (canvas == null || !canvas.enabled || canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                RectTransform canvasRect = canvas.transform as RectTransform;

                if (canvasRect == null)
                    continue;

                Plane canvasPlane = new(canvasRect.forward, canvasRect.position);

                if (!canvasPlane.Raycast(pointerRay, out float planeDistance) ||
                    planeDistance < 0.0f ||
                    planeDistance > bestDistance)
                {
                    continue;
                }

                Camera camera = canvas.worldCamera != null ? canvas.worldCamera : eventCamera;
                Vector3 worldPoint = pointerRay.GetPoint(planeDistance);
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldPoint);

                var eventData = new PointerEventData(EventSystem.current)
                {
                    position = screenPoint
                };

                raycastResults.Clear();
                raycaster.Raycast(eventData, raycastResults);

                if (raycastResults.Count == 0)
                    continue;

                RaycastResult result = raycastResults[0];
                result.worldPosition = worldPoint;
                result.distance = planeDistance;
                bestResult = result;
                bestDistance = planeDistance;
                found = true;
            }

            return found;
        }
    }
}
