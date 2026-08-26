using System;
using System.Collections.Generic;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// Composites a user-supplied texture pack over a shipped atlas.
    ///
    /// The arithmetic here is a direct transliteration of <c>blit_padded_tile</c> and
    /// <c>write_texture_set_atlas</c> in scripts/art/generate-art-assets.py. That is deliberate:
    /// the runtime result has to be indistinguishable from what the offline generator produces, or
    /// a pack tile and a built-in tile would mip and bleed differently in the same atlas.
    ///
    /// Everything except <see cref="Compose"/>'s final upload is pure <c>Color32[]</c> arithmetic
    /// with no Unity object creation, so it runs under EditMode's -nographics. The GPU and PNG
    /// boundary is isolated behind <see cref="IBlockiverseAtlasPixelSource"/>.
    /// </summary>
    public static class BlockiverseTexturePackAtlasBuilder
    {
        /// <summary>Alpha at or above which a texel counts as covered, matching the atlas
        /// importer's <c>alphaTestReferenceValue: 0.5</c> and the shader's cutoff.</summary>
        public const byte CoverageAlphaThreshold = 128;

        /// <summary>
        /// Builds the composited atlas texture. Returns null if the base atlas cannot be read, in
        /// which case the caller should keep whatever it is already drawing.
        /// </summary>
        public static Texture2D Compose(
            Texture2D baseAtlas,
            string packId,
            IReadOnlyList<string> tileNames,
            int packTilePixels,
            IBlockiverseAtlasPixelSource pixelSource)
        {
            if (baseAtlas == null || pixelSource == null)
                return null;

            Color32[] basePixels = pixelSource.ReadAtlas(baseAtlas);
            if (basePixels == null)
                return null;

            int scale = ScaleForTileSize(packTilePixels);
            int width = BlockVisualAtlas.AtlasWidthPixels * scale;
            int height = BlockVisualAtlas.AtlasHeightPixels * scale;

            Color32[] composed = UpscaleNearest(
                basePixels, BlockVisualAtlas.AtlasWidthPixels, BlockVisualAtlas.AtlasHeightPixels, scale);

            int applied = 0;
            var unknown = new List<string>();
            var unused = new List<string>();

            foreach (string tileName in tileNames ?? Array.Empty<string>())
            {
                if (BlockAtlasTileNames.IsRecognisedButUnused(tileName))
                {
                    unused.Add(tileName);
                    continue;
                }

                if (!BlockAtlasTileNames.TryGetTileIndex(tileName, out int tileIndex))
                {
                    unknown.Add(tileName);
                    continue;
                }

                if (!BlockiverseTexturePackLibrary.TryReadTileBytes(packId, tileName, out byte[] png))
                    continue;

                if (!pixelSource.TryDecodeTile(png, out Color32[] tile, out int tileSize))
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Assets,
                        $"Skipping tile '{tileName}' in texture pack '{packId}': not a square PNG.");
                    continue;
                }

                int targetSize = BlockVisualAtlas.TilePixels * scale;
                if (targetSize % tileSize != 0)
                {
                    // Integer upscale only. A fractional resample would blur pixel art, so a tile
                    // that does not divide evenly is skipped rather than quietly degraded.
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Assets,
                        $"Skipping tile '{tileName}' in texture pack '{packId}': {tileSize}px does not "
                        + $"scale to {targetSize}px by a whole number.");
                    continue;
                }

                Color32[] scaled = tileSize == targetSize
                    ? tile
                    : UpscaleNearest(tile, tileSize, tileSize, targetSize / tileSize);

                BlitPaddedTile(composed, width, height, scaled, targetSize, tileIndex, scale);
                applied++;
            }

            if (unknown.Count > 0)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Texture pack '{packId}' contains {unknown.Count} unrecognised tile name(s), ignored: "
                    + string.Join(", ", unknown.GetRange(0, Math.Min(unknown.Count, 8))));
            }

            if (unused.Count > 0)
            {
                // Specific message: these ARE real Blockiverse texture names, they simply have no
                // atlas slot because a flowing cell renders with its family's source tile.
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Texture pack '{packId}' supplies flow variants ({string.Join(", ", unused)}), which have "
                    + "no atlas slot -- flowing cells render with their family's source tile.");
            }

            BlockiverseLog.Info(
                BlockiverseLogCategory.Assets,
                $"Composited texture pack '{packId}': {applied} tile(s) at scale {scale} ({width}x{height}).");

            return BuildTexture(composed, width, height);
        }

        /// <summary>
        /// Atlas scale for a pack's tile size. 16px and 32px packs composite at scale 1; a 64px
        /// pack doubles the atlas. Never below 1 — a low-res pack is upscaled into the shipped
        /// grid rather than shrinking it, so built-in tiles it does not override keep their detail.
        /// </summary>
        public static int ScaleForTileSize(int packTilePixels)
        {
            if (packTilePixels <= BlockVisualAtlas.TilePixels)
                return 1;

            int scale = packTilePixels / BlockVisualAtlas.TilePixels;
            return Mathf.Clamp(scale, 1, BlockVisualAtlas.MaxAtlasScale);
        }

        /// <summary>Integer nearest-neighbour upscale. Scale 1 is a straight copy.</summary>
        public static Color32[] UpscaleNearest(Color32[] source, int sourceWidth, int sourceHeight, int scale)
        {
            if (scale <= 1)
            {
                var copy = new Color32[source.Length];
                Array.Copy(source, copy, source.Length);
                return copy;
            }

            int width = sourceWidth * scale;
            var result = new Color32[width * sourceHeight * scale];

            for (int y = 0; y < sourceHeight * scale; y++)
            {
                int sourceRow = (y / scale) * sourceWidth;
                int targetRow = y * width;
                for (int x = 0; x < width; x++)
                    result[targetRow + x] = source[sourceRow + (x / scale)];
            }

            return result;
        }

        /// <summary>
        /// Writes one tile into its atlas slot with edge-clamp padding, matching
        /// <c>blit_padded_tile</c> exactly.
        ///
        /// TWO THINGS ARE EASY TO GET WRONG HERE.
        ///
        /// First, the Y origin. <c>GetPixels32</c> and <c>ReadPixels</c> put row 0 at the BOTTOM,
        /// while <c>BlockVisualAtlas.BuildTileRect</c> treats tile row 0 as the TOP of the image
        /// (it flips with <c>1 - maxY/H</c>). This expression is the single place that mismatch is
        /// resolved; get it wrong and the atlas is mirrored by tile row, which presents as "every
        /// block has some other block's texture".
        ///
        /// Second, the padding must overwrite the whole stride cell, not just the tile. The
        /// padding is edge-clamp REPLICATION of the tile's own border, and if the base atlas's
        /// padding were left in place the pack tile would mip-bleed into the built-in tile's
        /// colours at distance.
        /// </summary>
        public static void BlitPaddedTile(
            Color32[] atlas, int atlasWidth, int atlasHeight,
            Color32[] tile, int tileSize, int tileIndex, int scale)
        {
            int stride = BlockVisualAtlas.TileStridePixels * scale;
            int padding = BlockVisualAtlas.TilePaddingPixels * scale;

            int column = tileIndex % BlockVisualAtlas.Columns;
            int row = tileIndex / BlockVisualAtlas.Columns;

            int originX = column * stride + padding;
            int originY = atlasHeight - (row + 1) * stride + padding;

            for (int dy = -padding; dy < tileSize + padding; dy++)
            {
                int sourceY = Mathf.Clamp(dy, 0, tileSize - 1);
                int targetY = originY + dy;
                if (targetY < 0 || targetY >= atlasHeight)
                    continue;

                int sourceRow = sourceY * tileSize;
                int targetRow = targetY * atlasWidth;

                for (int dx = -padding; dx < tileSize + padding; dx++)
                {
                    int targetX = originX + dx;
                    if (targetX < 0 || targetX >= atlasWidth)
                        continue;

                    atlas[targetRow + targetX] = tile[sourceRow + Mathf.Clamp(dx, 0, tileSize - 1)];
                }
            }
        }

        /// <summary>
        /// Uploads the composed pixels with a hand-built mip chain.
        ///
        /// The chain is generated explicitly rather than via <c>Apply(updateMipmaps: true)</c>
        /// because the shipped atlases are imported with <c>mipMapsPreserveCoverage: 1</c>, which
        /// is an IMPORT-time setting that Apply does not reproduce. Without it, foliage cutouts
        /// thin out and disappear with distance while built-in tiles stay solid — a difference
        /// that only shows up several chunks away and reads as a pack being "blurry".
        /// </summary>
        static Texture2D BuildTexture(Color32[] pixels, int width, int height)
        {
            int levels = 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Min(width, height), 2));

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, levels, linear: false)
            {
                // Must match BlockVisualAtlas.AuthoredAtlasName or the material factories reject it.
                name = BlockVisualAtlas.AuthoredAtlasName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 1,
            };

            texture.SetPixelData(pixels, 0);

            float targetCoverage = MeasureCoverage(pixels);
            Color32[] level = pixels;
            int levelWidth = width;
            int levelHeight = height;

            for (int mip = 1; mip < levels; mip++)
            {
                int nextWidth = Mathf.Max(1, levelWidth / 2);
                int nextHeight = Mathf.Max(1, levelHeight / 2);

                level = Downsample(level, levelWidth, levelHeight, nextWidth, nextHeight);
                ScaleAlphaToCoverage(level, targetCoverage);

                texture.SetPixelData(level, mip);
                levelWidth = nextWidth;
                levelHeight = nextHeight;
            }

            // updateMipmaps: false -- the chain above IS the chain; letting Unity regenerate it
            // would discard the coverage correction. makeNoLongerReadable drops the CPU copy,
            // halving what the composited atlas costs in memory.
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        /// <summary>
        /// 2x2 box downsample. RGB is averaged in LINEAR space (decode sRGB, average, re-encode),
        /// which is what Unity's own generator does for an sRGB texture; averaging the encoded
        /// bytes directly darkens every mip. Alpha is linear already and is averaged as-is.
        ///
        /// Source reads are clamped because the atlas is not a power of two: at 768x480 the chain
        /// reaches odd sizes (…, 3, 1), and truncating instead of clamping would drop the last row
        /// and column from every level below that point.
        /// </summary>
        public static Color32[] Downsample(
            Color32[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            var result = new Color32[targetWidth * targetHeight];

            for (int y = 0; y < targetHeight; y++)
            {
                int y0 = Mathf.Min(y * 2, sourceHeight - 1);
                int y1 = Mathf.Min(y * 2 + 1, sourceHeight - 1);

                for (int x = 0; x < targetWidth; x++)
                {
                    int x0 = Mathf.Min(x * 2, sourceWidth - 1);
                    int x1 = Mathf.Min(x * 2 + 1, sourceWidth - 1);

                    Color32 a = source[y0 * sourceWidth + x0];
                    Color32 b = source[y0 * sourceWidth + x1];
                    Color32 c = source[y1 * sourceWidth + x0];
                    Color32 d = source[y1 * sourceWidth + x1];

                    result[y * targetWidth + x] = new Color32(
                        AverageChannelLinear(a.r, b.r, c.r, d.r),
                        AverageChannelLinear(a.g, b.g, c.g, d.g),
                        AverageChannelLinear(a.b, b.b, c.b, d.b),
                        (byte)((a.a + b.a + c.a + d.a) / 4));
                }
            }

            return result;
        }

        /// <summary>Fraction of texels at or above the cutout threshold.</summary>
        public static float MeasureCoverage(Color32[] pixels)
        {
            if (pixels.Length == 0)
                return 0.0f;

            int covered = 0;
            foreach (Color32 pixel in pixels)
                if (pixel.a >= CoverageAlphaThreshold)
                    covered++;

            return covered / (float)pixels.Length;
        }

        /// <summary>
        /// Largest alpha multiplier the coverage correction will apply. A cutout that needs more
        /// than this has lost so much alpha that boosting further would turn a faint edge into a
        /// hard one; better to under-restore than to invent geometry.
        /// </summary>
        public const float MaxCoverageAlphaScale = 4.0f;

        /// <summary>
        /// Scales this mip's alpha so its covered fraction matches mip 0's, reproducing the
        /// importer's <c>mipMapsPreserveCoverage</c>.
        ///
        /// The correction only ever scales UP. Box-downsampling averages alpha, which can only
        /// reduce the fraction of texels above the cutout threshold, so a mip whose coverage is
        /// already correct must be left alone -- scaling it down would thin foliage that was fine.
        ///
        /// Coverage is measured over the WHOLE ATLAS rather than per tile, because the importer's
        /// setting is per-texture. A per-tile correction would make pack tiles and built-in tiles
        /// thin at different rates within one image: a subtle, distance-dependent artefact that is
        /// very hard to attribute to anything.
        /// </summary>
        public static void ScaleAlphaToCoverage(Color32[] pixels, float targetCoverage)
        {
            if (targetCoverage <= 0.0f || pixels.Length == 0)
                return;

            if (CoverageAtScale(pixels, 1.0f) >= targetCoverage)
                return;   // Nothing was lost; leave it exactly as it is.

            if (CoverageAtScale(pixels, MaxCoverageAlphaScale) < targetCoverage)
            {
                ApplyAlphaScale(pixels, MaxCoverageAlphaScale);   // As close as the cap allows.
                return;
            }

            // Invariant: `low` is known to UNDER-shoot the target and `high` to meet it, so the
            // answer is always `high` at the end. Applying the last midpoint instead would be a
            // coin flip on which side of the threshold it happened to land.
            float low = 1.0f;
            float high = MaxCoverageAlphaScale;

            for (int iteration = 0; iteration < 12; iteration++)
            {
                float mid = (low + high) * 0.5f;
                if (CoverageAtScale(pixels, mid) < targetCoverage)
                    low = mid;
                else
                    high = mid;
            }

            ApplyAlphaScale(pixels, high);
        }

        static float CoverageAtScale(Color32[] pixels, float scale)
        {
            int covered = 0;
            foreach (Color32 pixel in pixels)
                if (ScaledAlpha(pixel.a, scale) >= CoverageAlphaThreshold)
                    covered++;

            return covered / (float)pixels.Length;
        }

        static void ApplyAlphaScale(Color32[] pixels, float scale)
        {
            if (Mathf.Approximately(scale, 1.0f))
                return;

            for (int i = 0; i < pixels.Length; i++)
                pixels[i].a = ScaledAlpha(pixels[i].a, scale);
        }

        static byte ScaledAlpha(byte alpha, float scale) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * scale), 0, 255);

        static byte AverageChannelLinear(byte a, byte b, byte c, byte d)
        {
            float linear = (SrgbToLinear(a) + SrgbToLinear(b) + SrgbToLinear(c) + SrgbToLinear(d)) * 0.25f;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(LinearToSrgb(linear) * 255.0f), 0, 255);
        }

        static float SrgbToLinear(byte channel)
        {
            float value = channel / 255.0f;
            return value <= 0.04045f ? value / 12.92f : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        static float LinearToSrgb(float value)
        {
            return value <= 0.0031308f ? value * 12.92f : 1.055f * Mathf.Pow(value, 1.0f / 2.4f) - 0.055f;
        }
    }
}
