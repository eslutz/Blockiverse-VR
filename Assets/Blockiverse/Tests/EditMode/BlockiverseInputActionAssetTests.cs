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
        const string StickDeadzoneProcessor = "stickDeadzone(min=0.2,max=0.95)";

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

            AssertControllerActions(asset, BlockiverseInputActionNames.LeftHandMap, "LeftHand", expectFaceButtons: false);
            AssertControllerActions(asset, BlockiverseInputActionNames.RightHandMap, "RightHand", expectFaceButtons: true);
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, "<XRController>{LeftHand}/menuButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump, "<XRController>{RightHand}/primaryButton");
            AssertActionDoesNotContainPath(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump, "<XRController>{LeftHand}/primaryButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.BlockEditingToggle, "<XRController>{RightHand}/secondaryButton");
            AssertActionDoesNotContainPath(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.BlockEditingToggle, "<XRController>{LeftHand}/secondaryButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Sprint, "<XRController>{LeftHand}/thumbstickClicked");
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo);
        }

        [Test]
        public void JumpUsesGameplayMapAndHasNoLegacyControllerBindings()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            InputAction rightHandJump = asset.FindActionMap(BlockiverseInputActionNames.RightHandMap)
                .FindAction(BlockiverseInputActionNames.Jump, throwIfNotFound: false);

            Assert.That(rightHandJump, Is.Null, "RightHand/Jump is stale; JumpProvider reads Blockiverse Gameplay/Jump.");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap,
                BlockiverseInputActionNames.Jump, "<XRController>{RightHand}/primaryButton");
            AssertActionDoesNotContainPath(asset, BlockiverseInputActionNames.GameplayMap,
                BlockiverseInputActionNames.Jump, "<XRController>{LeftHand}/primaryButton");
        }

        static void AssertControllerActions(InputActionAsset asset, string mapName, string handUsage, bool expectFaceButtons)
        {
            string controllerPath = $"<XRController>{{{handUsage}}}";

            AssertAction(asset, mapName, BlockiverseInputActionNames.Position, $"{controllerPath}/devicePosition");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Rotation, $"{controllerPath}/deviceRotation");
            AssertAction(asset, mapName, BlockiverseInputActionNames.IsTracked, $"{controllerPath}/isTracked");
            AssertAction(asset, mapName, BlockiverseInputActionNames.TrackingState, $"{controllerPath}/trackingState");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Select, $"{controllerPath}/triggerPressed");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Activate, $"{controllerPath}/gripPressed");
            if (expectFaceButtons)
            {
                AssertAction(asset, mapName, BlockiverseInputActionNames.PrimaryButton, $"{controllerPath}/primaryButton");
                AssertAction(asset, mapName, BlockiverseInputActionNames.SecondaryButton, $"{controllerPath}/secondaryButton");
            }
            else
            {
                AssertNoAction(asset, mapName, BlockiverseInputActionNames.PrimaryButton);
                AssertNoAction(asset, mapName, BlockiverseInputActionNames.SecondaryButton);
            }

            AssertAction(asset, mapName, BlockiverseInputActionNames.UiPress, $"{controllerPath}/triggerPressed");
            AssertAction(asset, mapName, BlockiverseInputActionNames.UiScroll, $"{controllerPath}/thumbstick");
            AssertAction(asset, mapName, BlockiverseInputActionNames.HapticDevice, $"{controllerPath}/*");
            AssertActionWithProcessor(asset, mapName, BlockiverseInputActionNames.Move, $"{controllerPath}/thumbstick", StickDeadzoneProcessor);
            AssertActionWithProcessor(asset, mapName, BlockiverseInputActionNames.Turn, $"{controllerPath}/thumbstick", StickDeadzoneProcessor);
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

        static void AssertActionWithProcessor(
            InputActionAsset asset,
            string mapName,
            string actionName,
            string expectedPath,
            string expectedProcessor)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);

            Assert.That(action, Is.Not.Null, $"{mapName}/{actionName}");
            Assert.That(action.bindings, Has.Some.Matches<InputBinding>(
                binding => binding.effectivePath == expectedPath &&
                    binding.processors == expectedProcessor),
                $"{mapName}/{actionName} deadzoned binding not found: {expectedPath} processor={expectedProcessor}");
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
