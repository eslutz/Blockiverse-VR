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
            : this(actionId, null, label)
        {
        }

        public MenuAction(string actionId, string labelKey, string fallbackLabel)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("Menu action ids must be non-empty.", nameof(actionId));
            if (string.IsNullOrWhiteSpace(labelKey) && string.IsNullOrWhiteSpace(fallbackLabel))
                throw new ArgumentException("Menu action labels must be non-empty.", nameof(fallbackLabel));

            ActionId = actionId;
            LabelKey = labelKey;
            this.fallbackLabel = fallbackLabel;
        }

        readonly string fallbackLabel;

        public string ActionId { get; }
        public string LabelKey { get; }
        public string Label => BlockiverseLocalization.Text(LabelKey, fallbackLabel);
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
            wired = false;
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

            ResolveRuntimeReferences();

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
            ResolveRuntimeReferences();
        }

        public void ResolveRuntimeReferences()
        {
            bool changed = false;

            if (titleLabel == null)
            {
                titleLabel = FindChildComponent<TMP_Text>("Panel/Title") ?? FindChildComponent<TMP_Text>("Title");
                changed |= titleLabel != null;
            }

            if (statusLabel == null)
            {
                statusLabel = FindChildComponent<TMP_Text>("Panel/Status") ?? FindChildComponent<TMP_Text>("Status");
                changed |= statusLabel != null;
            }

            if (NeedsRefresh(actionButtons))
            {
                actionButtons = FindActionButtons();
                changed |= actionButtons.Length > 0;
            }

            if (actionButtons != null && actionButtons.Length > 0 && NeedsRefresh(actionLabels, actionButtons.Length))
            {
                actionLabels = FindActionLabels(actionButtons);
                changed |= actionLabels.Length > 0;
            }

            if (changed)
                wired = false;

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
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, cue);
        }

        T FindChildComponent<T>(string path) where T : Component
        {
            Transform child = transform.Find(path);
            return child != null ? child.GetComponent<T>() : null;
        }

        Button[] FindActionButtons()
        {
            var indexedButtons = new List<(int index, Button button)>();
            foreach (Button button in GetComponentsInChildren<Button>(true))
            {
                if (button == null || !TryGetIndexedName(button.gameObject.name, "Action ", out int index))
                    continue;

                indexedButtons.Add((index, button));
            }

            indexedButtons.Sort((a, b) => a.index.CompareTo(b.index));

            var buttons = new Button[indexedButtons.Count];
            for (int i = 0; i < indexedButtons.Count; i++)
                buttons[i] = indexedButtons[i].button;
            return buttons;
        }

        TMP_Text[] FindActionLabels(Button[] buttons)
        {
            var labels = new TMP_Text[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                Transform label = buttons[i] != null ? buttons[i].transform.Find("Label") : null;
                labels[i] = label != null
                    ? label.GetComponent<TMP_Text>()
                    : buttons[i] != null ? buttons[i].GetComponentInChildren<TMP_Text>(true) : null;
            }

            return labels;
        }

        static bool NeedsRefresh<T>(T[] values) where T : class
        {
            return NeedsRefresh(values, values != null ? values.Length : 0);
        }

        static bool NeedsRefresh<T>(T[] values, int expectedLength) where T : class
        {
            if (values == null || values.Length != expectedLength || values.Length == 0)
                return true;

            for (int i = 0; i < values.Length; i++)
                if (values[i] == null)
                    return true;
            return false;
        }

        static bool TryGetIndexedName(string value, string prefix, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            return int.TryParse(value.Substring(prefix.Length), out index) && index > 0;
        }
    }
}
