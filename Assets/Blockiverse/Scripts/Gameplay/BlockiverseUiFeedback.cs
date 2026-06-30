using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public static class BlockiverseUiFeedback
    {
        public static void Play(
            ref BlockiverseAudioCuePlayer audioCuePlayer,
            ref IBlockiverseInteractionHaptics interactionHaptics,
            BlockiverseAudioCue cue,
            bool playAudio = true,
            bool playHaptic = true)
        {
            Resolve(ref audioCuePlayer, ref interactionHaptics);

            if (playAudio)
                audioCuePlayer?.PlayCue(cue);

            if (playHaptic)
                interactionHaptics?.PlayUiTick();
        }

        public static void PlayAudio(ref BlockiverseAudioCuePlayer audioCuePlayer, BlockiverseAudioCue cue)
        {
            if (audioCuePlayer == null && Application.isPlaying)
                audioCuePlayer = Object.FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            audioCuePlayer?.PlayCue(cue);
        }

        public static void Resolve(
            ref BlockiverseAudioCuePlayer audioCuePlayer,
            ref IBlockiverseInteractionHaptics interactionHaptics)
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = Object.FindFirstObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
            {
                var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour is IBlockiverseInteractionHaptics haptics)
                    {
                        interactionHaptics = haptics;
                        break;
                    }
                }
            }
        }
    }
}