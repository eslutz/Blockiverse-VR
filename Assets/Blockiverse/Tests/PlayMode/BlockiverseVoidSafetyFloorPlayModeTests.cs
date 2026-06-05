using Blockiverse.Core;
using Blockiverse.Gameplay;
using Blockiverse.Voxel;
using NUnit.Framework;
using UnityEngine;

namespace Blockiverse.Tests.PlayMode
{
    public sealed class BlockiverseVoidSafetyFloorPlayModeTests
    {
        [Test]
        public void ConfigureFromWorldBoundsCreatesCatchFloorBelowVoxelWorld()
        {
            var floorObject = new GameObject("Void Safety Floor");

            try
            {
                var floor = floorObject.AddComponent<BlockiverseVoidSafetyFloor>();
                floor.Configure(
                    new WorldBounds(width: 16, height: 32, depth: 24),
                    fallAllowanceMeters: 8.0f,
                    thicknessMeters: 1.0f,
                    horizontalMarginMeters: 8.0f,
                    layerName: BlockiverseProject.InteractionLayerName);

                BoxCollider collider = floorObject.GetComponent<BoxCollider>();

                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(collider.size, Is.EqualTo(new Vector3(32.0f, 1.0f, 40.0f)));
                Assert.That(collider.center, Is.EqualTo(new Vector3(8.0f, -8.5f, 12.0f)));
                Assert.That(floor.TopY, Is.EqualTo(-8.0f).Within(0.001f));

                int expectedLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

                if (expectedLayer >= 0)
                    Assert.That(floorObject.layer, Is.EqualTo(expectedLayer));
            }
            finally
            {
                Object.DestroyImmediate(floorObject);
            }
        }
    }
}
