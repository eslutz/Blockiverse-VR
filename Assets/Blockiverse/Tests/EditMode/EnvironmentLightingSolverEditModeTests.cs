using Blockiverse.Gameplay;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class EnvironmentLightingSolverEditModeTests
    {
        const float Midday = 0.25f;

        static EnvironmentState Weather(float cloud, float precip, float storm, float fog = 0f) => new()
        {
            CloudCoverage = cloud,
            PrecipitationIntensity = precip,
            StormIntensity = storm,
            FogDensity = fog,
        };

        [Test]
        public void ClearSkyDoesNotDimDaylight()
        {
            float factor = EnvironmentLightingSolver.WeatherLightFactor(Midday, Weather(0.1f, 0f, 0f));
            Assert.That(factor, Is.EqualTo(1f).Within(0.001f), "Clear weather should not dim midday light.");
        }

        [Test]
        public void StormDimsDaylightBelowClear()
        {
            float clear = EnvironmentLightingSolver.WeatherLightFactor(Midday, Weather(0.1f, 0f, 0f));
            float storm = EnvironmentLightingSolver.WeatherLightFactor(Midday, Weather(1.0f, 1.0f, 1.0f));
            Assert.That(storm, Is.LessThan(clear), "A thunderstorm must dim daylight more than a clear sky.");
        }

        [Test]
        public void WeatherFactorNeverDropsBelowComfortFloor()
        {
            float blizzard = EnvironmentLightingSolver.WeatherLightFactor(Midday, Weather(1.0f, 1.0f, 1.0f));
            Assert.That(blizzard, Is.GreaterThanOrEqualTo(EnvironmentLightingSolver.MinWeatherLightFactor));
            Assert.That(blizzard, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void FogStateIsFarDenserThanClearAirButClearAirIsNeverZero()
        {
            float fogState = EnvironmentLightingSolver.FogDensity(Weather(0.65f, 0f, 0f, fog: 0.8f));
            float clear = EnvironmentLightingSolver.FogDensity(Weather(0.1f, 0f, 0f, fog: 0f));

            // Clear air used to return EXACTLY zero here, and the caller gated on
            // "density > 0" to decide whether to switch RenderSettings.fog on at all. So Clear,
            // PartlyCloudy and Overcast -- the three longest-dwelling weather states -- rendered
            // with fog fully disabled: no aerial perspective at any distance, and the only fade
            // visible was the sky gradient behind the world edge. In the headset that reads as
            // "something hazy far away, perfectly clear air around me".
            Assert.That(clear, Is.EqualTo(EnvironmentLightingSolver.ClearAirDensity).Within(1e-6f),
                "Clear weather must still carry the aerial-perspective floor.");
            Assert.That(clear, Is.GreaterThan(0f));

            // The floor must stay subtle. If it ever approached a real fog state, clear days would
            // read as overcast murk -- the opposite failure, and harder to notice in review.
            Assert.That(fogState, Is.GreaterThan(clear * 5.0f),
                "An explicit fog state has to be dramatically denser than clear air, or weather stops reading.");
        }

        [Test]
        public void HeavyPrecipitationAddsHaze()
        {
            Assert.That(EnvironmentLightingSolver.FogDensity(Weather(0.95f, 1.0f, 0.5f, fog: 0f)), Is.GreaterThan(0f),
                "Heavy precipitation should add a light haze even without an explicit fog state.");
        }
    }
}
