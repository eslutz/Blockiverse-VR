using Blockiverse.Core;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Blockiverse.Tests.EditMode
{
    // Guards the fix for selector arrows advancing two options per trigger pull on Quest:
    // the ray interactor already turns UI Press into a pointer click, so the XRUIInputModule
    // must never route the same action through Submit (which invokes the auto-selected
    // Button's onClick a second time, on press, before the pointer click fires on release).
    //
    // The guard outlived the uGUI menus it was written for. UI Toolkit's panel event handler
    // turns a Submit dispatch into a NavigationSubmitEvent on the focused element, and a
    // UIElements Button fires its clickable from that as well as from the pointer click — the
    // same double-dispatch through a different framework, so the rule is unchanged.
    public sealed class BlockiverseXrUiInputConfiguratorEditModeTests
    {
        GameObject eventSystemObject;
        GameObject buttonObject;
        InputActionAsset actions;

        [SetUp]
        public void SetUp()
        {
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap rightHand = actions.AddActionMap(BlockiverseInputActionNames.RightHandMap);
            rightHand.AddAction(BlockiverseInputActionNames.UiPress, InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            rightHand.AddAction(BlockiverseInputActionNames.UiScroll, InputActionType.Value, "<XRController>{RightHand}/thumbstick");
            InputActionMap leftHand = actions.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
            leftHand.AddAction(BlockiverseInputActionNames.UiPress, InputActionType.Button, "<XRController>{LeftHand}/triggerPressed");
            leftHand.AddAction(BlockiverseInputActionNames.UiScroll, InputActionType.Value, "<XRController>{LeftHand}/thumbstick");

            eventSystemObject = new GameObject("Test EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }

        [TearDown]
        public void TearDown()
        {
            if (buttonObject != null)
                Object.DestroyImmediate(buttonObject);
            if (eventSystemObject != null)
                Object.DestroyImmediate(eventSystemObject);
            if (actions != null)
                Object.DestroyImmediate(actions);
        }

        [Test]
        public void ConfigureFromAssetBindsPointerClickButNeverSubmitOrNavigate()
        {
            XRUIInputModule module = eventSystemObject.GetComponent<XRUIInputModule>();

            BlockiverseXrUiInputConfigurator.Configure(module, actions, BlockiverseControllerRole.Right);

            Assert.That(module.leftClickAction, Is.Not.Null);
            Assert.That(module.leftClickAction.action.name, Is.EqualTo(BlockiverseInputActionNames.UiPress));
            Assert.That(module.scrollWheelAction, Is.Not.Null);
            Assert.That(module.scrollWheelAction.action.name, Is.EqualTo(BlockiverseInputActionNames.UiScroll));
            Assert.That(module.submitAction, Is.Null,
                "UI Press must not also drive Submit: one trigger pull would fire the selected Button twice.");
            Assert.That(module.navigateAction, Is.Null,
                "UI Scroll must not move uGUI selection under the ray.");
        }

        [Test]
        public void ConfigureFromReferencesBindsPointerClickButNeverSubmitOrNavigate()
        {
            XRUIInputModule module = eventSystemObject.GetComponent<XRUIInputModule>();
            InputActionReference press = InputActionReference.Create(
                actions.FindActionMap(BlockiverseInputActionNames.RightHandMap).FindAction(BlockiverseInputActionNames.UiPress));
            InputActionReference scroll = InputActionReference.Create(
                actions.FindActionMap(BlockiverseInputActionNames.RightHandMap).FindAction(BlockiverseInputActionNames.UiScroll));

            try
            {
                BlockiverseXrUiInputConfigurator.Configure(module, press, scroll);

                Assert.That(module.leftClickAction, Is.SameAs(press));
                Assert.That(module.scrollWheelAction, Is.SameAs(scroll));
                Assert.That(module.submitAction, Is.Null);
                Assert.That(module.navigateAction, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(press);
                Object.DestroyImmediate(scroll);
            }
        }

        [Test]
        public void SubmitOnSelectedButtonInvokesOnClickWhichIsWhyItMustStayUnbound()
        {
            // Documents the uGUI behavior behind the guard above: a Button that became the
            // EventSystem selection on pointer-down runs onClick from OnSubmit as well as
            // from OnPointerClick, so any Submit dispatch doubles every ray click.
            //
            // Kept on the uGUI Button after the menu cutover because that is where the bug was
            // actually observed and where it can still be demonstrated in EditMode — UI Toolkit
            // needs a live panel to dispatch a navigation event. The uGUI package remains in the
            // project (CreativeHotbar, the subtitle toast, the system keyboard field), so this
            // is a real mechanism, not a museum piece.
            EventSystem eventSystem = eventSystemObject.GetComponent<EventSystem>();
            buttonObject = new GameObject("Next", typeof(RectTransform), typeof(Button));
            Button button = buttonObject.GetComponent<Button>();
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);

            eventSystem.SetSelectedGameObject(buttonObject);
            ExecuteEvents.Execute(buttonObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);

            Assert.That(clicks, Is.EqualTo(1),
                "uGUI Button.OnSubmit invokes onClick; the input module must therefore never route UI Press to Submit.");
        }
    }
}
