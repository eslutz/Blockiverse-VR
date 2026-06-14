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
    public sealed class BlockiverseSystemKeyboardField : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, ISelectHandler, ISubmitHandler
    {
        [SerializeField] TMP_InputField inputField;
        [SerializeField] TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;

        TouchScreenKeyboard keyboard;
        string textBeforeEdit;

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

            if (keyboard != null && keyboard.active)
                return;

            textBeforeEdit = inputField.text;
            keyboard = TouchScreenKeyboard.Open(inputField.text, keyboardType);
        }

        static TouchScreenKeyboardType SupportedKeyboardType(TouchScreenKeyboardType requestedType)
        {
            // Meta Quest's system keyboard overlay only supports Default when opened from Unity.
            return TouchScreenKeyboardType.Default;
        }

        void Update()
        {
            if (keyboard == null || inputField == null)
                return;

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
                inputField.text = keyboard.text;
                inputField.onEndEdit.Invoke(inputField.text);
            }
            else
            {
                inputField.text = textBeforeEdit;
            }

            keyboard = null;
        }
    }
}
