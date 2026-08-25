using System.Linq;
using Blockiverse.Core;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    public sealed class BlockTextureSetIdsEditModeTests
    {
        [Test]
        public void CuratedPilotIsSelectableWithoutChangingTheDefaultTextureSet()
        {
            Assert.That(BlockTextureSetIds.Normalize("CURATED_V1"), Is.EqualTo(BlockTextureSetIds.CuratedV1));
            Assert.That(BlockTextureSetIds.All, Does.Contain(BlockTextureSetIds.CuratedV1));
            Assert.That(BlockTextureSetIds.MenuOptions, Does.Contain(BlockTextureSetIds.CuratedV1));
            Assert.That(BlockTextureSetIds.Default, Is.EqualTo(BlockTextureSetIds.Enhanced));
            Assert.That(BlockTextureSetIds.MenuOptions.Distinct().Count(), Is.EqualTo(BlockTextureSetIds.MenuOptions.Length));
        }
    }
}
