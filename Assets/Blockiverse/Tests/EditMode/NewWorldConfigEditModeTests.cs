using Blockiverse.UI;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class NewWorldConfigEditModeTests
    {
        [Test]
        public void DefaultsMatchSpecification()
        {
            var config = new NewWorldConfig("918273645");

            Assert.That(config.Name, Is.EqualTo("New World"));
            Assert.That(config.GameMode, Is.EqualTo("survival"));
            Assert.That(config.Difficulty, Is.EqualTo("normal"));
            Assert.That(config.WorldSize, Is.EqualTo("small"));
            Assert.That(config.WorldPreset, Is.EqualTo("survival_terrain"));
            Assert.That(config.StartingBiome, Is.EqualTo("balanced"));
            Assert.That(config.ExperimentalRulesEnabled, Is.False);
            Assert.That(config.Seed, Is.EqualTo(918273645UL));
        }

        [Test]
        public void SelectorsCycleThroughOptionsAndWrap()
        {
            var config = new NewWorldConfig();

            config.CycleGameMode();
            Assert.That(config.GameMode, Is.EqualTo("creative"));
            Assert.That(config.IsCreativeMode, Is.True);
            config.CycleGameMode();
            Assert.That(config.GameMode, Is.EqualTo("survival"), "Game mode should wrap back to survival.");

            config.CycleDifficulty();
            Assert.That(config.Difficulty, Is.EqualTo("hard"));
            config.CycleDifficulty();
            Assert.That(config.Difficulty, Is.EqualTo("easy"));

            config.CycleWorldSize();
            Assert.That(config.WorldSize, Is.EqualTo("medium"));
            config.CycleWorldPreset();
            Assert.That(config.WorldPreset, Is.EqualTo("flat_builder"));
            config.CycleStartingBiome();
            Assert.That(config.StartingBiome, Is.EqualTo("meadow"));
        }

        [Test]
        public void NumericSeedPassesThroughWhileTextSeedHashesDeterministically()
        {
            Assert.That(NewWorldConfig.HashSeed("12345"), Is.EqualTo(12345UL));

            ulong first = NewWorldConfig.HashSeed("meadow-home");
            ulong second = NewWorldConfig.HashSeed("meadow-home");
            Assert.That(first, Is.EqualTo(second), "Text seeds must hash deterministically.");
            Assert.That(first, Is.Not.EqualTo(0UL));
            Assert.That(NewWorldConfig.HashSeed("other"), Is.Not.EqualTo(first));
        }

        [Test]
        public void RandomizeSeedUsesInjectedSource()
        {
            var config = new NewWorldConfig();
            config.RandomizeSeed(() => 424242UL);

            Assert.That(config.SeedText, Is.EqualTo("424242"));
            Assert.That(config.Seed, Is.EqualTo(424242UL));
        }

        [Test]
        public void ValidationRejectsEmptyName()
        {
            var config = new NewWorldConfig();
            config.SetName("   ");

            Assert.That(config.IsValid(out string error), Is.False);
            Assert.That(error, Is.Not.Empty);

            config.SetName("Meadow Home");
            Assert.That(config.IsValid(out _), Is.True);
        }

        [Test]
        public void ToggleExperimentalRulesFlips()
        {
            var config = new NewWorldConfig();
            Assert.That(config.ExperimentalRulesEnabled, Is.False);
            config.ToggleExperimentalRules();
            Assert.That(config.ExperimentalRulesEnabled, Is.True);
            config.ToggleExperimentalRules();
            Assert.That(config.ExperimentalRulesEnabled, Is.False);
        }
    }
}
