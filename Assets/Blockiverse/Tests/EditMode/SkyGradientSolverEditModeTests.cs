using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The sky used to be Unity's stock procedural skybox, which reads the DIRECTION of
    // RenderSettings.sun and nothing else. This project points one shared sun/moon light down from
    // overhead at night so the ground stays lit, so that skybox drew a full noon sky at midnight
    // behind a correctly dark world. These pin the replacement.
    public sealed class SkyGradientSolverEditModeTests
    {
        const float Midday = 0.25f;
        const float Midnight = 0.75f;
        const float Dawn = 0.0f;

        static float Luminance(Color c) => 0.2126f * c.linear.r + 0.7152f * c.linear.g + 0.0722f * c.linear.b;

        [Test]
        public void ElevationComesFromTheClockNotTheLight()
        {
            // The whole point. The shared directional light lies about elevation at night by
            // design, so the sky must derive it independently.
            Assert.That(SkyGradientSolver.SunElevation(Midday), Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(SkyGradientSolver.SunElevation(Midnight), Is.EqualTo(-1.0f).Within(1e-3f));
            Assert.That(SkyGradientSolver.SunElevation(Dawn), Is.EqualTo(0.0f).Within(1e-3f));
        }

        [Test]
        public void TheNightSkyIsDramaticallyDarkerThanTheDaySky()
        {
            Color dayZenith = SkyGradientSolver.ZenithColor(Midday, cloudCoverage: 0.0f, moonPhaseScale: 1.0f);
            Color nightZenith = SkyGradientSolver.ZenithColor(Midnight, cloudCoverage: 0.0f, moonPhaseScale: 1.0f);
            Color dayHorizon = SkyGradientSolver.HorizonColor(Midday, 0.0f, 1.0f);
            Color nightHorizon = SkyGradientSolver.HorizonColor(Midnight, 0.0f, 1.0f);

            // The reported bug was specifically the HORIZON staying bright while the world went
            // dark, so it gets its own assertion rather than riding on the zenith's.
            Assert.That(Luminance(nightZenith), Is.LessThan(Luminance(dayZenith) * 0.1f));
            Assert.That(Luminance(nightHorizon), Is.LessThan(Luminance(dayHorizon) * 0.1f),
                "The bright horizon band is the thing that made night read as daytime.");
        }

        [Test]
        public void NightIsDarkBlueRatherThanBlack()
        {
            // A pure black sky reads as a rendering failure, and the ruleset asks for
            // "dark blue-black".
            Color nightZenith = SkyGradientSolver.ZenithColor(Midnight, 0.0f, 1.0f);

            Assert.That(Luminance(nightZenith), Is.GreaterThan(0.0f));
            Assert.That(nightZenith.b, Is.GreaterThan(nightZenith.r), "Night should stay blue, not grey.");
        }

        [Test]
        public void AMoonlessNightIsDarkerThanAFullMoonNight()
        {
            Color full = SkyGradientSolver.ZenithColor(Midnight, 0.0f, moonPhaseScale: 1.0f);
            Color none = SkyGradientSolver.ZenithColor(Midnight, 0.0f, moonPhaseScale: 0.0f);

            Assert.That(Luminance(none), Is.LessThan(Luminance(full)));
        }

        [Test]
        public void TwilightWarmsTheHorizonWithoutBrighteningTheZenith()
        {
            Color duskHorizon = SkyGradientSolver.HorizonColor(Dawn, 0.0f, 1.0f);
            Color nightHorizon = SkyGradientSolver.HorizonColor(Midnight, 0.0f, 1.0f);

            Assert.That(duskHorizon.r, Is.GreaterThan(duskHorizon.b),
                "Dawn and dusk should read warm at the horizon.");
            Assert.That(Luminance(duskHorizon), Is.GreaterThan(Luminance(nightHorizon)));

            Assert.That(SkyGradientSolver.TwilightAmount(Dawn), Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(SkyGradientSolver.TwilightAmount(Midday), Is.EqualTo(0.0f));
            Assert.That(SkyGradientSolver.TwilightAmount(Midnight), Is.EqualTo(0.0f));
        }

        [Test]
        public void OvercastDarkensTheSkyAtEveryTimeOfDay()
        {
            foreach (float time in new[] { Midday, Midnight, Dawn })
            {
                Color clear = SkyGradientSolver.ZenithColor(time, cloudCoverage: 0.0f, moonPhaseScale: 1.0f);
                Color overcast = SkyGradientSolver.ZenithColor(time, cloudCoverage: 1.0f, moonPhaseScale: 1.0f);

                Assert.That(Luminance(overcast), Is.LessThan(Luminance(clear)), $"Overcast should dim the sky at t={time}.");
            }
        }

        [Test]
        public void CloudsGoDarkAndGreyAsTheStormCloses()
        {
            Color fair = SkyGradientSolver.CloudColor(Midday, cloudCoverage: 0.2f);
            Color storm = SkyGradientSolver.CloudColor(Midday, cloudCoverage: 1.0f);
            Color night = SkyGradientSolver.CloudColor(Midnight, cloudCoverage: 0.6f);

            Assert.That(Luminance(storm), Is.LessThan(Luminance(fair)),
                "A thunderstorm deck should be grey, not the white of fair-weather cloud.");
            Assert.That(Luminance(night), Is.LessThan(Luminance(SkyGradientSolver.CloudColor(Midday, 0.6f))),
                "Clouds are lit by the sky, so they darken at night with everything else.");
            Assert.That(Luminance(night), Is.GreaterThan(0.0f),
                "An overcast night must not become a flat void.");
        }

        [Test]
        public void TheSunDiskIsHiddenOnceTheRealSunIsBelowTheHorizon()
        {
            // Guards the specific oddity the stock skybox produced: a sun disk drawn at the zenith
            // at midnight, because the shared light points straight down then.
            Color middaySun = SkyGradientSolver.SunDiskColor(Midday, moonPhaseScale: 1.0f);
            Color midnightBody = SkyGradientSolver.SunDiskColor(Midnight, moonPhaseScale: 1.0f);
            Color newMoonBody = SkyGradientSolver.SunDiskColor(Midnight, moonPhaseScale: 0.0f);

            Assert.That(Luminance(middaySun), Is.GreaterThan(Luminance(midnightBody)));
            Assert.That(midnightBody.b, Is.GreaterThan(midnightBody.r), "At night the body in the sky is the moon.");
            Assert.That(Luminance(newMoonBody), Is.EqualTo(0.0f).Within(1e-4f),
                "A new moon shows no disk at all.");
        }

        [Test]
        public void DayAmountCrossfadesRatherThanSnapping()
        {
            Assert.That(SkyGradientSolver.DayAmount(Midday), Is.EqualTo(1.0f).Within(1e-3f));
            Assert.That(SkyGradientSolver.DayAmount(Midnight), Is.EqualTo(0.0f).Within(1e-3f));

            float previous = 0.0f;
            for (float t = 0.75f; t <= 1.0f; t += 0.01f)
            {
                float amount = SkyGradientSolver.DayAmount(t % 1.0f);
                Assert.That(amount, Is.GreaterThanOrEqualTo(previous - 1e-4f), $"Day amount fell going into dawn at t={t}.");
                previous = amount;
            }
        }
    }
}
