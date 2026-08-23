using System.Collections.Generic;
using Blockiverse.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // Base class for every UI Toolkit screen controller (ADR 0010 §5: one UXML document and
    // one dedicated controller per screen; no generic shell). Encodes the lifecycle
    // discipline the Phase 1 proof panel established, which every production screen must
    // follow because each of its failure modes is silent:
    //
    //  - rootVisualElement is null until UIDocument builds its panel; Attach retries on the
    //    next OnEnable rather than failing loudly during domain reloads.
    //  - A screen hides by collapsing its root element and disabling its collider — NEVER by
    //    disabling the UIDocument, whose OnDisable nulls rootVisualElement and whose OnEnable
    //    rebuilds a brand-new tree behind this component's back.
    //  - Callbacks are unregistered from the previous elements before re-querying, and the
    //    registration balance can only ever read 0 or 1; double-binding fires every command
    //    twice with no exception and no warning.
    //  - Every named element is queried and IsBound asserted with a loud error, because a
    //    missing VisualTreeAsset still yields a root and presents as a healthy blank panel.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public abstract class UiToolkitScreenController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Applied to the document root in order. Assigned by the bootstrapper; Tokens.uss " +
                 "must come before any sheet that reads its variables.")]
        List<StyleSheet> styleSheets = new();

        [SerializeField]
        [Tooltip("Disabled together with the panel so a hidden panel cannot intercept world rays.")]
        Collider panelCollider;

        UIDocument document;
        VisualElement screenRoot;
        UiToolkitMenuHost host;
        bool visible;
        bool acceptsInput;

        public const string ScreenRootElementName = "bv-screen-root";

        public abstract string ScreenId { get; }

        public bool IsVisible => visible;
        public bool AcceptsInput => acceptsInput;
        public bool IsBound { get; private set; }
        public int AttachCount { get; private set; }
        public int CallbackRegistrationBalance { get; private set; }
        public VisualElement Root => screenRoot;
        public UiToolkitMenuHost Host => host;

        // The routing/domain hub. Screen controllers use this for the public verbs the uGUI
        // panels reached through persistent listeners (OpenInventoryScreen, Close*Screen, …)
        // and DispatchAction for canonical action ids.
        protected BlockiverseMenuController MenuController => host != null ? host.MenuController : null;

        public void ConfigureHost(UiToolkitMenuHost menuHost) => host = menuHost;

        protected void DispatchAction(string actionId)
        {
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
                controller.DispatchAction(actionId);
        }

        void Awake()
        {
            document = GetComponent<UIDocument>();

            if (panelCollider == null)
                panelCollider = GetComponent<Collider>();

            OnAwake();
        }

        void OnEnable() => Attach();

        void OnDisable() => Detach();

        // Routed visibility. acceptsInputNow is false while a modal owns the input target,
        // and always false for the world-loading overlay; the collider tracks it so a
        // visible-but-input-blocked screen cannot swallow rays meant for the modal above it.
        public void SetVisible(bool value, bool acceptsInputNow)
        {
            bool wasVisible = visible;
            visible = value;
            acceptsInput = acceptsInputNow && value;

            if (screenRoot != null)
                screenRoot.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

            if (panelCollider != null)
                panelCollider.enabled = acceptsInput;

            if (value && !wasVisible)
                OnShown();
            else if (!value && wasVisible)
                OnHidden();
        }

        // EditMode test seam: UIDocument never builds rootVisualElement outside Play mode, so
        // tests instantiate the VisualTreeAsset themselves and attach the controller to it.
        public void AttachForTest(VisualElement root)
        {
            AttachTo(root);
        }

        // How many pixels of content do not fit, i.e. how far the panel is from holding its
        // own content without scrolling. Positive means it scrolls.
        //
        // Measured rather than modelled: counting rows in the UXML gets this wrong, because a
        // row of buttons laid out horizontally looks like N rows to a parser and is one row on
        // screen. Only a real layout pass knows, so this is Play-mode only — outside it, and
        // before the first layout, it returns 0 and reports false.
        public bool TryMeasureContentOverflow(out float overflowPixels)
        {
            overflowPixels = 0f;

            if (screenRoot == null || float.IsNaN(screenRoot.layout.height))
                return false;

            ScrollView scroll = screenRoot.Q<ScrollView>();

            if (scroll != null)
            {
                float content = scroll.contentContainer.layout.height;
                float viewport = scroll.contentViewport.layout.height;

                if (float.IsNaN(content) || float.IsNaN(viewport))
                    return false;

                overflowPixels = content - viewport;
                return true;
            }

            // No ScrollView: content cannot scroll, so overflow is what spills past the root.
            float needed = 0f;

            foreach (VisualElement child in screenRoot.Children())
            {
                if (!float.IsNaN(child.layout.height))
                    needed += child.layout.height + child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom;
            }

            float available = screenRoot.layout.height
                - screenRoot.resolvedStyle.paddingTop
                - screenRoot.resolvedStyle.paddingBottom;
            overflowPixels = needed - available;
            return true;
        }

        void Attach()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            VisualElement root = document != null ? document.rootVisualElement : null;

            if (root == null)
                return;

            foreach (StyleSheet sheet in styleSheets)
            {
                if (sheet == null)
                    continue;

                if (!root.styleSheets.Contains(sheet))
                    root.styleSheets.Add(sheet);
            }

            AttachTo(root);

            // Re-apply the current visibility to the freshly built tree, or a panel hidden
            // before a rebuild comes back visible.
            SetVisible(visible, acceptsInput);
        }

        void AttachTo(VisualElement root)
        {
            UnregisterIfRegistered();

            screenRoot = root.Q<VisualElement>(ScreenRootElementName) ?? root;
            IsBound = OnAttach(root);
            AttachCount++;

            if (CallbackRegistrationBalance == 0)
            {
                OnRegisterCallbacks();
                CallbackRegistrationBalance++;
            }

            if (!IsBound)
            {
                BlockiverseLog.Error(
                    BlockiverseLogCategory.General,
                    $"{GetType().Name} attached but could not find its elements. " +
                    "The UXML did not load, or its element names have drifted.",
                    exception: null,
                    context: this);
            }
        }

        void Detach()
        {
            UnregisterIfRegistered();
            OnDetach();
            screenRoot = null;
            IsBound = false;
        }

        void UnregisterIfRegistered()
        {
            if (CallbackRegistrationBalance == 0)
                return;

            OnUnregisterCallbacks();
            CallbackRegistrationBalance--;
        }

        // Query helper: logs loudly on a missing element and reports the miss through the
        // return value so OnAttach can compute IsBound as a conjunction.
        protected T Require<T>(VisualElement root, string elementName, ref bool allFound) where T : VisualElement
        {
            T element = root.Q<T>(elementName);

            if (element == null)
            {
                allFound = false;
                BlockiverseLog.Error(
                    BlockiverseLogCategory.General,
                    $"{GetType().Name}: element '{elementName}' ({typeof(T).Name}) not found in its document.",
                    exception: null,
                    context: this);
            }

            return element;
        }

        protected virtual void OnAwake()
        {
        }

        // Query every element by stable name and register nothing here; return false if any
        // element is missing. Called on every attach with brand-new element instances.
        protected abstract bool OnAttach(VisualElement root);

        // Register value-changed/click callbacks on the elements captured in OnAttach. The
        // base class guarantees this is balanced with OnUnregisterCallbacks.
        protected abstract void OnRegisterCallbacks();

        protected abstract void OnUnregisterCallbacks();

        // Clear element references captured in OnAttach.
        protected abstract void OnDetach();

        // Routed-visibility transitions (screen became / stopped being the routed screen).
        // LAN discovery listening and similar visibility-keyed resources belong here, NOT in
        // OnEnable/OnDisable — screens hide by collapsing the root, so Unity lifecycle
        // callbacks fire once at scene load (matrix §4 item 7).
        protected virtual void OnShown()
        {
        }

        protected virtual void OnHidden()
        {
        }
    }
}
