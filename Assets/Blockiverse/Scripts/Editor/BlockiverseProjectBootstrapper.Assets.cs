using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.MetaAvatars;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.UI;
using Blockiverse.VR;
using Oculus.Avatar2;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Editor.Configuration;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace Blockiverse.Editor
{
    public static partial class BlockiverseProjectBootstrapper
    {
        static void EnsureInteractionMaterials()
        {
            EnsureMaterial(BlockiverseProject.PointerLineMaterialPath, PointerLineColor, preferUnlit: true);
            EnsureFluidAtlasTiles();
            EnsureBlockItemIcons();
            EnsureBlockTextureMaterial();
        }

        // Every registered item needs a committed inventory icon. Block-mapped items (terrain,
        // stone, deep rock) that lack an authored icon derive one from their 16×16 block source
        // tile, so a newly registered block doesn't leave an icon gap. Additive: never overwrites
        // an authored icon.
        static void EnsureBlockItemIcons()
        {
            foreach (ItemDefinition item in ItemRegistry.Default.All)
            {
                if (item.Id.IsNone)
                    continue;

                string iconPath = $"Assets/Blockiverse/Art/Textures/Items/{item.Id.Value}.png";
                if (File.Exists(iconPath))
                    continue;

                string sourcePath = $"Assets/Blockiverse/Art/Textures/Blocks/Source/{item.Id.Value}.png";
                if (!File.Exists(sourcePath))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
                File.Copy(sourcePath, iconPath);
                AssetDatabase.ImportAsset(iconPath);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, $"Derived item icon {item.Id.Value}.png from its block source tile.");
            }
        }

        // Atlas tile indexes assigned to the fluid blocks in BlockVisualAtlas.TileIndexByBlockId.
        const int FreshwaterAtlasTileIndex = 73;
        const int BrineAtlasTileIndex = 74;
        const int EmberflowAtlasTileIndex = 75;

        // Paints the freshwater/brine/emberflow tiles into the authored block atlas. Strictly
        // additive and deterministic: a tile is only painted while it is still blank (fully
        // transparent or one uniform placeholder color), so authored pixels are never touched
        // and reruns are no-ops.
        // Keep this editor fallback aligned with scripts/art/generate-art-assets.py: runtime
        // tiles sit inside duplicated padding so mipmaps cannot bleed neighboring block art.
        static void EnsureFluidAtlasTiles()
        {
            // Source tiles back the atlas tiles (every block needs a committed source PNG); write
            // them from the same pixel functions so source and atlas stay consistent. Flow cells
            // render with their family's source tile, so their source PNGs reuse the family pixels.
            EnsureFluidSourceTile("freshwater", FreshwaterTilePixel);
            EnsureFluidSourceTile("brine", BrineTilePixel);
            EnsureFluidSourceTile("emberflow", EmberflowTilePixel);
            EnsureFluidSourceTile("freshwater_flow", FreshwaterTilePixel);
            EnsureFluidSourceTile("brine_flow", BrineTilePixel);
            EnsureFluidSourceTile("emberflow_flow", EmberflowTilePixel);

            string path = BlockVisualAtlas.AuthoredAtlasPath;
            if (!File.Exists(path))
                return;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)) ||
                    texture.width != BlockVisualAtlas.AtlasWidthPixels ||
                    texture.height != BlockVisualAtlas.AtlasHeightPixels)
                {
                    BlockiverseLog.Warning(
                        BlockiverseLogCategory.Bootstrap,
                        $"Authored block atlas at {path} is missing or not the expected size; fluid tiles were not painted.");
                    return;
                }

                bool painted = TryPaintAtlasTile(texture, FreshwaterAtlasTileIndex, FreshwaterTilePixel);
                painted |= TryPaintAtlasTile(texture, BrineAtlasTileIndex, BrineTilePixel);
                painted |= TryPaintAtlasTile(texture, EmberflowAtlasTileIndex, EmberflowTilePixel);

                if (!painted)
                    return;

                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, "Painted fluid tiles into the authored block atlas.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Writes a 16×16 source tile PNG for a fluid block (additive — never overwrites a committed
        // source). The art-asset validation requires one source PNG per renderable block.
        static void EnsureFluidSourceTile(string canonicalId, Func<int, int, Color32> pixelAt)
        {
            string path = $"Assets/Blockiverse/Art/Textures/Blocks/Source/{canonicalId}.png";
            if (File.Exists(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            int size = BlockVisualAtlas.TilePixels;
            var tile = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            try
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                        tile.SetPixel(x, y, pixelAt(x, y));
                }

                tile.Apply();
                File.WriteAllBytes(path, tile.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                BlockiverseLog.Info(BlockiverseLogCategory.Bootstrap, $"Wrote fluid source tile {canonicalId}.png.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }

        static bool TryPaintAtlasTile(Texture2D atlas, int tileIndex, Func<int, int, Color32> pixelAt)
        {
            int column = tileIndex % BlockVisualAtlas.Columns;
            int row = tileIndex / BlockVisualAtlas.Columns;
            int originX = column * BlockVisualAtlas.TileStridePixels + BlockVisualAtlas.TilePaddingPixels;
            // Tile rows count from the top of the atlas; texture pixel rows from the bottom.
            int originY = (BlockVisualAtlas.Rows - 1 - row) * BlockVisualAtlas.TileStridePixels + BlockVisualAtlas.TilePaddingPixels;

            if (!IsAtlasTileBlank(atlas, originX, originY))
                return false;

            for (int y = -BlockVisualAtlas.TilePaddingPixels; y < BlockVisualAtlas.TilePixels + BlockVisualAtlas.TilePaddingPixels; y++)
            {
                for (int x = -BlockVisualAtlas.TilePaddingPixels; x < BlockVisualAtlas.TilePixels + BlockVisualAtlas.TilePaddingPixels; x++)
                {
                    int sourceX = Mathf.Clamp(x, 0, BlockVisualAtlas.TilePixels - 1);
                    int sourceY = Mathf.Clamp(y, 0, BlockVisualAtlas.TilePixels - 1);
                    atlas.SetPixel(originX + x, originY + y, pixelAt(sourceX, sourceY));
                }
            }

            return true;
        }

        // Blank = fully transparent or one uniform fill. Authored 16px tiles always vary, so this
        // can never overwrite real art.
        static bool IsAtlasTileBlank(Texture2D atlas, int originX, int originY)
        {
            Color32 first = atlas.GetPixel(originX, originY);
            bool allTransparent = true;
            bool allUniform = true;

            for (int y = 0; y < BlockVisualAtlas.TilePixels; y++)
            {
                for (int x = 0; x < BlockVisualAtlas.TilePixels; x++)
                {
                    Color32 pixel = atlas.GetPixel(originX + x, originY + y);
                    allTransparent &= pixel.a == 0;
                    allUniform &= pixel.r == first.r && pixel.g == first.g && pixel.b == first.b && pixel.a == first.a;
                }
            }

            return allTransparent || allUniform;
        }

        // Calm freshwater: blue body with wave crest/trough bands and sparse sparkle pixels.
        static Color32 FreshwaterTilePixel(int x, int y)
        {
            if ((x * 7 + y * 13) % 23 == 0)
                return new Color32(208, 232, 248, 255);

            int band = (y + ((x + y * 3) % 4 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(64, 124, 198, 255);
            if (band == 2)
                return new Color32(34, 76, 148, 255);

            return new Color32(45, 96, 172, 255);
        }

        // Brine: muted teal-green body with pale salt flecks.
        static Color32 BrineTilePixel(int x, int y)
        {
            if ((x * 11 + y * 7) % 19 == 0)
                return new Color32(226, 234, 226, 255);

            int band = (y + ((x + y * 2) % 5 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(72, 138, 134, 255);
            if (band == 2)
                return new Color32(30, 84, 86, 255);

            return new Color32(44, 108, 106, 255);
        }

        // Emberflow: molten red-orange body bands with bright ember streaks and dark crust flecks.
        static Color32 EmberflowTilePixel(int x, int y)
        {
            if ((x * 5 + y * 11) % 29 == 0)
                return new Color32(255, 232, 150, 255);

            if ((x * 13 + y * 3) % 31 == 0)
                return new Color32(96, 28, 16, 255);

            int band = (y + ((x + y * 3) % 4 == 0 ? 1 : 0)) % 4;
            if (band == 0)
                return new Color32(244, 138, 38, 255);
            if (band == 2)
                return new Color32(168, 52, 16, 255);

            return new Color32(212, 88, 24, 255);
        }

        static void EnsureBlockTextureMaterial()
        {
            Material material = EnsureMaterial(BlockiverseProject.ChunkAtlasMaterialPath, Color.white, preferUnlit: false);
            Texture2D authoredAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(BlockVisualAtlas.AuthoredAtlasPath);

            if (authoredAtlas != null)
            {
                SetMaterialTexture(material, authoredAtlas);
                SetMaterialColor(material, Color.white);
            }
            else
            {
                SetMaterialColor(material, TestBlockColor);
            }

            EditorUtility.SetDirty(material);
        }

        static Material EnsureMaterial(string path, Color color, bool preferUnlit)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(FindShader(preferUnlit));
                material.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
            }

            SetMaterialColor(material, color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Shader FindShader(bool preferUnlit)
        {
            string[] shaderNames = preferUnlit
                ? new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "Standard" }
                : new[] { "Universal Render Pipeline/Lit", "Standard", "Sprites/Default" };

            foreach (string shaderName in shaderNames)
            {
                Shader shader = Shader.Find(shaderName);

                if (shader != null)
                    return shader;
            }

            throw new InvalidOperationException("Unable to find a built-in shader for Blockiverse material creation.");
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            else
                material.color = color;
        }

        static void SetMaterialTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);

            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }
    }
}
