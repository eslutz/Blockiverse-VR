using System;
using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.UI;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.InputSystem;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BootScenePlayModeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneLoadsWithXrRigAndCamera()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            Assert.That(rig, Is.Not.Null);
            Assert.That(Camera.main, Is.Not.Null);

            Type markerType = Type.GetType("Blockiverse.VR.BlockiverseXRRigMarker, Blockiverse.VR");
            Assert.That(markerType, Is.Not.Null);
            Assert.That(rig.GetComponent(markerType), Is.Not.Null);
        }

        // The only test anywhere that proves the GENERATED Boot scene's HUD family is bound and
        // rendering real values end to end. Every UI Toolkit test under Tests/EditMode/Toolkit
        // builds its VisualElement tree by hand, so a HUD that generates but never binds — which
        // presents as a perfectly healthy blank panel — would ship green without this. Retargeted
        // from the uGUI SurvivalInventoryPanel/SurvivalCraftingPanel/SurvivalHealthPanel version;
        // the six text oracles are its oracles, unchanged.
        [UnityTest]
        public IEnumerator BootSceneShowsBoundSurvivalHudScreens()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);
            yield return null;

            GameplayHudController hud = FindScreenIncludingInactive<GameplayHudController>();
            InventoryScreenController inventoryScreen = FindScreenIncludingInactive<InventoryScreenController>();
            CraftingScreenController craftingScreen = FindScreenIncludingInactive<CraftingScreenController>();

            Assert.That(hud, Is.Not.Null);
            Assert.That(inventoryScreen, Is.Not.Null);
            Assert.That(craftingScreen, Is.Not.Null);

            Assert.That(hud.IsBound, Is.True, "The generated gameplay HUD did not find its elements.");
            Assert.That(inventoryScreen.IsBound, Is.True, "The generated inventory screen did not find its elements.");
            Assert.That(craftingScreen.IsBound, Is.True, "The generated crafting screen did not find its elements.");

            Assert.That(hud.IsVisible, Is.False, "The gameplay HUD starts hidden while the title/menu route is active.");

            // The inventory screen binds at the routed-visibility boundary, not in Awake: screens
            // hide by collapsing their root rather than disabling the component, so OnEnable is
            // not an "opened" signal. It therefore holds nothing until it is shown.
            inventoryScreen.SetVisible(true, true);

            AssertScreenRendersText(inventoryScreen, "Hotbar 1 /");
            AssertScreenRendersText(inventoryScreen, "Empty");
            AssertScreenRendersText(craftingScreen, "Work Plank x6");
            AssertScreenRendersText(craftingScreen, "Ready");
            AssertScreenRendersText(hud, "100 / 100");
            AssertScreenRendersText(hud, "Stable");
        }

        static T FindScreenIncludingInactive<T>() where T : UiToolkitScreenController =>
            UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

        [UnityTest]
        public IEnumerator BootSceneShowsDismissibleControllerMappingPopup()
        {
            string key = BlockiverseMenuController.ControllerMappingPopupSeenPrefKey;
            PlayerPrefs.DeleteKey(key);

            try
            {
                yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

                // The first-run prompt is the toolkit ControllerMappingScreen. This test carried
                // a uGUI half through the coexistence window (the fallback popup had to exist and
                // stay hidden); that half went with the uGUI menus.
                BlockiverseMenuController menuController =
                    UnityEngine.Object.FindFirstObjectByType<BlockiverseMenuController>(FindObjectsInactive.Include);
                Assert.That(menuController, Is.Not.Null);
                Assert.That(menuController.HasFrontend, Is.True,
                    "The generated Boot scene must register the UI Toolkit host as the menu frontend.");
                Assert.That(menuController.IsActiveScreen(MenuActions.ControllerMappingScreen), Is.True,
                    "First run must route to the controller-mapping screen.");

                UiToolkitScreenController mappingScreen = null;
                foreach (UiToolkitScreenController screen in UnityEngine.Object.FindObjectsByType<UiToolkitScreenController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (screen.ScreenId == MenuActions.ControllerMappingScreen)
                        mappingScreen = screen;
                }

                Assert.That(mappingScreen, Is.Not.Null, "The toolkit controller-mapping screen is missing from the scene.");
                Assert.That(mappingScreen.IsVisible, Is.True,
                    "The toolkit controller-mapping screen must be the visible first-run surface.");

                menuController.CloseControllerMappingScreen();
                yield return null;

                Assert.That(mappingScreen.IsVisible, Is.False);
                Assert.That(menuController.IsActiveScreen(MenuActions.TitleScreen), Is.True);
                Assert.That(PlayerPrefs.GetInt(key, 0), Is.EqualTo(1));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [UnityTest]
        public IEnumerator BootSceneUsesNativeXrUiInteractionStack()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            yield return null;

            XRUIInputModule uiInputModule = UnityEngine.Object.FindFirstObjectByType<XRUIInputModule>();
            XRInteractionManager interactionManager = UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();

            Assert.That(uiInputModule, Is.Not.Null, "EventSystem should use XRUIInputModule for tracked-device UI.");
            Assert.That(interactionManager, Is.Not.Null, "Scene should contain an XRInteractionManager.");
            Assert.That(uiInputModule.enableXRInput, Is.True);
            Assert.That(uiInputModule.enableMouseInput, Is.False);
            Assert.That(uiInputModule.enableTouchInput, Is.False);
            AssertUiActionReference(
                uiInputModule.leftClickAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiPress);
            AssertUiActionReference(
                uiInputModule.scrollWheelAction,
                BlockiverseInputActionNames.RightHandMap,
                BlockiverseInputActionNames.UiScroll);
            // Submit and Navigate must stay unbound: the ray interactor already turns UI Press
            // into a pointer click, and routing the same action through Submit fired the
            // auto-selected button twice per trigger pull (selector arrows advanced two options).
            // Found on uGUI, but the rule outlived it — UI Toolkit's panel event handler
            // dispatches submit the same way, so a bound Submit still double-fires.
            Assert.That(uiInputModule.submitAction, Is.Null,
                "UI Press must not also drive Submit or every click double-fires.");
            Assert.That(uiInputModule.navigateAction, Is.Null,
                "UI Scroll must not move focus-based selection under the ray.");
            EventSystem eventSystem = uiInputModule.GetComponent<EventSystem>();
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.sendNavigationEvents, Is.False,
                "Ray-driven UI must not dispatch navigation/submit events.");

            // Both controllers carry UI/block rays; the active dominant/tool hand owns visibility.
            GameObject rig = GameObject.Find(BlockiverseProject.XrRigRootName);
            foreach (string controllerName in new[] { "Left Controller", "Right Controller" })
            {
                Transform interactionRay = rig.transform.Find($"Camera Offset/{controllerName}/Interaction Ray");
                Assert.That(interactionRay, Is.Not.Null, controllerName);
                XRRayInteractor rayInteractor = interactionRay.GetComponent<XRRayInteractor>();
                Assert.That(rayInteractor, Is.Not.Null, controllerName);
                Assert.That(rayInteractor.enableUIInteraction, Is.True, controllerName);
                Assert.That(rayInteractor.blockUIOnInteractableSelection, Is.False, controllerName);
            }

            CreativeWorldManager worldManager = UnityEngine.Object.FindFirstObjectByType<CreativeWorldManager>(FindObjectsInactive.Include);
            Assert.That(worldManager, Is.Not.Null);
            // Since the M8.5 menu gate, Boot deliberately generates the explorable
            // title mini-world (InitializeDefaultWorldOnAwake); the session itself
            // stays inactive with world input blocked until Create/Load/Join.
            Assert.That(worldManager.World, Is.Not.Null, "Boot generates the title mini-world at startup.");
            Assert.That(BlockiverseRuntimeState.AllowWorldInput, Is.False,
                "World input stays blocked until a session grants it.");
        }

        static void AssertUiActionReference(InputActionReference reference, string expectedMap, string expectedAction)
        {
            Assert.That(reference, Is.Not.Null, $"{expectedAction} reference must be configured explicitly.");
            Assert.That(reference.action, Is.Not.Null);
            Assert.That(reference.action.actionMap?.name, Is.EqualTo(expectedMap));
            Assert.That(reference.action.name, Is.EqualTo(expectedAction));
        }

        // Walks the screen's live element tree rather than its controller state, so a screen whose
        // Refresh silently skipped a label cannot pass. Label and Button both derive from
        // TextElement, which is why the query is on the base type.
        static void AssertScreenRendersText(UiToolkitScreenController screen, string expectedText)
        {
            Assert.That(screen.Root, Is.Not.Null, $"{screen.GetType().Name} has no attached element tree.");

            bool found = false;
            foreach (TextElement element in screen.Root.Query<TextElement>().ToList())
            {
                if (element.text != null && element.text.Contains(expectedText))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True,
                $"Expected {screen.GetType().Name} to render text '{expectedText}'.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }
}
