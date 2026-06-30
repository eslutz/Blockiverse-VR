using Blockiverse.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseUiSelectableFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        [SerializeField] bool hoverHaptic = true;
        [SerializeField] bool pressHaptic = true;
        [SerializeField] bool hoverAudio;
        [SerializeField] bool pressAudio;
        [SerializeField] BlockiverseAudioCue hoverCue = BlockiverseAudioCue.UiSelect;
        [SerializeField] BlockiverseAudioCue pressCue = BlockiverseAudioCue.UiConfirm;

        Selectable selectable;
        BlockiverseAudioCuePlayer audioCuePlayer;
        Blockiverse.Core.IBlockiverseInteractionHaptics interactionHaptics;

        public void Configure(
            bool playHoverHaptic = true,
            bool playPressHaptic = true,
            bool playHoverAudio = false,
            bool playPressAudio = false)
        {
            hoverHaptic = playHoverHaptic;
            pressHaptic = playPressHaptic;
            hoverAudio = playHoverAudio;
            pressAudio = playPressAudio;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!CanPlayFeedback())
                return;

            Resolve();

            if (hoverAudio)
                audioCuePlayer?.PlayCue(hoverCue);

            if (hoverHaptic)
                interactionHaptics?.PlayUiTick();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanPlayFeedback())
                return;

            Resolve();

            if (pressAudio)
                audioCuePlayer?.PlayCue(pressCue);

            if (pressHaptic)
                interactionHaptics?.PlayUiClick();
        }

        void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        bool CanPlayFeedback()
        {
            if (selectable == null)
                selectable = GetComponent<Selectable>();

            return selectable == null || selectable.IsInteractable();
        }

        void Resolve()
        {
            BlockiverseUiFeedback.Resolve(ref audioCuePlayer, ref interactionHaptics);
        }
    }
}
