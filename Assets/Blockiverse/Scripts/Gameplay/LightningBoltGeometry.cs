using System;
using UnityEngine;

namespace Blockiverse.Gameplay
{
    // Builds the lightning bolt as geometry rather than a sprite.
    //
    // A stretched 32x32 point-filtered tile visibly stair-steps over tens of blocks, and a single
    // tall billboard reads unmistakably as a flat card in stereo -- it swims as the head moves,
    // which is fatal for the one effect in the game the player is meant to stop and look at. A
    // ribbon is also the cheaper option on a tile GPU: a 0.76-block-wide strip covers a fraction
    // of the transparent fill a 4-block-wide quad would.
    //
    // Everything here is pure and allocation-free into caller-owned buffers, so the shape can be
    // unit-tested and the view can rebuild a bolt per strike without producing garbage.
    public static class LightningBoltGeometry
    {
        // Segments along the main channel. Enough to read as a jagged path; few enough that the
        // whole bolt including forks stays a few hundred triangles.
        public const int DefaultSegments = 12;
        public const int ReducedSegments = 8;

        // Lateral wander as a fraction of the bolt's height. The first comparison render used
        // 4.5% with a smooth oscillation and read as bent wire; 11% with a random walk is what was
        // actually accepted.
        public const float LateralWanderFraction = 0.11f;

        public const int ForkCount = 3;

        // Half-width in blocks, from the accepted render.
        public const float HalfWidthBlocks = 0.38f;

        // A ribbon thinner than a pixel does not anti-alias -- it drops out and shimmers frame to
        // frame. The software rasteriser used for the comparison renders floored sub-pixel
        // coverage, which hardware will not do, so distant bolts must be widened to hold a
        // minimum on-screen width or they flicker.
        public const float MinimumScreenWidthPixels = 1.5f;

        // Builds the main channel as a bottom-anchored random walk. points[0] is exactly the
        // origin so the bolt's foot lands on the scorched block, and y increases strictly so the
        // ribbon extrusion can never fold back on itself.
        public static void BuildPolyline(System.Random random, float height, int segments, Vector3[] points)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (points == null || points.Length < segments + 1)
                throw new ArgumentException("Buffer too small for the requested segment count.", nameof(points));
            if (segments < 1)
                throw new ArgumentOutOfRangeException(nameof(segments));

            float maxWander = height * LateralWanderFraction;
            float step = height / segments;

            // A constant lean, so the bolt is not a vertical column with noise on it. Real strikes
            // come down at an angle.
            float leanPerSegment = (float)(random.NextDouble() * 2.0 - 1.0) * maxWander * 0.35f;

            points[0] = Vector3.zero;
            float x = 0.0f;

            for (int i = 1; i <= segments; i++)
            {
                // Random walk, not an oscillation: an oscillation reads as a bent wire because the
                // direction changes are regular.
                x += leanPerSegment + (float)(random.NextDouble() * 2.0 - 1.0) * maxWander;
                x = Mathf.Clamp(x, -maxWander * segments, maxWander * segments);

                points[i] = new Vector3(x, step * i, 0.0f);
            }
        }

        // Builds a fork: a short branch leaving the main channel at `startIndex` and dying out
        // before it reaches the ground. Returns the number of points written.
        public static int BuildFork(
            System.Random random,
            Vector3[] mainPoints,
            int mainCount,
            int startIndex,
            float height,
            Vector3[] points)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (mainPoints == null || points == null)
                throw new ArgumentNullException(nameof(mainPoints));
            if (startIndex < 0 || startIndex >= mainCount)
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            // Forks are short -- a third of the bolt at most -- and always travel downward, so a
            // fork can never end up above the cloud or below the strike point.
            int forkSegments = Mathf.Min(points.Length - 1, 2 + random.Next(3));
            float maxWander = height * LateralWanderFraction;
            float step = mainPoints[startIndex].y / (forkSegments + 1);
            float direction = random.Next(2) == 0 ? -1.0f : 1.0f;

            // Starts ON the main channel: a fork that begins in mid-air reads as a second bolt.
            points[0] = mainPoints[startIndex];

            for (int i = 1; i <= forkSegments; i++)
            {
                points[i] = new Vector3(
                    points[i - 1].x + direction * (float)random.NextDouble() * maxWander,
                    points[i - 1].y - step,
                    0.0f);
            }

            return forkSegments + 1;
        }

        // Extrudes a polyline into an indexed triangle strip. UV u runs 0..1 ACROSS the width so
        // the alpha ramp softens the edges, and v runs 0..1 along the length.
        public static void BuildRibbon(
            Vector3[] points,
            int count,
            float halfWidth,
            Vector3[] vertices,
            Vector2[] uvs,
            int[] indices) =>
            BuildRibbon(points, count, halfWidth, vertices, uvs, indices, vertexOffset: 0, indexOffset: 0);

        // Offset overload, so the main channel, every fork and the impact glow all land in one
        // mesh -- and therefore one draw call. Three separate meshes would triple the cost of the
        // one effect the player is meant to stop and look at.
        public static void BuildRibbon(
            Vector3[] points,
            int count,
            float halfWidth,
            Vector3[] vertices,
            Vector2[] uvs,
            int[] indices,
            int vertexOffset,
            int indexOffset)
        {
            if (points == null || vertices == null || uvs == null || indices == null)
                throw new ArgumentNullException(nameof(points));
            if (count < 2)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (vertices.Length < vertexOffset + count * 2 || uvs.Length < vertexOffset + count * 2)
                throw new ArgumentException("Vertex buffers too small.", nameof(vertices));
            if (indices.Length < indexOffset + (count - 1) * 6)
                throw new ArgumentException("Index buffer too small.", nameof(indices));

            for (int i = 0; i < count; i++)
            {
                float v = i / (float)(count - 1);
                int vertex = vertexOffset + i * 2;

                vertices[vertex] = new Vector3(points[i].x - halfWidth, points[i].y, points[i].z);
                vertices[vertex + 1] = new Vector3(points[i].x + halfWidth, points[i].y, points[i].z);
                uvs[vertex] = new Vector2(0.0f, v);
                uvs[vertex + 1] = new Vector2(1.0f, v);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int quad = indexOffset + i * 6;
                int baseVertex = vertexOffset + i * 2;

                indices[quad] = baseVertex;
                indices[quad + 1] = baseVertex + 2;
                indices[quad + 2] = baseVertex + 1;
                indices[quad + 3] = baseVertex + 1;
                indices[quad + 4] = baseVertex + 2;
                indices[quad + 5] = baseVertex + 3;
            }
        }

        // Widens the ribbon with distance so it never falls under MinimumScreenWidthPixels on
        // screen. Without this, distant bolts -- which the ring deliberately produces -- shimmer
        // and drop out, a bug that would otherwise only show up on device.
        public static float ResolveHalfWidth(float distance, float verticalFovDegrees, float screenHeightPixels)
        {
            if (distance <= 0.0f || verticalFovDegrees <= 0.0f || screenHeightPixels <= 0.0f)
                return HalfWidthBlocks;

            // Metres per pixel at this distance, from the vertical FOV.
            float worldHeightAtDistance = 2.0f * distance * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float metresPerPixel = worldHeightAtDistance / screenHeightPixels;
            float minimumHalfWidth = MinimumScreenWidthPixels * metresPerPixel * 0.5f;

            return Mathf.Max(HalfWidthBlocks, minimumHalfWidth);
        }

        // Runtime-generated cross-width alpha ramp: opaque white-hot core falling to a blue edge
        // and then to nothing. Built in code rather than shipped as a PNG, so there is no art
        // asset, no .meta, no atlas entry and no prefab change -- and it collapses what would have
        // been a core pass plus a glow pass into one draw.
        public static Color[] BuildGradientPixels(int width)
        {
            if (width < 2)
                throw new ArgumentOutOfRangeException(nameof(width));

            var core = new Color(1.0f, 1.0f, 0.98f);
            var edge = new Color(0.48f, 0.67f, 1.0f);
            var pixels = new Color[width];

            for (int i = 0; i < width; i++)
            {
                // Distance from the ribbon's centre, 0 at the core and 1 at either edge.
                float t = Mathf.Abs(i / (float)(width - 1) * 2.0f - 1.0f);

                // Matches the accepted render: a tight gaussian core plus a wide soft shoulder.
                float coreAmount = Mathf.Exp(-(t / 0.26f) * (t / 0.26f));
                float glow = (1.0f - t) * (1.0f - t) * 0.42f;

                Color rgb = Color.Lerp(edge, core, coreAmount);
                pixels[i] = new Color(rgb.r, rgb.g, rgb.b, Mathf.Clamp01(coreAmount + glow));
            }

            return pixels;
        }
    }
}
