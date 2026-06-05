using System.Linq;
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
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump, "<XRController>{RightHand}/primaryButton");
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo);
            AssertNoBindingPath(asset, "<XRController>{LeftHand}/primaryButton", "Left X is intentionally unassigned.");
            AssertNoBindingPath(asset, "<XRController>{LeftHand}/secondaryButton", "Left Y is intentionally unassigned.");
            AssertNoBindingPath(asset, "<XRController>{RightHand}/secondaryButton", "Right B is intentionally unassigned.");
        }

        [Test]
        public void JumpUsesRightAAndHasNoLegacyControllerBindings()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            InputAction rightHandJump = asset.FindActionMap(BlockiverseInputActionNames.RightHandMap)
                .FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false);

            Assert.That(rightHandJump, Is.Null, "RightHand/Jump is stale; JumpProvider reads Blockiverse Gameplay/Jump.");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap,
                BlockiverseInputActionNames.Jump, "<XRController>{RightHand}/primaryButton");
            AssertNoBindingPath(asset, "<XRController>{LeftHand}/primaryButton", "Jump must no longer be on Left X.");
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
            // Teleport Mode and Teleport Select are now thumbstick-up composites (push forward to
            // aim, release to teleport), not trigger or A button.
            AssertActionContainsPath(asset, mapName, BlockiverseInputActionNames.TeleportMode, "thumbstick");
            AssertActionContainsPath(asset, mapName, BlockiverseInputActionNames.TeleportSelect, "thumbstick");
            AssertActionDoesNotContainPath(asset, mapName, BlockiverseInputActionNames.TeleportMode, "primaryButton");
            AssertActionDoesNotContainPath(asset, mapName, BlockiverseInputActionNames.TeleportSelect, "triggerPressed");
        }

        static void AssertAction(InputActionAsset asset, string mapName, string actionName, string expectedPath)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);

            Assert.That(action, Is.Not.Null, $"{mapName}/{actionName}");
            Assert.That(action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath),
                $"{mapName}/{actionName} binding not found: {expectedPath}");
        }

        static void AssertActionContainsPath(InputActionAsset asset, string mapName, string actionName, string pathSubstring)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);

            Assert.That(action, Is.Not.Null, $"{mapName}/{actionName}");
            Assert.That(action.bindings, Has.Some.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "").Contains(pathSubstring)),
                $"{mapName}/{actionName} should have a binding containing '{pathSubstring}'");
        }

        static void AssertActionDoesNotContainPath(InputActionAsset asset, string mapName, string actionName, string pathSubstring)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);

            Assert.That(action, Is.Not.Null, $"{mapName}/{actionName}");
            Assert.That(action.bindings, Has.None.Matches<InputBinding>(b =>
                (b.effectivePath ?? b.path ?? "").Contains(pathSubstring)),
                $"{mapName}/{actionName} should not have a binding containing '{pathSubstring}'");
        }

        static void AssertNoAction(InputActionAsset asset, string mapName, string actionName)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName, throwIfNotFound: false);

            Assert.That(action, Is.Null, $"{mapName}/{actionName} should not exist.");
        }

        static void AssertNoBindingPath(InputActionAsset asset, string expectedPath, string message)
        {
            Assert.That(asset.actionMaps.SelectMany(map => map.bindings), Has.None.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath || binding.path == expectedPath),
                message);
        }
    }
}
