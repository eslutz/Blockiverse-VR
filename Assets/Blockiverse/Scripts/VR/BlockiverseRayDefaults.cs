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
        public const int DefaultXriInteractionLayerMask = 1;

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
            // XRI UI mirroring still requires layer overlap with XRSimpleInteractable surfaces.
            // Selection/activation bindings remain unused so the ray does not select 3D tools.
            ray.interactionLayers = DefaultXriInteractionLayerMask;
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
            // Controller poses are already updated in Update + BeforeRender. XRI line smoothing
            // can retain pre-tracking/menu-start points for a few frames on Quest, which looks
            // like a drifting ribbon before gameplay starts.
            lineVisual.smoothMovement = false;
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
