using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // World-space to voxel-cell conversion, owned here rather than on an interaction component so
    // the world simulation can use it without depending on the interaction/presentation layer.
    // CreativeInteractionController.ToBlockPosition forwards to this so there is one implementation.
    public static class VoxelWorldCoordinates
    {
        public static BlockPosition ToBlockPosition(Vector3 worldPosition) => new(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y),
            Mathf.FloorToInt(worldPosition.z));
    }
}
