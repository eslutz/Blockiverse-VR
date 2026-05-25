using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseHeightReset : MonoBehaviour
    {
        const float DefaultStandingEyeHeight = 1.6f;

        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public void ResetHeight()
        {
            if (origin == null)
                return;

            origin.CameraYOffset = settings != null
                ? settings.StandingEyeHeight
                : DefaultStandingEyeHeight;
        }
    }
}
