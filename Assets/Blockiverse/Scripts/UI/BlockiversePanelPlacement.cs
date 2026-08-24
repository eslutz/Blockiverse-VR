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
        // RETIRED as the title fixture's geometry (2.0 m out, 1.4 m up, no pitch). Eric reported
        // (2026-08-24) that the title menu sat at a pleasing distance and tilt but every screen he
        // navigated to was "further away and straight up and down" — because the title panel is
        // shown BEFORE the fixture pose exists and so recenters at the menu profile (0.95 m, 10
        // degrees of pitch), while every later screen applies this fixture instead. Two formulas
        // for one family of screens. The fixture now derives from the SAME menu-profile constants
        // the recenter path uses, so all anchored screens share one distance and one tilt.
        //
        // Kept only as the historical values; nothing should place a panel with them.
        public const float LegacyTitlePanelDistanceMeters = 2.0f;
        public const float LegacyTitlePanelHeightMeters = 1.4f;

        /// <summary>
        /// The heading the title mini-world's menu always faces along, in degrees.
        /// </summary>
        /// <remarks>
        /// A CONSTANT, not the rig's current yaw. Deriving it from the rig meant the menu's world
        /// bearing from spawn changed with however the player happened to be turned, so returning
        /// to the title after a session could leave the menu behind them (Eric, 2026-08-24). The
        /// player is oriented to face this heading instead — see BlockiverseRigPlacement.
        /// </remarks>
        public const float TitleMenuHeadingDegrees = 0.0f;

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
