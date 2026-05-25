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

            AssertAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Position, "<XRController>{LeftHand}/devicePosition");
            AssertAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Rotation, "<XRController>{LeftHand}/deviceRotation");
            AssertAction(asset, BlockiverseInputActionNames.LeftHandMap, BlockiverseInputActionNames.Select, "<XRController>{LeftHand}/triggerPressed");
            AssertAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Position, "<XRController>{RightHand}/devicePosition");
            AssertAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Rotation, "<XRController>{RightHand}/deviceRotation");
            AssertAction(asset, BlockiverseInputActionNames.RightHandMap, BlockiverseInputActionNames.Select, "<XRController>{RightHand}/triggerPressed");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.Menu, "<XRController>{LeftHand}/menuButton");
            AssertAction(asset, BlockiverseInputActionNames.GameplayMap, BlockiverseInputActionNames.HeightReset, "<XRController>{LeftHand}/primaryButton");
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
