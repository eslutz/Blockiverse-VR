using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    public static class BlockVisualAtlas
    {
        public const int Columns = 4;
        public const int Rows = 4;
        public const int TilePixels = 16;
        public const string AuthoredAtlasName = "blockiverse_block_atlas";
        public const string AuthoredAtlasPath = "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png";

        const float UvInset = 0.001f;

        static readonly Dictionary<int, int> TileIndexByBlockId = new()
        {
            { BlockRegistry.MeadowTurf.Value, 0 },
            { BlockRegistry.Loam.Value, 1 },
            { BlockRegistry.Slate.Value, 2 },
            { BlockRegistry.Timber.Value, 3 },
            { BlockRegistry.Leafmass.Value, 4 },
            { BlockRegistry.Clearstone.Value, 5 },
            { BlockRegistry.Coalstone.Value, 6 },
            { BlockRegistry.Copperstone.Value, 7 },
            { BlockRegistry.Ironstone.Value, 8 },
            { BlockRegistry.Workbench.Value, 9 },
            { BlockRegistry.Torchbud.Value, 10 },
            { BlockRegistry.StorageCrate.Value, 11 }
        };

        public static Rect GetTileRect(BlockId blockId)
        {
            int tileIndex = GetTileIndex(blockId);
            int column = tileIndex % Columns;
            int row = tileIndex / Columns;
            float width = 1.0f / Columns;
            float height = 1.0f / Rows;

            return new Rect(
                column * width + UvInset,
                1.0f - (row + 1) * height + UvInset,
                width - UvInset * 2.0f,
                height - UvInset * 2.0f);
        }

        public static Material CreateMaterial(Material sourceMaterial)
        {
            Material material = CreateBaseMaterial(sourceMaterial);

            if (!TryGetBaseTexture(material, out Texture texture))
            {
                string message =
                    $"Authored block atlas is missing from the source material. Assign {AuthoredAtlasPath} to the block material.";
                BlockiverseLog.Warning(BlockiverseLogCategory.Assets, message);
                throw new InvalidOperationException(message);
            }

            if (!IsAuthoredAtlasTexture(texture))
            {
                string message =
                    $"Block material texture '{texture.name}' is not the expected authored atlas. Assign {AuthoredAtlasPath} ({Columns * TilePixels}x{Rows * TilePixels}).";
                BlockiverseLog.Warning(BlockiverseLogCategory.Assets, message);
                throw new InvalidOperationException(message);
            }

            SetBaseColor(material, Color.white);
            material.name = "Blockiverse Authored Block Atlas Material";
            return material;
        }

        static int GetTileIndex(BlockId blockId)
        {
            if (TileIndexByBlockId.TryGetValue(blockId.Value, out int tileIndex))
                return tileIndex;

            throw new ArgumentException($"No visual atlas tile is registered for block ID {blockId}.", nameof(blockId));
        }

        public static bool HasAuthoredTile(BlockId blockId)
        {
            return TileIndexByBlockId.ContainsKey(blockId.Value);
        }

        public static void ValidateRenderableBlockCoverage(BlockRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            var missingTiles = new List<string>();
            foreach (BlockDefinition block in registry.All)
            {
                if (block.IsRenderable && !HasAuthoredTile(block.Id))
                    missingTiles.Add($"{block.Name} ({block.Id})");
            }

            if (missingTiles.Count > 0)
            {
                string message =
                    $"Renderable blocks are missing visual atlas tile mappings: {string.Join(", ", missingTiles)}.";
                BlockiverseLog.Warning(BlockiverseLogCategory.Assets, message);
                throw new InvalidOperationException(message);
            }
        }

        public static bool TryGetBaseTexture(Material material, out Texture texture)
        {
            texture = null;

            if (material == null)
                return false;

            if (material.HasProperty("_BaseMap"))
            {
                texture = material.GetTexture("_BaseMap");

                if (texture != null)
                    return true;
            }

            if (material.HasProperty("_MainTex"))
            {
                texture = material.GetTexture("_MainTex");
                return texture != null;
            }

            return false;
        }

        public static bool IsAuthoredAtlasTexture(Texture texture)
        {
            return texture is Texture2D texture2D &&
                   texture2D.name == AuthoredAtlasName &&
                   texture2D.width == Columns * TilePixels &&
                   texture2D.height == Rows * TilePixels;
        }

        static Material CreateBaseMaterial(Material sourceMaterial)
        {
            Shader shader = sourceMaterial != null
                ? sourceMaterial.shader
                : Shader.Find("Universal Render Pipeline/Lit") ??
                  Shader.Find("Standard") ??
                  Shader.Find("Sprites/Default");

            return sourceMaterial != null ? new Material(sourceMaterial) : new Material(shader);
        }

        static void SetBaseColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }
}
