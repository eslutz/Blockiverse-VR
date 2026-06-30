using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Persistence;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class VoxelWorldRenderingPlayModeTests
    {
        [UnityTest]
        public IEnumerator RenderingPresetsHaveValidSurfaces()
        {
            string[] presets = { WorldPresetIds.SurvivalTerrain, WorldPresetIds.FlatBuilder, WorldPresetIds.VoidBuilder };
            
            foreach (string presetId in presets)
            {
                yield return TestPresetRendering(presetId);
            }
        }

        private IEnumerator TestPresetRendering(string presetId)
        {
            // 1. Create the world
            GeneratedCreativeWorld generated = WorldSaveGeneration.GenerateNewWorld(
                presetId,
                seed: 6401,
                width: 32,
                depth: 32,
                startingBiome: null);
            
            VoxelWorld world = generated.World;
            BlockRegistry registry = generated.Registry;

            // 2. Set up renderer
            GameObject worldObject = new GameObject("PlayMode Renderer - " + presetId);
            VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();
            
            Material sourceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            renderer.Configure(world, registry, sourceMaterial, layer: 0);

            // Rebuild all chunks immediately for the test
            renderer.RebuildAll();
            yield return null;

            // 3. Assertions
            MeshFilter[] meshFilters = worldObject.GetComponentsInChildren<MeshFilter>();
            Assert.That(meshFilters.Length, Is.GreaterThan(0), $"Preset {presetId} should generate at least one chunk mesh.");

            foreach (MeshFilter filter in meshFilters)
            {
                Assert.That(filter.sharedMesh, Is.Not.Null, $"Chunk mesh in {presetId} should not be null.");
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), $"Chunk mesh in {presetId} should have vertices.");
                
                MeshRenderer meshRenderer = filter.GetComponent<MeshRenderer>();
                Assert.That(meshRenderer.sharedMaterial, Is.Not.Null, $"Material in {presetId} should not be null.");
                
                Texture texture = null;
                BlockVisualAtlas.TryGetBaseTexture(meshRenderer.sharedMaterial, out texture);
                Assert.That(texture, Is.Not.Null, $"Atlas texture should be bound in {presetId}.");
                Assert.That(texture.name, Is.EqualTo(BlockVisualAtlas.AuthoredAtlasName));
                
                // Check for fallback surfaces (magenta/missing). 
                // URP uses "Hidden/InternalErrorShader" for missing materials.
                Assert.That(meshRenderer.sharedMaterial.shader.name, Is.Not.EqualTo("Hidden/InternalErrorShader"), 
                    $"Material on {filter.name} in {presetId} should not be the error shader.");
            }

            // Cleanup
            Object.DestroyImmediate(worldObject);
            Object.DestroyImmediate(sourceMaterial);
            yield return null;
        }
    }
}
