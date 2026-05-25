using Blockiverse.Core;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseInputActionAssetTests
    {
        [Test]
        public void InputActionAssetContainsM1ActionMaps()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.FindActionMap(BlockiverseInputActionNames.LeftHandMap), Is.Not.Null);
            Assert.That(asset.FindActionMap(BlockiverseInputActionNames.RightHandMap), Is.Not.Null);
            Assert.That(asset.FindActionMap(BlockiverseInputActionNames.GameplayMap), Is.Not.Null);
        }

        [Test]
        public void ControllerActionsContainQuestBindings()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            AssertControllerActions(asset, BlockiverseInputActionNames.LeftHandMap, "LeftHand");
            AssertControllerActions(asset, BlockiverseInputActionNames.RightHandMap, "RightHand");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, "<XRController>{LeftHand}/menuButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.HeightReset, "<XRController>{LeftHand}/primaryButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo, "<XRController>{LeftHand}/secondaryButton");
        }

        static void AssertControllerActions(InputActionAsset asset, string mapName, string handUsage)
        {
            string controllerPath = $"<XRController>{{{handUsage}}}";

            AssertAction(asset, mapName, BlockiverseInputActionNames.Position, $"{controllerPath}/devicePosition");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Rotation, $"{controllerPath}/deviceRotation");
            AssertAction(asset, mapName, BlockiverseInputActionNames.IsTracked, $"{controllerPath}/isTracked");
            AssertAction(asset, mapName, BlockiverseInputActionNames.TrackingState, $"{controllerPath}/trackingState");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Select, $"{controllerPath}/triggerPressed");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Activate, $"{controllerPath}/gripPressed");
            AssertAction(asset, mapName, BlockiverseInputActionNames.UiPress, $"{controllerPath}/triggerPressed");
            AssertAction(asset, mapName, BlockiverseInputActionNames.UiScroll, $"{controllerPath}/thumbstick");
            AssertAction(asset, mapName, BlockiverseInputActionNames.HapticDevice, $"{controllerPath}/*");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Move, $"{controllerPath}/thumbstick");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Turn, $"{controllerPath}/thumbstick");
            AssertAction(asset, mapName, BlockiverseInputActionNames.TeleportMode, $"{controllerPath}/primaryButton");
            AssertAction(asset, mapName, BlockiverseInputActionNames.TeleportSelect, $"{controllerPath}/triggerPressed");
        }

        static void AssertAction(InputActionAsset asset, string mapName, string actionName, string expectedPath)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);

            Assert.That(action, Is.Not.Null, $"{mapName}/{actionName}");
            Assert.That(action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath));
        }
    }
}
