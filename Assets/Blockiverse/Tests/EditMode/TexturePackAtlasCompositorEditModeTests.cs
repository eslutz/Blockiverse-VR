using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The compositor's pixel arithmetic, tested against hand-computed expectations rather than
    // against itself. All synthetic Color32[] -- no GPU, no PNG decode, so it runs under
    // -nographics like the rest of EditMode.
    //
    // These are the checks that stand in for "looks right on device", because the two failures
    // most likely here (a mirrored Y origin, and padding that does not replicate) both produce
    // something that renders perfectly happily and is simply wrong.
    public sealed class TexturePackAtlasCompositorEditModeTests
    {
        static Color32[] Solid(int size, byte r, byte g, byte b, byte a = 255)
        {
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(r, g, b, a);
            return pixels;
        }

        static Color32[] BlankAtlas(int scale, Color32 fill)
        {
            int width = BlockVisualAtlas.AtlasWidthPixels * scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * scale;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fill;
            return pixels;
        }

        static Color32 At(Color32[] pixels, int width, int x, int y) => pixels[y * width + x];

        // ── tile placement ──────────────────────────────────────────────────

        [TestCase(0x00, TestName = "first slot")]
        [TestCase(0x0F, TestName = "end of row 0")]
        [TestCase(0x10, TestName = "start of row 1")]
        [TestCase(0x60, TestName = "last used slot")]
        public void ATileLandsInTheSlotItsIndexNames(int tileIndex)
        {
            const int Scale = 1;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;

            Color32[] atlas = BlankAtlas(Scale, new Color32(0, 0, 0, 255));
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, 255, 0, 255), tileSize, tileIndex, Scale);

            // Recomputed here independently of the implementation, in Unity's BOTTOM-UP pixel space.
            // GetPixels32 puts row 0 at the bottom while BuildTileRect treats tile row 0 as the TOP
            // of the image, and getting that flip wrong mirrors the whole atlas by row -- which
            // presents as "every block has some other block's texture".
            int stride = BlockVisualAtlas.TileStridePixels * Scale;
            int padding = BlockVisualAtlas.TilePaddingPixels * Scale;
            int originX = (tileIndex % BlockVisualAtlas.Columns) * stride + padding;
            int originY = height - (tileIndex / BlockVisualAtlas.Columns + 1) * stride + padding;

            Assert.That(At(atlas, width, originX, originY), Is.EqualTo(new Color32(255, 0, 255, 255)),
                $"Slot 0x{tileIndex:X2} bottom-left corner is not the tile.");
            Assert.That(At(atlas, width, originX + tileSize - 1, originY + tileSize - 1),
                Is.EqualTo(new Color32(255, 0, 255, 255)),
                $"Slot 0x{tileIndex:X2} top-right corner is not the tile.");
        }

        [Test]
        public void TileRowZeroLandsAtTheTopOfTheImage()
        {
            // Stated separately because it is the single most consequential line in the compositor
            // and the easiest to get backwards.
            const int Scale = 1;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;

            Color32[] atlas = BlankAtlas(Scale, new Color32(0, 0, 0, 255));
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, 1, 2, 3), tileSize, tileIndex: 0, scale: Scale);

            // Row 0 is the top of the PNG, which in bottom-up pixel space is the HIGHEST y.
            int padding = BlockVisualAtlas.TilePaddingPixels * Scale;
            int topRow = height - padding - 1;

            Assert.That(At(atlas, width, padding, topRow), Is.EqualTo(new Color32(1, 2, 3, 255)),
                "Tile row 0 is not at the top of the image; the atlas is mirrored by row.");
        }

        // ── padding ─────────────────────────────────────────────────────────

        [Test]
        public void PaddingReplicatesTheTilesEdgeRatherThanLeavingTheBaseBehind()
        {
            // Edge-clamp replication is what stops a tile mip-bleeding into its neighbour. If the
            // base atlas's padding were left in place, a pack tile would blend into a built-in
            // tile's colours at distance -- invisible up close, wrong from across a chunk.
            const int Scale = 1;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;
            int padding = BlockVisualAtlas.TilePaddingPixels * Scale;

            Color32[] atlas = BlankAtlas(Scale, new Color32(9, 9, 9, 255));
            var tile = new Color32(200, 100, 50, 255);
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, tile.r, tile.g, tile.b), tileSize, tileIndex: 0, scale: Scale);

            int originX = padding;
            int originY = height - BlockVisualAtlas.TileStridePixels * Scale + padding;

            // Every texel of the stride cell, including all four padding bands and the corners.
            for (int dy = -padding; dy < tileSize + padding; dy++)
            {
                for (int dx = -padding; dx < tileSize + padding; dx++)
                {
                    Assert.That(At(atlas, width, originX + dx, originY + dy), Is.EqualTo(tile),
                        $"Padding at ({dx},{dy}) is not a replica of the tile edge.");
                }
            }
        }

        [Test]
        public void AlphaIsCopiedVerbatimIntoThePadding()
        {
            // Cutout foliage depends on this: an averaged or premultiplied alpha in the padding
            // would put a halo around every blade at the 0.5 cutoff.
            const int Scale = 1;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;

            Color32[] atlas = BlankAtlas(Scale, new Color32(0, 0, 0, 255));
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, 10, 20, 30, a: 0), tileSize, tileIndex: 0, scale: Scale);

            int padding = BlockVisualAtlas.TilePaddingPixels * Scale;
            int originY = height - BlockVisualAtlas.TileStridePixels * Scale + padding;

            Assert.That(At(atlas, width, padding - 1, originY).a, Is.EqualTo(0),
                "Transparent alpha did not survive into the padding.");
        }

        [Test]
        public void ATileNeverWritesOutsideItsOwnStrideCell()
        {
            const int Scale = 1;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;
            int stride = BlockVisualAtlas.TileStridePixels * Scale;

            var background = new Color32(9, 9, 9, 255);
            Color32[] atlas = BlankAtlas(Scale, background);
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, 200, 0, 0), tileSize, tileIndex: 0, scale: Scale);

            // The neighbouring slot must be untouched.
            Assert.That(At(atlas, width, stride + BlockVisualAtlas.TilePaddingPixels, height - stride / 2),
                Is.EqualTo(background), "A tile bled into the neighbouring slot.");
        }

        // ── scaling ─────────────────────────────────────────────────────────

        [TestCase(16, 1)]
        [TestCase(32, 1)]
        [TestCase(64, 2)]
        [TestCase(128, 4)]
        public void ScaleForTileSizeMapsPackResolutionToAtlasScale(int tilePixels, int expected)
        {
            Assert.That(BlockiverseTexturePackAtlasBuilder.ScaleForTileSize(tilePixels), Is.EqualTo(expected));
        }

        [Test]
        public void ALowResolutionPackNeverShrinksTheAtlas()
        {
            // A 16px pack upscales into the shipped grid rather than shrinking it, so built-in
            // tiles it does not override keep their full detail.
            Assert.That(BlockiverseTexturePackAtlasBuilder.ScaleForTileSize(16), Is.EqualTo(1));
            Assert.That(BlockiverseTexturePackAtlasBuilder.ScaleForTileSize(8), Is.EqualTo(1));
        }

        [Test]
        public void NearestUpscaleDuplicatesTexelsExactlyWithNoBlending()
        {
            // Integer nearest only. Any interpolation here would blur pixel art, which is the one
            // thing a pixel-art pack author cannot forgive.
            var source = new Color32[4]
            {
                new(1, 0, 0, 255), new(2, 0, 0, 255),
                new(3, 0, 0, 255), new(4, 0, 0, 255),
            };

            Color32[] scaled = BlockiverseTexturePackAtlasBuilder.UpscaleNearest(source, 2, 2, 2);

            Assert.That(scaled.Length, Is.EqualTo(16));
            Assert.That(scaled[0].r, Is.EqualTo(1));
            Assert.That(scaled[1].r, Is.EqualTo(1), "Texel was interpolated instead of duplicated.");
            Assert.That(scaled[4].r, Is.EqualTo(1));
            Assert.That(scaled[15].r, Is.EqualTo(4));
        }

        [Test]
        public void UpscalingByOneIsACopyNotAnAlias()
        {
            var source = new Color32[] { new(7, 0, 0, 255) };
            Color32[] copy = BlockiverseTexturePackAtlasBuilder.UpscaleNearest(source, 1, 1, 1);

            copy[0] = new Color32(8, 0, 0, 255);
            Assert.That(source[0].r, Is.EqualTo(7), "Upscale-by-1 aliased the source array.");
        }

        [Test]
        public void ATileScaledUpStillFillsItsWholeSlot()
        {
            const int Scale = 2;
            int width = BlockVisualAtlas.AtlasWidthPixels * Scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * Scale;
            int tileSize = BlockVisualAtlas.TilePixels * Scale;

            Color32[] atlas = BlankAtlas(Scale, new Color32(0, 0, 0, 255));
            BlockiverseTexturePackAtlasBuilder.BlitPaddedTile(
                atlas, width, height, Solid(tileSize, 5, 6, 7), tileSize, tileIndex: 0x11, scale: Scale);

            int stride = BlockVisualAtlas.TileStridePixels * Scale;
            int padding = BlockVisualAtlas.TilePaddingPixels * Scale;
            int originX = (0x11 % BlockVisualAtlas.Columns) * stride + padding;
            int originY = height - (0x11 / BlockVisualAtlas.Columns + 1) * stride + padding;

            Assert.That(At(atlas, width, originX, originY), Is.EqualTo(new Color32(5, 6, 7, 255)));
            Assert.That(At(atlas, width, originX + tileSize - 1, originY + tileSize - 1),
                Is.EqualTo(new Color32(5, 6, 7, 255)));
        }

        // ── mip chain ───────────────────────────────────────────────────────

        [Test]
        public void DownsamplingHandlesOddDimensionsWithoutDroppingTheLastRow()
        {
            // 768x480 is not a power of two, so the chain reaches odd sizes. Truncating instead of
            // clamping would silently drop the last row and column from every level below that.
            var source = new Color32[3 * 3];
            for (int i = 0; i < source.Length; i++)
                source[i] = new Color32(100, 100, 100, 255);

            Color32[] result = BlockiverseTexturePackAtlasBuilder.Downsample(source, 3, 3, 2, 2);

            Assert.That(result.Length, Is.EqualTo(4));
            foreach (Color32 pixel in result)
                Assert.That(pixel.r, Is.EqualTo(100).Within(2), "An odd dimension produced an out-of-range texel.");
        }

        [Test]
        public void DownsamplingAveragesColourInLinearSpaceNotEncodedBytes()
        {
            // Averaging sRGB bytes directly darkens every mip -- a mid-grey between black and white
            // is 188, not 128. Unity's own generator averages in linear for an sRGB texture, so a
            // composited atlas must too or pack tiles darken with distance while built-ins do not.
            var source = new Color32[4]
            {
                new(0, 0, 0, 255), new(255, 255, 255, 255),
                new(0, 0, 0, 255), new(255, 255, 255, 255),
            };

            Color32[] result = BlockiverseTexturePackAtlasBuilder.Downsample(source, 2, 2, 1, 1);

            Assert.That(result[0].r, Is.EqualTo(188).Within(3),
                "Mip RGB was averaged in sRGB space; every mip level will be too dark.");
        }

        [Test]
        public void CoverageIsMeasuredAtTheCutoutThreshold()
        {
            var pixels = new Color32[4]
            {
                new(0, 0, 0, 255), new(0, 0, 0, 0),
                new(0, 0, 0, 200), new(0, 0, 0, 10),
            };

            Assert.That(BlockiverseTexturePackAtlasBuilder.MeasureCoverage(pixels), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void AlphaIsRescaledSoAMipKeepsTheOriginalCoverage()
        {
            // This is mipMapsPreserveCoverage, which is an IMPORT-time setting that
            // Apply(updateMipmaps: true) does not reproduce. Without it foliage cutouts thin and
            // vanish with distance while built-in tiles stay solid.
            var mip = new Color32[8];
            for (int i = 0; i < mip.Length; i++)
                mip[i] = new Color32(0, 0, 0, (byte)(i < 4 ? 120 : 20));

            // 120 is just under the 128 threshold, so coverage starts at zero.
            Assert.That(BlockiverseTexturePackAtlasBuilder.MeasureCoverage(mip), Is.EqualTo(0.0f));

            BlockiverseTexturePackAtlasBuilder.ScaleAlphaToCoverage(mip, targetCoverage: 0.5f);

            Assert.That(BlockiverseTexturePackAtlasBuilder.MeasureCoverage(mip), Is.EqualTo(0.5f).Within(0.13f),
                "Coverage was not restored; cutout foliage will thin out with distance.");
        }

        [Test]
        public void CoverageCorrectionIsANoOpWhenThereIsNothingToPreserve()
        {
            var opaque = new Color32[4];
            for (int i = 0; i < opaque.Length; i++)
                opaque[i] = new Color32(0, 0, 0, 255);

            BlockiverseTexturePackAtlasBuilder.ScaleAlphaToCoverage(opaque, targetCoverage: 1.0f);

            foreach (Color32 pixel in opaque)
                Assert.That(pixel.a, Is.EqualTo(255));
        }
    }
}
