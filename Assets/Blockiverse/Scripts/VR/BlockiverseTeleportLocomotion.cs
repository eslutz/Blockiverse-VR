using Blockiverse.Gameplay;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.VR
{
    public sealed class BlockiverseTeleportLocomotion : MonoBehaviour
    {
        [SerializeField] XROrigin origin;
        [SerializeField] BlockiverseComfortSettings settings;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;

        public void Configure(XROrigin xrOrigin, BlockiverseComfortSettings comfortSettings)
        {
            origin = xrOrigin;
            settings = comfortSettings;
        }

        public void ConfigureFeedback(BlockiverseAudioCuePlayer cuePlayer)
        {
            audioCuePlayer = cuePlayer;
        }

        public bool TryTeleportTo(Vector3 worldPosition)
        {
            if (origin == null)
                return false;

            if (settings != null && !settings.TeleportEnabled)
                return false;

            bool moved = origin.MoveCameraToWorldLocation(worldPosition);
            if (moved)
                PlayFootstepCue();

            return moved;
        }

        void PlayFootstepCue()
        {
            if (audioCuePlayer == null && Application.isPlaying)
                audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            audioCuePlayer?.PlayCue(BlockiverseAudioCue.Footstep);
        }
    }
}
