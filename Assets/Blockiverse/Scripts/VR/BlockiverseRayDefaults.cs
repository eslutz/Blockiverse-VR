using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Blockiverse.VR
{
    public static class BlockiverseRayDefaults
    {
        public const float PointerLineWidthMeters = 0.01f;
        public const float PointerMinimumLineLengthMeters = 0.75f;

        public static void ConfigureInteractionRay(
            XRRayInteractor ray,
            Transform rayOrigin,
            LayerMask raycastMask)
        {
            if (ray == null)
                return;

            ConfigureCommonRay(ray, rayOrigin, raycastMask);
            ray.lineType = XRRayInteractor.LineType.StraightLine;
            ray.enableUIInteraction = true;
            ray.blockUIOnInteractableSelection = false;
            ray.maxRaycastDistance = CreativeInteractionController.MaxBlockInteractionReachMeters;
            ray.manipulateAttachTransform = false;
            // The ray is used for UI and for reading the current block raycast hit. It should not
            // select 3D XRI interactables such as teleport areas.
            ray.interactionLayers = 0;
        }

        public static void ConfigureTeleportRay(
            XRRayInteractor ray,
            Transform rayOrigin,
            LayerMask raycastMask)
        {
            if (ray == null)
                return;

            ConfigureCommonRay(ray, rayOrigin, raycastMask);
            ray.lineType = XRRayInteractor.LineType.ProjectileCurve;
            ray.enableUIInteraction = false;
            ray.manipulateAttachTransform = false;
        }

        public static void ConfigureLineVisual(
            LineRenderer lineRenderer,
            XRInteractorLineVisual lineVisual,
            Material pointerMaterial,
            Color? pointerColor)
        {
            if (lineRenderer != null)
            {
                lineRenderer.useWorldSpace = true;
                lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
                lineRenderer.receiveShadows = false;

                if (pointerMaterial != null)
                    lineRenderer.sharedMaterial = pointerMaterial;

                if (pointerColor.HasValue)
                {
                    lineRenderer.startColor = pointerColor.Value;
                    lineRenderer.endColor = pointerColor.Value;
                }
            }

            if (lineVisual == null)
                return;

            lineVisual.lineWidth = PointerLineWidthMeters;
            lineVisual.overrideInteractorLineLength = false;
            lineVisual.autoAdjustLineLength = true;
            lineVisual.minLineLength = PointerMinimumLineLengthMeters;
            lineVisual.stopLineAtFirstRaycastHit = true;
            lineVisual.smoothMovement = true;
            lineVisual.overrideInteractorLineOrigin = false;
            lineVisual.lineOriginTransform = null;
        }

        public static void ConfigureLineVisual(XRRayInteractor ray)
        {
            if (ray == null)
                return;

            ConfigureLineVisual(
                ray.GetComponent<LineRenderer>(),
                ray.GetComponent<XRInteractorLineVisual>(),
                null,
                null);
        }

        static void ConfigureCommonRay(
            XRRayInteractor ray,
            Transform rayOrigin,
            LayerMask raycastMask)
        {
            ray.rayOriginTransform = rayOrigin;
            ray.raycastMask = raycastMask;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            ray.hitClosestOnly = true;
            ray.blendVisualLinePoints = true;
            ray.raycastSnapVolumeInteraction = XRRayInteractor.QuerySnapVolumeInteraction.Ignore;
        }
    }
}
