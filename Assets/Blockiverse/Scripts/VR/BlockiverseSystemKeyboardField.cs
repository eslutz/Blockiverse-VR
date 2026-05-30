using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blockiverse.VR
{
    /// <summary>
    /// Opens the native system (Quest) keyboard via <see cref="TouchScreenKeyboard"/> when a
    /// world-space <see cref="InputField"/> is selected or clicked by the controller ray, and
    /// streams the result back into the field. This is the native text-entry path for VR; legacy
    /// UI input fields cannot be typed into without a hardware keyboard otherwise.
    /// </summary>
    [RequireComponent(typeof(InputField))]
    public sealed class BlockiverseSystemKeyboardField : MonoBehaviour, IPointerClickHandler, ISelectHandler
    {
        [SerializeField] InputField inputField;

        TouchScreenKeyboard keyboard;

        public void Configure(InputField field)
        {
            inputField = field;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            OpenKeyboard();
        }

        public void OnSelect(BaseEventData eventData)
        {
            OpenKeyboard();
        }

        void Awake()
        {
            if (inputField == null)
                inputField = GetComponent<InputField>();
        }

        void OpenKeyboard()
        {
            if (inputField == null || !TouchScreenKeyboard.isSupported)
                return;

            if (keyboard != null && keyboard.active)
                return;

            keyboard = TouchScreenKeyboard.Open(inputField.text, TouchScreenKeyboardType.Default);
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

            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                inputField.text = keyboard.text;
                inputField.onEndEdit.Invoke(inputField.text);
            }

            keyboard = null;
        }
    }
}
