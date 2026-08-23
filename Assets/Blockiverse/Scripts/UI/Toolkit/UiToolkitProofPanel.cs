using System.Collections.Generic;
using Blockiverse.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI.Toolkit
{
    // Phase 1 proof panel (ADR 0010). Scaffolding, not a production screen.
    //
    // It answers one question that no amount of editor testing can: does a native world-space
    // UI Toolkit panel accept controller-ray hover, activation, scrolling and text entry on a real
    // Quest, without any custom input code? Everything it does is therefore observable in a headset
    // — every interaction writes a line the player can read — and nothing it does touches a routed
    // screen, the router, or the rig prefab.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiToolkitProofPanel : MonoBehaviour
    {
        public const string ScreenElementName = "bv-proof-screen";
        public const string ButtonName = "bv-proof-button";
        public const string ToggleName = "bv-proof-toggle";
        public const string SliderName = "bv-proof-slider";
        public const string TextFieldName = "bv-proof-text-field";
        public const string ScrollViewName = "bv-proof-content";
        public const string StatusName = "bv-proof-status";

        [SerializeField]
        [Tooltip("Applied to the document root in order. Assigned by the bootstrapper; Tokens.uss " +
                 "must come before any sheet that reads its variables.")]
        List<StyleSheet> styleSheets = new();

        [SerializeField]
        [Tooltip("Disabled together with the panel so a hidden panel cannot intercept world rays.")]
        Collider panelCollider;

        UIDocument document;
        VisualElement screen;
        Button button;
        Toggle toggle;
        Slider slider;
        TextField textField;
        Label status;

        bool visible = true;

        // Counters exist so the callback discipline is provable rather than asserted. A screen that
        // double-registers fires every action twice, and with UI Toolkit's callback model that is
        // silent — no exception, no warning, just two commands.
        public int AttachCount { get; private set; }

        // Net registrations: incremented once per RegisterCallbacks, decremented once per
        // Unregister. Anything other than 0 or 1 means the discipline has broken.
        public int CallbackRegistrationBalance { get; private set; }

        public int ButtonActivationCount { get; private set; }
        public string LastStatusMessage { get; private set; } = string.Empty;

        // True when the last Attach found every named element. False means the UXML did not load or
        // its element names drifted — the panel would otherwise present as a healthy blank
        // rectangle, reporting "ready" through every counter while doing nothing.
        public bool IsBound { get; private set; }

        public bool IsVisible => visible;

        void Awake()
        {
            document = GetComponent<UIDocument>();

            if (panelCollider == null)
                panelCollider = GetComponent<Collider>();
        }

        void OnEnable() => Attach();

        void OnDisable() => Detach();

        // Hides by collapsing the root element, NOT by disabling the UIDocument.
        //
        // UIDocument.OnDisable calls set_rootVisualElement(null), and OnEnable rebuilds the tree
        // from the VisualTreeAsset — producing brand-new Button/Toggle/Slider instances. Because
        // UIDocument is a different component, disabling it does not run this component's OnDisable,
        // so Attach/Detach never re-run and the cached references end up pointing at a discarded
        // tree while the registration balance still reads as bound. One hide/show cycle would leave it
        // rendered, unstyled and completely inert, with nothing logged.
        public void SetVisible(bool value)
        {
            visible = value;

            if (screen != null)
                screen.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

            if (panelCollider != null)
                panelCollider.enabled = value;
        }

        void Attach()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            VisualElement root = document != null ? document.rootVisualElement : null;

            if (root == null)
            {
                // rootVisualElement is null until the document has built its panel. UIDocument
                // rebuilds on enable, so a later OnEnable will find it; failing loudly here would
                // spam the log during normal domain reloads.
                return;
            }

            ApplyStyleSheets(root);

            // Unregister from the PREVIOUS elements before re-querying, so a rebuilt tree cannot
            // leave the guard set while the references it guards have been replaced.
            UnregisterCallbacks();

            screen = root.Q<VisualElement>(ScreenElementName);
            button = root.Q<Button>(ButtonName);
            toggle = root.Q<Toggle>(ToggleName);
            slider = root.Q<Slider>(SliderName);
            textField = root.Q<TextField>(TextFieldName);
            status = root.Q<Label>(StatusName);

            IsBound = screen != null && button != null && toggle != null &&
                      slider != null && textField != null && status != null;

            RegisterCallbacks();
            AttachCount++;

            if (!IsBound)
            {
                // Loud, because the alternative is a blank panel in a headset with every counter
                // reporting success. A missing VisualTreeAsset produces exactly this state.
                BlockiverseLog.Error(
                    BlockiverseLogCategory.General,
                    $"{nameof(UiToolkitProofPanel)} attached but could not find its elements. " +
                    "The UXML did not load, or its element names have drifted. " +
                    $"screen={screen != null} button={button != null} toggle={toggle != null} " +
                    $"slider={slider != null} textField={textField != null} status={status != null}",
                    exception: null,
                    context: this);
                return;
            }

            // Re-apply the current visibility to the freshly built tree, or a panel hidden before a
            // rebuild comes back visible.
            SetVisible(visible);
            Report("Ready. Point at a control.");
        }

        void Detach()
        {
            UnregisterCallbacks();

            screen = null;
            button = null;
            toggle = null;
            slider = null;
            textField = null;
            status = null;
            IsBound = false;
        }

        void ApplyStyleSheets(VisualElement root)
        {
            foreach (StyleSheet sheet in styleSheets)
            {
                if (sheet == null)
                    continue;

                // Contains() rather than a "did I already do this" flag: UIDocument may rebuild the
                // root without this component being disabled, and a flag would then leave the
                // rebuilt tree unstyled.
                if (!root.styleSheets.Contains(sheet))
                    root.styleSheets.Add(sheet);
            }
        }

        void RegisterCallbacks()
        {
            if (CallbackRegistrationBalance > 0)
                return;

            if (button != null)
                button.clicked += OnButtonClicked;

            if (toggle != null)
                toggle.RegisterValueChangedCallback(OnToggleChanged);

            if (slider != null)
                slider.RegisterValueChangedCallback(OnSliderChanged);

            if (textField != null)
                textField.RegisterValueChangedCallback(OnTextChanged);

            CallbackRegistrationBalance++;
        }

        void UnregisterCallbacks()
        {
            if (CallbackRegistrationBalance == 0)
                return;

            if (button != null)
                button.clicked -= OnButtonClicked;

            if (toggle != null)
                toggle.UnregisterValueChangedCallback(OnToggleChanged);

            if (slider != null)
                slider.UnregisterValueChangedCallback(OnSliderChanged);

            if (textField != null)
                textField.UnregisterValueChangedCallback(OnTextChanged);

            CallbackRegistrationBalance--;
        }

        void OnButtonClicked()
        {
            ButtonActivationCount++;

            // The count is shown, not just the fact of a press: one ray press producing two
            // activations is the specific failure this panel exists to detect, and "Activations: 2"
            // after one trigger pull is visible in a headset where a duplicated log line is not.
            Report($"Button activated. Activations: {ButtonActivationCount}.");
        }

        void OnToggleChanged(ChangeEvent<bool> evt) =>
            Report($"Toggle {(evt.newValue ? "on" : "off")}.");

        void OnSliderChanged(ChangeEvent<float> evt) =>
            Report($"Slider {evt.newValue:0}.");

        void OnTextChanged(ChangeEvent<string> evt) =>
            Report($"Text length {evt.newValue?.Length ?? 0}.");

        void Report(string message)
        {
            LastStatusMessage = message;

            if (status != null)
                status.text = message;
        }

        // Public, not internal: this project forbids InternalsVisibleTo (CLAUDE.md), so an internal
        // member would be invisible to the test assembly.
        //
        // This exercises the STATUS path, not the binding. It calls the handler directly, so it
        // increments exactly once however many handlers are subscribed and therefore cannot detect
        // double-binding — do not use it as evidence of that. Double-binding is proven instead by
        // CallbackRegistrationBalance, which can only sit at 1 if every register is matched by
        // exactly one unregister, and which a test can assert across repeated enable/disable cycles.
        public void SimulateButtonActivation() => OnButtonClicked();
    }
}
