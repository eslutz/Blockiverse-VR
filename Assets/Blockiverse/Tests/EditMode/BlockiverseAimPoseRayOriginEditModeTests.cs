using Blockiverse.Core;
using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR;

namespace Blockiverse.Tests.EditMode
{
    // The controller transform rides the OpenXR grip pose; the ray must ride the aim pose, which is
    // where Meta's own system pointer originates. These pin the grip-local offset math the runtime
    // component applies each frame, independent of any tracked device.
    public sealed class BlockiverseAimPoseRayOriginEditModeTests
    {
        const InputTrackingState FullyTracked = InputTrackingState.Position | InputTrackingState.Rotation;

        [Test]
        public void ResolvesAimPoseInGripLocalSpace()
        {
            // A Touch-style controller: grip pitched up the handle, aim pitched ~60° below it and
            // offset forward of the grip position.
            Quaternion gripRotation = Quaternion.Euler(-30.0f, 45.0f, 10.0f);
            Vector3 gripPosition = new(0.3f, 1.1f, 0.4f);
            Quaternion expectedLocalRotation = Quaternion.Euler(60.0f, 0.0f, 0.0f);
            Vector3 expectedLocalPosition = new(0.0f, 0.02f, 0.08f);

            Quaternion aimRotation = gripRotation * expectedLocalRotation;
            Vector3 aimPosition = gripPosition + gripRotation * expectedLocalPosition;

            bool resolved = BlockiverseAimPoseRayOrigin.TryResolveLocalOffset(
                gripPosition, gripRotation, FullyTracked,
                aimPosition, aimRotation, FullyTracked,
                out Vector3 localPosition, out Quaternion localRotation);

            Assert.That(resolved, Is.True);
            Assert.That(Quaternion.Angle(localRotation, expectedLocalRotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(localPosition, expectedLocalPosition), Is.LessThan(0.0001f));
        }

        [Test]
        public void OffsetIsInvariantToWhereTheControllerIsHeld()
        {
            // Grip and aim are two poses of one rigid controller, so the grip-local offset must not
            // change as the controller moves — that invariance is what makes applying it in local
            // space jitter-free regardless of update order.
            Quaternion offsetRotation = Quaternion.Euler(58.0f, 2.0f, -1.0f);
            Vector3 offsetPosition = new(0.005f, 0.03f, 0.06f);

            Quaternion firstGrip = Quaternion.Euler(-20.0f, 0.0f, 0.0f);
            Quaternion secondGrip = Quaternion.Euler(15.0f, 120.0f, -35.0f);
            Vector3 firstGripPosition = new(0.0f, 1.0f, 0.0f);
            Vector3 secondGripPosition = new(-0.4f, 1.3f, 0.7f);

            BlockiverseAimPoseRayOrigin.TryResolveLocalOffset(
                firstGripPosition, firstGrip, FullyTracked,
                firstGripPosition + firstGrip * offsetPosition, firstGrip * offsetRotation, FullyTracked,
                out Vector3 firstLocalPosition, out Quaternion firstLocalRotation);
            BlockiverseAimPoseRayOrigin.TryResolveLocalOffset(
                secondGripPosition, secondGrip, FullyTracked,
                secondGripPosition + secondGrip * offsetPosition, secondGrip * offsetRotation, FullyTracked,
                out Vector3 secondLocalPosition, out Quaternion secondLocalRotation);

            Assert.That(Quaternion.Angle(firstLocalRotation, secondLocalRotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(firstLocalPosition, secondLocalPosition), Is.LessThan(0.0001f));
        }

        [TestCase(InputTrackingState.None, InputTrackingState.None)]
        [TestCase(InputTrackingState.Position, InputTrackingState.Position | InputTrackingState.Rotation)]
        [TestCase(InputTrackingState.Position | InputTrackingState.Rotation, InputTrackingState.Rotation)]
        public void UntrackedPosesFallBackToTheFixedOffset(InputTrackingState gripState, InputTrackingState aimState)
        {
            bool resolved = BlockiverseAimPoseRayOrigin.TryResolveLocalOffset(
                Vector3.one, Quaternion.Euler(10.0f, 20.0f, 30.0f), gripState,
                Vector3.zero, Quaternion.identity, aimState,
                out Vector3 localPosition, out Quaternion localRotation);

            Assert.That(resolved, Is.False, "A ray must never follow an untracked aim pose.");
            Assert.That(localPosition, Is.EqualTo(BlockiverseAimPoseRayOrigin.FallbackLocalPosition));
            Assert.That(Quaternion.Angle(localRotation, BlockiverseAimPoseRayOrigin.FallbackLocalRotation), Is.LessThan(0.001f));
        }

        [Test]
        public void DegenerateRotationsFallBackInsteadOfProducingNaN()
        {
            bool resolved = BlockiverseAimPoseRayOrigin.TryResolveLocalOffset(
                Vector3.zero, new Quaternion(0.0f, 0.0f, 0.0f, 0.0f), FullyTracked,
                Vector3.zero, Quaternion.identity, FullyTracked,
                out Vector3 localPosition, out Quaternion localRotation);

            Assert.That(resolved, Is.False);
            Assert.That(float.IsNaN(localRotation.w), Is.False);
            Assert.That(localPosition, Is.EqualTo(BlockiverseAimPoseRayOrigin.FallbackLocalPosition));
        }

        [Test]
        public void FallbackOffsetMirrorsAcrossHandsAndPitchesSixtyDegrees()
        {
            Quaternion right = BlockiverseAimPoseRayOrigin.ResolveFallbackLocalRotation(BlockiverseControllerRole.Right);
            Quaternion left = BlockiverseAimPoseRayOrigin.ResolveFallbackLocalRotation(BlockiverseControllerRole.Left);

            // Measured grip->aim pitch on Touch controllers is ~60°, not the 90° the old fixed origin used.
            Assert.That(Quaternion.Angle(right, Quaternion.identity), Is.EqualTo(60.0f).Within(1.0f));
            Assert.That(
                Quaternion.Angle(right, Quaternion.Euler(90.0f, 0.0f, 0.0f)),
                Is.GreaterThan(25.0f),
                "The fallback must not regress to the old 90° pitch that put the ray ~30° below the aim pose.");

            // Left mirrors right across the controller's X axis: same pitch, opposite yaw/roll.
            Vector3 rightForward = right * Vector3.forward;
            Vector3 leftForward = left * Vector3.forward;
            Assert.That(leftForward.x, Is.EqualTo(-rightForward.x).Within(0.0001f));
            Assert.That(leftForward.y, Is.EqualTo(rightForward.y).Within(0.0001f));
            Assert.That(leftForward.z, Is.EqualTo(rightForward.z).Within(0.0001f));

            Vector3 rightPosition = BlockiverseAimPoseRayOrigin.ResolveFallbackLocalPosition(BlockiverseControllerRole.Right);
            Vector3 leftPosition = BlockiverseAimPoseRayOrigin.ResolveFallbackLocalPosition(BlockiverseControllerRole.Left);
            Assert.That(leftPosition, Is.EqualTo(new Vector3(-rightPosition.x, rightPosition.y, rightPosition.z)));
        }

        [Test]
        public void HandUsageFollowsTheControllerRole()
        {
            Assert.That(BlockiverseAimPoseRayOrigin.ResolveHandUsage(BlockiverseControllerRole.Left), Is.EqualTo("LeftHand"));
            Assert.That(BlockiverseAimPoseRayOrigin.ResolveHandUsage(BlockiverseControllerRole.Right), Is.EqualTo("RightHand"));
        }
    }
}
