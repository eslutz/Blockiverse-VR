using System;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BlockiverseVoidSafetyFloor : MonoBehaviour
    {
        public const float DefaultFallAllowanceMeters = 8.0f;
        public const float DefaultThicknessMeters = 1.0f;
        public const float DefaultHorizontalMarginMeters = 8.0f;

        [SerializeField] float topY;

        public float TopY => topY;

        public void Configure(
            WorldBounds bounds,
            float fallAllowanceMeters = DefaultFallAllowanceMeters,
            float thicknessMeters = DefaultThicknessMeters,
            float horizontalMarginMeters = DefaultHorizontalMarginMeters,
            string layerName = null)
        {
            int layer = !string.IsNullOrWhiteSpace(layerName)
                ? LayerMask.NameToLayer(layerName)
                : -1;

            Configure(bounds, fallAllowanceMeters, thicknessMeters, horizontalMarginMeters, layer);
        }

        public void Configure(
            WorldBounds bounds,
            float fallAllowanceMeters,
            float thicknessMeters,
            float horizontalMarginMeters,
            int layer)
        {
            if (fallAllowanceMeters < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(fallAllowanceMeters), "Fall allowance must be non-negative.");
            if (thicknessMeters <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(thicknessMeters), "Safety floor thickness must be positive.");
            if (horizontalMarginMeters < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(horizontalMarginMeters), "Safety floor margin must be non-negative.");

            topY = -fallAllowanceMeters;

            BoxCollider collider = GetComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.size = new Vector3(
                bounds.Width + horizontalMarginMeters * 2.0f,
                thicknessMeters,
                bounds.Depth + horizontalMarginMeters * 2.0f);
            collider.center = new Vector3(
                bounds.Width * 0.5f,
                topY - thicknessMeters * 0.5f,
                bounds.Depth * 0.5f);

            if (layer >= 0)
                gameObject.layer = layer;
        }
    }
}
