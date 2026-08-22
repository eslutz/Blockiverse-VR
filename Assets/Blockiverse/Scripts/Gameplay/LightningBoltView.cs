using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Gameplay
{
    // Draws one lightning bolt: a procedural ribbon from the cloud deck down to the struck block,
    // plus its forks and a small glow at the impact point, all in a single mesh and a single
    // additive draw.
    //
    // Created at runtime rather than authored, following CreativeWorldManager's placement preview.
    // Nothing here touches a prefab, so the generated XR rig is untouched by the whole feature.
    //
    // One instance, restarted on each strike. Storms never produce two simultaneous visible bolts
    // -- the flash refuses to retrigger inside its own window for comfort reasons -- so a pool
    // would be machinery with nothing to hold. Buffers and the mesh are allocated once, so a
    // strike produces no steady-state garbage.
    [DisallowMultipleComponent]
    public sealed class LightningBoltView : MonoBehaviour
    {
        // Real strikes are gone before you can focus on them. Long enough to register, short
        // enough that the afterimage is the memory rather than the object.
        public const float LifetimeSeconds = 0.16f;

        // Where the bolt comes from, in blocks above the struck surface.
        public const float CloudHeightBlocks = 42.0f;

        // Impact glow, in blocks across. Localised on purpose: a full-height glow billboard would
        // be the single most expensive thing on a tile GPU, and it is the same trap that ruled out
        // the stretched-sprite bolt.
        public const float ImpactGlowRadiusBlocks = 1.6f;

        const int GradientWidth = 64;
        const int MaxPoints = 24;
        const int MaxVertices = 256;
        const int MaxIndices = 768;

        readonly Vector3[] mainPoints = new Vector3[MaxPoints];
        readonly Vector3[] forkPoints = new Vector3[MaxPoints];
        readonly Vector3[] vertices = new Vector3[MaxVertices];
        readonly Vector2[] uvs = new Vector2[MaxVertices];
        readonly int[] indices = new int[MaxIndices];

        Mesh mesh;
        MeshRenderer meshRenderer;
        Material material;
        Texture2D gradient;
        MaterialPropertyBlock propertyBlock;
        Transform head;
        float elapsed = float.MaxValue;
        int vertexCount;
        int indexCount;

        public bool IsStriking => elapsed < LifetimeSeconds;

        public MeshRenderer Renderer => meshRenderer;

        // The head to billboard toward. Set explicitly by the caller that creates the bolt, which
        // already knows where the player is; Camera.main is only the fallback. That matters
        // because Camera.main resolves to whichever tagged camera the engine finds first, and in a
        // scene with more than one -- a loaded Boot scene alongside a test rig, say -- the bolt
        // would silently face the wrong one.
        public void Configure(Transform headTransform)
        {
            if (headTransform != null)
                head = headTransform;
        }

        // Builds and shows a bolt whose foot sits exactly on `footPosition`. `seed` makes the shape
        // reproducible for a given strike; `distance` widens the ribbon so a far bolt still covers
        // enough pixels to survive rasterisation.
        public void Strike(Vector3 footPosition, int seed, float distance, bool reducedParticles)
        {
            EnsureResources();

            transform.position = footPosition;

            var random = new System.Random(seed);
            int segments = reducedParticles ? LightningBoltGeometry.ReducedSegments : LightningBoltGeometry.DefaultSegments;
            float halfWidth = LightningBoltGeometry.ResolveHalfWidth(distance, ResolveVerticalFov(), Screen.height);

            vertexCount = 0;
            indexCount = 0;

            LightningBoltGeometry.BuildPolyline(random, CloudHeightBlocks, segments, mainPoints);
            AppendRibbon(mainPoints, segments + 1, halfWidth);

            // Forks are most of what makes a ribbon read as lightning rather than as a line, and
            // they cost a handful of triangles.
            for (int i = 0; i < LightningBoltGeometry.ForkCount; i++)
            {
                int startIndex = 1 + random.Next(segments - 1);
                int count = LightningBoltGeometry.BuildFork(
                    random, mainPoints, segments + 1, startIndex, CloudHeightBlocks, forkPoints);

                AppendRibbon(forkPoints, count, halfWidth * 0.6f);
            }

            AppendImpactGlow(reducedParticles ? ImpactGlowRadiusBlocks * 0.5f : ImpactGlowRadiusBlocks);

            mesh.Clear();
            mesh.SetVertices(vertices, 0, vertexCount);
            mesh.SetUVs(0, uvs, 0, vertexCount);
            mesh.SetIndices(indices, 0, indexCount, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();

            elapsed = 0.0f;
            meshRenderer.enabled = true;
            ApplyFade();
        }

        void LateUpdate()
        {
            if (!IsStriking)
            {
                if (meshRenderer != null && meshRenderer.enabled)
                    meshRenderer.enabled = false;

                return;
            }

            elapsed += Time.deltaTime;
            FaceHead();
            ApplyFade();
        }

        // Yaw only. A full LookRotation would tilt the whole bolt when the player looks up, which
        // instantly reads as a flat card -- the exact failure that ruled out a sprite.
        void FaceHead()
        {
            if (head == null)
            {
                if (Camera.main == null)
                    return;

                head = Camera.main.transform;
            }

            Vector3 toHead = head.position - transform.position;
            toHead.y = 0.0f;

            if (toHead.sqrMagnitude < 1e-6f)
                return;

            transform.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up);
        }

        void ApplyFade()
        {
            if (meshRenderer == null)
                return;

            // Holds full brightness for most of its life and drops off at the end: a linear fade
            // from the first frame makes the bolt look like it is dissolving rather than flashing.
            float t = Mathf.Clamp01(elapsed / LifetimeSeconds);
            float alpha = t < 0.6f ? 1.0f : 1.0f - (t - 0.6f) / 0.4f;

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, new Color(1.0f, 1.0f, 1.0f, alpha));
            propertyBlock.SetColor(ColorId, new Color(1.0f, 1.0f, 1.0f, alpha));
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        void AppendRibbon(Vector3[] points, int count, float halfWidth)
        {
            if (count < 2 || vertexCount + count * 2 > MaxVertices || indexCount + (count - 1) * 6 > MaxIndices)
                return;

            LightningBoltGeometry.BuildRibbon(
                points, count, halfWidth, vertices, uvs, indices, vertexCount, indexCount);

            vertexCount += count * 2;
            indexCount += (count - 1) * 6;
        }

        // A small quad at the foot, in the same mesh and the same draw, so the strike visibly
        // happens TO the block rather than just ending near it.
        void AppendImpactGlow(float radius)
        {
            if (vertexCount + 4 > MaxVertices || indexCount + 6 > MaxIndices)
                return;

            int v = vertexCount;

            vertices[v] = new Vector3(-radius, 0.0f, 0.0f);
            vertices[v + 1] = new Vector3(radius, 0.0f, 0.0f);
            vertices[v + 2] = new Vector3(-radius, radius * 2.0f, 0.0f);
            vertices[v + 3] = new Vector3(radius, radius * 2.0f, 0.0f);

            // Both edges sample the ramp's soft outer end, so the quad reads as a bloom rather
            // than as a rectangle with a bright bar down the middle.
            uvs[v] = new Vector2(0.0f, 0.0f);
            uvs[v + 1] = new Vector2(1.0f, 0.0f);
            uvs[v + 2] = new Vector2(0.0f, 1.0f);
            uvs[v + 3] = new Vector2(1.0f, 1.0f);

            indices[indexCount] = v;
            indices[indexCount + 1] = v + 2;
            indices[indexCount + 2] = v + 1;
            indices[indexCount + 3] = v + 1;
            indices[indexCount + 4] = v + 2;
            indices[indexCount + 5] = v + 3;

            vertexCount += 4;
            indexCount += 6;
        }

        static float ResolveVerticalFov() => Camera.main != null ? Camera.main.fieldOfView : 90.0f;

        void EnsureResources()
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Lightning Bolt" };
                mesh.MarkDynamic();
            }

            if (gradient == null)
            {
                // Generated in code: no PNG, no .meta, no atlas entry, no art-pipeline validation
                // to update. Point filtering would be wrong here -- 64 texels map across roughly
                // ten screen pixels of ribbon width -- so this one is bilinear.
                gradient = new Texture2D(GradientWidth, 1, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "Lightning Gradient",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                gradient.SetPixels(LightningBoltGeometry.BuildGradientPixels(GradientWidth));
                gradient.Apply();
            }

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                material = new Material(shader) { name = "Lightning Bolt" };

                // Additive: lightning adds light to whatever is behind it, and additive blending
                // needs no sorting, which matters for a mesh whose own forks overlap.
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                SetFloatIfPresent(material, "_Surface", 1.0f);
                SetFloatIfPresent(material, "_Blend", 1.0f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.One);
                SetFloatIfPresent(material, "_ZWrite", 0.0f);
                SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

                if (material.HasProperty(BaseMapId))
                    material.SetTexture(BaseMapId, gradient);
                if (material.HasProperty(MainTexId))
                    material.SetTexture(MainTexId, gradient);
            }

            if (meshRenderer == null)
            {
                MeshFilter filter = gameObject.GetComponent<MeshFilter>();
                if (filter == null)
                    filter = gameObject.AddComponent<MeshFilter>();

                filter.sharedMesh = mesh;

                meshRenderer = gameObject.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                    meshRenderer = gameObject.AddComponent<MeshRenderer>();

                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.enabled = false;
            }
        }

        static void SetFloatIfPresent(Material target, string property, float value)
        {
            if (target.HasProperty(property))
                target.SetFloat(property, value);
        }

        void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
            if (gradient != null)
                Destroy(gradient);
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
