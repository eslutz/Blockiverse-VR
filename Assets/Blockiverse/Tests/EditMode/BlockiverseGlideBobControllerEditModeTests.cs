using System.Collections.Generic;
using Blockiverse.VR;
using Blockiverse.Core;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseGlideBobControllerEditModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
        }

        [Test]
        public void StationaryReturnsZeroBobOffset()
        {
            var (rig, settings, origin, bob) = CreateTestStack();

            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = GlideStyle.Bobbing;

            // Simulate stationary (speed = 0)
            bob.SpeedOverride = () => 0.0f;

            // Trigger LateUpdate manually several times
            for (int i = 0; i < 5; i++)
            {
                RunLateUpdate(bob);
            }

            // Camera offset Y should remain unchanged (base is 0 in this test)
            Assert.That(origin.CameraFloorOffsetObject.transform.localPosition.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingIsZeroWhenStyleIsSmooth()
        {
            var (rig, settings, origin, bob) = CreateTestStack();

            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = GlideStyle.Smooth; // Smooth movement, no bobbing

            bob.SpeedOverride = () => 1.5f; // Moving

            for (int i = 0; i < 5; i++)
            {
                RunLateUpdate(bob);
            }

            Assert.That(origin.CameraFloorOffsetObject.transform.localPosition.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingAppliesVerticalOffsetWhenMovingAndEnabled()
        {
            var (rig, settings, origin, bob) = CreateTestStack();

            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = GlideStyle.Bobbing;

            bob.SpeedOverride = () => 1.5f; // Moving at 1.5 m/s

            // Run one update to advance phase and apply bob
            RunLateUpdate(bob);

            float localPosY = origin.CameraFloorOffsetObject.transform.localPosition.y;
            Assert.That(localPosY, Is.Not.EqualTo(0f));
            Assert.That(Mathf.Abs(localPosY), Is.LessThanOrEqualTo(1.5f * bob.Amplitude + 0.01f));
        }

        [Test]
        public void BobbingOffsetDecaysToZeroWhenStopping()
        {
            var (rig, settings, origin, bob) = CreateTestStack();

            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = GlideStyle.Bobbing;

            // First, move to generate non-zero bob
            bob.SpeedOverride = () => 1.5f;
            RunLateUpdate(bob);
            float movingY = origin.CameraFloorOffsetObject.transform.localPosition.y;
            Assert.That(movingY, Is.Not.EqualTo(0f));

            // Stop moving
            bob.SpeedOverride = () => 0.0f;

            // Run multiple updates to let it decay
            for (int i = 0; i < 50; i++)
            {
                RunLateUpdate(bob);
            }

            float stoppedY = origin.CameraFloorOffsetObject.transform.localPosition.y;
            Assert.That(stoppedY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ExternalHeightModificationIsHandledWithoutDrift()
        {
            var (rig, settings, origin, bob) = CreateTestStack();

            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = GlideStyle.Bobbing;

            // Move to apply non-zero bob offset
            bob.SpeedOverride = () => 1.5f;
            RunLateUpdate(bob);
            float bobYBefore = origin.CameraFloorOffsetObject.transform.localPosition.y;
            Assert.That(bobYBefore, Is.Not.EqualTo(0f));

            // Simulate external change to Camera Offset Y (e.g., from height reset)
            Transform cameraOffset = origin.CameraFloorOffsetObject.transform;
            cameraOffset.localPosition = new Vector3(0, 1.6f, 0); // Override directly to a clean base height

            // Run update - should detect external change, reset lastAppliedBobY to 0, and apply bob on top of 1.6f
            RunLateUpdate(bob);

            float bobYAfter = cameraOffset.localPosition.y;
            Assert.That(bobYAfter, Is.Not.EqualTo(1.6f));
            Assert.That(bobYAfter, Is.Not.EqualTo(bobYBefore));
            Assert.That(bobYAfter, Is.GreaterThan(1.5f).And.LessThan(1.7f));
        }

        (GameObject rig, BlockiverseComfortSettings settings, XROrigin origin, BlockiverseGlideBobController bob) CreateTestStack()
        {
            GameObject rig = CreateObject("Test Rig");
            BlockiverseComfortSettings settings = rig.AddComponent<BlockiverseComfortSettings>();
            XROrigin origin = rig.AddComponent<XROrigin>();

            GameObject cameraOffset = CreateObject("Camera Offset");
            cameraOffset.transform.SetParent(rig.transform, false);
            origin.CameraFloorOffsetObject = cameraOffset;

            BlockiverseGlideBobController bob = rig.AddComponent<BlockiverseGlideBobController>();

            // Manually run Awake to bind references in EditMode
            var awakeMethod = typeof(BlockiverseGlideBobController).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(awakeMethod, Is.Not.Null, "Awake method not found via reflection.");
            awakeMethod.Invoke(bob, null);

            return (rig, settings, origin, bob);
        }

        GameObject CreateObject(string name)
        {
            GameObject target = new(name);
            objectsToDestroy.Add(target);
            return target;
        }

        void RunLateUpdate(BlockiverseGlideBobController bob)
        {
            // Use reflection to call LateUpdate since it is private
            var method = typeof(BlockiverseGlideBobController).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, "LateUpdate method not found via reflection.");
            method.Invoke(bob, null);
        }
    }
}
