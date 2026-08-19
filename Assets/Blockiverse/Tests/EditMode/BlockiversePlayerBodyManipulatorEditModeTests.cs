using Blockiverse.VR;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The player's collision size is a game rule, not a property of who is wearing the headset:
    // two blocks tall standing, one block crouched. XRI's stock body manipulator would resize
    // the capsule to the tracked camera height on every move, which broke both.
    public sealed class BlockiversePlayerBodyManipulatorEditModeTests
    {
        BlockiversePlayerBodyManipulator manipulator;

        [SetUp]
        public void SetUp()
        {
            manipulator = ScriptableObject.CreateInstance<BlockiversePlayerBodyManipulator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (manipulator != null)
                Object.DestroyImmediate(manipulator);
        }

        [Test]
        public void StandingCapsuleFitsATwoBlockOpening()
        {
            Assert.That(manipulator.CapsuleHeight, Is.LessThan(2.0f),
                "The player must fit through any two-block-tall opening.");
            Assert.That(manipulator.CapsuleHeight, Is.GreaterThan(1.0f),
                "A standing player is taller than one block, so one-block gaps stay blocked.");
        }

        [Test]
        public void CrouchingCapsuleFitsAOneBlockOpening()
        {
            manipulator.Crouching = true;

            Assert.That(manipulator.CapsuleHeight, Is.LessThan(1.0f),
                "Crouching must fit through a one-block opening.");
        }

        [Test]
        public void CapsuleHeightIsIndependentOfTrackedCameraHeight()
        {
            // The contract that regressed: capsule height comes from the game's player size,
            // so a 1.5 m player and a 2.1 m player occupy the same volume.
            float standing = manipulator.CapsuleHeight;
            manipulator.Crouching = true;
            float crouched = manipulator.CapsuleHeight;
            manipulator.Crouching = false;

            Assert.That(manipulator.CapsuleHeight, Is.EqualTo(standing));
            Assert.That(crouched, Is.LessThan(standing));
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                    useRealPlayerHeight: false, crouching: false, 1.8f, 0.9f, trackedHeight: 2.2f),
                Is.EqualTo(1.8f),
                "In the default mode the size depends only on the configured player size.");
        }

        [Test]
        public void RealHeightModeSizesTheCapsuleFromTheTrackedHeight()
        {
            manipulator.Configure(standingHeight: 1.8f, crouchHeight: 0.9f);

            // A short player fits where the fixed-size player would not have to duck...
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                    useRealPlayerHeight: true, crouching: false, 1.8f, 0.9f, trackedHeight: 1.4f),
                Is.EqualTo(1.4f).Within(1e-4f));

            // ...and a tall player is genuinely taller, so two-block gaps can block them.
            Assert.That(
                BlockiversePlayerBodyManipulatorTallHeight(),
                Is.GreaterThan(1.8f));

            // Physically ducking shrinks the capsule through the tracked height.
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                    useRealPlayerHeight: true, crouching: false, 1.8f, 0.9f, trackedHeight: 1.1f),
                Is.EqualTo(1.1f).Within(1e-4f));

            // The crouch toggle still works without kneeling, and never makes them taller.
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                    useRealPlayerHeight: true, crouching: true, 1.8f, 0.9f, trackedHeight: 2.0f),
                Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                    useRealPlayerHeight: true, crouching: true, 1.8f, 0.9f, trackedHeight: 0.6f),
                Is.EqualTo(0.6f).Within(1e-4f),
                "Already ducking below the crouch height stays at the tracked height.");
        }

        static float BlockiversePlayerBodyManipulatorTallHeight() =>
            BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(
                useRealPlayerHeight: true, crouching: false, 1.8f, 0.9f, trackedHeight: 2.0f);

        [Test]
        public void TrackedHeightIsClampedToASaneRange()
        {
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(true, false, 1.8f, 0.9f, trackedHeight: 0.0f),
                Is.EqualTo(BlockiversePlayerBodyManipulator.MinTrackedCapsuleHeight).Within(1e-4f),
                "Lost tracking must not collapse the capsule.");
            Assert.That(
                BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(true, false, 1.8f, 0.9f, trackedHeight: 9.0f),
                Is.EqualTo(BlockiversePlayerBodyManipulator.MaxTrackedCapsuleHeight).Within(1e-4f));
        }

        [Test]
        public void FixedModeIgnoresTrackedHeightEntirely()
        {
            foreach (float tracked in new[] { 0.5f, 1.4f, 1.75f, 2.2f })
            {
                Assert.That(
                    BlockiversePlayerBodyManipulator.ResolveCapsuleHeight(false, false, 1.8f, 0.9f, tracked),
                    Is.EqualTo(1.8f).Within(1e-4f),
                    "Default mode: everyone occupies the same volume.");
            }
        }

        [Test]
        public void ConfigureClampsCrouchBelowStandingAndKeepsPositiveHeights()
        {
            manipulator.Configure(standingHeight: 1.8f, crouchHeight: 2.5f);
            Assert.That(manipulator.CrouchCapsuleHeightMeters, Is.LessThanOrEqualTo(manipulator.StandingCapsuleHeightMeters),
                "Crouching can never make the player taller.");

            manipulator.Configure(standingHeight: -5.0f, crouchHeight: -5.0f);
            Assert.That(manipulator.CapsuleHeight, Is.GreaterThan(0.0f),
                "A degenerate capsule would break CharacterController.Move.");
        }
    }
}
