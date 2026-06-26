#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Blockiverse.Core;
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
        const float PointerDiagnosticIntervalSeconds = 1.0f;
        const int MenuInteractionSmokeDelayFrames = 20;
        const string FeedbackHoverClass = "bv-interactive--hovered";
        const string FeedbackPressedClass = "bv-interactive--pressed";
        const string MenuInteractionSmokeMarkerFileName = "run-menu-interaction-smoke";
        const string MenuInteractionSmokePlayerPrefsKey = "Blockiverse.Diagnostics.RunMenuInteractionSmoke";
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
        readonly List<Button> buttons = new();
        readonly HashSet<VisualElement> xriInteractiveElements = new();
        readonly Dictionary<Button, Action> xriButtonActions = new();
        readonly List<Collider> xriColliderTargets = new();
        readonly List<RaycastHit> xriRaycastHits = new();
        Vector3[] xriRayLinePoints = Array.Empty<Vector3>();
        XRInteractionManager xriInteractionManager;
        BlockiverseInputRig xriInputRig;
        NearFarInteractor[] xriNearFarInteractors = Array.Empty<NearFarInteractor>();
        XRRayInteractor[] xriRayInteractors = Array.Empty<XRRayInteractor>();
        VisualElement xriHoveredElement;
        VisualElement xriPressedElement;
        Vector2 xriPointerPanelPosition;
        bool xriWasPressed;
        bool menuInteractionSmokeRequested;
        bool menuInteractionSmokeCompleted;
        int menuInteractionSmokeFramesRemaining = -1;
        float nextXriReferenceRefreshTime;
        float nextPointerDiagnosticTime;

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
            if (wantsVisible && pendingView != null)
                TryApplyPendingView();
            UpdateXriPointerBridge();
            UpdateMenuInteractionSmoke();
        }

        public void Show(BlockiverseUiToolkitMenuView view, bool acceptsInput)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            wantsVisible = true;
            pendingView = view;
            pendingAcceptsInput = acceptsInput;
            TryApplyPendingView();
        }

        public void Hide()
        {
            wantsVisible = false;
            pendingView = null;
            ResolveVisualTree();
            if (root == null)
                return;

            ClearXriPointerBridgeState();
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
                xriInteractiveElements.Clear();
                xriButtonActions.Clear();
                ClearXriPointerBridgeState();
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

            if (TryGetComponent(out BlockiverseUiToolkitMenuPresenter presenter))
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

        void EnsureWorldSpaceCollider()
        {
            if (!TryGetComponent(out BoxCollider worldSpaceCollider))
                worldSpaceCollider = gameObject.AddComponent<BoxCollider>();

            int vrUiLayer = LayerMask.NameToLayer(BlockiverseProject.VrUiLayerName);
            gameObject.layer = vrUiLayer >= 0
                ? vrUiLayer
                : BlockiverseProject.VrUiLayerIndex;
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
            xriInteractiveElements.Clear();
            xriButtonActions.Clear();

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
                RegisterXriPointerAction(button, invokeAction);
                actionsRoot.Add(button);
                buttons.Add(button);
            }
        }

        void RegisterInteractiveFeedback(VisualElement element, bool playClickFeedbackOnPointerDown)
        {
            if (element == null)
                return;

            xriInteractiveElements.Add(element);
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
                RegisterXriPointerAction(back, invokePrevious);
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
                RegisterXriPointerAction(next, invokeNext);
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
                RegisterXriPointerAction(button, invokeSelection);

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
            RegisterXriPointerAction(previous, invokePrevious);
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
            RegisterXriPointerAction(next, invokeNext);
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

        void RegisterXriPointerAction(Button button, Action action)
        {
            if (button == null || action == null)
                return;

            xriButtonActions[button] = action;
        }

        void UpdateXriPointerBridge()
        {
            if (!IsVisible || root == null || document == null)
            {
                ClearXriPointerBridgeState();
                return;
            }

            if (!TryGetXRPointerTarget(out VisualElement target, out Vector2 panelPosition, out bool pressed))
            {
                ClearXriPointerBridgeState();
                return;
            }

            xriPointerPanelPosition = panelPosition;
            if (!ReferenceEquals(target, xriHoveredElement))
            {
                ClearInteractiveHover(xriHoveredElement);
                xriHoveredElement = target;
                ApplyInteractiveHover(xriHoveredElement);
            }

            if (pressed && !xriWasPressed)
            {
                xriPressedElement = target;
                if (CanPlayInteractiveFeedback(xriPressedElement))
                {
                    xriPressedElement.AddToClassList(FeedbackPressedClass);
                    if (xriPressedElement is not Button)
                        PlayUiClickFeedback();
                }
            }

            if (pressed && xriPressedElement is Slider slider)
                ApplySliderPointerValue(slider, xriPointerPanelPosition);

            if (!pressed && xriWasPressed)
            {
                VisualElement pressedElement = xriPressedElement;
                ClearInteractiveHover(pressedElement);
                if (ReferenceEquals(pressedElement, target))
                    ActivateXriElement(pressedElement, xriPointerPanelPosition);

                xriPressedElement = null;
                if (xriHoveredElement != null)
                    ApplyInteractiveHover(xriHoveredElement);
            }

            xriWasPressed = pressed;
        }

        void ClearXriPointerBridgeState()
        {
            ClearInteractiveHover(xriPressedElement);
            if (!ReferenceEquals(xriPressedElement, xriHoveredElement))
                ClearInteractiveHover(xriHoveredElement);

            xriHoveredElement = null;
            xriPressedElement = null;
            xriWasPressed = false;
        }

        bool TryGetXRPointerTarget(out VisualElement target, out Vector2 panelPosition, out bool pressed)
        {
            target = null;
            panelPosition = default;
            pressed = false;

            RefreshXriPointerBridgeReferences();

            if (xriInteractionManager == null)
            {
                LogPointerDiagnostic("no-interaction-manager", "XRInteractionManager not found.");
                return false;
            }

            VisualElement bestTarget = null;
            Vector2 bestPanelPosition = default;
            bool bestPressed = false;
            string bestDiagnosticName = null;
            string bestDiagnosticDetails = null;

            if (TryGetTargetFromInputRigRays(
                    out VisualElement inputRigTarget,
                    out Vector2 inputRigPanelPosition,
                    out bool inputRigPressed,
                    out string inputRigSource))
            {
                bestTarget = inputRigTarget;
                bestPanelPosition = inputRigPanelPosition;
                bestPressed = inputRigPressed;
                bestDiagnosticName = "input-rig-hit";
                bestDiagnosticDetails = $"target={inputRigTarget.name} pressed={inputRigPressed} source={inputRigSource}";
            }

            if (!bestPressed)
            {
                foreach (XRRayInteractor rayInteractor in xriRayInteractors)
                {
                    if (!ShouldConsiderXriRayInteractor(rayInteractor))
                        continue;

                    if (TryGetTargetFromRayInteractor(rayInteractor, out VisualElement candidateTarget, out Vector2 candidatePanelPosition, out string raySource))
                    {
                        bool candidatePressed = rayInteractor.uiPressInput != null && rayInteractor.uiPressInput.ReadIsPerformed();
                        if (ShouldPreferPointerCandidate(bestTarget != null, bestPressed, candidatePressed))
                        {
                            bestTarget = candidateTarget;
                            bestPanelPosition = candidatePanelPosition;
                            bestPressed = candidatePressed;
                            bestDiagnosticName = "xrray-hit";
                            bestDiagnosticDetails = $"target={candidateTarget.name} pressed={candidatePressed} source={raySource}";
                        }

                        if (candidatePressed)
                            break;
                    }
                }
            }

            if (!bestPressed)
            {
                foreach (NearFarInteractor interactor in xriNearFarInteractors)
                {
                    if (!ShouldConsiderNearFarInteractor(interactor))
                        continue;

                    if (interactor.farInteractionCaster is not CurveInteractionCaster caster)
                        continue;

                    if (TryGetTargetFromCaster(caster, out VisualElement candidateTarget, out Vector2 candidatePanelPosition))
                    {
                        bool candidatePressed = interactor.uiPressInput != null && interactor.uiPressInput.ReadIsPerformed();
                        if (ShouldPreferPointerCandidate(bestTarget != null, bestPressed, candidatePressed))
                        {
                            bestTarget = candidateTarget;
                            bestPanelPosition = candidatePanelPosition;
                            bestPressed = candidatePressed;
                            bestDiagnosticName = "near-far-hit";
                            bestDiagnosticDetails = $"target={candidateTarget.name} pressed={candidatePressed}";
                        }

                        if (candidatePressed)
                            break;
                    }
                }
            }

            if (bestTarget != null)
            {
                target = bestTarget;
                panelPosition = bestPanelPosition;
                pressed = bestPressed;
                LogPointerDiagnostic(bestDiagnosticName, bestDiagnosticDetails);
                return true;
            }

            LogPointerDiagnostic(
                "no-target",
                $"nearFar={xriNearFarInteractors.Length} xrRays={xriRayInteractors.Length} layer={gameObject.layer} {DescribePointerCandidates()}");
            return false;
        }

        void RefreshXriPointerBridgeReferences()
        {
            if (xriInteractionManager != null && Time.unscaledTime < nextXriReferenceRefreshTime)
                return;

            xriInteractionManager = FindAnyObjectByType<XRInteractionManager>();
            xriInputRig = FindAnyObjectByType<BlockiverseInputRig>();
            xriNearFarInteractors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            xriRayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextXriReferenceRefreshTime = Time.unscaledTime + 1.0f;
        }

        internal static bool ShouldConsiderNearFarInteractor(NearFarInteractor interactor) =>
            interactor != null && interactor.isActiveAndEnabled && interactor.enableUIInteraction;

        internal static bool ShouldConsiderXriRayInteractor(XRRayInteractor rayInteractor) =>
            rayInteractor != null && rayInteractor.isActiveAndEnabled && rayInteractor.enableUIInteraction;

        internal static bool ShouldConsiderInputRigRayInteractor(XRRayInteractor rayInteractor) =>
            rayInteractor != null &&
            rayInteractor.enableUIInteraction &&
            BlockiverseRuntimeState.MenuInputActive;

        internal static bool ShouldPreferPointerCandidate(bool hasCurrentCandidate, bool currentPressed, bool candidatePressed) =>
            !hasCurrentCandidate || (!currentPressed && candidatePressed);

        bool TryGetTargetFromInputRigRays(
            out VisualElement target,
            out Vector2 panelPosition,
            out bool pressed,
            out string source)
        {
            target = null;
            panelPosition = default;
            pressed = false;
            source = "none";

            if (xriInputRig == null)
                return false;

            bool hasTarget = false;

            if (TryGetTargetFromInputRigRay(
                    xriInputRig.RightInteractionRay,
                    BlockiverseControllerRole.Right,
                    out VisualElement rightTarget,
                    out Vector2 rightPanelPosition,
                    out bool rightPressed,
                    out string rightSource))
            {
                target = rightTarget;
                panelPosition = rightPanelPosition;
                pressed = rightPressed;
                source = rightSource;
                hasTarget = true;
            }

            if (TryGetTargetFromInputRigRay(
                    xriInputRig.LeftInteractionRay,
                    BlockiverseControllerRole.Left,
                    out VisualElement leftTarget,
                    out Vector2 leftPanelPosition,
                    out bool leftPressed,
                    out string leftSource) &&
                ShouldPreferPointerCandidate(hasTarget, pressed, leftPressed))
            {
                target = leftTarget;
                panelPosition = leftPanelPosition;
                pressed = leftPressed;
                source = leftSource;
                hasTarget = true;
            }

            return hasTarget;
        }

        bool TryGetTargetFromInputRigRay(
            XRRayInteractor rayInteractor,
            BlockiverseControllerRole hand,
            out VisualElement target,
            out Vector2 panelPosition,
            out bool pressed,
            out string source)
        {
            target = null;
            panelPosition = default;
            pressed = false;
            source = hand.ToString();

            if (!ShouldConsiderInputRigRayInteractor(rayInteractor))
                return false;

            if (!xriInputRig.TryGetInteractionRayPose(hand, out Vector3 origin, out Vector3 direction))
                return false;

            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return false;

            float distance = rayInteractor.maxRaycastDistance > Mathf.Epsilon
                ? rayInteractor.maxRaycastDistance
                : CreativeInteractionController.MaxBlockInteractionReachMeters;
            if (!TryGetTargetFromWorldRay(new Ray(origin, direction.normalized), distance, out target, out panelPosition))
                return false;

            pressed = rayInteractor.uiPressInput != null && rayInteractor.uiPressInput.ReadIsPerformed();
            source = $"{hand.ToString().ToLowerInvariant()}-pose";
            return true;
        }

        bool TryGetTargetFromCaster(CurveInteractionCaster caster, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;
            xriColliderTargets.Clear();
            xriRaycastHits.Clear();

            bool hasCasterHits = caster.TryGetColliderTargets(xriInteractionManager, xriColliderTargets, xriRaycastHits);

            if (hasCasterHits && TryGetTargetFromRaycastHits(xriRaycastHits, out target, out panelPosition))
                return true;

            return TryGetTargetFromCasterSampleRay(caster, out target, out panelPosition);
        }

        string DescribePointerCandidates()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var builder = new StringBuilder();
            builder.Append("candidates=");

            int described = 0;
            foreach (NearFarInteractor interactor in xriNearFarInteractors)
            {
                if (!ShouldConsiderNearFarInteractor(interactor))
                    continue;

                builder.Append(described++ == 0 ? string.Empty : " | ");
                builder.Append("nearFar:");
                builder.Append(interactor.name);
                builder.Append(' ');

                if (interactor.farInteractionCaster is CurveInteractionCaster caster)
                    AppendCasterDiagnostic(builder, caster);
                else
                    builder.Append("caster=none");
            }

            foreach (XRRayInteractor rayInteractor in xriRayInteractors)
            {
                if (!ShouldConsiderXriRayInteractor(rayInteractor))
                    continue;

                builder.Append(described++ == 0 ? string.Empty : " | ");
                builder.Append("xrRay:");
                builder.Append(rayInteractor.name);
                builder.Append(' ');
                AppendRayInteractorDiagnostic(builder, rayInteractor);
            }

            if (xriInputRig != null)
            {
                AppendInputRigRayDiagnostic(builder, BlockiverseControllerRole.Right, xriInputRig.RightInteractionRay, ref described);
                AppendInputRigRayDiagnostic(builder, BlockiverseControllerRole.Left, xriInputRig.LeftInteractionRay, ref described);
            }

            if (described == 0)
                builder.Append("none-active");

            return builder.ToString();
#else
            return string.Empty;
#endif
        }

        void AppendInputRigRayDiagnostic(
            StringBuilder builder,
            BlockiverseControllerRole hand,
            XRRayInteractor rayInteractor,
            ref int described)
        {
            if (rayInteractor == null || !rayInteractor.enableUIInteraction)
                return;

            builder.Append(described++ == 0 ? string.Empty : " | ");
            builder.Append("inputRig:");
            builder.Append(hand);
            builder.Append(' ');

            if (xriInputRig != null &&
                xriInputRig.TryGetInteractionRayPose(hand, out Vector3 origin, out Vector3 direction) &&
                direction.sqrMagnitude > Mathf.Epsilon)
            {
                float distance = rayInteractor.maxRaycastDistance > Mathf.Epsilon
                    ? rayInteractor.maxRaycastDistance
                    : CreativeInteractionController.MaxBlockInteractionReachMeters;
                if (TryGetPanelPositionFromWorldRay(new Ray(origin, direction.normalized), distance, out Vector2 panelPosition))
                    AppendPanelPickDiagnostic(builder, panelPosition, "pose");
                else
                    builder.Append("pose=plane-miss");
            }
            else
            {
                builder.Append("pose=missing");
            }
        }

        void AppendCasterDiagnostic(StringBuilder builder, CurveInteractionCaster caster)
        {
            if (caster == null)
            {
                builder.Append("caster=null");
                return;
            }

            if (!caster.samplePoints.IsCreated || caster.samplePoints.Length < 2)
            {
                builder.Append("casterLine=missing ");
                AppendCasterOriginDiagnostic(builder, caster);
                return;
            }

            Vector3 from = caster.samplePoints[0];
            Vector3 to = caster.samplePoints[caster.samplePoints.Length - 1];
            Vector3 rayVector = to - from;
            float distance = rayVector.magnitude;
            if (distance <= Mathf.Epsilon)
            {
                builder.Append("casterLine=zero ");
                AppendCasterOriginDiagnostic(builder, caster);
                return;
            }

            if (TryGetPanelPositionFromWorldRay(new Ray(from, rayVector / distance), distance, out Vector2 panelPosition))
                AppendPanelPickDiagnostic(builder, panelPosition, $"casterLine[{caster.samplePoints.Length}]");
            else
                builder.Append($"casterLine[{caster.samplePoints.Length}]=plane-miss");
        }

        void AppendRayInteractorDiagnostic(StringBuilder builder, XRRayInteractor rayInteractor)
        {
            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                builder.Append("currentHit=");
                builder.Append(hit.collider != null ? hit.collider.name : "null");
                builder.Append(' ');
                if (TryGetTargetFromRaycastHit(hit, out VisualElement hitTarget, out Vector2 hitPanelPosition))
                    builder.Append($"hitTarget={DescribeElement(hitTarget)} hitPanel=({hitPanelPosition.x:0},{hitPanelPosition.y:0}) ");
            }
            else
            {
                builder.Append("currentHit=none ");
            }

            if (rayInteractor.GetLinePoints(ref xriRayLinePoints, out int linePointCount))
            {
                if (TryGetPanelPositionFromLinePoints(xriRayLinePoints, linePointCount, out Vector2 linePanelPosition))
                    AppendPanelPickDiagnostic(builder, linePanelPosition, $"line[{linePointCount}]");
                else
                    builder.Append($"line[{linePointCount}]=plane-miss ");
            }
            else
            {
                builder.Append("line=unavailable ");
            }

            Transform rayOrigin = rayInteractor.rayOriginTransform != null
                ? rayInteractor.rayOriginTransform
                : rayInteractor.transform;
            float distance = rayInteractor.maxRaycastDistance > Mathf.Epsilon
                ? rayInteractor.maxRaycastDistance
                : CreativeInteractionController.MaxBlockInteractionReachMeters;
            if (TryGetPanelPositionFromWorldRay(new Ray(rayOrigin.position, rayOrigin.forward), distance, out Vector2 originPanelPosition))
                AppendPanelPickDiagnostic(builder, originPanelPosition, "origin");
            else
                builder.Append("origin=plane-miss");
        }

        void AppendCasterOriginDiagnostic(StringBuilder builder, CurveInteractionCaster caster)
        {
            if (TryGetPanelPositionFromCasterOrigin(caster, out Vector2 originPanelPosition))
                AppendPanelPickDiagnostic(builder, originPanelPosition, "origin");
            else
                builder.Append("origin=plane-miss");
        }

        void AppendPanelPickDiagnostic(StringBuilder builder, Vector2 panelPosition, string source)
        {
            VisualElement picked = document?.rootVisualElement?.panel?.Pick(panelPosition);
            VisualElement target = FindXriInteractiveElementAt(panelPosition);
            builder.Append(source);
            builder.Append("=panel(");
            builder.Append(panelPosition.x.ToString("0"));
            builder.Append(',');
            builder.Append(panelPosition.y.ToString("0"));
            builder.Append(") picked=");
            builder.Append(DescribeElement(picked));
            builder.Append(" target=");
            builder.Append(DescribeElement(target));
            builder.Append(' ');
        }

        static string DescribeElement(VisualElement element)
        {
            if (element == null)
                return "null";

            return string.IsNullOrEmpty(element.name)
                ? element.GetType().Name
                : $"{element.GetType().Name}:{element.name}";
        }

        bool TryGetTargetFromRayInteractor(
            XRRayInteractor rayInteractor,
            out VisualElement target,
            out Vector2 panelPosition,
            out string raySource)
        {
            target = null;
            panelPosition = default;
            raySource = "none";

            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit) &&
                TryGetTargetFromRaycastHit(hit, out target, out panelPosition))
            {
                raySource = "current-hit";
                return true;
            }

            if (rayInteractor.GetLinePoints(ref xriRayLinePoints, out int linePointCount) &&
                TryGetTargetFromLinePoints(xriRayLinePoints, linePointCount, out target, out panelPosition))
            {
                raySource = "line-points";
                return true;
            }

            Transform rayOrigin = rayInteractor.rayOriginTransform != null
                ? rayInteractor.rayOriginTransform
                : rayInteractor.transform;
            float distance = rayInteractor.maxRaycastDistance > Mathf.Epsilon
                ? rayInteractor.maxRaycastDistance
                : CreativeInteractionController.MaxBlockInteractionReachMeters;
            bool hitFromOrigin = TryGetTargetFromWorldRay(
                new Ray(rayOrigin.position, rayOrigin.forward),
                distance,
                out target,
                out panelPosition);
            raySource = hitFromOrigin ? "origin-forward" : "none";
            return hitFromOrigin;
        }

        bool TryGetTargetFromLinePoints(
            IReadOnlyList<Vector3> linePoints,
            int linePointCount,
            out VisualElement target,
            out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (!TryGetPanelPositionFromLinePoints(linePoints, linePointCount, out panelPosition))
                return false;

            VisualElement picked = document.rootVisualElement.panel?.Pick(panelPosition);
            target = FindXriInteractiveElementAt(panelPosition);
            return target != null;
        }

        internal bool TryGetPanelPositionFromLinePoints(
            IReadOnlyList<Vector3> linePoints,
            int linePointCount,
            out Vector2 panelPosition)
        {
            panelPosition = default;
            if (linePoints == null || linePointCount < 2)
                return false;

            int count = Mathf.Min(linePointCount, linePoints.Count);
            for (int i = 1; i < count; i++)
            {
                Vector3 from = linePoints[i - 1];
                Vector3 to = linePoints[i];
                Vector3 rayVector = to - from;
                float distance = rayVector.magnitude;
                if (distance <= Mathf.Epsilon)
                    continue;

                if (TryGetPanelPositionFromWorldRay(
                        new Ray(from, rayVector / distance),
                        distance,
                        out panelPosition))
                {
                    return true;
                }
            }

            return false;
        }

        bool TryGetTargetFromCasterSampleRay(CurveInteractionCaster caster, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (!caster.samplePoints.IsCreated || caster.samplePoints.Length < 2)
                return TryGetTargetFromCasterOriginRay(caster, out target, out panelPosition);

            Vector3 from = caster.samplePoints[0];
            Vector3 to = caster.samplePoints[caster.samplePoints.Length - 1];
            Vector3 rayVector = to - from;
            float distance = rayVector.magnitude;
            if (distance <= Mathf.Epsilon)
                return TryGetTargetFromCasterOriginRay(caster, out target, out panelPosition);

            return TryGetTargetFromWorldRay(
                new Ray(from, rayVector / distance),
                distance,
                out target,
                out panelPosition);
        }

        bool TryGetTargetFromCasterOriginRay(CurveInteractionCaster caster, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (!TryGetPanelPositionFromCasterOrigin(caster, out panelPosition))
                return false;

            target = FindXriInteractiveElementAt(panelPosition);
            return target != null;
        }

        internal bool TryGetPanelPositionFromCasterOrigin(CurveInteractionCaster caster, out Vector2 panelPosition)
        {
            panelPosition = default;

            if (caster == null)
                return false;

            Transform castOrigin = caster.castOrigin != null ? caster.castOrigin : caster.transform;
            if (castOrigin == null)
                return false;

            float castDistance = caster.castDistance > Mathf.Epsilon
                ? caster.castDistance
                : CreativeInteractionController.MaxBlockInteractionReachMeters;
            return TryGetPanelPositionFromWorldRay(
                new Ray(castOrigin.position, castOrigin.forward),
                castDistance,
                out panelPosition);
        }

        bool TryGetTargetFromWorldRay(Ray ray, float distance, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    distance,
                    BlockiverseProject.VrUiRaycastLayerMask,
                    QueryTriggerInteraction.Collide) &&
                TryGetTargetFromRaycastHit(hit, out target, out panelPosition))
            {
                return true;
            }

            return TryGetTargetFromPanelPlaneRay(ray, distance, out target, out panelPosition);
        }

        bool TryGetTargetFromPanelPlaneRay(Ray ray, float distance, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (!TryGetPanelPositionFromWorldRay(ray, distance, out panelPosition))
                return false;

            VisualElement picked = document.rootVisualElement.panel?.Pick(panelPosition);
            target = FindXriInteractiveElementAt(panelPosition);
            return target != null;
        }

        bool TryGetTargetFromRaycastHits(
            IReadOnlyList<RaycastHit> raycastHits,
            out VisualElement target,
            out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            for (int i = 0; i < raycastHits.Count; i++)
                if (TryGetTargetFromRaycastHit(raycastHits[i], out target, out panelPosition))
                    return true;

            return false;
        }

        bool TryGetTargetFromRaycastHit(RaycastHit hit, out VisualElement target, out Vector2 panelPosition)
        {
            target = null;
            panelPosition = default;

            if (!IsOwnWorldSpaceCollider(hit.collider))
                return false;

            Vector3 localPoint = transform.InverseTransformPoint(hit.point);
            panelPosition = LocalPointToPanelPosition(localPoint);
            VisualElement picked = document.rootVisualElement.panel?.Pick(panelPosition);
            target = FindXriInteractiveElementAt(panelPosition);
            return target != null;
        }

        bool IsOwnWorldSpaceCollider(Collider hitCollider)
        {
            if (hitCollider == null)
                return false;

            if (hitCollider.gameObject == gameObject)
                return true;

            return hitCollider.transform.IsChildOf(transform);
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

        internal static Vector3 PanelPositionToLocalPoint(Vector2 panelPosition)
        {
            float normalizedX = Mathf.Clamp01(panelPosition.x / ReferencePanelWidthPixels);
            float normalizedY = Mathf.Clamp01(panelPosition.y / ReferencePanelHeightPixels);

            return new Vector3(
                Mathf.Lerp(
                    -ReadableWorldSpaceColliderSize.x * 0.5f,
                    ReadableWorldSpaceColliderSize.x * 0.5f,
                    normalizedX),
                Mathf.Lerp(
                    ReadableWorldSpaceColliderSize.y * 0.5f,
                    -ReadableWorldSpaceColliderSize.y * 0.5f,
                    normalizedY),
                0.0f);
        }

        internal bool TryGetPanelPositionFromWorldRay(Ray ray, float distance, out Vector2 panelPosition)
        {
            panelPosition = default;
            if (distance <= Mathf.Epsilon)
                return false;

            Vector3 localOrigin = transform.InverseTransformPoint(ray.origin);
            Vector3 localEnd = transform.InverseTransformPoint(ray.GetPoint(distance));
            Vector3 localDelta = localEnd - localOrigin;
            if (Mathf.Abs(localDelta.z) <= Mathf.Epsilon)
                return false;

            float intersection = -localOrigin.z / localDelta.z;
            if (intersection < 0.0f || intersection > 1.0f)
                return false;

            Vector3 localPoint = localOrigin + localDelta * intersection;
            float halfWidth = ReadableWorldSpaceColliderSize.x * 0.5f;
            float halfHeight = ReadableWorldSpaceColliderSize.y * 0.5f;
            if (localPoint.x < -halfWidth || localPoint.x > halfWidth ||
                localPoint.y < -halfHeight || localPoint.y > halfHeight)
            {
                return false;
            }

            panelPosition = LocalPointToPanelPosition(localPoint);
            return true;
        }

        VisualElement FindXriInteractiveElement(VisualElement picked)
        {
            for (VisualElement current = picked; current != null; current = current.parent)
            {
                if (xriInteractiveElements.Contains(current) || IsXriInteractiveElement(current))
                    return current;
            }

            return null;
        }

        VisualElement FindXriInteractiveElementAt(Vector2 panelPosition)
        {
            VisualElement picked = document?.rootVisualElement?.panel?.Pick(panelPosition);
            VisualElement target = FindXriInteractiveElement(picked);
            if (target != null)
                return target;

            return FindXriInteractiveElementByBounds(panelPosition);
        }

        VisualElement FindXriInteractiveElementByBounds(Vector2 panelPosition)
        {
            VisualElement best = null;
            float bestArea = float.PositiveInfinity;

            foreach (VisualElement candidate in xriInteractiveElements)
            {
                if (!IsXriBoundsCandidate(candidate, panelPosition, out Rect bounds))
                    continue;

                float area = bounds.width * bounds.height;
                if (area < bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }

            return best;
        }

        static bool IsXriBoundsCandidate(VisualElement candidate, Vector2 panelPosition, out Rect bounds)
        {
            bounds = default;
            if (!CanPlayInteractiveFeedback(candidate))
                return false;

            IResolvedStyle style = candidate.resolvedStyle;
            if (style.display == DisplayStyle.None || style.visibility != Visibility.Visible)
                return false;

            bounds = candidate.worldBound;
            return bounds.width > Mathf.Epsilon &&
                bounds.height > Mathf.Epsilon &&
                bounds.Contains(panelPosition);
        }

        static bool IsXriInteractiveElement(VisualElement element) =>
            element is Button ||
            element is Toggle ||
            element is Slider ||
            element is TextField;

        void ActivateXriElement(VisualElement element, Vector2 panelPosition)
        {
            if (!CanPlayInteractiveFeedback(element))
                return;

            switch (element)
            {
                case Button button when xriButtonActions.TryGetValue(button, out Action action):
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

        void UpdateMenuInteractionSmoke()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (menuInteractionSmokeCompleted)
                return;

            if (!menuInteractionSmokeRequested)
                menuInteractionSmokeRequested = IsMenuInteractionSmokeRequested();

            if (!menuInteractionSmokeRequested)
                return;

            if (!IsVisible || root == null || document?.rootVisualElement?.panel == null || buttons.Count == 0)
            {
                LogSmokeDiagnostic("WAIT", "Menu surface is not ready for smoke validation.");
                return;
            }

            if (menuInteractionSmokeFramesRemaining < 0)
            {
                menuInteractionSmokeFramesRemaining = MenuInteractionSmokeDelayFrames;
                LogSmokeDiagnostic("START", $"actions={buttons.Count} layer={gameObject.layer}");
                return;
            }

            if (menuInteractionSmokeFramesRemaining-- > 0)
                return;

            menuInteractionSmokeCompleted = true;
            RunMenuInteractionSmoke();
#endif
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        bool IsMenuInteractionSmokeRequested()
        {
            if (PlayerPrefs.GetInt(MenuInteractionSmokePlayerPrefsKey, 0) == 1)
                return true;

            return File.Exists(MenuInteractionSmokeMarkerPath);
        }

        void RunMenuInteractionSmoke()
        {
            Button button = buttons.Find(candidate => candidate != null && candidate.enabledSelf);
            if (button == null)
            {
                LogSmokeDiagnostic("FAIL", "No enabled action button was available.");
                return;
            }

            Rect buttonBounds = button.worldBound;
            if (buttonBounds.width <= Mathf.Epsilon || buttonBounds.height <= Mathf.Epsilon)
            {
                LogSmokeDiagnostic("FAIL", $"Button {button.name} had empty worldBound {buttonBounds}.");
                return;
            }

            Vector2 targetPanelPosition = buttonBounds.center;
            Vector3 targetWorldPosition = transform.TransformPoint(PanelPositionToLocalPoint(targetPanelPosition));
            Vector3 rayOrigin = Camera.main != null
                ? Camera.main.transform.position
                : transform.position - transform.forward;
            Vector3 rayVector = targetWorldPosition - rayOrigin;
            float distance = rayVector.magnitude + 0.25f;
            bool sawAction = false;
            string invokedAction = null;
            void OnSmokeActionInvoked(string actionId)
            {
                sawAction = true;
                invokedAction = actionId;
            }

            ActionInvoked += OnSmokeActionInvoked;
            try
            {
                if (rayVector.sqrMagnitude <= Mathf.Epsilon ||
                    !TryGetTargetFromWorldRay(new Ray(rayOrigin, rayVector.normalized), distance, out VisualElement target, out Vector2 pickedPanelPosition))
                {
                    LogSmokeDiagnostic(
                        "FAIL",
                        $"Physics ray did not resolve a UI target. button={button.name} origin={rayOrigin} target={targetWorldPosition} layer={gameObject.layer}");
                    return;
                }

                ApplyInteractiveHover(target);
                ActivateXriElement(target, pickedPanelPosition);
                if (!sawAction)
                {
                    LogSmokeDiagnostic("FAIL", $"Target {target.name} activated without dispatching an action.");
                    return;
                }

                LogSmokeDiagnostic(
                    "PASS",
                    $"button={button.name} target={target.name} action={invokedAction} panel={pickedPanelPosition}");
                ClearMenuInteractionSmokeRequest();
            }
            finally
            {
                ActionInvoked -= OnSmokeActionInvoked;
            }
        }

        void ClearMenuInteractionSmokeRequest()
        {
            PlayerPrefs.SetInt(MenuInteractionSmokePlayerPrefsKey, 0);
            try
            {
                if (File.Exists(MenuInteractionSmokeMarkerPath))
                    File.Delete(MenuInteractionSmokeMarkerPath);
            }
            catch (Exception ex)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Trace,
                    $"BV_UI_SMOKE WARN failed to clear marker: {ex.Message}",
                    this);
            }
        }

        static string MenuInteractionSmokeMarkerPath =>
            Path.Combine(BlockiverseTrace.DiagnosticsDirectoryPath, MenuInteractionSmokeMarkerFileName);

        void LogSmokeDiagnostic(string result, string details)
        {
            BlockiverseLog.Info(
                BlockiverseLogCategory.Trace,
                $"BV_UI_SMOKE {result} {details}",
                this);
        }
#endif

        void LogPointerDiagnostic(string eventName, string details)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.unscaledTime < nextPointerDiagnosticTime)
                return;

            nextPointerDiagnosticTime = Time.unscaledTime + PointerDiagnosticIntervalSeconds;
            Collider worldCollider = GetComponent<BoxCollider>();
            string colliderState = worldCollider != null
                ? $"colliderLayer={worldCollider.gameObject.layer} colliderEnabled={worldCollider.enabled} trigger={worldCollider.isTrigger} size={worldCollider.bounds.size}"
                : "collider=missing";
            BlockiverseLog.Info(
                BlockiverseLogCategory.Trace,
                $"BV_UI_POINTER {eventName} visible={IsVisible} {colliderState} mask={BlockiverseProject.VrUiRaycastLayerMask} {details}",
                this);
#endif
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
