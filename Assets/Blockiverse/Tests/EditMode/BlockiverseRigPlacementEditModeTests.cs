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
    }
}
