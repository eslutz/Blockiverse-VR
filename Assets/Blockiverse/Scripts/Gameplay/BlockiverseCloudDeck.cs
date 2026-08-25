using System.Collections.Generic;
using Blockiverse.Voxel;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// A blocky cloud layer rendered as real geometry above the world, drifting with the wind.
    ///
    /// Why geometry and not more skybox: the procedural sky is painted at infinity, so it has no
    /// volume and no underside. A deck of actual cells reads as a voxel sky — you see thickness at
    /// a grazing angle, the shading under it differs from the top, and it matches the art direction
    /// of everything below it. The two layers divide the work: the deck carries the clouds you can
    /// see the shape of, and the skybox veil carries the rest of the hemisphere, which is what the
    /// deck dissolves INTO at its rim rather than ending against.
    ///
    /// THREE constraints shaped this, and each is easy to get wrong:
    ///
    /// 1. RENDER-ONLY. Clouds are never voxels: no BlockRegistry entry, no save-format presence,
    ///    no collider. They are a pure function of (seed, clock), which also means every peer
    ///    computes the same sky with nothing on the wire — consistent with the lockstep world sim.
    ///
    /// 2. NO NEW SHADER. GraphicsSettings' always-included list carries the voxel shader alone, so
    ///    a cloud shader reached through Shader.Find would be stripped from the Android player and
    ///    render magenta on device while looking correct in the editor. The deck therefore renders
    ///    through that one shader's unlit `_BLOCKIVERSE_SKY` variant, which reads vertex colour as
    ///    a literal colour and samples no texture. Being opaque, it has NO alpha to fade its rim
    ///    with — which is why the rim treatment below is done in geometry and vertex colour.
    ///
    /// 3. ABOVE THE BUILD LIMIT. At WorldMaxY 127 a deck inside world bounds is reachable, which
    ///    means handling the camera being inside a cloud. Sitting above everything buildable skips
    ///    that case entirely; flythrough is a deliberate follow-up, not an oversight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseCloudDeck : MonoBehaviour
    {
        public const float CellMeters = 10.0f;

        // Grid extent, and the RADIUS actually filled inside it.
        //
        // The deck used to be a filled 56x56 square, and its perimeter was a square you could
        // read as one: a rectangle's corners sit 1.41x further out than its edge midpoints, so at
        // a fixed altitude the boundary rose and fell around the compass in an unmistakably
        // man-made way ("the clear square boundary edges of these new clouds"). A circle has no
        // such signature — the boundary sits at one elevation angle in every direction, which is
        // what a real cloud field's vanishing distance does.
        public const int GridCells = 92;
        public const float RadiusCells = 45.0f;

        // Where the radial dissolve begins, as a fraction of RadiusCells. Between here and the
        // rim, coverage ramps to zero and colour crosses to the horizon — so the deck thins out
        // and takes the sky's own colour instead of ending on a line.
        public const float RimFadeStartFraction = 0.46f;

        public const float AltitudeMeters = 160.0f;
        public const float ThicknessMeters = 5.0f;
        public const float DriftMetersPerSecond = 1.6f;

        // How far the head must move before the deck re-evaluates which cell it is over.
        //
        // Without this the trigger is FloorToInt(head.x / CellMeters) straight off the headset, and
        // a tracked head never holds still: a player standing on a 10 m cell boundary flips that
        // index back and forth on tracking jitter alone and rebuilds the entire deck EVERY FRAME —
        // 8,464 density evaluations plus a full mesh build, sustained, with nothing logged.
        public const float RebuildDeadZoneMeters = 0.5f;

        // MEASURED quantiles of the density field below: entry i is the threshold that leaves
        // roughly CoverageStops[i] of the sky covered.
        //
        // A TABLE and not a formula, because the first attempt used one. It thresholded a 3x3
        // average of uniform hashes against a band around 0.5 -- but averaging nine uniforms
        // concentrates the field at 0.5 with a standard deviation of only 0.096, so even a narrow
        // band swallowed most cells: coverage 0.05 produced 57% cloud and 0.3 produced 91%.
        // "Clear sky is full of clouds and they're all connected."
        //
        // THE STOPS ARE DENSE AT BOTH ENDS ON PURPOSE. The previous table ran a straight line from
        // "empty" at coverage 0 to the f=0.05 quantile, straight across the density tail, and did
        // the same from f=0.85 down to nothing. Both ends were badly wrong as a result, and the
        // error at the low end was the one that mattered: Clear asks for 1.78% of cells and got
        // 0.07% — a 25x miss, i.e. a completely empty sky where a few small masses were intended.
        //
        // Measured over 43,200 cells across 3 seeds by replicating DeterministicHash exactly.
        // Worst error across all ten weather states after this: 0.46 percentage points. Verified
        // end to end by BlockiverseCloudDeckEditModeTests.CoverageProducesRoughlyThatFractionOfSky,
        // whose tolerance is now 0.02 — at the old 0.12 the 25x miss above passed cleanly.
        static readonly float[] CoverageStops =
        {
            0.000f, 0.005f, 0.010f, 0.020f, 0.030f, 0.050f, 0.100f, 0.200f, 0.300f,
            0.400f, 0.500f, 0.600f, 0.700f, 0.850f, 0.900f, 0.950f, 1.000f,
        };

        static readonly float[] CoverageCutoffs =
        {
            1.0100f, 0.8700f, 0.8408f, 0.8085f, 0.7862f, 0.7538f, 0.7042f, 0.6386f, 0.5870f,
            0.5422f, 0.4999f, 0.4560f, 0.4113f, 0.3314f, 0.2935f, 0.2429f, 0.0000f,
        };

        // The cutoff that admits nothing. Named because the radial dissolve interpolates TOWARD it.
        const float EmptyCutoff = 1.01f;

        const int CloudSalt = 9311;
        // Density span over which a mass climbs from its thinnest rim to full thickness.
        const float EdgeFadeDensityRange = 0.14f;
        const float EdgeMinThicknessFraction = 0.22f;

        // How much of full thickness survives at the radial rim. Thickness is what makes a deck
        // read as heavier than its cell count: a 5 m slab seen at 12 degrees of elevation presents
        // its SIDE, so a cell's silhouette is several times its footprint and the sky near the
        // horizon looks solid at coverages that look sparse overhead. Thinning with radius is the
        // direct counter.
        const float RimThicknessFraction = 0.3f;

        Mesh mesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Transform follow;
        int worldSeed;
        float driftMeters;
        int builtCellX = int.MinValue;
        int builtCellZ = int.MinValue;
        float builtCoverage = -1.0f;
        bool hasRebuildAnchor;
        Vector2 rebuildAnchor;

        readonly List<Vector3> vertices = new();
        readonly List<Vector3> normals = new();
        readonly List<int> triangles = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> colors = new();

        // Per-vertex inputs to the colour, kept so a sky-colour change can rewrite the colour
        // array WITHOUT rebuilding geometry.
        //
        // This is load-bearing now that the sky variant is unlit. On the lit path the sun and the
        // ambient did the time-of-day work at render time; unlit, the baked vertex colour is the
        // deck's ONLY colour, and Rebuild is the only thing that used to write it. Drift alone
        // forces a rebuild every CellMeters/DriftMetersPerSecond = 6.25 s, so the deck advanced
        // through dawn and dusk in six-second steps.
        readonly List<float> vertexFade = new();
        readonly List<bool> vertexIsTop = new();

        // One density sample per grid cell plus a one-cell apron, so the side-face neighbour tests
        // read the cache instead of recomputing. The apron is what makes the boundary cells' tests
        // valid without a bounds check in the inner loop.
        const int DensityStride = GridCells + 2;
        readonly float[] densityCache = new float[DensityStride * DensityStride];

        Rect tileRect;
        // Seeded with a plausible midday sky rather than white. Rebuild paints from these, and a
        // deck built before any sky update reaches it — ApplySky returns early when the lighting
        // controller does not own the sky material instance, and outside play mode entirely — would
        // otherwise render pure white under the unlit sky shader. White CLOUDS are fine; a white
        // RIM is a bright ring exactly where the deck is supposed to disappear.
        Color topColor = new(0.96f, 0.96f, 0.97f, 1.0f);
        Color sideColor = new(0.67f, 0.70f, 0.74f, 1.0f);
        Color horizonColor = new(0.66f, 0.78f, 0.92f, 1.0f);
        bool hasAppliedSky;
        float coverage;

        public int BuiltQuadCount { get; private set; }

        /// <summary>How many times the geometry has been rebuilt. Public so the rebuild TRIGGER
        /// can be pinned: the failure it guards is not a wrong picture but a full deck rebuild per
        /// frame, which no assertion about the mesh's contents can see.</summary>
        public int RebuildCount { get; private set; }

        /// <summary>How many times the head has moved far enough to re-resolve which cell the deck
        /// sits over. This, not RebuildCount, is what the dead zone actually guards: a rebuild has a
        /// second and entirely legitimate cause (wind drift crossing a cell), so counting rebuilds
        /// cannot tell jitter apart from weather.</summary>
        public int CellResolveCount { get; private set; }

        /// <summary>Builds the deck at a chosen origin. Test seam: Rebuild is otherwise reachable
        /// only from LateUpdate, which EditMode never runs.</summary>
        public void RebuildAt(int originCellX, int originCellZ)
        {
            builtCellX = originCellX;
            builtCellZ = originCellZ;
            builtCoverage = coverage;
            Rebuild(originCellX, originCellZ);
        }

        /// <summary>Runs one frame of follow/rebuild logic. Same seam, for the trigger itself.</summary>
        public void TickForTests() => LateUpdate();

        public void Configure(Transform followTransform, Material deckMaterial, Rect cloudTileRect, int seed)
        {
            follow = followTransform;
            worldSeed = seed;
            tileRect = cloudTileRect;

            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();

            if (deckMaterial != null)
                meshRenderer.sharedMaterial = deckMaterial;

            // A deck this far up casting into the world would cost a full shadow sweep for a
            // shadow nothing can see the edge of.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (mesh == null)
            {
                mesh = new Mesh { name = "Blockiverse Cloud Deck" };
                // 32-bit indices. A closed sky over the enlarged grid is around 9,000 quads, i.e.
                // ~36,000 vertices — comfortably under the 16-bit limit at the coverages that
                // actually occur, but the failure mode if a future grid or coverage curve crosses
                // 65,535 is a hard runtime throw in Rebuild rather than anything gradual.
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.MarkDynamic();
            }

            meshFilter.sharedMesh = mesh;
        }

        /// <summary>Coverage is the weather service's 0..1, remapped by <see cref="DeckCoverage"/>.
        /// Colours come from the sky solver so the deck and the skybox agree at dawn and dusk, when
        /// a mismatch is most visible; <paramref name="deckHorizon"/> is what the rim dissolves
        /// into and must be the colour of the sky BEHIND the rim, not another cloud tone.</summary>
        public void SetSky(float cloudCoverage, Color deckTop, Color deckSide, Color deckHorizon)
        {
            float wanted = DeckCoverage(cloudCoverage);

            // Stored and written to the vertex stream in sRGB, exactly as authored.
            //
            // The project renders in LINEAR colour space, and the two routes a colour can take into
            // a shader disagree there: Material.SetColor converts sRGB to linear, mesh.colors does
            // not convert at all. The conversion therefore has to happen somewhere, and the SHADER
            // is the right somewhere — mesh colours are an 8-bit stream, and 8 bits of *linear*
            // quantises the darks badly (a night aerial colour of 0.0057 linear rounds to 1/255,
            // a 30% error), while 8 bits of sRGB is precisely what sRGB encoding is for. See the
            // _BLOCKIVERSE_SKY branch in BlockiverseVoxelLit.shader.
            //
            // That vertex colours pass through unconverted is not an assumption: this project's own
            // lit path packs sky exposure and emitter reach into them as raw 0..1 SCALARS and
            // multiplies them linearly, which would be systematically wrong if Unity converted.
            bool colorsChanged = !hasAppliedSky
                || deckTop != topColor || deckSide != sideColor || deckHorizon != horizonColor;

            coverage = wanted;
            topColor = deckTop;
            sideColor = deckSide;
            horizonColor = deckHorizon;
            hasAppliedSky = true;

            // Geometry is unchanged by a colour change, so repaint rather than rebuild.
            if (colorsChanged)
                ApplyVertexColors();
        }

        /// <summary>
        /// Maps the weather service's cloud coverage onto the fraction of deck cells to fill.
        /// </summary>
        /// <remarks>
        /// NOT the identity, and the difference is not cosmetic tuning. Two effects mean a deck
        /// filled to fraction f reads as considerably more than f of the sky:
        ///
        /// - THICKNESS AT GRAZING ANGLES. Cells are 10 m across and 5 m deep. Overhead you see
        ///   only their footprint; out at 15 degrees of elevation you see the side too, so the
        ///   silhouette is roughly (1 + 0.5*cot(theta)) times the footprint — about 2.9x at 15
        ///   degrees. Most of the sky's SOLID ANGLE is near the horizon (the share above elevation
        ///   theta is 1 - sin theta), so this dominates.
        /// - THE SKYBOX VEIL. The same coverage also drives a translucent veil across the whole
        ///   hemisphere. The deck is not the only thing carrying the weather.
        ///
        /// So the raw values (Clear 0.10, PartlyCloudy 0.45) were producing a sky that read
        /// overcast at Clear and solid by PartlyCloudy: "clear skies still have too much cloud
        /// coverage and it quickly escalates from there". The exponent pulls the low and middle of
        /// the range down while leaving 1.0 pinned, so a thunderstorm still closes the sky.
        ///
        ///   Clear 0.10 -> 0.018    PartlyCloudy 0.45 -> 0.25
        ///   Overcast 0.80 -> 0.68  Thunderstorm 1.00 -> 1.00
        /// </remarks>
        public static float DeckCoverage(float weatherCoverage) =>
            Mathf.Pow(Mathf.Clamp01(weatherCoverage), 1.75f);

        void LateUpdate()
        {
            if (meshFilter == null || follow == null)
                return;

            driftMeters += DriftMetersPerSecond * Time.deltaTime;

            // The deck sits over the player and slides with the wind. Position carries the
            // sub-cell remainder so motion is smooth; the mesh only rebuilds when the drift
            // crosses a whole cell, which is once every few seconds rather than every frame.
            float driftCells = driftMeters / CellMeters;
            int driftWhole = Mathf.FloorToInt(driftCells);
            float driftRemainder = (driftCells - driftWhole) * CellMeters;

            Vector2 head = new(follow.position.x, follow.position.z);

            // Hysteresis, not the raw cell index — see RebuildDeadZoneMeters. The anchor is the
            // head position the current cell was resolved at, so tracking jitter can never move
            // it and only real locomotion does.
            if (!hasRebuildAnchor || Vector2.Distance(head, rebuildAnchor) >= RebuildDeadZoneMeters)
            {
                rebuildAnchor = head;
                hasRebuildAnchor = true;
                CellResolveCount++;
                resolvedCellX = Mathf.FloorToInt(head.x / CellMeters);
                resolvedCellZ = Mathf.FloorToInt(head.y / CellMeters);
            }

            transform.position = new Vector3(
                resolvedCellX * CellMeters + driftRemainder,
                AltitudeMeters,
                resolvedCellZ * CellMeters);

            bool coverageChanged = !Mathf.Approximately(coverage, builtCoverage);

            if (resolvedCellX - driftWhole != builtCellX || resolvedCellZ != builtCellZ || coverageChanged)
            {
                builtCellX = resolvedCellX - driftWhole;
                builtCellZ = resolvedCellZ;
                builtCoverage = coverage;
                Rebuild(builtCellX, builtCellZ);
            }
        }

        int resolvedCellX;
        int resolvedCellZ;

        /// <summary>Whether a cloud cell exists at an absolute grid coordinate, ignoring the radial
        /// dissolve — this is the raw coverage-to-occupancy mapping, and <paramref
        /// name="cloudCoverage"/> here is a DECK coverage, not a weather coverage.</summary>
        public bool IsCloudCell(int cellX, int cellZ, float cloudCoverage)
        {
            return CloudDensity(cellX, cellZ) > CoverageCutoff(cloudCoverage);
        }

        /// <summary>Smooth cloud density at a cell, in roughly [0,1]. Two octaves of INTERPOLATED
        /// value noise: interpolation is what makes masses with soft boundaries instead of the
        /// hard-edged blobs a box filter produces, and the second octave is what breaks a single
        /// continuous sheet into separate clouds.</summary>
        public float CloudDensity(int cellX, int cellZ)
        {
            return 0.62f * ValueNoise(cellX / 3.0f, cellZ / 3.0f)
                 + 0.38f * ValueNoise(cellX / 1.3f + 41.3f, cellZ / 1.3f + 17.7f);
        }

        /// <summary>Density threshold for a requested coverage, interpolated from the measured
        /// quantile table.</summary>
        public static float CoverageCutoff(float cloudCoverage)
        {
            float c = Mathf.Clamp01(cloudCoverage);

            for (int i = 1; i < CoverageStops.Length; i++)
            {
                if (c > CoverageStops[i])
                    continue;

                float span = CoverageStops[i] - CoverageStops[i - 1];
                float t = span <= 0.0f ? 0.0f : (c - CoverageStops[i - 1]) / span;
                return Mathf.Lerp(CoverageCutoffs[i - 1], CoverageCutoffs[i], t);
            }

            return CoverageCutoffs[CoverageCutoffs.Length - 1];
        }

        /// <summary>How far through the rim dissolve a cell at this grid offset sits: 0 well
        /// inside the deck, 1 at or beyond the radius. Both the coverage falloff and the colour
        /// crossfade run on this one value so they cannot disagree about where the rim is.</summary>
        public static float RimFade(int offsetX, int offsetZ)
        {
            float radius = Mathf.Sqrt(offsetX * (float)offsetX + offsetZ * (float)offsetZ);
            return Mathf.InverseLerp(RimFadeStartFraction * RadiusCells, RadiusCells, radius);
        }

        float ValueNoise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x);
            int iz = Mathf.FloorToInt(z);
            float fx = Smooth(x - ix);
            float fz = Smooth(z - iz);

            float a = CellHash(ix, iz);
            float b = CellHash(ix + 1, iz);
            float c = CellHash(ix, iz + 1);
            float d = CellHash(ix + 1, iz + 1);

            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        static float Smooth(float t) => t * t * (3.0f - 2.0f * t);

        float CellHash(int x, int z) =>
            DeterministicHash.Hash(worldSeed, x, 0, z, CloudSalt) / (float)uint.MaxValue;

        void Rebuild(int originCellX, int originCellZ)
        {
            RebuildCount++;
            vertices.Clear();
            normals.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();
            vertexFade.Clear();
            vertexIsTop.Clear();

            int half = GridCells / 2;
            float baseCutoff = CoverageCutoff(coverage);

            // ONE density evaluation per cell, cached. The occupancy test runs five times per cell
            // (itself plus four neighbours for the side faces) and each evaluation is two octaves
            // of interpolated noise, i.e. eight hashes — so computing it inline cost forty hashes
            // per cell. Caching is what made enlarging the grid from 56 to 92 cells across
            // CHEAPER than the square it replaced rather than three times the work.
            for (int gz = -half - 1; gz <= half; gz++)
            {
                for (int gx = -half - 1; gx <= half; gx++)
                    densityCache[DensityIndex(gx, gz, half)] =
                        CloudDensity(originCellX + gx, originCellZ + gz);
            }

            for (int gz = -half; gz < half; gz++)
            {
                for (int gx = -half; gx < half; gx++)
                {
                    float fade = RimFade(gx, gz);
                    if (fade >= 1.0f)
                        continue;

                    float y1 = CellTop(gx, gz, half, baseCutoff);
                    if (y1 <= 0.0f)
                        continue;

                    float x0 = gx * CellMeters;
                    float z0 = gz * CellMeters;
                    float x1 = x0 + CellMeters;
                    float z1 = z0 + CellMeters;
                    const float y0 = 0.0f;

                    // Underside first: it is the face a player on the ground actually sees.
                    AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                            new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, fade, isTop: false);
                    AddQuad(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1),
                            new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.up, fade, isTop: true);

                    // Sides are emitted from the NEIGHBOUR'S TOP up to this cell's top, not on a
                    // yes/no test of whether the neighbour exists.
                    //
                    // Thickness varies per cell — with the density margin taper and again with the
                    // radial one — so two adjacent filled cells routinely differ in height, and a
                    // binary test emits nothing between them. The wall from the shorter cell's top
                    // to the taller one's is then simply missing, and because the far side is
                    // back-face culled you see straight through the mass. The rim taper made this
                    // much worse (rim cells drop to 30% thickness) and put the holes exactly where
                    // the deck is most edge-on.
                    //
                    // An empty neighbour is CellTop 0, i.e. the full-height case falls out of the
                    // same expression rather than being a separate branch.
                    EmitSide(gx, gz - 1, half, baseCutoff, y1, fade,
                        new Vector3(x0, 0.0f, z0), new Vector3(x1, 0.0f, z0), Vector3.back);
                    EmitSide(gx, gz + 1, half, baseCutoff, y1, fade,
                        new Vector3(x1, 0.0f, z1), new Vector3(x0, 0.0f, z1), Vector3.forward);
                    EmitSide(gx - 1, gz, half, baseCutoff, y1, fade,
                        new Vector3(x0, 0.0f, z1), new Vector3(x0, 0.0f, z0), Vector3.left);
                    EmitSide(gx + 1, gz, half, baseCutoff, y1, fade,
                        new Vector3(x1, 0.0f, z0), new Vector3(x1, 0.0f, z1), Vector3.right);
                }
            }

            BuiltQuadCount = triangles.Count / 6;

            mesh.Clear();
            if (vertices.Count == 0)
                return;

            mesh.SetVertices(vertices);
            // Written directly, never RecalculateNormals: every face here is axis-aligned so its
            // normal is known at emission, and the alternative was a full normal solve over ~38,000
            // vertices on every rebuild for a shader path that returns before reading one.
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            ApplyVertexColors();
            mesh.RecalculateBounds();
        }

        // Emits the side face between this cell and a neighbour, spanning the neighbour's top up
        // to this cell's top. Nothing is emitted when the neighbour is at least as tall.
        void EmitSide(
            int neighbourX, int neighbourZ, int half, float baseCutoff,
            float y1, float fade, Vector3 a, Vector3 b, Vector3 normal)
        {
            float neighbourTop = CellTop(neighbourX, neighbourZ, half, baseCutoff);
            if (neighbourTop >= y1)
                return;

            AddQuad(
                new Vector3(a.x, neighbourTop, a.z),
                new Vector3(a.x, y1, a.z),
                new Vector3(b.x, y1, b.z),
                new Vector3(b.x, neighbourTop, b.z),
                normal, fade, isTop: false);
        }

        /// <summary>The top of a cell in the deck's local space, or 0 when the cell is empty.</summary>
        float CellTop(int offsetX, int offsetZ, int half, float baseCutoff)
        {
            float fade = RimFade(offsetX, offsetZ);
            if (fade >= 1.0f)
                return 0.0f;

            // Coverage falls to nothing across the rim band by walking the threshold up to the one
            // that admits no cell at all. Doing it on the CUTOFF rather than on the coverage keeps
            // the quantile table as the single mapping.
            float cutoff = Mathf.Lerp(baseCutoff, EmptyCutoff, fade);
            float density = Density(offsetX, offsetZ, half);

            if (density <= cutoff)
                return 0.0f;

            // Edge fade, done with geometry because the deck is opaque and has no alpha to fade
            // with. A cell barely over the threshold gets a thin slab and a deep-interior cell gets
            // full thickness, so a mass tapers at its rim instead of ending on a full-height wall.
            // Radial distance thins it again so the deck does not present a wall of sides at the
            // horizon.
            float margin = Mathf.Clamp01((density - cutoff) / EdgeFadeDensityRange);
            return ThicknessMeters
                * Mathf.Lerp(EdgeMinThicknessFraction, 1.0f, margin)
                * Mathf.Lerp(1.0f, RimThicknessFraction, fade);
        }

        static int DensityIndex(int offset, int half) => offset + half + 1;

        static int DensityIndex(int offsetX, int offsetZ, int half) =>
            DensityIndex(offsetX, half) + DensityStride * DensityIndex(offsetZ, half);

        float Density(int offsetX, int offsetZ, int half) =>
            densityCache[DensityIndex(offsetX, offsetZ, half)];

        // Repaints without touching geometry. Toward the rim every face crosses to the colour of
        // the sky behind it, which is the whole trick for "the clouds go on forever" on an opaque
        // material: the far cells are still drawn, they simply stop being distinguishable from the
        // sky, so there is no boundary left to see.
        void ApplyVertexColors()
        {
            if (mesh == null || vertexFade.Count == 0)
                return;

            colors.Clear();

            for (int i = 0; i < vertexFade.Count; i++)
            {
                Color baseColor = vertexIsTop[i] ? topColor : sideColor;
                colors.Add(Color.Lerp(baseColor, horizonColor, vertexFade[i]));
            }

            mesh.SetColors(colors);
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float fade, bool isTop)
        {
            int start = vertices.Count;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                uvs.Add(new Vector2(tileRect.xMin, tileRect.yMin));
                vertexFade.Add(fade);
                vertexIsTop.Add(isTop);
            }

            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        // The mesh is minted here, so it is destroyed here. Nothing else holds a handle on it, and
        // it is the largest single allocation this object owns.
        void OnDestroy()
        {
            if (mesh == null)
                return;

            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);

            mesh = null;
        }
    }
}
