using Blockiverse.Gameplay;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseMusicEditModeTests
    {
        [Test]
        public void ResolveContextMapsMenuWorldStateAndTimeOfDay()
        {
            // No active world session → menu music regardless of the other inputs.
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: false, underground: false, normalizedTime: 0.3f),
                Is.EqualTo(BlockiverseMusicContext.Menu));
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: false, underground: true, normalizedTime: 0.9f),
                Is.EqualTo(BlockiverseMusicContext.Menu));

            // Underground wins over time of day.
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: true, underground: true, normalizedTime: 0.3f),
                Is.EqualTo(BlockiverseMusicContext.Cave));

            // Above ground follows the shared day window.
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: true, underground: false, normalizedTime: 0.3f),
                Is.EqualTo(BlockiverseMusicContext.Day));
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: true, underground: false, normalizedTime: 0.7f),
                Is.EqualTo(BlockiverseMusicContext.Night));
            Assert.That(BlockiverseMusicScheduling.ResolveContext(worldActive: true, underground: false, normalizedTime: 0.01f),
                Is.EqualTo(BlockiverseMusicContext.Night));
        }

        [Test]
        public void MusicDayWindowMatchesTheSharedWorldClockWindow()
        {
            // Music and ambience must flip day/night together; both read WorldTimeClock.IsDay.
            Assert.That(WorldTimeClock.IsDay(WorldTimeClock.DayStartNormalizedTime), Is.True);
            Assert.That(WorldTimeClock.IsDay(WorldTimeClock.NightStartNormalizedTime), Is.False);
            Assert.That(WorldTimeClock.IsDay(0.30f), Is.True);
            Assert.That(WorldTimeClock.IsDay(0.95f), Is.False);
        }

        [Test]
        public void RolledGapsStayInsideTheConfiguredRange()
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < 200; i++)
            {
                float gap = BlockiverseMusicScheduling.RollGapSeconds(rng);
                Assert.That(gap, Is.GreaterThanOrEqualTo(BlockiverseMusicScheduling.MinGapSeconds));
                Assert.That(gap, Is.LessThanOrEqualTo(BlockiverseMusicScheduling.MaxGapSeconds));
            }

            Assert.That(BlockiverseMusicScheduling.RollGapSeconds(null),
                Is.EqualTo(BlockiverseMusicScheduling.MinGapSeconds));
        }

        [Test]
        public void FadeEnvelopeRampsInHoldsAndRampsOut()
        {
            const float clipLength = 30.0f;

            // Silent outside the clip and at its exact boundaries.
            Assert.That(BlockiverseMusicScheduling.ResolveFadeLevel(0.0f, clipLength), Is.Zero);
            Assert.That(BlockiverseMusicScheduling.ResolveFadeLevel(clipLength, clipLength), Is.Zero);
            Assert.That(BlockiverseMusicScheduling.ResolveFadeLevel(5.0f, 0.0f), Is.Zero);

            // Mid-ramp values rise, hold at full, then fall into the tail.
            float duringFadeIn = BlockiverseMusicScheduling.ResolveFadeLevel(
                BlockiverseMusicScheduling.FadeInSeconds * 0.5f, clipLength);
            Assert.That(duringFadeIn, Is.GreaterThan(0.0f).And.LessThan(1.0f));

            Assert.That(BlockiverseMusicScheduling.ResolveFadeLevel(clipLength * 0.5f, clipLength), Is.EqualTo(1.0f));

            float duringFadeOut = BlockiverseMusicScheduling.ResolveFadeLevel(
                clipLength - BlockiverseMusicScheduling.FadeOutSeconds * 0.5f, clipLength);
            Assert.That(duringFadeOut, Is.GreaterThan(0.0f).And.LessThan(1.0f));
        }

        [Test]
        public void MusicVolumeFollowsTheMusicCategoryAndMute()
        {
            var settingsObject = new UnityEngine.GameObject("Feedback Settings");
            try
            {
                var settings = settingsObject.AddComponent<BlockiverseFeedbackSettings>();
                settings.MasterVolume = 0.8f;
                settings.MusicVolume = 0.5f;

                Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Music), Is.EqualTo(0.4f).Within(0.0001f));

                settings.MuteAll = true;
                Assert.That(settings.ResolveVolume(BlockiverseAudioCategory.Music), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settingsObject);
            }
        }
    }
}
