using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseGaitCycleEditModeTests
    {
        const float FrameSeconds = 1f / 60f;
        // 1.8 m/s, the default glide speed.
        const float MetersPerFrame = 0.03f;

        readonly List<GameObject> objectsToDestroy = new();

        [SetUp]
        public void SetUp()
        {
            // The gait gates on AllowWorldInput, which defaults to false outside a running session.
            BlockiverseRuntimeState.SetRouterState(isGamePaused: false, allowWorldInput: true);
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
        public void FootfallFiresOncePerStepLength()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // Five steps' worth of travel from the mid-step start phase.
            int frames = Mathf.CeilToInt(5f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame);
            Walk(rig, gait, frames);

            Assert.That(footfalls, Is.EqualTo(5));
        }

        [Test]
        public void FootfallLeadsTheBobLowPointByTheConfiguredPhase()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            var phasesAtFootfall = new List<float>();
            gait.Footfall += () => phasesAtFootfall.Add(gait.BobPhase01);

            Walk(rig, gait, Mathf.CeilToInt(4f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            // The trough sits on phase 0, so the cue must fire a lead short of the wrap — never on
            // or after it, which would put the sound behind the view drop.
            float expected = 1f - BlockiverseGaitCycle.DefaultFootfallLeadPhase;
            float phasePerFrame = MetersPerFrame / BlockiverseGaitCycle.DefaultStepLengthMeters;

            Assert.That(phasesAtFootfall, Is.Not.Empty);
            foreach (float phase in phasesAtFootfall)
                Assert.That(phase, Is.InRange(expected, expected + phasePerFrame + 0.0001f));
        }

        [Test]
        public void TeleportDoesNotFireFootfalls()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            rig.transform.position = new Vector3(0f, 0f, 40f);
            gait.Advance(FrameSeconds);

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
        }

        [Test]
        public void GoingAirborneReseedsWithoutSnappingThePhase()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // Walk far enough for the first footfall, then leave the ground for a frame.
            Walk(rig, gait, Mathf.CeilToInt(0.8f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.EqualTo(1));
            float phaseBefore = gait.BobPhase01;

            gait.GroundedOverride = () => false;
            gait.Advance(FrameSeconds);
            gait.GroundedOverride = () => true;

            // The phase must hold — the bob draws it every frame, so a snap here is a camera jump.
            Assert.That(gait.BobPhase01, Is.EqualTo(phaseBefore).Within(0.0001f));

            // The footfall index re-seeded, so a third of a step of travel must not reach a footfall.
            Walk(rig, gait, Mathf.CeilToInt(0.33f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(footfalls, Is.EqualTo(1));
        }

        [Test]
        public void ExternalSuppressionStopsFootfallsAndStepping()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            gait.ExternallySuppressed = true;

            // Creative flight skimming the ground: grounded, moving, but not walking.
            Walk(rig, gait, Mathf.CeilToInt(3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.IsSuppressed, Is.True);

            // Landing out of flight and walking again earns a footfall only after real travel.
            // The phase held at mid-step (0.5) through the suppression, so the next crossing sits
            // 0.4 steps out: none after 0.3 steps, one shortly after.
            gait.ExternallySuppressed = false;
            Walk(rig, gait, Mathf.CeilToInt(0.3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.Zero);
            Walk(rig, gait, Mathf.CeilToInt(0.3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.EqualTo(1));
        }

        [Test]
        public void BlockedWorldInputSuppressesTheCycle()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

            Walk(rig, gait, Mathf.CeilToInt(3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
        }

        [Test]
        public void FootfallRateIsCappedAtSprintCadence()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            var footfallTimes = new List<float>();
            float simTime = 0f;

            // 8.8 m/s: the max move-speed slider under sprint. Crossings arrive every ~90 ms,
            // faster than the 0.18 s ceiling, so roughly every other one must be swallowed.
            const float sprintMetersPerFrame = 8.8f * FrameSeconds;
            gait.Footfall += () => footfallTimes.Add(simTime);

            int crossings = 20;
            int frames = Mathf.CeilToInt(crossings * BlockiverseGaitCycle.DefaultStepLengthMeters / sprintMetersPerFrame);
            for (int frame = 0; frame < frames; frame++)
            {
                rig.transform.position += new Vector3(0f, 0f, sprintMetersPerFrame);
                simTime += FrameSeconds;
                gait.Advance(FrameSeconds);
            }

            Assert.That(footfallTimes.Count, Is.GreaterThan(0));
            Assert.That(footfallTimes.Count, Is.LessThan(crossings), "the ceiling should swallow crossings at sprint cadence");
            for (int i = 1; i < footfallTimes.Count; i++)
            {
                Assert.That(
                    footfallTimes[i] - footfallTimes[i - 1],
                    Is.GreaterThanOrEqualTo(BlockiverseGaitCycle.MinFootfallIntervalSeconds - 0.0001f));
            }
        }

        [Test]
        public void StoppingHoldsPhaseSoTappedMovementDoesNotMachineGun()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // Ten taps of a fifth of a step in total: distance-quantised phase means no cue fires.
            for (int tap = 0; tap < 10; tap++)
            {
                Walk(rig, gait, 1);
                for (int idle = 0; idle < 10; idle++)
                    gait.Advance(FrameSeconds);
            }

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.BobPhase01, Is.GreaterThan(0.5f), "phase should have accumulated, not reset");
        }

        [Test]
        public void WalkingIntoAWallStopsTheCycle()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // The rig never moves, as if pressed against a wall with the stick held.
            for (int frame = 0; frame < 300; frame++)
                gait.Advance(FrameSeconds);

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.Speed, Is.EqualTo(0f).Within(0.0001f));
        }

        (GameObject rig, BlockiverseGaitCycle gait) CreateGait()
        {
            GameObject rig = new("Test Rig");
            objectsToDestroy.Add(rig);
            BlockiverseGaitCycle gait = rig.AddComponent<BlockiverseGaitCycle>();
            gait.GroundedOverride = () => true;
            gait.Advance(FrameSeconds);
            return (rig, gait);
        }

        static void Walk(GameObject rig, BlockiverseGaitCycle gait, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                rig.transform.position += new Vector3(0f, 0f, MetersPerFrame);
                gait.Advance(FrameSeconds);
            }
        }
    }
}
