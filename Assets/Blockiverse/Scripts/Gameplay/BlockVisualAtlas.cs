using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blockiverse.Gameplay
{
    public static class BlockVisualAtlas
    {
        public const int Columns = 8;
        public const int Rows = 10;
        public const int TilePixels = 16;
        public const int TilePaddingPixels = 4;
        public const int TileStridePixels = TilePixels + TilePaddingPixels * 2;
        public const int AtlasWidthPixels = Columns * TileStridePixels;
        public const int AtlasHeightPixels = Rows * TileStridePixels;
        public const string AuthoredAtlasName = "blockiverse_block_atlas";
        public const string AuthoredAtlasPath = "Assets/Blockiverse/Art/Textures/Blocks/blockiverse_block_atlas.png";
        public const string VoxelLitShaderName = "Blockiverse/Voxel Lit";

        const float UvInsetPixels = 0.5f;

        static readonly Dictionary<int, int> TileIndexByBlockId = new()
        {
            { BlockRegistry.MeadowTurf.Value,        0 },
            { BlockRegistry.LooseLoam.Value,          1 },
            { BlockRegistry.Graystone.Value,          2 },
            { BlockRegistry.BranchwoodLog.Value,      3 },
            { BlockRegistry.Leafmoss.Value,           4 },
            { BlockRegistry.LumenQuartzCluster.Value, 5 },
            { BlockRegistry.EmbercoalSeam.Value,      6 },
            { BlockRegistry.RosycopperBloom.Value,    7 },
            { BlockRegistry.RustcoreOre.Value,        8 },
            { BlockRegistry.BuildTable.Value,         9 },
            { BlockRegistry.Glowwick.Value,           10 },
            { BlockRegistry.StorageCrate.Value,       11 },
            { BlockRegistry.Worldroot.Value,          12 },
            { BlockRegistry.Deepmantle.Value,         13 },
            { BlockRegistry.DarkSlate.Value,          14 },
            { BlockRegistry.WarmGranite.Value,        15 },
            { BlockRegistry.WhiteLimestone.Value,     16 },
            { BlockRegistry.BlackBasalt.Value,        17 },
            { BlockRegistry.DryTurf.Value,            18 },
            { BlockRegistry.SnowcapTurf.Value,        19 },
            { BlockRegistry.Rootsoil.Value,           20 },
            { BlockRegistry.Claybed.Value,            21 },
            { BlockRegistry.RiverSilt.Value,          22 },
            { BlockRegistry.PaleSand.Value,           23 },
            { BlockRegistry.ShingleGravel.Value,      24 },
            { BlockRegistry.Snowpack.Value,           25 },
            { BlockRegistry.Frostglass.Value,         26 },
            { BlockRegistry.Thornbrush.Value,         27 },
            { BlockRegistry.Reedgrass.Value,          28 },
            { BlockRegistry.WorkPlank.Value,          29 },
            { BlockRegistry.CutstoneBlock.Value,      30 },
            { BlockRegistry.FiredBrickBlock.Value,    31 },
            { BlockRegistry.ClearpaneGlass.Value,     32 },
            { BlockRegistry.SurfacePebbles.Value,     33 },
            { BlockRegistry.FlintyShingle.Value,      34 },
            { BlockRegistry.PaletinThread.Value,      35 },
            { BlockRegistry.SunmetalFleck.Value,      36 },
            { BlockRegistry.NiterstonePocket.Value,   37 },
            { BlockRegistry.BrightsaltCrust.Value,    38 },
            { BlockRegistry.ShellgritBed.Value,       39 },
            { BlockRegistry.ResinKnot.Value,          40 },
            { BlockRegistry.Berrybush.Value,          41 },
            { BlockRegistry.GrainStalk.Value,         42 },
            { BlockRegistry.UmbraliteNode.Value,      43 },
            { BlockRegistry.StaropalGeode.Value,      44 },
            { BlockRegistry.Campfire.Value,           45 },
            { BlockRegistry.ClayKiln.Value,           46 },
            { BlockRegistry.BellowsForge.Value,       47 },
            { BlockRegistry.PrepBoard.Value,          48 },
            { BlockRegistry.MendBench.Value,          49 },
            { BlockRegistry.LumenLamp.Value,          50 },
            { BlockRegistry.SparkFlare.Value,         51 },
            { BlockRegistry.TendedSoil.Value,          52 },
            { BlockRegistry.GrainStalk_S1.Value,      53 },
            { BlockRegistry.GrainStalk_S2.Value,      54 },
            { BlockRegistry.Berrybush_S1.Value,       55 },
            { BlockRegistry.Berrybush_S2.Value,       56 },
            { BlockRegistry.Reedgrass_S1.Value,       57 },
            { BlockRegistry.Sapling.Value,            58 },
            { BlockRegistry.Sapling_S1.Value,         59 },
            { BlockRegistry.Sapling_S2.Value,         60 },
            { BlockRegistry.GrainStalk_S3.Value,      61 },
            { BlockRegistry.GrainStalk_S4.Value,      62 },
            { BlockRegistry.Berrybush_S3.Value,       63 },
            { BlockRegistry.Berrybush_S4.Value,       64 },
            { BlockRegistry.Berrybush_S5.Value,       65 },
            { BlockRegistry.Reedgrass_S2.Value,       66 },
            { BlockRegistry.Reedgrass_S3.Value,       67 },
            { BlockRegistry.SmoothBranchwood.Value,   68 },
            { BlockRegistry.ReedBasket.Value,          69 },
            { BlockRegistry.ToolRack.Value,            70 },
            { BlockRegistry.PantryJar.Value,           71 },
            { BlockRegistry.DeepLocker.Value,          72 },
            { BlockRegistry.Freshwater.Value,          73 },
            { BlockRegistry.Brine.Value,               74 },
            { BlockRegistry.Emberflow.Value,           75 },
            { BlockRegistry.Bedroll.Value,             76 },
            // Flowing cells render with their family's source tile.
            { BlockRegistry.FreshwaterFlow.Value,      73 },
            { BlockRegistry.BrineFlow.Value,           74 },
            { BlockRegistry.EmberflowFlow.Value,       75 },
        };

        public static Rect GetTileRect(BlockId blockId)
        {
            int tileIndex = GetTileIndex(blockId);
            int column = tileIndex % Columns;
            int row = tileIndex / Columns;
            float minX = column * TileStridePixels + TilePaddingPixels + UvInsetPixels;
            float maxX = column * TileStridePixels + TilePaddingPixels + TilePixels - UvInsetPixels;
            float minY = row * TileStridePixels + TilePaddingPixels + UvInsetPixels;
            float maxY = row * TileStridePixels + TilePaddingPixels + TilePixels - UvInsetPixels;

            return new Rect(
                minX / AtlasWidthPixels,
                1.0f - maxY / AtlasHeightPixels,
                (maxX - minX) / AtlasWidthPixels,
                (maxY - minY) / AtlasHeightPixels);
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
                    $"Block material texture '{texture.name}' is not the expected authored atlas. Assign {AuthoredAtlasPath} ({AtlasWidthPixels}x{AtlasHeightPixels}).";
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
                   texture2D.width == AtlasWidthPixels &&
                   texture2D.height == AtlasHeightPixels;
        }

        static Material CreateBaseMaterial(Material sourceMaterial)
        {
            sourceMaterial = ResolveSourceMaterial(sourceMaterial);
            Shader voxelShader = Shader.Find(VoxelLitShaderName);
            Shader shader = voxelShader != null
                ? voxelShader
                : sourceMaterial != null
                ? sourceMaterial.shader
                : Shader.Find("Universal Render Pipeline/Lit") ??
                  Shader.Find("Standard") ??
                  Shader.Find("Sprites/Default");

            TryGetBaseTexture(sourceMaterial, out Texture sourceTexture);
            Material material = sourceMaterial != null ? new Material(sourceMaterial) : new Material(shader);

            if (voxelShader != null)
                material.shader = voxelShader;

            if (sourceTexture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", sourceTexture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", sourceTexture);
            }

            return material;
        }

        static Material ResolveSourceMaterial(Material sourceMaterial)
        {
            if (sourceMaterial != null)
                return sourceMaterial;

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Material>(BlockiverseProject.ChunkAtlasMaterialPath);
#else
            return null;
#endif
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
