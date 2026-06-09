using System;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using Blockiverse.VR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blockiverse.UI
{
    // A single labelled action presented by a button-list menu (voxel_survival_menus §9).
    public readonly struct MenuAction
    {
        public MenuAction(string actionId, string label)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Menu action ids must be non-empty.", nameof(actionId));
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("Menu action labels must be non-empty.", nameof(label));

            ActionId = actionId;
            Label = label;
        }

        public string ActionId { get; }
        public string Label { get; }
    }

    // Reusable button-list menu used by the Title, Pause, Death, and Confirmation screens
    // (voxel_survival_menus §6.2/§6.7/§6.21/§6.22). Each visible button maps to a canonical action
    // id (§7); clicking it raises ActionInvoked. Surplus buttons are hidden so one prefab can host
    // menus of different lengths.
    public sealed class BlockiverseActionMenu : MonoBehaviour
    {
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Button[] actionButtons;
        [SerializeField] TMP_Text[] actionLabels;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        readonly List<string> actionIds = new();
        bool wired;

        public event Action<string> ActionInvoked;

        public IReadOnlyList<string> ActionIds => actionIds;

        public void Configure(TMP_Text title, Button[] buttons, TMP_Text[] labels, TMP_Text status = null)
        {
            titleLabel = title;
            actionButtons = buttons ?? Array.Empty<Button>();
            actionLabels = labels ?? Array.Empty<TMP_Text>();
            statusLabel = status;
            WireButtons();
        }

        public void ConfigureFeedback(BlockiverseAudioCuePlayer cuePlayer, BlockiverseInteractionHaptics haptics)
        {
            audioCuePlayer = cuePlayer;
            interactionHaptics = haptics;
        }

        // Populates the menu with a title and an ordered action list. Buttons beyond the action
        // count are deactivated.
        public void SetMenu(string title, IReadOnlyList<MenuAction> actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            WireButtons();

            if (titleLabel != null)
                titleLabel.text = title;

            actionIds.Clear();
            for (int i = 0; i < actionButtons.Length; i++)
            {
                bool hasAction = i < actions.Count;
                actionIds.Add(hasAction ? actions[i].ActionId : null);

                if (actionLabels != null && i < actionLabels.Length && actionLabels[i] != null)
                    actionLabels[i].text = hasAction ? actions[i].Label : string.Empty;

                if (actionButtons[i] != null)
                {
                    actionButtons[i].gameObject.SetActive(hasAction);
                    actionButtons[i].interactable = hasAction;
                }
            }
        }

        public void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message;
        }

        void Awake()
        {
            WireButtons();
        }

        void WireButtons()
        {
            if (wired || actionButtons == null)
                return;

            for (int i = 0; i < actionButtons.Length; i++)
            {
                if (actionButtons[i] == null)
                    continue;

                int index = i;
                actionButtons[i].onClick.RemoveAllListeners();
                actionButtons[i].onClick.AddListener(() => InvokeActionAt(index));
            }

            wired = true;
        }

        void InvokeActionAt(int index)
        {
            if (index < 0 || index >= actionIds.Count)
                return;

            string actionId = actionIds[index];
            if (string.IsNullOrEmpty(actionId))
                return;

            PlayFeedback(BlockiverseAudioCue.UiSelect);
            ActionInvoked?.Invoke(actionId);
        }

        void PlayFeedback(BlockiverseAudioCue cue)
        {
            if (Application.isPlaying)
            {
                if (audioCuePlayer == null)
                    audioCuePlayer = FindFirstObjectByType<BlockiverseAudioCuePlayer>();
                if (interactionHaptics == null)
                    interactionHaptics = FindFirstObjectByType<BlockiverseInteractionHaptics>();
            }

            audioCuePlayer?.PlayCue(cue);
            interactionHaptics?.PlayUiTick();
        }
    }
}
