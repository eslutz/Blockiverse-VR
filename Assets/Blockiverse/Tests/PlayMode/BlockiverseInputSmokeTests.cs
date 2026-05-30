using System.Collections;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseInputSmokeTests
    {
        const string BootSceneName = "Boot";

        [UnityTest]
        public IEnumerator BootSceneEnablesInputRig()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            BlockiverseInputRig inputRig = Object.FindFirstObjectByType<BlockiverseInputRig>();
            Assert.That(inputRig, Is.Not.Null);
            Assert.That(inputRig.InputActions, Is.Not.Null);
            Assert.That(inputRig.InputActions.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator BootSceneHasTrackedControllerAnchors()
        {
            yield return BlockiversePlayModeSceneTestUtility.LoadSceneSingle(BootSceneName);

            BlockiverseControllerAnchor[] anchors = Object.FindObjectsByType<BlockiverseControllerAnchor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            Assert.That(anchors, Has.Length.EqualTo(2));
            Assert.That(anchors, Has.Some.Matches<BlockiverseControllerAnchor>(anchor => anchor.Role == BlockiverseControllerRole.Left));
            Assert.That(anchors, Has.Some.Matches<BlockiverseControllerAnchor>(anchor => anchor.Role == BlockiverseControllerRole.Right));
        }

        [UnityTest]
        public IEnumerator ControllerAnchorPreservesFallbackPoseWhenUntracked()
        {
            var inputActions = ScriptableObject.CreateInstance<InputActionAsset>();
            GameObject rigObject = new("Test Input Rig");
            GameObject controllerObject = new("Left Controller");

            try
            {
                InputActionMap leftHandMap = inputActions.AddActionMap(BlockiverseInputActionNames.LeftHandMap);
                leftHandMap.AddAction(BlockiverseInputActionNames.Position, InputActionType.PassThrough, expectedControlLayout: "Vector3");
                leftHandMap.AddAction(BlockiverseInputActionNames.Rotation, InputActionType.PassThrough, expectedControlLayout: "Quaternion");
                leftHandMap.AddAction(BlockiverseInputActionNames.IsTracked, InputActionType.Button);

                BlockiverseInputRig inputRig = rigObject.AddComponent<BlockiverseInputRig>();
                inputRig.Configure(inputActions);

                Vector3 fallbackPosition = new(1.0f, 2.0f, 3.0f);
                Quaternion fallbackRotation = Quaternion.Euler(10.0f, 20.0f, 30.0f);
                controllerObject.transform.localPosition = fallbackPosition;
                controllerObject.transform.localRotation = fallbackRotation;

                BlockiverseControllerAnchor anchor = controllerObject.AddComponent<BlockiverseControllerAnchor>();
                anchor.Configure(BlockiverseControllerRole.Left);

                yield return null;

                // The anchor is a passive role marker; without a TrackedPoseDriver it never moves
                // the controller transform, so the authored fallback pose is preserved.
                Assert.That(anchor.Role, Is.EqualTo(BlockiverseControllerRole.Left));
                Assert.That(Vector3.Distance(controllerObject.transform.localPosition, fallbackPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Dot(controllerObject.transform.localRotation, fallbackRotation), Is.GreaterThan(0.9999f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(inputActions);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return BlockiversePlayModeSceneTestUtility.CleanupTrackedPoseDrivers();
        }
    }
}
