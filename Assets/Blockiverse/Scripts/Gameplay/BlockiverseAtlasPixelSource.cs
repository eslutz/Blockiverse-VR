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
                    UnityEngine.Object.Destroy(readable);
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

            // markNonReadable: false -- the whole point is to read it back on the CPU.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                if (!texture.LoadImage(pngBytes, markNonReadable: false))
                    return false;

                if (texture.width != texture.height)
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
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
