using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)]
    public sealed class BlockiverseTrackedPoseDriverLifecycle : MonoBehaviour
    {
        TrackedPoseDriver trackedPoseDriver;

        public static void Ensure(TrackedPoseDriver driver)
        {
            if (driver == null || !Application.isPlaying)
                return;

            var lifecycle = driver.GetComponent<BlockiverseTrackedPoseDriverLifecycle>();

            if (lifecycle == null)
                lifecycle = driver.gameObject.AddComponent<BlockiverseTrackedPoseDriverLifecycle>();

            lifecycle.trackedPoseDriver = driver;
        }

        void Awake()
        {
            if (trackedPoseDriver == null)
                trackedPoseDriver = GetComponent<TrackedPoseDriver>();
        }

        void OnDisable()
        {
            DisableTrackedPoseDriver();
        }

        void OnDestroy()
        {
            DisableTrackedPoseDriver();
        }

        void DisableTrackedPoseDriver()
        {
            if (trackedPoseDriver != null && trackedPoseDriver.enabled)
                trackedPoseDriver.enabled = false;
        }
    }
}
