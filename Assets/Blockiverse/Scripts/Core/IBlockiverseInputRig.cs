using UnityEngine;
using UnityEngine.Events;

namespace Blockiverse.Core
{
    public interface IBlockiverseInputRig
    {
        bool LocomotionSuppressed { get; set; }
        UnityEvent MenuPressed { get; }
        UnityEvent QuickMenuPressed { get; }
        UnityEvent BreakPressed { get; }
        bool TryGetActiveInteractionRayPose(out Vector3 rayOrigin, out Vector3 rayDirection);
        bool TryGetInteractionRayPose(BlockiverseControllerRole hand, out Vector3 rayOrigin, out Vector3 rayDirection);
    }
}
