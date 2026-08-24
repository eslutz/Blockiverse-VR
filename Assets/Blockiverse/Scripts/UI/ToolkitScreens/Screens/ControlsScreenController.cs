using Blockiverse.UI.Toolkit;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // Controls reference screen (migration matrix row 13), reached from the settings hub.
    // Ports the generated uGUI "Controls Panel": title, the same canonical mapping copy the
    // first-run popup shows (one shared ControllerMappingText constant in the uGUI source),
    // one Close button. Close dispatches the canonical controls.close action — the exact
    // route the uGUI persistent listener takes through
    // BlockiverseMenuController.CloseControlsScreen.
    [UiToolkitScreen(
        MenuActions.ControlsScreen,
        "Assets/Blockiverse/UI/Documents/ControlsScreen.uxml",
        760,
        630,
        UiToolkitPlacementProfile.Menu)]
    public sealed class ControlsScreenController : UiToolkitScreenController
    {
        // Deliberately the first-run screen's key: the two screens show identical copy today
        // because the uGUI source shares one constant, and one entry keeps them in lockstep.
        public const string BodyTextKey = ControllerMappingScreenController.BodyTextKey;

        public const string BodyElementName = "bv-controls-body";
        public const string CloseButtonElementName = "bv-controls-close";

        Label bodyLabel;
        Button closeButton;

        public override string ScreenId => MenuActions.ControlsScreen;

        protected override bool OnAttach(VisualElement root)
        {
            bool allFound = true;
            bodyLabel = Require<Label>(root, BodyElementName, ref allFound);
            closeButton = Require<Button>(root, CloseButtonElementName, ref allFound);

            ApplyBodyText();

            return allFound;
        }

        protected override void OnRegisterCallbacks()
        {
            if (closeButton != null)
                closeButton.clicked += OnClosePressed;

            // The body is cached dynamic text, so static-binding locale updates do not cover
            // it. HasSettings guards both ends: touching the event must never force settings
            // creation, and removing a never-added handler is a no-op if availability flipped.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (closeButton != null)
                closeButton.clicked -= OnClosePressed;

            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        }

        protected override void OnDetach()
        {
            bodyLabel = null;
            closeButton = null;
        }

        void OnSelectedLocaleChanged(Locale locale) => ApplyBodyText();

        void ApplyBodyText()
        {
            if (bodyLabel != null)
                bodyLabel.text = UiText.Get(BodyTextKey);
        }

        void OnClosePressed() => DispatchAction(MenuActions.ControlsClose);

        // EditMode test seam (public: InternalsVisibleTo is forbidden project-wide). Invokes
        // the handler directly, so it cannot detect double-binding — the base class'
        // CallbackRegistrationBalance covers that.
        public void SimulateClose() => OnClosePressed();
    }
}
