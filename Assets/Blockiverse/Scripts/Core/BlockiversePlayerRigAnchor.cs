using UnityEngine;

namespace Blockiverse.Core
{
    // Explicit runtime anchor for the generated player rig. Gameplay assemblies can depend on this
    // Core component without referencing VR-specific rig types or string-searching the hierarchy.
    public sealed class BlockiversePlayerRigAnchor : MonoBehaviour
    {
        static BlockiversePlayerRigAnchor activeAnchor;

        public static bool TryGetRigTransform(out Transform rigTransform)
        {
            if (activeAnchor != null)
            {
                rigTransform = activeAnchor.transform;
                return true;
            }

            rigTransform = null;
            return false;
        }

        void OnEnable()
        {
            activeAnchor = this;
        }

        void OnDisable()
        {
            if (activeAnchor == this)
                activeAnchor = null;
        }

        void OnDestroy()
        {
            if (activeAnchor == this)
                activeAnchor = null;
        }
    }
}
