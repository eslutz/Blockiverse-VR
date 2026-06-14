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
            Assert.That(atlas.width, Is.EqualTo(BlockVisualAtlas.AtlasWidthPixels));
            Assert.That(atlas.height, Is.EqualTo(BlockVisualAtlas.AtlasHeightPixels));
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(4));

            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
            Assert.That(androidSettings.overridden, Is.True);
            Assert.That(androidSettings.textureCompression, Is.Not.EqualTo(TextureImporterCompression.Uncompressed));
            // The atlas is non-square; Android max texture size must be large
            // enough to hold the larger dimension without downscaling (which would misalign tiles), and
            // no larger than the next power of two above it to keep the Quest texture budget tight.
            int largestAtlasDimension = Math.Max(BlockVisualAtlas.AtlasWidthPixels, BlockVisualAtlas.AtlasHeightPixels);
            Assert.That(androidSettings.maxTextureSize, Is.GreaterThanOrEqualTo(largestAtlasDimension));
            Assert.That(androidSettings.maxTextureSize, Is.LessThanOrEqualTo(NextPowerOfTwo(largestAtlasDimension)));
        }

        [Test]
        public void AuthoredBlockSourceTilesUseProductionResolution()
        {
            Assert.That(BlockVisualAtlas.TilePixels, Is.EqualTo(32));

            foreach (BlockDefinition block in BlockRegistry.CreateDefault().All.Where(block => block.Id != BlockRegistry.Air))
            {
                string path = $"Assets/Blockiverse/Art/Textures/Blocks/Source/{block.CanonicalId}.png";
                Texture2D sourceTile = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                Assert.That(sourceTile, Is.Not.Null, $"Missing block source tile: {path}");
                Assert.That(sourceTile.width, Is.EqualTo(BlockVisualAtlas.TilePixels), $"{block.CanonicalId} source width should match atlas tile size.");
                Assert.That(sourceTile.height, Is.EqualTo(BlockVisualAtlas.TilePixels), $"{block.CanonicalId} source height should match atlas tile size.");
            }
        }

        [Test]
        public void ArtGeneratorCoversEveryRegisteredBlockTexture()
        {
            string generatorSource = File.ReadAllText("scripts/art/generate-art-assets.py");

            foreach (BlockDefinition block in BlockRegistry.CreateDefault().All.Where(block => block.Id != BlockRegistry.Air))
                Assert.That(
                    generatorSource,
                    Does.Contain($"\"{block.CanonicalId}\""),
                    $"Art generator must produce {block.CanonicalId}; committed source tiles are not enough.");
        }

        [Test]
        public void LaunchArtworkUsesCompressedFilteredMipmappedImportSettings()
        {
            Texture2D launchArtwork = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockiverseProject.LaunchArtworkPath);
            TextureImporter importer = AssetImporter.GetAtPath(BlockiverseProject.LaunchArtworkPath) as TextureImporter;

            Assert.That(launchArtwork, Is.Not.Null);
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.mipmapEnabled, Is.True, "Launch backdrop is scaled in VR UI and must sample from mipmaps.");
            Assert.That(importer.streamingMipmaps, Is.True, "Large menu backdrop should participate in texture streaming.");
            Assert.That(importer.filterMode, Is.Not.EqualTo(FilterMode.Point), "Launch backdrop should not use blocky point filtering.");

            TextureImporterPlatformSettings defaultSettings = importer.GetDefaultPlatformTextureSettings();
            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");

            Assert.That(defaultSettings.textureCompression, Is.Not.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(androidSettings.overridden, Is.True);
            Assert.That(androidSettings.textureCompression, Is.Not.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(androidSettings.maxTextureSize, Is.LessThanOrEqualTo(2048));
        }

        [Test]
        public void GeneratedUiAndVfxSpritesAreReferencedByBootstrapperWiring()
        {
            string[] bootstrapperFiles = Directory
                .GetFiles("Assets/Blockiverse/Scripts/Editor", "BlockiverseProjectBootstrapper*.cs")
                .OrderBy(path => path)
                .ToArray();
            string bootstrapperSource = string.Join("\n", bootstrapperFiles.Select(File.ReadAllText));

            foreach (string spriteName in UiSpriteNames.Concat(VfxSpriteNames))
                Assert.That(bootstrapperSource, Does.Contain(spriteName), $"{spriteName} must be wired into generated runtime UI/VFX.");
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
        public void ItemTextureFilesMatchRegisteredItemIds()
        {
            var registeredIds = new HashSet<string>(
                ItemRegistry.CreateDefault().All
                    .Where(item => !item.Id.IsNone)
                    .Select(item => item.Id.Value),
                StringComparer.OrdinalIgnoreCase);
            string[] orphanTextureIds = Directory
                .GetFiles("Assets/Blockiverse/Art/Textures/Items", "*.png", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(id => !registeredIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(orphanTextureIds, Is.Empty, $"Item icon textures without registered item IDs: {string.Join(", ", orphanTextureIds)}");
        }

        [Test]
        public void EveryRegisteredItemResolvesGeneratedIconSprite()
        {
            ItemDefinition[] items = ItemRegistry.CreateDefault().All
                .Where(item => !item.Id.IsNone)
                .ToArray();
            string[] ids = items.Select(item => item.Id.Value).ToArray();
            Sprite[] sprites = ids
                .Select(id => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Blockiverse/Art/Textures/Items/{id}.png"))
                .ToArray();
            var iconLibraryObject = new GameObject("Item Icon Library Test");
            try
            {
                BlockiverseItemIconLibrary library = iconLibraryObject.AddComponent<BlockiverseItemIconLibrary>();
                library.Configure(ids, sprites);

                foreach (ItemDefinition item in items)
                {
                    Assert.That(
                        library.TryGetIcon(item.Id, out Sprite sprite),
                        Is.True,
                        $"Expected icon library to resolve {item.Id.Value}.");
                    Assert.That(sprite, Is.Not.Null, $"Expected generated icon sprite for {item.Id.Value}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(iconLibraryObject);
            }
        }

        [Test]
        public void IconLibraryConfigureRejectsMismatchedArrayLengths()
        {
            var iconLibraryObject = new GameObject("Item Icon Library Test");
            try
            {
                BlockiverseItemIconLibrary library = iconLibraryObject.AddComponent<BlockiverseItemIconLibrary>();

                Assert.Throws<ArgumentException>(() =>
                    library.Configure(new[] { ItemId.ReedFiber.Value }, Array.Empty<Sprite>()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(iconLibraryObject);
            }
        }

        [Test]
        public void AuthoredAtlasMaterialUsesExpectedTexture()
        {
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);

            Material material = BlockVisualAtlas.CreateMaterial(sourceMaterial);

            Assert.That(material, Is.Not.Null);
            Assert.That(BlockVisualAtlas.TryGetBaseTexture(material, out Texture texture), Is.True);
            Assert.That(texture, Is.SameAs(AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath)));
            Assert.That(BlockVisualAtlas.IsAuthoredAtlasTexture(texture), Is.True);
        }

        [Test]
        public void VoxelLitShaderIsAlwaysIncludedForPlayerBuilds()
        {
            string shaderPath = "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader";
            string shaderGuid = AssetDatabase.AssetPathToGUID(shaderPath);
            string graphicsSettings = File.ReadAllText("ProjectSettings/GraphicsSettings.asset");

            Assert.That(shaderGuid, Is.Not.Empty);
            Assert.That(graphicsSettings, Does.Contain($"guid: {shaderGuid}"));
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
                BlockVisualAtlas.AtlasWidthPixels,
                BlockVisualAtlas.AtlasHeightPixels,
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
