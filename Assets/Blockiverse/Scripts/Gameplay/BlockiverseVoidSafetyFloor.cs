using System;
using Blockiverse.Core;
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
        public const float DefaultRecoveryContactToleranceMeters = 0.25f;
        public const float DefaultRecoveryCheckIntervalSeconds = 0.25f;

        [SerializeField] float topY;
        [SerializeField] bool hasRecoverySpawnPosition;
        [SerializeField] BlockPosition recoverySpawnPosition;
        [SerializeField] float recoveryContactY;

        Transform cachedRigTransform;
        Transform cachedHeadTransform;
        float nextRecoveryCheckTime;

        public float TopY => topY;
        public bool HasRecoverySpawnPosition => hasRecoverySpawnPosition;
        public BlockPosition RecoverySpawnPosition => recoverySpawnPosition;
        public float RecoveryContactY => recoveryContactY;

        public void Configure(
            WorldBounds bounds,
            float fallAllowanceMeters = DefaultFallAllowanceMeters,
            float thicknessMeters = DefaultThicknessMeters,
            float horizontalMarginMeters = DefaultHorizontalMarginMeters,
            string layerName = null,
            BlockPosition? recoverySpawnPosition = null)
        {
            int layer = !string.IsNullOrWhiteSpace(layerName)
                ? LayerMask.NameToLayer(layerName)
                : -1;
            if (layer < 0 && layerName == BlockiverseProject.InteractionLayerName)
                layer = BlockiverseProject.InteractionLayerIndex;

            Configure(bounds, fallAllowanceMeters, thicknessMeters, horizontalMarginMeters, layer, recoverySpawnPosition);
        }

        public void Configure(
            WorldBounds bounds,
            float fallAllowanceMeters,
            float thicknessMeters,
            float horizontalMarginMeters,
            int layer,
            BlockPosition? recoverySpawnPosition = null)
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

            hasRecoverySpawnPosition = recoverySpawnPosition.HasValue;
            this.recoverySpawnPosition = recoverySpawnPosition.GetValueOrDefault();
            recoveryContactY = topY + DefaultRecoveryContactToleranceMeters;
            cachedRigTransform = null;
            cachedHeadTransform = null;
            nextRecoveryCheckTime = 0.0f;
        }

        void Update()
        {
            if (!hasRecoverySpawnPosition || Time.time < nextRecoveryCheckTime)
                return;

            nextRecoveryCheckTime = Time.time + DefaultRecoveryCheckIntervalSeconds;
            TryRecoverRigIfBelowFloor();
        }

        public bool TryRecoverRigIfBelowFloor()
        {
            if (!hasRecoverySpawnPosition)
                return false;

            Transform rig = ResolveRigTransform();
            if (rig == null)
                return false;

            bool rigReachedFloor = rig.position.y <= recoveryContactY;
            bool headReachedFloor = false;
            Transform head = ResolveHeadTransform();

            if (head != null)
                headReachedFloor = head.position.y <= recoveryContactY;

            if (!rigReachedFloor && !headReachedFloor)
                return false;

            CreativeWorldManager.PositionRigAtSpawn(recoverySpawnPosition);
            return true;
        }

        Transform ResolveRigTransform()
        {
            if (cachedRigTransform != null)
                return cachedRigTransform;

            cachedRigTransform = BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig)
                ? rig
                : null;
            return cachedRigTransform;
        }

        Transform ResolveHeadTransform()
        {
            if (cachedHeadTransform != null)
                return cachedHeadTransform;

            Camera head = Camera.main;
            cachedHeadTransform = head != null ? head.transform : null;
            return cachedHeadTransform;
        }
    }
}
