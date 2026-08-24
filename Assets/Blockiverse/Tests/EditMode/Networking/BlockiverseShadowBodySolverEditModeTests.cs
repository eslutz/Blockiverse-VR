using Blockiverse.Networking;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.Networking.EditMode
{
    // The solver builds the shadow-only block body from the three tracked poses. These tests pin
    // the properties the shadow is FOR: it stands where the player stands, compresses when they
    // crouch, reaches where their hands reach, and never goes NaN on a degenerate pose.
    public sealed class BlockiverseShadowBodySolverEditModeTests
    {
        static Pose HeadAt(float x, float y, float z) => new(new Vector3(x, y, z), Quaternion.identity);
        static readonly Pose RestingLeftHand = new(new Vector3(-0.3f, 1.1f, 0.2f), Quaternion.identity);
        static readonly Pose RestingRightHand = new(new Vector3(0.3f, 1.1f, 0.2f), Quaternion.identity);

        [Test]
        public void StandingBodyRunsFromTheFloorToTheHead()
        {
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 1.62f, 0.0f), RestingLeftHand, RestingRightHand);

            float legBottom = layout.LeftLeg.LocalPosition.y - layout.LeftLeg.LocalScale.y * 0.5f;
            float torsoTop = layout.Torso.LocalPosition.y + layout.Torso.LocalScale.y * 0.5f;
            float legTop = layout.LeftLeg.LocalPosition.y + layout.LeftLeg.LocalScale.y * 0.5f;
            float torsoBottom = layout.Torso.LocalPosition.y - layout.Torso.LocalScale.y * 0.5f;

            Assert.That(legBottom, Is.EqualTo(0.0f).Within(0.001f), "Feet on the floor.");
            Assert.That(legTop, Is.EqualTo(torsoBottom).Within(0.001f), "Legs meet the torso at the hip.");
            Assert.That(torsoTop, Is.LessThan(1.62f), "The torso stops below the head centre.");
            Assert.That(layout.Head.LocalPosition, Is.EqualTo(new Vector3(0.0f, 1.62f, 0.0f)),
                "The head box sits exactly at the tracked head.");
        }

        [Test]
        public void TheBodyStandsWhereTheHeadIsNotAtTheOrigin()
        {
            // THE defect the shadow body replaces: the old capsule caster was pinned to the
            // play-space origin, so roomscale movement detached the shadow from the player.
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(2.0f, 1.62f, -1.5f), RestingLeftHand, RestingRightHand);

            Assert.That(layout.Torso.LocalPosition.x, Is.EqualTo(2.0f).Within(0.001f));
            Assert.That(layout.Torso.LocalPosition.z, Is.EqualTo(-1.5f).Within(0.001f));
            float legBottom = layout.RightLeg.LocalPosition.y - layout.RightLeg.LocalScale.y * 0.5f;
            Assert.That(legBottom, Is.EqualTo(0.0f).Within(0.001f), "Feet still on the floor after moving.");
        }

        [Test]
        public void CrouchingCompressesTorsoAndLegsTogether()
        {
            ShadowBodyLayout standing = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 1.62f, 0.0f), RestingLeftHand, RestingRightHand);
            ShadowBodyLayout crouched = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 0.9f, 0.0f), RestingLeftHand, RestingRightHand);

            Assert.That(crouched.Torso.LocalScale.y, Is.LessThan(standing.Torso.LocalScale.y));
            Assert.That(crouched.LeftLeg.LocalScale.y, Is.LessThan(standing.LeftLeg.LocalScale.y));
            float legBottom = crouched.LeftLeg.LocalPosition.y - crouched.LeftLeg.LocalScale.y * 0.5f;
            Assert.That(legBottom, Is.EqualTo(0.0f).Within(0.001f),
                "Crouching compresses the body; it must not push the legs through the floor.");
        }

        [Test]
        public void ArmsReachFromTheShouldersToTheHands()
        {
            var reachingHand = new Pose(new Vector3(0.8f, 1.5f, 0.6f), Quaternion.identity);
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 1.62f, 0.0f), RestingLeftHand, reachingHand);

            // The arm box's +Y end must land on the hand: centre + rotation * (0, len/2, 0).
            Vector3 armEnd = layout.RightArm.LocalPosition
                + layout.RightArm.LocalRotation * new Vector3(0.0f, layout.RightArm.LocalScale.y * 0.5f, 0.0f);
            Assert.That(Vector3.Distance(armEnd, reachingHand.position), Is.LessThan(0.01f),
                "The arm must end at the hand so a reach reads in the shadow.");
            Assert.That(layout.RightHand.LocalPosition, Is.EqualTo(reachingHand.position),
                "The hand box sits exactly at the tracked hand.");
        }

        [Test]
        public void BodyYawFollowsHeadYaw()
        {
            var turnedHead = new Pose(new Vector3(0.0f, 1.62f, 0.0f), Quaternion.Euler(0.0f, 90.0f, 0.0f));
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                turnedHead, RestingLeftHand, RestingRightHand);

            // Facing +X: the left leg's sideways offset must land on the -Z side (yaw-local -X).
            Vector3 legOffset = layout.LeftLeg.LocalPosition - new Vector3(
                layout.Torso.LocalPosition.x, layout.LeftLeg.LocalPosition.y, layout.Torso.LocalPosition.z);
            Assert.That(legOffset.z, Is.EqualTo(BlockiverseShadowBodySolver.LegSeparationMeters).Within(0.001f),
                "Turning the head yaws the whole body, legs included.");
            Assert.That(Mathf.Abs(legOffset.x), Is.LessThan(0.001f));
        }

        [Test]
        public void LookingStraightDownKeepsAnUprightFiniteBody()
        {
            // The facing projection degenerates when the head pitches to a pole; the body must
            // stay upright and finite rather than collapsing or going NaN.
            var lookingDown = new Pose(new Vector3(0.0f, 1.62f, 0.0f), Quaternion.Euler(90.0f, 0.0f, 0.0f));
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                lookingDown, RestingLeftHand, RestingRightHand);

            Vector3 torsoUp = layout.Torso.LocalRotation * Vector3.up;
            Assert.That(torsoUp.y, Is.EqualTo(1.0f).Within(0.001f), "The torso stays vertical.");
            AssertFinite(layout.Torso);
            AssertFinite(layout.LeftArm);
            AssertFinite(layout.LeftLeg);
        }

        [Test]
        public void AHandAtTheShoulderStillYieldsAFiniteArm()
        {
            // Zero-length arm segment: the direction is undefined and a zero scale would make the
            // box's inverse transforms NaN in the shadow pass.
            float shoulderY = 1.62f - BlockiverseShadowBodySolver.HeadHalfHeightMeters
                - BlockiverseShadowBodySolver.NeckGapMeters - BlockiverseShadowBodySolver.ShoulderDropMeters;
            var handAtShoulder = new Pose(
                new Vector3(-BlockiverseShadowBodySolver.ShoulderHalfSpanMeters, shoulderY, 0.0f),
                Quaternion.identity);

            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 1.62f, 0.0f), handAtShoulder, RestingRightHand);

            Assert.That(layout.LeftArm.LocalScale.y,
                Is.GreaterThanOrEqualTo(BlockiverseShadowBodySolver.MinimumSegmentMeters));
            AssertFinite(layout.LeftArm);
        }

        [Test]
        public void AHeadOnTheFloorStillYieldsAFiniteBody()
        {
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 0.05f, 0.0f), RestingLeftHand, RestingRightHand);

            AssertFinite(layout.Torso);
            AssertFinite(layout.LeftLeg);
            Assert.That(layout.Torso.LocalScale.y, Is.GreaterThan(0.0f));
            Assert.That(layout.LeftLeg.LocalScale.y, Is.GreaterThan(0.0f));
        }

        [Test]
        public void ArmsAreThickEnoughToReadAsRectanglesInTheShadow()
        {
            // Eric's report (2026-08-24): the arm shadow read as a thin line, not a rectangular
            // arm. Root cause was the arm's cross-section, not a geometry bug — 0.12 m was both
            // thinner than the hand it connects to AND close to the shadow-map's texel size, so
            // pinning the cross-section against known-legible parts is what guards this: an arm
            // narrower than the hand it meets, or no meaningfully thicker than the old value,
            // would silently reopen exactly this report.
            ShadowBodyLayout layout = BlockiverseShadowBodySolver.Solve(
                HeadAt(0.0f, 1.62f, 0.0f), RestingLeftHand, RestingRightHand);

            Assert.That(BlockiverseShadowBodySolver.ArmThicknessMeters,
                Is.GreaterThanOrEqualTo(BlockiverseShadowBodySolver.HandScale.x),
                "An arm thinner than the hand it connects to looks disjointed, not rectangular.");
            Assert.That(BlockiverseShadowBodySolver.ArmThicknessMeters, Is.GreaterThan(0.12f),
                "Must be a real fix over the original value that read as a line, not a fresh guess.");
            Assert.That(BlockiverseShadowBodySolver.ArmThicknessMeters,
                Is.LessThan(BlockiverseShadowBodySolver.TorsoDepthMeters),
                "The torso must stay the widest part of the silhouette.");

            Assert.That(layout.LeftArm.LocalScale.x, Is.EqualTo(BlockiverseShadowBodySolver.ArmThicknessMeters));
            Assert.That(layout.LeftArm.LocalScale.z, Is.EqualTo(BlockiverseShadowBodySolver.ArmThicknessMeters));
        }

        static void AssertFinite(ShadowBodyPart part)
        {
            Assert.That(float.IsFinite(part.LocalPosition.x) && float.IsFinite(part.LocalPosition.y)
                && float.IsFinite(part.LocalPosition.z), "Position must be finite.");
            Assert.That(float.IsFinite(part.LocalScale.x) && float.IsFinite(part.LocalScale.y)
                && float.IsFinite(part.LocalScale.z), "Scale must be finite.");
            Assert.That(part.LocalScale.x > 0.0f && part.LocalScale.y > 0.0f && part.LocalScale.z > 0.0f,
                "Scale must be strictly positive.");
        }
    }
}
