using Blockiverse.Core;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Rig teleport helpers, extracted from CreativeWorldManager so world simulation does not depend
    // on the comfort-transition presentation component. Shared by world load
    // (BlockiverseWorldSessionController), survival respawn (SurvivalVitalsRuntime), and void
    // recovery (BlockiverseVoidSafetyFloor); public because no InternalsVisibleTo covers the UI
    // assembly. Every method no-ops when there is no rig in the scene, which is the headless case.
    public static class BlockiverseRigPlacement
    {
        // Places the rig standing on the given block's centre, preserving its current heading.
        public static void PositionAtSpawn(BlockPosition spawnPosition)
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return;

            Vector3 position = new(spawnPosition.X + 0.5f, spawnPosition.Y, spawnPosition.Z + 0.5f);
            float yawDegrees = rig.eulerAngles.y;
            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, yawDegrees))
                rig.position = position;
        }

        /// <summary>
        /// Places the rig on the given block's centre and turns it so the player's VIEW ends up
        /// pointing along <paramref name="headingDegrees"/>, whichever way they are physically
        /// facing in the room.
        /// </summary>
        /// <remarks>
        /// Why this exists: <see cref="PositionAtSpawn"/> preserves the rig's heading, and the rig
        /// heading is NOT the view heading — the headset drives the camera's local rotation, so
        /// view yaw = rig yaw + head-local yaw. Returning to the title mini-world therefore put
        /// the player down still facing wherever they had physically turned, with the menu pinned
        /// at a fixed world heading behind them (Eric, 2026-08-24). Subtracting the head-local yaw
        /// is what makes "face the menu" mean the player's eyes rather than the rig's axis.
        ///
        /// COMFORT: this is a forced re-orientation, which is only acceptable because of how it is
        /// done. It is instantaneous — rotation sickness comes from vection, sustained visual
        /// rotation with no matching inner-ear signal, and a single-frame yaw change produces no
        /// optical flow at all (the same reason snap turn is this project's comfort default and
        /// smooth turn is opt-in). It also runs behind the existing fade-to-black, and only on
        /// entering the title mini-world — a transition the player asked for, with no locomotion
        /// in progress. Animating it instead WOULD be the anti-pattern. The one real cost is that
        /// virtual forward no longer matches room forward afterwards, which is unavoidable for any
        /// behaviour that keeps the menu fixed and still has the player facing it.
        /// </remarks>
        public static void PositionAtSpawnFacing(BlockPosition spawnPosition, float headingDegrees)
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return;

            Vector3 position = new(spawnPosition.X + 0.5f, spawnPosition.Y, spawnPosition.Z + 0.5f);
            float targetRigYaw = ResolveRigYawForViewHeading(rig, headingDegrees);

            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, targetRigYaw))
                rig.SetPositionAndRotation(position, Quaternion.Euler(0.0f, targetRigYaw, 0.0f));
        }

        /// <summary>
        /// The rig yaw that puts the player's view on <paramref name="headingDegrees"/>. Public so
        /// the arithmetic can be pinned without a headset — the bug it fixes is invisible on
        /// desktop, where the camera has no tracked rotation and rig yaw equals view yaw.
        /// </summary>
        public static float ResolveRigYawForViewHeading(Transform rig, float headingDegrees)
        {
            if (rig == null)
                return headingDegrees;

            Camera head = Camera.main;
            if (head == null)
                return headingDegrees;

            // Pitch near the poles makes an euler yaw meaningless, so take the head's forward,
            // flatten it, and measure THAT. A player looking at their feet when they hit "return
            // to title" must not be spun 180 degrees by a decomposition artefact.
            Vector3 flatForward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude <= 1e-6f)
                flatForward = Vector3.ProjectOnPlane(head.transform.up, Vector3.up);
            if (flatForward.sqrMagnitude <= 1e-6f)
                return headingDegrees;

            float viewYaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up).eulerAngles.y;
            float headLocalYaw = Mathf.DeltaAngle(rig.eulerAngles.y, viewYaw);
            return headingDegrees - headLocalYaw;
        }

        // Places the rig at a saved player position/heading (world load with saved player state).
        public static void PositionAt(Vector3 position, float yawDegrees)
        {
            if (!BlockiversePlayerRigAnchor.TryGetRigTransform(out Transform rig))
                return;

            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, yawDegrees))
                rig.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
        }
    }
}
