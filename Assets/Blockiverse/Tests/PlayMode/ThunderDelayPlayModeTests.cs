using System.Collections;
using System.Collections.Generic;
using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    // The delay between seeing a bolt and hearing it is the strongest distance cue the game has,
    // and it is the one part of the thunder work that cannot be tested in EditMode -- the drain
    // runs on Update, and EditMode has no frame loop.
    public sealed class ThunderDelayPlayModeTests
    {
        readonly List<GameObject> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                    Object.Destroy(target);
            }

            objectsToDestroy.Clear();
        }

        (WeatherFeedbackController controller, List<BlockiverseAudioCue> played) CreateController()
        {
            var audioObject = new GameObject("Thunder Audio Cue Player");
            objectsToDestroy.Add(audioObject);
            audioObject.AddComponent<AudioSource>();
            BlockiverseAudioCuePlayer audio = audioObject.AddComponent<BlockiverseAudioCuePlayer>();
            audio.ConfigureClip(BlockiverseAudioCue.ThunderNear, AudioClip.Create("thunder_near", 64, 1, 8000, stream: false));
            audio.ConfigureClip(BlockiverseAudioCue.ThunderFar, AudioClip.Create("thunder_far", 64, 1, 8000, stream: false));

            var played = new List<BlockiverseAudioCue>();
            audio.CuePlayed += (cue, _) => played.Add(cue);

            var controllerObject = new GameObject("Weather Feedback Under Test");
            objectsToDestroy.Add(controllerObject);
            WeatherFeedbackController controller = controllerObject.AddComponent<WeatherFeedbackController>();
            controller.Configure(audio);

            return (controller, played);
        }

        [UnityTest]
        public IEnumerator DistantThunderStaysSilentUntilItsDueTimeAndThenFiresOnce()
        {
            (WeatherFeedbackController controller, List<BlockiverseAudioCue> played) = CreateController();
            yield return null;

            const float distance = 34.0f;
            float expectedDelay = BlockiverseThunderScheduling.ResolveDelaySeconds(distance);
            Assert.That(expectedDelay, Is.EqualTo(1.0f).Within(0.01f), "Fixture guard on the chosen distance.");

            controller.QueueThunder(distance);
            Assert.That(controller.PendingThunderCount, Is.EqualTo(1));

            // Well before the clap is due, nothing may have played -- the old behaviour fired
            // immediately at full volume no matter how far away the strike was.
            float quietDeadline = Time.time + expectedDelay * 0.5f;
            while (Time.time < quietDeadline)
            {
                Assert.That(played, Is.Empty, "Thunder arrived before its travel time had elapsed.");
                yield return null;
            }

            float fireDeadline = Time.time + expectedDelay;
            while (Time.time < fireDeadline && played.Count == 0)
                yield return null;

            Assert.That(played, Has.Count.EqualTo(1), "The clap never arrived.");
            Assert.That(controller.PendingThunderCount, Is.Zero);

            // And it must not repeat afterwards.
            float settleDeadline = Time.time + 0.3f;
            while (Time.time < settleDeadline)
                yield return null;

            Assert.That(played, Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OverlappingStrikesEachGetTheirOwnClap()
        {
            // A list rather than a single next-time field, because a distant strike still
            // travelling must not be cancelled by a closer one behind it.
            (WeatherFeedbackController controller, List<BlockiverseAudioCue> played) = CreateController();
            yield return null;

            controller.QueueThunder(85.0f);
            controller.QueueThunder(12.0f);

            Assert.That(controller.PendingThunderCount, Is.EqualTo(2));

            float deadline = Time.time + BlockiverseThunderScheduling.ResolveDelaySeconds(85.0f) + 0.5f;
            while (Time.time < deadline && controller.PendingThunderCount > 0)
                yield return null;

            Assert.That(played, Has.Count.EqualTo(2));

            // The near strike is closer, so its clap arrives first even though it was queued
            // second -- and it uses the near clip.
            Assert.That(played[0], Is.EqualTo(BlockiverseAudioCue.ThunderNear));
            Assert.That(played[1], Is.EqualTo(BlockiverseAudioCue.ThunderFar));
        }
    }
}
