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
                Assert.That(floor.HasRecoverySpawnPosition, Is.False);

                int expectedLayer = LayerMask.NameToLayer(BlockiverseProject.InteractionLayerName);

                if (expectedLayer >= 0)
                    Assert.That(floorObject.layer, Is.EqualTo(expectedLayer));
            }
            finally
            {
                Object.DestroyImmediate(floorObject);
            }
        }

        [Test]
        public void RecoverRigIfBelowFloorMovesRigBackToConfiguredSpawn()
        {
            var floorObject = new GameObject("Void Safety Floor");
            var rigObject = new GameObject(BlockiverseProject.XrRigRootName);
            rigObject.AddComponent<BlockiversePlayerRigAnchor>();

            try
            {
                var floor = floorObject.AddComponent<BlockiverseVoidSafetyFloor>();
                var spawn = new BlockPosition(4, 6, 9);
                floor.Configure(
                    new WorldBounds(width: 16, height: 32, depth: 24),
                    fallAllowanceMeters: 8.0f,
                    thicknessMeters: 1.0f,
                    horizontalMarginMeters: 8.0f,
                    layerName: BlockiverseProject.InteractionLayerName,
                    recoverySpawnPosition: spawn);
                rigObject.transform.position = new Vector3(20.0f, floor.RecoveryContactY - 0.01f, 20.0f);

                bool recovered = floor.TryRecoverRigIfBelowFloor();

                Assert.That(recovered, Is.True);
                Assert.That(floor.HasRecoverySpawnPosition, Is.True);
                Assert.That(floor.RecoverySpawnPosition, Is.EqualTo(spawn));
                Assert.That(rigObject.transform.position, Is.EqualTo(new Vector3(4.5f, 6.0f, 9.5f)));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(floorObject);
            }
        }
    }
}
