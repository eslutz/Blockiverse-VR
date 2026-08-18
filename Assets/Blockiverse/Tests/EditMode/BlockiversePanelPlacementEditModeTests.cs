using Blockiverse.UI;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // Placement contracts for routed world-space menus: title menus are world fixtures
    // derived from spawn (never the headset); in-session menus lazily follow the player
    // with hysteresis, height-locked and un-pitched.
    public sealed class BlockiversePanelPlacementEditModeTests
    {
        [Test]
        public void SpawnRelativePoseIsInFrontOfSpawnAlongSpawnYawAtFixedHeight()
        {
            var spawnBase = new Vector3(10.5f, 64.0f, 20.5f);

            Pose facingNorth = BlockiversePanelPlacement.SpawnRelativePose(spawnBase, 0.0f, 2.0f, 1.4f);
            Assert.That(facingNorth.position.x, Is.EqualTo(10.5f).Within(1e-4f));
            Assert.That(facingNorth.position.y, Is.EqualTo(65.4f).Within(1e-4f));
            Assert.That(facingNorth.position.z, Is.EqualTo(22.5f).Within(1e-4f));
            Assert.That(Vector3.Angle(facingNorth.rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));

            Pose facingEast = BlockiversePanelPlacement.SpawnRelativePose(spawnBase, 90.0f, 2.0f, 1.4f);
            Assert.That(facingEast.position.x, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(facingEast.position.z, Is.EqualTo(20.5f).Within(1e-4f));
            Assert.That(Vector3.Angle(facingEast.rotation * Vector3.forward, Vector3.right), Is.LessThan(0.01f));
        }

        [Test]
        public void SpawnRelativePoseNeverPitches()
        {
            Pose pose = BlockiversePanelPlacement.SpawnRelativePose(Vector3.zero, 37.0f, 2.0f, 1.4f);
            Vector3 forward = pose.rotation * Vector3.forward;
            Assert.That(forward.y, Is.EqualTo(0.0f).Within(1e-4f), "World-fixed menus stand upright.");
        }

        [Test]
        public void FollowTargetIsAheadOfHeadAtHeadHeightIgnoringHeadPitch()
        {
            var head = new Vector3(3.0f, 1.7f, -2.0f);
            // Looking steeply down: placement must still be level and ahead.
            Vector3 lookingDown = (Quaternion.Euler(60.0f, 0.0f, 0.0f) * Vector3.forward);

            Pose pose = BlockiversePanelPlacement.FollowTargetPose(head, lookingDown, 1.2f, 0.0f, -0.1f);

            Assert.That(pose.position.x, Is.EqualTo(3.0f).Within(1e-4f));
            Assert.That(pose.position.y, Is.EqualTo(1.6f).Within(1e-4f), "Height locks to head height plus offset.");
            Assert.That(pose.position.z, Is.EqualTo(-0.8f).Within(1e-4f));
            Assert.That((pose.rotation * Vector3.forward).y, Is.EqualTo(0.0f).Within(1e-4f), "No pitch.");
        }

        [Test]
        public void ShouldRecenterOnlyPastYawOrDistanceThreshold()
        {
            var head = new Vector3(0.0f, 1.7f, 0.0f);
            Pose panel = BlockiversePanelPlacement.FollowTargetPose(head, Vector3.forward, 1.2f, 0.0f, 0.0f);

            // Small head turn inside the cone: stay put.
            Vector3 smallTurn = Quaternion.Euler(0.0f, 20.0f, 0.0f) * Vector3.forward;
            Assert.That(BlockiversePanelPlacement.ShouldRecenter(panel, head, smallTurn, 1.2f, 30.0f, 1.5f), Is.False);

            // Large head turn: re-center.
            Vector3 bigTurn = Quaternion.Euler(0.0f, 45.0f, 0.0f) * Vector3.forward;
            Assert.That(BlockiversePanelPlacement.ShouldRecenter(panel, head, bigTurn, 1.2f, 30.0f, 1.5f), Is.True);

            // Walking away past the distance threshold with the same heading: re-center.
            var farHead = head + new Vector3(0.0f, 0.0f, -2.0f);
            Assert.That(BlockiversePanelPlacement.ShouldRecenter(panel, farHead, Vector3.forward, 1.2f, 30.0f, 1.5f), Is.True);

            // Small step: stay put.
            var nearHead = head + new Vector3(0.3f, 0.0f, 0.0f);
            Assert.That(BlockiversePanelPlacement.ShouldRecenter(panel, nearHead, Vector3.forward, 1.2f, 30.0f, 1.5f), Is.False);
        }

        [Test]
        public void SmoothTowardConvergesWithoutOvershoot()
        {
            var start = new Pose(Vector3.zero, Quaternion.identity);
            var target = new Pose(new Vector3(2.0f, 0.0f, 0.0f), Quaternion.Euler(0.0f, 90.0f, 0.0f));

            Pose current = start;
            float previousDistance = float.MaxValue;
            for (int i = 0; i < 120; i++)
            {
                current = BlockiversePanelPlacement.SmoothToward(current, target, 0.35f, 1.0f / 72.0f);
                float distance = Vector3.Distance(current.position, target.position);
                Assert.That(distance, Is.LessThanOrEqualTo(previousDistance + 1e-5f), "Monotonic approach.");
                previousDistance = distance;
            }

            Assert.That(Vector3.Distance(current.position, target.position), Is.LessThan(0.02f));
            Assert.That(Quaternion.Angle(current.rotation, target.rotation), Is.LessThan(1.0f));
            Assert.That(
                BlockiversePanelPlacement.SmoothToward(start, target, 0.0f, 0.016f).position,
                Is.EqualTo(target.position),
                "Zero smoothing snaps.");
        }
    }
}
