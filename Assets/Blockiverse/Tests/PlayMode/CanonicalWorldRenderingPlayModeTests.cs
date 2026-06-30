using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class CanonicalWorldRenderingPlayModeTests
    {
        [UnityTest]
        public IEnumerator CanonicalPresetsRenderWithAuthoredAtlasMaterial()
        {
            foreach (GeneratedCreativeWorld generated in CreateCanonicalPresetFixtures())
            {
                GameObject root = new($"Renderer {generated.GenerationPreset}");
                Texture2D atlasTexture = null;
                Material sourceMaterial = null;

                try
                {
                    sourceMaterial = CreateAuthoredAtlasMaterial(out atlasTexture);
                    VoxelWorldRenderer renderer = root.AddComponent<VoxelWorldRenderer>();

                    renderer.Configure(generated.World, generated.Registry, sourceMaterial, layer: -1);

                    yield return null;

                    MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
                    MeshRenderer[] meshRenderers = root.GetComponentsInChildren<MeshRenderer>();

                    Assert.That(meshFilters, Is.Not.Empty, $"{generated.GenerationPreset} should create render chunks.");
                    Assert.That(meshRenderers, Is.Not.Empty, $"{generated.GenerationPreset} should create chunk renderers.");
                    Assert.That(renderer.Stats.ChunkCount, Is.GreaterThan(0), generated.GenerationPreset.ToString());
                    Assert.That(renderer.Stats.TriangleCount, Is.GreaterThan(0), generated.GenerationPreset.ToString());

                    foreach (MeshFilter filter in meshFilters)
                    {
                        Assert.That(filter.sharedMesh, Is.Not.Null, filter.name);
                        Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), filter.name);
                    }

                    foreach (MeshRenderer meshRenderer in meshRenderers)
                    {
                        Assert.That(meshRenderer.sharedMaterial, Is.Not.Null, meshRenderer.name);
                        Assert.That(meshRenderer.sharedMaterial.shader, Is.Not.Null, meshRenderer.name);
                        
                        // Check for fallback error shaders (magenta fallback)
                        string shaderName = meshRenderer.sharedMaterial.shader.name;
                        Assert.That(shaderName, Is.Not.EqualTo("Hidden/InternalErrorShader"), $"Material on {meshRenderer.name} in {generated.GenerationPreset} must not use URP's internal error shader.");
                        Assert.That(shaderName, Is.Not.EqualTo("Hidden/FallbackErrorShader"), $"Material on {meshRenderer.name} in {generated.GenerationPreset} must not use URP's fallback error shader.");
                        Assert.That(shaderName, Does.Not.Contain("Error"), $"Shader on {meshRenderer.name} in {generated.GenerationPreset} has 'Error' in name: {shaderName}");

                        // Assert no solid magenta fallback color exists
                        if (meshRenderer.sharedMaterial.HasProperty("_Color"))
                        {
                            Assert.That(meshRenderer.sharedMaterial.color, Is.Not.EqualTo(Color.magenta), $"Material color on {meshRenderer.name} in {generated.GenerationPreset} should not be magenta.");
                        }
                        if (meshRenderer.sharedMaterial.HasProperty("_BaseColor"))
                        {
                            Assert.That(meshRenderer.sharedMaterial.GetColor("_BaseColor"), Is.Not.EqualTo(Color.magenta), $"Material base color on {meshRenderer.name} in {generated.GenerationPreset} should not be magenta.");
                        }

                        Assert.That(
                            BlockVisualAtlas.TryGetBaseTexture(meshRenderer.sharedMaterial, out Texture texture),
                            Is.True,
                            meshRenderer.name);
                        Assert.That(texture, Is.SameAs(atlasTexture), meshRenderer.name);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(sourceMaterial);
                    Object.DestroyImmediate(atlasTexture);
                }
            }
        }

        static GeneratedCreativeWorld[] CreateCanonicalPresetFixtures()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            var survivalSettings = new WorldGenerationSettings(
                width: 32,
                height: WorldConstants.WorldMaxY + 1,
                depth: 32,
                chunkSize: WorldConstants.ChunkSize,
                seed: 6401,
                groundHeight: WorldConstants.SeaLevel);
            var flatSettings = new WorldGenerationSettings(
                width: 32,
                height: 16,
                depth: 32,
                chunkSize: WorldConstants.ChunkSize,
                seed: 1001,
                groundHeight: 2);
            var voidSettings = new WorldGenerationSettings(
                width: 32,
                height: 16,
                depth: 32,
                chunkSize: WorldConstants.ChunkSize,
                seed: 2201,
                groundHeight: 2);

            return new[]
            {
                new GeneratedCreativeWorld(
                    registry,
                    survivalSettings,
                    new SurvivalTerrainPreset(registry, survivalSettings).Generate(),
                    CreativeWorldGenerationPreset.SurvivalLite),
                new GeneratedCreativeWorld(
                    registry,
                    flatSettings,
                    new FlatBuilderPreset(registry, flatSettings).Generate(),
                    CreativeWorldGenerationPreset.FlatCreative),
                new GeneratedCreativeWorld(
                    registry,
                    voidSettings,
                    new VoidBuilderPreset(registry, voidSettings).Generate(),
                    CreativeWorldGenerationPreset.VoidBuilder)
            };
        }

        static Material CreateAuthoredAtlasMaterial(out Texture2D atlasTexture)
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
