using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;

namespace Blockiverse.Tests.EditMode
{
    // The same tile mapping is written down THREE times: the BLOCKS list in
    // scripts/art/generate-art-assets.py (which composes the atlas),
    // BlockVisualAtlas.TileIndexByBlockId (which maps a block to a slot), and
    // BlockAtlasTileNames (which maps a pack's filename to a slot). BlockVisualAtlas's own
    // comment has noted for a long time that the first two "drift silently".
    //
    // These tests are what stops that. A drift in ANY of the three now fails loudly, instead of
    // shipping a block rendered with some other block's texture -- a bug that looks like an art
    // mistake and is nearly impossible to attribute to a table.
    public sealed class BlockAtlasTileNameTableEditModeTests
    {
        // The generator is the source of truth: it is what actually blits pixels into the atlas.
        // Reading it from a test is established practice here -- M4ArtAssetValidationEditModeTests
        // does the same with the same relative path, so the working directory is the project root
        // under batchmode.
        const string GeneratorPath = "scripts/art/generate-art-assets.py";

        static Dictionary<string, int> ReadGeneratorBlocks()
        {
            string source = File.ReadAllText(GeneratorPath);

            int start = source.IndexOf("BLOCKS = [", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Could not find the BLOCKS list in {GeneratorPath}.");
            int end = source.IndexOf("\n]", start, System.StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), $"Could not find the end of the BLOCKS list in {GeneratorPath}.");

            // Accepts either hex or decimal so the test does not break if the literals are ever
            // rewritten in the other base -- it is checking the VALUES agree, not their spelling.
            var pattern = new Regex(@"\(\s*""([a-z0-9_]+)""\s*,\s*(?:0[xX]([0-9A-Fa-f]+)|(\d+))\s*,");

            var blocks = new Dictionary<string, int>();
            foreach (Match match in pattern.Matches(source.Substring(start, end - start)))
            {
                int index = match.Groups[2].Success
                    ? System.Convert.ToInt32(match.Groups[2].Value, 16)
                    : int.Parse(match.Groups[3].Value);

                Assert.That(blocks.ContainsKey(match.Groups[1].Value), Is.False,
                    $"'{match.Groups[1].Value}' appears twice in the generator's BLOCKS list.");
                blocks[match.Groups[1].Value] = index;
            }

            Assert.That(blocks, Is.Not.Empty, "Parsed no entries from the generator's BLOCKS list.");
            return blocks;
        }

        [Test]
        public void TheTileNameTableMatchesTheArtGeneratorExactly()
        {
            Dictionary<string, int> generator = ReadGeneratorBlocks();

            Assert.That(generator.Count, Is.EqualTo(BlockAtlasTileNames.AtlasTileCount),
                "The C# tile-name table and the art generator disagree on how many tiles exist.");

            var wrong = new List<string>();
            foreach (KeyValuePair<string, int> entry in generator)
            {
                if (!BlockAtlasTileNames.TryGetTileIndex(entry.Key, out int actual))
                    wrong.Add($"{entry.Key}: missing from BlockAtlasTileNames");
                else if (actual != entry.Value)
                    wrong.Add($"{entry.Key}: generator says 0x{entry.Value:X2}, C# says 0x{actual:X2}");
            }

            Assert.That(wrong, Is.Empty,
                "The tile-name table has drifted from the art generator. A pack tile would be "
                + "composited into the wrong atlas slot:\n  " + string.Join("\n  ", wrong));
        }

        [Test]
        public void EveryGeneratorTileIndexIsInsideTheAtlasGrid()
        {
            int slots = BlockVisualAtlas.Columns * BlockVisualAtlas.Rows;

            foreach (KeyValuePair<string, int> entry in ReadGeneratorBlocks())
            {
                Assert.That(entry.Value, Is.GreaterThanOrEqualTo(0).And.LessThan(slots),
                    $"'{entry.Key}' has tile index 0x{entry.Value:X2}, outside the "
                    + $"{BlockVisualAtlas.Columns}x{BlockVisualAtlas.Rows} grid.");
            }
        }

        [Test]
        public void TheTileNameTableAgreesWithBlockVisualAtlasForEveryRenderableBlock()
        {
            // THIS is the test BlockVisualAtlas.cs's "they drift silently" comment has been asking
            // for. It ties the two hand-maintained C# tables together through the one thing they
            // share -- the canonical block id -- so neither can be edited alone.
            BlockRegistry registry = BlockRegistry.Default;

            var wrong = new List<string>();
            foreach (BlockDefinition block in registry.All)
            {
                if (!block.IsRenderable || !BlockVisualAtlas.HasAuthoredTile(block.Id))
                    continue;

                if (!BlockAtlasTileNames.TryGetTileIndex(block.CanonicalId, out int byName))
                    continue;   // Face-variant and alias names have no block; covered elsewhere.

                int byBlock = BlockVisualAtlas.GetTileIndex(block.Id);
                if (byName != byBlock)
                    wrong.Add($"{block.CanonicalId}: by name 0x{byName:X2}, by block id 0x{byBlock:X2}");
            }

            Assert.That(wrong, Is.Empty,
                "BlockAtlasTileNames and BlockVisualAtlas.TileIndexByBlockId disagree. One of the "
                + "two hand-maintained tables has drifted:\n  " + string.Join("\n  ", wrong));
        }

        [Test]
        public void EveryPerFaceOverrideResolvesToANamedTile()
        {
            // The six per-face tiles (turf sides, log end grain) have no block of their own, so
            // they are reachable only by name. A pack that wants to restyle grass sides needs them
            // to exist in the table.
            string[] perFaceNames =
            {
                "meadow_turf_side", "dry_turf_side", "snowcap_turf_side",
                "rootsoil_side", "branchwood_log_end", "smooth_branchwood_end",
            };

            foreach (string name in perFaceNames)
            {
                Assert.That(BlockAtlasTileNames.TryGetTileIndex(name, out int index), Is.True,
                    $"'{name}' is missing from the tile-name table, so no pack could restyle it.");
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
            }
        }

        [Test]
        public void FlowAliasesAreRecognisedButHaveNoAtlasSlot()
        {
            // A flowing cell renders with its family's source tile, so these names are real but
            // unusable. Distinguishing them from a typo is the difference between telling a pack
            // author "that file does nothing" and "we have never heard of that file".
            foreach (string alias in BlockAtlasTileNames.FlowAliasNames)
            {
                Assert.That(BlockAtlasTileNames.IsRecognisedButUnused(alias), Is.True, alias);
                Assert.That(BlockAtlasTileNames.TryGetTileIndex(alias, out _), Is.False,
                    $"'{alias}' claims an atlas slot, but flow variants share their family's tile.");
                Assert.That(BlockAtlasTileNames.IsUnknown(alias), Is.False, alias);
            }
        }

        [Test]
        public void AnUnrecognisedNameIsReportedAsUnknown()
        {
            Assert.That(BlockAtlasTileNames.IsUnknown("definitely_not_a_block"), Is.True);
            Assert.That(BlockAtlasTileNames.IsUnknown("meadow_turf"), Is.False);
        }

        [Test]
        public void TileNamesAreMatchedCaseInsensitively()
        {
            // A pack authored on a case-insensitive filesystem must not break on Android's ext4.
            Assert.That(BlockAtlasTileNames.TryGetTileIndex("MEADOW_TURF", out int upper), Is.True);
            Assert.That(BlockAtlasTileNames.TryGetTileIndex("meadow_turf", out int lower), Is.True);
            Assert.That(upper, Is.EqualTo(lower));
        }

        [Test]
        public void NoTwoTextureNamesShareAnAtlasSlot()
        {
            var byIndex = new Dictionary<int, string>();

            foreach (string name in BlockAtlasTileNames.AllTextureNames)
            {
                if (!BlockAtlasTileNames.TryGetTileIndex(name, out int index))
                    continue;

                Assert.That(byIndex.ContainsKey(index), Is.False,
                    $"'{name}' and '{(byIndex.TryGetValue(index, out string other) ? other : "?")}' "
                    + $"both claim slot 0x{index:X2}; one would silently overwrite the other.");
                byIndex[index] = name;
            }

            Assert.That(byIndex.Count, Is.EqualTo(BlockAtlasTileNames.AtlasTileCount));
        }
    }
}
