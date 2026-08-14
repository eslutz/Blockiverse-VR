using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Blockiverse.VR
{
    /// <summary>
    /// Opens the native system (Quest) keyboard via <see cref="TouchScreenKeyboard"/> when a
    /// world-space <see cref="TMP_InputField"/> is selected or clicked by the controller ray, and
    /// streams the result back into the field. This is the native text-entry path for VR; legacy
    /// UI input fields cannot be typed into without a hardware keyboard otherwise.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public sealed class BlockiverseSystemKeyboardField : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, ISelectHandler, ISubmitHandler, IDeselectHandler
    {
        [SerializeField] TMP_InputField inputField;
        [SerializeField] TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;

        static BlockiverseSystemKeyboardField activeField;

        TouchScreenKeyboard keyboard;
        string textBeforeEdit;

        public static BlockiverseSystemKeyboardField ActiveField => activeField;
        public static bool AnyKeyboardVisible => activeField != null;
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
        }

        void OpenKeyboard()
        {
            if (inputField == null || !TouchScreenKeyboard.isSupported)
                return;

            if (activeField == this && keyboard != null && keyboard.active)
                return;

            if (activeField != null && activeField != this)
                activeField.CloseKeyboard(commitCurrentText: true, invokeEndEdit: true);

            textBeforeEdit = inputField.text;
            keyboard = TouchScreenKeyboard.Open(inputField.text, keyboardType);
            SetActiveField(this);
            inputField.ActivateInputField();
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
                inputField.text = keyboard.text;
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
            bool wasVisible = activeField != null;
            activeField = field;
            bool isVisible = activeField != null;

            if (wasVisible != isVisible)
                KeyboardVisibilityChanged?.Invoke(isVisible);
        }
    }
}
