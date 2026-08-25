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
        /// POSITION HAS THE SAME PROBLEM, and fixing only the yaw left the other half of it
        /// visible: the rig origin is the tracking origin, not the player, so the head sits
        /// wherever they have physically walked to inside their room. Placing the RIG on the spawn
        /// block therefore places the head that same room-offset away from it, while the menu is
        /// pinned relative to spawn — so the player arrives standing beside the menu, aimed at the
        /// heading it is on rather than at the menu itself. "I'm off to the left a little bit and
        /// looking to the left past the menu" (Eric, 2026-08-25) is exactly a lateral room offset:
        /// the yaw was right, which is why it reads as being in the wrong PLACE rather than the
        /// wrong direction. Offsetting the rig by the head's flattened local position puts the
        /// player's eyes on the spawn block, which is what every caller already believed it did.
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

            Vector3 headTarget = new(spawnPosition.X + 0.5f, spawnPosition.Y, spawnPosition.Z + 0.5f);
            float targetRigYaw = ResolveRigYawForViewHeading(rig, headingDegrees);
            Vector3 position = ResolveRigPositionForHead(rig, headTarget, targetRigYaw);

            if (!BlockiverseComfortTransition.TryMoveRigWithComfort(rig, position, targetRigYaw))
                rig.SetPositionAndRotation(position, Quaternion.Euler(0.0f, targetRigYaw, 0.0f));
        }

        /// <summary>
        /// Where the rig has to stand so the player's HEAD ends up over
        /// <paramref name="headTarget"/> once <paramref name="rigYawDegrees"/> is applied.
        /// </summary>
        /// <remarks>
        /// Public for the same reason <see cref="ResolveRigYawForViewHeading"/> is: on desktop the
        /// camera sits at the rig origin, so the offset is zero and the uncorrected code is
        /// indistinguishable from the corrected one. The yaw is an input rather than the rig's
        /// current one because the head's offset is expressed in RIG-LOCAL space — turning the rig
        /// swings it around the origin, so an offset measured before the turn and applied after it
        /// would be wrong by exactly the angle of the turn.
        ///
        /// Only X and Z are compensated. Y is the floor the rig stands on; the head's height above
        /// it is the player's own height, and subtracting that would bury the rig.
        /// </remarks>
        public static Vector3 ResolveRigPositionForHead(Transform rig, Vector3 headTarget, float rigYawDegrees)
        {
            if (rig == null)
                return headTarget;

            Camera head = Camera.main;
            if (head == null)
                return headTarget;

            Vector3 localHead = rig.InverseTransformPoint(head.transform.position);
            Vector3 offset = Quaternion.Euler(0.0f, rigYawDegrees, 0.0f) * new Vector3(localHead.x, 0.0f, localHead.z);

            return new Vector3(headTarget.x - offset.x, headTarget.y, headTarget.z - offset.z);
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
