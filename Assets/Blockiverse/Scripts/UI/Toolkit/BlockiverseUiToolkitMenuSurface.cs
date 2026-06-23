using System;
using System.Collections.Generic;
using Blockiverse.VR;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseUiToolkitMenuSurface : MonoBehaviour
    {
        const float ReadableTransformScale = 1.0f;
        static readonly Vector2 ReadableWorldSpaceSize = new(1.05f, 0.59f);

        [SerializeField] UIDocument document;
        [SerializeField] bool hideOnAwake = true;

        VisualElement root;
        Label kickerLabel;
        Label titleLabel;
        Label purposeLabel;
        VisualElement actionsRoot;
        VisualElement detailsRoot;
        Label statusLabel;
        readonly List<Button> buttons = new();

        public event Action<string> ActionInvoked;
        public event Action<string, string> TextInputChanged;
        public event Action<string, bool> CycleInvoked;
        public event Action<string> SelectionInvoked;
        public event Action<string, bool> ToggleChanged;
        public event Action<string, float> SliderChanged;
        public event Action<int> PageInvoked;

        public bool IsVisible => root != null && root.resolvedStyle.display != DisplayStyle.None;

        public void Configure(UIDocument targetDocument)
        {
            document = targetDocument;
            EnsureReadableWorldSpaceSizing();
            ResolveVisualTree();
            if (hideOnAwake)
                Hide();
        }

        void Awake()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            EnsureReadableWorldSpaceSizing();
            ResolveVisualTree();
            if (hideOnAwake)
                Hide();
        }

        public void Show(BlockiverseUiToolkitMenuView view, bool acceptsInput)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            ResolveVisualTree();
            if (root == null)
                return;

            root.style.display = DisplayStyle.Flex;
            root.SetEnabled(acceptsInput);

            if (kickerLabel != null)
                kickerLabel.text = view.Kicker;
            if (titleLabel != null)
                titleLabel.text = view.Title;
            if (purposeLabel != null)
                purposeLabel.text = view.Purpose;
            if (statusLabel != null)
                statusLabel.text = view.Status;

            PopulateActions(view.Actions, acceptsInput);
            PopulateDetails(view);
        }

        public void Hide()
        {
            ResolveVisualTree();
            if (root != null)
                root.style.display = DisplayStyle.None;
        }

        public void SetInputEnabled(bool acceptsInput)
        {
            ResolveVisualTree();
            root?.SetEnabled(acceptsInput);
        }

        void ResolveVisualTree()
        {
            if (document == null || document.rootVisualElement == null)
                return;

            root ??= document.rootVisualElement.Q<VisualElement>("blockiverse-menu-root");
            kickerLabel ??= document.rootVisualElement.Q<Label>("blockiverse-menu-kicker");
            titleLabel ??= document.rootVisualElement.Q<Label>("blockiverse-menu-title");
            purposeLabel ??= document.rootVisualElement.Q<Label>("blockiverse-menu-purpose");
            actionsRoot ??= document.rootVisualElement.Q<VisualElement>("blockiverse-menu-actions");
            detailsRoot ??= document.rootVisualElement.Q<VisualElement>("blockiverse-menu-details");
            statusLabel ??= document.rootVisualElement.Q<Label>("blockiverse-menu-status");
        }

        void EnsureReadableWorldSpaceSizing()
        {
            if (document == null)
                return;

            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = ReadableWorldSpaceSize;

            if (TryGetComponent(out BlockiverseWorldSpacePanelPresenter presenter))
                presenter.EnsurePanelScale(ReadableTransformScale);
        }

        void PopulateActions(IReadOnlyList<MenuAction> actions, bool acceptsInput)
        {
            if (actionsRoot == null)
                return;

            actionsRoot.Clear();
            buttons.Clear();

            if (actions == null || actions.Count == 0)
            {
                var empty = new Label("No direct actions.");
                empty.AddToClassList("bv-section-text");
                actionsRoot.Add(empty);
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                MenuAction action = actions[i];
                var button = new Button { text = action.Label };
                button.name = $"action-{i + 1}";
                button.AddToClassList("bv-action-button");
                if (i == 0)
                    button.AddToClassList("bv-action-button--primary");
                if (IsDangerAction(action.ActionId))
                    button.AddToClassList("bv-action-button--danger");
                else if (IsSecondaryAction(action.ActionId))
                    button.AddToClassList("bv-action-button--secondary");

                string actionId = action.ActionId;
                button.SetEnabled(acceptsInput);
                button.clicked += () => ActionInvoked?.Invoke(actionId);
                actionsRoot.Add(button);
                buttons.Add(button);
            }
        }

        void PopulateDetails(BlockiverseUiToolkitMenuView view)
        {
            if (detailsRoot == null)
                return;

            detailsRoot.Clear();

            var title = new Label("Screen Details");
            title.AddToClassList("bv-section-title");
            detailsRoot.Add(title);

            var purpose = new Label(view.Purpose);
            purpose.AddToClassList("bv-section-text");
            detailsRoot.Add(purpose);

            PopulateTextInputs(view.TextInputs);
            PopulateCycleRows(view.CycleRows);
            PopulateSelectionRows(view.SelectionRows);
            PopulateToggleRows(view.ToggleRows);
            PopulateSliderRows(view.SliderRows);
            PopulatePaging(view.Paging);

            if (view.Details != null)
            {
                foreach (MenuDetailRow detail in view.Details)
                {
                    var row = new VisualElement();
                    row.AddToClassList("bv-row");

                    var label = new Label(detail.Label);
                    label.AddToClassList("bv-row-label");
                    row.Add(label);

                    var value = new Label(detail.Value);
                    value.AddToClassList("bv-row-value");
                    row.Add(value);

                    detailsRoot.Add(row);
                }
            }

            if (view.Tags == null || view.Tags.Count == 0)
                return;

            var pillRow = new VisualElement();
            pillRow.AddToClassList("bv-pill-row");
            foreach (string tag in view.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                var pill = new Label(tag);
                pill.AddToClassList("bv-pill");
                pillRow.Add(pill);
            }
            detailsRoot.Add(pillRow);
        }

        void PopulateTextInputs(IReadOnlyList<MenuTextInputRow> inputs)
        {
            if (inputs == null || inputs.Count == 0)
                return;

            for (int i = 0; i < inputs.Count; i++)
            {
                MenuTextInputRow input = inputs[i];
                var row = new VisualElement();
                row.AddToClassList("bv-input-row");

                var label = new Label(input.Label);
                label.AddToClassList("bv-row-label");
                row.Add(label);

                var field = new TextField();
                field.name = input.FieldId;
                field.AddToClassList("bv-text-field");
                field.SetValueWithoutNotify(input.Value);
                string fieldId = input.FieldId;
                field.RegisterValueChangedCallback(evt => TextInputChanged?.Invoke(fieldId, evt.newValue));
                row.Add(field);

                detailsRoot.Add(row);
            }
        }

        void PopulateCycleRows(IReadOnlyList<MenuCycleRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                MenuCycleRow cycle = rows[i];
                var row = new VisualElement();
                row.AddToClassList("bv-cycle-row");

                var label = new Label(cycle.Label);
                label.AddToClassList("bv-row-label");
                row.Add(label);

                string fieldId = cycle.FieldId;
                var back = new Button { text = "<" };
                back.name = $"{fieldId}-previous";
                back.AddToClassList("bv-icon-button");
                back.clicked += () => CycleInvoked?.Invoke(fieldId, false);
                row.Add(back);

                var value = new Label(cycle.Value);
                value.AddToClassList("bv-cycle-value");
                row.Add(value);

                var next = new Button { text = ">" };
                next.name = $"{fieldId}-next";
                next.AddToClassList("bv-icon-button");
                next.clicked += () => CycleInvoked?.Invoke(fieldId, true);
                row.Add(next);

                detailsRoot.Add(row);
            }
        }

        void PopulateSelectionRows(IReadOnlyList<MenuSelectionRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                MenuSelectionRow selection = rows[i];
                string valueId = selection.ValueId;
                var button = new Button();
                button.name = $"selection-{i + 1}";
                button.AddToClassList("bv-selection-button");
                if (selection.Selected)
                    button.AddToClassList("bv-selection-button--selected");
                button.clicked += () => SelectionInvoked?.Invoke(valueId);

                var label = new Label(selection.Label);
                label.AddToClassList("bv-selection-label");
                button.Add(label);

                if (!string.IsNullOrWhiteSpace(selection.Description))
                {
                    var description = new Label(selection.Description);
                    description.AddToClassList("bv-selection-description");
                    button.Add(description);
                }

                detailsRoot.Add(button);
            }
        }

        void PopulatePaging(MenuPagingState? paging)
        {
            if (!paging.HasValue || !paging.Value.HasMultiplePages)
                return;

            MenuPagingState value = paging.Value;
            var row = new VisualElement();
            row.AddToClassList("bv-paging-row");

            var previous = new Button { text = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldPreviousPage) };
            previous.AddToClassList("bv-page-button");
            previous.SetEnabled(value.PageIndex > 0);
            previous.clicked += () => PageInvoked?.Invoke(-1);
            row.Add(previous);

            var label = new Label(value.DisplayText);
            label.AddToClassList("bv-page-label");
            row.Add(label);

            var next = new Button { text = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldNextPage) };
            next.AddToClassList("bv-page-button");
            next.SetEnabled(value.PageIndex < value.PageCount - 1);
            next.clicked += () => PageInvoked?.Invoke(1);
            row.Add(next);

            detailsRoot.Add(row);
        }

        void PopulateToggleRows(IReadOnlyList<MenuToggleRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                MenuToggleRow toggleRow = rows[i];
                var row = new VisualElement();
                row.AddToClassList("bv-toggle-row");

                var copy = new VisualElement();
                copy.AddToClassList("bv-toggle-copy");
                var label = new Label(toggleRow.Label);
                label.AddToClassList("bv-row-label");
                copy.Add(label);

                if (!string.IsNullOrWhiteSpace(toggleRow.Description))
                {
                    var description = new Label(toggleRow.Description);
                    description.AddToClassList("bv-selection-description");
                    copy.Add(description);
                }

                row.Add(copy);

                var toggle = new Toggle();
                toggle.name = toggleRow.FieldId;
                toggle.AddToClassList("bv-toggle");
                toggle.SetValueWithoutNotify(toggleRow.Value);
                string fieldId = toggleRow.FieldId;
                toggle.RegisterValueChangedCallback(evt => ToggleChanged?.Invoke(fieldId, evt.newValue));
                row.Add(toggle);

                detailsRoot.Add(row);
            }
        }

        void PopulateSliderRows(IReadOnlyList<MenuSliderRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                MenuSliderRow sliderRow = rows[i];
                var row = new VisualElement();
                row.AddToClassList("bv-slider-row");

                var header = new VisualElement();
                header.AddToClassList("bv-slider-header");

                var label = new Label(sliderRow.Label);
                label.AddToClassList("bv-row-label");
                header.Add(label);

                var valueLabel = new Label(sliderRow.ValueLabel);
                valueLabel.AddToClassList("bv-row-value");
                header.Add(valueLabel);
                row.Add(header);

                var slider = new Slider(sliderRow.MinValue, sliderRow.MaxValue);
                slider.name = sliderRow.FieldId;
                slider.AddToClassList("bv-slider");
                slider.SetValueWithoutNotify(sliderRow.Value);
                string fieldId = sliderRow.FieldId;
                slider.RegisterValueChangedCallback(evt =>
                {
                    valueLabel.text = evt.newValue.ToString("0.##");
                    SliderChanged?.Invoke(fieldId, evt.newValue);
                });
                row.Add(slider);

                detailsRoot.Add(row);
            }
        }

        static bool IsDangerAction(string actionId)
        {
            return string.Equals(actionId, MenuActions.TitleQuit, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.PauseQuit, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.PauseReturnToTitle, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.DeathReturnToTitle, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.WorldDetailsDeleteRequested, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.CreativeToolsDelete, StringComparison.Ordinal);
        }

        static bool IsSecondaryAction(string actionId)
        {
            return string.Equals(actionId, MenuActions.SettingsClose, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.ConfirmCancel, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.NewWorldCancel, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.LoadWorldCancel, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.WorldDetailsBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.ControllerMappingClose, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.InventoryBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.VitalsBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.CraftingBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.PlayerHubClose, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.ContextHubBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.StatusHubBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.ContainerBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.StationBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.MapBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.FarmingBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.ItemDetailsBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.RecipePinBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.BlockCatalogBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.CreativeToolsClose, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.AvatarStatusBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.MetaPolicyStatusBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.DiagnosticsBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.NetworkCommandStatusBack, StringComparison.Ordinal) ||
                   string.Equals(actionId, MenuActions.SurvivalRejectionDismiss, StringComparison.Ordinal);
        }
    }
}
