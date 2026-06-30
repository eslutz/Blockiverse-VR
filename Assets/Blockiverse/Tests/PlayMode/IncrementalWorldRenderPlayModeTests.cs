using System.Collections;
using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Blockiverse.Tests.PlayMode
{
    // R2: the deferred initial render must eager-bake only the spawn region (so the player lands on
    // solid, collidable ground and the loading screen can lift) and then fill the rest of the world
    // in incrementally across frames under the per-frame budgets — never in one blocking call.
    public sealed class IncrementalWorldRenderPlayModeTests
    {
        [UnityTest]
        public IEnumerator SpawnRegionBakesEagerlyThenWorldFillsInIncrementally()
        {
            BlockRegistry registry = BlockRegistry.CreateDefault();
            // 5x1x5 = 25 chunks with a full ground layer so every chunk carries geometry; the spawn
            // neighbourhood is a clear subset of the queued world.
            var world = new VoxelWorld(new WorldBounds(80, 16, 80), chunkSize: 16, seed: 5);
            for (int x = 0; x < world.Bounds.Width; x++)
            {
                for (int z = 0; z < world.Bounds.Depth; z++)
                    world.SetBlock(new BlockPosition(x, 0, z), BlockRegistry.Graystone, trackChange: false);
            }

            var worldObject = new GameObject("Chunk Renderer");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                blockMaterial = CreateAuthoredAtlasMaterial(out atlasTexture);
                VoxelWorldRenderer renderer = worldObject.AddComponent<VoxelWorldRenderer>();

                // Deferred: Configure only queues the world (no geometry, not ready).
                renderer.Configure(world, registry, blockMaterial, layer: -1, deferInitialRebuild: true);

                Assert.That(renderer.SpawnRegionReady, Is.False);
                Assert.That(renderer.PendingVisualRebuildCount, Is.EqualTo(25));

                // Eager spawn-region bake at the centre chunk (2,0,2): a 3x1x3 neighbourhood.
                var spawn = new BlockPosition(40, 1, 40);
                renderer.RebuildSpawnRegion(spawn);

                // World-ready gate is up: the spawn area is meshed and its collider baked, so the
                // player would land on solid ground.
                Assert.That(renderer.SpawnRegionReady, Is.True);

                MeshCollider spawnCollider = null;
                foreach (MeshCollider collider in worldObject.GetComponentsInChildren<MeshCollider>())
                {
                    if (collider.gameObject.name == "Chunk 2,0,2")
                        spawnCollider = collider;
                }

                Assert.That(spawnCollider, Is.Not.Null, "The spawn chunk must have a render object.");
                Assert.That(spawnCollider.sharedMesh, Is.Not.Null);
                Assert.That(spawnCollider.sharedMesh.vertexCount, Is.GreaterThan(0),
                    "The spawn chunk collider must be baked so the player lands on solid ground.");

                // The rest of the world is still queued — it was NOT built in one blocking call.
                Assert.That(renderer.PendingVisualRebuildCount, Is.GreaterThan(0),
                    "The non-spawn chunks must remain queued for incremental fill.");

                // Pump frames: the renderer's Update drains the visual queue and bakes colliders
                // under the per-frame budgets, so the world fills in across several frames.
                const int maxFrames = 300;
                int frames = 0;
                while ((renderer.PendingVisualRebuildCount > 0 || renderer.PendingColliderRebuildCount > 0) &&
                       frames < maxFrames)
                {
                    yield return null;
                    frames++;
                }

                Assert.That(renderer.PendingVisualRebuildCount, Is.Zero,
                    "All chunks should have meshed across frames.");
                Assert.That(renderer.PendingColliderRebuildCount, Is.Zero,
                    "All chunk colliders should have baked across frames.");
                Assert.That(frames, Is.GreaterThan(0),
                    "The world must fill in over multiple frames, not in a single blocking call.");
                Assert.That(worldObject.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(25),
                    "Every ground chunk should have a render object once the queue drains.");
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(blockMaterial);
                Object.DestroyImmediate(atlasTexture);
            }
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