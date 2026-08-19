using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Blockiverse.Core;
using Blockiverse.Gameplay;

namespace Blockiverse.VR
{
    /// <summary>
    /// Opens the native system (Quest) keyboard via <see cref="TouchScreenKeyboard"/> when a
    /// world-space <see cref="TMP_InputField"/> is selected or clicked by the controller ray, and
    /// streams the result back into the field. This is the native text-entry path for VR; legacy
    /// UI input fields cannot be typed into without a hardware keyboard otherwise.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class BlockiverseSystemKeyboardField : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, ISelectHandler, ISubmitHandler, IDeselectHandler, IBlockiverseSystemKeyboardField
    {
        [SerializeField] TMP_InputField inputField;
        [SerializeField] TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;

        const float KeyboardAppearTimeoutSeconds = 1.5f;

        static BlockiverseSystemKeyboardField activeField;
        float keyboardOpenRequestedAt;
        bool reportedKeyboardVisible;

        TouchScreenKeyboard keyboard;
        string textBeforeEdit;

        public static BlockiverseSystemKeyboardField ActiveField => activeField;
        // True only while a system keyboard is genuinely on screen. Deliberately not "a field is
        // focused": if TouchScreenKeyboard.Open fails to surface an overlay, the player must keep
        // their hands rather than being left with neither hands nor a keyboard.
        public static bool AnyKeyboardVisible =>
            activeField != null && activeField.keyboard != null && activeField.keyboard.active;
        public static event Action<bool> KeyboardVisibilityChanged;

        public TouchScreenKeyboardType KeyboardType => keyboardType;

        public void Configure(TMP_InputField field)
        {
            Configure(field, field != null ? field.keyboardType : TouchScreenKeyboardType.Default);
        }

        public void Configure(TMP_InputField field, TouchScreenKeyboardType keyboardType)
        {
            inputField = field;
            this.keyboardType = SupportedKeyboardType(keyboardType);
            TakeSoftKeyboardOwnership();
        }

        // TMP_InputField opens its OWN system keyboard from ActivateInputField and then closes it
        // again from LateUpdate/DeactivateInputField. Racing it meant a click fired several
        // competing TouchScreenKeyboard.Open calls against the single native keyboard and TMP
        // promptly dismissed the winner: hands hid (we had latched "keyboard shown") but no
        // keyboard ever appeared. Setting shouldHideSoftKeyboard makes this component the sole
        // owner of the keyboard for the field.
        void TakeSoftKeyboardOwnership()
        {
            if (inputField != null)
                inputField.shouldHideSoftKeyboard = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OpenKeyboard();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OpenKeyboard();
        }

        public void OnSelect(BaseEventData eventData)
        {
            OpenKeyboard();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            OpenKeyboard();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (activeField == this && (keyboard == null || !keyboard.active))
                SetActiveField(null);
        }

        void Awake()
        {
            if (inputField == null)
                inputField = GetComponent<TMP_InputField>();
            keyboardType = SupportedKeyboardType(inputField != null ? inputField.keyboardType : keyboardType);
            TakeSoftKeyboardOwnership();
        }

        void OpenKeyboard()
        {
            if (inputField == null || !TouchScreenKeyboard.isSupported)
                return;

            if (activeField == this && keyboard != null)
                return;

            if (activeField != null && activeField != this)
                activeField.CloseKeyboard(commitCurrentText: true, invokeEndEdit: true);

            textBeforeEdit = inputField.text;
            keyboard = TouchScreenKeyboard.Open(inputField.text, keyboardType);
            keyboardOpenRequestedAt = Time.unscaledTime;
            reportedKeyboardVisible = false;
            SetActiveField(this);

            if (keyboard == null)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.General,
                    "System keyboard open returned no keyboard; text entry is unavailable.",
                    context: this);
            }
        }

        static TouchScreenKeyboardType SupportedKeyboardType(TouchScreenKeyboardType requestedType)
        {
            // Meta Quest's system keyboard overlay only supports Default when opened from Unity.
            return TouchScreenKeyboardType.Default;
        }

        void Update()
        {
            if (activeField != this)
            {
                keyboard = null;
                return;
            }

            if (keyboard == null || inputField == null)
            {
                SetActiveField(null);
                return;
            }

            if (keyboard.active)
            {
                if (!reportedKeyboardVisible)
                {
                    reportedKeyboardVisible = true;
                    KeyboardVisibilityChanged?.Invoke(true);
                }

                inputField.text = keyboard.text;
                return;
            }

            // Opened but never surfaced: release the field so the player keeps their hands, and
            // leave evidence in the log rather than silently swallowing the failure.
            if (!reportedKeyboardVisible &&
                keyboard.status == TouchScreenKeyboard.Status.Visible &&
                Time.unscaledTime - keyboardOpenRequestedAt > KeyboardAppearTimeoutSeconds)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.General,
                    $"System keyboard did not appear within {KeyboardAppearTimeoutSeconds:0.#}s " +
                    $"(supported={TouchScreenKeyboard.isSupported} status={keyboard.status}); releasing the field.",
                    context: this);
                inputField.text = textBeforeEdit;
                keyboard = null;
                SetActiveField(null);
                return;
            }

            // The keyboard closed this frame. Commit on Done; otherwise (Canceled / LostFocus)
            // revert the field to the text captured before editing so a cancel does not leave the
            // partially streamed text behind.
            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                CommitKeyboardText(keyboard.text, invokeEndEdit: true);
            }
            else
            {
                inputField.text = textBeforeEdit;
            }

            keyboard = null;
            SetActiveField(null);
        }

        void CloseKeyboard(bool commitCurrentText, bool invokeEndEdit)
        {
            if (keyboard != null)
            {
                if (commitCurrentText && inputField != null)
                    CommitKeyboardText(keyboard.text, invokeEndEdit);
                else if (inputField != null)
                    inputField.text = textBeforeEdit;

                if (keyboard.active)
                    keyboard.active = false;
            }

            keyboard = null;

            if (activeField == this)
                SetActiveField(null);
        }

        void CommitKeyboardText(string text, bool invokeEndEdit)
        {
            if (inputField == null)
                return;

            inputField.text = text;

            if (invokeEndEdit)
                inputField.onEndEdit.Invoke(inputField.text);
        }

        static void SetActiveField(BlockiverseSystemKeyboardField field)
        {
            bool wasVisible = AnyKeyboardVisible;

            if (activeField != null && activeField != field)
                activeField.reportedKeyboardVisible = false;

            activeField = field;

            // Only the hidden edge is raised here; the visible edge fires from Update once the
            // keyboard is really on screen, so the hands never hide for a keyboard that never came.
            if (wasVisible && !AnyKeyboardVisible)
                KeyboardVisibilityChanged?.Invoke(false);
        }
    }
}
