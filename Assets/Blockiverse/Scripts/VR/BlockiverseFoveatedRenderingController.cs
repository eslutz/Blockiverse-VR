using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseFoveatedRenderingController : MonoBehaviour
    {
        public const float DefaultFoveatedRenderingLevel = 0.66f;
        const float RetryIntervalSeconds = 0.5f;

        [SerializeField, Range(0.0f, 1.0f)] float foveatedRenderingLevel = DefaultFoveatedRenderingLevel;
        [SerializeField] bool allowGazeTrackedFoveation;

        readonly List<XRDisplaySubsystem> displaySubsystems = new();
        float nextApplyTime;
        bool appliedToRunningDisplay;

        public float FoveatedRenderingLevel => foveatedRenderingLevel;
        public bool AllowGazeTrackedFoveation => allowGazeTrackedFoveation;
        public bool AppliedToRunningDisplay => appliedToRunningDisplay;

        public void Configure(float level, bool allowGazeTracking = false)
        {
            foveatedRenderingLevel = Mathf.Clamp01(level);
            allowGazeTrackedFoveation = allowGazeTracking;
            appliedToRunningDisplay = false;
            TryApply();
        }

        void OnEnable()
        {
            appliedToRunningDisplay = false;
            nextApplyTime = 0.0f;
            TryApply();
        }

        void Update()
        {
            if (appliedToRunningDisplay || Time.unscaledTime < nextApplyTime)
                return;

            TryApply();
        }

        public bool TryApply()
        {
            displaySubsystems.Clear();
            SubsystemManager.GetSubsystems(displaySubsystems);

            bool applied = false;
            foreach (XRDisplaySubsystem displaySubsystem in displaySubsystems)
            {
                if (displaySubsystem == null || !displaySubsystem.running)
                    continue;

                displaySubsystem.foveatedRenderingLevel = foveatedRenderingLevel;
                displaySubsystem.foveatedRenderingFlags = allowGazeTrackedFoveation
                    ? XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed
                    : 0;
                applied = true;
            }

            appliedToRunningDisplay = applied;
            if (!appliedToRunningDisplay)
                nextApplyTime = Time.unscaledTime + RetryIntervalSeconds;

            return appliedToRunningDisplay;
        }
    }
}
