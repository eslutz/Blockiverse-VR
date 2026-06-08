using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class M4ArtAssetValidationEditModeTests
    {
        static int NextPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
                power <<= 1;
            return power;
        }

        static readonly string[] UiSpriteNames =
        {
            "hotbar_frame",
            "selected_slot",
            "health_pip",
            "inventory_panel",
            "crafting_panel",
            "multiplayer_status_badge",
            "settings_panel",
            "feedback_toast"
        };

        static readonly string[] VfxSpriteNames =
        {
            "block_dust_particle",
            "block_puff_particle",
            "resource_spark_particle",
            "craft_spark_particle",
            "rain_splash_particle",
            "snowflake_particle",
            "fog_wisp_particle",
            "ember_particle"
        };

        [Test]
        public void AuthoredBlockAtlasExistsWithExpectedDimensionsAndImportSettings()
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath);
            TextureImporter importer = AssetImporter.GetAtPath(BlockVisualAtlas.AuthoredAtlasPath) as TextureImporter;

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels));
            Assert.That(atlas.height, Is.EqualTo(BlockVisualAtlas.Rows * BlockVisualAtlas.TilePixels));
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.False);

            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
            Assert.That(androidSettings.overridden, Is.True);
            // The atlas is non-square (8×9 tiles → 128×144 px). Android max texture size must be large
            // enough to hold the larger dimension without downscaling (which would misalign tiles), and
            // no larger than the next power of two above it to keep the Quest texture budget tight.
            int largestAtlasDimension = Math.Max(BlockVisualAtlas.Columns, BlockVisualAtlas.Rows) * BlockVisualAtlas.TilePixels;
            Assert.That(androidSettings.maxTextureSize, Is.GreaterThanOrEqualTo(largestAtlasDimension));
            Assert.That(androidSettings.maxTextureSize, Is.LessThanOrEqualTo(NextPowerOfTwo(largestAtlasDimension)));
        }

        [Test]
        public void EveryRenderableBlockHasAuthoredAtlasMapping()
        {
            BlockDefinition[] renderableBlocks = BlockRegistry.CreateDefault().All
                .Where(block => block.IsRenderable)
                .ToArray();

            foreach (BlockDefinition block in renderableBlocks)
            {
                Assert.That(
                    BlockVisualAtlas.HasAuthoredTile(block.Id),
                    Is.True,
                    $"Expected authored atlas mapping for {block.Name} ({block.Id}).");
            }
        }

        [Test]
        public void RequiredPhase14ArtAssetsAreCommittedWithMetaFiles()
        {
            var expectedPaths = new List<string>();
            expectedPaths.Add(BlockVisualAtlas.AuthoredAtlasPath);
            expectedPaths.AddRange(BlockRegistry.CreateDefault().All
                .Where(block => block.Id != BlockRegistry.Air)
                .Select(block => $"Assets/Blockiverse/Art/Textures/Blocks/Source/{block.CanonicalId}.png"));
            expectedPaths.AddRange(ItemRegistry.CreateDefault().All
                .Where(item => !item.Id.IsNone)
                .Select(item => $"Assets/Blockiverse/Art/Textures/Items/{item.Id.Value}.png"));
            expectedPaths.AddRange(UiSpriteNames.Select(name => $"Assets/Blockiverse/Art/Sprites/UI/{name}.png"));
            expectedPaths.AddRange(VfxSpriteNames.Select(name => $"Assets/Blockiverse/Art/Sprites/VFX/{name}.png"));

            foreach (string path in expectedPaths)
            {
                Assert.That(File.Exists(path), Is.True, $"Missing asset: {path}");
                Assert.That(File.Exists($"{path}.meta"), Is.True, $"Missing Unity meta file: {path}.meta");
            }
        }

        [Test]
        public void AuthoredAtlasMaterialUsesExpectedTexture()
        {
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.TestBlockMaterialPath);

            Material material = BlockVisualAtlas.CreateMaterial(sourceMaterial);

            Assert.That(material, Is.Not.Null);
            Assert.That(BlockVisualAtlas.TryGetBaseTexture(material, out Texture texture), Is.True);
            Assert.That(texture, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath)));
            Assert.That(BlockVisualAtlas.IsAuthoredAtlasTexture(texture), Is.True);
        }

        [Test]
        public void MissingAuthoredAtlasFailsFast()
        {
            Material sourceMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => BlockVisualAtlas.CreateMaterial(sourceMaterial));
                Assert.That(exception.Message, Does.Contain(BlockVisualAtlas.AuthoredAtlasPath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMaterial);
            }
        }

        [Test]
        public void UnrelatedTextureIsRejectedAsAuthoredAtlas()
        {
            Material sourceMaterial = new(Shader.Find("Sprites/Default"));
            Texture2D unrelatedTexture = new(
                BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels,
                BlockVisualAtlas.Rows * BlockVisualAtlas.TilePixels,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = "unrelated_texture"
            };

            try
            {
                sourceMaterial.mainTexture = unrelatedTexture;

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => BlockVisualAtlas.CreateMaterial(sourceMaterial));

                Assert.That(exception.Message, Does.Contain("not the expected authored atlas"));
                Assert.That(BlockVisualAtlas.IsAuthoredAtlasTexture(unrelatedTexture), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMaterial);
                UnityEngine.Object.DestroyImmediate(unrelatedTexture);
            }
        }

        [Test]
        public void M4ArtAssetFileNamesAvoidForbiddenReferences()
        {
            string[] forbiddenTokens =
            {
                "minecraft",
                "creeper",
                "steve",
                "enderman",
                "mojang"
            };

            string[] assetPaths = Directory.GetFiles("Assets/Blockiverse/Art", "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string assetPath in assetPaths)
            {
                string normalized = assetPath.Replace('\\', '/').ToLowerInvariant();

                foreach (string forbiddenToken in forbiddenTokens)
                    Assert.That(normalized, Does.Not.Contain(forbiddenToken), $"Forbidden token in asset path: {assetPath}");
            }
        }
    }
}
