using Blockiverse.Networking;
using UnityEngine;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseKeyboardHandVisibilityController : MonoBehaviour
    {
        [SerializeField] BlockiverseNetworkAvatarRig avatarRig;

        public void Configure(BlockiverseNetworkAvatarRig rig)
        {
            avatarRig = rig;
            ApplyKeyboardVisibility(BlockiverseSystemKeyboardField.AnyKeyboardVisible);
        }

        void OnEnable()
        {
            ResolveAvatarRig();
            BlockiverseSystemKeyboardField.KeyboardVisibilityChanged += ApplyKeyboardVisibility;
            ApplyKeyboardVisibility(BlockiverseSystemKeyboardField.AnyKeyboardVisible);
        }

        void OnDisable()
        {
            BlockiverseSystemKeyboardField.KeyboardVisibilityChanged -= ApplyKeyboardVisibility;
            ApplyKeyboardVisibility(false);
        }

        void ApplyKeyboardVisibility(bool visible)
        {
            ResolveAvatarRig();
            avatarRig?.SetFirstPersonFallbackVisualsSuppressed(visible);
        }

        void ResolveAvatarRig()
        {
            if (avatarRig != null)
                return;

            avatarRig = GetComponent<BlockiverseNetworkAvatarRig>();
            if (avatarRig == null)
                avatarRig = GetComponentInParent<BlockiverseNetworkAvatarRig>(includeInactive: true);
            if (avatarRig == null)
                avatarRig = GetComponentInChildren<BlockiverseNetworkAvatarRig>(includeInactive: true);
        }
    }
}
