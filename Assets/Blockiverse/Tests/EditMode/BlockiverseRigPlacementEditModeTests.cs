using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The yaw arithmetic behind "the player always starts facing the title menu".
    //
    // This bug is INVISIBLE without a headset: with no tracked HMD the camera has no local
    // rotation, so rig yaw == view yaw and the broken code looks correct. Every test here
    // therefore fakes a non-zero head-local yaw by rotating a camera under a rig, or it would
    // pass for the wrong reason — the exact failure mode this repo keeps hitting.
    public sealed class BlockiverseRigPlacementEditModeTests
    {
        GameObject rigObject;
        GameObject cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null)
                Object.DestroyImmediate(cameraObject);
            if (rigObject != null)
                Object.DestroyImmediate(rigObject);
        }

        Transform BuildRig(float rigYaw, float headLocalYaw)
        {
            rigObject = new GameObject("Rig");
            rigObject.transform.rotation = Quaternion.Euler(0.0f, rigYaw, 0.0f);

            cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetParent(rigObject.transform, worldPositionStays: false);
            cameraObject.transform.localRotation = Quaternion.Euler(0.0f, headLocalYaw, 0.0f);

            return rigObject.transform;
        }

        static float ResolvedViewYaw(Transform rig, float headingDegrees)
        {
            // What the view yaw becomes once the computed rig yaw is applied.
            float rigYaw = BlockiverseRigPlacement.ResolveRigYawForViewHeading(rig, headingDegrees);
            float headLocalYaw = Mathf.DeltaAngle(rig.eulerAngles.y, rig.GetChild(0).eulerAngles.y);
            return rigYaw + headLocalYaw;
        }

        [Test]
        public void TheComputedRigYawPutsTheVIEWOnTheRequestedHeading()
        {
            // The whole point: "facing the menu" must mean the player's eyes, not the rig axis.
            Transform rig = BuildRig(rigYaw: 25.0f, headLocalYaw: 70.0f);

            Assert.That(Mathf.DeltaAngle(ResolvedViewYaw(rig, 0.0f), 0.0f), Is.EqualTo(0.0f).Within(0.01f));
        }

        [Test]
        public void APlayerPhysicallyTurnedRightAroundStillEndsUpFacingTheMenu()
        {
            // Eric's case: he had turned in the room, so rig yaw and view yaw disagreed by a lot.
            Transform rig = BuildRig(rigYaw: 0.0f, headLocalYaw: 180.0f);

            float rigYaw = BlockiverseRigPlacement.ResolveRigYawForViewHeading(rig, 0.0f);
            Assert.That(Mathf.DeltaAngle(rigYaw, 180.0f), Is.EqualTo(0.0f).Within(0.01f),
                "The rig must counter-rotate by the head's local yaw.");
            Assert.That(Mathf.DeltaAngle(ResolvedViewYaw(rig, 0.0f), 0.0f), Is.EqualTo(0.0f).Within(0.01f));
        }

        [Test]
        public void AHeadAlreadyOnTheHeadingNeedsNoRotation()
        {
            Transform rig = BuildRig(rigYaw: 0.0f, headLocalYaw: 0.0f);

            Assert.That(BlockiverseRigPlacement.ResolveRigYawForViewHeading(rig, 0.0f),
                Is.EqualTo(0.0f).Within(0.01f));
        }

        [Test]
        public void LookingStraightDownDoesNotSpinThePlayerFromAEulerArtefact()
        {
            // Pitch at the poles makes an euler yaw meaningless — a player looking at their feet
            // when they hit "return to title" must not be spun by a decomposition artefact. The
            // implementation flattens the head's FORWARD instead of reading eulerAngles.y.
            Transform rig = BuildRig(rigYaw: 0.0f, headLocalYaw: 0.0f);
            rig.GetChild(0).localRotation = Quaternion.Euler(89.9f, 0.0f, 0.0f);

            float rigYaw = BlockiverseRigPlacement.ResolveRigYawForViewHeading(rig, 0.0f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(rigYaw, 0.0f)), Is.LessThan(5.0f),
                "A near-vertical gaze must not produce a large spurious yaw.");
        }

        [Test]
        public void ANullRigFallsBackToTheRequestedHeading()
        {
            Assert.That(BlockiverseRigPlacement.ResolveRigYawForViewHeading(null, 42.0f),
                Is.EqualTo(42.0f).Within(0.0001f));
        }

        // Where the player's EYES land, which is the other half of the same bug. The rig origin is
        // the tracking origin, not the player: put the rig on the spawn block and the head is left
        // standing wherever they had walked to inside their room, beside a menu pinned relative to
        // spawn. "I'm off to the left a little bit and looking to the left past the menu" (Eric,
        // 2026-08-25) — the heading was already correct, which is exactly why it reads as being in
        // the wrong place rather than pointed the wrong way.
        Transform BuildOffsetRig(float rigYaw, Vector3 headLocalPosition)
        {
            Transform rig = BuildRig(rigYaw, headLocalYaw: 0.0f);
            rig.GetChild(0).localPosition = headLocalPosition;
            return rig;
        }

        static Vector3 HeadAfterPlacement(Transform rig, Vector3 rigPosition, float rigYaw)
        {
            Vector3 localHead = rig.GetChild(0).localPosition;
            return rigPosition + Quaternion.Euler(0.0f, rigYaw, 0.0f) * localHead;
        }

        [Test]
        public void TheRigIsOffsetSoThePlayersHeadLandsOnTheSpawnBlock()
        {
            Transform rig = BuildOffsetRig(rigYaw: 0.0f, headLocalPosition: new Vector3(1.4f, 1.7f, -0.6f));
            var target = new Vector3(64.5f, 66.0f, 64.5f);

            Vector3 placed = BlockiverseRigPlacement.ResolveRigPositionForHead(rig, target, 0.0f);
            Vector3 head = HeadAfterPlacement(rig, placed, 0.0f);

            Assert.That(head.x, Is.EqualTo(target.x).Within(0.001f));
            Assert.That(head.z, Is.EqualTo(target.z).Within(0.001f));

            // Negative half: the correction must have actually done something, or a no-op passes.
            Assert.That(Vector3.Distance(placed, target), Is.GreaterThan(0.5f),
                "The rig did not move off the spawn block, so no room offset was compensated.");
        }

        [Test]
        public void TheOffsetIsMeasuredInTheYawThePlayerWillEndUpWith()
        {
            // The head's offset is RIG-LOCAL, so turning the rig swings it around the origin. An
            // offset measured before the turn and applied after it is wrong by exactly the angle
            // of the turn — and since the title transition always turns the player, that error is
            // present every single time rather than occasionally.
            Transform rig = BuildOffsetRig(rigYaw: 0.0f, headLocalPosition: new Vector3(1.5f, 1.7f, 0.0f));
            var target = new Vector3(10.5f, 4.0f, 20.5f);

            const float TargetYaw = 90.0f;
            Vector3 placed = BlockiverseRigPlacement.ResolveRigPositionForHead(rig, target, TargetYaw);
            Vector3 head = HeadAfterPlacement(rig, placed, TargetYaw);

            Assert.That(head.x, Is.EqualTo(target.x).Within(0.001f));
            Assert.That(head.z, Is.EqualTo(target.z).Within(0.001f));

            // At 90 degrees a local +X offset points along world -Z, so the compensation has to
            // come out on Z and leave X untouched. A version that ignored the yaw would correct
            // the wrong AXIS, not merely the wrong amount — which is why this pins the axis rather
            // than a distance, and why the assertion above alone would not catch it.
            Assert.That(placed.z, Is.EqualTo(target.z + 1.5f).Within(0.001f),
                "The correction was applied in the rig's OLD frame.");
            Assert.That(placed.x, Is.EqualTo(target.x).Within(0.001f));
        }

        [Test]
        public void TheFloorHeightIsNeverCompensatedAway()
        {
            // Y is the floor the rig stands on and the head's height above it is the player's own
            // height. Subtracting that would bury the rig 1.7 m into the ground, and the fall would
            // look like a spawn-safety bug rather than a placement one.
            Transform rig = BuildOffsetRig(rigYaw: 0.0f, headLocalPosition: new Vector3(0.0f, 1.72f, 0.0f));
            var target = new Vector3(3.5f, 70.0f, 9.5f);

            Assert.That(BlockiverseRigPlacement.ResolveRigPositionForHead(rig, target, 0.0f).y,
                Is.EqualTo(70.0f).Within(0.0001f));
        }

        [Test]
        public void OnDesktopWhereTheHeadSitsOnTheRigOriginNothingMoves()
        {
            // The reason this needs a test at all: with no tracked HMD the camera is at the rig
            // origin, the offset is zero, and the broken code and the fixed one are identical.
            Transform rig = BuildOffsetRig(rigYaw: 30.0f, headLocalPosition: Vector3.zero);
            var target = new Vector3(1.5f, 2.0f, 3.5f);

            Vector3 placed = BlockiverseRigPlacement.ResolveRigPositionForHead(rig, target, 30.0f);
            Assert.That(Vector3.Distance(placed, target), Is.LessThan(0.001f));
        }

        [Test]
        public void ANullRigLeavesTheTargetAlone()
        {
            var target = new Vector3(5.0f, 6.0f, 7.0f);
            Assert.That(BlockiverseRigPlacement.ResolveRigPositionForHead(null, target, 12.0f), Is.EqualTo(target));
        }
    }
}
