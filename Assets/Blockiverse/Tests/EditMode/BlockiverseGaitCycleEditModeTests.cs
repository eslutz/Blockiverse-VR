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
        public void TeleportDoesNotFireFootfallsOrSnapThePhase()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // Walk partway into a step first so the phase sits at a non-trivial value.
            Walk(rig, gait, Mathf.CeilToInt(0.3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            float phaseBefore = gait.BobPhase01;

            rig.transform.position += new Vector3(0f, 0f, 40f);
            gait.Advance(FrameSeconds);

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.BobPhase01, Is.EqualTo(phaseBefore).Within(0.0001f), "a teleport must not snap the bob phase");
        }

        [Test]
        public void SnapTurnOriginDisplacementDoesNotAdvanceTheGait()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            Walk(rig, gait, Mathf.CeilToInt(0.3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            float phaseBefore = gait.BobPhase01;
            int footfallsBefore = footfalls;

            // A snap turn rotates the XR origin around the camera: a player standing 0.9 m from
            // play-space centre translates the rig ~0.7 m in one frame — under the 2 m teleport
            // guard but a physically impossible stride at ~42 m/s.
            rig.transform.position += new Vector3(0.5f, 0f, 0.5f);
            gait.Advance(FrameSeconds);

            Assert.That(footfalls, Is.EqualTo(footfallsBefore));
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.Speed, Is.Zero);
            Assert.That(gait.BobPhase01, Is.EqualTo(phaseBefore).Within(0.0001f), "a turn displacement must not advance the phase");
        }

        [Test]
        public void OriginMotionWithoutMoveIntentDoesNotStep()
        {
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            // Smooth turn in place translates the origin at plausible walking speeds. With the
            // move stick centred, that travel must not read as a gait.
            gait.MoveIntentOverride = () => 0f;
            Walk(rig, gait, Mathf.CeilToInt(3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.BobPhase01, Is.EqualTo(0.5f).Within(0.0001f), "intentless travel must hold the phase");

            // Stick engaged again: walking resumes from the held phase (crossing sits 0.4 steps out).
            gait.MoveIntentOverride = () => 1f;
            Walk(rig, gait, Mathf.CeilToInt(0.5f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.EqualTo(1));
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

            // Walk partway into a step before suppressing so the phase-hold assertion is not
            // trivially satisfied by the start value.
            Walk(rig, gait, Mathf.CeilToInt(0.2f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            float phaseBefore = gait.BobPhase01;
            gait.ExternallySuppressed = true;

            // Creative flight skimming the ground: grounded, moving, but not walking.
            Walk(rig, gait, Mathf.CeilToInt(3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(footfalls, Is.Zero);
            Assert.That(gait.IsStepping, Is.False);
            Assert.That(gait.IsSuppressed, Is.True);
            Assert.That(gait.BobPhase01, Is.EqualTo(phaseBefore).Within(0.0001f), "suppression must hold the phase, not snap it");

            // Landing out of flight and walking again earns a footfall only after real travel: the
            // held phase sits ~0.2 steps short of the next crossing... it is at start (0.5) + 0.2
            // walked, so the crossing at phase 0.9 is ~0.2 steps out. None immediately, one after.
            gait.ExternallySuppressed = false;
            Walk(rig, gait, Mathf.CeilToInt(0.1f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.Zero);
            Walk(rig, gait, Mathf.CeilToInt(0.3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));
            Assert.That(footfalls, Is.EqualTo(1));
        }

        [Test]
        public void BlockedWorldInputDoesNotSuppressTheCycle()
        {
            // The inverse of what this used to assert, and the reason it changed: blocked world
            // input means "a menu holds focus", which is the entire title/mini-world state. Gating
            // the walk cycle on it killed the bob and the footsteps everywhere the player can walk
            // but not build -- exactly the mini-world. The menus ruleset says menus never suppress
            // locomotion; only block editing stays gated.
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();
            int footfalls = 0;
            gait.Footfall += () => footfalls++;

            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);

            Walk(rig, gait, Mathf.CeilToInt(3f * BlockiverseGaitCycle.DefaultStepLengthMeters / MetersPerFrame));

            Assert.That(gait.IsSuppressed, Is.False, "Menu focus must not suppress the walk cycle.");
            Assert.That(gait.IsStepping, Is.True);
            Assert.That(footfalls, Is.GreaterThan(0), "Footsteps must fire in the title mini-world.");
        }

        [Test]
        public void OnlyExternalSuppressionCanStopTheCycle()
        {
            // Pins the contract so the world-input gate cannot be reintroduced silently. Creative
            // flight is the one legitimate suppressor.
            (GameObject rig, BlockiverseGaitCycle gait) = CreateGait();

            BlockiverseRuntimeState.SetRouterState(isGamePaused: true, allowWorldInput: false);
            Assert.That(gait.IsSuppressed, Is.False);

            gait.ExternallySuppressed = true;
            Assert.That(gait.IsSuppressed, Is.True);

            gait.ExternallySuppressed = false;
            Assert.That(gait.IsSuppressed, Is.False);
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
