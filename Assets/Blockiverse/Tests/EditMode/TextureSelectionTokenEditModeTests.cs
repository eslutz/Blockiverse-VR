using System;
using Blockiverse.Core;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // The texture token vocabulary. The behaviour that matters here is the ASYMMETRY between the
    // two kinds of token: an unknown built-in id is corruption and is coerced away, while a
    // well-formed pack token is preserved even though nothing on disk may match it. Losing that
    // distinction is what silently destroys a player's pack selection on the next autosave.
    public sealed class TextureSelectionTokenEditModeTests
    {
        [Test]
        public void AWellFormedPackTokenSurvivesNormalizationEvenThoughNoSuchPackExists()
        {
            // No filesystem is involved and no such pack is installed. It must still round-trip:
            // the save has to be able to carry the player's choice back out after they reinstall.
            Assert.That(
                BlockiverseTextureSelection.NormalizeToken("pack:mossy_stones"),
                Is.EqualTo("pack:mossy_stones"),
                "A pack token was coerced away. The player's selection would be overwritten by the next autosave.");
        }

        [Test]
        public void AnUnknownBuiltInIdIsStillCoercedToTheDefault()
        {
            // The other half of the asymmetry: there are exactly four built-in sets, so anything
            // else in that position can only be corruption and there is nothing to preserve.
            Assert.That(BlockiverseTextureSelection.NormalizeToken("wibble"), Is.EqualTo(BlockTextureSetIds.Default));
        }

        [Test]
        public void EveryBuiltInIdNormalizesToItself()
        {
            foreach (string id in BlockTextureSetIds.All)
                Assert.That(BlockiverseTextureSelection.NormalizeToken(id), Is.EqualTo(id));
        }

        [Test]
        public void BlankAndNullTokensBecomeTheDefault()
        {
            Assert.That(BlockiverseTextureSelection.NormalizeToken(null), Is.EqualTo(BlockTextureSetIds.Default));
            Assert.That(BlockiverseTextureSelection.NormalizeToken(""), Is.EqualTo(BlockTextureSetIds.Default));
            Assert.That(BlockiverseTextureSelection.NormalizeToken("   "), Is.EqualTo(BlockTextureSetIds.Default));
        }

        [Test]
        public void TokensAreMatchedCaseInsensitivelyAndReturnedLowercase()
        {
            // Consistent with BlockTextureSetIds.Normalize, which has always been case-insensitive.
            // A hand-edited manifest should not be a silent reset to the default.
            Assert.That(BlockiverseTextureSelection.NormalizeToken("Enhanced"), Is.EqualTo(BlockTextureSetIds.Enhanced));
            Assert.That(BlockiverseTextureSelection.NormalizeToken("pack:Mossy_Stones"), Is.EqualTo("pack:mossy_stones"));
            Assert.That(BlockiverseTextureSelection.NormalizeToken("PACK:MOSSY"), Is.EqualTo("pack:mossy"));
        }

        [Test]
        public void SurroundingWhitespaceIsTrimmedRatherThanTreatedAsCorruption()
        {
            Assert.That(BlockiverseTextureSelection.NormalizeToken("  pack:mossy  "), Is.EqualTo("pack:mossy"));
        }

        [TestCase("pack:", TestName = "empty pack id")]
        [TestCase("pack:../x", TestName = "path traversal")]
        [TestCase("pack:a/b", TestName = "path separator")]
        [TestCase("pack:a\\b", TestName = "windows path separator")]
        [TestCase("pack:a.b", TestName = "dot")]
        [TestCase("pack:a b", TestName = "space")]
        [TestCase("pack:a-b", TestName = "hyphen")]
        [TestCase("pack:pack:a", TestName = "nested prefix")]
        public void AMalformedPackTokenBecomesTheDefaultRatherThanAPackId(string token)
        {
            // A malformed token is not a selection anyone could have legitimately made, so unlike a
            // merely-uninstalled pack there is nothing worth preserving. Rejecting `.`, `/` and `\`
            // here is what stops a pack id from ever becoming a path when the library resolves it.
            Assert.That(
                BlockiverseTextureSelection.NormalizeToken(token),
                Is.EqualTo(BlockTextureSetIds.Default),
                $"'{token}' produced a pack token. A malformed id must never reach the filesystem.");

            Assert.That(BlockiverseTextureSelection.IsPackToken(token), Is.False);
        }

        [Test]
        public void APackIdLongerThanTheLimitIsRejected()
        {
            string atLimit = new string('a', BlockiverseTextureSelection.MaxPackIdLength);
            string overLimit = new string('a', BlockiverseTextureSelection.MaxPackIdLength + 1);

            Assert.That(BlockiverseTextureSelection.IsValidPackId(atLimit), Is.True);
            Assert.That(BlockiverseTextureSelection.IsValidPackId(overLimit), Is.False);
            Assert.That(
                BlockiverseTextureSelection.NormalizeToken("pack:" + overLimit),
                Is.EqualTo(BlockTextureSetIds.Default));
        }

        [Test]
        public void IsPackTokenDistinguishesPacksFromBuiltIns()
        {
            Assert.That(BlockiverseTextureSelection.IsPackToken("pack:mossy"), Is.True);
            Assert.That(BlockiverseTextureSelection.IsPackToken(BlockTextureSetIds.Enhanced), Is.False);
            Assert.That(BlockiverseTextureSelection.IsPackToken(null), Is.False);
        }

        [Test]
        public void TryGetPackIdReturnsTheLowercasedIdWithoutThePrefix()
        {
            Assert.That(BlockiverseTextureSelection.TryGetPackId("pack:Mossy_Stones", out string packId), Is.True);
            Assert.That(packId, Is.EqualTo("mossy_stones"));

            Assert.That(BlockiverseTextureSelection.TryGetPackId(BlockTextureSetIds.Ai, out string none), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void ForPackRoundTripsThroughNormalizeToken()
        {
            string token = BlockiverseTextureSelection.ForPack("mossy_stones");

            Assert.That(token, Is.EqualTo("pack:mossy_stones"));
            Assert.That(BlockiverseTextureSelection.NormalizeToken(token), Is.EqualTo(token));
        }

        [Test]
        public void ForPackThrowsOnAnIdThatWouldNotSurviveNormalization()
        {
            // Fail at the call site that invented the bad id rather than three layers away, where
            // it would present as "my pack silently became the default".
            Assert.Throws<ArgumentException>(() => BlockiverseTextureSelection.ForPack("../x"));
            Assert.Throws<ArgumentException>(() => BlockiverseTextureSelection.ForPack(""));
            Assert.Throws<ArgumentException>(() => BlockiverseTextureSelection.ForPack(null));
        }

        [Test]
        public void NormalizingAnAlreadyNormalizedTokenIsIdempotent()
        {
            // The save path normalizes on write AND on read, so a token that changed on each pass
            // would drift across repeated save/load cycles.
            foreach (string token in new[] { "pack:mossy", "pack:Mossy", "enhanced", "Enhanced", "wibble", null })
            {
                string once = BlockiverseTextureSelection.NormalizeToken(token);
                Assert.That(BlockiverseTextureSelection.NormalizeToken(once), Is.EqualTo(once), $"'{token}' drifted.");
            }
        }
    }
}
