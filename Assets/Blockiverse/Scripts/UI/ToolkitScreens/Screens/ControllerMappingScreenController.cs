using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI.Toolkit;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    // First-run controller mapping reference (migration matrix row 5). Ports the generated
    // uGUI "Controller Mapping Popup": title, the canonical mapping copy, one Close button.
    //
    // Close routes through BlockiverseMenuController.CloseControllerMappingScreen, which owns
    // the Blockiverse.ControllerMappingPopupSeen PlayerPrefs gate and the depth-aware
    // ClearToRoot-vs-Pop routing (matrix §4 item 18) — neither is duplicated here. The uGUI
    // popup's ray-intersection close fallback is also not ported: it existed because that
    // Canvas' Close button was unreachable on some devices, and the menu controller already
    // skips it while a UI Toolkit frontend is registered.
    [UiToolkitScreen(
        MenuActions.ControllerMappingScreen,
        "Assets/Blockiverse/UI/Documents/ControllerMappingScreen.uxml",
        760,
        630,
        UiToolkitPlacementProfile.Menu)]
    public sealed class ControllerMappingScreenController : UiToolkitScreenController
    {
        // Requested table entry carrying the canonical 11-line mapping copy. The uGUI source
        // authors that copy as a raw literal with no table key (it is reverse-lookup-exempt),
        // so it cannot be a static UXML binding until the entry exists; UiText.Get falls back
        // to returning the key itself in the meantime.
        public const string BodyTextKey = "ui.toolkit.controller_map.body";

        public const string BodyElementName = "bv-mapping-body";
        public const string CloseButtonElementName = "bv-mapping-close";

        Label bodyLabel;
        Button closeButton;

        BlockiverseAudioCuePlayer audioCuePlayer;
        IBlockiverseInteractionHaptics interactionHaptics;

        // Close is navigation, not a confirmation, so it takes the plain click cue.
        void OnCloseButtonClicked()
        {
            BlockiverseUiFeedback.Play(ref audioCuePlayer, ref interactionHaptics, BlockiverseAudioCue.UiSelect);
            OnClosePressed();
        }

        public override string ScreenId => MenuActions.ControllerMappingScreen;

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
                closeButton.clicked += OnCloseButtonClicked;

            // The body is cached dynamic text, so static-binding locale updates do not cover
            // it. HasSettings guards both ends: touching the event must never force settings
            // creation, and removing a never-added handler is a no-op if availability flipped.
            if (LocalizationSettings.HasSettings)
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        protected override void OnUnregisterCallbacks()
        {
            if (closeButton != null)
                closeButton.clicked -= OnCloseButtonClicked;

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

        void OnClosePressed()
        {
            BlockiverseMenuController controller = MenuController;
            if (controller != null)
                controller.CloseControllerMappingScreen();
        }

        // EditMode test seam (public: InternalsVisibleTo is forbidden project-wide). Invokes
        // the handler directly, so it cannot detect double-binding — the base class'
        // CallbackRegistrationBalance covers that.
        public void SimulateClose() => OnClosePressed();
    }
}
