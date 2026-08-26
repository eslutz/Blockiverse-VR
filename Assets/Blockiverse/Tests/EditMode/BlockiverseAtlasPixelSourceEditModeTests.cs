using Blockiverse.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    // The dimension gate that stops a decode-time memory bomb: a highly compressible PNG can
    // declare an enormous pixel count while staying tiny on disk, and Texture2D.LoadImage decodes
    // and allocates the full image BEFORE any shape check runs. TryPeekPngDimensions reads the
    // IHDR chunk directly -- no decode, no allocation -- so the size can be refused first.
    public sealed class BlockiverseAtlasPixelSourceEditModeTests
    {
        static byte[] BuildMinimalPngHeader(uint width, uint height)
        {
            // Signature (8) + chunk length (4, unused by the peek) + "IHDR" (4) + width (4 BE) +
            // height (4 BE) = 24 bytes. No IDAT, no real pixel data -- the peek never needs it.
            var bytes = new byte[24];
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            signature.CopyTo(bytes, 0);
            bytes[8] = 0; bytes[9] = 0; bytes[10] = 0; bytes[11] = 13;   // IHDR length, per spec
            bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
            WriteUInt32BigEndian(bytes, 16, width);
            WriteUInt32BigEndian(bytes, 20, height);
            return bytes;
        }

        static void WriteUInt32BigEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        [Test]
        public void PeekReadsDimensionsFromTheHeaderAloneWithNoPixelData()
        {
            byte[] header = BuildMinimalPngHeader(32, 32);

            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out int width, out int height), Is.True);
            Assert.That(width, Is.EqualTo(32));
            Assert.That(height, Is.EqualTo(32));
        }

        [Test]
        public void PeekDistinguishesWidthFromHeight()
        {
            byte[] header = BuildMinimalPngHeader(16, 48);

            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out int width, out int height), Is.True);
            Assert.That(width, Is.EqualTo(16));
            Assert.That(height, Is.EqualTo(48));
        }

        [Test]
        public void PeekSucceedsForAnEnormousDeclaredSizeWithNoRealBody()
        {
            // The peek itself does not judge size -- it only reads what the header claims. Proving
            // it succeeds on a 24-byte, no-body "PNG" claiming 100000x100000 is what shows the
            // size gate in TryDecodeTile runs BEFORE any allocation, not as a side effect of a
            // decode that would have failed anyway on the missing pixel data.
            byte[] header = BuildMinimalPngHeader(100_000, 100_000);

            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out int width, out int height), Is.True);
            Assert.That(width, Is.EqualTo(100_000));
            Assert.That(height, Is.EqualTo(100_000));
        }

        [TestCase(0, TestName = "empty")]
        [TestCase(23, TestName = "one byte short of IHDR")]
        public void PeekRejectsBytesTooShortToHoldAHeader(int length)
        {
            var bytes = new byte[length];
            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(bytes, out _, out _), Is.False);
        }

        [Test]
        public void PeekRejectsNull()
        {
            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(null, out _, out _), Is.False);
        }

        [Test]
        public void PeekRejectsAWrongSignature()
        {
            byte[] header = BuildMinimalPngHeader(32, 32);
            header[0] = 0x00;   // Corrupt the PNG magic byte.

            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out _, out _), Is.False);
        }

        [Test]
        public void PeekRejectsAChunkThatIsNotIHDR()
        {
            byte[] header = BuildMinimalPngHeader(32, 32);
            header[12] = (byte)'I'; header[13] = (byte)'D'; header[14] = (byte)'A'; header[15] = (byte)'T';

            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out _, out _), Is.False,
                "IHDR must be the first chunk per the PNG spec; a differently-typed chunk there is not trustworthy.");
        }

        [Test]
        public void PeekRejectsAZeroDeclaredDimension()
        {
            byte[] header = BuildMinimalPngHeader(0, 32);
            Assert.That(BlockiverseAtlasPixelSource.TryPeekPngDimensions(header, out _, out _), Is.False);
        }

        // ── the actual gate ─────────────────────────────────────────────────

        [Test]
        public void TryDecodeTileRefusesAPngLargerThanAnyManifestCanDeclare()
        {
            // THE regression test. No manifest can declare a tile above MaxDecodableTilePixels
            // (BlockiverseTexturePackManifest.SupportedTilePixels tops out at 64), so anything
            // larger reaching this call is either corrupt or hostile input, and must be refused
            // without Texture2D.LoadImage ever allocating it.
            int oversized = BlockiverseAtlasPixelSource.MaxDecodableTilePixels + 1;
            byte[] png = EncodeSolidPng(oversized, oversized);

            var source = new BlockiverseAtlasPixelSource();
            bool decoded = source.TryDecodeTile(png, out Color32[] pixels, out int size);

            Assert.That(decoded, Is.False, "An oversized tile was decoded instead of refused.");
            Assert.That(pixels, Is.Null);
            Assert.That(size, Is.EqualTo(0));
        }

        [Test]
        public void TryDecodeTileAcceptsATileExactlyAtTheLimit()
        {
            // The gate must not be off-by-one in the safe direction either -- 64px packs are
            // explicitly supported (BlockiverseTexturePackManifest.SupportedTilePixels).
            int atLimit = BlockiverseAtlasPixelSource.MaxDecodableTilePixels;
            byte[] png = EncodeSolidPng(atLimit, atLimit);

            var source = new BlockiverseAtlasPixelSource();
            Assert.That(source.TryDecodeTile(png, out Color32[] pixels, out int size), Is.True);
            Assert.That(size, Is.EqualTo(atLimit));
            Assert.That(pixels, Is.Not.Null);
        }

        [Test]
        public void TryDecodeTileAcceptsAnOrdinaryTile()
        {
            byte[] png = EncodeSolidPng(32, 32);

            var source = new BlockiverseAtlasPixelSource();
            Assert.That(source.TryDecodeTile(png, out Color32[] pixels, out int size), Is.True);
            Assert.That(size, Is.EqualTo(32));
            Assert.That(pixels.Length, Is.EqualTo(32 * 32));
        }

        [Test]
        public void TryDecodeTileRejectsANonSquareTile()
        {
            byte[] png = EncodeSolidPng(32, 16);

            var source = new BlockiverseAtlasPixelSource();
            Assert.That(source.TryDecodeTile(png, out Color32[] pixels, out _), Is.False);
            Assert.That(pixels, Is.Null);
        }

        static byte[] EncodeSolidPng(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(120, 60, 30, 255);
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false);
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
