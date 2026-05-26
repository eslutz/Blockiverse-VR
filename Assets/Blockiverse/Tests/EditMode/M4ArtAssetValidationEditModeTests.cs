using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Blockiverse.Tests.EditMode
{
    public sealed class M4ArtAssetValidationEditModeTests
    {
        static readonly string[] BlockTextureNames =
        {
            "meadow_turf",
            "loam",
            "slate",
            "clearstone",
            "timber",
            "leafmass",
            "coalstone",
            "copperstone",
            "ironstone",
            "workbench",
            "storage_crate",
            "torchbud"
        };

        static readonly string[] ItemIconNames =
        {
            "timber_chunk",
            "slate_shard",
            "copper_nugget",
            "iron_nugget",
            "workbench_kit",
            "crate_kit",
            "chipper_tool",
            "pick_tool"
        };

        static readonly string[] UiSpriteNames =
        {
            "hotbar_frame",
            "selected_slot",
            "health_pip",
            "inventory_panel",
            "crafting_panel",
            "multiplayer_status_badge"
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
            Assert.That(androidSettings.maxTextureSize, Is.LessThanOrEqualTo(BlockVisualAtlas.Columns * BlockVisualAtlas.TilePixels));
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
        public void RequiredM4ArtAssetsAreCommittedWithMetaFiles()
        {
            var expectedPaths = new List<string>();
            expectedPaths.Add(BlockVisualAtlas.AuthoredAtlasPath);
            expectedPaths.AddRange(BlockTextureNames.Select(name => $"Assets/Blockiverse/Art/Textures/Blocks/Source/{name}.png"));
            expectedPaths.AddRange(ItemIconNames.Select(name => $"Assets/Blockiverse/Art/Textures/Items/{name}.png"));
            expectedPaths.AddRange(UiSpriteNames.Select(name => $"Assets/Blockiverse/Art/Sprites/UI/{name}.png"));

            foreach (string path in expectedPaths)
            {
                Assert.That(File.Exists(path), Is.True, $"Missing asset: {path}");
                Assert.That(File.Exists($"{path}.meta"), Is.True, $"Missing Unity meta file: {path}.meta");
            }
        }

        [Test]
        public void AuthoredAtlasIsSelectedBeforeProceduralFallback()
        {
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.TestBlockMaterialPath);

            BlockVisualMaterialResult result = BlockVisualAtlas.CreateMaterial(sourceMaterial, allowProceduralFallback: false);

            Assert.That(result.Source, Is.EqualTo(BlockTextureSource.AuthoredAtlas));
            Assert.That(result.Material, Is.Not.Null);
            Assert.That(BlockVisualAtlas.TryGetBaseTexture(result.Material, out Texture texture), Is.True);
            Assert.That(texture, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath)));
        }

        [Test]
        public void ProceduralFallbackRequiresExplicitPermission()
        {
            Material sourceMaterial = new(Shader.Find("Sprites/Default"));

            try
            {
                Assert.Throws<InvalidOperationException>(() => BlockVisualAtlas.CreateMaterial(sourceMaterial, allowProceduralFallback: false));

                BlockVisualMaterialResult result = BlockVisualAtlas.CreateMaterial(sourceMaterial, allowProceduralFallback: true);

                Assert.That(result.Source, Is.EqualTo(BlockTextureSource.ProceduralFallback));
                Assert.That(result.Material, Is.Not.Null);
                Assert.That(BlockVisualAtlas.TryGetBaseTexture(result.Material, out Texture texture), Is.True);
                Assert.That(texture.name, Does.Contain("Generated Block Atlas"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceMaterial);
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
