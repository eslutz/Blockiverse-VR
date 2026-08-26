using Blockiverse.Core;
using Blockiverse.Networking;
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
            Assert.That(config.WorldPreset, Is.EqualTo(WorldPresetIds.SurvivalTerrain));
            Assert.That(GetStringProperty(config, "TextureSet"), Is.EqualTo("enhanced"));
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
            config.CycleWorldSize();
            Assert.That(config.WorldSize, Is.EqualTo("large"));
            config.CycleWorldSize();
            Assert.That(config.WorldSize, Is.EqualTo("x_large"));
            config.CycleWorldSize();
            Assert.That(config.WorldSize, Is.EqualTo("small"), "World size should wrap small→medium→large→x_large→small.");
            config.CycleWorldPreset();
            Assert.That(config.WorldPreset, Is.EqualTo(WorldPresetIds.FlatBuilder));
            config.CycleWorldPreset();
            Assert.That(config.WorldPreset, Is.EqualTo(WorldPresetIds.SurvivalTerrain), "World preset should wrap after flat_builder.");

            config.CycleTextureSet();
            Assert.That(GetStringProperty(config, "TextureSet"), Is.EqualTo("ai_simplified"));
            Assert.That(BlockiverseLocalization.DisplayNameForCanonicalId("ai_simplified"), Is.EqualTo("AI Simplified"));
            config.CycleTextureSet();
            Assert.That(GetStringProperty(config, "TextureSet"), Is.EqualTo("ai"));
            config.CycleTextureSet();
            Assert.That(GetStringProperty(config, "TextureSet"), Is.EqualTo("original"));
            config.CycleTextureSet();
            Assert.That(GetStringProperty(config, "TextureSet"), Is.EqualTo("enhanced"),
                "Texture set should wrap after Original back to the default Enhanced set.");
        }

        [Test]
        public void NumericSeedPassesThroughWhileTextSeedHashesDeterministically()
        {
            Assert.That(NewWorldConfig.HashSeed("12345"), Is.EqualTo(12345UL));
            Assert.That(NewWorldConfig.HashSeed("12,345"), Is.Not.EqualTo(12345UL),
                "Seed parsing must stay invariant and ungrouped; formatted text seeds should hash.");

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

        [TestCase("small", 192)]
        [TestCase("medium", 256)]
        [TestCase("large", 384)]
        [TestCase("x_large", 512)]
        public void EveryMenuWorldSizeBuildsItsAdvertisedSquareDimensions(string size, int side)
        {
            (int width, int depth) = WorldSaveGeneration.SizeFor(size);

            Assert.That(width, Is.EqualTo(side));
            Assert.That(depth, Is.EqualTo(side));
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
        public void ValidationRejectsSurvivalBuilderPresetCombinations()
        {
            var config = new NewWorldConfig();
            config.CycleWorldPreset();

            Assert.That(config.WorldPreset, Is.EqualTo(WorldPresetIds.FlatBuilder));
            Assert.That(config.GameMode, Is.EqualTo("survival"));
            Assert.That(config.IsValid(out string error), Is.False);
            Assert.That(error, Does.Contain("Survival Terrain"));

            config.CycleGameMode();
            Assert.That(config.GameMode, Is.EqualTo("creative"));
            Assert.That(config.IsValid(out _), Is.True);
        }

        static string GetStringProperty(object target, string propertyName)
        {
            System.Reflection.PropertyInfo property = target.GetType().GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Missing NewWorldConfig.{propertyName}.");
            return (string)property.GetValue(target);
        }

    }
}
