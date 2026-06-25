#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.Reflection;
using Blockiverse.Gameplay;
using Blockiverse.VR;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Blockiverse.UI
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseUiToolkitMenuSurface : MonoBehaviour
    {
        const float ReferencePanelWidthPixels = 1280.0f;
        const float ReferencePanelHeightPixels = 720.0f;
        const float ReferencePixelsPerUnit = 100.0f;
        const int RecenterFramesAfterShow = 90;
        const float MinimumReadableDistanceMeters = 0.55f;
        const float MaximumReadableDistanceMeters = 1.8f;
        const string FeedbackHoverClass = "bv-interactive--hovered";
        const string FeedbackPressedClass = "bv-interactive--pressed";
        public const float ReadableTransformScale = 0.1f;
        public static readonly Vector2 ReadableWorldSpaceSize = new(ReferencePanelWidthPixels, ReferencePanelHeightPixels);
        public static readonly Vector3 ReadableWorldSpaceColliderSize = new(
            ReferencePanelWidthPixels / ReferencePixelsPerUnit,
            ReferencePanelHeightPixels / ReferencePixelsPerUnit,
            0.08f);
        static readonly FieldInfo WorldSpaceColliderField = typeof(UIDocument)
            .GetField("m_WorldSpaceCollider", BindingFlags.NonPublic | BindingFlags.Instance);

        [SerializeField] UIDocument document;
        [SerializeField] bool hideOnAwake = true;
        [SerializeField] BlockiverseAudioCuePlayer audioCuePlayer;
        [SerializeField] BlockiverseInteractionHaptics interactionHaptics;

        VisualElement root;
        VisualElement screenRoot;
        Label kickerLabel;
        Label titleLabel;
        Label purposeLabel;
        VisualElement actionsRoot;
        VisualElement detailsRoot;
        Label statusLabel;
        VisualElement documentRoot;
        BlockiverseUiToolkitMenuView pendingView;
        bool pendingAcceptsInput;
        bool wantsVisible;
        bool warnedMissingWorldSpaceColliderField;
        int recenterFramesRemaining;
        readonly List<Button> buttons = new();
        readonly HashSet<VisualElement> fallbackInteractiveElements = new();
        readonly Dictionary<Button, Action> fallbackButtonActions = new();
        readonly List<Collider> fallbackColliderTargets = new();
        readonly List<RaycastHit> fallbackRaycastHits = new();
        XRInteractionManager fallbackInteractionManager;
        NearFarInteractor[] fallbackNearFarInteractors = Array.Empty<NearFarInteractor>();
        CurveInteractionCaster[] fallbackCasters = Array.Empty<CurveInteractionCaster>();
        VisualElement fallbackHoveredElement;
        VisualElement fallbackPressedElement;
        Vector2 fallbackPointerPanelPosition;
        bool fallbackWasPressed;
        float nextFallbackReferenceRefreshTime;

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
            if (wantsVisible)
                TryApplyPendingView();
            else if (hideOnAwake)
                Hide();
        }

        void Awake()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            EnsureReadableWorldSpaceSizing();
            ResolveVisualTree();
            if (wantsVisible)
                TryApplyPendingView();
            else if (hideOnAwake)
                Hide();
        }

        void OnEnable()
        {
            EnsureReadableWorldSpaceSizing();
            ResolveVisualTree();
            if (wantsVisible)
                TryApplyPendingView();
            else if (hideOnAwake)
                Hide();
        }

        void LateUpdate()
        {
            EnsureReadableWorldSpaceSizing();
            EnsureReadablePlacement();
            if (wantsVisible && pendingView != null)
                TryApplyPendingView();
            UpdateXRPointerFallback();
        }

        public void Show(BlockiverseUiToolkitMenuView view, bool acceptsInput)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            wantsVisible = true;
            pendingView = view;
            pendingAcceptsInput = acceptsInput;
            recenterFramesRemaining = RecenterFramesAfterShow;
            TryApplyPendingView();
        }

        public void Hide()
        {
            wantsVisible = false;
            pendingView = null;
            ResolveVisualTree();
            if (root == null)
                return;

            ClearXRPointerFallbackState();
            root.style.display = DisplayStyle.None;
        }

        public void SetInputEnabled(bool acceptsInput)
        {
            pendingAcceptsInput = acceptsInput;
            ResolveVisualTree();
            root?.SetEnabled(acceptsInput);
        }

        bool TryApplyPendingView()
        {
            ResolveVisualTree();
            if (pendingView == null || root == null || actionsRoot == null || detailsRoot == null)
                return false;

            ApplyView(pendingView, pendingAcceptsInput);
            pendingView = null;
            return true;
        }

        void ApplyView(BlockiverseUiToolkitMenuView view, bool acceptsInput)
        {
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

        void ResolveVisualTree()
        {
            if (document == null || document.rootVisualElement == null)
                return;

            if (!ReferenceEquals(documentRoot, document.rootVisualElement))
            {
                documentRoot = document.rootVisualElement;
                root = null;
                screenRoot = null;
                kickerLabel = null;
                titleLabel = null;
                purposeLabel = null;
                actionsRoot = null;
                detailsRoot = null;
                statusLabel = null;
                buttons.Clear();
                fallbackInteractiveElements.Clear();
                fallbackButtonActions.Clear();
                ClearXRPointerFallbackState();
            }

            root = documentRoot.Q<VisualElement>("blockiverse-menu-root");
            screenRoot = documentRoot.Q<VisualElement>("blockiverse-menu-screen");
            kickerLabel = documentRoot.Q<Label>("blockiverse-menu-kicker");
            titleLabel = documentRoot.Q<Label>("blockiverse-menu-title");
            purposeLabel = documentRoot.Q<Label>("blockiverse-menu-purpose");
            actionsRoot = documentRoot.Q<VisualElement>("blockiverse-menu-actions");
            detailsRoot = documentRoot.Q<VisualElement>("blockiverse-menu-details");
            statusLabel = documentRoot.Q<Label>("blockiverse-menu-status");
            ApplyFixedPanelLayout();
        }

        void ApplyFixedPanelLayout()
        {
            documentRoot.style.width = ReferencePanelWidthPixels;
            documentRoot.style.height = ReferencePanelHeightPixels;

            if (root != null)
            {
                root.style.width = ReferencePanelWidthPixels;
                root.style.height = ReferencePanelHeightPixels;
                root.style.minWidth = ReferencePanelWidthPixels;
                root.style.minHeight = ReferencePanelHeightPixels;
            }

            if (screenRoot != null)
            {
                screenRoot.style.width = ReferencePanelWidthPixels;
                screenRoot.style.height = ReferencePanelHeightPixels;
                screenRoot.style.minWidth = ReferencePanelWidthPixels;
                screenRoot.style.minHeight = ReferencePanelHeightPixels;
            }
        }

        void EnsureReadableWorldSpaceSizing()
        {
            if (document == null)
                return;

            SetFixedWorldSpaceSizeMode(document);
            document.worldSpaceSize = ReadableWorldSpaceSize;
            EnsureWorldSpaceCollider();

            if (TryGetComponent(out BlockiverseWorldSpacePanelPresenter presenter))
                presenter.EnsurePanelScale(ReadableTransformScale);
        }

        static void SetFixedWorldSpaceSizeMode(UIDocument targetDocument)
        {
#if UNITY_6000_5_OR_NEWER
            targetDocument.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
#else
            targetDocument.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
#endif
        }

        void EnsureReadablePlacement()
        {
            if (!wantsVisible || !IsVisible)
                return;

            if (!TryGetComponent(out BlockiverseWorldSpacePanelPresenter presenter))
                return;

            Transform head = Camera.main != null ? Camera.main.transform : null;
            bool shouldRecenter = recenterFramesRemaining > 0;
            if (recenterFramesRemaining > 0)
                recenterFramesRemaining--;

            if (head != null)
            {
                Vector3 toPanel = transform.position - head.position;
                float distance = toPanel.magnitude;
                float forwardDistance = Vector3.Dot(head.forward, toPanel);
                shouldRecenter |= distance < MinimumReadableDistanceMeters ||
                    distance > MaximumReadableDistanceMeters ||
                    forwardDistance < MinimumReadableDistanceMeters;
            }

            if (shouldRecenter)
                presenter.Recenter();
        }

        void EnsureWorldSpaceCollider()
        {
            if (!TryGetComponent(out BoxCollider worldSpaceCollider))
                worldSpaceCollider = gameObject.AddComponent<BoxCollider>();

            worldSpaceCollider.isTrigger = true;
            worldSpaceCollider.center = Vector3.zero;
            worldSpaceCollider.size = ReadableWorldSpaceColliderSize;
            AssignWorldSpaceCollider(worldSpaceCollider);
        }

        void AssignWorldSpaceCollider(Collider worldSpaceCollider)
        {
            if (document == null || worldSpaceCollider == null)
                return;

            if (WorldSpaceColliderField == null)
            {
                if (!warnedMissingWorldSpaceColliderField)
                {
                    Debug.LogWarning("UI Toolkit menu could not find UIDocument.m_WorldSpaceCollider; XR UI rays may not hit the runtime menu.", this);
                    warnedMissingWorldSpaceColliderField = true;
                }

                return;
            }

            if (!ReferenceEquals(WorldSpaceColliderField.GetValue(document), worldSpaceCollider))
                WorldSpaceColliderField.SetValue(document, worldSpaceCollider);
        }

        void PopulateActions(IReadOnlyList<MenuAction> actions, bool acceptsInput)
        {
            if (actionsRoot == null)
                return;

            actionsRoot.Clear();
            buttons.Clear();
            fallbackInteractiveElements.Clear();
            fallbackButtonActions.Clear();

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
                RegisterInteractiveFeedback(button);
                Action invokeAction = () => ActionInvoked?.Invoke(actionId);
                button.clicked += invokeAction;
                RegisterXRPointerFallbackAction(button, invokeAction);
                actionsRoot.Add(button);
                buttons.Add(button);
            }
        }

        void RegisterInteractiveFeedback(VisualElement element, bool playClickFeedbackOnPointerDown)
        {
            if (element == null)
                return;

            fallbackInteractiveElements.Add(element);
            element.RegisterCallback<PointerEnterEvent>(_ => ApplyInteractiveHover(element));
            element.RegisterCallback<PointerOverEvent>(_ => ApplyInteractiveHover(element));
            element.RegisterCallback<PointerLeaveEvent>(_ => ClearInteractiveHover(element));
            element.RegisterCallback<PointerOutEvent>(_ => ClearInteractiveHover(element));
            element.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (!CanPlayInteractiveFeedback(element))
                    return;

                element.AddToClassList(FeedbackPressedClass);
                if (playClickFeedbackOnPointerDown)
                    PlayUiClickFeedback();
            });
            element.RegisterCallback<PointerUpEvent>(_ =>
            {
                element.RemoveFromClassList(FeedbackPressedClass);
            });
        }

        void RegisterInteractiveFeedback(Button button)
        {
            if (button == null)
                return;

            RegisterInteractiveFeedback((VisualElement)button, playClickFeedbackOnPointerDown: false);
            button.clicked += PlayUiClickFeedback;
        }

        void ApplyInteractiveHover(VisualElement element)
        {
            if (!CanPlayInteractiveFeedback(element) || element.ClassListContains(FeedbackHoverClass))
                return;

            element.AddToClassList(FeedbackHoverClass);
            PlayUiHoverFeedback();
        }

        static void ClearInteractiveHover(VisualElement element)
        {
            if (element == null)
                return;

            element.RemoveFromClassList(FeedbackHoverClass);
            element.RemoveFromClassList(FeedbackPressedClass);
        }

        static bool CanPlayInteractiveFeedback(VisualElement element) =>
            element != null && element.enabledInHierarchy;

        void PlayUiHoverFeedback()
        {
            BlockiverseUiFeedback.Resolve(ref audioCuePlayer, ref interactionHaptics);
            interactionHaptics?.PlayUiTick();
        }

        void PlayUiClickFeedback()
        {
            BlockiverseUiFeedback.Resolve(ref audioCuePlayer, ref interactionHaptics);
            audioCuePlayer?.PlayCue(BlockiverseAudioCue.UiSelect);
            interactionHaptics?.PlayUiClick();
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
                RegisterInteractiveFeedback(field, playClickFeedbackOnPointerDown: true);
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
                RegisterInteractiveFeedback(back);
                Action invokePrevious = () => CycleInvoked?.Invoke(fieldId, false);
                back.clicked += invokePrevious;
                RegisterXRPointerFallbackAction(back, invokePrevious);
                row.Add(back);

                var value = new Label(cycle.Value);
                value.AddToClassList("bv-cycle-value");
                row.Add(value);

                var next = new Button { text = ">" };
                next.name = $"{fieldId}-next";
                next.AddToClassList("bv-icon-button");
                RegisterInteractiveFeedback(next);
                Action invokeNext = () => CycleInvoked?.Invoke(fieldId, true);
                next.clicked += invokeNext;
                RegisterXRPointerFallbackAction(next, invokeNext);
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
                RegisterInteractiveFeedback(button);
                Action invokeSelection = () => SelectionInvoked?.Invoke(valueId);
                button.clicked += invokeSelection;
                RegisterXRPointerFallbackAction(button, invokeSelection);

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
            RegisterInteractiveFeedback(previous);
            Action invokePrevious = () => PageInvoked?.Invoke(-1);
            previous.clicked += invokePrevious;
            RegisterXRPointerFallbackAction(previous, invokePrevious);
            row.Add(previous);

            var label = new Label(value.DisplayText);
            label.AddToClassList("bv-page-label");
            row.Add(label);

            var next = new Button { text = BlockiverseLocalization.Text(BlockiverseLocalization.Keys.LoadWorldNextPage) };
            next.AddToClassList("bv-page-button");
            next.SetEnabled(value.PageIndex < value.PageCount - 1);
            RegisterInteractiveFeedback(next);
            Action invokeNext = () => PageInvoked?.Invoke(1);
            next.clicked += invokeNext;
            RegisterXRPointerFallbackAction(next, invokeNext);
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
                RegisterInteractiveFeedback(toggle, playClickFeedbackOnPointerDown: true);
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
                RegisterInteractiveFeedback(slider, playClickFeedbackOnPointerDown: true);
                slider.RegisterValueChangedCallback(evt =>
                {
                    valueLabel.text = evt.newValue.ToString("0.##");
                    SliderChanged?.Invoke(fieldId, evt.newValue);
                });
                row.Add(slider);

                detailsRoot.Add(row);
            }
        }

        void RegisterXRPointerFallbackAction(Button button, Action action)
        {
            if (button == null || action == null)
                return;

            fallbackButtonActions[button] = action;
        }

        void UpdateXRPointerFallback()
        {
            if (!IsVisible || root == null || document == null)
            {
                ClearXRPointerFallbackState();
                return;
            }

            if (!TryGetXRPointerTarget(out VisualElement target, out Vector2 panelPosition, out bool pressed))
            {
                ClearXRPointerFallbackState();
                return;
            }

            fallbackPointerPanelPosition = panelPosition;
            if (!ReferenceEquals(target, fallbackHoveredElement))
            {
                ClearInteractiveHover(fallbackHoveredElement);
                fallbackHoveredElement = target;
                ApplyInteractiveHover(fallbackHoveredElement);
            }

            if (pressed && !fallbackWasPressed)
            {
                fallbackPressedElement = target;
                if (CanPlayInteractiveFeedback(fallbackPressedElement))
                {
                    fallbackPressedElement.AddToClassList(FeedbackPressedClass);
                    if (fallbackPressedElement is not Button)
                        PlayUiClickFeedback();
                }
            }

            if (pressed && fallbackPressedElement is Slider slider)
                ApplySliderPointerValue(slider, fallbackPointerPanelPosition);

            if (!pressed && fallbackWasPressed)
            {
                VisualElement pressedElement = fallbackPressedElement;
                ClearInteractiveHover(pressedElement);
                if (ReferenceEquals(pressedElement, target))
                    ActivateFallbackElement(pressedElement, fallbackPointerPanelPosition);

                fallbackPressedElement = null;
                if (fallbackHoveredElement != null)
                    ApplyInteractiveHover(fallbackHoveredElement);
            }

            fallbackWasPressed = pressed;
        }

        void ClearXRPointerFallbackState()
        {
            ClearInteractiveHover(fallbackPressedElement);
            if (!ReferenceEquals(fallbackPressedElement, fallbackHoveredElement))
                ClearInteractiveHover(fallbackHoveredElement);

            fallbackHoveredElement = null;
            fallbackPressedElement = null;
            fallbackWasPressed = false;
        }

        bool TryGetXRPointerTarget(out VisualElement target, out Vector2 panelPosition, out bool pressed)
        {
            target = null;
            panelPosition = default;
            pressed = false;

            RefreshXRPointerFallbackReferences();

            if (fallbackInteractionManager == null)
                return false;

            foreach (NearFarInteractor interactor in fallbackNearFarInteractors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled || !interactor.enableUIInteraction)
                    continue;

                if (interactor.farInteractionCaster is not CurveInteractionCaster caster)
                    continue;

                if (TryGetTargetFromCaster(caster, out target, out panelPosition))
                {
                    pressed = interactor.uiPressInput != null && interactor.uiPressInput.ReadIsPerformed();
                    return true;
                }
            }

            foreach (CurveInteractionCaster caster in fallbackCasters)
            {
                if (caster == null || !caster.isActiveAndEnabled)
                    continue;

                if (TryGetTargetFromCaster(caster, out target, out panelPosition))
                    return true;
            }

            return false;
        }

        void RefreshXRPointerFallbackReferences()
        {
            if (fallbackInteractionManager != null && Time.unscaledTime < nextFallbackReferenceRefreshTime)
                return;

            fallbackInteractionManager = FindAnyObjectByType<XRInteractionManager>();
            fallbackNearFarInteractors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            fallbackCasters = FindObjectsByType<CurveInteractionCaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextFallbackReferenceRefreshTime = Time.unscaledTime + 1.0f;
        }

        bool TryGetTargetFromCaster(CurveInteractionCaster caster, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;
            fallbackColliderTargets.Clear();
            fallbackRaycastHits.Clear();

            if (!caster.TryGetColliderTargets(fallbackInteractionManager, fallbackColliderTargets, fallbackRaycastHits))
                return false;

            for (int i = 0; i < fallbackRaycastHits.Count; i++)
            {
                RaycastHit hit = fallbackRaycastHits[i];
                if (hit.collider == null || hit.collider.gameObject != gameObject)
                    continue;

                Vector3 localPoint = transform.InverseTransformPoint(hit.point);
                panelPosition = LocalPointToPanelPosition(localPoint);
                VisualElement picked = document.rootVisualElement.panel?.Pick(panelPosition);
                target = FindFallbackInteractiveElement(picked);
                return target != null;
            }

            return false;
        }

        internal static Vector2 LocalPointToPanelPosition(Vector3 localPoint)
        {
            float normalizedX = Mathf.InverseLerp(
                -ReadableWorldSpaceColliderSize.x * 0.5f,
                ReadableWorldSpaceColliderSize.x * 0.5f,
                localPoint.x);
            float normalizedY = Mathf.InverseLerp(
                ReadableWorldSpaceColliderSize.y * 0.5f,
                -ReadableWorldSpaceColliderSize.y * 0.5f,
                localPoint.y);

            return new Vector2(
                Mathf.Clamp01(normalizedX) * ReferencePanelWidthPixels,
                Mathf.Clamp01(normalizedY) * ReferencePanelHeightPixels);
        }

        VisualElement FindFallbackInteractiveElement(VisualElement picked)
        {
            for (VisualElement current = picked; current != null; current = current.parent)
            {
                if (fallbackInteractiveElements.Contains(current) || IsFallbackInteractiveElement(current))
                    return current;
            }

            return null;
        }

        static bool IsFallbackInteractiveElement(VisualElement element) =>
            element is Button ||
            element is Toggle ||
            element is Slider ||
            element is TextField;

        void ActivateFallbackElement(VisualElement element, Vector2 panelPosition)
        {
            if (!CanPlayInteractiveFeedback(element))
                return;

            switch (element)
            {
                case Button button when fallbackButtonActions.TryGetValue(button, out Action action):
                    PlayUiClickFeedback();
                    action.Invoke();
                    break;
                case Toggle toggle:
                    toggle.value = !toggle.value;
                    break;
                case Slider slider:
                    ApplySliderPointerValue(slider, panelPosition);
                    break;
                case TextField textField:
                    textField.Focus();
                    break;
            }
        }

        static void ApplySliderPointerValue(Slider slider, Vector2 panelPosition)
        {
            if (slider == null)
                return;

            Rect bounds = slider.worldBound;
            if (bounds.width <= Mathf.Epsilon)
                return;

            float normalized = Mathf.Clamp01((panelPosition.x - bounds.xMin) / bounds.width);
            slider.value = Mathf.Lerp(slider.lowValue, slider.highValue, normalized);
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
