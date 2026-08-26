using System.Collections.Generic;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    /// <summary>
    /// A sea-level plane surrounding the world, so the map ends in an horizon instead of in a cliff.
    ///
    /// The world is a FIXED 128 x 128 blocks — 128 metres square. That is not a streaming radius
    /// with more terrain waiting beyond it; it is the whole world, and its outer columns are real
    /// rendered faces with nothing behind them. From any elevation you were therefore looking at a
    /// square cliff standing in the skybox's flat grey below-horizon band: "if you go high enough
    /// you can clearly see the edge of the game world."
    ///
    /// This does not extend the world. Nothing here is a voxel, is saved, is collidable, or is
    /// simulated — it is a lid over the void, and the island it leaves behind reads as an island in
    /// an open sea, which is a coherent thing to be rather than a truncation.
    ///
    /// HOW THE RIM HIDES ITSELF. The plane is finite too, so it has the same problem one ring out.
    /// It is solved by colour rather than by size: every vertex crossfades to the AERIAL colour —
    /// the colour the sky itself takes at the horizon, which is also what
    /// <see cref="BlockiverseLightingCycleController"/> now uses for fog and for the skybox's
    /// below-horizon band. Three surfaces that used to hold three independent opinions about what
    /// "infinitely far away" looks like now agree on one, so the seams between them have no colour
    /// difference left to draw.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseHorizonSkirt : MonoBehaviour
    {
        // How far past the world's edge the plane reaches — DERIVED FROM THE WORLD, not a constant.
        //
        // Bounded by the camera's 500 m far clip, and not by taste: a triangle crossing the far
        // plane is clipped, which would draw a hard arc at exactly 500 m sweeping around as the
        // player turns, far worse than the edge it replaces. The worst case is a player standing
        // in one CORNER of the world looking at the opposite corner of the plane, which is
        // sqrt((W+m)^2 + (D+m)^2) — not the world diagonal plus the margin, which is what a first
        // pass here assumed and what its own test caught: 360 m put the far corner at 541 m.
        //
        // It has to scale because the world does. Medium worlds are 192 blocks square
        // (WorldSaveGeneration.SizeFor), where a margin sized for a 128 world overruns the clip.
        public const float MaxOuterMarginMeters = 220.0f;
        // Slack under the 500 m far clip (BlockiverseProjectBootstrapper.XrRig.cs), for the head's
        // offset from the rig origin and for not sitting exactly on the boundary.
        public const float MaxFarRimMeters = 470.0f;

        /// <summary>How far past <paramref name="bounds"/> the plane may reach.</summary>
        public static float OuterMarginFor(WorldBounds bounds)
        {
            // Conservative for non-square worlds: sqrt((W+m)^2 + (D+m)^2) <= (max(W,D)+m)*sqrt(2),
            // so solving on the larger dimension is safe for both.
            float longestSide = Mathf.Max(bounds.Width, bounds.Depth);
            float budget = MaxFarRimMeters / Mathf.Sqrt(2.0f) - longestSide;
            return Mathf.Clamp(budget, 0.0f, MaxOuterMarginMeters);
        }

        // Rings out from the world edge, and samples per side of the rectangle. The plane is flat
        // and its colour gradient is smooth, so this only has to be dense enough that the gradient
        // does not band — there is no silhouette detail to resolve.
        public const int RingCount = 10;
        public const int SamplesPerSide = 20;

        // Ring i sits at the margin * (i/RingCount)^RingBias metres out. Above 1 this packs rings
        // toward the world, which is where the colour is changing fastest and where the player is
        // close enough to see banding.
        const float RingBias = 2.0f;

        // The near tone, as a multiplier on the aerial colour rather than an absolute colour, so
        // it tracks dusk, night and overcast for free instead of glowing at midnight.
        public static readonly Color SeaShade = new(0.70f, 0.79f, 0.90f, 1.0f);

        Mesh mesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;

        readonly List<Vector3> vertices = new();
        readonly List<int> triangles = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> colors = new();
        // Per-vertex position along the fade, cached at build so a colour change is one pass over
        // an array rather than a rebuild of the geometry.
        float[] fadeByVertex = System.Array.Empty<float>();
        // Seeded with a plausible daytime horizon rather than default-black or white, so a plane
        // built before any sky update is merely slightly wrong rather than glaring.
        Color appliedAerial = new(0.66f, 0.78f, 0.92f, 1.0f);
        bool hasAppliedSky;

        public int BuiltQuadCount { get; private set; }

        /// <summary>
        /// Whether a world should have one at all.
        /// </summary>
        /// <remarks>
        /// The plane is the SEA, and it sits at sea level because that is where the ocean's own
        /// surface is. A world shorter than sea level has no ocean for it to continue: builder
        /// canvases are 64 blocks tall against a sea level of 64
        /// (`WorldSaveGeneration.BuilderWorldHeight`), so a plane at 64 would hang above the whole
        /// world instead of meeting anything.
        /// </remarks>
        public static bool SuitsWorld(WorldBounds bounds) => bounds.Height > WorldConstants.SeaLevel;

        /// <summary>Builds the plane around <paramref name="bounds"/>. Idempotent.</summary>
        public void Configure(WorldBounds bounds, Material skirtMaterial, Rect tileRect)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();

            if (skirtMaterial != null)
                meshRenderer.sharedMaterial = skirtMaterial;

            // It has no thickness and sits below everything; a shadow from it could only ever be
            // its own, and receiving one would band the gradient it exists to keep smooth.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (mesh == null)
            {
                mesh = new Mesh { name = "Blockiverse Horizon Skirt" };
                mesh.MarkDynamic();
            }

            meshFilter.sharedMesh = mesh;
            Rebuild(bounds, tileRect);
        }

        /// <summary>
        /// Recolours the plane. <paramref name="aerial"/> is what everything infinitely far away
        /// looks like — the same colour driving fog and the skybox's below-horizon band.
        /// </summary>
        public void SetSky(Color aerial)
        {
            if (mesh == null || fadeByVertex.Length == 0)
                return;

            // Written to the vertex stream in sRGB, as authored; the sky shader variant converts.
            // Doing it here instead would quantise the darks, because mesh colours are 8-bit and
            // 8 bits of linear is not enough for a night sky. See BlockiverseVoxelLit.shader.
            //
            // ApplySky calls this every frame; the colours change only with the clock and the
            // weather, and rebuilding 880 vertex colours per frame for an unchanged value is pure
            // waste.
            if (hasAppliedSky && aerial == appliedAerial)
                return;

            appliedAerial = aerial;
            hasAppliedSky = true;
            ApplyVertexColors();
        }

        /// <summary>
        /// The ONE writer of this mesh's vertex colours.
        /// </summary>
        /// <remarks>
        /// Rebuild used to fill white placeholders for SetSky to overwrite, which is safe only
        /// while SetSky reliably follows — and it does not. `BlockiverseLightingCycleController`
        /// returns from ApplySky before touching the sky when it does not own the sky material
        /// instance, and outside play mode entirely, so the plane could be left holding the
        /// placeholder. Under the unlit sky shader that is not a subtle mis-tint: it is a blazing
        /// white plane across the whole horizon.
        ///
        /// One writer and a sane starting <see cref="appliedAerial"/> make that unreachable rather
        /// than unlikely. Same defect shape as the canopy fast path that returned an unfloored
        /// value — a second path that skips the step the first one takes.
        /// </remarks>
        void ApplyVertexColors()
        {
            if (mesh == null || fadeByVertex.Length == 0)
                return;

            Color near = new(
                appliedAerial.r * SeaShade.r,
                appliedAerial.g * SeaShade.g,
                appliedAerial.b * SeaShade.b,
                1.0f);

            colors.Clear();
            for (int i = 0; i < fadeByVertex.Length; i++)
                colors.Add(Color.Lerp(near, appliedAerial, fadeByVertex[i]));

            mesh.SetColors(colors);
        }

        void Rebuild(WorldBounds bounds, Rect tileRect)
        {
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();

            int perimeter = SamplesPerSide * 4;
            // Exactly the ocean's own RENDERED surface, which is not quite sea level. Two facts
            // compose, and taking only the first leaves this plane 2.5 cm PROUD of the sea it is
            // continuing: SurvivalTerrainPreset fills water up to but NOT including SeaLevel (so
            // the topmost water block's top face is the plane y = SeaLevel), and
            // BlockiverseVoxelLitInput.hlsl then levels every fluid family's mean surface to
            // -MaxWaveDipMeters * 0.5.
            //
            // Sub-pixel at the 64 m the world's edge sits at — and exactly the defect the wave
            // shader's own comment records being caught on device, where brine sitting 5 mm proud
            // of freshwater read as one body being thicker than the other.
            float y = WorldConstants.SeaLevel - VoxelWorldRenderer.MaxWaveDipMeters * 0.5f;
            float margin = OuterMarginFor(bounds);
            var fades = new List<float>();

            for (int ring = 0; ring <= RingCount; ring++)
            {
                float t = ring / (float)RingCount;
                float expansion = margin * Mathf.Pow(t, RingBias);

                // Fade is stored against DISTANCE, not against the ring index. Rings are packed
                // toward the world (RingBias), so an index-driven fade runs most of its length in
                // the first few metres: with the packing this file ships, ring 5 sits 53 m out
                // and would already be more sky than sea. Distance-linear also interpolates
                // exactly across a quad, so the gradient cannot band however the rings are spaced.
                float fade = margin > 0.0f ? Mathf.Clamp01(expansion / margin) : 1.0f;

                for (int k = 0; k < perimeter; k++)
                {
                    Vector2 point = PerimeterPoint(bounds, expansion, k);
                    vertices.Add(new Vector3(point.x, y, point.y));
                    uvs.Add(new Vector2(tileRect.xMin, tileRect.yMin));
                    fades.Add(fade);
                }
            }

            for (int ring = 0; ring < RingCount; ring++)
            {
                int inner = ring * perimeter;
                int outer = (ring + 1) * perimeter;

                for (int k = 0; k < perimeter; k++)
                {
                    int next = (k + 1) % perimeter;

                    // Wound so the plane faces UP. The perimeter walk is counter-clockwise seen
                    // from above and Unity front-faces are clockwise from the front, which is why
                    // the inner pair comes first and the outer pair reversed.
                    triangles.Add(inner + k);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);

                    triangles.Add(inner + k);
                    triangles.Add(outer + next);
                    triangles.Add(outer + k);
                }
            }

            fadeByVertex = fades.ToArray();
            BuiltQuadCount = triangles.Count / 6;

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            // Through the same single writer the sky updates go through, so a plane that is never
            // handed a sky colour still renders as a plausible one instead of as white.
            ApplyVertexColors();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // The mesh is minted here, so it is destroyed here. Nothing else holds a handle on it.
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

        /// <summary>
        /// Point <paramref name="index"/> of a closed rectangular loop around the world, pushed
        /// <paramref name="expansion"/> metres outward. Every ring uses the same parameterisation,
        /// which is what lets consecutive rings be stitched index to index.
        /// </summary>
        public static Vector2 PerimeterPoint(WorldBounds bounds, float expansion, int index)
        {
            float x0 = -expansion;
            float z0 = -expansion;
            float x1 = bounds.Width + expansion;
            float z1 = bounds.Depth + expansion;

            int perimeter = SamplesPerSide * 4;
            index = ((index % perimeter) + perimeter) % perimeter;

            int side = index / SamplesPerSide;
            float t = (index % SamplesPerSide) / (float)SamplesPerSide;

            return side switch
            {
                0 => new Vector2(Mathf.Lerp(x0, x1, t), z0),
                1 => new Vector2(x1, Mathf.Lerp(z0, z1, t)),
                2 => new Vector2(Mathf.Lerp(x1, x0, t), z1),
                _ => new Vector2(x0, Mathf.Lerp(z1, z0, t)),
            };
        }
    }
}
