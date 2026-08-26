using System;
using Blockiverse.Core;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// The one place texture-pack compositing touches the GPU or a PNG decoder.
    ///
    /// It exists so the compositor itself can be pure <c>Color32[]</c> arithmetic and therefore
    /// testable under EditMode's <c>-nographics</c>, where <c>Graphics.Blit</c> and
    /// <c>ReadPixels</c> are not available. Tests substitute a fake; the player uses
    /// <see cref="BlockiverseAtlasPixelSource"/>.
    /// </summary>
    public interface IBlockiverseAtlasPixelSource
    {
        /// <summary>Reads a shipped atlas back to CPU pixels. Null when it cannot be read.</summary>
        Color32[] ReadAtlas(Texture2D atlas);

        /// <summary>Decodes a square PNG. False when it is not a PNG or is not square.</summary>
        bool TryDecodeTile(byte[] pngBytes, out Color32[] pixels, out int size);
    }

    /// <summary>
    /// The real implementation: a GPU round trip for the shipped atlas, and Unity's PNG decoder
    /// for pack tiles.
    /// </summary>
    public sealed class BlockiverseAtlasPixelSource : IBlockiverseAtlasPixelSource
    {
        // UnityEngine.Object.Destroy is illegal outside Play mode -- it logs an error and no-ops
        // rather than throwing, which is exactly the kind of failure a test-runner treats as a
        // hard failure while gameplay code never notices. Matches the idiom already established in
        // BlockiverseWorldPresentation.DestroyGenerated.
        static void DestroyTemporary(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// Largest tile dimension this decoder will allocate for. No manifest can legitimately
        /// declare a larger tile (<see cref="BlockiverseTexturePackManifest.SupportedTilePixels"/>
        /// tops out at 64), so anything above this is either corruption or a hostile file and is
        /// refused before <c>Texture2D.LoadImage</c> ever runs.
        /// </summary>
        public static readonly int MaxDecodableTilePixels = Max(BlockiverseTexturePackManifest.SupportedTilePixels);

        static int Max(int[] values)
        {
            int max = 0;
            foreach (int value in values)
                if (value > max)
                    max = value;
            return max;
        }

        /// <summary>
        /// Reads the shipped atlas, which is imported with <c>isReadable: 0</c> and so has no CPU
        /// copy to call <c>GetPixels32</c> on.
        ///
        /// A GPU round trip is used rather than flipping the importer to readable, because all
        /// four atlases are referenced by a serialized array on the scene component and therefore
        /// load at scene load — making them readable would cost every player about 4 MB of
        /// permanent system memory, including the overwhelming majority who never install a pack.
        /// This way the cost is one ~1.4 MB readback, once, only when a pack is actually applied.
        ///
        /// <c>RenderTextureReadWrite.sRGB</c> IS LOAD-BEARING AND IS THE TRAP HERE. The project
        /// renders in Linear space (ProjectSettings m_ActiveColorSpace: 1) and the atlas is
        /// imported as sRGB, so sampling it in a Blit yields linear values. An sRGB render target
        /// re-encodes on write, making the round trip an identity. A Linear target instead stores
        /// the linear values verbatim, producing a washed-out atlas that looks entirely plausible
        /// in a screenshot and wrong on device. Alpha is never colour-converted either way, so
        /// cutout coverage survives exactly.
        /// </summary>
        public Color32[] ReadAtlas(Texture2D atlas)
        {
            if (atlas == null)
                return null;

            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                temporary = RenderTexture.GetTemporary(
                    atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                temporary.filterMode = FilterMode.Point;

                Graphics.Blit(atlas, temporary);
                RenderTexture.active = temporary;

                readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, mipChain: false, linear: false);
                readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, recalculateMipMaps: false);
                readable.Apply(updateMipmaps: false);

                return readable.GetPixels32();
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not read the block atlas for texture pack compositing: {exception.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null)
                    RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                    DestroyTemporary(readable);
            }
        }

        /// <summary>
        /// Decodes a pack tile. Rejects anything non-square up front: the compositor's upscale is
        /// integer-only, so a non-square tile has no correct interpretation and guessing one would
        /// silently distort a pack author's art.
        /// </summary>
        public bool TryDecodeTile(byte[] pngBytes, out Color32[] pixels, out int size)
        {
            pixels = null;
            size = 0;

            if (pngBytes == null || pngBytes.Length == 0)
                return false;

            // Bound the DECODED size before allocating anything. The 4 MiB cap on the compressed
            // file (BlockiverseTexturePackLibrary.MaxTilePngBytes) does not bound this: a highly
            // compressible PNG -- a huge field of one colour -- can declare an enormous pixel
            // count while staying tiny on disk. LoadImage decodes and allocates the full texture
            // BEFORE any shape check runs, so on a memory-constrained Quest an unbounded call here
            // can exhaust memory or crash the app from selecting a single malicious pack.
            if (!TryPeekPngDimensions(pngBytes, out int declaredWidth, out int declaredHeight))
                return false;

            if (declaredWidth > MaxDecodableTilePixels || declaredHeight > MaxDecodableTilePixels)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Refusing to decode a {declaredWidth}x{declaredHeight} texture pack tile: "
                    + $"exceeds the {MaxDecodableTilePixels}px limit.");
                return false;
            }

            // markNonReadable: false -- the whole point is to read it back on the CPU.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                if (!texture.LoadImage(pngBytes, markNonReadable: false))
                    return false;

                if (texture.width != texture.height)
                    return false;

                // Defence in depth: the header claimed a size within bounds, but a decoder is not
                // obligated to agree with a chunk it deems corrupt. Trust what was actually
                // allocated, not what was declared.
                if (texture.width > MaxDecodableTilePixels)
                    return false;

                pixels = texture.GetPixels32();
                size = texture.width;
                return true;
            }
            catch (Exception exception)
            {
                BlockiverseLog.Warning(
                    BlockiverseLogCategory.Assets,
                    $"Could not decode a texture pack tile: {exception.Message}");
                return false;
            }
            finally
            {
                DestroyTemporary(texture);
            }
        }

        /// <summary>
        /// Reads the pixel dimensions straight out of a PNG's IHDR chunk, without decoding a
        /// single pixel. Pure byte parsing -- no Unity types -- so it is EditMode-testable and
        /// cheap enough to run before every decode as a size gate.
        ///
        /// Per the PNG spec, IHDR is always the first chunk and is always exactly 13 bytes of
        /// data, so its layout is fixed: 8-byte signature, 4-byte chunk length, 4-byte chunk type
        /// ("IHDR"), then a 4-byte big-endian width and a 4-byte big-endian height. This is the
        /// same fixed-offset trick most PNG dimension readers use rather than pulling in a decoder.
        /// </summary>
        public static bool TryPeekPngDimensions(byte[] pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            // 8 signature + 4 length + 4 "IHDR" + 4 width + 4 height.
            if (pngBytes == null || pngBytes.Length < 24)
                return false;

            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < signature.Length; i++)
                if (pngBytes[i] != signature[i])
                    return false;

            if (pngBytes[12] != (byte)'I' || pngBytes[13] != (byte)'H'
                || pngBytes[14] != (byte)'D' || pngBytes[15] != (byte)'R')
            {
                return false;   // Not a PNG this fixed-offset read can trust.
            }

            width = ReadUInt32BigEndianClamped(pngBytes, 16);
            height = ReadUInt32BigEndianClamped(pngBytes, 20);
            return width > 0 && height > 0;
        }

        static int ReadUInt32BigEndianClamped(byte[] bytes, int offset)
        {
            uint value = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

            // A declared dimension near uint.MaxValue is certainly corrupt or hostile input, not a
            // legitimate 4-billion-pixel image. Clamp rather than overflow into `int`, so the
            // caller's ">" bound check still refuses it instead of silently wrapping negative.
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
