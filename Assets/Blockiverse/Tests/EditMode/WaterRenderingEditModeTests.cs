using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Tests.EditMode
{
    public sealed class WaterRenderingEditModeTests
    {
        const string VoxelShaderPath = "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader";

        [Test]
        public void OnlyTopFaceQuadsCarryTheSurfaceMask()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 11);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            Assert.That(fluid.FaceCount, Is.EqualTo(6),
                "A lone water block in open air exposes all six faces.");

            AssertSurfaceMaskMarksExactlyTheTopQuads(fluid, topPlaneY: 3.0f);
        }

        [Test]
        public void WaterUnderASolidCeilingEmitsNoMaskedVertex()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 12);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(4, 3, 4), BlockRegistry.Graystone, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            Assert.That(fluid.FluidVertexData.Any(data => data.x > 0.0f), Is.False,
                "A capped water cell emits no +Y face, so nothing is masked and the shader has nothing it could displace.");
        }

        [Test]
        public void SaplingAboveWaterStillEmitsAMaskedTopFace()
        {
            // The crack mode that killed the surface-drop design: a sapling is renderable but not
            // solid, so ShouldRenderFace still emits the water's top face. If masking depended on
            // "has air above" instead of "a +Y face was emitted", this cell would stay put while
            // its neighbours dipped and the shared vertical face -- which is culled -- could not
            // bridge the gap.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 13);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(4, 3, 4), BlockRegistry.Sapling, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            AssertSurfaceMaskMarksExactlyTheTopQuads(fluid, topPlaneY: 3.0f);
        }

        [Test]
        public void EmberflowAboveFreshwaterStillEmitsAMaskedTopFace()
        {
            // Same crack mode, cross-family: the same-family merge in ShouldRenderFace does not
            // apply, so the freshwater top face survives and must carry the mask.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 14);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(4, 3, 4), BlockRegistry.Emberflow, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            List<Vector2> masked = MaskedQuadData(fluid, topPlaneY: 3.0f).ToList();

            Assert.That(masked.Count, Is.EqualTo(4),
                "The freshwater cell keeps a full-height, masked top face even with a different fluid family above it.");
            Assert.That(masked.All(data => Mathf.Approximately(data.y, (int)FluidFamily.Freshwater)), Is.True,
                "The masked top face belongs to the freshwater cell, so it must carry the freshwater family index.");
        }

        [Test]
        public void PlacingASolidBlockOnALakeLeavesEveryFluidVertexOnTheVoxelGrid()
        {
            // Two same-family water cells, one capped by a placed block. The capped cell keeps its
            // full height and emits no top face; the open one is masked. Nothing moves on the CPU
            // at all -- the dip is purely a shader displacement -- so no slit can open around a
            // block placed on the water surface.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 15);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(5, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(5, 3, 4), BlockRegistry.Graystone, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            Assert.That(fluid.Vertices.All(vertex =>
                    Mathf.Approximately(vertex.x, Mathf.Round(vertex.x)) &&
                    Mathf.Approximately(vertex.y, Mathf.Round(vertex.y)) &&
                    Mathf.Approximately(vertex.z, Mathf.Round(vertex.z))),
                Is.True,
                "Fluid geometry stays on the voxel grid; the wave is a shader displacement, never a mesh edit.");

            List<Vector2> masked = MaskedQuadData(fluid, topPlaneY: 3.0f).ToList();

            Assert.That(masked.Count, Is.EqualTo(4),
                "Exactly one of the two cells has open sky, so exactly one masked top quad exists.");
        }

        [Test]
        public void AWallStandingOnALowerWaterSurfaceRidesItDown()
        {
            // A step in flowing water: a two-deep column beside a one-deep one. The lower surface
            // dips under the wave while the taller column's wall stands on that same plane. If the
            // wall's foot stayed put, the wave would open a see-through slit under it at every
            // step. Both sets of vertices share an x/z edge, and the wave is a pure function of
            // x/z, so masking the foot closes the seam exactly rather than approximately.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 22);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(5, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(5, 3, 4), BlockRegistry.Freshwater, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            // The wall of the tall column that faces the short one is the only quad lying wholly in
            // the x = 5 plane: its foot sits at y = 3, exactly where the short column's top face is.
            // Matching per vertex instead of per quad would also catch the top face's own corners,
            // which share those coordinates.
            int wallQuad = -1;

            for (int quad = 0; quad < fluid.Vertices.Count / 4; quad++)
            {
                bool inThePlane = Enumerable.Range(quad * 4, 4)
                    .All(index => Mathf.Approximately(fluid.Vertices[index].x, 5.0f));

                if (inThePlane)
                    wallQuad = quad;
            }

            Assert.That(wallQuad, Is.GreaterThanOrEqualTo(0),
                "Fixture precondition: the tall column must emit a wall facing the short one.");

            foreach (int index in Enumerable.Range(wallQuad * 4, 4))
            {
                float expected = Mathf.Approximately(fluid.Vertices[index].y, 3.0f) ? 1.0f : 0.0f;

                Assert.That(fluid.FluidVertexData[index].x, Is.EqualTo(expected).Within(0.0001f),
                    Mathf.Approximately(fluid.Vertices[index].y, 3.0f)
                        ? "The wall foot shares a plane with the lower surface and must dip with it, or a slit opens under every step in flowing water."
                        : "Only the foot rides the lower surface; the wall's own top edge belongs to the taller column and must not move.");
            }
        }

        [Test]
        public void AFreestandingWaterWallKeepsItsFootPlanted()
        {
            // The inverse case, and why the rule checks the cell BELOW the neighbour: a two-deep
            // column with open air beside it all the way down has nothing under its wall to track.
            // Dipping that foot would tear the wall away from the cell below it.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 23);
            world.SetBlock(new BlockPosition(5, 2, 4), BlockRegistry.Freshwater, trackChange: false);
            world.SetBlock(new BlockPosition(5, 3, 4), BlockRegistry.Freshwater, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            for (int index = 0; index < fluid.Vertices.Count; index++)
            {
                if (Mathf.Approximately(fluid.Vertices[index].y, 4.0f))
                    continue;

                Assert.That(fluid.FluidVertexData[index].x, Is.EqualTo(0.0f).Within(0.0001f),
                    $"Vertex {index} at y={fluid.Vertices[index].y} was masked, but only the top face at y=4 may move on a freestanding column.");
            }
        }

        [Test]
        public void FluidVertexDataCarriesTheFamilyIndexForEveryFamily()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();

            foreach (FluidFamily family in new[] { FluidFamily.Freshwater, FluidFamily.Brine, FluidFamily.Emberflow })
            {
                var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 16);
                world.SetBlock(new BlockPosition(4, 2, 4), FluidBlocks.SourceOf(family), trackChange: false);

                ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

                Assert.That(fluid.FluidVertexData.Count, Is.EqualTo(fluid.Vertices.Count),
                    $"Every {family} vertex needs its own packed entry or the channel desynchronises from the mesh.");
                Assert.That(fluid.FluidVertexData.All(data => Mathf.Approximately(data.y, (int)family)), Is.True,
                    $"{family} vertices must carry family index {(int)family} so the shader can select its tint and wave.");
            }
        }

        [Test]
        public void FlowingCellsCarryTheirSourceFamily()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 17);
            world.SetBlock(new BlockPosition(4, 2, 4), FluidBlocks.FlowOf(FluidFamily.Brine), trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            Assert.That(fluid.FaceCount, Is.EqualTo(6),
                "Guard against a vacuous pass: a flow cell must actually mesh, or the family assertion below is All() over nothing.");
            Assert.That(fluid.FluidVertexData.All(data => Mathf.Approximately(data.y, (int)FluidFamily.Brine)), Is.True,
                "A flowing cell renders as its family, so a stream must not tint differently from the lake feeding it.");
        }

        [Test]
        public void SolidMeshesCarryNoFluidVertexData()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 18);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Graystone, trackChange: false);

            ChunkMeshData solid = ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out _);

            Assert.That(solid.FluidVertexData, Is.Null,
                "Terrain carries no second UV channel: the water attributes exist only where water does.");
        }

        [Test]
        public void FluidVertexColorsStillCarryBakedLightingOnEveryChannel()
        {
            // The reason the surface mask and family live in a UV channel at all: vertex COLOR is
            // already fully spent on lighting, and water needs all three channels as much as stone
            // does -- a torch-lit pool reads through G, a glowing emberflow through B.
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 19);
            world.SetBlock(new BlockPosition(4, 2, 4), BlockRegistry.Freshwater, trackChange: false);

            ChunkMeshBuilder.Build(world, registry, new ChunkCoordinate(0, 0, 0), out ChunkMeshData fluid);

            Assert.That(fluid.Colors.Count, Is.EqualTo(fluid.Vertices.Count).And.GreaterThan(0),
                "Fluid vertices keep their baked lighting colour.");
            Assert.That(fluid.Colors.Any(color => color.r > 0.0f), Is.True,
                "Open-sky water must carry real sky exposure in R; a zeroed R would mean the water channel had displaced the lighting bake.");
            Assert.That(fluid.Colors.All(color => Mathf.Approximately(color.a, 1.0f)), Is.True,
                "COLOR.a stays a literal 1: it is the opacity multiplier the moment the material blends, never a flag.");
            Assert.That(fluid.FluidVertexData.Any(data => data.x > 0.0f), Is.True,
                "The packed channel carries the surface data instead, which is the whole reason COLOR is left alone.");
        }

        [Test]
        public void BothFluidMaterialsRenderFromUnderneathWhileTerrainStaysCulled()
        {
            // Every fluid quad winds outward and same-family interior faces are merged away, so
            // with back-face culling the surface simply did not exist when seen from below --
            // swimming under a lake and looking up showed a hole in the world.
            //
            // Both fluid materials must agree. If only the shading material un-culled, the prime
            // would write no depth at the underside and the double-blend that ADR 0007 section 4
            // exists to eliminate would come straight back for anyone looking up through water.
            Texture2D atlasTexture = null;
            Material sourceMaterial = null;
            Material fluidMaterial = null;
            Material primeMaterial = null;
            Material blockMaterial = null;

            try
            {
                sourceMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                fluidMaterial = BlockVisualAtlas.CreateFluidMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);
                primeMaterial = BlockVisualAtlas.CreateFluidDepthPrimeMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);
                blockMaterial = BlockVisualAtlas.CreateMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);

                Assert.That(fluidMaterial.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off).Within(0.001f),
                    "The water surface has to be visible from below.");
                Assert.That(primeMaterial.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off).Within(0.001f),
                    "The prime must claim the same pixels the shading pass will blend into, from every direction.");
                Assert.That(blockMaterial.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Back).Within(0.001f),
                    "Opaque terrain never needs its backfaces and must keep paying nothing for them.");
            }
            finally
            {
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(primeMaterial);
                Object.DestroyImmediate(fluidMaterial);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidMaterialIsTransparentAndKeepsTheAuthoredAtlas()
        {
            Texture2D atlasTexture = null;
            Material sourceMaterial = null;
            Material fluidMaterial = null;

            try
            {
                sourceMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                fluidMaterial = BlockVisualAtlas.CreateFluidMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);

                Assert.That(fluidMaterial.renderQueue, Is.EqualTo((int)RenderQueue.Transparent),
                    "Water must render in the transparent queue or it blends against the clear colour before the seabed is drawn.");
                Assert.That(fluidMaterial.GetTag("RenderType", searchFallbacks: false), Is.EqualTo("Transparent"),
                    "The clone inherits the authored material's Opaque override tag unless it is explicitly replaced.");
                Assert.That(fluidMaterial.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha).Within(0.001f));
                Assert.That(fluidMaterial.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha).Within(0.001f));
                Assert.That(fluidMaterial.GetFloat("_ZWrite"), Is.EqualTo(0.0f).Within(0.001f),
                    "The depth prime already wrote water's depth; writing it again here would be redundant.");
                Assert.That(fluidMaterial.GetShaderPassEnabled(BlockVisualAtlas.WaterDepthPrimePassName), Is.False,
                    "The shading material must not also run the prime pass, or every water pixel is rasterised twice for nothing.");
                Assert.That(fluidMaterial.IsKeywordEnabled(BlockVisualAtlas.WaterShaderKeyword), Is.True,
                    "Without the keyword the water material renders through the opaque terrain path.");
                Assert.That(BlockVisualAtlas.TryGetBaseTexture(fluidMaterial, out Texture texture), Is.True);
                Assert.That(texture, Is.SameAs(atlasTexture),
                    "Water samples the same authored atlas as terrain; its transparency comes from the tint, not the texture.");
                Assert.That(fluidMaterial.GetColor("_TintEmberflow").a, Is.EqualTo(1.0f).Within(0.001f),
                    "Emberflow is opaque lava, not translucent water.");
                Assert.That(fluidMaterial.GetColor("_TintFreshwater").a, Is.LessThan(1.0f),
                    "Freshwater must let the seabed through or the whole feature is invisible.");
            }
            finally
            {
                Object.DestroyImmediate(fluidMaterial);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void SolidAtlasMaterialStaysOpaqueWithTheWaterKeywordOff()
        {
            Texture2D atlasTexture = null;
            Material sourceMaterial = null;
            Material blockMaterial = null;

            try
            {
                // The source carries the water state, so the assertions below prove CreateMaterial
                // RE-ASSERTS opaque rather than merely inheriting it: the runtime material is a
                // clone of an authored asset, and whatever surface state that asset happens to
                // carry would otherwise ride along into terrain rendering.
                sourceMaterial = CreateWaterishSourceMaterial(out atlasTexture);
                blockMaterial = BlockVisualAtlas.CreateMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);

                Assert.That(sourceMaterial.IsKeywordEnabled(BlockVisualAtlas.WaterShaderKeyword), Is.True,
                    "Fixture precondition: the source material must start with the water state for this test to mean anything.");

                Assert.That(blockMaterial.renderQueue, Is.EqualTo((int)RenderQueue.Geometry),
                    "Terrain stays in the geometry queue; adding water must not move a single opaque pixel.");
                Assert.That(blockMaterial.GetTag("RenderType", searchFallbacks: false), Is.EqualTo("Opaque"));
                Assert.That(blockMaterial.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One).Within(0.001f));
                Assert.That(blockMaterial.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.Zero).Within(0.001f));
                Assert.That(blockMaterial.GetFloat("_ZWrite"), Is.EqualTo(1.0f).Within(0.001f));
                Assert.That(blockMaterial.IsKeywordEnabled(BlockVisualAtlas.WaterShaderKeyword), Is.False,
                    "Terrain must never compile through the water variant.");
                Assert.That(blockMaterial.GetShaderPassEnabled(BlockVisualAtlas.WaterDepthPrimePassName), Is.False,
                    "Terrain must never run the water depth prime; it would cost an extra pass over every chunk in the world.");
            }
            finally
            {
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidDepthPrimeMaterialDrawsDepthOnlyBeforeTheShadingMaterial()
        {
            // The prime is what makes water blending order-independent: it claims each pixel for
            // the nearest water fragment, so a far wall seen through a near surface, or another
            // chunk's surface behind this one, is depth-rejected before it can blend. Without it
            // the number of blended layers depended on voxel submission order, so a patch of water
            // changed density as the player walked around it.
            Texture2D atlasTexture = null;
            Material sourceMaterial = null;
            Material primeMaterial = null;
            Material shadingMaterial = null;

            try
            {
                sourceMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                primeMaterial = BlockVisualAtlas.CreateFluidDepthPrimeMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);
                shadingMaterial = BlockVisualAtlas.CreateFluidMaterial(sourceMaterial, atlasTexture, BlockTextureSetIds.Default);

                Assert.That(primeMaterial.renderQueue, Is.LessThan(shadingMaterial.renderQueue),
                    "Render queue is what orders the prime before every water shading draw in the scene; nothing else guarantees it.");
                Assert.That(primeMaterial.renderQueue, Is.EqualTo(BlockVisualAtlas.FluidDepthPrimeRenderQueue));
                Assert.That(primeMaterial.GetFloat("_ZWrite"), Is.EqualTo(1.0f).Within(0.001f),
                    "The prime exists to write depth; without it the pass is a no-op.");
                Assert.That(primeMaterial.GetShaderPassEnabled(BlockVisualAtlas.WaterDepthPrimePassName), Is.True);
                Assert.That(primeMaterial.GetShaderPassEnabled("UniversalForward"), Is.False,
                    "The prime must not also shade, or water would blend twice and the pass would defeat its own purpose.");
                Assert.That(primeMaterial.IsKeywordEnabled(BlockVisualAtlas.WaterShaderKeyword), Is.True,
                    "The prime must compile the same water variant, or its vertices land at the undisplaced height and the depth it writes is wrong.");
            }
            finally
            {
                Object.DestroyImmediate(shadingMaterial);
                Object.DestroyImmediate(primeMaterial);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidChildRendersWithTheTransparentMaterialAndSolidChunksWithTheOpaqueOne()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 20);
            world.SetBlock(new BlockPosition(1, 0, 1), BlockRegistry.MeadowTurf, trackChange: false);
            world.SetBlock(new BlockPosition(2, 0, 1), BlockRegistry.Freshwater, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, BlockiverseProject.InteractionLayerIndex);

                MeshRenderer fluidRenderer = worldObject.GetComponentsInChildren<MeshRenderer>()
                    .Single(mesh => mesh.gameObject.name == "Fluid");
                MeshRenderer chunkRenderer = fluidRenderer.transform.parent.GetComponent<MeshRenderer>();

                Assert.That(fluidRenderer.sharedMaterials.Length, Is.EqualTo(2),
                    "The fluid mesh is drawn twice, once per material: depth prime then shading.");
                Assert.That(fluidRenderer.sharedMaterials[0].name, Is.EqualTo(BlockVisualAtlas.FluidDepthPrimeMaterialName));
                Assert.That(fluidRenderer.sharedMaterials[1].name, Is.EqualTo(BlockVisualAtlas.FluidMaterialName),
                    "The fluid child must use the transparent clone; sharing the terrain material is what made water opaque.");
                Assert.That(fluidRenderer.sharedMaterials[0].renderQueue,
                    Is.LessThan(fluidRenderer.sharedMaterials[1].renderQueue),
                    "Queue order, not array order, is what actually sequences the two draws.");
                Assert.That(chunkRenderer.sharedMaterials.Length, Is.EqualTo(1),
                    "Terrain is drawn once; only water pays for a second pass.");
                Assert.That(chunkRenderer.sharedMaterial.name, Is.EqualTo(BlockVisualAtlas.BlockMaterialName),
                    "Solid chunks keep the opaque material.");
                Assert.That(fluidRenderer.sharedMaterial, Is.Not.SameAs(chunkRenderer.sharedMaterial),
                    "Distinct shared materials, so terrain batching is unaffected by the water blend state.");
                Assert.That(fluidRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off),
                    "A translucent sheet casting a solid shadow over the seabed reads as a bug.");

                // The one line that joins the packed CPU channel to the GPU. Everything else about
                // the water look is downstream of it, and nothing else would notice if it vanished.
                Mesh fluidMesh = fluidRenderer.GetComponent<MeshFilter>().sharedMesh;
                Mesh chunkMesh = chunkRenderer.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(fluidMesh.uv2.Length, Is.EqualTo(fluidMesh.vertexCount),
                    "The fluid mesh must carry the packed water channel in UV1, or the shader reads zeros and water never moves.");
                Assert.That(fluidMesh.uv2.Any(packed => packed.x > 0.0f), Is.True,
                    "At least one surface vertex must be masked in the uploaded mesh.");
                Assert.That(chunkMesh.uv2.Length, Is.EqualTo(0),
                    "Terrain meshes carry no second UV channel; the water attributes exist only where water does.");
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void FluidMeshBoundsAbsorbTheWaveDip()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var world = new VoxelWorld(new WorldBounds(16, 16, 16), chunkSize: 16, seed: 21);
            world.SetBlock(new BlockPosition(2, 0, 2), BlockRegistry.Graystone, trackChange: false);
            world.SetBlock(new BlockPosition(2, 3, 2), BlockRegistry.Freshwater, trackChange: false);
            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
                renderer.Configure(world, registry, blockMaterial, BlockiverseProject.InteractionLayerIndex);

                MeshFilter fluidFilter = worldObject.GetComponentsInChildren<MeshFilter>()
                    .Single(filter => filter.gameObject.name == "Fluid");
                MeshFilter chunkFilter = fluidFilter.transform.parent.GetComponent<MeshFilter>();

                Assert.That(fluidFilter.sharedMesh.bounds.min.y,
                    Is.LessThanOrEqualTo(3.0f - VoxelWorldRenderer.MaxWaveDipMeters + 0.0001f),
                    "Fluid bounds must be padded downward or a wave trough gets frustum-culled and pops at the edge of vision.");
                Assert.That(chunkFilter.sharedMesh.bounds.min.y, Is.EqualTo(0.0f).Within(0.0001f),
                    "Only the fluid mesh is padded; terrain bounds stay exact.");
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
        }

        [Test]
        public void WaveAmplitudesFitInsideTheMeshBoundsPadding()
        {
            // The shader dips a surface vertex by up to 2x its amplitude. If a future look-dev pass
            // raises an amplitude past the padding, troughs leave the mesh bounds and pop -- a
            // device-only artefact that no runtime test would catch.
            string shader = File.ReadAllText(VoxelShaderPath);
            MatchCollection matches = Regex.Matches(shader, @"_Wave\w+\(""[^""]*"",\s*Vector\)\s*=\s*\(\s*([0-9.]+)");

            Assert.That(matches.Count, Is.EqualTo(3),
                "One wave vector per fluid family is expected in the shader properties.");

            foreach (Match match in matches)
            {
                float amplitude = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

                Assert.That(amplitude * 2.0f, Is.LessThanOrEqualTo(VoxelWorldRenderer.MaxWaveDipMeters + 0.0001f),
                    $"Wave amplitude {amplitude} dips further than the {VoxelWorldRenderer.MaxWaveDipMeters} m the fluid mesh bounds are padded by.");
            }
        }

        [Test]
        public void VoxelShaderDeclaresWaterAsALocalMultiCompileKeyword()
        {
            // multi_compile, not shader_feature: no material ASSET references this shader (the
            // materials are runtime clones), so shader_feature variants would be stripped from the
            // Android player and water would render opaque on device but correct in the editor.
            string shader = File.ReadAllText(VoxelShaderPath);

            Assert.That(shader, Does.Contain("#pragma multi_compile_local _ _BLOCKIVERSE_WATER"),
                "The water variant must be a multi_compile keyword to survive player-build stripping.");
            Assert.That(shader, Does.Not.Contain("shader_feature_local _ _BLOCKIVERSE_WATER"),
                "shader_feature would strip the water variant out of the Quest build.");
            Assert.That(shader, Does.Contain(BlockVisualAtlas.WaterShaderKeyword),
                "The keyword the material enables and the keyword the shader declares must be the same string.");
        }

        [Test]
        public void VoxelShaderDrivesForwardSurfaceStateFromTheMaterial()
        {
            string shader = File.ReadAllText(VoxelShaderPath);

            Assert.That(shader, Does.Contain("Blend [_SrcBlend] [_DstBlend]"),
                "Blend state must come from the material so one shader serves both opaque terrain and transparent water.");
            Assert.That(shader, Does.Contain("ZWrite [_ZWrite]"));
            Assert.That(shader, Does.Contain("Cull [_Cull]"));
            Assert.That(shader, Does.Contain("\"LightMode\" = \"ShadowCaster\""),
                "The shadow pass survives the water work; terrain still casts shadows.");
        }

        [Test]
        public void UnderwaterFogPaletteCoversEveryFamilyAndEmberflowIsDensest()
        {
            float freshwater = BlockiverseWaterView.FogDensityFor(FluidFamily.Freshwater);
            float brine = BlockiverseWaterView.FogDensityFor(FluidFamily.Brine);
            float emberflow = BlockiverseWaterView.FogDensityFor(FluidFamily.Emberflow);

            Assert.That(freshwater, Is.GreaterThan(0.0f), "Every family needs a positive underwater density.");
            Assert.That(brine, Is.GreaterThan(freshwater), "Brine reads murkier than fresh water.");
            Assert.That(emberflow, Is.GreaterThan(brine),
                "Emberflow must be near-opaque at arm's length: being submerged in lava should not read as a clear view.");

            Assert.That(BlockiverseWaterView.FogColorFor(FluidFamily.Emberflow).r,
                Is.GreaterThan(BlockiverseWaterView.FogColorFor(FluidFamily.Emberflow).b),
                "Emberflow fog is hot, not cold.");
            Assert.That(BlockiverseWaterView.FogColorFor(FluidFamily.Freshwater).b,
                Is.GreaterThan(BlockiverseWaterView.FogColorFor(FluidFamily.Freshwater).r),
                "Freshwater fog is cold, not hot.");
        }

        static void AssertSurfaceMaskMarksExactlyTheTopQuads(ChunkMeshData fluid, float topPlaneY)
        {
            Assert.That(fluid.FluidVertexData, Is.Not.Null, "Fluid meshes must carry the packed water channel.");
            Assert.That(fluid.FluidVertexData.Count, Is.EqualTo(fluid.Vertices.Count),
                "One packed entry per vertex, or the channel desynchronises from the mesh.");

            int maskedQuads = 0;

            for (int quad = 0; quad < fluid.Vertices.Count / 4; quad++)
            {
                int start = quad * 4;
                bool isTopQuad = Enumerable.Range(start, 4)
                    .All(index => Mathf.Approximately(fluid.Vertices[index].y, topPlaneY));
                float expected = isTopQuad ? 1.0f : 0.0f;

                if (isTopQuad)
                    maskedQuads++;

                for (int index = start; index < start + 4; index++)
                {
                    Assert.That(fluid.FluidVertexData[index].x, Is.EqualTo(expected).Within(0.0001f),
                        $"Vertex {index} at y={fluid.Vertices[index].y} carries the wrong surface mask; only emitted +Y faces may move.");
                }
            }

            Assert.That(maskedQuads, Is.EqualTo(1), "Exactly one top face is expected in this fixture.");
        }

        static IEnumerable<Vector2> MaskedQuadData(ChunkMeshData fluid, float topPlaneY)
        {
            for (int index = 0; index < fluid.Vertices.Count; index++)
            {
                if (fluid.FluidVertexData[index].x > 0.0f && Mathf.Approximately(fluid.Vertices[index].y, topPlaneY))
                    yield return fluid.FluidVertexData[index];
            }
        }

        // A source material already wearing the transparent water state, to prove the opaque path
        // actively resets it.
        static Material CreateWaterishSourceMaterial(out Texture2D atlasTexture)
        {
            Material material = CreateBlockAtlasMaterial(out atlasTexture);
            material.shader = Shader.Find(BlockVisualAtlas.VoxelLitShaderName);
            material.SetTexture("_BaseMap", atlasTexture);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword(BlockVisualAtlas.WaterShaderKeyword);
            return material;
        }

        static Material CreateBlockAtlasMaterial(out Texture2D atlasTexture)
        {
            atlasTexture = new Texture2D(
                BlockVisualAtlas.AtlasWidthPixels,
                BlockVisualAtlas.AtlasHeightPixels,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = BlockVisualAtlas.AuthoredAtlasName
            };

            Material material = new(Shader.Find("Sprites/Default"));
            material.mainTexture = atlasTexture;
            return material;
        }
    }
}
