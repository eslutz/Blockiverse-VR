using System;
using System.Collections.Generic;
using Blockiverse.Voxel;
using Unity.Profiling;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Views over ChunkMeshBuilder's pooled per-thread lists; the contents are valid only until
    // the next Build call on the same thread. Consumers must copy the data out (e.g. into a Mesh)
    // before triggering another rebuild.
    public sealed class ChunkMeshData
    {
        public ChunkMeshData(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            int faceCount,
            List<Vector2> fluidVertexData = null,
            List<int> cutoutTriangles = null,
            List<Vector3> normals = null)
        {
            CutoutTriangles = cutoutTriangles;
            Normals = normals;
            Vertices = vertices;
            Triangles = triangles;
            Uvs = uvs;
            Colors = colors;
            FaceCount = faceCount;
            FluidVertexData = fluidVertexData;
        }

        public List<Vector3> Vertices { get; }
        public List<int> Triangles { get; }
        public List<Vector2> Uvs { get; }
        public List<Color> Colors { get; }
        public int FaceCount { get; }

        // Fluid-only second UV channel (x = surface mask, y = FluidFamily index), null on solid
        // meshes. Vertex COLOR cannot carry it: VoxelLightSampler.ToVertexColor already spends
        // R, G and B on sky exposure, emitter reach and self emission, and water needs all three
        // (a torch-lit pool, a glowing emberflow) exactly as much as stone does.
        public List<Vector2> FluidVertexData { get; }

        // Alpha-cutout indices (leaf canopies) into the SAME Vertices list, rendered as submesh 1
        // with the cutout material. Deliberately a second index list rather than a second mesh:
        // the chunk's MeshCollider is fed MeshFilter.sharedMesh verbatim, so keeping leaves in the
        // chunk mesh is what keeps them collidable — a canopy you can fall through is a worse bug
        // than a canopy that looks solid. Null when the chunk has no cutout blocks.
        public List<int> CutoutTriangles { get; }

        // Explicit per-vertex normals. Only the foliage mesh supplies these: Mesh.RecalculateNormals
        // on two intersecting vertical quads yields horizontal normals, which lights grass as a pair
        // of vertical planes and reads near-black from above. Null on meshes that are happy to be
        // recalculated (cube faces are).
        public List<Vector3> Normals { get; }

        public int TriangleCount => Triangles.Count / 3;
        public int CutoutTriangleCount => CutoutTriangles == null ? 0 : CutoutTriangles.Count / 3;
    }

    public static class ChunkMeshBuilder
    {
        static readonly ProfilerMarker BuildMarker = new("Blockiverse.ChunkMeshBuilder.Build");

        // Build output lists are pooled per thread so chunk rebuilds do not allocate every call
        // (GC hitches on Quest). [ThreadStatic] keeps the pool safe should Build ever run off the
        // main thread; the returned ChunkMeshData aliases these lists, so each result must be
        // consumed before the next Build call on the same thread clears and reuses them.
        [ThreadStatic] static List<Vector3> pooledVertices;
        [ThreadStatic] static List<int> pooledTriangles;
        [ThreadStatic] static List<Vector2> pooledUvs;
        [ThreadStatic] static List<Color> pooledColors;
        [ThreadStatic] static List<Vector3> pooledFluidVertices;
        [ThreadStatic] static List<int> pooledFluidTriangles;
        [ThreadStatic] static List<Vector2> pooledFluidUvs;
        [ThreadStatic] static List<Color> pooledFluidColors;
        [ThreadStatic] static List<Vector2> pooledFluidVertexData;
        [ThreadStatic] static List<BlockPosition> pooledEmitters;
        // Cutout indices share the terrain vertex buffer, so only the index list is pooled here.
        [ThreadStatic] static List<int> pooledCutoutTriangles;
        // Cross-quad / decal foliage is its own mesh on its own GameObject, so it needs a full set.
        [ThreadStatic] static List<Vector3> pooledFoliageVertices;
        [ThreadStatic] static List<int> pooledFoliageTriangles;
        [ThreadStatic] static List<Vector2> pooledFoliageUvs;
        [ThreadStatic] static List<Color> pooledFoliageColors;
        [ThreadStatic] static List<Vector3> pooledFoliageNormals;

        static readonly BlockPosition[] NeighborOffsets =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 1, 0),
            new(0, -1, 0),
            new(0, 0, 1),
            new(0, 0, -1)
        };

        // FaceVertices[2] is the all-ones-in-Y quad and NeighborOffsets[2] is (0, 1, 0): the top
        // face -- the only face the water shader displaces along its whole height.
        public const int TopFaceIndex = 2;

        // NeighborOffsets[3] is (0, -1, 0). Everything that is neither 2 nor 3 is a side wall.
        public const int BottomFaceIndex = 3;

        // A cross block emits two quads, always. Named so the face-count arithmetic reads.
        public const int CrossQuadsPerBlock = 2;

        // How far a ground decal floats above the cell floor: enough to beat depth precision at
        // Quest's far plane, little enough that it never reads as hovering.
        public const float DecalGroundOffset = 0.02f;

        static readonly Vector3[,] FaceVertices =
        {
            { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) },
            { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) },
            { new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0) },
            { new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1) },
            { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) },
            { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) }
        };

        public static ChunkMeshData Build(
            VoxelWorld world,
            BlockRegistry registry,
            ChunkCoordinate chunk,
            VoxelSkyLightMap skyLight = null,
            VoxelEmitterIndex emitters = null)
        {
            return Build(world, registry, chunk, out _, out _, skyLight, emitters);
        }

        // Kept so existing two-mesh callers compile unchanged; foliage is discarded.
        public static ChunkMeshData Build(
            VoxelWorld world,
            BlockRegistry registry,
            ChunkCoordinate chunk,
            out ChunkMeshData fluidMesh,
            VoxelSkyLightMap skyLight = null,
            VoxelEmitterIndex emitters = null)
        {
            return Build(world, registry, chunk, out fluidMesh, out _, skyLight, emitters);
        }

        // Builds the chunk's render geometry in one walk, split into two meshes: solid faces
        // (rendered and collidable) and fluid faces (rendered, ray-targetable, but excluded from
        // physics contacts so players wade through water instead of walking on it).
        // `emitters` drives the per-face line-of-sight bake that stops realtime point lights from
        // reaching through solids. Without one (isolated tests) no occlusion data exists, so faces
        // bake as fully reachable rather than silently killing every torch.
        public static ChunkMeshData Build(
            VoxelWorld world,
            BlockRegistry registry,
            ChunkCoordinate chunk,
            out ChunkMeshData fluidMesh,
            out ChunkMeshData foliageMesh,
            VoxelSkyLightMap skyLight = null,
            VoxelEmitterIndex emitters = null)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            using ProfilerMarker.AutoScope buildScope = BuildMarker.Auto();

            List<Vector3> vertices = pooledVertices ??= new();
            List<int> triangles = pooledTriangles ??= new();
            List<Vector2> uvs = pooledUvs ??= new();
            List<Color> colors = pooledColors ??= new();
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();
            int faceCount = 0;

            List<Vector3> fluidVertices = pooledFluidVertices ??= new();
            List<int> fluidTriangles = pooledFluidTriangles ??= new();
            List<Vector2> fluidUvs = pooledFluidUvs ??= new();
            List<Color> fluidColors = pooledFluidColors ??= new();
            List<Vector2> fluidVertexData = pooledFluidVertexData ??= new();
            fluidVertices.Clear();
            fluidTriangles.Clear();
            fluidUvs.Clear();
            fluidColors.Clear();
            fluidVertexData.Clear();
            int fluidFaceCount = 0;

            List<int> cutoutTriangles = pooledCutoutTriangles ??= new();
            cutoutTriangles.Clear();
            int cutoutFaceCount = 0;

            List<Vector3> foliageVertices = pooledFoliageVertices ??= new();
            List<int> foliageTriangles = pooledFoliageTriangles ??= new();
            List<Vector2> foliageUvs = pooledFoliageUvs ??= new();
            List<Color> foliageColors = pooledFoliageColors ??= new();
            List<Vector3> foliageNormals = pooledFoliageNormals ??= new();
            foliageVertices.Clear();
            foliageTriangles.Clear();
            foliageUvs.Clear();
            foliageColors.Clear();
            foliageNormals.Clear();
            int foliageFaceCount = 0;

            int startX = chunk.X * world.ChunkSize;
            int startY = chunk.Y * world.ChunkSize;
            int startZ = chunk.Z * world.ChunkSize;
            int endX = Math.Min(startX + world.ChunkSize, world.Bounds.Width);
            int endY = Math.Min(startY + world.ChunkSize, world.Bounds.Height);
            int endZ = Math.Min(startZ + world.ChunkSize, world.Bounds.Depth);

            List<BlockPosition> nearbyEmitters = pooledEmitters ??= new();
            nearbyEmitters.Clear();
            if (emitters != null)
            {
                int reach = VoxelLightSampler.MaxEmitterReachDistance;
                emitters.CollectInRange(
                    new BlockPosition(startX - reach, startY - reach, startZ - reach),
                    new BlockPosition(endX - 1 + reach, endY - 1 + reach, endZ - 1 + reach),
                    nearbyEmitters);
            }

            for (int y = Math.Max(0, startY); y < endY; y++)
            {
                for (int z = Math.Max(0, startZ); z < endZ; z++)
                {
                    for (int x = Math.Max(0, startX); x < endX; x++)
                    {
                        var position = new BlockPosition(x, y, z);
                        BlockDefinition definition = registry.Get(world.GetBlock(position));

                        if (!definition.IsRenderable)
                            continue;

                        bool isFluid = definition.Category == BlockCategory.Fluid;
                        float selfEmission = definition.EmissiveLight / (float)VoxelLightSampler.MaxEmissiveLevel;
                        BlockRenderShape shape = definition.RenderShape;

                        // Cross blocks are not built out of faces at all: they emit fixed geometry
                        // once per cell and skip the six-face loop entirely. Their light is sampled
                        // from their OWN cell, because the per-face bake below samples the NEIGHBOUR
                        // across a face direction and these have no face direction.
                        if (shape == BlockRenderShape.Cross || shape == BlockRenderShape.Decal)
                        {
                            float ownSky = VoxelLightSampler.SampleSkyExposure(
                                world, registry, position, skyLight: skyLight);
                            // Omnidirectional, not a face sample. Passing a face normal here makes
                            // SampleEmitterReach reject every emitter at or below the plant before
                            // line-of-sight is even tested, so a torch on the ground beside a bush
                            // would leave it unlit.
                            float ownReach = emitters == null
                                ? 1.0f
                                : VoxelLightSampler.SampleOmnidirectionalEmitterReach(
                                    world, registry, position, nearbyEmitters);
                            Color foliageColor = VoxelLightSampler.ToVertexColor(ownSky, ownReach, selfEmission);

                            if (shape == BlockRenderShape.Cross)
                            {
                                AddCrossQuads(
                                    foliageVertices, foliageTriangles, foliageUvs, foliageColors, foliageNormals,
                                    position, definition.Id, foliageColor);
                                foliageFaceCount += CrossQuadsPerBlock;
                            }
                            else
                            {
                                AddGroundDecal(
                                    foliageVertices, foliageTriangles, foliageUvs, foliageColors, foliageNormals,
                                    position, definition.Id, foliageColor);
                                foliageFaceCount++;
                            }

                            continue;
                        }

                        // Resolved on the first face this cell actually emits, not once per fluid
                        // cell: PlaceFluids fills entire submerged volumes whose interior cells
                        // emit nothing, and they would all pay the lookup on every RebuildAll.
                        FluidFamily fluidFamily = FluidFamily.Freshwater;
                        bool fluidFamilyResolved = false;

                        for (int face = 0; face < NeighborOffsets.Length; face++)
                        {
                            BlockPosition neighbor = position + NeighborOffsets[face];

                            if (!ShouldRenderFace(world, registry, definition, neighbor, face))
                                continue;

                            // The cell a face looks into is normally air, because a face was only
                            // ever emitted toward something that did not occlude. Cutout leaves
                            // broke that: they stopped occluding FACES but still block LIGHT, so
                            // an interior canopy face now looks into another leaf cell — and
                            // sampling there returns cave darkness, painting every interior leaf
                            // (and every trunk face inside a canopy) pure black.
                            //
                            // Walk outward along the face normal to the first cell that actually
                            // transmits light, and dim by how far we had to go, so interior leaves
                            // read darker than the canopy surface instead of black.
                            BlockPosition lightCell = ResolveLightSampleCell(
                                world, registry, neighbor, NeighborOffsets[face],
                                out int blockedSteps, out bool foundLitCell);
                            float depthFade = InteriorLightFalloff(blockedSteps);

                            // Deeply buried: no lit cell within the budget. Use the falloff itself
                            // as the exposure rather than multiplying it by a zero sample, so the
                            // face reads as dim interior rather than collapsing back to black.
                            float skyExposure = foundLitCell
                                ? depthFade * VoxelLightSampler.SampleSkyExposure(
                                    world, registry, lightCell, skyLight: skyLight)
                                : depthFade;
                            float emitterReach = emitters == null
                                ? 1.0f
                                : foundLitCell
                                    ? depthFade * VoxelLightSampler.SampleEmitterReach(
                                        world, registry, lightCell, NeighborOffsets[face], nearbyEmitters)
                                    : 0.0f;
                            Color vertexColor = VoxelLightSampler.ToVertexColor(skyExposure, emitterReach, selfEmission);

                            if (isFluid)
                            {
                                if (!fluidFamilyResolved)
                                {
                                    FluidBlocks.TryGetFamily(definition.Id, out fluidFamily);
                                    fluidFamilyResolved = true;
                                }

                                // A side wall standing on a lower neighbour's surface has to ride
                                // that surface down, or the wave opens a slit beneath it at every
                                // step in flowing water.
                                bool wallFootFollowsSurface =
                                    face != TopFaceIndex &&
                                    face != BottomFaceIndex &&
                                    HasSameFamilySurfaceBelow(world, registry, neighbor, fluidFamily);

                                AddFluidFace(
                                    fluidVertices, fluidTriangles, fluidUvs, fluidColors, fluidVertexData,
                                    position, face, definition.Id, vertexColor, fluidFamily, wallFootFollowsSurface);
                                fluidFaceCount++;
                            }
                            else if (shape == BlockRenderShape.CutoutCube)
                            {
                                // Identical geometry to a cube — only the index list differs, so
                                // these land in submesh 1 and are drawn by the cutout material.
                                // Sharing the vertex buffer is what keeps leaves inside the chunk
                                // mesh, and therefore inside the chunk's collider.
                                AddFace(vertices, cutoutTriangles, uvs, colors, position, face, definition.Id, vertexColor,
                                    varyTileOrientation: true);
                                cutoutFaceCount++;
                            }
                            else
                            {
                                AddFace(vertices, triangles, uvs, colors, position, face, definition.Id, vertexColor);
                                faceCount++;
                            }
                        }
                    }
                }
            }

            nearbyEmitters.Clear();

            fluidMesh = new ChunkMeshData(
                fluidVertices, fluidTriangles, fluidUvs, fluidColors, fluidFaceCount, fluidVertexData);
            foliageMesh = new ChunkMeshData(
                foliageVertices, foliageTriangles, foliageUvs, foliageColors, foliageFaceCount,
                normals: foliageNormals);

            // FaceCount is the terrain face total and drives the renderer's "is this chunk empty"
            // check, so a chunk holding ONLY leaves must not report zero — it would be released and
            // the canopy would vanish.
            return new ChunkMeshData(
                vertices, triangles, uvs, colors, faceCount + cutoutFaceCount,
                cutoutTriangles: cutoutTriangles);
        }

        // True when the cell under this face's (non-fluid) neighbour is the same fluid family. That
        // cell's top face is an emitted, displaced surface at exactly the height of our wall's foot
        // and at exactly the same x/z, so both dip by the same amount and the seam stays closed.
        static bool HasSameFamilySurfaceBelow(
            VoxelWorld world, BlockRegistry registry, BlockPosition neighbor, FluidFamily family)
        {
            BlockPosition below = neighbor + NeighborOffsets[BottomFaceIndex];

            if (!world.Bounds.Contains(below))
                return false;

            return FluidBlocks.TryGetFamily(world.GetBlock(below), out FluidFamily belowFamily) &&
                   belowFamily == family;
        }

        static bool ShouldRenderFace(
            VoxelWorld world, BlockRegistry registry, BlockDefinition current, BlockPosition neighbor, int faceIndex)
        {
            if (!world.Bounds.Contains(neighbor))
                return true;

            BlockDefinition neighborDefinition = registry.Get(world.GetBlock(neighbor));

            // Adjacent cells of the same fluid family (source or flowing) merge into one volume —
            // internal faces between them would otherwise z-fight inside every lake and stream.
            if (current.Category == BlockCategory.Fluid &&
                FluidBlocks.TryGetFamily(current.Id, out FluidFamily currentFamily) &&
                FluidBlocks.TryGetFamily(neighborDefinition.Id, out FluidFamily neighborFamily) &&
                currentFamily == neighborFamily)
            {
                return false;
            }

            // Same problem, different geometry: two adjacent non-occluding cells of the same block
            // (a canopy interior) each want to emit the face on their shared plane, producing two
            // COINCIDENT coplanar quads. Fluids merge them away entirely because a lake interior
            // should not be drawn at all; leaves must NOT merge — the interior is exactly what the
            // alpha gaps are meant to reveal — so emit the shared plane exactly ONCE instead.
            //
            // Left as a duplicate pair it is not merely wasted work: the two quads carry DIFFERENT
            // vertex colours (each bakes light by walking outward in the opposite direction), both
            // write depth, and the material is two-sided, so they z-fight at equal depth and
            // flicker per eye in VR. Halving the interior face count is the secondary benefit.
            //
            // Emitting only on the positive-axis faces (indices 0, 2, 4 = +X, +Y, +Z) is what makes
            // the choice deterministic and chunk-order independent: for any shared plane exactly
            // one of the two cells sees it as a positive-direction face.
            if (!current.OccludesFaces &&
                neighborDefinition.IsRenderable &&
                !neighborDefinition.OccludesFaces &&
                current.Id.Equals(neighborDefinition.Id))
            {
                return (faceIndex & 1) == 0;
            }

            return !neighborDefinition.IsRenderable || !neighborDefinition.OccludesFaces;
        }

        // A distinct name, not an AddFace overload: ChunkRenderingEditModeTests reflects AddFace
        // up by name and a second overload would throw AmbiguousMatch.
        static void AddFluidFace(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            List<Vector2> fluidVertexData,
            BlockPosition position,
            int faceIndex,
            BlockId blockId,
            Color vertexColor,
            FluidFamily family,
            bool wallFootFollowsSurface)
        {
            AddFace(vertices, triangles, uvs, colors, position, faceIndex, blockId, vertexColor);

            // The surface mask comes from the face index, never from a vertex height test, so it
            // marks exactly the +Y faces that were actually emitted. A water cell with a sapling,
            // a different fluid, or a placed block above still emits its top face at full height
            // and still gets masked, so neighbouring water can never dip away from a face that
            // stayed put -- the shoreline crack that killed the surface-drop design is not
            // expressible here.
            bool topFace = faceIndex == TopFaceIndex;

            for (int i = 0; i < 4; i++)
            {
                // The whole top face moves; on a side wall only the foot moves, and only when it
                // is standing on a neighbouring surface that moves with it.
                bool footVertex = FaceVertices[faceIndex, i].y == 0.0f;
                bool masked = topFace || (wallFootFollowsSurface && footVertex);

                fluidVertexData.Add(new Vector2(masked ? 1.0f : 0.0f, (int)family));
            }
        }

        // How far outward the light search will walk before giving up. A canopy is a few cells
        // thick; beyond that the fragment is genuinely buried and the floor below is the right
        // answer anyway. Bounded because this runs per emitted face.
        const int MaxInteriorLightSteps = 4;

        // Per-cell dimming for a face buried inside light-blocking geometry. Not physical
        // attenuation — just enough separation that an interior leaf reads dimmer than the canopy
        // surface rather than identical to it or black.
        const float InteriorLightStepFalloff = 0.65f;

        // Walks outward from `start` along `offset` until it finds a cell that transmits light,
        // returning that cell and how many blockers it passed through. Returns `start` unchanged
        // (with zero steps) in the overwhelmingly common case where `start` is already passable,
        // so ordinary terrain pays one predicate call and nothing else.
        static BlockPosition ResolveLightSampleCell(
            VoxelWorld world,
            BlockRegistry registry,
            BlockPosition start,
            BlockPosition offset,
            out int blockedSteps,
            out bool foundLitCell)
        {
            blockedSteps = 0;
            BlockPosition cell = start;

            for (int step = 0; step < MaxInteriorLightSteps; step++)
            {
                if (!world.Bounds.Contains(cell))
                {
                    foundLitCell = true;
                    return cell;
                }

                BlockDefinition definition = registry.Get(world.GetBlock(cell));
                if (VoxelLightSampler.IsLightPassable(definition))
                {
                    foundLitCell = true;
                    return cell;
                }

                blockedSteps++;
                cell += offset;
            }

            // Budget exhausted while still inside light-blocking geometry. Sampling here would
            // return cave darkness and multiply the falloff by zero, putting the interior straight
            // back to pure black — the exact bug the walk exists to fix, just relocated to
            // canopies thicker than the budget. A radius-3 canopy is seven cells across and two
            // overlapping trees are thicker, so this is reachable in ordinary worlds even though a
            // small test fixture never reaches it.
            foundLitCell = false;
            return cell;
        }

        static float InteriorLightFalloff(int blockedSteps)
        {
            if (blockedSteps <= 0)
                return 1.0f;

            float falloff = 1.0f;
            for (int i = 0; i < blockedSteps; i++)
                falloff *= InteriorLightStepFalloff;

            return falloff;
        }

        // Two intersecting vertical quads on the cell's diagonals — the standard voxel foliage
        // shape. Distinct name, NOT an AddFace overload: ChunkRenderingEditModeTests reflects
        // AddFace up by name and a second overload would throw AmbiguousMatch.
        //
        // Fixed planes rather than a camera-facing billboard, per vegetation ruleset §4a.2: a
        // single billboard reads as a flat card in stereo, which this project already rejected
        // once for the lightning bolt. Two fixed planes cost the same two quads and have no such
        // failure mode.
        static void AddCrossQuads(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            List<Vector3> normals,
            BlockPosition position,
            BlockId blockId,
            Color vertexColor)
        {
            Rect uvRect = BlockVisualAtlas.GetTileRect(blockId);
            var origin = new Vector3(position.X, position.Y, position.Z);

            // Every cross used to be the same two corner-to-corner quads at exactly 1.0 tall, so a
            // field of grass was one silhouette stamped on a lattice: uniform height and a visible
            // grid. Position-hashed so it is stable across remeshes and identical on every peer.
            uint h = DeterministicHash.Hash(0, position.X, position.Y, position.Z, salt: CrossVariantSalt);

            // Continuous yaw, not a 90-degree snap -- a snap still lines neighbours up.
            float yaw = (h & 1023u) / 1024.0f * Mathf.PI;
            float height = Mathf.Lerp(0.66f, 1.0f, ((h >> 10) & 255u) / 255.0f);
            float offsetX = ((((h >> 18) & 63u) / 63.0f) - 0.5f) * 0.24f;
            float offsetZ = ((((h >> 24) & 63u) / 63.0f) - 0.5f) * 0.24f;

            var centre = origin + new Vector3(0.5f + offsetX, 0.0f, 0.5f + offsetZ);

            // Three planes, not two. An X resolves as exactly two flat cards from the side, which
            // is most obvious in VR where each eye separates them.
            for (int plane = 0; plane < CrossPlaneCount; plane++)
            {
                float angle = yaw + plane * (Mathf.PI / CrossPlaneCount);
                var half = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * CrossHalfWidth;
                var top = new Vector3(0.0f, height, 0.0f);

                AddFoliageQuad(vertices, triangles, uvs, colors, normals, uvRect, vertexColor,
                    centre - half,
                    centre - half + top,
                    centre + half + top,
                    centre + half);
            }
        }

        // Distinct from the placement salts so orientation and placement cannot correlate.
        const int CrossVariantSalt = 7717;
        const int CrossPlaneCount = 3;
        // A rotated plane sweeps a circle of this radius, so it stays inside the cell at any yaw.
        const float CrossHalfWidth = 0.5f;

        // A single quad just above the cell floor, for flat groundcover (moss, lichen, leaf
        // litter). Distinct name for the same reflection reason as AddCrossQuads.
        static void AddGroundDecal(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            List<Vector3> normals,
            BlockPosition position,
            BlockId blockId,
            Color vertexColor)
        {
            Rect uvRect = BlockVisualAtlas.GetTileRect(blockId);
            var origin = new Vector3(position.X, position.Y, position.Z);
            float y = DecalGroundOffset;

            // Same winding as the cube top face so it faces up.
            AddFoliageQuad(vertices, triangles, uvs, colors, normals, uvRect, vertexColor,
                origin + new Vector3(0, y, 1),
                origin + new Vector3(1, y, 1),
                origin + new Vector3(1, y, 0),
                origin + new Vector3(0, y, 0));
        }

        // Shared quad emitter for the foliage stream. Writes an explicit UP normal for every
        // vertex instead of leaving it to Mesh.RecalculateNormals: recalculated normals on two
        // intersecting vertical planes point sideways, so grass would light as a pair of walls and
        // read near-black from above. Shading foliage as though it faces the sky is both the
        // cheaper and the better-looking answer.
        static void AddFoliageQuad(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            List<Vector3> normals,
            Rect uvRect,
            Color vertexColor,
            Vector3 bottomLeft,
            Vector3 topLeft,
            Vector3 topRight,
            Vector3 bottomRight)
        {
            int vertexStart = vertices.Count;

            vertices.Add(bottomLeft);
            vertices.Add(topLeft);
            vertices.Add(topRight);
            vertices.Add(bottomRight);

            for (int i = 0; i < 4; i++)
            {
                colors.Add(vertexColor);
                normals.Add(Vector3.up);
            }

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));

            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);
        }

        // Signature is changed in place rather than overloaded: ChunkRenderingEditModeTests looks
        // this method up by name via reflection, and a second overload would throw AmbiguousMatch.
        // Corner 0..3 of an atlas tile, counter-clockwise from bottom-left.
        static Vector2 TileCorner(Rect uvRect, int corner)
        {
            return corner switch
            {
                0 => new Vector2(uvRect.xMin, uvRect.yMin),
                1 => new Vector2(uvRect.xMin, uvRect.yMax),
                2 => new Vector2(uvRect.xMax, uvRect.yMax),
                _ => new Vector2(uvRect.xMax, uvRect.yMin),
            };
        }

        const int TileOrientationSalt = 8821;

        static void AddFace(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            List<Color> colors,
            BlockPosition position,
            int faceIndex,
            BlockId blockId,
            Color vertexColor,
            bool varyTileOrientation = false)
        {
            int vertexStart = vertices.Count;
            // Face-aware: a turf block samples grass on top, dirt on the sides, loam underneath.
            Rect uvRect = BlockVisualAtlas.GetTileRect(blockId, faceIndex);
            var origin = new Vector3(position.X, position.Y, position.Z);

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = FaceVertices[faceIndex, i];
                vertices.Add(origin + corner);
                colors.Add(vertexColor);
            }

            // Permuting the corner order rotates and mirrors the SAME tile: eight appearances for
            // zero atlas slots and zero extra samples, which is the cheapest answer to "every leaf
            // block looks identical". Opt-in, because it is only valid for tiles with no inherent
            // orientation -- leaves qualify, a log's rings or a turf's grass fringe do not.
            if (varyTileOrientation)
            {
                uint v = DeterministicHash.Hash(0, position.X, position.Y, position.Z, salt: TileOrientationSalt);
                v ^= (uint)faceIndex * 2654435761u;   // six sides of one block must not shuffle alike

                int rotation = (int)(v & 3u);
                bool mirror = ((v >> 2) & 1u) != 0u;

                for (int i = 0; i < 4; i++)
                {
                    int corner = (i + rotation) & 3;
                    uvs.Add(TileCorner(uvRect, mirror ? 3 - corner : corner));
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    uvs.Add(TileCorner(uvRect, i));
            }

            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 0);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);
        }
    }

    public sealed class ChunkRebuildQueue
    {
        // Wide enough for both bakes: the sky-openness probe, and the emitter line-of-sight reach
        // (placing or removing a block up to 15 cells from a spark flare can change which faces
        // that flare can see).
        // Math.Max is not a constant expression; the conditional keeps this a compile-time const.
        const int LightingProbeInvalidationPadding =
            (VoxelLightSampler.DefaultProbeDistance > VoxelLightSampler.MaxEmitterReachDistance
                ? VoxelLightSampler.DefaultProbeDistance
                : VoxelLightSampler.MaxEmitterReachDistance) + 1;

        readonly VoxelWorld world;
        readonly VoxelSkyLightMap skyLight;
        readonly VoxelEmitterIndex emitters;
        readonly HashSet<ChunkCoordinate> dirtyChunks = new();
        readonly List<ChunkCoordinate> drainSnapshot = new();

        public ChunkRebuildQueue(VoxelWorld world, VoxelSkyLightMap skyLight = null, VoxelEmitterIndex emitters = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.skyLight = skyLight;
            this.emitters = emitters;
            world.BlockChanged += OnBlockChanged;
        }

        public int Count => dirtyChunks.Count;

        // Unsubscribes from the world; call when the renderer is reconfigured onto a new world
        // so the stale queue does not keep marking chunks for it.
        public void Detach()
        {
            world.BlockChanged -= OnBlockChanged;
        }

        public void MarkDirty(ChunkCoordinate chunk)
        {
            dirtyChunks.Add(chunk);
        }

        // Drops a single chunk from the dirty set without rebuilding it. The eager spawn-region
        // bake uses this to claim its chunks so the later incremental drain does not rebuild them
        // again. Returns true if the chunk was queued.
        public bool ClearDirty(ChunkCoordinate chunk) => dirtyChunks.Remove(chunk);

        public IReadOnlyCollection<ChunkCoordinate> DrainDirtyChunks()
        {
            return DrainDirtyChunks(int.MaxValue);
        }

        public IReadOnlyCollection<ChunkCoordinate> DrainDirtyChunks(int maxCount)
        {
            // The per-world-tick RebuildDirty pump drains this every tick even when nothing is
            // dirty; return the shared empty array in that case so a static world allocates no
            // garbage per tick.
            if (dirtyChunks.Count == 0 || maxCount <= 0)
                return Array.Empty<ChunkCoordinate>();

            DrainDirtyChunks(drainSnapshot, maxCount);
            return drainSnapshot;
        }

        public int DrainDirtyChunks(List<ChunkCoordinate> destination, int maxCount)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            if (dirtyChunks.Count == 0 || maxCount <= 0)
                return 0;

            if (maxCount >= dirtyChunks.Count)
            {
                destination.AddRange(dirtyChunks);
                dirtyChunks.Clear();
                return destination.Count;
            }

            foreach (ChunkCoordinate chunk in dirtyChunks)
            {
                destination.Add(chunk);
                if (destination.Count >= maxCount)
                    break;
            }

            foreach (ChunkCoordinate chunk in destination)
                dirtyChunks.Remove(chunk);

            return destination.Count;
        }

        void OnBlockChanged(BlockChange change)
        {
            ChunkCoordinate changedChunk = world.GetChunkCoordinate(change.Position);
            MarkDirty(changedChunk);

            // Keep the emitter index ahead of the deferred rebuild so the rebuilt faces trace
            // toward the world as it is now, not as it was.
            emitters?.ApplyChange(change);

            if (skyLight == null)
            {
                // No sky map (isolated construction): conservatively invalidate the whole lit
                // column below the edit, as before.
                MarkLightingAffectedChunks(change.Position.X, change.Position.Z, 0, change.Position.Y);
            }
            else if (skyLight.ApplyChange(change, out int previousTop, out int newTop))
            {
                // The column's sky profile moved (a surface block was added/removed): every cell
                // between the ground and the higher of the two tops may change classification.
                int maxY = Math.Max(change.Position.Y, Math.Max(previousTop, newTop));
                MarkLightingAffectedChunks(change.Position.X, change.Position.Z, 0, maxY);
            }
            else
            {
                // Sky profile unchanged (typical mining/building underground or beneath cover):
                // light can only differ within probe range of the edit, not all the way down.
                MarkLightingAffectedChunks(
                    change.Position.X,
                    change.Position.Z,
                    Math.Max(0, change.Position.Y - LightingProbeInvalidationPadding),
                    Math.Min(world.Bounds.Height - 1, change.Position.Y + LightingProbeInvalidationPadding));
            }

            BlockPosition local = ChunkCoordinate.LocalPositionFromBlockPosition(change.Position, world.ChunkSize);
            MarkNeighborIfNeeded(local.X == 0, change.Position + new BlockPosition(-1, 0, 0));
            MarkNeighborIfNeeded(local.X == world.ChunkSize - 1, change.Position + new BlockPosition(1, 0, 0));
            MarkNeighborIfNeeded(local.Y == 0, change.Position + new BlockPosition(0, -1, 0));
            MarkNeighborIfNeeded(local.Y == world.ChunkSize - 1, change.Position + new BlockPosition(0, 1, 0));
            MarkNeighborIfNeeded(local.Z == 0, change.Position + new BlockPosition(0, 0, -1));
            MarkNeighborIfNeeded(local.Z == world.ChunkSize - 1, change.Position + new BlockPosition(0, 0, 1));
        }

        void MarkLightingAffectedChunks(int x, int z, int minY, int maxY)
        {
            int minX = Math.Max(0, x - LightingProbeInvalidationPadding);
            int maxX = Math.Min(world.Bounds.Width - 1, x + LightingProbeInvalidationPadding);
            int minZ = Math.Max(0, z - LightingProbeInvalidationPadding);
            int maxZ = Math.Min(world.Bounds.Depth - 1, z + LightingProbeInvalidationPadding);

            ChunkCoordinate minChunk = ChunkCoordinate.FromBlockPosition(new BlockPosition(minX, minY, minZ), world.ChunkSize);
            ChunkCoordinate maxChunk = ChunkCoordinate.FromBlockPosition(new BlockPosition(maxX, maxY, maxZ), world.ChunkSize);

            for (int chunkY = minChunk.Y; chunkY <= maxChunk.Y; chunkY++)
            {
                for (int chunkZ = minChunk.Z; chunkZ <= maxChunk.Z; chunkZ++)
                {
                    for (int chunkX = minChunk.X; chunkX <= maxChunk.X; chunkX++)
                        MarkDirty(new ChunkCoordinate(chunkX, chunkY, chunkZ));
                }
            }
        }

        void MarkNeighborIfNeeded(bool condition, BlockPosition neighbor)
        {
            if (!condition || !world.Bounds.Contains(neighbor))
                return;

            MarkDirty(world.GetChunkCoordinate(neighbor));
        }
    }

    public readonly struct VoxelRenderStats
    {
        public VoxelRenderStats(int chunkCount, int triangleCount, int queuedRebuildCount)
        {
            ChunkCount = chunkCount;
            TriangleCount = triangleCount;
            QueuedRebuildCount = queuedRebuildCount;
        }

        public int ChunkCount { get; }
        public int TriangleCount { get; }
        public int QueuedRebuildCount { get; }
    }
}
