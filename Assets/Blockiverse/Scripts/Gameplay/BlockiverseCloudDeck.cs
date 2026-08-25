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
    /// of everything below it. The two layers divide the work: the skybox keeps the high veil and
    /// the full hemisphere down to the horizon, where geometry would need an absurd extent.
    ///
    /// THREE constraints shaped this, and each is easy to get wrong:
    ///
    /// 1. RENDER-ONLY. Clouds are never voxels: no BlockRegistry entry, no save-format presence,
    ///    no collider. They are a pure function of (seed, clock), which also means every peer
    ///    computes the same sky with nothing on the wire — consistent with the lockstep world sim.
    ///
    /// 2. NO NEW SHADER. GraphicsSettings' always-included list carries the voxel shader alone, so
    ///    a cloud shader reached through Shader.Find would be stripped from the Android player and
    ///    render magenta on device while looking correct in the editor. The deck therefore borrows
    ///    the atlas material it is handed. That also buys fog for free, which is what fades the
    ///    deck's finite edge into the sky instead of ending it on a hard line.
    ///
    /// 3. ABOVE THE BUILD LIMIT. At WorldMaxY 127 a deck inside world bounds is reachable, which
    ///    means handling the camera being inside a cloud. Sitting above everything buildable skips
    ///    that case entirely; flythrough is a deliberate follow-up, not an oversight.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlockiverseCloudDeck : MonoBehaviour
    {
        public const float CellMeters = 12.0f;
        // 24 cells is 288 m across — comfortably past where individual cells resolve, and small
        // enough that a full rebuild stays trivial.
        public const int GridCells = 24;
        public const float AltitudeMeters = 160.0f;
        public const float ThicknessMeters = 5.0f;
        public const float DriftMetersPerSecond = 1.6f;

        // Coverage is remapped into this band rather than used raw: at 0 the sky should be empty,
        // but a linear map leaves stray single cells at very low coverage, which read as debris.
        public const float MinCoverageThreshold = 0.12f;
        public const float MaxCoverageThreshold = 0.82f;

        const int CloudSalt = 9311;

        Mesh mesh;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Transform follow;
        int worldSeed;
        float driftMeters;
        int builtCellX = int.MinValue;
        int builtCellZ = int.MinValue;
        float builtCoverage = -1.0f;

        readonly List<Vector3> vertices = new();
        readonly List<int> triangles = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> colors = new();

        Rect tileRect;
        Color topColor = Color.white;
        Color sideColor = Color.white;
        float coverage;

        public int BuiltQuadCount { get; private set; }

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
                mesh.MarkDynamic();
            }

            meshFilter.sharedMesh = mesh;
        }

        /// <summary>Coverage is the weather service's 0..1. Colours come from the sky solver so the
        /// deck and the skybox agree at dawn and dusk, when a mismatch is most visible.</summary>
        public void SetSky(float cloudCoverage, Color deckTop, Color deckSide)
        {
            coverage = Mathf.Clamp01(cloudCoverage);
            topColor = deckTop;
            sideColor = deckSide;
        }

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

            int centreCellX = Mathf.FloorToInt(follow.position.x / CellMeters);
            int centreCellZ = Mathf.FloorToInt(follow.position.z / CellMeters);

            transform.position = new Vector3(
                centreCellX * CellMeters + driftRemainder,
                AltitudeMeters,
                centreCellZ * CellMeters);

            bool coverageChanged = !Mathf.Approximately(coverage, builtCoverage);

            if (centreCellX - driftWhole != builtCellX || centreCellZ != builtCellZ || coverageChanged)
            {
                builtCellX = centreCellX - driftWhole;
                builtCellZ = centreCellZ;
                builtCoverage = coverage;
                Rebuild(builtCellX, builtCellZ);
            }
        }

        /// <summary>Whether a cloud cell exists at an absolute grid coordinate.
        ///
        /// Smoothed over the 3x3 neighbourhood rather than thresholding a raw hash: a raw hash is
        /// salt-and-pepper, which reads as static, not weather. Averaging first makes the field
        /// spatially coherent so cells clump into masses with ragged edges.</summary>
        public bool IsCloudCell(int cellX, int cellZ, float cloudCoverage)
        {
            if (cloudCoverage <= 0.001f)
                return false;

            float total = 0.0f;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                    total += DeterministicHash.Hash(worldSeed, cellX + dx, 0, cellZ + dz, CloudSalt) / (float)uint.MaxValue;
            }

            float smoothed = total / 9.0f;
            float threshold = Mathf.Lerp(MinCoverageThreshold, MaxCoverageThreshold, Mathf.Clamp01(cloudCoverage));

            // The smoothed field concentrates around 0.5, so compare against a band centred there
            // rather than against the raw value — otherwise coverage barely moves the result.
            return smoothed > 0.5f - (threshold * 0.5f) && smoothed < 0.5f + (threshold * 0.5f);
        }

        void Rebuild(int originCellX, int originCellZ)
        {
            vertices.Clear();
            triangles.Clear();
            uvs.Clear();
            colors.Clear();

            int half = GridCells / 2;

            for (int gz = -half; gz < half; gz++)
            {
                for (int gx = -half; gx < half; gx++)
                {
                    int cellX = originCellX + gx;
                    int cellZ = originCellZ + gz;

                    if (!IsCloudCell(cellX, cellZ, coverage))
                        continue;

                    float x0 = gx * CellMeters;
                    float z0 = gz * CellMeters;
                    float x1 = x0 + CellMeters;
                    float z1 = z0 + CellMeters;
                    float y0 = 0.0f;
                    float y1 = ThicknessMeters;

                    // Underside first: it is the face a player on the ground actually sees.
                    AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                            new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), sideColor);
                    AddQuad(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1),
                            new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), topColor);

                    // Sides only where the neighbour is empty — the interior of a cloud mass is
                    // never visible, and emitting it would multiply the quad count for nothing.
                    if (!IsCloudCell(cellX, cellZ - 1, coverage))
                        AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0),
                                new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), sideColor);

                    if (!IsCloudCell(cellX, cellZ + 1, coverage))
                        AddQuad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1),
                                new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), sideColor);

                    if (!IsCloudCell(cellX - 1, cellZ, coverage))
                        AddQuad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1),
                                new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), sideColor);

                    if (!IsCloudCell(cellX + 1, cellZ, coverage))
                        AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0),
                                new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), sideColor);
                }
            }

            BuiltQuadCount = triangles.Count / 6;

            mesh.Clear();
            if (vertices.Count == 0)
                return;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int start = vertices.Count;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            uvs.Add(new Vector2(tileRect.xMin, tileRect.yMin));
            uvs.Add(new Vector2(tileRect.xMin, tileRect.yMax));
            uvs.Add(new Vector2(tileRect.xMax, tileRect.yMax));
            uvs.Add(new Vector2(tileRect.xMax, tileRect.yMin));

            for (int i = 0; i < 4; i++)
                colors.Add(color);

            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }
}
