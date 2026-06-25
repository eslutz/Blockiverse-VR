using Blockiverse.Gameplay;
using UnityEngine;

namespace Blockiverse.VR
{
    public static class BlockiverseUiFeedback
    {
        public static void Play(
            ref BlockiverseAudioCuePlayer audioCuePlayer,
            ref BlockiverseInteractionHaptics interactionHaptics,
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
                audioCuePlayer = Object.FindAnyObjectByType<BlockiverseAudioCuePlayer>();

            audioCuePlayer?.PlayCue(cue);
        }

        public static void Resolve(
            ref BlockiverseAudioCuePlayer audioCuePlayer,
            ref BlockiverseInteractionHaptics interactionHaptics)
        {
            if (!Application.isPlaying)
                return;

            if (audioCuePlayer == null)
                audioCuePlayer = Object.FindAnyObjectByType<BlockiverseAudioCuePlayer>();

            if (interactionHaptics == null)
                interactionHaptics = Object.FindAnyObjectByType<BlockiverseInteractionHaptics>();
        }
    }
}
