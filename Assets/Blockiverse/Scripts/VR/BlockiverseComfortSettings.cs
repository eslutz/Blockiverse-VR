using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseComfortSettings : MonoBehaviour
    {
        const float MinSnapTurnDegrees = 15.0f;
        const float MaxSnapTurnDegrees = 90.0f;
        const float MinStandingEyeHeight = 1.0f;
        const float MaxStandingEyeHeight = 2.2f;
        const float MinContinuousMoveSpeed = 0.5f;
        const float MaxContinuousMoveSpeed = 4.0f;

        [SerializeField] bool teleportEnabled = true;
        [SerializeField] bool continuousMoveEnabled = true;
        [SerializeField] float continuousMoveSpeed = 1.8f;
        [SerializeField] bool smoothTurnEnabled;
        [SerializeField] float snapTurnDegrees = 45.0f;
        [SerializeField] float standingEyeHeight = 1.6f;

        public bool TeleportEnabled
        {
            get => teleportEnabled;
            set => teleportEnabled = value;
        }

        public bool ContinuousMoveEnabled
        {
            get => continuousMoveEnabled;
            set => continuousMoveEnabled = value;
        }

        public float ContinuousMoveSpeed
        {
            get => continuousMoveSpeed;
            set => continuousMoveSpeed = Mathf.Clamp(value, MinContinuousMoveSpeed, MaxContinuousMoveSpeed);
        }

        public bool SmoothTurnEnabled
        {
            get => smoothTurnEnabled;
            set => smoothTurnEnabled = value;
        }

        public float SnapTurnDegrees
        {
            get => snapTurnDegrees;
            set => snapTurnDegrees = Mathf.Clamp(value, MinSnapTurnDegrees, MaxSnapTurnDegrees);
        }

        public float StandingEyeHeight
        {
            get => standingEyeHeight;
            set => standingEyeHeight = Mathf.Clamp(value, MinStandingEyeHeight, MaxStandingEyeHeight);
        }

        void OnValidate()
        {
            ContinuousMoveSpeed = continuousMoveSpeed;
            SnapTurnDegrees = snapTurnDegrees;
            StandingEyeHeight = standingEyeHeight;
        }
    }
}
