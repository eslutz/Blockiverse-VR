using UnityEngine.UIElements;

namespace Blockiverse.UI
{
    /// <summary>
    /// Makes a world-space <see cref="TextField"/> re-open the Quest system keyboard on every tap,
    /// not just the first.
    /// </summary>
    /// <remarks>
    /// UI Toolkit opens the system keyboard when a text input GAINS focus. On a headset the
    /// keyboard is a separate overlay, so dismissing it does not move focus — the field is still
    /// focused when the player looks back at the panel. Tapping it again is therefore not a focus
    /// change, nothing fires, and the keyboard never returns. Eric hit exactly this on device: it
    /// worked the first time and never again.
    ///
    /// The fix is to give UI Toolkit a fresh focus edge, NOT to open the keyboard ourselves. That
    /// distinction is the whole design, and the project has already paid for the lesson: the uGUI
    /// path needed BlockiverseSystemKeyboardField with `shouldHideSoftKeyboard = true` precisely
    /// because TMP_InputField opened its own keyboard and then closed it again from LateUpdate,
    /// racing a single native overlay until neither owner won — hands hid and no keyboard ever
    /// appeared. Calling TouchScreenKeyboard.Open here would rebuild that race against UI Toolkit
    /// instead of TMP. Let the framework own the keyboard; only correct the focus state it reads.
    ///
    /// Deliberately not verified in the simulator: the Meta XR Simulator has no system keyboard,
    /// so this is reasoned from the focus model and confirmed on hardware, or not at all.
    /// </remarks>
    public static class ToolkitKeyboardField
    {
        static readonly EventCallback<PointerDownEvent> Handler = OnPointerDown;

        /// <summary>Idempotent: unregisters before registering, so an Attach/Detach imbalance
        /// across a screen re-attach cannot stack duplicate handlers.</summary>
        public static void Attach(TextField field)
        {
            if (field == null)
                return;

            field.UnregisterCallback(Handler, TrickleDown.TrickleDown);
            field.RegisterCallback(Handler, TrickleDown.TrickleDown);
        }

        public static void Detach(TextField field)
        {
            field?.UnregisterCallback(Handler, TrickleDown.TrickleDown);
        }

        static void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.currentTarget is not TextField field)
                return;

            FocusController focus = field.focusController;

            if (focus == null)
                return;

            // Only intervene when the field ALREADY holds focus. A first tap works on its own, and
            // blurring an unfocused field would cancel the focus the framework is about to give it.
            bool alreadyFocused = focus.focusedElement == field
                || (focus.focusedElement is VisualElement focused && field.Contains(focused));

            if (!alreadyFocused)
                return;

            field.Blur();

            // Next frame, not this one: blur and focus inside a single event dispatch collapse to
            // no net change, and UI Toolkit would see no focus edge — the same nothing-happens the
            // player already reported.
            field.schedule.Execute(() => field.Focus());
        }
    }
}
