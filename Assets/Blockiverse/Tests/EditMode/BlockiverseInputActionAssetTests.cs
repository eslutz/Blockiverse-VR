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

            AssertPoseActions(asset);
            AssertControllerActions(asset, BlockiverseInputActionNames.LeftHandMap, "LeftHand");
            AssertControllerActions(asset, BlockiverseInputActionNames.RightHandMap, "RightHand");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, "<XRController>{LeftHand}/menuButton");
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.BlockEditingToggle);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Sprint);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Crouch);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Undo);
        }

        [Test]
        public void SwappableControlsUseControllerMaps()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            AssertControllerRoleActions(asset, BlockiverseInputActionNames.LeftHandMap, "LeftHand");
            AssertControllerRoleActions(asset, BlockiverseInputActionNames.RightHandMap, "RightHand");
            AssertNoAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Jump);
            AssertNoAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Jump);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Jump);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.BlockEditingToggle);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Sprint);
            AssertNoAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Crouch);
        }

        [Test]
        public void InputActionIdsAreDeterministicAndReferencesAreGenerated()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                BlockiverseProject.InputActionsAssetPath);

            Assert.That(asset, Is.Not.Null);

            foreach (InputActionMap map in asset.actionMaps)
            {
                Assert.That(map.id, Is.EqualTo(BlockiverseDeterministicInputIds.ForMap(map.name)),
                    $"{map.name} map id should be generated from the deterministic catalog.");

                foreach (InputAction action in map.actions)
                {
                    Assert.That(action.id, Is.EqualTo(BlockiverseDeterministicInputIds.ForAction(map.name, action.name)),
                        $"{map.name}/{action.name} action id should be generated from the deterministic catalog.");
                    AssertReferenceAsset(asset, map.name, action.name);
                }
            }
        }

        static void AssertPoseActions(InputActionAsset asset)
        {
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Position, "<XRHMD>/centerEyePosition");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.Rotation, "<XRHMD>/centerEyeRotation");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyePosition, "<XRHMD>/leftEyePosition");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.LeftEyeRotation, "<XRHMD>/leftEyeRotation");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyePosition, "<XRHMD>/rightEyePosition");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.RightEyeRotation, "<XRHMD>/rightEyeRotation");
            AssertAction(asset, BlockiverseInputActionNames.HeadMap, BlockiverseInputActionNames.TrackingState, "<XRHMD>/trackingState");
            AssertAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.AimPosition, "<XRController>{LeftHand}/pointerPosition");
            AssertAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.AimRotation, "<XRController>{LeftHand}/pointerRotation");
            AssertAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.AimPosition, "<XRController>{RightHand}/pointerPosition");
            AssertAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.AimRotation, "<XRController>{RightHand}/pointerRotation");
        }

        static void AssertReferenceAsset(InputActionAsset asset, string mapName, string actionName)
        {
            InputAction action = asset.FindActionMap(mapName).FindAction(actionName);
            string referencePath = BlockiverseInputActionReferencePaths.GetReferencePath(mapName, actionName);
            InputActionReference reference = AssetDatabase.LoadAssetAtPath<InputActionReference>(referencePath);

            Assert.That(reference, Is.Not.Null, $"Missing generated InputActionReference asset: {referencePath}");
            Assert.That(reference.action, Is.SameAs(action), $"{referencePath} should target {mapName}/{actionName}.");
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
            AssertControllerRoleActions(asset, mapName, handUsage);

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

        static void AssertControllerRoleActions(InputActionAsset asset, string mapName, string handUsage)
        {
            string controllerPath = $"<XRController>{{{handUsage}}}";

            AssertAction(asset, mapName, BlockiverseInputActionNames.PrimaryButton, $"{controllerPath}/primaryButton");
            AssertAction(asset, mapName, BlockiverseInputActionNames.SecondaryButton, $"{controllerPath}/secondaryButton");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Sprint, $"{controllerPath}/thumbstickClicked");
            AssertAction(asset, mapName, BlockiverseInputActionNames.Crouch, $"{controllerPath}/thumbstickClicked");
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
