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
