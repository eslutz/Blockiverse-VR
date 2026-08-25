using System;
using System.Collections.Generic;
using Blockiverse.Core;
using Blockiverse.Voxel;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blockiverse.Gameplay
{
    public static class BlockVisualAtlas
    {
        public const int Columns = 12;
        public const int Rows = 10;
        public const int TilePixels = 32;
        public const int TilePaddingPixels = 8;
        public const int TileStridePixels = TilePixels + TilePaddingPixels * 2;
        public const int AtlasWidthPixels = Columns * TileStridePixels;
        public const int AtlasHeightPixels = Rows * TileStridePixels;
        public const string AuthoredAtlasName = "blockiverse_block_atlas";
        public const string AuthoredAtlasPath = "Assets/Blockiverse/Art/Textures/Blocks/TextureSets/enhanced/blockiverse_block_atlas.png";
        public const string VoxelLitShaderName = "Blockiverse/Voxel Lit";

        // Local multi_compile keyword on the one voxel shader. There is deliberately no second
        // .shader file: GraphicsSettings' m_AlwaysIncludedShaders lists this shader alone, so a
        // separate water shader reached only through Shader.Find would be stripped from the
        // Android player and water would render magenta on device while looking right in editor.
        public const string WaterShaderKeyword = "_BLOCKIVERSE_WATER";

        // Same stripping constraint as the water keyword above: it is a third state on the SAME
        // multi_compile_local line, never a separate .shader asset.
        public const string CutoutShaderKeyword = "_BLOCKIVERSE_CUTOUT";
        public const string SkyShaderKeyword = "_BLOCKIVERSE_SKY";

        public const string BlockMaterialName = "Blockiverse Authored Block Atlas Material";
        public const string FluidMaterialName = "Blockiverse Authored Fluid Atlas Material";
        public const string FluidDepthPrimeMaterialName = "Blockiverse Authored Fluid Depth Prime Material";
        public const string CutoutMaterialName = "Blockiverse Authored Cutout Atlas Material";
        public const string SkyMaterialName = "Blockiverse Authored Sky Atlas Material";

        // LightMode tag of the water depth-prime pass. Only the prime material runs it; terrain and
        // the water shading material both switch it off, so neither pays for a pass it never wants.
        public const string WaterDepthPrimePassName = "SRPDefaultUnlit";
        const string ForwardPassName = "UniversalForward";

        // One below the transparent queue, which is what orders the prime before every water
        // shading draw in the scene -- including other chunks', so the nearest water surface
        // anywhere claims the pixel. Render queue is the primary sort key, so this ordering does
        // not depend on the pipeline's internal shader-tag order.
        public const int FluidDepthPrimeRenderQueue = (int)RenderQueue.Transparent - 1;

        // URP's AlphaTest band: after all opaque geometry (2000) and before transparent (3000), so
        // opaque depth still rejects hidden foliage before the alpha test costs anything.
        public const int CutoutRenderQueue = (int)RenderQueue.AlphaTest;
        public const int SkyRenderQueue = (int)RenderQueue.Geometry + 50;

        // Half coverage. Deliberately not near 0: a low threshold keeps almost-transparent
        // fringe pixels alive, which on a point-filtered atlas reads as a halo around every blade.
        public const float DefaultAlphaCutoff = 0.5f;

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
            { BlockRegistry.MirrorPane.Value,          77 },
            // Vegetation additions. Indices mirror the BLOCKS list in generate-art-assets.py; the
            // two lists are hand-maintained mirrors of each other, so they drift silently.
            { BlockRegistry.DrygrassTuft.Value,        78 },
            { BlockRegistry.MeadowTuft.Value,          79 },
            { BlockRegistry.WildflowerCluster.Value,   80 },
            { BlockRegistry.DuneSage.Value,            81 },
            { BlockRegistry.SaltReed.Value,            82 },
            { BlockRegistry.FrostFern.Value,           83 },
            { BlockRegistry.WindrootShrub.Value,       84 },
            { BlockRegistry.HangingReed.Value,         85 },
            { BlockRegistry.MossCarpet.Value,          86 },
            { BlockRegistry.SnowLichen.Value,          87 },
            { BlockRegistry.FallenLeaves.Value,        88 },
            { BlockRegistry.CharredLog.Value,          89 },
            { BlockRegistry.SnowBlock.Value,           90 },
            // Flowing cells render with their family's source tile.
            { BlockRegistry.FreshwaterFlow.Value,      73 },
            { BlockRegistry.BrineFlow.Value,           74 },
            { BlockRegistry.EmberflowFlow.Value,       75 },
        };

        // Per-face tile overrides (vegetation ruleset §4a.5). Without these a turf block samples
        // the same tile on all six faces and reads as uniformly green from every angle, which is a
        // primary reason the world looks flat and over-clean — the classic voxel look is a grass
        // top over dirt sides.
        //
        // Only the two axes that differ are stored. `Side` replaces the four vertical faces;
        // `Bottom` replaces the downward face. A block absent from this map, or a face with no
        // override, falls back to the block's single tile, so every other block is unaffected.
        readonly struct BlockFaceTiles
        {
            public BlockFaceTiles(int side, int bottom)
            {
                Side = side;
                Bottom = bottom;
            }

            public readonly int Side;
            public readonly int Bottom;
        }

        // Indices mirror the BLOCKS list in generate-art-assets.py, same hand-maintained pairing as
        // TileIndexByBlockId above.
        static readonly Dictionary<int, BlockFaceTiles> FaceTilesByBlockId = new()
        {
            // Turf: grass on top (the block's own tile), dirt-with-fringe on the sides, plain loam
            // underneath — reusing loose_loam's tile rather than authoring a fourth.
            { BlockRegistry.MeadowTurf.Value,   new BlockFaceTiles(side: 91, bottom: 1) },
            { BlockRegistry.DryTurf.Value,      new BlockFaceTiles(side: 92, bottom: 1) },
            { BlockRegistry.SnowcapTurf.Value,  new BlockFaceTiles(side: 93, bottom: 1) },
            { BlockRegistry.Rootsoil.Value,     new BlockFaceTiles(side: 94, bottom: 1) },

            // Logs: bark around the sides, end grain on both cut ends. Bottom and top share it.
            { BlockRegistry.BranchwoodLog.Value,    new BlockFaceTiles(side: -1, bottom: 95) },
            { BlockRegistry.SmoothBranchwood.Value, new BlockFaceTiles(side: -1, bottom: 96) },
        };

        // Face-aware overload. `faceIndex` is a ChunkMeshBuilder face index; -1 means "no
        // particular face", which is what cross quads and decals pass.
        // A zero-area UV rect over a near-white fully-opaque texel, carried by sky geometry (the
        // cloud deck and the horizon skirt) so their meshes have valid UVs.
        //
        // NO LONGER LOAD-BEARING, and the reason is worth keeping. The deck used to be tinted by
        // this texel, on the assumption that one measured pixel is white in the atlas. Measured
        // across all four generated texture sets at texel (302, 345) of 576x480:
        //
        //   enhanced (248,255,255)  ai (248,255,255)  ai_simplified (248,255,255)
        //   original (194,204,209)  <-- 20% dark and blue
        //
        // So selecting the `original` texture set tinted the entire sky, and would have put a
        // visible line all the way round the horizon skirt, whose rim has to land on EXACTLY the
        // aerial colour to disappear. The sky shader variant now samples no texture at all, which
        // makes the atlas irrelevant to the sky rather than making this one texel a shared
        // dependency of four independently generated art sets. Pinned by
        // BlockiverseHorizonSkirtEditModeTests.TheSkyVariantNeverSamplesTheAtlas.
        public static readonly Rect WhiteTexelUv = new(
            (302.0f + 0.5f) / AtlasWidthPixels,
            1.0f - ((345.0f + 0.5f) / AtlasHeightPixels),
            0.0f,
            0.0f);

        public static Rect GetTileRect(BlockId blockId, int faceIndex)
        {
            return BuildTileRect(ResolveTileIndex(blockId, faceIndex));
        }

        static int ResolveTileIndex(BlockId blockId, int faceIndex)
        {
            int tileIndex = GetTileIndex(blockId);

            if (faceIndex < 0 || !FaceTilesByBlockId.TryGetValue(blockId.Value, out BlockFaceTiles faces))
                return tileIndex;

            // Logs want end grain on BOTH caps, so top uses the bottom override too. Turf leaves
            // Side at -1 for neither cap, so its top keeps the block's own grass tile.
            bool isTop = faceIndex == ChunkMeshBuilder.TopFaceIndex;
            bool isBottom = faceIndex == ChunkMeshBuilder.BottomFaceIndex;

            if (isBottom || (isTop && faces.Side < 0))
                return faces.Bottom >= 0 ? faces.Bottom : tileIndex;

            if (isTop)
                return tileIndex;

            return faces.Side >= 0 ? faces.Side : tileIndex;
        }

        public static Rect GetTileRect(BlockId blockId)
        {
            return BuildTileRect(GetTileIndex(blockId));
        }

        static Rect BuildTileRect(int tileIndex)
        {
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
            return CreateMaterial(sourceMaterial, selectedAtlas: null, textureSetId: BlockTextureSetIds.Default);
        }

        public static Material CreateMaterial(Material sourceMaterial, Texture2D selectedAtlas, string textureSetId)
        {
            Material material = CreateBaseMaterial(sourceMaterial);

            if (selectedAtlas != null)
                SetBaseTexture(material, selectedAtlas);

            if (!TryGetBaseTexture(material, out Texture texture))
            {
                string message =
                    $"Authored block atlas is missing from the source material. Assign {AtlasPathForTextureSet(textureSetId)} to the block material.";
                BlockiverseLog.Warning(BlockiverseLogCategory.Assets, message);
                throw new InvalidOperationException(message);
            }

            if (!IsAuthoredAtlasTexture(texture))
            {
                string message =
                    $"Block material texture '{texture.name}' is not the expected authored atlas. Assign {AtlasPathForTextureSet(textureSetId)} ({AtlasWidthPixels}x{AtlasHeightPixels}).";
                BlockiverseLog.Warning(BlockiverseLogCategory.Assets, message);
                throw new InvalidOperationException(message);
            }

            SetBaseColor(material, Color.white);

            // Re-asserted, not inherited: the material is cloned from the authored URP Lit source
            // asset, so whatever surface state that asset happens to carry would otherwise ride
            // along into terrain rendering.
            ApplySurfaceState(
                material,
                renderType: "Opaque",
                queue: (int)RenderQueue.Geometry,
                srcBlend: BlendMode.One,
                dstBlend: BlendMode.Zero,
                zWrite: 1.0f);
            material.DisableKeyword(WaterShaderKeyword);
            material.SetShaderPassEnabled(WaterDepthPrimePassName, false);
            material.SetShaderPassEnabled(ForwardPassName, true);

            material.name = BlockMaterialName;
            return material;
        }

        // The water material is a runtime clone of the same authored atlas material, built on the
        // path CreativeWorldManager.ConfigureWorldRuntime already drives, so texture-set switching
        // and world reload need no extra wiring and no new .mat asset ships.
        public static Material CreateFluidMaterial(Material sourceMaterial, Texture2D selectedAtlas, string textureSetId)
        {
            Material material = CreateMaterial(sourceMaterial, selectedAtlas, textureSetId);

            // ZWrite is OFF here because CreateFluidDepthPrimeMaterial already wrote the depth:
            // the nearest water fragment claims each pixel and every farther one is rejected before
            // it can blend, so exactly one layer of tint lands from every angle. Writing depth here
            // as well would be redundant, and it was never enough on its own -- ZWrite decides
            // which fragments survive, not how many blend.
            ApplySurfaceState(
                material,
                renderType: "Transparent",
                queue: (int)RenderQueue.Transparent,
                srcBlend: BlendMode.SrcAlpha,
                dstBlend: BlendMode.OneMinusSrcAlpha,
                zWrite: 0.0f,
                // Every fluid quad winds outward and same-family faces are merged away, so from
                // underneath a lake the surface was back-facing and culled -- you looked up at a
                // hole in the world. Cull Off costs nothing here: only one side of a quad ever
                // faces the camera, so it still rasterises once per pixel.
                cull: CullMode.Off);
            material.EnableKeyword(WaterShaderKeyword);
            material.SetShaderPassEnabled(WaterDepthPrimePassName, false);
            material.SetShaderPassEnabled(ForwardPassName, true);

            material.name = FluidMaterialName;
            return material;
        }

        // Alpha-cutout foliage: leaf canopies and cross-quad plants. Another runtime clone of the
        // authored atlas material, so no new .mat asset ships and texture-set switching keeps
        // working unchanged.
        public static Material CreateCutoutMaterial(Material sourceMaterial, Texture2D selectedAtlas, string textureSetId)
        {
            Material material = CreateMaterial(sourceMaterial, selectedAtlas, textureSetId);

            // Opaque blending with ZWrite on — this is alpha TEST, not alpha blend. The queue sits
            // in URP's AlphaTest band, after all opaque geometry, so the opaque depth buffer can
            // still reject hidden foliage before it is shaded. That ordering matters more here
            // than usual: clip() disables early-Z for the draw on a tile GPU, so anything not
            // rejected by prior depth gets fully shaded.
            ApplySurfaceState(
                material,
                renderType: "TransparentCutout",
                queue: CutoutRenderQueue,
                srcBlend: BlendMode.One,
                dstBlend: BlendMode.Zero,
                zWrite: 1.0f,
                // Two-sided for two reasons: a cross quad is viewed from both sides by definition,
                // and on a cutout cube the gaps expose the inside of the far shell, which is what
                // gives a canopy depth without adding a single triangle.
                cull: CullMode.Off);
            material.EnableKeyword(CutoutShaderKeyword);
            material.DisableKeyword(WaterShaderKeyword);
            material.SetShaderPassEnabled(WaterDepthPrimePassName, false);
            material.SetShaderPassEnabled(ForwardPassName, true);
            SetFloatIfPresent(material, "_Cutoff", DefaultAlphaCutoff);

            material.name = CutoutMaterialName;
            return material;
        }

        // Sky geometry: the cloud deck overhead and the horizon skirt at sea level. A third
        // runtime clone of the same authored atlas material, for the same reasons as the other two
        // — no new .mat asset, no Shader.Find, and texture-set switching keeps working.
        //
        // What makes it its own material rather than the block one with a different mesh is the
        // keyword. Both surfaces have to be able to take an EXACT colour, because both exist to
        // stop being distinguishable from the sky at their outer edge, and the lit path cannot
        // deliver one: vertex colour there is baked light data, and self emission is a scalar, so
        // a pale blue vertex colour renders as a dimmer white rather than as pale blue.
        public static Material CreateSkyMaterial(Material sourceMaterial, Texture2D selectedAtlas, string textureSetId)
        {
            Material material = CreateMaterial(sourceMaterial, selectedAtlas, textureSetId);

            // Plain opaque geometry, but drawn AFTER terrain.
            //
            // The skirt is a large and frequently occluded surface — standing on the island looking
            // out, the terrain in front hides most of it — and at terrain's own queue the opaque
            // front-to-back sort decides the order from bounds, which for the skirt is centred on
            // the world rather than on the player. One queue later lets terrain depth reject those
            // pixels before they are shaded, which on a tile GPU is the difference that matters.
            // The deck neither gains nor loses (nothing occludes the sky) and shares the material.
            //
            // Still inside the documented ordering: terrain 2000 < sky 2050 < cutout 2450 < water.
            ApplySurfaceState(
                material,
                renderType: "Opaque",
                queue: SkyRenderQueue,
                srcBlend: BlendMode.One,
                dstBlend: BlendMode.Zero,
                zWrite: 1.0f,
                // The deck's cells are closed boxes and the skirt is a single-sided plane meant to
                // be seen from above, so back-face culling is free on both.
                cull: CullMode.Back);
            material.EnableKeyword(SkyShaderKeyword);
            material.DisableKeyword(WaterShaderKeyword);
            material.DisableKeyword(CutoutShaderKeyword);
            material.SetShaderPassEnabled(WaterDepthPrimePassName, false);
            material.SetShaderPassEnabled(ForwardPassName, true);

            material.name = SkyMaterialName;
            return material;
        }

        // The depth half of the water pair. Rendered from the same fluid mesh through a second
        // entry in the renderer's shared materials, one queue earlier, writing depth and no colour.
        // It carries the water keyword and the same authored atlas so its vertex program is the
        // same variant as the shading pass's, which is what keeps the two depths in agreement.
        public static Material CreateFluidDepthPrimeMaterial(Material sourceMaterial, Texture2D selectedAtlas, string textureSetId)
        {
            Material material = CreateMaterial(sourceMaterial, selectedAtlas, textureSetId);

            ApplySurfaceState(
                material,
                renderType: "Transparent",
                queue: FluidDepthPrimeRenderQueue,
                srcBlend: BlendMode.One,
                dstBlend: BlendMode.Zero,
                zWrite: 1.0f,
                // MUST match the shading material. If only that one un-culled, the prime would
                // write no depth at the underside and the double-blend ADR 0007 section 4 exists
                // to eliminate would come straight back for anyone looking up through water.
                cull: CullMode.Off);
            material.EnableKeyword(WaterShaderKeyword);
            material.SetShaderPassEnabled(WaterDepthPrimePassName, true);
            material.SetShaderPassEnabled(ForwardPassName, false);

            material.name = FluidDepthPrimeMaterialName;
            return material;
        }

        // cull is a parameter rather than a constant because terrain and water want different
        // answers: opaque terrain never needs its backfaces and pays for culling them, while a
        // water surface has to be visible from BELOW -- swimming under a lake and looking up at a
        // hole in the world was the whole reason this became a parameter.
        static void ApplySurfaceState(
            Material material,
            string renderType,
            int queue,
            BlendMode srcBlend,
            BlendMode dstBlend,
            float zWrite,
            CullMode cull = CullMode.Back)
        {
            material.SetOverrideTag("RenderType", renderType);
            material.renderQueue = queue;
            SetFloatIfPresent(material, "_SrcBlend", (float)srcBlend);
            SetFloatIfPresent(material, "_DstBlend", (float)dstBlend);
            SetFloatIfPresent(material, "_ZWrite", zWrite);
            SetFloatIfPresent(material, "_Cull", (float)cull);
        }

        // The fallback shaders CreateBaseMaterial can land on (URP Lit, Standard, Sprites/Default)
        // do not all declare these, and SetFloat on a missing property is a silent no-op that
        // would leave the state question unanswered.
        static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        public static string AtlasPathForTextureSet(string textureSetId) =>
            $"Assets/Blockiverse/Art/Textures/Blocks/TextureSets/{BlockTextureSetIds.Normalize(textureSetId)}/blockiverse_block_atlas.png";

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
                SetBaseTexture(material, sourceTexture);

            return material;
        }

        static void SetBaseTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
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
