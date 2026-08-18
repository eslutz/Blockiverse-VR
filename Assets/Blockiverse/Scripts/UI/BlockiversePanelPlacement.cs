using UnityEngine;

namespace Blockiverse.UI
{
    // How a routed world-space panel decides where it lives.
    public enum BlockiversePanelPlacementMode
    {
        // Legacy: recenter in front of the headset once when shown, then stay put.
        RecenterOnShow = 0,

        // Fixture of the world: pose is supplied explicitly (title mini-world menus sit at a
        // spawn-relative pose like a sign) and never derives from the headset.
        WorldFixed = 1,

        // In-session menus: glide to a comfortable pose in front of the player and only
        // re-center after the head yaws or the player moves past a threshold. Height is
        // locked to head height, pitch is never applied — no gaze-locking.
        LazyFollow = 2,
    }

    // Pure pose math shared by the presenter, the bootstrapper, and tests. Kept free of
    // component state so the placement contracts can be asserted without a scene.
    public static class BlockiversePanelPlacement
    {
        public const float DefaultFollowYawThresholdDegrees = 30.0f;
        public const float DefaultFollowDistanceThresholdMeters = 1.5f;
        public const float DefaultFollowSmoothingSeconds = 0.35f;
        public const float DefaultTitlePanelDistanceMeters = 2.0f;
        public const float DefaultTitlePanelHeightMeters = 1.4f;

        // A world-fixed pose in front of a spawn block: `distance` metres out along the
        // spawn-facing yaw, `height` metres above the block's base, facing back toward
        // spawn. Independent of any headset pose.
        public static Pose SpawnRelativePose(Vector3 spawnBase, float spawnYawDegrees, float distance, float height)
        {
            Vector3 forward = YawToForward(spawnYawDegrees);
            Vector3 position = spawnBase + forward * Mathf.Max(0.1f, distance) + Vector3.up * height;
            // The panel faces the player standing at spawn, i.e. its forward points away
            // from spawn along the same yaw (uGUI canvases face +Z toward the viewer's -Z).
            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        // The follow target for a head pose: `distance` metres ahead along the flattened
        // head yaw, at head height plus `verticalOffset`, no pitch.
        public static Pose FollowTargetPose(
            Vector3 headPosition,
            Vector3 headForward,
            float distance,
            float horizontalOffset,
            float verticalOffset)
        {
            Vector3 forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 position = headPosition
                + forward * Mathf.Max(0.1f, distance)
                + right * horizontalOffset
                + Vector3.up * verticalOffset;
            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        // True when the panel has drifted far enough out of the player's comfortable
        // frame that a lazy follow should re-center it.
        public static bool ShouldRecenter(
            Pose currentPanelPose,
            Vector3 headPosition,
            Vector3 headForward,
            float distance,
            float yawThresholdDegrees,
            float distanceThresholdMeters)
        {
            Vector3 headFlat = Vector3.ProjectOnPlane(headForward, Vector3.up);
            Vector3 panelFlat = Vector3.ProjectOnPlane(currentPanelPose.rotation * Vector3.forward, Vector3.up);
            if (headFlat.sqrMagnitude > Mathf.Epsilon && panelFlat.sqrMagnitude > Mathf.Epsilon)
            {
                float yawDelta = Vector3.Angle(headFlat, panelFlat);
                if (yawDelta > yawThresholdDegrees)
                    return true;
            }

            Vector3 idealFlat = Vector3.ProjectOnPlane(
                headPosition + (headFlat.sqrMagnitude > Mathf.Epsilon ? headFlat.normalized : Vector3.forward) * distance,
                Vector3.up);
            Vector3 panelPositionFlat = Vector3.ProjectOnPlane(currentPanelPose.position, Vector3.up);
            return Vector3.Distance(idealFlat, panelPositionFlat) > distanceThresholdMeters;
        }

        public static Pose SmoothToward(Pose current, Pose target, float smoothingSeconds, float deltaTime)
        {
            if (smoothingSeconds <= 0.0f || deltaTime <= 0.0f)
                return target;

            // Exponential smoothing: frame-rate independent approach to the target.
            float t = 1.0f - Mathf.Exp(-deltaTime / smoothingSeconds);
            return new Pose(
                Vector3.Lerp(current.position, target.position, t),
                Quaternion.Slerp(current.rotation, target.rotation, t));
        }

        static Vector3 YawToForward(float yawDegrees) =>
            Quaternion.Euler(0.0f, yawDegrees, 0.0f) * Vector3.forward;
    }
}
