using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // Tokens that must move together.
    //
    // UI Toolkit centres a horizontal slider's tracker and dragger by placing each at
    // top: 50% and pulling it back with a negative top margin of half its own height.
    // Base.uss overrides both heights, so it must override both margins too — and USS has
    // no calc(), so the halves are literals. Changing a height and forgetting its offset
    // leaves the control hanging half its height below the rail, which is a subtle enough
    // wrongness that it shipped once already and was caught by eye rather than by a test.
    public sealed class HearthstoneTokenPairingEditModeTests
    {
        const string TokensPath = "Assets/Blockiverse/UI/Styles/Tokens.uss";

        static float Token(string tokens, string name)
        {
            Match match = Regex.Match(tokens, $@"{Regex.Escape(name)}:\s*(-?[\d.]+)px");
            Assert.That(match.Success, Is.True, $"{name} missing from {TokensPath}");
            return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        [TestCase("--hs-slider-track-height", "--hs-slider-track-offset")]
        [TestCase("--hs-slider-dragger-height", "--hs-slider-dragger-offset")]
        public void CentringOffsetIsHalfItsHeightNegated(string heightToken, string offsetToken)
        {
            Assert.That(File.Exists(TokensPath), Is.True);
            string tokens = File.ReadAllText(TokensPath);

            float height = Token(tokens, heightToken);
            float offset = Token(tokens, offsetToken);

            // Positive control: a zero height would make the relationship hold vacuously.
            Assert.That(height, Is.GreaterThan(0f), $"{heightToken} should be a real size.");
            Assert.That(offset, Is.EqualTo(-height / 2f).Within(0.01f),
                $"{offsetToken} must be {heightToken} halved and negated, or the control " +
                "sits off-centre on the slider rail.");
        }
    }
}
