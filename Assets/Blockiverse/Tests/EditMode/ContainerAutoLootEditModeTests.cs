using Blockiverse.Gameplay;
using Blockiverse.Networking;
using Blockiverse.Survival;
using Blockiverse.Voxel;
using Blockiverse.WorldGen;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blockiverse.Tests.EditMode
{
    // Guards the gameplay rule "breaking a container puts its contents in the breaker's
    // inventory" against the UI stack it used to be attached to.
    //
    // Until the uGUI cutover the only caller of CreativeWorldManager.SetActivePlayerInventory
    // in the whole repository was SurvivalHudController.Bind — a menu component. Deleting the
    // uGUI menus would therefore have left activePlayerInventory permanently null, and the
    // auto-loot branch silently skipped: the crate still disappears, its contents just cease to
    // exist. Nothing in the suite would have gone red. This test pins the replacement contract
    // — resolve the local survival inventory on demand — so the rule cannot be re-orphaned by
    // the next UI change either.
    public sealed class ContainerAutoLootEditModeTests
    {
        static readonly BlockPosition ContainerPos = new(6, 5, 6);

        [Test]
        public void BreakingAContainerLootsIntoTheLocalInventoryWithNoExplicitRegistration()
        {
            var worldObject = new GameObject("Auto Loot Creative World");
            worldObject.SetActive(false);
            var syncObject = new GameObject("Auto Loot Survival Sync");
            Texture2D atlasTexture = null;
            Material blockMaterial = null;

            try
            {
                BlockRegistry registry = BlockRegistry.CreateDefault();
                var settings = new WorldGenerationSettings(
                    width: 16,
                    height: 32,
                    depth: 16,
                    chunkSize: 16,
                    seed: 909,
                    groundHeight: 4);
                VoxelWorld world = new FlatBuilderPreset(registry, settings).Generate();
                world.SetBlock(ContainerPos, BlockRegistry.StorageCrate);

                CreativeWorldManager manager = worldObject.AddComponent<CreativeWorldManager>();
                blockMaterial = CreateBlockAtlasMaterial(out atlasTexture);
                BlockiverseWorldPresentation.Attach(manager, blockMaterial, -1);
                manager.InitializeGeneratedWorld(new GeneratedCreativeWorld(
                    registry,
                    settings,
                    world,
                    CreativeWorldGenerationPreset.SurvivalLite,
                    new[]
                    {
                        new StructureContainerLoot(
                            ContainerPos,
                            new[] { new ContainerLootItem(ItemId.ReedFiber.Value, 5) })
                    }));

                Assert.That(manager.ContainerStore.Contains(ContainerPos), Is.True,
                    "Fixture failed: the crate carries no loot to lose.");

                MultiplayerSurvivalSync survivalSync = syncObject.AddComponent<MultiplayerSurvivalSync>();
                survivalSync.Configure(null, null, manager);

                // The point of the test: nothing calls SetActivePlayerInventory. Before the port
                // this read null and the loot was discarded.
                Assert.That(manager.ActivePlayerInventory, Is.Not.Null,
                    "With a survival sync present the manager must resolve the local inventory itself.");
                Assert.That(manager.ActivePlayerInventory, Is.SameAs(survivalSync.LocalInventory));
                Assert.That(survivalSync.LocalInventory.CountOf(ItemId.ReedFiber), Is.Zero,
                    "Negative control: the fibre must arrive from the crate, not from the fixture.");

                manager.World.SetBlock(ContainerPos, BlockRegistry.Air);

                Assert.That(survivalSync.LocalInventory.CountOf(ItemId.ReedFiber), Is.EqualTo(5),
                    "Breaking a container must loot it into the local player's inventory.");
                Assert.That(manager.ContainerStore.Contains(ContainerPos), Is.False,
                    "The emptied container must be removed from the store.");
            }
            finally
            {
                Object.DestroyImmediate(syncObject);
                Object.DestroyImmediate(worldObject);

                if (blockMaterial != null)
                    Object.DestroyImmediate(blockMaterial);
                if (atlasTexture != null)
                    Object.DestroyImmediate(atlasTexture);

                GameObject sunObject = GameObject.Find(BlockiverseLightingRuntime.SunObjectName);
                if (sunObject != null)
                    Object.DestroyImmediate(sunObject);
            }
        }

        static Material CreateBlockAtlasMaterial(out Texture2D atlasTexture)
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
