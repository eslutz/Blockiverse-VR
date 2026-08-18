using System.Linq;
using Blockiverse.UI;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockiverseTmpFontFallbackEditModeTests
    {
        [Test]
        public void RuntimeFallbackFontListIncludesNonLatinSystemFamilies()
        {
            string[] fontNames = BlockiverseTmpFontFallbackBootstrapper.PreferredOsFontNames.ToArray();

            Assert.That(fontNames, Has.Some.Contains("CJK"));
            Assert.That(fontNames, Has.Some.Contains("Arabic"));
            Assert.That(fontNames, Has.Some.Contains("Thai"));
            Assert.That(fontNames, Has.Some.Contains("Devanagari"));
            Assert.That(fontNames, Contains.Item("sans-serif"),
                "Quest/Android should have a generic system fallback even when locale-specific names differ.");
        }
    }
}
