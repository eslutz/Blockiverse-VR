using System.Collections.Generic;
using Blockiverse.VR;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseGlideBobControllerEditModeTests
    {
        const float FrameSeconds = 1f / 60f;
        // 1.8 m/s, the default glide speed.
        const float MetersPerFrame = 0.03f;
        // Frames for the amplitude follower to reach full travel speed, with margin.
        const int SettleFrames = 60;

        readonly List<GameObject> objectsToDestroy = new();

        [SetUp]
        public void SetUp()
        {
            BlockiverseRuntimeState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
                if (target != null)
                    Object.DestroyImmediate(target);
            objectsToDestroy.Clear();
            BlockiverseRuntimeState.Reset();
        }

        [Test]
        public void StationaryReturnsZeroBobOffset()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);

            for (int frame = 0; frame < 5; frame++)
                stack.RunFrame();

            Assert.That(stack.CameraOffsetY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingIsZeroWhenStyleIsSmooth()
        {
            TestStack stack = CreateTestStack(GlideStyle.Smooth);

            stack.Walk(SettleFrames);

            Assert.That(stack.CameraOffsetY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingAppliesVerticalOffsetWhenMovingAndEnabled()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);

            stack.Walk(SettleFrames);

            Assert.That(stack.CameraOffsetY, Is.Not.EqualTo(0f));
            Assert.That(Mathf.Abs(stack.CameraOffsetY), Is.LessThanOrEqualTo(1.8f * stack.Bob.Amplitude + 0.001f));
        }

        [Test]
        public void BobbingDoesNotApplyWhileAirborne()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);
            stack.Grounded = false;

            stack.Walk(SettleFrames);

            Assert.That(stack.CameraOffsetY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingDoesNotApplyWhileWorldInputIsBlocked()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);
            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

            stack.Walk(SettleFrames);

            Assert.That(stack.CameraOffsetY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobbingOffsetDecaysToZeroWhenStopping()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);

            stack.Walk(SettleFrames);
            Assert.That(stack.CameraOffsetY, Is.Not.EqualTo(0f));

            for (int frame = 0; frame < SettleFrames; frame++)
                stack.RunFrame();

            Assert.That(stack.CameraOffsetY, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BobTroughLandsOnGaitPhaseZero()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);
            stack.Walk(SettleFrames);

            // Sample a full step and find where the view actually bottoms out.
            int frames = Mathf.CeilToInt(BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame);
            float lowestOffset = float.MaxValue;
            float phaseAtLowest = -1f;
            for (int frame = 0; frame < frames; frame++)
            {
                stack.Walk(1);
                if (stack.CameraOffsetY < lowestOffset)
                {
                    lowestOffset = stack.CameraOffsetY;
                    phaseAtLowest = stack.Gait.BobPhase01;
                }
            }

            // Phase 0 is the trough, so the sampled minimum must sit within one frame of the wrap.
            float phasePerFrame = MetersPerFrame / BlockiverseGaitCycle.DefaultStepLengthMeters;
            float distanceFromZero = Mathf.Min(phaseAtLowest, 1f - phaseAtLowest);
            Assert.That(lowestOffset, Is.LessThan(0f));
            Assert.That(distanceFromZero, Is.LessThanOrEqualTo(phasePerFrame + 0.0001f));
        }

        [Test]
        public void FootfallFiresWhileTheViewIsStillDroppingToTheTrough()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);
            stack.Walk(SettleFrames);

            float offsetAtFootfall = float.NaN;
            stack.Gait.Footfall += () => offsetAtFootfall = stack.CameraOffsetY;

            // Walk a step, capturing the offset still showing when the cue fires, and the one that
            // follows a lead's worth of travel later.
            int leadFrames = Mathf.CeilToInt(
                BlockiverseGaitCycle.DefaultFootfallLeadPhase * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame);
            int frames = Mathf.CeilToInt(BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame);
            for (int frame = 0; frame < frames && float.IsNaN(offsetAtFootfall); frame++)
                stack.Walk(1);

            Assert.That(float.IsNaN(offsetAtFootfall), Is.False, "no footfall fired during a full step");

            stack.Walk(leadFrames);

            // The cue leads the trough, so the view is still on its way down when the step sounds.
            Assert.That(stack.CameraOffsetY, Is.LessThan(offsetAtFootfall));
        }

        [Test]
        public void AbsoluteHeightWriteFollowedByClearAppliedOffsetKeepsBaseHeight()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);

            stack.Walk(SettleFrames);
            Assert.That(stack.CameraOffsetY, Is.Not.EqualTo(0f));

            // Height reset writes an absolute base height and tells the bob to forget its offset.
            stack.CameraOffset.localPosition = new Vector3(0f, 1.6f, 0f);
            stack.Bob.ClearAppliedOffset();

            // Stop and let the bob settle: the base must land exactly on the written height.
            for (int frame = 0; frame < SettleFrames; frame++)
                stack.RunFrame();

            Assert.That(stack.CameraOffsetY, Is.EqualTo(1.6f).Within(0.001f));
        }

        [Test]
        public void AdditiveExternalHeightWritesDoNotDriftTheBaseHeight()
        {
            TestStack stack = CreateTestStack(GlideStyle.Bobbing);
            stack.Walk(SettleFrames);

            // Crouch easing nudges the same Y by a delta every frame while the bob is running. The
            // bob must not fold its own offset into that base, or the view sinks a little per frame.
            const float crouchStep = -0.01f;
            const int crouchFrames = 40;
            for (int frame = 0; frame < crouchFrames; frame++)
            {
                stack.Walk(1);
                Vector3 localPosition = stack.CameraOffset.localPosition;
                localPosition.y += crouchStep;
                stack.CameraOffset.localPosition = localPosition;
            }

            // Let the bob decay so only the crouch delta remains.
            for (int frame = 0; frame < SettleFrames; frame++)
                stack.RunFrame();

            Assert.That(stack.CameraOffsetY, Is.EqualTo(crouchStep * crouchFrames).Within(0.001f));
        }

        TestStack CreateTestStack(GlideStyle glideStyle)
        {
            GameObject rig = new("Test Rig");
            objectsToDestroy.Add(rig);

            BlockiverseComfortSettings settings = rig.AddComponent<BlockiverseComfortSettings>();
            settings.LocomotionMode = BlockiverseLocomotionMode.Glide;
            settings.GlideStyle = glideStyle;

            XROrigin origin = rig.AddComponent<XROrigin>();
            GameObject cameraOffset = new("Camera Offset");
            objectsToDestroy.Add(cameraOffset);
            cameraOffset.transform.SetParent(rig.transform, false);
            origin.CameraFloorOffsetObject = cameraOffset;

            // Added before the bob so the bob's Awake binds it.
            BlockiverseGaitCycle gait = rig.AddComponent<BlockiverseGaitCycle>();
            BlockiverseGlideBobController bob = rig.AddComponent<BlockiverseGlideBobController>();

            var awake = typeof(BlockiverseGlideBobController).GetMethod(
                "Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(awake, Is.Not.Null, "Awake method not found via reflection.");
            awake.Invoke(bob, null);

            BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);

            var stack = new TestStack(rig, gait, bob, cameraOffset.transform);
            stack.RunFrame();
            return stack;
        }

        sealed class TestStack
        {
            static readonly System.Reflection.MethodInfo LateUpdateMethod =
                typeof(BlockiverseGlideBobController).GetMethod(
                    "LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            readonly GameObject rig;

            public TestStack(GameObject rig, BlockiverseGaitCycle gait, BlockiverseGlideBobController bob, Transform cameraOffset)
            {
                this.rig = rig;
                Gait = gait;
                Bob = bob;
                CameraOffset = cameraOffset;
                Grounded = true;
                gait.GroundedOverride = () => Grounded;
                Assert.That(LateUpdateMethod, Is.Not.Null, "LateUpdate method not found via reflection.");
            }

            public BlockiverseGaitCycle Gait { get; }
            public BlockiverseGlideBobController Bob { get; }
            public Transform CameraOffset { get; }
            public bool Grounded { get; set; }
            public float CameraOffsetY => CameraOffset.localPosition.y;

            public void RunFrame()
            {
                Gait.Advance(FrameSeconds);
                LateUpdateMethod.Invoke(Bob, null);
            }

            public void Walk(int frames)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    rig.transform.position += new Vector3(0f, 0f, MetersPerFrame);
                    RunFrame();
                }
            }
        }
    }
}
