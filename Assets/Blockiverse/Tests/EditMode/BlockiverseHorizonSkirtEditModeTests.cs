using System.Collections.Generic;
using System.IO;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blockiverse.Tests.EditMode
{
    // The sea-level plane that stops the world ending in a visible cliff.
    //
    // The world is 128 blocks square and that is the WHOLE world, not a streaming radius, so from
    // any elevation its outer columns stood against the skybox's flat below-horizon band: "if you
    // go high enough you can clearly see the edge of the game world" (Eric, 2026-08-25).
    public sealed class BlockiverseHorizonSkirtEditModeTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        static readonly WorldBounds DefaultWorld = new(128, WorldConstants.WorldMaxY + 1, 128);

        BlockiverseHorizonSkirt CreateSkirt(WorldBounds bounds)
        {
            var host = new GameObject("Horizon Skirt Under Test");
            spawned.Add(host);
            BlockiverseHorizonSkirt skirt = host.AddComponent<BlockiverseHorizonSkirt>();
            skirt.Configure(bounds, null, new Rect(0.0f, 0.0f, 0.01f, 0.01f));
            return skirt;
        }

        // Both world sizes the menu can produce (WorldSaveGeneration.SizeFor).
        static readonly WorldBounds MediumWorld = new(192, WorldConstants.WorldMaxY + 1, 192);

        [Test]
        [TestCase(128)]
        [TestCase(192)]
        public void TheWholePlaneStaysInsideTheCameraFarClip(int side)
        {
            // A triangle that crosses the far plane is CLIPPED, which draws a hard arc at exactly
            // the far distance that sweeps around as the player turns — strictly worse than the
            // world edge this replaces. The rig camera's far clip is 500 m
            // (BlockiverseProjectBootstrapper.XrRig.cs:74).
            //
            // The worst case is a player standing in one CORNER of the world looking at the
            // opposite corner of the PLANE — sqrt((W+m)^2 + (D+m)^2), not the world diagonal plus
            // the margin. The first version of this code assumed the latter, and this test caught
            // it: a 360 m margin put the far corner at 541 m.
            //
            // Parameterised over both sizes because a margin sized for Small overruns the clip on
            // Medium, and Medium is one menu selection away.
            const float CameraFarClipMeters = 500.0f;

            var bounds = new WorldBounds(side, WorldConstants.WorldMaxY + 1, side);
            float margin = BlockiverseHorizonSkirt.OuterMarginFor(bounds);

            float worstCase = Mathf.Sqrt(
                (side + margin) * (side + margin) +
                (side + margin) * (side + margin));

            Assert.That(worstCase, Is.LessThan(CameraFarClipMeters),
                $"A {side}-block world's far corner is {worstCase:F0} m away against a {CameraFarClipMeters:F0} m far clip.");

            // Negative half: a margin that collapsed to nothing would pass the line above and
            // cover none of the void.
            Assert.That(margin, Is.GreaterThan(100.0f),
                $"A {side}-block world got a {margin:F0} m skirt; that covers nothing.");
        }

        [Test]
        public void OnlyWorldsWithASeaLevelInsideThemGetASea()
        {
            // The plane sits at sea level because that is where the ocean's surface is. A builder
            // canvas is 64 blocks tall against a sea level of 64, so a plane there would hang
            // above the entire world.
            Assert.That(BlockiverseHorizonSkirt.SuitsWorld(DefaultWorld), Is.True);
            Assert.That(BlockiverseHorizonSkirt.SuitsWorld(MediumWorld), Is.True);

            var builderCanvas = new WorldBounds(128, 64, 128); // WorldSaveGeneration.BuilderWorldHeight
            Assert.That(BlockiverseHorizonSkirt.SuitsWorld(builderCanvas), Is.False,
                "A 64-tall canvas has no sea level inside it for a sea to continue.");
        }

        [Test]
        public void ABiggerWorldGetsASmallerSkirt()
        {
            // The constraint is a fixed far clip shared between them, so the two cannot both have
            // the maximum. Pinned so a future world size cannot silently take the constant path.
            Assert.That(BlockiverseHorizonSkirt.OuterMarginFor(MediumWorld),
                Is.LessThan(BlockiverseHorizonSkirt.OuterMarginFor(DefaultWorld)));
        }

        [Test]
        public void ThePlaneFacesUP()
        {
            // Winding, which is the one thing here that is invisible until you are standing on it:
            // a plane wound the other way is backface-culled, so the fix would look like it had
            // simply not been applied and the void would still be there.
            BlockiverseHorizonSkirt skirt = CreateSkirt(DefaultWorld);
            Mesh mesh = skirt.GetComponent<MeshFilter>().sharedMesh;

            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));

            Vector3[] normals = mesh.normals;

            // Without this the loop below is vacuous: an empty normals array has no downward
            // entries and the test passes whether or not the plane is wound correctly.
            Assert.That(normals.Length, Is.EqualTo(mesh.vertexCount),
                "The mesh carries no normals, so the check below proves nothing.");

            int downward = 0;

            foreach (Vector3 normal in normals)
            {
                if (normal.y < 0.5f)
                    downward++;
            }

            Assert.That(downward, Is.EqualTo(0),
                $"{downward} of {normals.Length} vertices face away from the sky; the plane is wound inside out.");
        }

        [Test]
        public void ThePlaneSurroundsTheWorldAndReachesTheOuterMargin()
        {
            BlockiverseHorizonSkirt skirt = CreateSkirt(DefaultWorld);
            Bounds bounds = skirt.GetComponent<MeshFilter>().sharedMesh.bounds;
            float margin = BlockiverseHorizonSkirt.OuterMarginFor(DefaultWorld);

            Assert.That(bounds.min.x, Is.EqualTo(-margin).Within(0.5f));
            Assert.That(bounds.max.x, Is.EqualTo(DefaultWorld.Width + margin).Within(0.5f));
            Assert.That(bounds.min.z, Is.EqualTo(-margin).Within(0.5f));
            Assert.That(bounds.max.z, Is.EqualTo(DefaultWorld.Depth + margin).Within(0.5f));

            // Flat, and at exactly the height of the ocean's own RENDERED surface — which is NOT
            // sea level. Water fills up to but not including SeaLevel (so its top face is the plane
            // y = SeaLevel), and the wave shader then levels every fluid's mean surface to
            // -MaxWaveDipMeters * 0.5. Asserting the composed value matters because taking only the
            // first half leaves this plane 2.5 cm proud of the sea it continues — the same defect
            // the wave shader's comment records being caught on device between brine and freshwater.
            float restingSurface = WorldConstants.SeaLevel - VoxelWorldRenderer.MaxWaveDipMeters * 0.5f;

            Assert.That(bounds.min.y, Is.EqualTo(restingSurface).Within(0.001f));
            Assert.That(bounds.max.y, Is.EqualTo(restingSurface).Within(0.001f));
            Assert.That(restingSurface, Is.Not.EqualTo((float)WorldConstants.SeaLevel),
                "If the wave levelling is ever removed, this test stops distinguishing the two.");
        }

        [Test]
        public void TheInnermostRingSitsExactlyOnTheWorldEdge()
        {
            // A gap here is a slot of visible void all the way round the world, which is the
            // failure the plane exists to prevent, only thinner.
            for (int i = 0; i < BlockiverseHorizonSkirt.SamplesPerSide * 4; i++)
            {
                Vector2 point = BlockiverseHorizonSkirt.PerimeterPoint(DefaultWorld, 0.0f, i);

                bool onEdge =
                    Mathf.Approximately(point.x, 0.0f) ||
                    Mathf.Approximately(point.y, 0.0f) ||
                    Mathf.Approximately(point.x, DefaultWorld.Width) ||
                    Mathf.Approximately(point.y, DefaultWorld.Depth);

                Assert.That(onEdge, Is.True, $"Ring 0 point {i} at {point} is not on the world's boundary.");
                Assert.That(point.x, Is.InRange(0.0f, (float)DefaultWorld.Width));
                Assert.That(point.y, Is.InRange(0.0f, (float)DefaultWorld.Depth));
            }
        }

        [Test]
        public void EveryRingSharesOneParameterisationSoRingsCanBeStitchedIndexToIndex()
        {
            // The mesh joins ring i point k to ring i+1 point k. If the two rings disagreed about
            // what k means the surface would shear — visible as a twisted fan rather than as a
            // flat plane, and only from inside the headset.
            int perimeter = BlockiverseHorizonSkirt.SamplesPerSide * 4;

            for (int i = 0; i < perimeter; i++)
            {
                Vector2 near = BlockiverseHorizonSkirt.PerimeterPoint(DefaultWorld, 0.0f, i);
                Vector2 far = BlockiverseHorizonSkirt.PerimeterPoint(DefaultWorld, 100.0f, i);
                Vector2 centre = new(DefaultWorld.Width * 0.5f, DefaultWorld.Depth * 0.5f);

                // Same side of the world, further out.
                Assert.That((far - centre).magnitude, Is.GreaterThan((near - centre).magnitude),
                    $"Ring point {i} did not move outward when the ring expanded.");
                Assert.That(Vector2.Dot((near - centre).normalized, (far - centre).normalized),
                    Is.GreaterThan(0.5f),
                    $"Ring point {i} jumped to a different part of the perimeter as the ring expanded.");
            }

            // And the loop closes.
            Assert.That(BlockiverseHorizonSkirt.PerimeterPoint(DefaultWorld, 40.0f, perimeter),
                Is.EqualTo(BlockiverseHorizonSkirt.PerimeterPoint(DefaultWorld, 40.0f, 0)));
        }

        [Test]
        public void TheRimTakesTheAerialColourAndTheNearEdgeDoesNot()
        {
            // The rim hides itself by COLOUR, not by distance — it crossfades to whatever the sky
            // is at the horizon, which is the same value driving fog and the skybox's
            // below-horizon band. If the fade never reaches the aerial colour the far edge is a
            // line; if it starts there the plane is invisible and the void is back.
            BlockiverseHorizonSkirt skirt = CreateSkirt(DefaultWorld);
            var aerial = new Color(0.66f, 0.78f, 0.92f, 1.0f);
            skirt.SetSky(aerial);

            Color[] colors = skirt.GetComponent<MeshFilter>().sharedMesh.colors;
            Vector3[] vertices = skirt.GetComponent<MeshFilter>().sharedMesh.vertices;

            Assert.That(colors.Length, Is.EqualTo(vertices.Length));

            float nearestDelta = float.MaxValue;
            float furthestDelta = float.MaxValue;
            float nearestRadius = float.MaxValue;
            float furthestRadius = 0.0f;
            Vector3 centre = new(DefaultWorld.Width * 0.5f, 0.0f, DefaultWorld.Depth * 0.5f);

            for (int i = 0; i < vertices.Length; i++)
            {
                float radius = Vector3.Distance(new Vector3(vertices[i].x, 0.0f, vertices[i].z), centre);
                float delta = Mathf.Abs(colors[i].r - aerial.r)
                            + Mathf.Abs(colors[i].g - aerial.g)
                            + Mathf.Abs(colors[i].b - aerial.b);

                if (radius < nearestRadius)
                {
                    nearestRadius = radius;
                    nearestDelta = delta;
                }

                if (radius > furthestRadius)
                {
                    furthestRadius = radius;
                    furthestDelta = delta;
                }
            }

            Assert.That(furthestDelta, Is.LessThan(0.01f),
                $"The far rim is {furthestDelta:0.000} away from the aerial colour; it will draw as an edge.");
            Assert.That(nearestDelta, Is.GreaterThan(0.1f),
                "The edge nearest the world must read as surface, not as sky, or nothing is being covered.");

            // And the fade is roughly linear in DISTANCE, not in ring index. Rings are packed
            // toward the world, so an index-driven fade spends most of its range in the first few
            // metres and the sea never reads as sea — the plane goes sky-coloured about 50 m off
            // the shore. Sampled at the halfway ring, which sits at a quarter of the margin.
            float halfway = FadeAtRadius(skirt, DefaultWorld, aerial, 0.25f);
            Assert.That(halfway, Is.LessThan(0.45f),
                $"A quarter of the way out the plane is already {halfway:0.00} of the way to sky.");
        }

        // How far toward the aerial colour the plane has gone at `fractionOfMargin` past the
        // world's edge, measured from the vertex colours the mesh actually carries.
        static float FadeAtRadius(
            BlockiverseHorizonSkirt skirt, WorldBounds bounds, Color aerial, float fractionOfMargin)
        {
            Mesh mesh = skirt.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Color[] colors = mesh.colors;

            float margin = BlockiverseHorizonSkirt.OuterMarginFor(bounds);
            float wanted = margin * fractionOfMargin;
            Color shade = BlockiverseHorizonSkirt.SeaShade;
            Color near = new(aerial.r * shade.r, aerial.g * shade.g, aerial.b * shade.b, 1.0f);

            float bestDistance = float.MaxValue;
            float bestFade = 0.0f;

            for (int i = 0; i < vertices.Length; i++)
            {
                // Distance past the world's edge, measured on the +X face where z is inside the
                // world so the corner geometry does not confuse the reading.
                if (vertices[i].z < 0.0f || vertices[i].z > bounds.Depth)
                    continue;

                float past = vertices[i].x - bounds.Width;
                if (past < 0.0f)
                    continue;

                float distance = Mathf.Abs(past - wanted);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                float span = aerial.g - near.g;
                bestFade = Mathf.Approximately(span, 0.0f) ? 0.0f : (colors[i].g - near.g) / span;
            }

            return bestFade;
        }

        const string VoxelShaderPath = "Assets/Blockiverse/Shaders/BlockiverseVoxelLit.shader";

        [Test]
        public void TheSkyVariantShortCircuitsTheLitPathBeforeItReadsBakedLightData()
        {
            // The whole reason there is a variant at all. In the lit path vertex colour is
            // (sky exposure, emitter reach, self emission) and self emission is a SCALAR added to
            // all three channels — so a pale blue vertex colour renders as a slightly dimmer white
            // and can never match a pale blue sky. The rim fade would look like it had simply not
            // been applied.
            //
            // Asserted on the source's ORDER rather than on a rendered pixel because an EditMode
            // test has no frame: what is checkable here is that the sky branch returns before the
            // baked-light interpretation begins.
            string shader = File.ReadAllText(VoxelShaderPath);

            int skyBranch = shader.IndexOf("#if defined(_BLOCKIVERSE_SKY)", System.StringComparison.Ordinal);
            int bakedLight = shader.IndexOf("half bakedSky = max(input.color.r", System.StringComparison.Ordinal);

            Assert.That(skyBranch, Is.GreaterThan(-1), "The sky variant is not in the forward pass.");
            Assert.That(bakedLight, Is.GreaterThan(-1), "The baked-light read moved; this test no longer measures anything.");
            Assert.That(skyBranch, Is.LessThan(bakedLight),
                "The sky branch must return before vertex colour is reinterpreted as light data.");

            // multi_compile, not shader_feature: no material ASSET references this shader, so a
            // shader_feature variant is stripped from the Android player — the deck and the skirt
            // would fall back to the lit path on device and look correct in the editor.
            Assert.That(shader, Does.Contain("#pragma multi_compile_local _ _BLOCKIVERSE_WATER _BLOCKIVERSE_CUTOUT _BLOCKIVERSE_SKY"),
                "All four states must share ONE multi_compile line, or the variant count doubles.");
            Assert.That(shader, Does.Not.Contain("shader_feature_local _ _BLOCKIVERSE_SKY"));
        }

        [Test]
        public void TheSkyVariantNeverSamplesTheAtlas()
        {
            // Not an optimisation — a correctness pin. The deck used to multiply itself by one
            // hand-picked "white" texel of the block atlas, and that texel is white in three of the
            // four generated texture sets and (194, 204, 209) in `original`. Selecting that set
            // therefore tinted the whole sky 20% dark and blue, and for the skirt it would be worse
            // than cosmetic: its rim has to land on EXACTLY the aerial colour to disappear, so a
            // rim multiplied by 0.76 is a visible line all the way round the world.
            //
            // The sky branch must therefore return before the atlas is touched at all, which also
            // means no texture fetch on the largest fill in the frame.
            string shader = File.ReadAllText(VoxelShaderPath);

            int skyBranch = shader.IndexOf("#if defined(_BLOCKIVERSE_SKY)", System.StringComparison.Ordinal);
            int endOfBranch = shader.IndexOf("#endif", skyBranch, System.StringComparison.Ordinal);
            int firstSample = shader.IndexOf("SAMPLE_TEXTURE2D(_BaseMap", System.StringComparison.Ordinal);

            Assert.That(skyBranch, Is.GreaterThan(-1));
            Assert.That(endOfBranch, Is.GreaterThan(skyBranch));
            Assert.That(firstSample, Is.GreaterThan(-1), "The forward pass no longer samples _BaseMap; this test is measuring nothing.");

            Assert.That(skyBranch, Is.LessThan(firstSample),
                "The sky branch must return BEFORE the atlas sample, or every sky pixel pays a " +
                "texture fetch and inherits whatever colour that texel happens to be in the " +
                "selected texture set.");

            string branch = shader.Substring(skyBranch, endOfBranch - skyBranch);
            Assert.That(branch, Does.Not.Contain("SAMPLE_TEXTURE2D"),
                "The sky branch itself must not sample the atlas.");
            Assert.That(branch, Does.Contain("MixFog"),
                "Without fog the rim cannot reach the aerial colour from the fogged side.");
        }

        [Test]
        public void TheSkyVariantConvertsItsVertexColourFromSRGB()
        {
            // The project renders in LINEAR colour space (ProjectSettings m_ActiveColorSpace: 1),
            // and the two routes a colour takes into a shader disagree there: Material.SetColor
            // converts sRGB to linear, mesh.colors does not convert at all. Without the conversion
            // the skirt's rim renders about 80% too bright at midday and over 12x too bright at
            // night — a lit grey sea under a black sky, and a bright ring exactly where the plane
            // is supposed to vanish.
            //
            // It belongs in the SHADER, not in C#: mesh colours are an 8-bit stream, and 8 bits of
            // linear cannot carry a night sky (0.0057 linear rounds to 1/255, a 30% error) while
            // 8 bits of sRGB is precisely what sRGB encoding is for. So this also guards against
            // "fixing" it on the C# side and double-converting.
            string shader = File.ReadAllText(VoxelShaderPath);

            int skyBranch = shader.IndexOf("#if defined(_BLOCKIVERSE_SKY)", System.StringComparison.Ordinal);
            int endOfBranch = shader.IndexOf("#endif", skyBranch, System.StringComparison.Ordinal);
            Assert.That(skyBranch, Is.GreaterThan(-1));

            // The conversion sits INSIDE the branch, so it cannot be reached by any other variant.
            int branchEnd = shader.IndexOf("return half4(MixFog", skyBranch, System.StringComparison.Ordinal);
            Assert.That(branchEnd, Is.GreaterThan(skyBranch));
            string branch = shader.Substring(skyBranch, branchEnd - skyBranch);

            Assert.That(branch, Does.Contain("SRGBToLinear"),
                "Sky vertex colours are authored in sRGB and must be converted before they are output.");
            Assert.That(branch, Does.Contain("UNITY_COLORSPACE_GAMMA"),
                "The conversion must be skipped when the project is in gamma space, or it double-converts.");

            // And nothing on the C# side may convert as well.
            string deck = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/BlockiverseCloudDeck.cs");
            string skirt = File.ReadAllText("Assets/Blockiverse/Scripts/Gameplay/BlockiverseHorizonSkirt.cs");
            Assert.That(deck, Does.Not.Contain(".linear"),
                "The deck must hand the shader sRGB; converting on both sides darkens the sky twice.");
            Assert.That(skirt, Does.Not.Contain(".linear"),
                "The skirt must hand the shader sRGB; converting on both sides darkens the sky twice.");
        }

        [Test]
        public void TheSkyMaterialIsOpaqueAndCarriesOnlyItsOwnKeyword()
        {
            Texture2D atlas = null;
            Material source = null;
            Material sky = null;

            try
            {
                atlas = new Texture2D(
                    BlockVisualAtlas.AtlasWidthPixels,
                    BlockVisualAtlas.AtlasHeightPixels,
                    TextureFormat.RGBA32,
                    mipChain: false)
                {
                    name = BlockVisualAtlas.AuthoredAtlasName
                };

                source = new Material(Shader.Find("Sprites/Default")) { mainTexture = atlas };
                sky = BlockVisualAtlas.CreateSkyMaterial(source, atlas, BlockTextureSetIds.Default);

                Assert.That(sky.IsKeywordEnabled(BlockVisualAtlas.SkyShaderKeyword), Is.True,
                    "Without the keyword the deck and the skirt render through the LIT path, where their " +
                    "vertex colour is read as baked light data and no colour they ask for arrives.");
                Assert.That(sky.IsKeywordEnabled(BlockVisualAtlas.WaterShaderKeyword), Is.False);
                Assert.That(sky.IsKeywordEnabled(BlockVisualAtlas.CutoutShaderKeyword), Is.False,
                    "The three states are mutually exclusive on one multi_compile line; two at once is undefined.");

                Assert.That(sky.renderQueue, Is.EqualTo(BlockVisualAtlas.SkyRenderQueue),
                    "Sky geometry draws after terrain so terrain depth can reject the large, " +
                    "frequently occluded skirt before it is shaded.");
                Assert.That(BlockVisualAtlas.SkyRenderQueue,
                    Is.InRange((int)RenderQueue.Geometry + 1, BlockVisualAtlas.CutoutRenderQueue - 1),
                    "It must stay between terrain and the cutout foliage in the documented order.");
                Assert.That(sky.GetFloat("_ZWrite"), Is.EqualTo(1.0f).Within(0.001f));
                Assert.That(sky.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One).Within(0.001f));
                Assert.That(sky.GetShaderPassEnabled(BlockVisualAtlas.WaterDepthPrimePassName), Is.False);

                Assert.That(BlockVisualAtlas.TryGetBaseTexture(sky, out Texture texture), Is.True);
                Assert.That(texture, Is.SameAs(atlas),
                    "It samples the same authored atlas — one white texel of it — so no new asset ships.");
            }
            finally
            {
                if (sky != null)
                    Object.DestroyImmediate(sky);
                if (source != null)
                    Object.DestroyImmediate(source);
                if (atlas != null)
                    Object.DestroyImmediate(atlas);
            }
        }
    }
}
