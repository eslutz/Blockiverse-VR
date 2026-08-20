using Blockiverse.Gameplay;
using Blockiverse.WorldGen;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Pins WeatherFeedbackController's pure precipitation-cue selection: audio loops and scatter
    // particles follow what is falling at the queried location (PrecipitationKind), not the
    // sky-wide weather state, while the light/heavy split and the fog branch keep their
    // weather-state behaviour. Environments come from the real WeatherService.Evaluate so these
    // cases stay honest against the §6.2/§6.3 model instead of hand-built structs the model
    // could silently drift away from.
    public sealed class WeatherFeedbackCueSelectionEditModeTests
    {
        const float Midday = 0.25f;
        const float Midnight = 0.75f;
        // The tallest natural terrain column: SurvivalBiomeResolver peaks relief at SeaLevel + 48.
        const int PeakAltitude = WorldConstants.SeaLevel + 48;

        static EnvironmentState Evaluate(
            WeatherState state,
            int biomeIndex,
            int altitudeY = WorldConstants.SeaLevel,
            float normalizedTime = Midday)
        {
            var service = new WeatherService(seed: 1, state);
            return service.Evaluate(normalizedTime, altitudeY, biomeIndex);
        }

        [Test]
        public void RainStatesAboveFreezingSelectRainCuesWithTheLightHeavySplit()
        {
            // The light/heavy distinction is the weather state's: light rain takes the light loop,
            // heavy rain and thunderstorm the heavy loop — the pre-rework per-state mapping.
            EnvironmentState light = Evaluate(WeatherState.LightRain, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState heavy = Evaluate(WeatherState.HeavyRain, SurvivalBiomeResolver.MeadowBiomeIndex);
            EnvironmentState storm = Evaluate(WeatherState.Thunderstorm, SurvivalBiomeResolver.MeadowBiomeIndex);

            Assert.That(light.Precipitation, Is.EqualTo(PrecipitationKind.Rain), "Fixture guard: a temperate meadow gets rain.");

            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(light), Is.EqualTo(BlockiverseAudioCue.RainLightLoop));
            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(heavy), Is.EqualTo(BlockiverseAudioCue.RainHeavyLoop));
            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(storm), Is.EqualTo(BlockiverseAudioCue.RainHeavyLoop));

            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(light), Is.EqualTo(BlockiverseVfxCue.RainSplash));
            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(heavy), Is.EqualTo(BlockiverseVfxCue.RainSplash));
            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(storm), Is.EqualTo(BlockiverseVfxCue.RainSplash));
        }

        [Test]
        public void TheSameRainStatesBelowFreezingSelectSnowCues()
        {
            // The same synced weather states over the freezing tundra: heard and seen as snow.
            foreach (WeatherState state in new[] { WeatherState.LightRain, WeatherState.HeavyRain, WeatherState.Thunderstorm })
            {
                EnvironmentState env = Evaluate(state, SurvivalBiomeResolver.TundraBiomeIndex);

                Assert.That(env.Precipitation, Is.EqualTo(PrecipitationKind.Snow),
                    $"Fixture guard: {state} over the tundra converts to snow (§6.3).");
                Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(env), Is.EqualTo(BlockiverseAudioCue.SnowWindLoop),
                    $"{state} below freezing must be heard as snow, not rain.");
                Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(env), Is.EqualTo(BlockiverseVfxCue.SnowflakeDrift),
                    $"{state} below freezing must drift snowflakes, not splash rain.");
            }
        }

        [Test]
        public void SnowStatesSelectSnowCuesEverywhere()
        {
            // Inherently snowy states collapse to the one snow loop wherever they are queried —
            // freezing tundra or 34 °C dunes alike (they always fall as snow, §6.3).
            foreach (WeatherState state in new[] { WeatherState.LightSnow, WeatherState.HeavySnow, WeatherState.Blizzard })
            {
                foreach (int biomeIndex in new[] { SurvivalBiomeResolver.TundraBiomeIndex, SurvivalBiomeResolver.DunesBiomeIndex })
                {
                    EnvironmentState env = Evaluate(state, biomeIndex);

                    Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(env), Is.EqualTo(BlockiverseAudioCue.SnowWindLoop),
                        $"{state} must select the snow loop for biome index {biomeIndex}.");
                    Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(env), Is.EqualTo(BlockiverseVfxCue.SnowflakeDrift),
                        $"{state} must select snowflake drift for biome index {biomeIndex}.");
                }
            }
        }

        [Test]
        public void DrySkiesSelectNoPrecipitationCues()
        {
            // Nothing falls, nothing plays — even over the freezing tundra, where a bug that read
            // temperature instead of kind would conjure a snow loop out of a clear sky.
            foreach (WeatherState state in new[] { WeatherState.Clear, WeatherState.PartlyCloudy, WeatherState.Overcast, WeatherState.Fog })
            {
                EnvironmentState env = Evaluate(state, SurvivalBiomeResolver.TundraBiomeIndex);

                Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(env), Is.Null,
                    $"{state} drops nothing, so no precipitation loop may play.");
                Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(env), Is.Null,
                    $"{state} drops nothing, so no precipitation particles may spawn.");
            }
        }

        [Test]
        public void FogIsNotAPrecipitationCue()
        {
            // Fog wisps are keyed to WeatherState.Fog in the controller's VFX tick, not to the
            // precipitation kind — the selector must stay silent so that branch remains reachable.
            EnvironmentState fog = Evaluate(WeatherState.Fog, SurvivalBiomeResolver.MeadowBiomeIndex);

            Assert.That(fog.Precipitation, Is.EqualTo(PrecipitationKind.None), "Fixture guard: fog drops nothing.");
            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(fog), Is.Null);
            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(fog), Is.Null);
        }

        [Test]
        public void KindFlipUnderAnUnchangedWeatherStateChangesTheSelectedCues()
        {
            // The change-detection guarantee: the weather state alone cannot distinguish a pinewild
            // valley from the freezing peak above it during one storm at night, but the selected
            // cues differ — so comparing selections catches the flip a state comparison would miss
            // (player walks up the mountain mid-storm; day/night boundary crosses the freeze line).
            var service = new WeatherService(seed: 1, WeatherState.HeavyRain);

            EnvironmentState valley = service.Evaluate(Midnight, WorldConstants.SeaLevel, SurvivalBiomeResolver.PinewildBiomeIndex);
            EnvironmentState peak = service.Evaluate(Midnight, PeakAltitude, SurvivalBiomeResolver.PinewildBiomeIndex);

            Assert.That(valley.Weather, Is.EqualTo(peak.Weather), "Fixture guard: one storm, one weather state.");
            Assert.That(valley.Precipitation, Is.EqualTo(PrecipitationKind.Rain), "Fixture guard: the valley is above freezing.");
            Assert.That(peak.Precipitation, Is.EqualTo(PrecipitationKind.Snow), "Fixture guard: the peak is below freezing.");

            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(valley), Is.EqualTo(BlockiverseAudioCue.RainHeavyLoop));
            Assert.That(WeatherFeedbackController.SelectPrecipitationLoop(peak), Is.EqualTo(BlockiverseAudioCue.SnowWindLoop));
            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(valley), Is.EqualTo(BlockiverseVfxCue.RainSplash));
            Assert.That(WeatherFeedbackController.SelectPrecipitationVfx(peak), Is.EqualTo(BlockiverseVfxCue.SnowflakeDrift));
        }
    }
}
