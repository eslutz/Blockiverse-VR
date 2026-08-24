using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // UI Toolkit port of the World Details screen (voxel_survival_menus §6.5): the metadata
    // display and pending-rename holder from BlockiverseWorldDetailsPanel, plus the
    // Play/Rename/Duplicate/Delete/Back list the uGUI screen carried as a separate
    // BlockiverseActionMenu. The action list arrives via SetActionMenu pushes and is rebuilt
    // wholesale on every push (availability changes re-push it); the session controller reads
    // CurrentSave/PendingRenameText through the frontend when it performs the file operations.
    // Metadata text goes through WorldSaveMetadataText, which owns the §6.5 key set and the
    // culture-formatted dates.
    [UiToolkitScreen(
        MenuActions.WorldDetailsScreen,
        "Assets/Blockiverse/UI/Documents/WorldDetailsScreen.uxml",
        700,
        810,
        UiToolkitPlacementProfile.Menu)]
    public sealed class WorldDetailsScreenController : UiToolkitScreenController, IUiToolkitWorldDetailsScreen, IUiToolkitActionMenuScreen
    {
        public const string TitleLabelName = "bv-title";
        public const string WorldNameLabelName = "bv-world-name";
        public const string MetadataLabelName = "bv-metadata";
        public const string RenameFieldName = "bv-rename-field";
        public const string ActionListName = "bv-action-list";
        public const string ActionButtonNamePrefix = "bv-action-";

        Label titleLabel;
        Label worldNameLabel;
        Label metadataLabel;
        TextField renameField;
        VisualElement actionList;

        readonly List<Button> actionButtons = new();
        readonly List<string> actionIds = new();
        string pendingTitle;
        IReadOnlyList<MenuAction> pendingActions;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        public override string ScreenId => MenuActions.WorldDetailsScreen;

        public WorldSaveSummary? CurrentSave { get; private set; }

        // The rename input's current text (the new world name applied by world_details.rename).
        public string PendingRenameText => renameField != null ? renameField.value : string.Empty;

        public IReadOnlyList<string> ActionIds => actionIds;

        public void ShowSave(WorldSaveSummary save)
        {
            CurrentSave = save;
            renameField?.SetValueWithoutNotify(save.Name);
            RenderSaveDetails();
        }

        public void Clear()
        {
            CurrentSave = null;
            renameField?.SetValueWithoutNotify(string.Empty);
            RenderSaveDetails();
        }

        public void SetActionMenu(string title, IReadOnlyList<MenuAction> actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            pendingTitle = title;
            pendingActions = actions;
            RenderActionMenu();
        }

        // Mirrors BlockiverseActionMenu.InvokeActionAt: same guards, same UiSelect cue before
        // the action routes. Public seam — EditMode tests cannot raise ClickEvent without a
        // live panel.
        public void SimulateActionClicked(int index) => InvokeActionAt(index);

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;

            titleLabel = Require<Label>(root, TitleLabelName, ref allFound);
            worldNameLabel = Require<Label>(root, WorldNameLabelName, ref allFound);
            metadataLabel = Require<Label>(root, MetadataLabelName, ref allFound);
            renameField = Require<TextField>(root, RenameFieldName, ref allFound);
            actionList = Require<VisualElement>(root, ActionListName, ref allFound);

            // The tree is brand new on every attach; render the retained state into it.
            if (CurrentSave.HasValue)
                renameField?.SetValueWithoutNotify(CurrentSave.Value.Name);

            RenderSaveDetails();
            RenderActionMenu();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            // Action buttons register at render time (RenderActionMenu), because every
            // SetActionMenu push replaces them outside the attach cycle.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            UnregisterActionButtons();

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            actionButtons.Clear();
            titleLabel = null;
            worldNameLabel = null;
            metadataLabel = null;
            renameField = null;
            actionList = null;
        }

        // Metadata and the action labels are dynamic strings; the static title/caption
        // bindings update natively on locale change.
        void OnSelectedLocaleChanged(Locale locale)
        {
            RenderSaveDetails();
            RenderActionMenu();
        }

        void RenderSaveDetails()
        {
            if (worldNameLabel != null)
                worldNameLabel.text = CurrentSave.HasValue ? CurrentSave.Value.Name : string.Empty;

            if (metadataLabel != null)
                metadataLabel.text = CurrentSave.HasValue
                    ? WorldSaveMetadataText.Build(CurrentSave.Value)
                    : string.Empty;
        }

        void RenderActionMenu()
        {
            actionIds.Clear();

            if (pendingActions != null)
            {
                foreach (MenuAction action in pendingActions)
                    actionIds.Add(action.ActionId);
            }

            if (titleLabel != null && pendingTitle != null)
                titleLabel.text = pendingTitle;

            if (actionList == null)
                return;

            UnregisterActionButtons();
            actionList.Clear();
            actionButtons.Clear();

            if (pendingActions == null)
                return;

            for (int i = 0; i < pendingActions.Count; i++)
            {
                Button button = new Button
                {
                    name = ActionButtonNamePrefix + (i + 1),
                    text = pendingActions[i].Label,
                };
                button.AddToClassList("hs-button");
                button.RegisterCallback<ClickEvent, int>(OnActionClicked, i);
                actionList.Add(button);
                actionButtons.Add(button);
            }
        }

        void UnregisterActionButtons()
        {
            foreach (Button button in actionButtons)
                button.UnregisterCallback<ClickEvent, int>(OnActionClicked);
        }

        void OnActionClicked(ClickEvent evt, int index) => InvokeActionAt(index);

        void InvokeActionAt(int index)
        {
            if (index < 0 || index >= actionIds.Count)
                return;

            string actionId = actionIds[index];

            if (string.IsNullOrEmpty(actionId))
                return;

            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            DispatchAction(actionId);
        }
    }
}
